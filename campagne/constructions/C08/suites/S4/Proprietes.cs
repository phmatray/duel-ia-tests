using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Atm.Tests;

/// <summary>
/// Tests de propriétés (FsCheck) : des règles qui doivent rester vraies pour toute entrée du domaine de la spécification,
/// vérifiées sur 200 cas tirés au hasard avec une graine fixe. Les générateurs de <see cref="Domaine"/> ne produisent
/// que des valeurs du domaine ; certains visent une région précise (retrait qui passe les contrôles 1 à 3, retrait
/// réussi, aller-retour) pour que la propriété porte sur des cas utiles plutôt que d'être vraie par vacuité.
/// </summary>
public class Proprietes
{
    private const string Graine = "42,1"; // FsCheck 3 exige un gamma impair : "42,0" fait planter l'hôte de test.
    private const int Cas = 200;

    private const int Plafond = 200;
    private const int Capacite = 5;
    private const int SoldeMax = 250;

    private static readonly Denomination[] Coupures = [Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    // ===================================================================================================
    // Les valeurs générées.
    // ===================================================================================================

    /// <summary>Un compte, et le module de billets (stock, cassette bloquée) devant lequel il se présente.</summary>
    public sealed record Situation(AccountState Account, NoteCounts Stock, Denomination? Jammed);

    /// <summary>Un montant de retrait du domaine : -10, 0, 5, et les multiples de 10 de 10 à 220.</summary>
    public sealed record Montant(int Value);

    /// <summary>Un dépôt du domaine : 0 ou 1 billet par coupure (éventuellement vide).</summary>
    public sealed record Depot(NoteCounts Notes);

    /// <summary>Un retrait qui passe les contrôles 1 à 3 : seule la combinaison de billets peut encore le faire échouer.</summary>
    public sealed record RetraitServable(Situation Situation, int Amount);

    /// <summary>Un retrait qui passe les quatre contrôles : une combinaison exacte existe parmi les billets disponibles.</summary>
    public sealed record RetraitReussi(Situation Situation, int Amount);

    /// <summary>
    /// Deux états reliés par un dépôt <see cref="Notes"/> (non vide, 1 billet au plus par coupure) : l'état « bas »
    /// (stock <c>Stock - Notes</c>, solde <c>Balance - Notes.Value</c>) et l'état « haut » (stock <c>Stock</c>, solde
    /// <c>Balance</c>). Les deux restent dans le domaine, et un retrait de <c>Notes.Value</c> depuis l'un ou l'autre
    /// respecte le plafond du jour.
    /// </summary>
    public sealed record AllerRetour(AccountState Haut, NoteCounts Stock, Denomination? Jammed, NoteCounts Notes)
    {
        public AccountState Bas => Haut with { Balance = Haut.Balance - Notes.Value };

        public NoteCounts StockBas => Stock - Notes;
    }

    /// <summary>Deux dépôts non vides, sans coupure commune : leur réunion est encore un dépôt du domaine.</summary>
    public sealed record DepotsDisjoints(NoteCounts A, NoteCounts B);

    /// <summary>Une situation de départ et une suite d'opérations du domaine, exécutées sur un même module.</summary>
    public sealed record Session(Situation Start, Operation[] Operations);

    /// <summary>Un appel direct au module de billets : son stock, sa cassette bloquée, et les billets demandés.</summary>
    public sealed record AppelModule(NoteCounts Stock, Denomination? Jammed, NoteCounts Notes);

    /// <summary>Un passage que le module doit accepter : 1 à 4 billets, pris dans le stock, hors cassette bloquée.</summary>
    public sealed record PassageAcceptable(NoteCounts Stock, Denomination? Jammed, NoteCounts Notes);

    public static class Domaine
    {
        private static readonly int[] MontantsDuDomaine = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

        private static readonly Denomination?[] Bloquees =
            [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

        private static Gen<NoteCounts> Billets(int max) =>
            from f in Gen.Choose(0, max)
            from t in Gen.Choose(0, max)
            from n in Gen.Choose(0, max)
            select new NoteCounts(f, t, n);

        private static Gen<int> Multiples(int pas, int min, int max) =>
            Gen.Choose(min / pas, max / pas).Select(i => i * pas);

        private static readonly Gen<NoteCounts> StockGen = Billets(Capacite);

        private static readonly Gen<NoteCounts> DepotGen = Billets(1);

        private static readonly Gen<Denomination?> BloqueeGen = Gen.Elements(Bloquees);

        private static readonly Gen<AccountState> CompteGen =
            from balance in Multiples(5, 0, SoldeMax)
            from withdrawn in Multiples(10, 0, Plafond)
            select new AccountState(balance, withdrawn);

        private static readonly Gen<Situation> SituationGen =
            from account in CompteGen
            from stock in StockGen
            from jammed in BloqueeGen
            select new Situation(account, stock, jammed);

        private static readonly Gen<Operation> OperationGen = Gen.OneOf(
            Gen.Elements(MontantsDuDomaine).Select(amount => (Operation)new Operation.Withdraw(amount)),
            DepotGen.Select(notes => (Operation)new Operation.Deposit(notes)));

        private static readonly Gen<RetraitServable> ServableGen =
            from amount in Multiples(10, 10, Plafond)
            from withdrawn in Multiples(10, 0, Plafond - amount)
            from balance in Multiples(5, amount + Frais(amount), SoldeMax)
            from stock in StockGen
            from jammed in BloqueeGen
            select new RetraitServable(new Situation(new AccountState(balance, withdrawn), stock, jammed), amount);

        public static Arbitrary<Situation> Situations() => Arb.From(SituationGen);

        public static Arbitrary<Montant> Montants() => Arb.From(Gen.Elements(MontantsDuDomaine).Select(m => new Montant(m)));

        public static Arbitrary<Depot> Depots() => Arb.From(DepotGen.Select(notes => new Depot(notes)));

        public static Arbitrary<Operation> Operations() => Arb.From(OperationGen);

        public static Arbitrary<RetraitServable> Servables() => Arb.From(ServableGen);

        public static Arbitrary<RetraitReussi> Reussis() => Arb.From(
            from r in ServableGen
            where Combinaisons(Disponibles(r.Situation.Stock, r.Situation.Jammed), r.Amount).Any()
            select new RetraitReussi(r.Situation, r.Amount));

        public static Arbitrary<AllerRetour> AllersRetours() => Arb.From(
            from notes in DepotGen
            where notes.Total > 0
            from jammed in BloqueeGen
            where jammed is not { } j || notes.Of(j) == 0
            from f in Gen.Choose(notes.Fifties, Capacite)
            from t in Gen.Choose(notes.Twenties, Capacite)
            from n in Gen.Choose(notes.Tens, Capacite)
            from withdrawn in Multiples(10, 0, Plafond - notes.Value)
            from balance in Multiples(5, notes.Value, SoldeMax)
            select new AllerRetour(new AccountState(balance, withdrawn), new NoteCounts(f, t, n), jammed, notes));

        public static Arbitrary<DepotsDisjoints> Disjoints() => Arb.From(
            from choix in Gen.ArrayOf(Gen.Choose(0, 2), 3)
            let a = new NoteCounts(choix[0] == 1 ? 1 : 0, choix[1] == 1 ? 1 : 0, choix[2] == 1 ? 1 : 0)
            let b = new NoteCounts(choix[0] == 2 ? 1 : 0, choix[1] == 2 ? 1 : 0, choix[2] == 2 ? 1 : 0)
            where a.Total > 0 && b.Total > 0
            select new DepotsDisjoints(a, b));

        public static Arbitrary<Session> Sessions() => Arb.From(
            from start in SituationGen
            from length in Gen.Choose(1, 30)
            from operations in Gen.ArrayOf(OperationGen, length)
            select new Session(start, operations));

        public static Arbitrary<AppelModule> AppelsModule() => Arb.From(
            from stock in StockGen
            from jammed in BloqueeGen
            from notes in StockGen
            select new AppelModule(stock, jammed, notes));

        public static Arbitrary<PassageAcceptable> PassagesAcceptables() => Arb.From(
            from stock in StockGen
            from jammed in BloqueeGen
            let available = Disponibles(stock, jammed)
            where available.Total > 0
            from f in Gen.Choose(0, available.Fifties)
            from t in Gen.Choose(0, available.Twenties)
            from n in Gen.Choose(0, available.Tens)
            let notes = new NoteCounts(f, t, n)
            where notes.Total is >= 1 and <= MoneyDispenser.MaxNotesPerPass
            select new PassageAcceptable(stock, jammed, notes));
    }

    // ===================================================================================================
    // Propriétés générales : toute situation, toute opération du domaine.
    // ===================================================================================================

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Real_module_never_faults_in_the_domain(Situation situation, Operation operation)
    {
        var (result, _) = Run(situation, operation);

        Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_refusal_changes_nothing_and_moves_no_note(Situation situation, Operation operation)
    {
        var spy = new Espion(new MoneyDispenser(situation.Stock, situation.Jammed));
        var result = new CashMachine(spy).Execute(situation.Account, operation);

        if (result.Status == AtmStatus.Ok)
        {
            return;
        }

        Assert.Equal(new AtmResult(result.Status, NoteCounts.None, 0, situation.Account), result);
        Assert.Equal(situation.Stock, spy.Stock);
        Assert.DoesNotContain(spy.Calls, call => call.Accepted);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Value_is_conserved_between_module_account_and_fee(Situation situation, Operation operation)
    {
        var (result, dispenser) = Run(situation, operation);

        // Ce qui sort du module quitte le compte, frais en plus ; ce qui y entre arrive sur le compte.
        Assert.Equal(situation.Stock.Value - situation.Account.Balance,
            dispenser.Stock.Value - result.Account.Balance - result.Fee);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Notes_returned_are_exactly_the_notes_that_left_the_module(Situation situation, Operation operation)
    {
        var (result, dispenser) = Run(situation, operation);

        switch (operation)
        {
            case Operation.Withdraw:
                Assert.Equal(situation.Stock - dispenser.Stock, result.Notes);
                break;
            case Operation.Deposit deposit:
                Assert.Equal(NoteCounts.None, result.Notes);
                var expectedStock = result.Status == AtmStatus.Ok ? situation.Stock + deposit.Notes : situation.Stock;
                Assert.Equal(expectedStock, dispenser.Stock);
                break;
        }
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Module_and_account_stay_consistent_after_any_operation(Situation situation, Operation operation)
    {
        var (result, dispenser) = Run(situation, operation);

        AssertStockInBounds(dispenser.Stock);
        Assert.Equal(situation.Jammed, dispenser.Jammed);
        if (situation.Jammed is { } jammed)
        {
            Assert.Equal(situation.Stock.Of(jammed), dispenser.Stock.Of(jammed));
        }

        Assert.InRange(result.Account.Balance, 0, int.MaxValue);
        Assert.Equal(0, result.Account.Balance % 5);
        Assert.InRange(result.Account.WithdrawnToday, situation.Account.WithdrawnToday, Plafond);
        Assert.Equal(0, result.Account.WithdrawnToday % 10);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Fee_is_five_exactly_for_successful_withdrawals_above_one_hundred(Situation situation, Operation operation)
    {
        var (result, _) = Run(situation, operation);

        var expected = operation is Operation.Withdraw { Amount: > 100 } && result.Status == AtmStatus.Ok ? 5 : 0;
        Assert.Equal(expected, result.Fee);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Withdrawals_never_load_and_deposits_never_dispense(Situation situation, Operation operation)
    {
        var spy = new Espion(new MoneyDispenser(situation.Stock, situation.Jammed));
        new CashMachine(spy).Execute(situation.Account, operation);

        var forbidden = operation is Operation.Withdraw ? nameof(IMoneyDispenser.Load) : nameof(IMoneyDispenser.Dispense);
        Assert.DoesNotContain(spy.Calls, call => call.Method == forbidden);
    }

    // ===================================================================================================
    // Le retrait.
    // ===================================================================================================

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Withdraw_status_is_given_by_the_first_failing_check(Situation situation, Montant montant)
    {
        var amount = montant.Value;
        var account = situation.Account;
        var expected =
            amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
            : account.WithdrawnToday + amount > Plafond ? AtmStatus.DailyLimitExceeded
            : amount + Frais(amount) > account.Balance ? AtmStatus.InsufficientFunds
            : !Combinaisons(Disponibles(situation.Stock, situation.Jammed), amount).Any() ? AtmStatus.CannotDispense
            : AtmStatus.Ok;

        var (result, _) = Run(situation, new Operation.Withdraw(amount));

        Assert.Equal(expected, result.Status);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_serviceable_withdrawal_fails_only_when_no_exact_combination_exists(RetraitServable retrait)
    {
        var (result, _) = Run(retrait.Situation, new Operation.Withdraw(retrait.Amount));
        var exists = Combinaisons(Disponibles(retrait.Situation.Stock, retrait.Situation.Jammed), retrait.Amount).Any();

        Assert.Equal(exists ? AtmStatus.Ok : AtmStatus.CannotDispense, result.Status);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Checks_one_to_three_depend_only_on_account_and_amount(Situation situation, Situation other, Montant montant)
    {
        // Même compte, même montant, autre module : les contrôles 1 à 3 ne regardent pas les billets.
        var operation = new Operation.Withdraw(montant.Value);
        var (result, _) = Run(situation, operation);
        var (otherResult, _) = Run(other with { Account = situation.Account }, operation);

        AtmStatus[] accountChecks = [AtmStatus.InvalidAmount, AtmStatus.DailyLimitExceeded, AtmStatus.InsufficientFunds];
        if (accountChecks.Contains(result.Status) || accountChecks.Contains(otherResult.Status))
        {
            Assert.Equal(result.Status, otherResult.Status);
        }
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_successful_withdrawal_updates_the_account_as_specified(RetraitReussi retrait)
    {
        var (situation, amount) = (retrait.Situation, retrait.Amount);
        var (result, _) = Run(situation, new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(amount, result.Notes.Value);
        Assert.Equal(Frais(amount), result.Fee);
        Assert.Equal(new AccountState(situation.Account.Balance - amount - Frais(amount),
            situation.Account.WithdrawnToday + amount), result.Account);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_successful_withdrawal_takes_only_available_notes(RetraitReussi retrait)
    {
        var situation = retrait.Situation;
        var (result, _) = Run(situation, new Operation.Withdraw(retrait.Amount));

        Assert.True(Contains(Disponibles(situation.Stock, situation.Jammed), result.Notes),
            $"{result.Notes} n'est pas pris dans les billets disponibles");
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_successful_withdrawal_gives_the_unique_combination_with_fewest_notes(RetraitReussi retrait)
    {
        var situation = retrait.Situation;
        var (result, _) = Run(situation, new Operation.Withdraw(retrait.Amount));

        foreach (var other in Combinaisons(Disponibles(situation.Stock, situation.Jammed), retrait.Amount))
        {
            Assert.True(other == result.Notes || other.Total > result.Notes.Total,
                $"{other} fait aussi {retrait.Amount} avec {other.Total} billets, contre {result.Notes.Total}");
        }
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_successful_withdrawal_is_dispensed_in_passes_of_one_to_four_notes(RetraitReussi retrait)
    {
        var situation = retrait.Situation;
        var spy = new Espion(new MoneyDispenser(situation.Stock, situation.Jammed));
        var result = new CashMachine(spy).Execute(situation.Account, new Operation.Withdraw(retrait.Amount));

        Assert.NotEmpty(spy.Calls);
        var total = NoteCounts.None;
        foreach (var call in spy.Calls)
        {
            Assert.Equal(nameof(IMoneyDispenser.Dispense), call.Method);
            Assert.True(call.Accepted);
            Assert.InRange(call.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass);
            total += call.Notes;
        }

        Assert.Equal(result.Notes, total);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void More_notes_in_the_module_never_turn_a_success_into_a_refusal(RetraitReussi retrait, Situation other)
    {
        var situation = retrait.Situation;
        var operation = new Operation.Withdraw(retrait.Amount);
        var (result, _) = Run(situation, operation);

        var richer = new NoteCounts(
            Math.Min(Capacite, situation.Stock.Fifties + other.Stock.Fifties),
            Math.Min(Capacite, situation.Stock.Twenties + other.Stock.Twenties),
            Math.Min(Capacite, situation.Stock.Tens + other.Stock.Tens));
        var (richerResult, _) = Run(situation with { Stock = richer }, operation);

        Assert.Equal(AtmStatus.Ok, richerResult.Status);
        Assert.Equal(result.Fee, richerResult.Fee);
        Assert.Equal(result.Account, richerResult.Account);
        Assert.InRange(richerResult.Notes.Total, 0, result.Notes.Total);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public Property Unjamming_a_cassette_never_turns_a_success_into_a_refusal(RetraitReussi retrait)
    {
        var situation = retrait.Situation;

        return Prop.When(situation.Jammed is not null, () =>
        {
            var operation = new Operation.Withdraw(retrait.Amount);
            var (result, _) = Run(situation, operation);
            var (unjammed, _) = Run(situation with { Jammed = null }, operation);

            Assert.Equal(AtmStatus.Ok, unjammed.Status);
            Assert.Equal(result.Account, unjammed.Account);
            Assert.InRange(unjammed.Notes.Total, 0, result.Notes.Total);
        });
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Notes_left_unused_by_the_combination_do_not_matter(RetraitReussi retrait, Situation other)
    {
        var situation = retrait.Situation;
        var operation = new Operation.Withdraw(retrait.Amount);
        var (result, _) = Run(situation, operation);
        var notes = result.Notes;

        // Un stock quelconque entre la combinaison et le stock d'origine donne le même résultat...
        int Between(Denomination d) =>
            notes.Of(d) + other.Stock.Of(d) % (Disponibles(situation.Stock, situation.Jammed).Of(d) - notes.Of(d) + 1);
        var poorer = new NoteCounts(Between(Denomination.Fifty), Between(Denomination.Twenty), Between(Denomination.Ten));
        if (situation.Jammed is { } jammed)
        {
            poorer = WithCount(poorer, jammed, situation.Stock.Of(jammed));
        }

        var (poorerResult, _) = Run(situation with { Stock = poorer }, operation);
        Assert.Equal(result, poorerResult);

        // ... de même que bloquer une cassette dont la combinaison ne prend rien.
        if (situation.Jammed is null)
        {
            foreach (var unused in Coupures.Where(d => notes.Of(d) == 0))
            {
                var (jammedResult, _) = Run(situation with { Jammed = unused }, operation);
                Assert.Equal(result, jammedResult);
            }
        }
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_richer_account_with_more_room_today_gets_the_same_withdrawal(RetraitReussi retrait, Situation other)
    {
        var situation = retrait.Situation;
        var account = situation.Account;
        var operation = new Operation.Withdraw(retrait.Amount);
        var (result, _) = Run(situation, operation);

        var richer = new AccountState(
            account.Balance + 5 * (other.Account.Balance / 5 % ((SoldeMax - account.Balance) / 5 + 1)),
            10 * (other.Account.WithdrawnToday / 10 % (account.WithdrawnToday / 10 + 1)));
        var (richerResult, _) = Run(situation with { Account = richer }, operation);

        Assert.Equal(AtmStatus.Ok, richerResult.Status);
        Assert.Equal(result.Notes, richerResult.Notes);
        Assert.Equal(result.Fee, richerResult.Fee);
        Assert.Equal(account.Balance - result.Account.Balance, richer.Balance - richerResult.Account.Balance);
        Assert.Equal(result.Account.WithdrawnToday - account.WithdrawnToday,
            richerResult.Account.WithdrawnToday - richer.WithdrawnToday);
    }

    // ===================================================================================================
    // Le dépôt.
    // ===================================================================================================

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Deposit_status_is_given_by_the_first_failing_check(Situation situation, Depot depot)
    {
        var notes = depot.Notes;
        var after = situation.Stock + notes;
        var expected =
            notes.Total == 0 ? AtmStatus.InvalidAmount
            : situation.Jammed is { } jammed && notes.Of(jammed) > 0 ? AtmStatus.CassetteJammed
            : Coupures.Any(d => after.Of(d) > Capacite) ? AtmStatus.CassetteFull
            : AtmStatus.Ok;

        var (result, _) = Run(situation, new Operation.Deposit(notes));

        Assert.Equal(expected, result.Status);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_successful_deposit_loads_the_notes_and_credits_their_value(AllerRetour cas)
    {
        var (result, dispenser) = Run(new Situation(cas.Bas, cas.StockBas, cas.Jammed), new Operation.Deposit(cas.Notes));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, cas.Haut), result);
        Assert.Equal(cas.Stock, dispenser.Stock);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_deposit_does_not_depend_on_the_account(Situation situation, Situation other, Depot depot)
    {
        var operation = new Operation.Deposit(depot.Notes);
        var (result, dispenser) = Run(situation, operation);
        var (otherResult, otherDispenser) = Run(situation with { Account = other.Account }, operation);

        Assert.Equal(result.Status, otherResult.Status);
        Assert.Equal(result.Notes, otherResult.Notes);
        Assert.Equal(result.Fee, otherResult.Fee);
        Assert.Equal(dispenser.Stock, otherDispenser.Stock);
        Assert.Equal(result.Account.Balance - situation.Account.Balance, otherResult.Account.Balance - other.Account.Balance);
        Assert.Equal(other.Account.WithdrawnToday, otherResult.Account.WithdrawnToday);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Depositing_two_disjoint_parts_in_either_order_equals_depositing_them_together(
        Situation situation, DepotsDisjoints depots)
    {
        // Le solde est borné pour que le compte rendu après le premier dépôt reste dans le domaine.
        var together = depots.A + depots.B;
        var start = situation with
        {
            Account = situation.Account with { Balance = Math.Min(situation.Account.Balance, SoldeMax - together.Value) },
        };

        var (once, onceDispenser) = Run(start, new Operation.Deposit(together));

        foreach (var (first, second) in new[] { (depots.A, depots.B), (depots.B, depots.A) })
        {
            var dispenser = new MoneyDispenser(start.Stock, start.Jammed);
            var machine = new CashMachine(dispenser);
            var r1 = machine.Execute(start.Account, new Operation.Deposit(first));
            var r2 = r1.Status == AtmStatus.Ok ? machine.Execute(r1.Account, new Operation.Deposit(second)) : r1;
            var bothOk = r1.Status == AtmStatus.Ok && r2.Status == AtmStatus.Ok;

            Assert.Equal(once.Status == AtmStatus.Ok, bothOk);
            if (bothOk)
            {
                Assert.Equal(once.Account, r2.Account);
                Assert.Equal(onceDispenser.Stock, dispenser.Stock);
            }
        }
    }

    // ===================================================================================================
    // Allers-retours entre dépôt et retrait.
    // ===================================================================================================

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Depositing_then_withdrawing_the_same_value_gives_back_the_same_notes(AllerRetour cas)
    {
        var dispenser = new MoneyDispenser(cas.StockBas, cas.Jammed);
        var machine = new CashMachine(dispenser);

        var deposit = machine.Execute(cas.Bas, new Operation.Deposit(cas.Notes));
        var withdraw = machine.Execute(deposit.Account, new Operation.Withdraw(cas.Notes.Value));

        // Un billet par coupure au plus : c'est toujours la combinaison la plus courte pour sa valeur.
        Assert.Equal(AtmStatus.Ok, deposit.Status);
        Assert.Equal(new AtmResult(AtmStatus.Ok, cas.Notes, 0,
            cas.Bas with { WithdrawnToday = cas.Bas.WithdrawnToday + cas.Notes.Value }), withdraw);
        Assert.Equal(cas.StockBas, dispenser.Stock);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Withdrawing_then_depositing_the_notes_back_restores_module_and_balance(AllerRetour cas)
    {
        var dispenser = new MoneyDispenser(cas.Stock, cas.Jammed);
        var machine = new CashMachine(dispenser);

        var withdraw = machine.Execute(cas.Haut, new Operation.Withdraw(cas.Notes.Value));
        var deposit = machine.Execute(withdraw.Account, new Operation.Deposit(withdraw.Notes));

        Assert.Equal(AtmStatus.Ok, withdraw.Status);
        Assert.Equal(cas.Notes, withdraw.Notes);
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0,
            cas.Haut with { WithdrawnToday = cas.Haut.WithdrawnToday + cas.Notes.Value }), deposit);
        Assert.Equal(cas.Stock, dispenser.Stock);
    }

    // ===================================================================================================
    // Sessions : une machine, un module, une suite d'opérations dont le compte rendu est réinjecté.
    // ===================================================================================================

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_session_keeps_value_stock_and_daily_total_consistent_at_every_step(Session session)
    {
        var start = session.Start;
        var dispenser = new MoneyDispenser(start.Stock, start.Jammed);
        var machine = new CashMachine(dispenser);
        var account = start.Account;
        var fees = 0;
        var withdrawnValue = 0;

        foreach (var operation in InDomain(session, () => account))
        {
            var result = machine.Execute(account, operation);

            Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
            account = result.Account;
            fees += result.Fee;
            if (operation is Operation.Withdraw)
            {
                withdrawnValue += result.Notes.Value;
            }

            Assert.Equal(start.Stock.Value - start.Account.Balance, dispenser.Stock.Value - account.Balance - fees);
            Assert.Equal(start.Account.WithdrawnToday + withdrawnValue, account.WithdrawnToday);
            Assert.InRange(account.WithdrawnToday, 0, Plafond);
            AssertStockInBounds(dispenser.Stock);
            if (start.Jammed is { } jammed)
            {
                Assert.Equal(start.Stock.Of(jammed), dispenser.Stock.Of(jammed));
            }
        }
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_machine_with_a_history_behaves_like_a_brand_new_one(Session session)
    {
        // Sans mémoire : après n'importe quel historique, la machine répond comme une machine neuve devant
        // un module neuf dans le même état.
        var start = session.Start;
        var dispenser = new MoneyDispenser(start.Stock, start.Jammed);
        var machine = new CashMachine(dispenser);
        var account = start.Account;

        foreach (var operation in InDomain(session, () => account))
        {
            var fresh = new MoneyDispenser(dispenser.Stock, start.Jammed);
            var expected = new CashMachine(fresh).Execute(account, operation);

            var result = machine.Execute(account, operation);

            Assert.Equal(expected, result);
            Assert.Equal(fresh.Stock, dispenser.Stock);
            account = result.Account;
        }
    }

    // ===================================================================================================
    // Pannes : le module rend false malgré les contrôles (hors du vrai module, mais le cas doit être traité).
    // ===================================================================================================

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_fault_during_a_withdrawal_stops_at_once_and_returns_the_notes_already_out(
        RetraitReussi retrait, NonNegativeInt choice)
    {
        var situation = retrait.Situation;
        var operation = new Operation.Withdraw(retrait.Amount);

        var spy = new Espion(new MoneyDispenser(situation.Stock, situation.Jammed));
        new CashMachine(spy).Execute(situation.Account, operation);
        var failingCall = 1 + choice.Get % spy.Calls.Count;

        var real = new MoneyDispenser(situation.Stock, situation.Jammed);
        var faulty = new EnPanne(real, failingCall, failLoad: false);
        var result = new CashMachine(faulty).Execute(situation.Account, operation);

        var alreadyOut = faulty.Dispensed.Aggregate(NoteCounts.None, (sum, pass) => sum + pass);
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, alreadyOut, 0, situation.Account), result);
        Assert.Equal(failingCall, faulty.DispenseCalls);
        Assert.Equal(failingCall - 1, faulty.Dispensed.Count);
        Assert.Equal(0, faulty.LoadCalls);
        Assert.Equal(situation.Stock - alreadyOut, real.Stock);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void A_fault_during_a_deposit_changes_nothing(AllerRetour cas)
    {
        var real = new MoneyDispenser(cas.StockBas, cas.Jammed);
        var faulty = new EnPanne(real, failingDispenseCall: 0, failLoad: true);
        var result = new CashMachine(faulty).Execute(cas.Bas, new Operation.Deposit(cas.Notes));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, cas.Bas), result);
        Assert.Equal(1, faulty.LoadCalls);
        Assert.Equal(0, faulty.DispenseCalls);
        Assert.Equal(cas.StockBas, real.Stock);
    }

    // ===================================================================================================
    // Le contrat du module de billets.
    // ===================================================================================================

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Module_dispense_is_all_or_nothing_and_refuses_exactly_the_specified_cases(AppelModule appel)
    {
        var dispenser = new MoneyDispenser(appel.Stock, appel.Jammed);
        var notes = appel.Notes;
        var allowed = notes.Total > 0
            && notes.Total <= MoneyDispenser.MaxNotesPerPass
            && (appel.Jammed is not { } jammed || notes.Of(jammed) == 0)
            && Contains(appel.Stock, notes);

        Assert.Equal(allowed, dispenser.Dispense(notes));
        Assert.Equal(allowed ? appel.Stock - notes : appel.Stock, dispenser.Stock);
        Assert.Equal(appel.Jammed, dispenser.Jammed);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Module_load_is_all_or_nothing_and_refuses_exactly_the_specified_cases(AppelModule appel)
    {
        var dispenser = new MoneyDispenser(appel.Stock, appel.Jammed);
        var notes = appel.Notes;
        var after = appel.Stock + notes;
        var allowed = notes.Total > 0
            && (appel.Jammed is not { } jammed || notes.Of(jammed) == 0)
            && Coupures.All(d => after.Of(d) <= MoneyDispenser.CassetteCapacity);

        Assert.Equal(allowed, dispenser.Load(notes));
        Assert.Equal(allowed ? after : appel.Stock, dispenser.Stock);
        Assert.Equal(appel.Jammed, dispenser.Jammed);
    }

    [Property(Arbitrary = new[] { typeof(Domaine) }, Replay = Graine, MaxTest = Cas)]
    public void Module_load_undoes_an_accepted_dispense(PassageAcceptable passage)
    {
        var dispenser = new MoneyDispenser(passage.Stock, passage.Jammed);

        Assert.True(dispenser.Dispense(passage.Notes));
        Assert.Equal(passage.Stock - passage.Notes, dispenser.Stock);
        Assert.True(dispenser.Load(passage.Notes));
        Assert.Equal(passage.Stock, dispenser.Stock);
    }

    // ===================================================================================================
    // Outils.
    // ===================================================================================================

    private static int Frais(int amount) => amount > 100 ? 5 : 0;

    private static (AtmResult Result, MoneyDispenser Dispenser) Run(Situation situation, Operation operation)
    {
        var dispenser = new MoneyDispenser(situation.Stock, situation.Jammed);
        var result = new CashMachine(dispenser).Execute(situation.Account, operation);

        return (result, dispenser);
    }

    /// <summary>Les opérations de la session, sauf les dépôts qui feraient sortir le solde du domaine.</summary>
    private static IEnumerable<Operation> InDomain(Session session, Func<AccountState> currentAccount) =>
        session.Operations.Where(operation =>
            operation is not Operation.Deposit deposit || currentAccount().Balance + deposit.Notes.Value <= SoldeMax);

    /// <summary>Le stock, la cassette bloquée comptant pour zéro.</summary>
    private static NoteCounts Disponibles(NoteCounts stock, Denomination? jammed) =>
        jammed is { } j ? WithCount(stock, j, 0) : stock;

    /// <summary>Toutes les combinaisons de billets pris dans <paramref name="available"/> qui font exactement le montant.</summary>
    private static IEnumerable<NoteCounts> Combinaisons(NoteCounts available, int amount) =>
        from f in Enumerable.Range(0, available.Fifties + 1)
        from t in Enumerable.Range(0, available.Twenties + 1)
        from n in Enumerable.Range(0, available.Tens + 1)
        let candidate = new NoteCounts(f, t, n)
        where candidate.Value == amount
        select candidate;

    private static bool Contains(NoteCounts outer, NoteCounts inner) => Coupures.All(d => inner.Of(d) <= outer.Of(d));

    private static NoteCounts WithCount(NoteCounts notes, Denomination denomination, int count) => denomination switch
    {
        Denomination.Fifty => notes with { Fifties = count },
        Denomination.Twenty => notes with { Twenties = count },
        _ => notes with { Tens = count },
    };

    private static void AssertStockInBounds(NoteCounts stock)
    {
        foreach (var d in Coupures)
        {
            Assert.InRange(stock.Of(d), 0, Capacite);
        }
    }

    /// <summary>Enregistre chaque appel fait au vrai module, sans rien changer à son comportement.</summary>
    private sealed class Espion(IMoneyDispenser inner) : IMoneyDispenser
    {
        public List<(string Method, NoteCounts Notes, bool Accepted)> Calls { get; } = [];

        public NoteCounts Stock => inner.Stock;

        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            var accepted = inner.Dispense(notes);
            Calls.Add((nameof(Dispense), notes, accepted));

            return accepted;
        }

        public bool Load(NoteCounts notes)
        {
            var accepted = inner.Load(notes);
            Calls.Add((nameof(Load), notes, accepted));

            return accepted;
        }
    }

    /// <summary>Le vrai module, sauf qu'il rend <c>false</c> au n-ième <c>Dispense</c>, ou à tout <c>Load</c>.</summary>
    private sealed class EnPanne(MoneyDispenser inner, int failingDispenseCall, bool failLoad) : IMoneyDispenser
    {
        public int DispenseCalls { get; private set; }

        public int LoadCalls { get; private set; }

        public List<NoteCounts> Dispensed { get; } = [];

        public NoteCounts Stock => inner.Stock;

        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            DispenseCalls++;
            if (DispenseCalls == failingDispenseCall || !inner.Dispense(notes))
            {
                return false;
            }

            Dispensed.Add(notes);

            return true;
        }

        public bool Load(NoteCounts notes)
        {
            LoadCalls++;

            return !failLoad && inner.Load(notes);
        }
    }
}
