using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Atm.Tests;

// ═════════════════════════════ Les entrées du domaine ═════════════════════════════

/// <summary>Le module de billets d'un cas : un stock de 0 à 5 billets par cassette et une cassette bloquée au plus.</summary>
public sealed record Module(NoteCounts Stock, Denomination? Jammed)
{
    public MoneyDispenser Build() => new(Stock, Jammed);

    /// <summary>Le stock, sans la cassette bloquée, qui compte pour zéro.</summary>
    public NoteCounts Available => new(
        Jammed == Denomination.Fifty ? 0 : Stock.Fifties,
        Jammed == Denomination.Twenty ? 0 : Stock.Twenties,
        Jammed == Denomination.Ten ? 0 : Stock.Tens);

    public override string ToString() =>
        $"stock 50×{Stock.Fifties} 20×{Stock.Twenties} 10×{Stock.Tens}, bloquée {Jammed?.ToString() ?? "aucune"}";
}

/// <summary>Un retrait quelconque du domaine.</summary>
public sealed record Retrait(AccountState Account, Module Module, int Amount);

/// <summary>Un retrait du domaine qui passe les contrôles 1 à 3 : seul le stock décide de l'issue.</summary>
public sealed record RetraitPlausible(AccountState Account, Module Module, int Amount);

/// <summary>Un dépôt quelconque du domaine (0 ou 1 billet par coupure).</summary>
public sealed record Depot(AccountState Account, Module Module, NoteCounts Notes);

/// <summary>
/// Un retrait plausible avec un module qui tombe en panne. Le passage en panne est <see cref="Draw"/> ramené à 1..n+1, où n est
/// le nombre de passages du retrait sans panne : n+1 signifie que la panne arriverait après le dernier passage.
/// </summary>
public sealed record RetraitEnPanne(RetraitPlausible Retrait, int Draw);

/// <summary>Une suite d'opérations du domaine, jouées l'une après l'autre sur la même machine et le même module.</summary>
public sealed record Sequence(AccountState Account, Module Module, Operation[] Operations)
{
    public override string ToString() => $"{Account}, {Module}, [{string.Join("; ", Operations.Select(o => o.ToString()))}]";
}

/// <summary>Les générateurs, tous restreints au domaine de la spécification.</summary>
public static class Domaine
{
    public static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static Gen<int> Balance => Gen.Choose(0, 50).Select(i => i * 5);

    private static Gen<int> WithdrawnToday => Gen.Choose(0, 20).Select(i => i * 10);

    private static Gen<AccountState> Account =>
        from balance in Balance
        from withdrawn in WithdrawnToday
        select new AccountState(balance, withdrawn);

    private static Gen<int> Amount => Gen.Elements(Amounts);

    private static Gen<NoteCounts> Stock =>
        from fifties in Gen.Choose(0, 5)
        from twenties in Gen.Choose(0, 5)
        from tens in Gen.Choose(0, 5)
        select new NoteCounts(fifties, twenties, tens);

    private static Gen<Denomination?> Jammed =>
        Gen.Elements<Denomination?>(null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten);

    private static Gen<Module> Module =>
        from stock in Stock
        from jammed in Jammed
        select new Module(stock, jammed);

    private static Gen<NoteCounts> DepositNotes =>
        from fifties in Gen.Choose(0, 1)
        from twenties in Gen.Choose(0, 1)
        from tens in Gen.Choose(0, 1)
        select new NoteCounts(fifties, twenties, tens);

    /// <summary>Montant valide, puis déjà-retiré et solde choisis pour passer le plafond et les fonds.</summary>
    private static Gen<RetraitPlausible> PlausibleFor(int amount, Module module) =>
        from withdrawn in Gen.Choose(0, (200 - amount) / 10).Select(i => i * 10)
        from balance in Gen.Choose((amount + (amount > 100 ? 5 : 0)) / 5, 50).Select(i => i * 5)
        select new RetraitPlausible(new AccountState(balance, withdrawn), module, amount);

