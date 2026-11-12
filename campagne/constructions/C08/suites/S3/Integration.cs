namespace Atm.Tests;

/// <summary>
/// Tests d'intégration : <see cref="CashMachine"/> travaille avec le vrai <see cref="MoneyDispenser"/>, sans doublure.
/// Après chaque opération, on vérifie le résultat et le stock du module.
/// </summary>
public class Integration
{
    private static readonly Denomination?[] AllJams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] AllAmounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly NoteCounts[] AllDeposits =
        [.. from f in new[] { 0, 1 } from t in new[] { 0, 1 } from n in new[] { 0, 1 } select new NoteCounts(f, t, n)];

    private static readonly AccountState[] AllAccounts =
    [
        .. from balance in Enumerable.Range(0, 51).Select(i => i * 5)
        from withdrawn in Enumerable.Range(0, 21).Select(i => i * 10)
        select new AccountState(balance, withdrawn),
    ];

    private static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    private static Operation Deposit(int fifties, int twenties, int tens) =>
        new Operation.Deposit(new NoteCounts(fifties, twenties, tens));

    // ===================================================================================================
    // Une machine, un module, plusieurs opérations : le stock du module porte l'état d'un appel à l'autre.
    // ===================================================================================================

    /// <summary>Exécute une opération et vérifie le résultat complet et le stock du module qui en résulte.</summary>
    private static AtmResult Step(CashMachine machine, MoneyDispenser dispenser, AccountState account, Operation operation,
        AtmStatus status, NoteCounts notes, int fee, AccountState accountAfter, NoteCounts stockAfter)
    {
        var result = machine.Execute(account, operation);

        Assert.Equal(new AtmResult(status, notes, fee, accountAfter), result);
        Assert.Equal(stockAfter, dispenser.Stock);

        return result;
    }

    [Fact]
    public void Deposited_notes_can_be_withdrawn_and_the_round_trip_restores_everything()
    {
        var dispenser = new MoneyDispenser(NoteCounts.None);
        var machine = new CashMachine(dispenser);

        // Le module est vide : rien à sortir.
        Step(machine, dispenser, new(100, 0), Withdraw(80),
            AtmStatus.CannotDispense, NoteCounts.None, 0, new(100, 0), NoteCounts.None);

        Step(machine, dispenser, new(100, 0), Deposit(1, 1, 1),
            AtmStatus.Ok, NoteCounts.None, 0, new(180, 0), new(1, 1, 1));

        // Les billets déposés sont aussitôt disponibles pour un retrait.
        Step(machine, dispenser, new(180, 0), Withdraw(80),
            AtmStatus.Ok, new(1, 1, 1), 0, new(100, 80), NoteCounts.None);
    }

    [Fact]
    public void Successive_withdrawals_drain_the_module_until_it_cannot_dispense()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(1, 2, 1));
        var machine = new CashMachine(dispenser);

        Step(machine, dispenser, new(250, 0), Withdraw(50),
            AtmStatus.Ok, new(1, 0, 0), 0, new(200, 50), new(0, 2, 1));

        // Plus de billet de 50 : 50 € se fait désormais avec 20×2 10×1.
        Step(machine, dispenser, new(200, 50), Withdraw(50),
            AtmStatus.Ok, new(0, 2, 1), 0, new(150, 100), NoteCounts.None);

        Step(machine, dispenser, new(150, 100), Withdraw(10),
            AtmStatus.CannotDispense, NoteCounts.None, 0, new(150, 100), NoteCounts.None);
    }

    [Fact]
    public void Same_call_twice_gives_different_results_because_only_the_module_changed()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(1, 0, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        Step(machine, dispenser, account, Withdraw(50),
            AtmStatus.Ok, new(1, 0, 0), 0, new(200, 50), NoteCounts.None);

        // Même compte, même opération : la machine n'a rien retenu, seul le module est vide.
        Step(machine, dispenser, account, Withdraw(50),
            AtmStatus.CannotDispense, NoteCounts.None, 0, account, NoteCounts.None);
    }

    [Fact]
    public void Deposits_fill_a_cassette_to_capacity_and_a_withdrawal_frees_room_again()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(3, 0, 0));
        var machine = new CashMachine(dispenser);

        Step(machine, dispenser, new(0, 0), Deposit(1, 0, 0),
            AtmStatus.Ok, NoteCounts.None, 0, new(50, 0), new(4, 0, 0));
        Step(machine, dispenser, new(50, 0), Deposit(1, 0, 0),
            AtmStatus.Ok, NoteCounts.None, 0, new(100, 0), new(5, 0, 0));

        // Cassette pleine : refus complet, même pour les billets qui auraient tenu ailleurs.
        Step(machine, dispenser, new(100, 0), Deposit(1, 1, 1),
            AtmStatus.CassetteFull, NoteCounts.None, 0, new(100, 0), new(5, 0, 0));

        Step(machine, dispenser, new(100, 0), Withdraw(50),
            AtmStatus.Ok, new(1, 0, 0), 0, new(50, 50), new(4, 0, 0));

        Step(machine, dispenser, new(50, 50), Deposit(1, 1, 1),
            AtmStatus.Ok, NoteCounts.None, 0, new(130, 50), new(5, 1, 1));
    }

    [Fact]
    public void Refusals_interleaved_with_successes_never_move_a_note()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(2, 2, 0));
        var machine = new CashMachine(dispenser);

        Step(machine, dispenser, new(250, 0), Withdraw(15),
            AtmStatus.InvalidAmount, NoteCounts.None, 0, new(250, 0), new(2, 2, 0));
        Step(machine, dispenser, new(250, 0), Withdraw(210),
            AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, new(250, 0), new(2, 2, 0));
        Step(machine, dispenser, new(30, 0), Withdraw(40),
            AtmStatus.InsufficientFunds, NoteCounts.None, 0, new(30, 0), new(2, 2, 0));
        Step(machine, dispenser, new(250, 0), Withdraw(10),
            AtmStatus.CannotDispense, NoteCounts.None, 0, new(250, 0), new(2, 2, 0));
        Step(machine, dispenser, new(250, 0), Deposit(0, 0, 0),
            AtmStatus.InvalidAmount, NoteCounts.None, 0, new(250, 0), new(2, 2, 0));

        Step(machine, dispenser, new(250, 0), Withdraw(140),
            AtmStatus.Ok, new(2, 2, 0), 5, new(105, 140), NoteCounts.None);

        Step(machine, dispenser, new(105, 140), Withdraw(10),
            AtmStatus.CannotDispense, NoteCounts.None, 0, new(105, 140), NoteCounts.None);
    }

    [Fact]
    public void Daily_limit_accumulates_when_the_caller_passes_the_returned_account_back()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(5, 5, 5));
        var machine = new CashMachine(dispenser);

        var first = Step(machine, dispenser, new(250, 0), Withdraw(100),
            AtmStatus.Ok, new(2, 0, 0), 0, new(150, 100), new(3, 5, 5));
        var second = Step(machine, dispenser, first.Account, Withdraw(90),
            AtmStatus.Ok, new(1, 2, 0), 0, new(60, 190), new(2, 3, 5));

        // Un dépôt ne rend pas de plafond.
        var third = Step(machine, dispenser, second.Account, Deposit(0, 1, 0),
            AtmStatus.Ok, NoteCounts.None, 0, new(80, 190), new(2, 4, 5));
        Step(machine, dispenser, third.Account, Withdraw(20),
            AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, new(80, 190), new(2, 4, 5));
        Step(machine, dispenser, third.Account, Withdraw(10),
            AtmStatus.Ok, new(0, 0, 1), 0, new(70, 200), new(2, 4, 4));
    }

    [Fact]
    public void Jammed_cassette_is_never_touched_through_a_whole_session()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(3, 4, 4), Denomination.Twenty);
        var machine = new CashMachine(dispenser);

        // 40 € sans billet de 20 : 10×4, même si 20×2 serait plus court.
        Step(machine, dispenser, new(200, 0), Withdraw(40),
            AtmStatus.Ok, new(0, 0, 4), 0, new(160, 40), new(3, 4, 0));
        Step(machine, dispenser, new(160, 40), Withdraw(20),
            AtmStatus.CannotDispense, NoteCounts.None, 0, new(160, 40), new(3, 4, 0));
        Step(machine, dispenser, new(160, 40), Deposit(0, 1, 0),
            AtmStatus.CassetteJammed, NoteCounts.None, 0, new(160, 40), new(3, 4, 0));
        Step(machine, dispenser, new(160, 40), Deposit(1, 1, 1),
            AtmStatus.CassetteJammed, NoteCounts.None, 0, new(160, 40), new(3, 4, 0));
        Step(machine, dispenser, new(160, 40), Deposit(1, 0, 1),
            AtmStatus.Ok, NoteCounts.None, 0, new(220, 40), new(4, 4, 1));

        Assert.Equal(Denomination.Twenty, dispenser.Jammed);
    }

    [Fact]
    public void Withdrawal_of_more_than_four_notes_empties_the_module_through_the_real_pass_limit()
    {
        // Le vrai module refuse tout passage de plus de 4 billets : 7 billets doivent sortir en plusieurs fois.
        var dispenser = new MoneyDispenser(new NoteCounts(0, 5, 2), Denomination.Fifty);
        var machine = new CashMachine(dispenser);

        Step(machine, dispenser, new(250, 0), Withdraw(120),
            AtmStatus.Ok, new(0, 5, 2), 5, new(125, 120), NoteCounts.None);
    }

    // ===================================================================================================
    // La machine lit le module à chaque appel : elle ne garde aucune copie de son stock.
    // ===================================================================================================

    [Fact]
    public void Machine_sees_notes_loaded_into_the_module_after_its_construction()
    {
        var dispenser = new MoneyDispenser(NoteCounts.None);
        var machine = new CashMachine(dispenser);

        Assert.True(dispenser.Load(new NoteCounts(0, 2, 0)));

        Step(machine, dispenser, new(100, 0), Withdraw(40),
            AtmStatus.Ok, new(0, 2, 0), 0, new(60, 40), NoteCounts.None);
    }

    [Fact]
    public void Machine_sees_notes_taken_from_the_module_after_its_construction()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(0, 4, 0));
        var machine = new CashMachine(dispenser);

        Assert.True(dispenser.Dispense(new NoteCounts(0, 3, 0)));

        Step(machine, dispenser, new(100, 0), Withdraw(40),
            AtmStatus.CannotDispense, NoteCounts.None, 0, new(100, 0), new(0, 1, 0));
        Step(machine, dispenser, new(100, 0), Deposit(0, 1, 0),
            AtmStatus.Ok, NoteCounts.None, 0, new(120, 0), new(0, 2, 0));
    }

    [Fact]
    public void Two_machines_sharing_one_module_see_each_other_changes()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(0, 4, 0));
        var first = new CashMachine(dispenser);
        var second = new CashMachine(dispenser);

        Step(first, dispenser, new(100, 0), Withdraw(60),
            AtmStatus.Ok, new(0, 3, 0), 0, new(40, 60), new(0, 1, 0));
        Step(second, dispenser, new(100, 0), Withdraw(40),
            AtmStatus.CannotDispense, NoteCounts.None, 0, new(100, 0), new(0, 1, 0));
        Step(second, dispenser, new(100, 0), Deposit(0, 1, 0),
            AtmStatus.Ok, NoteCounts.None, 0, new(120, 0), new(0, 2, 0));
        Step(first, dispenser, new(100, 0), Withdraw(40),
            AtmStatus.Ok, new(0, 2, 0), 0, new(60, 40), NoteCounts.None);
    }

    // ===================================================================================================
    // Parcours du domaine : tous les comptes, tous les montants, toutes les cassettes bloquées.
    // ===================================================================================================

    /// <summary>Stocks représentatifs : vide, plein, une seule coupure, et des stocks pauvres où l'ordre compte.</summary>
    public static TheoryData<NoteCounts> RepresentativeStocks => new()
    {
        new(0, 0, 0), new(5, 5, 5), new(5, 0, 0), new(0, 5, 0), new(0, 0, 5),
        new(1, 3, 0), new(1, 1, 1), new(4, 0, 0), new(3, 2, 1), new(2, 4, 3),
    };

    [Theory]
    [MemberData(nameof(RepresentativeStocks))]
    public void Every_account_amount_and_jam_matches_the_model(NoteCounts stock)
    {
        foreach (var jammed in AllJams)
        foreach (var account in AllAccounts)
        {
            foreach (var amount in AllAmounts)
            {
                CheckAgainstModel(account, stock, jammed, Withdraw(amount));
            }

            foreach (var notes in AllDeposits)
            {
                CheckAgainstModel(account, stock, jammed, new Operation.Deposit(notes));
            }
        }
    }

    [Fact]
    public void Real_module_never_faults_anywhere_in_the_domain()
    {
        // Parcours complet des stocks, cassettes bloquées, montants et dépôts ; les comptes sont ceux qui
        // laissent passer les contrôles 2 et 3, pour que le module soit effectivement sollicité.
        AccountState[] accounts = [new(250, 0), new(5, 0), new(115, 90), new(250, 200)];

        foreach (var account in accounts)
        foreach (var jammed in AllJams)
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
        {
            var stock = new NoteCounts(f, t, n);

            foreach (var amount in AllAmounts)
            {
                var result = CheckAgainstModel(account, stock, jammed, Withdraw(amount));
                Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
            }

            foreach (var notes in AllDeposits)
            {
                var result = CheckAgainstModel(account, stock, jammed, new Operation.Deposit(notes));
                Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
            }
        }
    }

    private static AtmResult CheckAgainstModel(AccountState account, NoteCounts stock, Denomination? jammed,
        Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, operation);
        var (expected, expectedStock) = Model.Execute(account, stock, jammed, operation);

        Assert.Equal(expected, result);
        Assert.Equal(expectedStock, dispenser.Stock);
        Assert.Equal(jammed, dispenser.Jammed);

        return result;
    }

    // ===================================================================================================
    // Sessions aléatoires (graine fixe) : de longues suites d'opérations sur un même module.
    // ===================================================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Long_random_sessions_on_one_module_match_the_model_at_every_step(int seed)
    {
        var random = new Random(seed);

        for (var session = 0; session < 200; session++)
        {
            var jammed = AllJams[random.Next(AllJams.Length)];
            var stock = new NoteCounts(random.Next(6), random.Next(6), random.Next(6));
            var dispenser = new MoneyDispenser(stock, jammed);
            var machine = new CashMachine(dispenser);
            var account = RandomAccount(random);
            var fees = 0;

            // Ce qui entre dans le module entre aussi sur le compte ; ce qui en sort quitte le compte avec les frais.
            var invariant = dispenser.Stock.Value - account.Balance - fees;

            for (var step = 0; step < 40; step++)
            {
                // La machine est sans mémoire : on change parfois de client ou de jour, le module, lui, reste.
                if (random.Next(10) == 0)
                {
                    account = RandomAccount(random);
                    invariant = dispenser.Stock.Value - account.Balance - fees;
                }

                var operation = RandomOperation(random, account);
                var stockBefore = dispenser.Stock;
                var result = machine.Execute(account, operation);
                var (expected, expectedStock) = Model.Execute(account, stockBefore, jammed, operation);

                Assert.True(expected == result,
                    $"graine {seed}, session {session}, étape {step} : {account} {operation} sur {stockBefore} " +
                    $"(bloquée {jammed}) → attendu {expected}, obtenu {result}");
                Assert.Equal(expectedStock, dispenser.Stock);

                // Les billets rendus sont exactement ceux qui ont quitté le module.
                if (operation is Operation.Withdraw)
                {
                    Assert.Equal(stockBefore - dispenser.Stock, result.Notes);
                }

                account = result.Account;
                fees += result.Fee;
                Assert.Equal(invariant, dispenser.Stock.Value - account.Balance - fees);
            }
        }
    }

    private static AccountState RandomAccount(Random random) =>
        new(random.Next(51) * 5, random.Next(21) * 10);

    /// <summary>Une opération du domaine, choisie pour que le compte rendu reste lui aussi dans le domaine.</summary>
    private static Operation RandomOperation(Random random, AccountState account)
    {
        if (random.Next(2) == 0)
        {
            return Withdraw(AllAmounts[random.Next(AllAmounts.Length)]);
        }

        var candidates = AllDeposits.Where(notes => account.Balance + notes.Value <= 250).ToArray();

        return new Operation.Deposit(candidates[random.Next(candidates.Length)]);
    }

    // ===================================================================================================
    // Modèle de référence, écrit directement d'après la spécification.
    // ===================================================================================================

    private static class Model
    {
        public static (AtmResult Result, NoteCounts Stock) Execute(AccountState account, NoteCounts stock,
            Denomination? jammed, Operation operation) => operation switch
            {
                Operation.Withdraw w => Withdraw(account, stock, jammed, w.Amount),
                Operation.Deposit d => Deposit(account, stock, jammed, d.Notes),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };

        private static (AtmResult, NoteCounts) Refuse(AtmStatus status, AccountState account, NoteCounts stock) =>
            (new AtmResult(status, NoteCounts.None, 0, account), stock);

        private static (AtmResult, NoteCounts) Withdraw(AccountState account, NoteCounts stock, Denomination? jammed,
            int amount)
        {
            if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount, account, stock);
            if (account.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded, account, stock);
            var fee = amount > 100 ? 5 : 0;
            if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds, account, stock);

            int Usable(Denomination d) => jammed == d ? 0 : stock.Of(d);

            NoteCounts? best = null;
            var ties = 0;
            for (var f = 0; f <= Usable(Denomination.Fifty); f++)
            for (var t = 0; t <= Usable(Denomination.Twenty); t++)
            for (var n = 0; n <= Usable(Denomination.Ten); n++)
            {
                var candidate = new NoteCounts(f, t, n);
                if (candidate.Value != amount) continue;
                if (best is not { } current || candidate.Total < current.Total)
                {
                    best = candidate;
                    ties = 0;
                }
                else if (candidate.Total == current.Total)
                {
                    ties++;
                }
            }

            if (best is not { } notes) return Refuse(AtmStatus.CannotDispense, account, stock);
            Assert.Equal(0, ties); // La spécification affirme l'unicité de la combinaison.

            var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);

            return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
        }

        private static (AtmResult, NoteCounts) Deposit(AccountState account, NoteCounts stock, Denomination? jammed,
            NoteCounts notes)
        {
            if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount, account, stock);
            if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed, account, stock);
            var after = stock + notes;
            if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5)
                return Refuse(AtmStatus.CassetteFull, account, stock);

            return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }),
                after);
        }
    }
}
