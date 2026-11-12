using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Atm.Tests;

// ---- Les données générées ----
// Des records : FsCheck affiche le contre-exemple lisiblement en cas d'échec.

/// <summary>Un état de départ pris dans le domaine de la spécification : le compte et le module.</summary>
public sealed record Situation(AccountState Account, NoteCounts Stock, Denomination? Jammed)
{
    public MoneyDispenser Module() => new(Stock, Jammed);

    internal FakeDispenser Doublure(int? failDispenseAt = null, bool failLoad = false) =>
        new(Stock, Jammed) { FailDispenseAt = failDispenseAt, FailLoad = failLoad };
}

/// <summary>Un montant de retrait du domaine : -10, 0, 5, ou un multiple de 10 de 10 à 220.</summary>
public sealed record Montant(int Value);

/// <summary>Un montant du domaine refusé dès le premier contrôle : -10, 0 ou 5.</summary>
public sealed record MontantInvalide(int Value);

/// <summary>Un dépôt du domaine : 0 ou 1 billet par coupure.</summary>
public sealed record Depot(NoteCounts Notes);

/// <summary>Un retrait valide, mais qui fait dépasser le plafond du jour.</summary>
public sealed record RetraitHorsPlafond(Situation Situation, int Amount);

/// <summary>Un retrait valide, dans le plafond, mais que le solde ne couvre pas (frais compris).</summary>
public sealed record RetraitSansProvision(Situation Situation, int Amount);

/// <summary>Un retrait qui passe les contrôles 1 à 3 : seul le stock décide encore.</summary>
public sealed record RetraitAdmis(Situation Situation, int Amount);

/// <summary>Un retrait qui passe les quatre contrôles : une combinaison exacte existe dans les billets disponibles.</summary>
public sealed record RetraitServable(Situation Situation, int Amount);

/// <summary>Un retrait servable dont la combinaison compte plus de 4 billets : il faut plusieurs passages.</summary>
public sealed record RetraitEnPlusieursPassages(Situation Situation, int Amount);

/// <summary>Un dépôt non vide qui contient un billet de la cassette bloquée.</summary>
public sealed record DepotBloque(Situation Situation, NoteCounts Notes);

/// <summary>Un dépôt non vide, sans billet bloqué, qui fait déborder au moins une cassette.</summary>
public sealed record DepotDebordant(Situation Situation, NoteCounts Notes);

/// <summary>Un dépôt qui passe les trois contrôles.</summary>
public sealed record DepotAdmis(Situation Situation, NoteCounts Notes);

/// <summary>Une suite d'opérations du domaine ; <c>null</c> marque un changement de jour.</summary>
public sealed record Parcours(Situation Depart, Operation?[] Pas)
{
    public override string ToString() =>
        $"Parcours {{ Depart = {Depart}, Pas = [{string.Join(", ", Pas.Select(p => p?.ToString() ?? "NouveauJour"))}] }}";
}

/// <summary>Des billets quelconques présentés directement au module (0 à 5 par coupure).</summary>
public sealed record Billets(NoteCounts Notes);

// ---- Les générateurs ----