    private static Gen<RetraitPlausible> Ordinary =>
        from amount in Gen.Choose(1, 20).Select(i => i * 10)
        from module in Module
        from retrait in PlausibleFor(amount, module)
        select retrait;

    /// <summary>
    /// Gros retraits sans billet de 50 disponible : de 90 à 150 € en 20 et en 10, au plus ce que contiennent ces cassettes. Il y a
    /// toujours une combinaison, de 5 à 10 billets, donc 2 ou 3 passages ; une fois sur deux, le plus gros montant possible.
    /// </summary>
    private static Gen<RetraitPlausible> Large =>
        from jammed in Gen.Elements<Denomination?>(Denomination.Fifty, null)
        from fifties in Gen.Choose(0, 5)
        from twenties in Gen.Choose(4, 5)
        from tens in Gen.Choose(2, 5)
        let module = new Module(new NoteCounts(jammed is null ? 0 : fifties, twenties, tens), jammed)
        let most = Math.Min(15, module.Available.Value / 10)
        from amount in Gen.Frequency((1, Gen.Choose(9, most)), (1, Gen.Constant(most))).Select(i => i * 10)
        from retrait in PlausibleFor(amount, module)
        select retrait;

    private static Gen<RetraitPlausible> Plausible => Gen.Frequency((3, Ordinary), (1, Large));

    private static Gen<Operation> Operation => Gen.OneOf(
        Amount.Select(amount => (Atm.Operation)new Operation.Withdraw(amount)),
        DepositNotes.Select(notes => (Atm.Operation)new Operation.Deposit(notes)));

    public static Arbitrary<Retrait> Retraits() =>
        (from account in Account from module in Module from amount in Amount select new Retrait(account, module, amount))
        .ToArbitrary();

    public static Arbitrary<RetraitPlausible> RetraitsPlausibles() => Plausible.ToArbitrary();

    public static Arbitrary<Depot> Depots() =>
        (from account in Account from module in Module from notes in DepositNotes select new Depot(account, module, notes))
        .ToArbitrary();

    public static Arbitrary<RetraitEnPanne> RetraitsEnPanne() =>
        (from retrait in Gen.Frequency((1, Ordinary), (2, Large))
         from draw in Gen.Choose(1, 4)
         select new RetraitEnPanne(retrait, draw))
        .ToArbitrary();

    public static Arbitrary<Sequence> Sequences() =>
        (from account in Account
         from module in Module
         from length in Gen.Choose(1, 12)
         from operations in Operation.ArrayOf(length)
         select new Sequence(account, module, operations))
        .ToArbitrary();
}

/// <summary>Module espion : délègue au vrai module, enregistre chaque appel et peut simuler une panne.</summary>
internal sealed class ModuleEspion(IMoneyDispenser inner, int? failingPass = null, bool failLoad = false) : IMoneyDispenser
{
    public List<NoteCounts> Dispensed { get; } = [];

    public List<NoteCounts> Loaded { get; } = [];

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        Dispensed.Add(notes);

        return Dispensed.Count != failingPass && inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        Loaded.Add(notes);

        return !failLoad && inner.Load(notes);
    }
}

// ═════════════════════════════ Les propriétés ═════════════════════════════

/// <summary>
/// Tests de propriétés : des règles de la spécification, vraies pour toute entrée du domaine, vérifiées sur des cas tirés au
/// hasard. Graine et nombre de cas fixés sur chaque propriété pour que les tirages soient reproductibles.
/// </summary>
public class Proprietes
{
    // "42,0" fait planter FsCheck 3 (le second nombre, le gamma, doit être impair) : même graine 42, gamma valide le plus proche.
    private const string Graine = "42,1";
    private const int Cas = 200;

    private static readonly NoteCounts Nothing = NoteCounts.None;

    // ───────────────────────── Outils ─────────────────────────

    private static int FeeFor(int amount) => amount > 100 ? 5 : 0;

