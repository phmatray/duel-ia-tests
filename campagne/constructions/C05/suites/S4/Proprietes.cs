using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Atm.Tests;

/// <summary>
/// Tests de propriétés : des règles de la spécification qui doivent rester vraies pour toutes les entrées du domaine,
/// vérifiées sur des cas tirés au hasard. Graine et nombre de cas fixés sur chaque propriété pour la reproductibilité.
/// </summary>
[Properties(Arbitrary = [typeof(Generateurs)])]
public class Proprietes
{
    // ---------- Les entrées du domaine ----------

    /// <summary>Le module de billets : un stock et une cassette bloquée (ou aucune).</summary>
    public sealed record Module(NoteCounts Stock, Denomination? Jam)
    {
        public MoneyDispenser Real() => new(Stock, Jam);

        internal RecordingDispenser Recording(int? failDispenseOnCall = null, bool failLoad = false) =>
            new(Stock, Jam) { FailDispenseOnCall = failDispenseOnCall, FailLoad = failLoad };

        public override string ToString() =>
            $"stock 50×{Stock.Fifties} 20×{Stock.Twenties} 10×{Stock.Tens}, bloquée {Jam?.ToString() ?? "aucune"}";
    }

    /// <summary>Un montant de retrait du domaine : -10, 0, 5, et les multiples de 10 de 10 à 220.</summary>
    public sealed record Montant(int Value);

    /// <summary>
    /// Un retrait du domaine : un compte et un montant. Un tiers des cas est tiré uniformément, les autres se placent
    /// autour des frontières du plafond du jour et du solde (frais compris), là où une erreur d'un cran se voit.
    /// </summary>
    public sealed record Retrait(AccountState Account, int Amount);

    /// <summary>
    /// Un compte et un montant qui passent les trois premiers contrôles du retrait : seul le contrôle des billets reste.
    /// Sert à obtenir beaucoup de retraits réussis.
    /// </summary>
    public sealed record RetraitAutorise(AccountState Account, int Amount);

    /// <summary>Un dépôt du domaine : 0 ou 1 billet par coupure.</summary>
    public sealed record Depot(NoteCounts Notes);

    /// <summary>Le numéro (de 1 à 4) de l'appel à <c>Dispense</c> où le module tombe en panne.</summary>
    public sealed record Passage(int Number);

    /// <summary>Une demande faite directement au module (0 à 5 billets par coupure).</summary>
    public sealed record Demande(NoteCounts Notes);

    public static class Generateurs
    {
        private static readonly Gen<Denomination?> Jams =
            Gen.Elements<Denomination?>(null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten);

        private static Gen<NoteCounts> Counts(int max) =>
            from f in Gen.Choose(0, max)
            from t in Gen.Choose(0, max)
            from c in Gen.Choose(0, max)
            select new NoteCounts(f, t, c);

        public static Arbitrary<AccountState> Comptes() => Arb.From(
            from balance in Gen.Choose(0, 50)
            from withdrawn in Gen.Choose(0, 20)
            select new AccountState(balance * 5, withdrawn * 10));

        public static Arbitrary<Module> Modules() => Arb.From(
            from stock in Counts(5)
            from jam in Jams
            select new Module(stock, jam));

        public static Arbitrary<Montant> Montants() => Arb.From(
            Gen.Elements([-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)]).Select(a => new Montant(a)));

        public static Arbitrary<Retrait> Retraits() => Arb.From(Gen.OneOf(
            from account in Comptes().Generator
            from amount in Montants().Generator
            select new Retrait(account, amount.Value),
            // Autour du plafond du jour : déjà retiré = 200 − montant, à un cran près.
            from amount in Gen.Choose(1, 20).Select(i => i * 10)
            from step in Gen.Choose(-1, 1)
            from balance in Gen.Choose(0, 50).Select(i => i * 5)
            select new Retrait(new AccountState(balance, Math.Clamp(200 - amount + 10 * step, 0, 200)), amount),
            // Autour du solde : solde = montant + frais, à un cran près, sous le plafond.
            from amount in Gen.Choose(1, 20).Select(i => i * 10)
            from step in Gen.Choose(-1, 1)
            from withdrawn in Gen.Choose(0, (200 - amount) / 10).Select(i => i * 10)
            select new Retrait(new AccountState(Math.Clamp(amount + Fee(amount) + 5 * step, 0, 250), withdrawn), amount)));