public static class Domaine
{
    public static readonly Denomination[] Coupures = [Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static int Frais(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Disponibles(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    /// <summary>Toutes les combinaisons de billets pris dans <paramref name="available"/> qui font exactement <paramref name="amount"/>.</summary>
    public static IEnumerable<NoteCounts> Combinaisons(int amount, NoteCounts available)
    {
        for (var f = 0; f <= available.Fifties; f++)
            for (var t = 0; t <= available.Twenties; t++)
                for (var c = 0; c <= available.Tens; c++)
                {
                    var n = new NoteCounts(f, t, c);
                    if (n.Value == amount) yield return n;
                }
    }

    public static bool TientDans(NoteCounts notes, NoteCounts limit) =>
        notes.Fifties <= limit.Fifties && notes.Twenties <= limit.Twenties && notes.Tens <= limit.Tens;

    public static bool Positif(NoteCounts notes) => notes is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0 };

    public static NoteCounts Avec(NoteCounts notes, Denomination d, int count) => d switch
    {
        Denomination.Fifty => notes with { Fifties = count },
        Denomination.Twenty => notes with { Twenties = count },
        _ => notes with { Tens = count },
    };

    // -- Briques --

    private static Gen<int> Multiples(int step, int min, int max) => Gen.Choose(min / step, max / step).Select(i => i * step);

    private static readonly Gen<int> Solde = Multiples(5, 0, 250);

    private static readonly Gen<int> DejaRetire = Multiples(10, 0, 200);

    private static readonly Gen<int> MontantValide = Multiples(10, 10, 220);

    private static readonly Gen<NoteCounts> Stock =
        from f in Gen.Choose(0, 5)
        from t in Gen.Choose(0, 5)
        from c in Gen.Choose(0, 5)
        select new NoteCounts(f, t, c);

    private static readonly Gen<Denomination?> Bloquee =
        Gen.Elements<Denomination?>(null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten);

    private static readonly Gen<NoteCounts> DepotQuelconque =
        Gen.Choose(0, 7).Select(m => new NoteCounts(m & 1, (m >> 1) & 1, (m >> 2) & 1));

    private static readonly Gen<int> MontantDuDomaine =
        Gen.Elements(new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10)).ToArray());

    private static readonly Gen<Situation> SituationQuelconque =
        from b in Solde
        from w in DejaRetire
        from s in Stock
        from j in Bloquee
        select new Situation(new AccountState(b, w), s, j);

    /// <summary>Un compte qui passe les contrôles 2 et 3 pour ce montant (montant valide, au plus 200).</summary>
    private static Gen<AccountState> CompteQuiCouvre(int amount) =>
        from w in Multiples(10, 0, 200 - amount)
        from b in Multiples(5, amount + Frais(amount), 250)
        select new AccountState(b, w);

    // -- Arbitraires --

    public static Arbitrary<Situation> Situation() => Arb.From(SituationQuelconque);

    public static Arbitrary<Montant> Montant() => Arb.From(MontantDuDomaine.Select(a => new Montant(a)));

    public static Arbitrary<MontantInvalide> MontantInvalide() =>
        Arb.From(Gen.Elements(-10, 0, 5).Select(a => new MontantInvalide(a)));

    public static Arbitrary<Depot> Depot() => Arb.From(DepotQuelconque.Select(n => new Depot(n)));

    public static Arbitrary<RetraitHorsPlafond> RetraitHorsPlafond() => Arb.From(
        from a in MontantValide
        from w in Multiples(10, Math.Max(0, 210 - a), 200)
        from b in Solde
        from s in Stock
        from j in Bloquee
        select new RetraitHorsPlafond(new Situation(new AccountState(b, w), s, j), a));

    public static Arbitrary<RetraitSansProvision> RetraitSansProvision() => Arb.From(
        from a in Multiples(10, 10, 200)
        from w in Multiples(10, 0, 200 - a)
        from b in Multiples(5, 0, a + Frais(a) - 5)
        from s in Stock
        from j in Bloquee
        select new RetraitSansProvision(new Situation(new AccountState(b, w), s, j), a));

    public static Arbitrary<RetraitAdmis> RetraitAdmis() => Arb.From(
        from a in Multiples(10, 10, 200)
        from account in CompteQuiCouvre(a)
        from s in Stock
        from j in Bloquee
        select new RetraitAdmis(new Situation(account, s, j), a));

    /// <summary>On tire d'abord une combinaison dans les billets disponibles : son montant est forcément servable.</summary>
    public static Arbitrary<RetraitServable> RetraitServable() => Arb.From(
        from tirage in (
            from s in Stock
            from j in Bloquee
            from f in Gen.Choose(0, 5)
            from t in Gen.Choose(0, 5)
            from c in Gen.Choose(0, 5)
            let available = Disponibles(s, j)
            let pick = new NoteCounts(Math.Min(f, available.Fifties), Math.Min(t, available.Twenties), Math.Min(c, available.Tens))
            select (Stock: s, Jammed: j, Pick: pick)).Where(x => x.Pick.Total > 0 && x.Pick.Value <= 200)
        from account in CompteQuiCouvre(tirage.Pick.Value)
        select new RetraitServable(new Situation(account, tirage.Stock, tirage.Jammed), tirage.Pick.Value));

