namespace Atm.Tests;

/// <summary>
/// Tests d'intégration : <see cref="CashMachine"/> travaille avec le vrai <see cref="MoneyDispenser"/>, sans doublure.
/// Chaque opération est vérifiée sur son résultat et sur le stock du module qui en découle.
/// </summary>
public class Integration
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static AccountState A(int balance, int withdrawn) => new(balance, withdrawn);

    private static Operation W(int amount) => new Operation.Withdraw(amount);

    private static Operation D(int fifties, int twenties, int tens) => new Operation.Deposit(N(fifties, twenties, tens));

    /// <summary>
    /// Un module réel, une seule machine branchée dessus pour toute la session, et le compte courant,
    /// qu'on fait évoluer opération après opération.
    /// </summary>
    private sealed class Session
    {
        public Session(NoteCounts stock, Denomination? jammed, AccountState account)
        {
            Dispenser = new MoneyDispenser(stock, jammed);
            Machine = new CashMachine(Dispenser);
            Account = account;
        }

        public MoneyDispenser Dispenser { get; }

        public CashMachine Machine { get; }

        public AccountState Account { get; private set; }

        /// <summary>Exécute l'opération sur le compte courant et vérifie le résultat complet et le stock du module.</summary>
        public void Step(Operation operation, AtmResult expected, NoteCounts expectedStock)
        {
            var jammedBefore = Dispenser.Jammed;
            var result = Machine.Execute(Account, operation);

            Assert.Equal(expected, result);
            Assert.Equal(expectedStock, Dispenser.Stock);
            Assert.Equal(jammedBefore, Dispenser.Jammed);

            Account = result.Account;
        }

        public void Ok(Operation operation, NoteCounts notes, int fee, AccountState after, NoteCounts stock) =>
            Step(operation, new AtmResult(AtmStatus.Ok, notes, fee, after), stock);

        public void Refused(Operation operation, AtmStatus status)
        {
            var stock = Dispenser.Stock;
            Step(operation, new AtmResult(status, NoteCounts.None, 0, Account), stock);
        }
    }

    // ==== Scénarios : plusieurs opérations sur le même module ====

    [Fact]
    public void Successive_withdrawals_drain_the_module_until_it_cannot_dispense()
    {
        var s = new Session(N(2, 2, 2), null, A(250, 0));

        s.Ok(W(50), N(1, 0, 0), 0, A(200, 50), N(1, 2, 2));
        s.Ok(W(50), N(1, 0, 0), 0, A(150, 100), N(0, 2, 2));
        s.Ok(W(50), N(0, 2, 1), 0, A(100, 150), N(0, 0, 1));
        s.Refused(W(20), AtmStatus.CannotDispense);
        s.Ok(W(10), N(0, 0, 1), 0, A(90, 160), N(0, 0, 0));
        s.Refused(W(10), AtmStatus.CannotDispense);
    }

    [Fact]
    public void Combination_follows_the_stock_left_by_previous_withdrawals()
    {
        // 60 € : d'abord 50+10, puis 20×3 une fois les billets de 10 épuisés.
        var s = new Session(N(2, 3, 1), null, A(250, 0));

        s.Ok(W(60), N(1, 0, 1), 0, A(190, 60), N(1, 3, 0));
        s.Ok(W(60), N(0, 3, 0), 0, A(130, 120), N(1, 0, 0));
        s.Refused(W(60), AtmStatus.CannotDispense);
        s.Ok(W(50), N(1, 0, 0), 0, A(80, 170), N(0, 0, 0));
    }

    [Fact]
    public void Deposit_makes_a_previously_impossible_withdrawal_possible()
    {
        var s = new Session(N(0, 0, 0), null, A(100, 0));

        s.Refused(W(50), AtmStatus.CannotDispense);
        s.Ok(D(1, 0, 0), NoteCounts.None, 0, A(150, 0), N(1, 0, 0));
        s.Ok(W(50), N(1, 0, 0), 0, A(100, 50), N(0, 0, 0));
    }

    [Fact]
    public void Withdrawal_frees_room_for_a_deposit_refused_as_full()
    {
        var s = new Session(N(5, 5, 5), null, A(100, 0));

        s.Refused(D(0, 1, 0), AtmStatus.CassetteFull);
        s.Ok(W(20), N(0, 1, 0), 0, A(80, 20), N(5, 4, 5));
        s.Ok(D(0, 1, 0), NoteCounts.None, 0, A(100, 20), N(5, 5, 5));
        s.Refused(D(0, 1, 0), AtmStatus.CassetteFull);
    }

    [Fact]
    public void Deposited_notes_come_back_out_on_withdrawal()
    {
        var s = new Session(N(0, 0, 0), null, A(0, 0));

        s.Ok(D(1, 1, 1), NoteCounts.None, 0, A(80, 0), N(1, 1, 1));
        s.Ok(W(80), N(1, 1, 1), 0, A(0, 80), N(0, 0, 0));
    }

    [Fact]
    public void Jammed_cassette_stays_untouched_across_a_sequence()
    {
        var s = new Session(N(3, 3, 3), Denomination.Twenty, A(200, 0));

        s.Refused(W(40), AtmStatus.CannotDispense);   // 20×2 interdit, 10×4 impossible avec 3 billets de 10
        s.Ok(W(60), N(1, 0, 1), 0, A(140, 60), N(2, 3, 2));
        s.Refused(D(0, 1, 0), AtmStatus.CassetteJammed);
        s.Refused(D(1, 1, 1), AtmStatus.CassetteJammed);
        s.Ok(D(1, 0, 1), NoteCounts.None, 0, A(200, 60), N(3, 3, 3));
        s.Ok(W(30), N(0, 0, 3), 0, A(170, 90), N(3, 3, 0));
        s.Refused(W(10), AtmStatus.CannotDispense);   // il reste des billets, mais seulement dans la cassette bloquée et en 50
    }

    [Fact]
    public void Withdrawal_of_many_notes_passes_through_the_real_module_beside_a_jammed_cassette()
    {
        // 150 € sans billets de 50 : 20×5 10×5, soit 10 billets, donc au moins trois passages de 4 au plus.
        var s = new Session(N(5, 5, 5), Denomination.Fifty, A(250, 0));

        s.Ok(W(150), N(0, 5, 5), 5, A(95, 150), N(5, 0, 0));
        s.Refused(W(10), AtmStatus.CannotDispense);
        s.Ok(D(0, 1, 1), NoteCounts.None, 0, A(125, 150), N(5, 1, 1));
        s.Refused(D(1, 0, 0), AtmStatus.CassetteJammed);
        s.Ok(W(30), N(0, 1, 1), 0, A(95, 180), N(5, 0, 0));
    }

    [Fact]
    public void Largest_withdrawal_empties_every_cassette_through_the_real_module()
    {
        // 200 € avec 50×1 20×5 10×5 : la seule combinaison prend les 11 billets.
        var s = new Session(N(1, 5, 5), null, A(205, 0));

        s.Ok(W(200), N(1, 5, 5), 5, A(0, 200), N(0, 0, 0));
    }

    [Fact]
    public void Daily_limit_and_fee_accumulate_across_withdrawals()
    {
        var s = new Session(N(5, 5, 5), null, A(250, 0));

        s.Ok(W(110), N(2, 0, 1), 5, A(135, 110), N(3, 5, 4));
        s.Ok(W(90), N(1, 2, 0), 0, A(45, 200), N(2, 3, 4));
        s.Refused(W(10), AtmStatus.DailyLimitExceeded);
        s.Ok(D(1, 1, 1), NoteCounts.None, 0, A(125, 200), N(3, 4, 5));   // le dépôt ne touche pas au plafond
        s.Refused(W(10), AtmStatus.DailyLimitExceeded);
    }

    [Fact]
    public void Funds_run_out_across_withdrawals_and_deposit_restores_them()
    {
        var s = new Session(N(5, 5, 5), null, A(115, 0));

        s.Ok(W(110), N(2, 0, 1), 5, A(0, 110), N(3, 5, 4));
        s.Refused(W(10), AtmStatus.InsufficientFunds);
        s.Ok(D(0, 0, 1), NoteCounts.None, 0, A(10, 110), N(3, 5, 5));
        s.Ok(W(10), N(0, 0, 1), 0, A(0, 120), N(3, 5, 4));
    }

    [Fact]
    public void Refusals_in_a_row_move_nothing_and_leave_the_module_fully_usable()
    {
        var s = new Session(N(1, 1, 1), Denomination.Ten, A(60, 150));

        for (var i = 0; i < 3; i++)
        {
            s.Refused(W(5), AtmStatus.InvalidAmount);
            s.Refused(W(60), AtmStatus.DailyLimitExceeded);
            s.Refused(W(-10), AtmStatus.InvalidAmount);
            s.Refused(D(0, 0, 0), AtmStatus.InvalidAmount);
            s.Refused(D(0, 0, 1), AtmStatus.CassetteJammed);
            s.Refused(W(10), AtmStatus.CannotDispense);
        }

        s.Ok(W(50), N(1, 0, 0), 0, A(10, 200), N(0, 1, 1));
        s.Refused(D(0, 0, 1), AtmStatus.CassetteJammed);
        s.Ok(D(1, 1, 0), NoteCounts.None, 0, A(80, 200), N(1, 2, 1));
    }

    // ==== La machine est sans mémoire : elle relit le module à chaque appel ====

    [Fact]
    public void Machine_sees_notes_loaded_directly_into_the_module_after_its_construction()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, A(100, 0)), machine.Execute(A(100, 0), W(70)));
        Assert.Equal(N(0, 0, 0), dispenser.Stock);

        Assert.True(dispenser.Load(N(1, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 0), 0, A(30, 70)), machine.Execute(A(100, 0), W(70)));
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Machine_sees_notes_dispensed_directly_from_the_module_after_its_construction()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.True(dispenser.Dispense(N(1, 0, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, A(100, 0)), machine.Execute(A(100, 0), W(50)));
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Machine_sees_room_freed_directly_in_the_module_for_a_deposit()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 5));
        var machine = new CashMachine(dispenser);

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, A(0, 0)), machine.Execute(A(0, 0), D(0, 0, 1)));
        Assert.Equal(N(0, 0, 5), dispenser.Stock);

        Assert.True(dispenser.Dispense(N(0, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(10, 0)), machine.Execute(A(0, 0), D(0, 0, 1)));
        Assert.Equal(N(0, 0, 5), dispenser.Stock);
    }

    [Fact]
    public void Two_machines_sharing_one_module_see_each_other_s_operations()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var first = new CashMachine(dispenser);
        var second = new CashMachine(dispenser);

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, A(50, 50)), first.Execute(A(100, 0), W(50)));
        Assert.Equal(N(0, 0, 0), dispenser.Stock);

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, A(100, 0)), second.Execute(A(100, 0), W(50)));
        Assert.Equal(N(0, 0, 0), dispenser.Stock);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(50, 30)), second.Execute(A(0, 30), D(1, 0, 0)));
        Assert.Equal(N(1, 0, 0), dispenser.Stock);

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, A(0, 100)), first.Execute(A(50, 50), W(50)));
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void One_machine_serves_independent_accounts_on_the_same_module()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        // Le compte d'une opération n'influence pas la suivante : seul le stock du module est partagé.
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, A(0, 110)), machine.Execute(A(115, 0), W(110)));
        Assert.Equal(N(3, 5, 4), dispenser.Stock);

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 1, 0), 0, A(230, 20)), machine.Execute(A(250, 0), W(20)));
        Assert.Equal(N(3, 4, 4), dispenser.Stock);

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, A(250, 200)), machine.Execute(A(250, 200), W(10)));
        Assert.Equal(N(3, 4, 4), dispenser.Stock);

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 0, 1), 0, A(0, 10)), machine.Execute(A(10, 0), W(10)));
        Assert.Equal(N(3, 4, 3), dispenser.Stock);
    }

    // ==== Modèle de référence indépendant ====

    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] DomainAmounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly NoteCounts[] DomainDeposits =
        [.. Enumerable.Range(0, 8).Select(mask => N(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1))];

    private static readonly Operation[] DomainOperations =
        [.. DomainAmounts.Select(W), .. DomainDeposits.Select(notes => (Operation)new Operation.Deposit(notes))];

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var a = 0; a <= 5; a++)
            for (var b = 0; b <= 5; b++)
                for (var c = 0; c <= 5; c++)
                    yield return N(a, b, c);
    }

    /// <summary>Pour chaque stock utilisable et chaque montant : la combinaison la plus courte, trouvée par énumération.</summary>
    private static readonly Dictionary<(NoteCounts Available, int Amount), NoteCounts?> Combinations = BuildCombinations();

    private static Dictionary<(NoteCounts, int), NoteCounts?> BuildCombinations()
    {
        var table = new Dictionary<(NoteCounts, int), NoteCounts?>();
        var all = AllStocks().ToList();
        foreach (var available in all)
            foreach (var amount in DomainAmounts.Where(a => a > 0 && a % 10 == 0))
            {
                var exact = all
                    .Where(n => n.Fifties <= available.Fifties && n.Twenties <= available.Twenties && n.Tens <= available.Tens)
                    .Where(n => n.Value == amount)
                    .ToList();
                NoteCounts? best = null;
                if (exact.Count > 0)
                {
                    var min = exact.Min(n => n.Total);
                    best = exact.Single(n => n.Total == min);
                }

                table[(available, amount)] = best;
            }

        return table;
    }

    private static NoteCounts Usable(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    private static bool InDomain(AccountState account) =>
        account.Balance is >= 0 and <= 250 && account.Balance % 5 == 0
        && account.WithdrawnToday is >= 0 and <= 200 && account.WithdrawnToday % 10 == 0;

    /// <summary>Ce que la spécification attend : le résultat et le stock du module après l'opération.</summary>
    private static (AtmResult Result, NoteCounts Stock) Expected(AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        AtmResult Refuse(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        switch (operation)
        {
            case Operation.Withdraw { Amount: var amount }:
            {
                var fee = amount > 100 ? 5 : 0;
                if (amount <= 0 || amount % 10 != 0) return (Refuse(AtmStatus.InvalidAmount), stock);
                if (account.WithdrawnToday + amount > 200) return (Refuse(AtmStatus.DailyLimitExceeded), stock);
                if (amount + fee > account.Balance) return (Refuse(AtmStatus.InsufficientFunds), stock);
                if (Combinations[(Usable(stock, jammed), amount)] is not { } notes) return (Refuse(AtmStatus.CannotDispense), stock);

                var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
                return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
            }

            case Operation.Deposit { Notes: var notes }:
            {
                var after = stock + notes;
                if (notes.Total == 0) return (Refuse(AtmStatus.InvalidAmount), stock);
                if (jammed is { } j && notes.Of(j) > 0) return (Refuse(AtmStatus.CassetteJammed), stock);
                if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (Refuse(AtmStatus.CassetteFull), stock);

                return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }), after);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    /// <summary>Exécute une opération avec la vraie machine et le vrai module, et la confronte au modèle.</summary>
    private static AtmResult CheckStep(CashMachine machine, MoneyDispenser dispenser, AccountState account, Operation operation)
    {
        var before = dispenser.Stock;
        var jammed = dispenser.Jammed;
        var (expected, expectedStock) = Expected(account, before, jammed, operation);

        var result = machine.Execute(account, operation);

        Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(expected, result);
        Assert.Equal(expectedStock, dispenser.Stock);
        Assert.Equal(jammed, dispenser.Jammed);

        // Conservation : ce qui est sorti et ce qui est entré explique exactement la variation du stock,
        // et l'argent du compte suit les billets (au prélèvement des frais près).
        var deposited = operation is Operation.Deposit { Notes: var notes } && result.Status == AtmStatus.Ok ? notes : NoteCounts.None;
        Assert.Equal(before - result.Notes + deposited, dispenser.Stock);
        Assert.Equal(account.Balance - result.Notes.Value - result.Fee + deposited.Value, result.Account.Balance);
        Assert.Equal(account.WithdrawnToday + result.Notes.Value, result.Account.WithdrawnToday);

        return result;
    }

    [Theory]
    [InlineData(170, 0)]
    [InlineData(90, 120)]
    [InlineData(0, 200)]
    public void Every_pair_of_operations_on_one_module_matches_the_model(int balance, int withdrawn)
    {
        foreach (var stock in AllStocks())
            foreach (var jammed in JamOptions)
                foreach (var first in DomainOperations)
                    foreach (var second in DomainOperations)
                    {
                        var dispenser = new MoneyDispenser(stock, jammed);
                        var machine = new CashMachine(dispenser);

                        var afterFirst = CheckStep(machine, dispenser, A(balance, withdrawn), first);
                        Assert.True(InDomain(afterFirst.Account));
                        CheckStep(machine, dispenser, afterFirst.Account, second);
                    }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Long_random_sequences_on_one_module_match_the_model(int seed)
    {
        var random = new Random(seed);

        for (var run = 0; run < 200; run++)
        {
            var dispenser = new MoneyDispenser(
                N(random.Next(6), random.Next(6), random.Next(6)),
                JamOptions[random.Next(JamOptions.Length)]);
            var machine = new CashMachine(dispenser);
            var account = RandomAccount(random);

            for (var step = 0; step < 100; step++)
            {
                var operation = DomainOperations[random.Next(DomainOperations.Length)];
                var result = CheckStep(machine, dispenser, account, operation);

                // Le compte suit les opérations tant qu'il reste dans le domaine ; sinon un autre client se présente.
                account = InDomain(result.Account) ? result.Account : RandomAccount(random);
            }
        }
    }

    private static AccountState RandomAccount(Random random) => A(5 * random.Next(51), 10 * random.Next(21));

    [Fact]
    public void Withdraw_matches_the_model_for_every_stock_jam_and_amount_at_account_boundaries()
    {
        foreach (var stock in AllStocks())
            foreach (var jammed in JamOptions)
                foreach (var amount in DomainAmounts)
                {
                    var fee = amount > 100 ? 5 : 0;
                    int[] balances = [0, amount + fee - 5, amount + fee, 250];
                    int[] withdrawns = [0, 190 - amount, 200 - amount, 210 - amount, 200];

                    foreach (var balance in balances)
                        foreach (var withdrawn in withdrawns)
                        {
                            var account = A(balance, withdrawn);
                            if (!InDomain(account))
                            {
                                continue;
                            }

                            var dispenser = new MoneyDispenser(stock, jammed);
                            CheckStep(new CashMachine(dispenser), dispenser, account, W(amount));
                        }
                }
    }

    [Fact]
    public void Deposit_matches_the_model_for_every_stock_jam_and_account()
    {
        foreach (var stock in AllStocks())
            foreach (var jammed in JamOptions)
                foreach (var notes in DomainDeposits)
                    foreach (var account in new[] { A(0, 0), A(170, 200), A(250, 90) })
                    {
                        var dispenser = new MoneyDispenser(stock, jammed);
                        CheckStep(new CashMachine(dispenser), dispenser, account, new Operation.Deposit(notes));
                    }
    }
}
