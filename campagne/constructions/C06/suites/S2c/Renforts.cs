namespace Atm.Tests;

/// <summary>Tests complémentaires : absence de mémoire, pannes à chaque passage, dépôt sur tout compte.</summary>
public class Renforts
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
        {
            yield return new NoteCounts(f, t, n);
        }
    }

    private static NoteCounts Sum(IEnumerable<NoteCounts> passes) => passes.Aggregate(NoteCounts.None, (a, b) => a + b);

    // ---- La machine est sans mémoire entre deux appels ----

    [Fact]
    public void Machine_reads_the_stock_at_each_call_not_at_construction()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(0, 0, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(account, new Operation.Withdraw(50)).Status);

        Assert.True(dispenser.Load(new NoteCounts(1, 0, 0)));

        var result = machine.Execute(account, new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(1, 0, 0), 0, new AccountState(200, 50)), result);
        Assert.Equal(new NoteCounts(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Machine_sees_notes_removed_by_a_previous_withdrawal()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(1, 0, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Withdraw(50)).Status);
        var second = machine.Execute(account, new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, account), second);
        Assert.Equal(new NoteCounts(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Machine_sees_cassette_space_freed_by_a_withdrawal_and_filled_by_a_deposit()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(0, 5, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(100, 0);
        var deposit = new Operation.Deposit(new NoteCounts(0, 1, 0));

        Assert.Equal(AtmStatus.CassetteFull, machine.Execute(account, deposit).Status);

        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Withdraw(20)).Status);
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(120, 0)), machine.Execute(account, deposit));
        Assert.Equal(new NoteCounts(0, 5, 0), dispenser.Stock);

        Assert.Equal(AtmStatus.CassetteFull, machine.Execute(account, deposit).Status);
    }

    [Fact]
    public void Same_account_given_twice_gives_the_same_result()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 100);

        var first = machine.Execute(account, new Operation.Withdraw(100));
        var second = machine.Execute(account, new Operation.Withdraw(100));

        var expected = new AtmResult(AtmStatus.Ok, new NoteCounts(2, 0, 0), 0, new AccountState(150, 200));
        Assert.Equal(expected, first);
        Assert.Equal(expected, second);
        Assert.Equal(new NoteCounts(1, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Shared_machine_behaves_like_a_fresh_machine_on_every_call()
    {
        // Suite d'opérations variées sur une même machine : chaque résultat doit être celui
        // qu'obtiendrait une machine neuve sur un module dans le même état.
        Operation[] operations =
        [
            new Operation.Withdraw(110), new Operation.Deposit(new(1, 1, 1)), new Operation.Withdraw(200),
            new Operation.Withdraw(60), new Operation.Deposit(new(0, 1, 1)), new Operation.Withdraw(30),
            new Operation.Withdraw(-10), new Operation.Deposit(new(1, 0, 0)), new Operation.Withdraw(150),
            new Operation.Withdraw(80), new Operation.Deposit(new(1, 1, 0)), new Operation.Withdraw(90),
            new Operation.Withdraw(40), new Operation.Deposit(NoteCounts.None), new Operation.Withdraw(10),
        ];

        foreach (var jammed in Jams)
        {
            var dispenser = new MoneyDispenser(new NoteCounts(3, 2, 4), jammed);
            var machine = new CashMachine(dispenser);
            var account = new AccountState(250, 0);

            foreach (var operation in operations)
            {
                var reference = new MoneyDispenser(dispenser.Stock, dispenser.Jammed);
                var expected = new CashMachine(reference).Execute(account, operation);

                var actual = machine.Execute(account, operation);

                Assert.Equal(expected, actual);
                Assert.Equal(reference.Stock, dispenser.Stock);

                // On repart d'un compte du domaine pour que la suite reste significative.
                account = new AccountState(Math.Min(250, actual.Account.Balance + 60), actual.Account.WithdrawnToday % 200);
            }
        }
    }

    // ---- Pannes du module pendant un retrait ----

    [Fact]
    public void Withdraw_fault_on_third_pass_returns_all_notes_from_the_first_two_passes()
    {
        var stock = new NoteCounts(0, 5, 5);
        var spy = new SpyDispenser(stock, failOnCall: 3);
        var account = new AccountState(250, 0);

        // 150 sans billets de 50 : 10 billets, donc au moins 3 passages.
        var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(2, spy.Dispensed.Count);
        Assert.Equal(Sum(spy.Dispensed), result.Notes);
        Assert.True(result.Notes.Total > 4, "Les billets des deux premiers passages doivent être cumulés.");
        Assert.Equal(stock - result.Notes, spy.Stock);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(3, spy.Calls);
    }

    [Fact]
    public void Withdraw_fault_on_first_of_several_passes_returns_no_notes_and_stops()
    {
        var stock = new NoteCounts(0, 5, 5);
        var spy = new SpyDispenser(stock, failOnCall: 1);
        var account = new AccountState(250, 0);

        var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal(1, spy.Calls);
        Assert.Equal(stock, spy.Stock);
    }

    [Fact]
    public void Withdraw_fault_at_every_pass_over_the_domain()
    {
        // Pour chaque retrait réussi, on rejoue en faisant échouer chacun des passages tour à tour.
        var amounts = Enumerable.Range(1, 20).Select(i => i * 10).ToArray();
        foreach (var account in new[] { new AccountState(250, 0), new AccountState(105, 0) })
        foreach (var stock in Stocks())
        foreach (var jammed in Jams)
        foreach (var amount in amounts)
        {
            var clean = new SpyDispenser(stock, jammed);
            var ok = new CashMachine(clean).Execute(account, new Operation.Withdraw(amount));
            if (ok.Status != AtmStatus.Ok)
            {
                continue;
            }

            var passes = clean.Dispensed.Count;
            for (var failing = 1; failing <= passes; failing++)
            {
                var spy = new SpyDispenser(stock, jammed, failOnCall: failing);
                var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(amount));

                Assert.Equal(AtmStatus.DispenserFault, result.Status);
                Assert.Equal(failing, spy.Calls);
                Assert.Equal(failing - 1, spy.Dispensed.Count);
                Assert.Equal(Sum(spy.Dispensed), result.Notes);
                Assert.Equal(stock - result.Notes, spy.Stock);
                Assert.Equal(0, result.Fee);
                Assert.Equal(account, result.Account);
            }
        }
    }

    [Fact]
    public void Withdraw_fault_reported_by_the_real_module_is_handled()
    {
        // Un module qui annonce plus de billets qu'il n'en a : le vrai Dispense rend false.
        var real = new MoneyDispenser(new NoteCounts(0, 0, 0));
        var lying = new LyingStockDispenser(real, announced: Full);
        var account = new AccountState(250, 0);

        var result = new CashMachine(lying).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal(1, lying.DispenseCalls);
    }

    // ---- Panne du module pendant un dépôt ----

    [Fact]
    public void Deposit_fault_stops_after_a_single_load_attempt()
    {
        var stock = new NoteCounts(2, 2, 2);
        var spy = new SpyDispenser(stock, Denomination.Twenty, failOnCall: 1);
        var account = new AccountState(250, 200);

        var result = new CashMachine(spy).Execute(account, new Operation.Deposit(new NoteCounts(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal(1, spy.Calls);
        Assert.Equal(stock, spy.Stock);
    }

    [Fact]
    public void Deposit_fault_reported_by_the_real_module_is_handled()
    {
        // Le module annonce des cassettes vides alors qu'elles sont pleines : le vrai Load rend false.
        var real = new MoneyDispenser(Full);
        var lying = new LyingStockDispenser(real, announced: new NoteCounts(0, 0, 0));
        var account = new AccountState(10, 30);

        var result = new CashMachine(lying).Execute(account, new Operation.Deposit(new NoteCounts(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal(Full, real.Stock);
    }

    // ---- Dépôt : tous les comptes du domaine ----

    [Fact]
    public void Deposit_success_credits_balance_only_for_every_account()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        for (var mask = 1; mask < 8; mask++)
        {
            var notes = new NoteCounts(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
            var stock = new NoteCounts(4, 0, 2);
            var spy = new SpyDispenser(stock);
            var account = new AccountState(balance, withdrawn);

            var result = new CashMachine(spy).Execute(account, new Operation.Deposit(notes));

            Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(balance + notes.Value, withdrawn)), result);
            Assert.Equal(stock + notes, spy.Stock);
            Assert.Empty(spy.Dispensed);
        }
    }

    [Fact]
    public void Deposit_refusals_leave_every_account_unchanged()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        {
            var account = new AccountState(balance, withdrawn);
            (NoteCounts stock, Denomination? jammed, NoteCounts notes, AtmStatus status)[] cases =
            [
                (Full, null, NoteCounts.None, AtmStatus.InvalidAmount),
                (new(0, 0, 0), Denomination.Ten, new(0, 0, 1), AtmStatus.CassetteJammed),
                (new(5, 0, 0), null, new(1, 1, 1), AtmStatus.CassetteFull),
            ];

            foreach (var (stock, jammed, notes, status) in cases)
            {
                var spy = new SpyDispenser(stock, jammed);
                var result = new CashMachine(spy).Execute(account, new Operation.Deposit(notes));

                Assert.Equal(new AtmResult(status, NoteCounts.None, 0, account), result);
                Assert.Equal(stock, spy.Stock);
                Assert.Equal(0, spy.Calls);
            }
        }
    }

    // ---- Un retrait ne charge jamais, un dépôt ne distribue jamais ----

    [Fact]
    public void Withdraw_never_loads_notes()
    {
        foreach (var stock in Stocks())
        foreach (var amount in new[] { 10, 60, 110, 200 })
        {
            var spy = new SpyDispenser(stock);
            new CashMachine(spy).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));

            Assert.Empty(spy.Loaded);
        }
    }

    [Fact]
    public void Deposit_loads_exactly_the_deposited_notes()
    {
        var spy = new SpyDispenser(new NoteCounts(1, 2, 3));

        var result = new CashMachine(spy).Execute(new AccountState(0, 0), new Operation.Deposit(new NoteCounts(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(1, 1, 1), Sum(spy.Loaded));
        Assert.Empty(spy.Dispensed);
    }

    /// <summary>Annonce un stock fictif mais délègue Dispense et Load au vrai module.</summary>
    private sealed class LyingStockDispenser(MoneyDispenser inner, NoteCounts announced) : IMoneyDispenser
    {
        public int DispenseCalls { get; private set; }

        public NoteCounts Stock => announced;

        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            DispenseCalls++;

            return inner.Dispense(notes);
        }

        public bool Load(NoteCounts notes) => inner.Load(notes);
    }
}