    /// <summary>Moitié plus de 4 billets (au moins 2 passages), moitié plus de 8 (3 passages, rares sinon).</summary>
    public static Arbitrary<RetraitEnPlusieursPassages> RetraitEnPlusieursPassages()
    {
        Gen<RetraitEnPlusieursPassages> AuMoins(int notes) => RetraitServable().Generator
            .Where(r => Combinaisons(r.Amount, Disponibles(r.Situation.Stock, r.Situation.Jammed)).Min(n => n.Total) >= notes)
            .Select(r => new RetraitEnPlusieursPassages(r.Situation, r.Amount));

        return Arb.From(Gen.OneOf(AuMoins(5), AuMoins(9)));
    }

    public static Arbitrary<DepotBloque> DepotBloque() => Arb.From(
        from situation in SituationQuelconque
        from j in Gen.Elements(Coupures)
        from n in DepotQuelconque
        select new DepotBloque(situation with { Jammed = j }, Avec(n, j, 1)));

    public static Arbitrary<DepotDebordant> DepotDebordant() => Arb.From(
        from situation in SituationQuelconque
        from plein in Gen.Elements(Coupures)
        from n in DepotQuelconque
        let notes = Avec(n, plein, 1)
        let jammed = situation.Jammed is { } j && notes.Of(j) > 0 ? (Denomination?)null : situation.Jammed
        select new DepotDebordant(situation with { Stock = Avec(situation.Stock, plein, 5), Jammed = jammed }, notes));

    public static Arbitrary<DepotAdmis> DepotAdmis() => Arb.From(
        (from situation in SituationQuelconque
         from n in DepotQuelconque
         select new DepotAdmis(situation, n))
        .Where(d => d.Notes.Total > 0
                    && (d.Situation.Jammed is not { } j || d.Notes.Of(j) == 0)
                    && TientDans(d.Situation.Stock + d.Notes, new NoteCounts(5, 5, 5))));

    public static Arbitrary<Parcours> Parcours() => Arb.From(
        from depart in SituationQuelconque
        from pas in Gen.ArrayOf(Gen.Frequency(
            (6, MontantDuDomaine.Select(a => (Operation?)new Operation.Withdraw(a))),
            (6, DepotQuelconque.Select(n => (Operation?)new Operation.Deposit(n))),
            (1, Gen.Constant((Operation?)null))))
        select new Parcours(depart, pas));

    public static Arbitrary<Billets> Billets() => Arb.From(Stock.Select(n => new Billets(n)));
}

// ---- Les propriétés ----

/// <summary>
/// Tests de propriétés : des règles de la spécification qui doivent tenir pour toute entrée du domaine,
/// vérifiées sur 200 cas tirés au hasard à partir d'une graine fixe.
/// </summary>
public sealed class Proprietes
{
    private static AtmResult Executer(MoneyDispenser module, AccountState account, Operation operation) =>
        new CashMachine(module).Execute(account, operation);

    private static void AssertRefus(AtmResult result, AtmStatus status, AccountState account)
    {
        Assert.Equal(new AtmResult(status, NoteCounts.None, 0, account), result);
    }