    private static bool IsValidAmount(int amount) => amount > 0 && amount % 10 == 0;

    private static (AtmResult Result, MoneyDispenser Dispenser) Run(AccountState account, Module module, Operation operation)
    {
        var dispenser = module.Build();
        var result = new CashMachine(dispenser).Execute(account, operation);

        return (result, dispenser);
    }

    private static (AtmResult Result, MoneyDispenser Dispenser) Withdraw(AccountState account, Module module, int amount) =>
        Run(account, module, new Operation.Withdraw(amount));

    private static (AtmResult Result, MoneyDispenser Dispenser) Deposit(AccountState account, Module module, NoteCounts notes) =>
        Run(account, module, new Operation.Deposit(notes));

    /// <summary>Toutes les combinaisons de billets disponibles qui font exactement <paramref name="amount"/>.</summary>
    private static IEnumerable<NoteCounts> Combinations(NoteCounts available, int amount)
    {
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var candidate = new NoteCounts(f, t, d);
            if (candidate.Value == amount)
            {
                yield return candidate;
            }
        }
    }

    private static bool FitsIn(NoteCounts notes, NoteCounts available) =>
        notes.Fifties <= available.Fifties && notes.Twenties <= available.Twenties && notes.Tens <= available.Tens;

    private static bool InDomain(NoteCounts stock) =>
        stock is { Fifties: >= 0 and <= 5, Twenties: >= 0 and <= 5, Tens: >= 0 and <= 5 };

    private static bool InDomain(AccountState account) =>
        account is { Balance: >= 0 and <= 250, WithdrawnToday: >= 0 and <= 200 }
        && account.Balance % 5 == 0 && account.WithdrawnToday % 10 == 0;

    private static NoteCounts Sum(IEnumerable<NoteCounts> notes) => notes.Aggregate(Nothing, (a, b) => a + b);

    private static void AssertRefused(AtmResult result, AccountState account, Module module, MoneyDispenser dispenser)
    {
        Assert.Equal(Nothing, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(module.Stock, dispenser.Stock);
    }

    // ───────────────────────── Retrait ─────────────────────────

    /// <summary>Le premier contrôle qui échoue, dans l'ordre de la spec, donne le statut ; sinon c'est <c>Ok</c>.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_LePremierControleEnEchecDonneLeStatut(Retrait r)
    {
        var (result, _) = Withdraw(r.Account, r.Module, r.Amount);

        var expected =
            !IsValidAmount(r.Amount) ? AtmStatus.InvalidAmount
            : r.Account.WithdrawnToday + r.Amount > 200 ? AtmStatus.DailyLimitExceeded
            : r.Amount + FeeFor(r.Amount) > r.Account.Balance ? AtmStatus.InsufficientFunds
            : !Combinations(r.Module.Available, r.Amount).Any() ? AtmStatus.CannotDispense
            : AtmStatus.Ok;

        Assert.Equal(expected, result.Status);
    }

    /// <summary>Même chose sur des retraits qui passent les contrôles 1 à 3 : seul le stock disponible décide.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Plausible_OkSiEtSeulementSiUneCombinaisonExiste(RetraitPlausible r)
    {
        var (result, _) = Withdraw(r.Account, r.Module, r.Amount);

        var exists = Combinations(r.Module.Available, r.Amount).Any();

        Assert.Equal(exists ? AtmStatus.Ok : AtmStatus.CannotDispense, result.Status);
    }

    /// <summary>
    /// Les frontières, au cent près : le plafond accepte <c>WithdrawnToday + amount = 200</c> et refuse 10 € de plus ; les fonds
    /// acceptent <c>Balance = amount + frais</c> (le solde tombe à 0) et refusent 5 € de moins.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_FrontieresDuPlafondEtDesFonds(RetraitPlausible r)
    {
        var fee = FeeFor(r.Amount);
        var (reference, _) = Withdraw(r.Account, r.Module, r.Amount);

        var (atLimit, _) = Withdraw(r.Account with { WithdrawnToday = 200 - r.Amount }, r.Module, r.Amount);
        var (overLimit, _) = Withdraw(r.Account with { WithdrawnToday = 210 - r.Amount }, r.Module, r.Amount);
        var (exactFunds, _) = Withdraw(r.Account with { Balance = r.Amount + fee }, r.Module, r.Amount);
        var (shortFunds, _) = Withdraw(r.Account with { Balance = r.Amount + fee - 5 }, r.Module, r.Amount);

        Assert.Equal(reference.Status, atLimit.Status);
        Assert.Equal(AtmStatus.DailyLimitExceeded, overLimit.Status);
        Assert.Equal(reference.Status, exactFunds.Status);
        Assert.Equal(AtmStatus.InsufficientFunds, shortFunds.Status);
        if (atLimit.Status == AtmStatus.Ok)
        {
            Assert.Equal(200, atLimit.Account.WithdrawnToday);
            Assert.Equal(0, exactFunds.Account.Balance);
        }
    }

    /// <summary>Avec le vrai module, un retrait ne rend jamais un statut de dépôt ni une panne.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_NeRendQueDesStatutsDeRetrait(Retrait r)
    {
        var (result, _) = Withdraw(r.Account, r.Module, r.Amount);

        Assert.Contains(result.Status, new[]
        {
            AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.DailyLimitExceeded, AtmStatus.InsufficientFunds,
            AtmStatus.CannotDispense,
        });
    }

    /// <summary>Sur un refus, rien ne change : pas de billets, pas de frais, compte et stock identiques.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Refuse_RienNeChange(Retrait r, RetraitPlausible p)
    {
        Check(r.Account, r.Module, r.Amount);
        Check(p.Account, p.Module, p.Amount);

        static void Check(AccountState account, Module module, int amount)
        {
            var (result, dispenser) = Withdraw(account, module, amount);
            if (result.Status != AtmStatus.Ok)
            {
                AssertRefused(result, account, module, dispenser);
            }
        }
    }

    /// <summary>Sur un succès : les billets font le montant, frais selon le seuil, compte et stock bougent d'autant.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Reussi_MouvementsCoherents(RetraitPlausible r)
    {
        var (result, dispenser) = Withdraw(r.Account, r.Module, r.Amount);
        if (result.Status != AtmStatus.Ok)
        {
            return;
        }

        var fee = FeeFor(r.Amount);
        Assert.Equal(r.Amount, result.Notes.Value);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(r.Account.Balance - r.Amount - fee, result.Account.Balance);
        Assert.Equal(r.Account.WithdrawnToday + r.Amount, result.Account.WithdrawnToday);
        Assert.Equal(r.Module.Stock - result.Notes, dispenser.Stock);
        Assert.Equal(r.Module.Jammed, dispenser.Jammed);

        // Le compte reste dans les bornes : solde positif, plafond respecté (les frais n'y comptent pas).
        Assert.InRange(result.Account.Balance, 0, 250);
        Assert.InRange(result.Account.WithdrawnToday, 0, 200);
    }

    /// <summary>Les billets rendus sont pris dans le disponible, jamais dans la cassette bloquée.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Reussi_BilletsPrisDansLeDisponible(RetraitPlausible r)
    {
        var (result, _) = Withdraw(r.Account, r.Module, r.Amount);
        if (result.Status != AtmStatus.Ok)
        {
            return;
        }

        Assert.True(FitsIn(result.Notes, r.Module.Available), $"{result.Notes} hors du disponible {r.Module.Available}");
        if (r.Module.Jammed is { } jammed)
        {
            Assert.Equal(0, result.Notes.Of(jammed));
        }
    }

    /// <summary>La combinaison rendue est celle qui compte le moins de billets, et elle est unique.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Reussi_LeMoinsDeBillets(RetraitPlausible r)
    {
        var (result, _) = Withdraw(r.Account, r.Module, r.Amount);
        if (result.Status != AtmStatus.Ok)
        {
            return;
        }

        var all = Combinations(r.Module.Available, r.Amount).ToList();
        var fewest = all.Min(c => c.Total);
        Assert.Equal(fewest, result.Notes.Total);
        Assert.Equal(result.Notes, Assert.Single(all, c => c.Total == fewest));
    }

    /// <summary>Les frais : 5 € exactement quand le montant dépasse 100 €, 0 € sinon et sur tout refus.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_FraisSelonLeSeuil(Retrait r)
    {
        var (result, _) = Withdraw(r.Account, r.Module, r.Amount);

        Assert.Equal(result.Status == AtmStatus.Ok && r.Amount > 100 ? 5 : 0, result.Fee);
    }

    /// <summary>
    /// Les passages : tous non vides, 4 billets au plus, sans billet de la cassette bloquée, et leur somme est la combinaison
    /// rendue. Un retrait ne range jamais rien, et un refus ne sollicite pas le module.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_PassagesDeQuatreBilletsAuPlus(Retrait r, RetraitPlausible p)
    {
        Check(r.Account, r.Module, r.Amount);
        Check(p.Account, p.Module, p.Amount);

        static void Check(AccountState account, Module module, int amount)
        {
            var spy = new ModuleEspion(module.Build());
            var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(amount));

            Assert.Empty(spy.Loaded);
            if (result.Status != AtmStatus.Ok)
            {
                Assert.Empty(spy.Dispensed);
                return;
            }

            Assert.All(spy.Dispensed, pass =>
            {
                Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass);
                Assert.True(pass is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0 }, $"passage négatif {pass}");
                if (module.Jammed is { } jammed)
                {
                    Assert.Equal(0, pass.Of(jammed));
                }
            });
            Assert.Equal(result.Notes, Sum(spy.Dispensed));
        }
    }

    /// <summary>La cassette bloquée compte pour zéro : même résultat qu'avec cette cassette vide et rien de bloqué.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_CassetteBloquee_CommeUneCassetteVide(Retrait r)
    {
        var equivalent = new Module(r.Module.Available, null);

        var (jammedResult, jammedDispenser) = Withdraw(r.Account, r.Module, r.Amount);
        var (emptyResult, emptyDispenser) = Withdraw(r.Account, equivalent, r.Amount);

        Assert.Equal(emptyResult, jammedResult);
        Assert.Equal(r.Module.Stock - r.Module.Available + emptyDispenser.Stock, jammedDispenser.Stock);
    }

    /// <summary>Plus de billets en stock ne fait jamais échouer un retrait réussi, et ne demande jamais plus de billets.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_PlusDeStock_JamaisMoinsBien(RetraitPlausible r, Retrait other)
    {
        // Le stock enrichi : le max des deux stocks tirés, donc toujours dans le domaine.
        var s = r.Module.Stock;
        var o = other.Module.Stock;
        var richer = new Module(
            new NoteCounts(Math.Max(s.Fifties, o.Fifties), Math.Max(s.Twenties, o.Twenties), Math.Max(s.Tens, o.Tens)),
            r.Module.Jammed);

        var (before, _) = Withdraw(r.Account, r.Module, r.Amount);
        var (after, _) = Withdraw(r.Account, richer, r.Amount);

        if (before.Status == AtmStatus.Ok)
        {
            Assert.Equal(AtmStatus.Ok, after.Status);
            Assert.True(after.Notes.Total <= before.Notes.Total, $"{after.Notes} compte plus de billets que {before.Notes}");
            Assert.Equal(before.Fee, after.Fee);
            Assert.Equal(before.Account, after.Account);
        }

        if (after.Status == AtmStatus.CannotDispense)
        {
            Assert.Equal(AtmStatus.CannotDispense, before.Status);
        }
    }

    /// <summary>Un solde plus élevé ne change rien, sauf qu'il peut lever un refus pour fonds insuffisants.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_SoldePlusEleve_SeulLesFondsInsuffisantsPeuventChanger(Retrait r, Retrait other)
    {
        var richer = r.Account with { Balance = Math.Max(r.Account.Balance, other.Account.Balance) };
        var extra = richer.Balance - r.Account.Balance;

        var (before, _) = Withdraw(r.Account, r.Module, r.Amount);
        var (after, _) = Withdraw(richer, r.Module, r.Amount);

        if (before.Status != AtmStatus.InsufficientFunds)
        {
            Assert.Equal(before.Status, after.Status);
            Assert.Equal(before.Notes, after.Notes);
            Assert.Equal(before.Fee, after.Fee);
            Assert.Equal(before.Account with { Balance = before.Account.Balance + extra }, after.Account);
        }
    }

    /// <summary>Moins de retraits dans la journée ne change rien, sauf qu'il peut lever un refus de plafond.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_MoinsDejaRetire_SeulLePlafondPeutChanger(Retrait r, Retrait other)
    {
        var fresher = r.Account with { WithdrawnToday = Math.Min(r.Account.WithdrawnToday, other.Account.WithdrawnToday) };
        var less = r.Account.WithdrawnToday - fresher.WithdrawnToday;

        var (before, _) = Withdraw(r.Account, r.Module, r.Amount);
        var (after, _) = Withdraw(fresher, r.Module, r.Amount);

        if (before.Status != AtmStatus.DailyLimitExceeded)
        {
            Assert.Equal(before.Status, after.Status);
            Assert.Equal(before.Notes, after.Notes);
            Assert.Equal(before.Fee, after.Fee);
            Assert.Equal(before.Account with { WithdrawnToday = before.Account.WithdrawnToday - less }, after.Account);
        }
    }

    /// <summary>
    /// Aller-retour : redéposer les billets d'un retrait réussi (quand c'est un dépôt du domaine) rend le stock de départ, et le
    /// solde de départ moins les frais ; le déjà-retiré, lui, ne redescend pas.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_PuisDepotDesMemesBillets_RetourAuDepart(RetraitPlausible r)
    {
        var dispenser = r.Module.Build();
        var machine = new CashMachine(dispenser);

        var withdrawal = machine.Execute(r.Account, new Operation.Withdraw(r.Amount));
        if (withdrawal.Status != AtmStatus.Ok || withdrawal.Notes is not { Fifties: <= 1, Twenties: <= 1, Tens: <= 1 })
        {
            return;
        }

        var deposit = machine.Execute(withdrawal.Account, new Operation.Deposit(withdrawal.Notes));

        Assert.Equal(AtmStatus.Ok, deposit.Status);
        Assert.Equal(r.Module.Stock, dispenser.Stock);
        Assert.Equal(new AccountState(r.Account.Balance - withdrawal.Fee, r.Account.WithdrawnToday + r.Amount), deposit.Account);
    }

    // ───────────────────────── Dépôt ─────────────────────────

    /// <summary>Le premier contrôle qui échoue, dans l'ordre de la spec, donne le statut ; sinon c'est <c>Ok</c>.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Depot_LePremierControleEnEchecDonneLeStatut(Depot d)
    {
        var (result, _) = Deposit(d.Account, d.Module, d.Notes);

        var after = d.Module.Stock + d.Notes;
        var expected =
            d.Notes.Total == 0 ? AtmStatus.InvalidAmount
            : d.Module.Jammed is { } jammed && d.Notes.Of(jammed) > 0 ? AtmStatus.CassetteJammed
            : !InDomain(after) ? AtmStatus.CassetteFull
            : AtmStatus.Ok;

        Assert.Equal(expected, result.Status);
    }

    /// <summary>Sur un refus, rien ne change ; et le module n'est même pas sollicité.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Depot_Refuse_RienNeChange(Depot d)
    {
        var spy = new ModuleEspion(d.Module.Build());
        var result = new CashMachine(spy).Execute(d.Account, new Operation.Deposit(d.Notes));

        Assert.Empty(spy.Dispensed);
        if (result.Status != AtmStatus.Ok)
        {
            Assert.Empty(spy.Loaded);
            Assert.Equal(Nothing, result.Notes);
            Assert.Equal(0, result.Fee);
            Assert.Equal(d.Account, result.Account);
            Assert.Equal(d.Module.Stock, spy.Stock);
        }
    }

    /// <summary>Sur un succès : les billets rejoignent le module, le solde augmente de leur valeur, rien d'autre ne bouge.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Depot_Reussi_LesBilletsRejoignentLeModule(Depot d)
    {
        var spy = new ModuleEspion(d.Module.Build());
        var result = new CashMachine(spy).Execute(d.Account, new Operation.Deposit(d.Notes));
        if (result.Status != AtmStatus.Ok)
        {
            return;
        }

        Assert.Equal(Nothing, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(d.Account.Balance + d.Notes.Value, d.Account.WithdrawnToday), result.Account);
        Assert.Equal(d.Module.Stock + d.Notes, spy.Stock);
        Assert.Equal(d.Notes, Sum(spy.Loaded));
        Assert.True(InDomain(spy.Stock), $"stock hors domaine après dépôt : {spy.Stock}");
    }

    /// <summary>Avec le vrai module, un dépôt ne rend jamais un statut de retrait ni une panne.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Depot_NeRendQueDesStatutsDeDepot(Depot d)
    {
        var (result, _) = Deposit(d.Account, d.Module, d.Notes);

        Assert.Contains(result.Status, new[]
        {
            AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.CassetteJammed, AtmStatus.CassetteFull,
        });
    }

    /// <summary>Le compte n'intervient pas dans les contrôles du dépôt : seul le solde est décalé.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Depot_IndependantDuCompte(Depot d, Retrait other)
    {
        var (first, firstDispenser) = Deposit(d.Account, d.Module, d.Notes);
        var (second, secondDispenser) = Deposit(other.Account, d.Module, d.Notes);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Notes, second.Notes);
        Assert.Equal(first.Fee, second.Fee);
        Assert.Equal(first.Account.Balance - d.Account.Balance, second.Account.Balance - other.Account.Balance);
        Assert.Equal(other.Account.WithdrawnToday, second.Account.WithdrawnToday);
        Assert.Equal(firstDispenser.Stock, secondDispenser.Stock);
    }

    /// <summary>Un dépôt sans billet de la cassette bloquée se passe comme si rien n'était bloqué.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Depot_SansBilletBloque_CommeSansBlocage(Depot d)
    {
        if (d.Module.Jammed is { } jammed && d.Notes.Of(jammed) > 0)
        {
            return;
        }

        var (jammedResult, jammedDispenser) = Deposit(d.Account, d.Module, d.Notes);
        var (freeResult, freeDispenser) = Deposit(d.Account, d.Module with { Jammed = null }, d.Notes);

        Assert.Equal(freeResult, jammedResult);
        Assert.Equal(freeDispenser.Stock, jammedDispenser.Stock);
    }

    // ───────────────────────── Pannes du module ─────────────────────────

    /// <summary>
    /// Panne pendant un retrait : <c>DispenserFault</c>, arrêt immédiat, pas de frais, compte identique, et <c>Notes</c> contient
    /// exactement les billets déjà sortis. Si la panne tombe après le dernier passage, le retrait se déroule normalement.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Retrait_PanneDuModule_ArretImmediat(RetraitEnPanne p)
    {
        var r = p.Retrait;
        var normalSpy = new ModuleEspion(r.Module.Build());
        var normal = new CashMachine(normalSpy).Execute(r.Account, new Operation.Withdraw(r.Amount));
        var passes = normalSpy.Dispensed;
        var failingPass = 1 + (p.Draw - 1) % (passes.Count + 1);

        var spy = new ModuleEspion(r.Module.Build(), failingPass);
        var result = new CashMachine(spy).Execute(r.Account, new Operation.Withdraw(r.Amount));

        if (failingPass > passes.Count)
        {
            // Refus (aucun passage) ou panne qui tomberait après le dernier passage : déroulement normal.
            Assert.Equal(normal, result);
            Assert.Equal(passes, spy.Dispensed);
            Assert.Equal(r.Module.Stock - result.Notes, spy.Stock);
            return;
        }

        // Mêmes passages que sans panne jusqu'à celui qui échoue, puis plus aucun.
        Assert.Equal(passes.Take(failingPass), spy.Dispensed);
        var alreadyOut = Sum(passes.Take(failingPass - 1));
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, alreadyOut, 0, r.Account), result);
        Assert.Equal(r.Module.Stock - alreadyOut, spy.Stock);
    }

    /// <summary>Panne pendant un dépôt : <c>DispenserFault</c>, <c>Notes</c> vide, pas de frais, compte et stock identiques.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Depot_PanneDuModule_RienNeChange(Depot d)
    {
        var (normal, _) = Deposit(d.Account, d.Module, d.Notes);

        var spy = new ModuleEspion(d.Module.Build(), failLoad: true);
        var result = new CashMachine(spy).Execute(d.Account, new Operation.Deposit(d.Notes));

        var expected = normal.Status == AtmStatus.Ok ? AtmStatus.DispenserFault : normal.Status;
        Assert.Equal(new AtmResult(expected, Nothing, 0, d.Account), result);
        Assert.Equal(d.Module.Stock, spy.Stock);
    }

    // ───────────────────────── Suites d'opérations ─────────────────────────

    /// <summary>
    /// La machine est sans mémoire : dans une suite d'opérations sur la même machine, chaque résultat est celui qu'une machine
    /// neuve, sur un module neuf dans le même état, aurait rendu. Le stock reste dans le domaine et la cassette bloquée ne bouge
    /// pas. On s'arrête dès que le compte sort du domaine (un dépôt peut porter le solde au-delà de 250).
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Suite_SansMemoireEntreDeuxAppels(Sequence s)
    {
        var dispenser = s.Module.Build();
        var machine = new CashMachine(dispenser);
        var account = s.Account;

        foreach (var operation in s.Operations)
        {
            if (!InDomain(account))
            {
                break;
            }

            var (fresh, freshDispenser) = Run(account, new Module(dispenser.Stock, s.Module.Jammed), operation);
            var result = machine.Execute(account, operation);

            Assert.Equal(fresh, result);
            Assert.Equal(freshDispenser.Stock, dispenser.Stock);
            Assert.Equal(s.Module.Jammed, dispenser.Jammed);
            Assert.True(InDomain(dispenser.Stock), $"stock hors domaine : {dispenser.Stock}");

            account = result.Account;
        }
    }

    /// <summary>
    /// Conservation de l'argent sur une suite d'opérations : les billets du module plus ceux du client sont constants, et le solde
    /// plus l'argent liquide du client plus les frais prélevés aussi.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Domaine)])]
    public void Suite_ConservationDeLArgent(Sequence s)
    {
        var dispenser = s.Module.Build();
        var machine = new CashMachine(dispenser);
        var account = s.Account;
        var cash = 0;
        var fees = 0;

        foreach (var operation in s.Operations)
        {
            if (!InDomain(account))
            {
                break;
            }

            var result = machine.Execute(account, operation);
            var deposited = operation is Operation.Deposit { Notes: var notes } && result.Status == AtmStatus.Ok ? notes : Nothing;
            cash += result.Notes.Value - deposited.Value;
            fees += result.Fee;
            account = result.Account;

            Assert.Equal(s.Module.Stock.Value, dispenser.Stock.Value + cash);
            Assert.Equal(s.Account.Balance, account.Balance + cash + fees);
            Assert.True(account.WithdrawnToday >= s.Account.WithdrawnToday, "le déjà-retiré a diminué");
            Assert.InRange(account.WithdrawnToday, 0, 200);
            Assert.True(account.Balance >= 0, $"solde négatif : {account.Balance}");
        }
    }
}