        public static Arbitrary<RetraitAutorise> RetraitsAutorises() => Arb.From(
            from amount in Gen.Choose(1, 20).Select(i => i * 10)
            from withdrawn in Gen.Choose(0, (200 - amount) / 10).Select(i => i * 10)
            from balance in Gen.Choose((amount + Fee(amount)) / 5, 50).Select(i => i * 5)
            select new RetraitAutorise(new AccountState(balance, withdrawn), amount));

        public static Arbitrary<Depot> Depots() => Arb.From(Counts(1).Select(n => new Depot(n)));

        public static Arbitrary<Passage> Passages() => Arb.From(Gen.Choose(1, 4).Select(n => new Passage(n)));

        public static Arbitrary<Demande> Demandes() => Arb.From(Counts(5).Select(n => new Demande(n)));

        public static Arbitrary<Operation> Operations() => Arb.From(Gen.OneOf(
            Montants().Generator.Select(m => (Operation)new Operation.Withdraw(m.Value)),
            Depots().Generator.Select(d => (Operation)new Operation.Deposit(d.Notes))));
    }

    // ---------- Outils ----------

    private static readonly Denomination[] AllDenominations = [Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static int Fee(int amount) => amount > 100 ? 5 : 0;

    private static NoteCounts Usable(Module module) => Without(module.Stock, module.Jam);

    private static NoteCounts Without(NoteCounts notes, Denomination? denomination) => new(
        denomination == Denomination.Fifty ? 0 : notes.Fifties,
        denomination == Denomination.Twenty ? 0 : notes.Twenties,
        denomination == Denomination.Ten ? 0 : notes.Tens);

    private static NoteCounts Single(Denomination denomination) => denomination switch
    {
        Denomination.Fifty => new NoteCounts(1, 0, 0),
        Denomination.Twenty => new NoteCounts(0, 1, 0),
        _ => new NoteCounts(0, 0, 1),
    };

    private static bool FitsIn(NoteCounts notes, NoteCounts stock) =>
        notes.Fifties <= stock.Fifties && notes.Twenties <= stock.Twenties && notes.Tens <= stock.Tens;

    private static bool WithinCapacity(NoteCounts stock) => FitsIn(stock, new NoteCounts(5, 5, 5));

    /// <summary>Toutes les combinaisons de billets pris dans <paramref name="available"/> qui font exactement le montant.</summary>
    private static List<NoteCounts> Combinations(int amount, NoteCounts available) => (
        from f in Enumerable.Range(0, available.Fifties + 1)
        from t in Enumerable.Range(0, available.Twenties + 1)
        from c in Enumerable.Range(0, available.Tens + 1)
        let notes = new NoteCounts(f, t, c)
        where notes.Value == amount
        select notes).ToList();

    private static NoteCounts Sum(IEnumerable<NoteCounts> notes) => notes.Aggregate(NoteCounts.None, (a, b) => a + b);

    private static AtmResult Run(Module module, AccountState account, Operation operation) =>
        new CashMachine(module.Real()).Execute(account, operation);

    private static AtmResult Withdraw(Module module, AccountState account, int amount) =>
        Run(module, account, new Operation.Withdraw(amount));

    // ---------- Retrait ----------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Le_statut_du_retrait_est_le_premier_controle_qui_echoue(Retrait retrait, Module module)
    {
        var (account, amount) = (retrait.Account, retrait.Amount);

        var actual = Withdraw(module, account, amount).Status;

        var expected =
            amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
            : account.WithdrawnToday + amount > 200 ? AtmStatus.DailyLimitExceeded
            : amount + Fee(amount) > account.Balance ? AtmStatus.InsufficientFunds
            : Combinations(amount, Usable(module)).Count == 0 ? AtmStatus.CannotDispense
            : AtmStatus.Ok;
        Assert.Equal(expected, actual);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Un_retrait_autorise_echoue_seulement_faute_de_combinaison(RetraitAutorise retrait, Module module)
    {
        var result = Withdraw(module, retrait.Account, retrait.Amount);

        var possible = Combinations(retrait.Amount, Usable(module)).Count > 0;
        Assert.Equal(possible ? AtmStatus.Ok : AtmStatus.CannotDispense, result.Status);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Un_retrait_reussi_rend_exactement_le_montant_avec_les_billets_disponibles(RetraitAutorise retrait, Module module)
    {
        var result = Withdraw(module, retrait.Account, retrait.Amount);
        if (result.Status != AtmStatus.Ok) return;

        Assert.Equal(retrait.Amount, result.Notes.Value);
        Assert.True(FitsIn(result.Notes, Usable(module)), $"{result.Notes} ne tient pas dans les billets disponibles");
        if (module.Jam is { } jam) Assert.Equal(0, result.Notes.Of(jam));
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Un_retrait_reussi_rend_l_unique_combinaison_la_plus_courte(RetraitAutorise retrait, Module module)
    {
        var result = Withdraw(module, retrait.Account, retrait.Amount);
        if (result.Status != AtmStatus.Ok) return;

        foreach (var other in Combinations(retrait.Amount, Usable(module)).Where(c => c != result.Notes))
        {
            Assert.True(other.Total > result.Notes.Total, $"{other} compte au plus autant de billets que {result.Notes}");
        }
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Un_retrait_reussi_debite_montant_et_frais_et_compte_le_montant_dans_le_plafond(RetraitAutorise retrait, Module module)
    {
        var (account, amount) = (retrait.Account, retrait.Amount);

        var result = Withdraw(module, account, amount);
        if (result.Status != AtmStatus.Ok) return;

        Assert.Equal(Fee(amount), result.Fee);
        Assert.Equal(account.Balance - amount - result.Fee, result.Account.Balance);
        Assert.Equal(account.WithdrawnToday + amount, result.Account.WithdrawnToday);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Les_frais_ne_sont_preleves_que_sur_un_retrait_reussi_de_plus_de_100(Retrait retrait, Module module)
    {
        var result = Withdraw(module, retrait.Account, retrait.Amount);

        var expected = result.Status == AtmStatus.Ok && retrait.Amount > 100 ? 5 : 0;
        Assert.Equal(expected, result.Fee);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Les_billets_sortent_en_passages_d_au_plus_quatre_billets(RetraitAutorise retrait, Module module)
    {
        var dispenser = module.Recording();

        var result = new CashMachine(dispenser).Execute(retrait.Account, new Operation.Withdraw(retrait.Amount));

        Assert.Empty(dispenser.LoadCalls);
        Assert.All(dispenser.DispenseCalls, pass => Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(result.Notes, Sum(dispenser.DispenseCalls));
        // « Autant de passages que nécessaire » : pas de passage superflu.
        Assert.Equal((result.Notes.Total + 3) / 4, dispenser.DispenseCalls.Count);
    }

    // ---------- Dépôt ----------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Le_statut_du_depot_est_le_premier_controle_qui_echoue(AccountState account, Module module, Depot depot)
    {
        var notes = depot.Notes;

        var actual = Run(module, account, new Operation.Deposit(notes)).Status;

        var expected =
            notes.Total == 0 ? AtmStatus.InvalidAmount
            : module.Jam is { } jam && notes.Of(jam) > 0 ? AtmStatus.CassetteJammed
            : !WithinCapacity(module.Stock + notes) ? AtmStatus.CassetteFull
            : AtmStatus.Ok;
        Assert.Equal(expected, actual);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Un_depot_reussi_credite_sa_valeur_et_range_les_billets(AccountState account, Module module, Depot depot)
    {
        var dispenser = module.Recording();

        var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(depot.Notes));
        if (result.Status != AtmStatus.Ok) return;

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + depot.Notes.Value }), result);
        Assert.Equal(module.Stock + depot.Notes, dispenser.Stock);
        Assert.Equal([depot.Notes], dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Deux_depots_de_coupures_differentes_valent_un_depot_groupe(AccountState account, Module module, Depot first, Depot second)
    {
        // On rend les deux dépôts non vides et disjoints : leur somme reste alors un dépôt du domaine.
        var a = first.Notes.Total == 0 ? new NoteCounts(1, 0, 0) : first.Notes;
        if (a.Total == 3) a = a with { Tens = 0 };
        var b = new NoteCounts(
            a.Fifties == 1 ? 0 : second.Notes.Fifties,
            a.Twenties == 1 ? 0 : second.Notes.Twenties,
            a.Tens == 1 ? 0 : second.Notes.Tens);
        if (b.Total == 0) b = Single(AllDenominations.First(d => a.Of(d) == 0));

        var grouped = module.Real();
        var together = new CashMachine(grouped).Execute(account, new Operation.Deposit(a + b));

        var separate = module.Real();
        var machine = new CashMachine(separate);
        var r1 = machine.Execute(account, new Operation.Deposit(a));
        var r2 = machine.Execute(r1.Account, new Operation.Deposit(b));

        var bothOk = r1.Status == AtmStatus.Ok && r2.Status == AtmStatus.Ok;
        Assert.Equal(bothOk, together.Status == AtmStatus.Ok);
        if (bothOk)
        {
            Assert.Equal(r2.Account, together.Account);
            Assert.Equal(separate.Stock, grouped.Stock);
        }
    }

    // ---------- Retrait et dépôt ----------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Un_refus_ne_change_rien(AccountState account, Module module, Operation operation)
    {
        var dispenser = module.Recording();

        var result = new CashMachine(dispenser).Execute(account, operation);
        if (result.Status == AtmStatus.Ok) return;

        Assert.Equal(new AtmResult(result.Status, NoteCounts.None, 0, account), result);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
        Assert.Equal(module.Stock, dispenser.Stock);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Le_vrai_module_ne_tombe_jamais_en_panne_et_chaque_operation_a_ses_statuts(AccountState account, Module module, Operation operation)
    {
        var status = Run(module, account, operation).Status;

        AtmStatus[] allowed = operation is Operation.Withdraw
            ? [AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.DailyLimitExceeded, AtmStatus.InsufficientFunds, AtmStatus.CannotDispense]
            : [AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.CassetteJammed, AtmStatus.CassetteFull];
        Assert.Contains(status, allowed);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Les_billets_sont_conserves_entre_le_module_et_le_client(AccountState account, Module module, Operation operation)
    {
        var dispenser = module.Real();

        var result = new CashMachine(dispenser).Execute(account, operation);

        var expectedStock = result.Status != AtmStatus.Ok ? module.Stock
            : operation is Operation.Deposit deposit ? module.Stock + deposit.Notes
            : module.Stock - result.Notes;
        Assert.Equal(expectedStock, dispenser.Stock);
        Assert.Equal(module.Jam, dispenser.Jammed);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void L_argent_est_conserve_entre_le_compte_et_le_module(AccountState account, Module module, Operation operation)
    {
        // Solde − valeur du stock + frais prélevés : invariant, quelle que soit l'opération.
        var dispenser = module.Real();

        var result = new CashMachine(dispenser).Execute(account, operation);

        Assert.Equal(
            account.Balance - module.Stock.Value,
            result.Account.Balance - dispenser.Stock.Value + result.Fee);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Une_operation_garde_compte_et_stock_dans_des_valeurs_coherentes(AccountState account, Module module, Operation operation)
    {
        var dispenser = module.Real();

        var after = new CashMachine(dispenser).Execute(account, operation).Account;

        Assert.True(after.Balance >= 0 && after.Balance % 5 == 0, $"solde {after.Balance}");
        Assert.InRange(after.WithdrawnToday, 0, 200);
        Assert.Equal(0, after.WithdrawnToday % 10);
        Assert.True(FitsIn(NoteCounts.None, dispenser.Stock) && WithinCapacity(dispenser.Stock), $"stock {dispenser.Stock} hors de 0 à 5");
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void La_machine_est_sans_memoire(AccountState firstAccount, AccountState account, Module module, Operation first, Operation second)
    {
        // Une machine qui a déjà servi répond comme une machine neuve branchée sur un module dans le même état.
        var used = module.Real();
        var machine = new CashMachine(used);
        machine.Execute(firstAccount, first);
        var snapshot = new Module(used.Stock, used.Jammed);

        var fresh = snapshot.Real();
        var expected = new CashMachine(fresh).Execute(account, second);

        Assert.Equal(expected, machine.Execute(account, second));
        Assert.Equal(fresh.Stock, used.Stock);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Rendre_un_a_un_les_billets_retires_restaure_le_stock(RetraitAutorise retrait, Module module)
    {
        var dispenser = module.Real();
        var machine = new CashMachine(dispenser);
        var withdrawal = machine.Execute(retrait.Account, new Operation.Withdraw(retrait.Amount));
        if (withdrawal.Status != AtmStatus.Ok) return;

        // Chaque dépôt d'un seul billet reste dans le domaine.
        var account = withdrawal.Account;
        foreach (var denomination in AllDenominations)
        {
            for (var i = 0; i < withdrawal.Notes.Of(denomination); i++)
            {
                var deposit = machine.Execute(account, new Operation.Deposit(Single(denomination)));
                Assert.Equal(AtmStatus.Ok, deposit.Status);
                account = deposit.Account;
            }
        }

        Assert.Equal(module.Stock, dispenser.Stock);
        Assert.Equal(new AccountState(retrait.Account.Balance - withdrawal.Fee, retrait.Account.WithdrawnToday + retrait.Amount), account);
    }

    // ---------- Relations entre retraits ----------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Un_solde_plus_eleve_ne_change_pas_un_retrait_reussi(RetraitAutorise retrait, Module module, AccountState other)
    {
        var richer = retrait.Account with { Balance = Math.Max(retrait.Account.Balance, other.Balance) };

        var before = Withdraw(module, retrait.Account, retrait.Amount);
        if (before.Status != AtmStatus.Ok) return;
        var after = Withdraw(module, richer, retrait.Amount);

        Assert.Equal(AtmStatus.Ok, after.Status);
        Assert.Equal(before.Notes, after.Notes);
        Assert.Equal(before.Fee, after.Fee);
        Assert.Equal(richer.Balance - retrait.Account.Balance, after.Account.Balance - before.Account.Balance);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Avoir_moins_retire_aujourd_hui_ne_change_pas_un_retrait_reussi(RetraitAutorise retrait, Module module, AccountState other)
    {
        var lessWithdrawn = retrait.Account with { WithdrawnToday = Math.Min(retrait.Account.WithdrawnToday, other.WithdrawnToday) };

        var before = Withdraw(module, retrait.Account, retrait.Amount);
        if (before.Status != AtmStatus.Ok) return;
        var after = Withdraw(module, lessWithdrawn, retrait.Amount);

        Assert.Equal(AtmStatus.Ok, after.Status);
        Assert.Equal(before.Notes, after.Notes);
        Assert.Equal(before.Fee, after.Fee);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Ajouter_des_billets_au_stock_ne_fait_pas_echouer_un_retrait(RetraitAutorise retrait, Module module, Demande extra)
    {
        var fuller = module with
        {
            Stock = new NoteCounts(
                Math.Min(5, module.Stock.Fifties + extra.Notes.Fifties),
                Math.Min(5, module.Stock.Twenties + extra.Notes.Twenties),
                Math.Min(5, module.Stock.Tens + extra.Notes.Tens)),
        };

        var before = Withdraw(module, retrait.Account, retrait.Amount);
        if (before.Status != AtmStatus.Ok) return;
        var after = Withdraw(fuller, retrait.Account, retrait.Amount);

        Assert.Equal(AtmStatus.Ok, after.Status);
        Assert.True(after.Notes.Total <= before.Notes.Total, $"{after.Notes} compte plus de billets que {before.Notes}");
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Une_cassette_bloquee_compte_pour_zero_au_retrait(Retrait retrait, Module module)
    {
        var jammed = Withdraw(module, retrait.Account, retrait.Amount);
        var emptied = Withdraw(new Module(Usable(module), null), retrait.Account, retrait.Amount);

        Assert.Equal(emptied, jammed);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Bloquer_une_cassette_inutilisee_ne_change_pas_un_retrait_reussi(RetraitAutorise retrait, Module module)
    {
        var free = module with { Jam = null };

        var before = Withdraw(free, retrait.Account, retrait.Amount);
        if (before.Status != AtmStatus.Ok) return;

        foreach (var unused in AllDenominations.Where(d => before.Notes.Of(d) == 0))
        {
            Assert.Equal(before, Withdraw(free with { Jam = unused }, retrait.Account, retrait.Amount));
        }
    }

    // ---------- Pannes du module ----------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Une_panne_au_retrait_arrete_tout_et_rend_les_billets_deja_sortis(RetraitAutorise retrait, Module module, Passage passage)
    {
        var failOn = passage.Number;
        var reference = module.Recording();
        var normal = new CashMachine(reference).Execute(retrait.Account, new Operation.Withdraw(retrait.Amount));
        var dispenser = module.Recording(failDispenseOnCall: failOn);

        var result = new CashMachine(dispenser).Execute(retrait.Account, new Operation.Withdraw(retrait.Amount));

        if (failOn > reference.DispenseCalls.Count)
        {
            Assert.Equal(normal, result); // la panne n'arrive jamais
            return;
        }

        var alreadyOut = Sum(dispenser.DispenseCalls.Take(failOn - 1));
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, alreadyOut, 0, retrait.Account), result);
        Assert.Equal(failOn, dispenser.DispenseCalls.Count); // on s'arrête aussitôt
        Assert.Equal(module.Stock - alreadyOut, dispenser.Stock);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Une_panne_au_depot_ne_rend_rien_et_ne_change_pas_le_compte(AccountState account, Module module, Depot depot)
    {
        var normal = Run(module, account, new Operation.Deposit(depot.Notes));
        var dispenser = module.Recording(failLoad: true);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(depot.Notes));

        if (normal.Status != AtmStatus.Ok)
        {
            Assert.Equal(normal, result); // les contrôles passent avant le module
            Assert.Empty(dispenser.LoadCalls);
            return;
        }

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal([depot.Notes], dispenser.LoadCalls);
        Assert.Equal(module.Stock, dispenser.Stock);
    }

    // ---------- Le contrat du module de billets ----------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Dispense_sort_tout_ou_rien(Module module, Demande demande)
    {
        var notes = demande.Notes;
        var dispenser = module.Real();

        var accepted = dispenser.Dispense(notes);

        var expected = notes.Total is > 0 and <= 4
            && (module.Jam is not { } jam || notes.Of(jam) == 0)
            && FitsIn(notes, module.Stock);
        Assert.Equal(expected, accepted);
        Assert.Equal(accepted ? module.Stock - notes : module.Stock, dispenser.Stock);
        Assert.Equal(module.Jam, dispenser.Jammed);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Load_range_tout_ou_rien(Module module, Demande demande)
    {
        var notes = demande.Notes;
        var dispenser = module.Real();

        var accepted = dispenser.Load(notes);

        var expected = notes.Total > 0
            && (module.Jam is not { } jam || notes.Of(jam) == 0)
            && WithinCapacity(module.Stock + notes);
        Assert.Equal(expected, accepted);
        Assert.Equal(accepted ? module.Stock + notes : module.Stock, dispenser.Stock);
        Assert.Equal(module.Jam, dispenser.Jammed);
    }
}