    // ======== Retrait : les contrôles, dans l'ordre ========

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_MontantInvalide_EstRefuseQuelQueSoitLeReste(Situation s, MontantInvalide a)
    {
        var module = s.Module();

        var result = Executer(module, s.Account, new Operation.Withdraw(a.Value));

        AssertRefus(result, AtmStatus.InvalidAmount, s.Account);
        Assert.Equal(s.Stock, module.Stock);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_AuDelaDuPlafond_EstRefuseQuelsQueSoientSoldeEtStock(RetraitHorsPlafond r)
    {
        var module = r.Situation.Module();

        var result = Executer(module, r.Situation.Account, new Operation.Withdraw(r.Amount));

        AssertRefus(result, AtmStatus.DailyLimitExceeded, r.Situation.Account);
        Assert.Equal(r.Situation.Stock, module.Stock);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_SoldeQuiNeCouvrePasMontantEtFrais_EstRefuseQuelQueSoitLeStock(RetraitSansProvision r)
    {
        var module = r.Situation.Module();

        var result = Executer(module, r.Situation.Account, new Operation.Withdraw(r.Amount));

        AssertRefus(result, AtmStatus.InsufficientFunds, r.Situation.Account);
        Assert.Equal(r.Situation.Stock, module.Stock);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Admis_EstServiSiEtSeulementSiUneCombinaisonExiste(RetraitAdmis r)
    {
        var s = r.Situation;
        var existe = Domaine.Combinaisons(r.Amount, Domaine.Disponibles(s.Stock, s.Jammed)).Any();

        var result = Executer(s.Module(), s.Account, new Operation.Withdraw(r.Amount));

        Assert.Equal(existe ? AtmStatus.Ok : AtmStatus.CannotDispense, result.Status);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_StatutSuitLePremierControleQuiEchoue(Situation s, Montant a)
    {
        var amount = a.Value;
        var fee = Domaine.Frais(amount);
        var expected =
            amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
            : s.Account.WithdrawnToday + amount > 200 ? AtmStatus.DailyLimitExceeded
            : amount + fee > s.Account.Balance ? AtmStatus.InsufficientFunds
            : !Domaine.Combinaisons(amount, Domaine.Disponibles(s.Stock, s.Jammed)).Any() ? AtmStatus.CannotDispense
            : AtmStatus.Ok;

        var result = Executer(s.Module(), s.Account, new Operation.Withdraw(amount));

        Assert.Equal(expected, result.Status);
    }

    // ======== Retrait : refus ========

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Refuse_NeChangeRien(Situation s, Montant a)
    {
        var dispenser = s.Doublure();

        var result = new CashMachine(dispenser).Execute(s.Account, new Operation.Withdraw(a.Value));

        // Avec un module qui ne tombe pas en panne, il n'y a que deux issues : le succès ou un refus de la table.
        Assert.Contains(result.Status, new[]
        {
            AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.DailyLimitExceeded, AtmStatus.InsufficientFunds,
            AtmStatus.CannotDispense,
        });
        if (result.Status == AtmStatus.Ok) return;

        AssertRefus(result, result.Status, s.Account);
        Assert.Equal(s.Stock, dispenser.Stock);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Refuse_LeStatutNeDependPasDuModule(Situation s, Montant a)
    {
        // Un refus est décidé avant toute sortie de billets : qu'elle doive échouer n'y change rien.
        var healthy = Executer(s.Module(), s.Account, new Operation.Withdraw(a.Value));
        if (healthy.Status == AtmStatus.Ok) return;

        var faulty = new CashMachine(s.Doublure(failDispenseAt: 1)).Execute(s.Account, new Operation.Withdraw(a.Value));

        Assert.Equal(healthy, faulty);
    }

    // ======== Retrait : succès ========

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Servable_DebiteLeCompteEtSortExactementLeMontant(RetraitServable r)
    {
        var s = r.Situation;
        var fee = Domaine.Frais(r.Amount);
        var module = s.Module();

        var result = Executer(module, s.Account, new Operation.Withdraw(r.Amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(r.Amount, result.Notes.Value);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(new AccountState(s.Account.Balance - r.Amount - fee, s.Account.WithdrawnToday + r.Amount), result.Account);
        // Ce que le résultat annonce est exactement ce qui a quitté le module.
        Assert.Equal(s.Stock - result.Notes, module.Stock);
        // Le compte reste dans le domaine : le solde ne devient pas négatif, le plafond n'est pas franchi.
        Assert.InRange(result.Account.Balance, 0, 250);
        Assert.InRange(result.Account.WithdrawnToday, 0, 200);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Servable_FraisSeulementAuDelaDe100(RetraitServable r)
    {
        var s = r.Situation;

        var result = Executer(s.Module(), s.Account, new Operation.Withdraw(r.Amount));

        Assert.Equal(r.Amount > 100, result.Fee == 5);
        Assert.Equal(r.Amount <= 100, result.Fee == 0);
        // Les frais ne comptent pas dans le plafond du jour.
        Assert.Equal(s.Account.WithdrawnToday + r.Amount, result.Account.WithdrawnToday);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Servable_NUtiliseQueDesBilletsDisponibles(RetraitServable r)
    {
        var s = r.Situation;

        var result = Executer(s.Module(), s.Account, new Operation.Withdraw(r.Amount));

        Assert.True(Domaine.Positif(result.Notes));
        Assert.True(Domaine.TientDans(result.Notes, s.Stock));
        if (s.Jammed is { } jammed) Assert.Equal(0, result.Notes.Of(jammed));
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Servable_RendLaSeuleCombinaisonLaPlusCourte(RetraitServable r)
    {
        var s = r.Situation;
        var combinaisons = Domaine.Combinaisons(r.Amount, Domaine.Disponibles(s.Stock, s.Jammed)).ToList();

        var result = Executer(s.Module(), s.Account, new Operation.Withdraw(r.Amount));

        Assert.Contains(result.Notes, combinaisons);
        // Aucune autre combinaison exacte ne compte autant de billets, ou moins.
        Assert.DoesNotContain(combinaisons, n => n != result.Notes && n.Total <= result.Notes.Total);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_Servable_SortEnPassagesDeUnAQuatreBillets(RetraitServable r)
    {
        var s = r.Situation;
        var dispenser = s.Doublure();

        var result = new CashMachine(dispenser).Execute(s.Account, new Operation.Withdraw(r.Amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.NotEmpty(dispenser.DispenseCalls);
        Assert.All(dispenser.DispenseCalls, pass =>
        {
            Assert.True(Domaine.Positif(pass));
            Assert.InRange(pass.Total, 1, 4);
            if (s.Jammed is { } jammed) Assert.Equal(0, pass.Of(jammed));
        });
        Assert.Equal(result.Notes, dispenser.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Empty(dispenser.LoadCalls);
    }

    // ======== Retrait : relations entre exécutions ========

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_LesBilletsNeDependentPasDuCompte(RetraitServable r, Situation autre)
    {
        // Un autre compte qui passe lui aussi les contrôles 2 et 3, sur le même module.
        var s = r.Situation;
        var fee = Domaine.Frais(r.Amount);
        var account = new AccountState(
            Math.Max(autre.Account.Balance, r.Amount + fee),
            Math.Min(autre.Account.WithdrawnToday, 200 - r.Amount));

        var first = Executer(s.Module(), s.Account, new Operation.Withdraw(r.Amount));
        var second = Executer(s.Module(), account, new Operation.Withdraw(r.Amount));

        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(first.Notes, second.Notes);
        Assert.Equal(first.Fee, second.Fee);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_PlusDeBillets_ResteServiAvecAuPlusAutantDeBillets(RetraitServable r, Billets ajout)
    {
        var s = r.Situation;
        var sum = s.Stock + ajout.Notes;
        var stock = new NoteCounts(Math.Min(sum.Fifties, 5), Math.Min(sum.Twenties, 5), Math.Min(sum.Tens, 5));

        var before = Executer(s.Module(), s.Account, new Operation.Withdraw(r.Amount));
        var after = Executer(new MoneyDispenser(stock, s.Jammed), s.Account, new Operation.Withdraw(r.Amount));

        Assert.Equal(AtmStatus.Ok, after.Status);
        Assert.True(after.Notes.Total <= before.Notes.Total);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_DebloquerLaCassette_ResteServiAvecAuPlusAutantDeBillets(RetraitServable r)
    {
        var s = r.Situation;

        var jammed = Executer(s.Module(), s.Account, new Operation.Withdraw(r.Amount));
        var free = Executer(new MoneyDispenser(s.Stock), s.Account, new Operation.Withdraw(r.Amount));

        Assert.Equal(AtmStatus.Ok, free.Status);
        Assert.True(free.Notes.Total <= jammed.Notes.Total);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_PuisDepotDesMemesBillets_RestaureLeStock(RetraitServable r)
    {
        var s = r.Situation;
        var module = s.Module();
        var machine = new CashMachine(module);

        var withdrawal = machine.Execute(s.Account, new Operation.Withdraw(r.Amount));
        var notes = withdrawal.Notes;
        if (notes.Fifties > 1 || notes.Twenties > 1 || notes.Tens > 1) return; // hors du domaine des dépôts

        var deposit = machine.Execute(withdrawal.Account, new Operation.Deposit(notes));

        Assert.Equal(AtmStatus.Ok, deposit.Status);
        Assert.Equal(s.Stock, module.Stock);
        Assert.Equal(new AccountState(s.Account.Balance - withdrawal.Fee, s.Account.WithdrawnToday + r.Amount), deposit.Account);
    }

    // ======== Retrait : pannes ========

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Retrait_PanneAuPassageK_SArreteEtRendLesBilletsDejaSortis(RetraitEnPlusieursPassages r, PositiveInt k)
    {
        var s = r.Situation;

        // Le découpage en passages sans panne : la panne tombe sur l'un d'eux, le premier comme les suivants.
        var reference = s.Doublure();
        new CashMachine(reference).Execute(s.Account, new Operation.Withdraw(r.Amount));
        var passes = reference.DispenseCalls.Count;
        Assert.True(passes >= 2);
        var failAt = (k.Get - 1) % passes + 1;

        var dispenser = s.Doublure(failDispenseAt: failAt);
        var result = new CashMachine(dispenser).Execute(s.Account, new Operation.Withdraw(r.Amount));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(s.Account, result.Account);
        // On s'arrête aussitôt : aucun passage après celui qui a échoué.
        Assert.Equal(failAt, dispenser.DispenseCalls.Count);
        // Les billets annoncés sont ceux des passages réussis, et ce sont eux qui ont quitté le module.
        Assert.Equal(dispenser.DispenseCalls.Take(failAt - 1).Aggregate(NoteCounts.None, (a, b) => a + b), result.Notes);
        Assert.Equal(s.Stock - result.Notes, dispenser.Stock);
        Assert.True(result.Notes.Value < r.Amount);
    }

    // ======== Dépôt ========

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Depot_Vide_EstRefuse(Situation s)
    {
        var module = s.Module();

        var result = Executer(module, s.Account, new Operation.Deposit(NoteCounts.None));

        AssertRefus(result, AtmStatus.InvalidAmount, s.Account);
        Assert.Equal(s.Stock, module.Stock);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Depot_AvecUnBilletBloque_EstRefuseMemeSiLaCassetteEstPleine(DepotBloque d)
    {
        var module = d.Situation.Module();

        var result = Executer(module, d.Situation.Account, new Operation.Deposit(d.Notes));

        AssertRefus(result, AtmStatus.CassetteJammed, d.Situation.Account);
        Assert.Equal(d.Situation.Stock, module.Stock);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Depot_QuiFeraitDeborderUneCassette_EstRefuse(DepotDebordant d)
    {
        var module = d.Situation.Module();

        var result = Executer(module, d.Situation.Account, new Operation.Deposit(d.Notes));

        AssertRefus(result, AtmStatus.CassetteFull, d.Situation.Account);
        Assert.Equal(d.Situation.Stock, module.Stock);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Depot_Admis_CrediteLeCompteEtRangeLesBillets(DepotAdmis d)
    {
        var s = d.Situation;
        var module = s.Module();

        var result = Executer(module, s.Account, new Operation.Deposit(d.Notes));

        Assert.Equal(
            new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, s.Account with { Balance = s.Account.Balance + d.Notes.Value }),
            result);
        Assert.Equal(s.Stock + d.Notes, module.Stock);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Depot_StatutSuitLePremierControleQuiEchoue(Situation s, Depot d)
    {
        var expected =
            d.Notes.Total == 0 ? AtmStatus.InvalidAmount
            : s.Jammed is { } j && d.Notes.Of(j) > 0 ? AtmStatus.CassetteJammed
            : !Domaine.TientDans(s.Stock + d.Notes, new NoteCounts(5, 5, 5)) ? AtmStatus.CassetteFull
            : AtmStatus.Ok;

        var result = Executer(s.Module(), s.Account, new Operation.Deposit(d.Notes));

        Assert.Equal(expected, result.Status);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Depot_Refuse_NeChangeRien(Situation s, Depot d)
    {
        var dispenser = s.Doublure();

        var result = new CashMachine(dispenser).Execute(s.Account, new Operation.Deposit(d.Notes));

        Assert.Contains(result.Status, new[]
        {
            AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.CassetteJammed, AtmStatus.CassetteFull,
        });
        Assert.Empty(dispenser.DispenseCalls); // un dépôt ne sort jamais de billets
        if (result.Status == AtmStatus.Ok) return;

        AssertRefus(result, result.Status, s.Account);
        Assert.Equal(s.Stock, dispenser.Stock);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Depot_PanneDuModule_NeChangeNiLeCompteNiLeStock(DepotAdmis d)
    {
        var s = d.Situation;
        var dispenser = s.Doublure(failLoad: true);

        var result = new CashMachine(dispenser).Execute(s.Account, new Operation.Deposit(d.Notes));

        AssertRefus(result, AtmStatus.DispenserFault, s.Account);
        Assert.Equal(s.Stock, dispenser.Stock);
        Assert.Equal(new[] { d.Notes }, dispenser.LoadCalls);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Depot_Refuse_LeStatutNeDependPasDuModule(Situation s, Depot d)
    {
        // Un refus est décidé avant tout appel au module : qu'il soit en panne n'y change rien.
        var healthy = Executer(s.Module(), s.Account, new Operation.Deposit(d.Notes));
        if (healthy.Status == AtmStatus.Ok) return;

        var faulty = new CashMachine(s.Doublure(failLoad: true)).Execute(s.Account, new Operation.Deposit(d.Notes));

        Assert.Equal(healthy, faulty);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Depot_PuisRetraitDuMemeMontant_RestaureLeSolde(DepotAdmis d)
    {
        var s = d.Situation;
        var module = s.Module();
        var machine = new CashMachine(module);
        var value = d.Notes.Value;

        var deposit = machine.Execute(s.Account, new Operation.Deposit(d.Notes));
        var withdrawal = machine.Execute(deposit.Account, new Operation.Withdraw(value));

        if (s.Account.WithdrawnToday + value > 200)
        {
            AssertRefus(withdrawal, AtmStatus.DailyLimitExceeded, deposit.Account);
            return;
        }

        // Les billets déposés ne sont pas bloqués : ils suffisent à servir leur propre montant, sans frais (au plus 80 €).
        Assert.Equal(AtmStatus.Ok, withdrawal.Status);
        Assert.Equal(0, withdrawal.Fee);
        Assert.Equal(s.Account.Balance, withdrawal.Account.Balance);
        Assert.Equal(s.Stock.Value, module.Stock.Value);
        Assert.True(withdrawal.Notes.Total <= d.Notes.Total);
    }

    // ======== La machine dans son ensemble ========

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Machine_SansMemoire_UneMachineQuiAServiSeComporteCommeUneNeuve(Parcours p, Montant a, Depot d)
    {
        var module = p.Depart.Module();
        var used = new CashMachine(module);
        var account = p.Depart.Account;
        foreach (var operation in p.Pas.OfType<Operation>().Take(10))
        {
            used.Execute(account, operation);
        }

        foreach (var operation in new Operation[] { new Operation.Withdraw(a.Value), new Operation.Deposit(d.Notes) })
        {
            var copy = new MoneyDispenser(module.Stock, module.Jammed);

            var expected = new CashMachine(copy).Execute(account, operation);
            var actual = used.Execute(account, operation);

            Assert.Equal(expected, actual);
            Assert.Equal(copy.Stock, module.Stock);
        }
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Machine_Deterministe_MemesEntreesMemesSorties(Situation s, Montant a, Depot d)
    {
        foreach (var operation in new Operation[] { new Operation.Withdraw(a.Value), new Operation.Deposit(d.Notes) })
        {
            var m1 = s.Module();
            var m2 = s.Module();

            Assert.Equal(Executer(m1, s.Account, operation), Executer(m2, s.Account, operation));
            Assert.Equal(m1.Stock, m2.Stock);
        }
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Parcours_LArgentEstConserveEtToutResteDansLeDomaine(Parcours p)
    {
        var module = p.Depart.Module();
        var machine = new CashMachine(module);
        var account = p.Depart.Account;
        var fees = 0;
        var invariant = account.Balance - module.Stock.Value;
        var jammedCount = p.Depart.Jammed is { } j0 ? p.Depart.Stock.Of(j0) : 0;

        foreach (var pas in p.Pas)
        {
            if (pas is null)
            {
                account = account with { WithdrawnToday = 0 }; // un nouveau jour
                continue;
            }

            // Le solde doit rester dans le domaine : on saute un dépôt qui le ferait dépasser 250.
            if (pas is Operation.Deposit(var depot) && account.Balance + depot.Value > 250) continue;

            var before = module.Stock;
            var result = machine.Execute(account, pas);

            Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
            if (result.Status != AtmStatus.Ok) Assert.Equal(account, result.Account);

            switch (pas)
            {
                case Operation.Withdraw(var amount) when result.Status == AtmStatus.Ok:
                    Assert.Equal(before - result.Notes, module.Stock);
                    Assert.Equal(account.WithdrawnToday + amount, result.Account.WithdrawnToday);
                    break;
                case Operation.Deposit(var notes) when result.Status == AtmStatus.Ok:
                    Assert.Equal(before + notes, module.Stock);
                    Assert.Equal(account.WithdrawnToday, result.Account.WithdrawnToday);
                    break;
                default:
                    Assert.Equal(before, module.Stock);
                    break;
            }

            account = result.Account;
            fees += result.Fee;

            // Le solde et le stock bougent ensemble, à la différence des frais près.
            Assert.Equal(invariant, account.Balance + fees - module.Stock.Value);
            Assert.InRange(account.Balance, 0, 250);
            Assert.True(account.Balance % 5 == 0);
            Assert.InRange(account.WithdrawnToday, 0, 200);
            Assert.True(account.WithdrawnToday % 10 == 0);
            Assert.True(Domaine.Positif(module.Stock) && Domaine.TientDans(module.Stock, new NoteCounts(5, 5, 5)));
            if (module.Jammed is { } jammed) Assert.Equal(jammedCount, module.Stock.Of(jammed));
        }
    }

    // ======== Le module de billets (contrat de IMoneyDispenser) ========

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Module_Dispense_ToutOuRien(Situation s, Billets b)
    {
        var module = s.Module();
        var notes = b.Notes;
        var accepted = notes.Total > 0
                       && notes.Total <= 4
                       && (s.Jammed is not { } j || notes.Of(j) == 0)
                       && Domaine.TientDans(notes, s.Stock);

        var ok = module.Dispense(notes);

        Assert.Equal(accepted, ok);
        Assert.Equal(ok ? s.Stock - notes : s.Stock, module.Stock);
        Assert.Equal(s.Jammed, module.Jammed);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Module_Load_ToutOuRien(Situation s, Billets b)
    {
        var module = s.Module();
        var notes = b.Notes;
        var accepted = notes.Total > 0
                       && (s.Jammed is not { } j || notes.Of(j) == 0)
                       && Domaine.TientDans(s.Stock + notes, new NoteCounts(5, 5, 5));

        var ok = module.Load(notes);

        Assert.Equal(accepted, ok);
        Assert.Equal(ok ? s.Stock + notes : s.Stock, module.Stock);
        Assert.Equal(s.Jammed, module.Jammed);
    }

    [Property(Replay = "42,1", MaxTest = 200, Arbitrary = [typeof(Domaine)])]
    public void Module_DispensePuisLoad_RestaureLeStock(Situation s, Billets b)
    {
        var module = s.Module();

        if (!module.Dispense(b.Notes)) return;

        Assert.True(module.Load(b.Notes));
        Assert.Equal(s.Stock, module.Stock);
    }
}
