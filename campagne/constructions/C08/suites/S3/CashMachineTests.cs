namespace Atm.Tests;

public class CashMachineTests
{
    private static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    private static Operation Deposit(int fifties, int twenties, int tens) =>
        new Operation.Deposit(new NoteCounts(fifties, twenties, tens));

    private static (AtmResult Result, MoneyDispenser Dispenser) Run(
        int balance, int withdrawn, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), operation);

        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult result, AccountState account,
        MoneyDispenser dispenser, NoteCounts stock)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
    }

    // ----- Exemples de la spécification -----

    public static TheoryData<int, int, NoteCounts, Denomination?, int, NoteCounts, int, int, int> WithdrawSuccessExamples => new()
    {
        { 100, 0, new(1, 3, 0), null, 60, new(0, 3, 0), 0, 40, 60 },
        { 100, 0, new(1, 1, 1), null, 80, new(1, 1, 1), 0, 20, 80 },
        { 105, 0, new(5, 5, 5), null, 100, new(2, 0, 0), 0, 5, 100 },
        { 115, 0, new(5, 5, 5), null, 110, new(2, 0, 1), 5, 0, 110 },
        { 250, 0, new(2, 2, 0), Denomination.Fifty, 40, new(0, 2, 0), 0, 210, 40 },
    };

    [Theory]
    [MemberData(nameof(WithdrawSuccessExamples))]
    public void Withdraw_spec_examples_succeed(int balance, int withdrawn, NoteCounts stock, Denomination? jammed,
        int amount, NoteCounts expectedNotes, int expectedFee, int expectedBalance, int expectedWithdrawn)
    {
        var (result, dispenser) = Run(balance, withdrawn, stock, jammed, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedNotes, result.Notes);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(new AccountState(expectedBalance, expectedWithdrawn), result.Account);
        Assert.Equal(stock - expectedNotes, dispenser.Stock);
    }

    public static TheoryData<int, int, NoteCounts, Denomination?, int, AtmStatus> WithdrawRefusalExamples => new()
    {
        { 110, 0, new(5, 5, 5), null, 110, AtmStatus.InsufficientFunds },
        { 250, 150, new(5, 5, 5), null, 60, AtmStatus.DailyLimitExceeded },
        { 250, 0, new(1, 0, 0), Denomination.Fifty, 50, AtmStatus.CannotDispense },
        { 0, 200, new(0, 0, 0), null, 5, AtmStatus.InvalidAmount },
    };

    [Theory]
    [MemberData(nameof(WithdrawRefusalExamples))]
    public void Withdraw_spec_examples_are_refused(int balance, int withdrawn, NoteCounts stock, Denomination? jammed,
        int amount, AtmStatus expected)
    {
        var (result, dispenser) = Run(balance, withdrawn, stock, jammed, Withdraw(amount));

        AssertRefused(expected, result, new AccountState(balance, withdrawn), dispenser, stock);
    }

    [Fact]
    public void Deposit_spec_example_cassette_full()
    {
        var stock = new NoteCounts(0, 5, 0);
        var (result, dispenser) = Run(10, 0, stock, null, Deposit(0, 1, 0));

        AssertRefused(AtmStatus.CassetteFull, result, new AccountState(10, 0), dispenser, stock);
    }

    [Fact]
    public void Deposit_spec_example_cassette_jammed()
    {
        var stock = new NoteCounts(0, 1, 0);
        var (result, dispenser) = Run(10, 30, stock, Denomination.Twenty, Deposit(0, 1, 0));

        AssertRefused(AtmStatus.CassetteJammed, result, new AccountState(10, 30), dispenser, stock);
    }

    [Fact]
    public void Deposit_spec_example_succeeds()
    {
        var (result, dispenser) = Run(10, 30, new NoteCounts(1, 1, 0), null, Deposit(1, 0, 1));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(70, 30), result.Account);
        Assert.Equal(new NoteCounts(2, 1, 1), dispenser.Stock);
    }

    // ----- Retrait : contrôles et ordre -----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void Withdraw_invalid_amount(int amount)
    {
        var stock = new NoteCounts(5, 5, 5);
        var (result, dispenser) = Run(250, 0, stock, null, Withdraw(amount));

        AssertRefused(AtmStatus.InvalidAmount, result, new AccountState(250, 0), dispenser, stock);
    }

    [Fact]
    public void Invalid_amount_comes_before_daily_limit()
    {
        var (result, _) = Run(0, 200, new NoteCounts(0, 0, 0), null, Withdraw(-10));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void Daily_limit_comes_before_insufficient_funds()
    {
        var (result, _) = Run(0, 0, new NoteCounts(5, 5, 5), null, Withdraw(210));

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void Insufficient_funds_comes_before_cannot_dispense()
    {
        var (result, _) = Run(0, 0, new NoteCounts(0, 0, 0), null, Withdraw(10));

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    [Fact]
    public void Daily_limit_is_inclusive_and_ignores_fee()
    {
        var (result, _) = Run(250, 0, new NoteCounts(4, 0, 0), null, Withdraw(200));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(45, 200), result.Account);
    }

    [Fact]
    public void Daily_limit_exceeded_by_ten()
    {
        var (result, _) = Run(250, 10, new NoteCounts(5, 5, 5), null, Withdraw(200));

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void No_fee_at_exactly_100()
    {
        var (result, _) = Run(100, 0, new NoteCounts(2, 0, 0), null, Withdraw(100));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(0, 100), result.Account);
    }

    [Fact]
    public void Cannot_dispense_when_stock_has_no_exact_combination()
    {
        var stock = new NoteCounts(1, 0, 0);
        var (result, dispenser) = Run(250, 0, stock, null, Withdraw(30));

        AssertRefused(AtmStatus.CannotDispense, result, new AccountState(250, 0), dispenser, stock);
    }

    [Fact]
    public void Jammed_ten_cassette_counts_for_zero()
    {
        var stock = new NoteCounts(0, 0, 5);
        var (result, dispenser) = Run(250, 0, stock, Denomination.Ten, Withdraw(10));

        AssertRefused(AtmStatus.CannotDispense, result, new AccountState(250, 0), dispenser, stock);
    }

    [Fact]
    public void Jammed_twenty_cassette_falls_back_on_other_notes()
    {
        var (result, dispenser) = Run(250, 0, new NoteCounts(0, 5, 5), Denomination.Twenty, Withdraw(40));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(0, 0, 4), result.Notes);
        Assert.Equal(new NoteCounts(0, 5, 1), dispenser.Stock);
    }

    [Fact]
    public void Fewest_notes_is_not_always_greedy()
    {
        // Un glouton prendrait 50 puis resterait bloqué sur 10 € sans billet de 10 ; la solution est 20×3.
        var (result, _) = Run(250, 0, new NoteCounts(1, 3, 0), null, Withdraw(60));

        Assert.Equal(new NoteCounts(0, 3, 0), result.Notes);
    }

    [Fact]
    public void Large_withdrawal_is_dispensed_in_several_passes()
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(new NoteCounts(3, 2, 1)));
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), Withdraw(200));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(3, 2, 1), result.Notes);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
        Assert.True(dispenser.Passes.Count >= 2);
        Assert.All(dispenser.Passes, pass => Assert.InRange(pass.Total, 1, 4));
    }

    // ----- Dépôt -----

    [Fact]
    public void Deposit_without_notes_is_invalid()
    {
        var stock = new NoteCounts(1, 1, 1);
        var (result, dispenser) = Run(10, 0, stock, null, Deposit(0, 0, 0));

        AssertRefused(AtmStatus.InvalidAmount, result, new AccountState(10, 0), dispenser, stock);
    }

    [Fact]
    public void Deposit_with_negative_notes_is_invalid()
    {
        var (result, _) = Run(10, 0, new NoteCounts(1, 1, 1), null, Deposit(-1, 1, 1));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void Empty_deposit_is_invalid_even_when_jammed()
    {
        var (result, _) = Run(10, 0, new NoteCounts(5, 5, 5), Denomination.Ten, Deposit(0, 0, 0));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void Jammed_comes_before_full()
    {
        var (result, _) = Run(10, 0, new NoteCounts(5, 5, 5), Denomination.Fifty, Deposit(1, 1, 0));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
    }

    [Fact]
    public void Deposit_avoiding_jammed_cassette_succeeds()
    {
        var (result, dispenser) = Run(0, 50, new NoteCounts(0, 0, 0), Denomination.Fifty, Deposit(0, 1, 1));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(30, 50), result.Account);
        Assert.Equal(new NoteCounts(0, 1, 1), dispenser.Stock);
    }

    // ----- Pannes -----

    [Fact]
    public void Dispenser_fault_on_first_pass_returns_nothing()
    {
        var dispenser = new FaultyDispenser(new NoteCounts(5, 5, 5), failOnDispenseCall: 1);
        var account = new AccountState(250, 0);
        var result = new CashMachine(dispenser).Execute(account, Withdraw(50));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
    }

    [Fact]
    public void Dispenser_fault_mid_withdrawal_returns_notes_already_dispensed()
    {
        var dispenser = new FaultyDispenser(new NoteCounts(3, 2, 1), failOnDispenseCall: 2);
        var account = new AccountState(250, 0);
        var result = new CashMachine(dispenser).Execute(account, Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(dispenser.Dispensed, result.Notes);
        Assert.InRange(result.Notes.Total, 1, 4);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
    }

    [Fact]
    public void Dispenser_fault_on_deposit()
    {
        var dispenser = new FaultyDispenser(new NoteCounts(0, 0, 0), failOnDispenseCall: 0, failLoad: true);
        var account = new AccountState(10, 30);
        var result = new CashMachine(dispenser).Execute(account, Deposit(1, 1, 1));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
    }

    // ----- Divers -----

    [Fact]
    public void Null_operation_throws()
    {
        var machine = new CashMachine(new MoneyDispenser(NoteCounts.None));

        Assert.Throws<ArgumentNullException>(() => machine.Execute(new AccountState(0, 0), null!));
    }

    [Fact]
    public void Unknown_operation_throws()
    {
        var machine = new CashMachine(new MoneyDispenser(NoteCounts.None));

        Assert.Throws<ArgumentException>(() => machine.Execute(new AccountState(0, 0), new UnknownOperation()));
    }

    private sealed record UnknownOperation : Operation;

    // ----- Parcours exhaustif du domaine de retrait (oracle indépendant) -----

    [Fact]
    public void Withdraw_matches_oracle_over_stock_jam_and_amount_domain()
    {
        int[] amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];
        Denomination?[] jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];
        (int Balance, int Withdrawn)[] accounts = [(250, 0), (100, 50), (105, 0), (0, 200)];

        foreach (var (balance, withdrawn) in accounts)
        foreach (var jammed in jams)
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
        foreach (var amount in amounts)
        {
            var stock = new NoteCounts(f, t, n);
            var (result, dispenser) = Run(balance, withdrawn, stock, jammed, Withdraw(amount));
            var (status, notes, fee) = Oracle(balance, withdrawn, stock, jammed, amount);

            Assert.Equal(status, result.Status);
            Assert.Equal(notes, result.Notes);
            Assert.Equal(fee, result.Fee);
            var expectedAccount = status == AtmStatus.Ok
                ? new AccountState(balance - amount - fee, withdrawn + amount)
                : new AccountState(balance, withdrawn);
            Assert.Equal(expectedAccount, result.Account);
            Assert.Equal(stock - notes, dispenser.Stock);
        }
    }

    private static (AtmStatus, NoteCounts, int) Oracle(int balance, int withdrawn, NoteCounts stock,
        Denomination? jammed, int amount)
    {
        if (amount <= 0 || amount % 10 != 0) return (AtmStatus.InvalidAmount, default, 0);
        if (withdrawn + amount > 200) return (AtmStatus.DailyLimitExceeded, default, 0);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > balance) return (AtmStatus.InsufficientFunds, default, 0);

        var candidates = new List<NoteCounts>();
        for (var f = 0; f <= (jammed == Denomination.Fifty ? 0 : stock.Fifties); f++)
        for (var t = 0; t <= (jammed == Denomination.Twenty ? 0 : stock.Twenties); t++)
        for (var n = 0; n <= (jammed == Denomination.Ten ? 0 : stock.Tens); n++)
        {
            var c = new NoteCounts(f, t, n);
            if (c.Value == amount) candidates.Add(c);
        }

        if (candidates.Count == 0) return (AtmStatus.CannotDispense, default, 0);
        var min = candidates.Min(c => c.Total);
        var best = Assert.Single(candidates, c => c.Total == min);

        return (AtmStatus.Ok, best, fee);
    }

    // ----- Doubles de test -----

    private sealed class RecordingDispenser(IMoneyDispenser inner) : IMoneyDispenser
    {
        public List<NoteCounts> Passes { get; } = [];
        public NoteCounts Stock => inner.Stock;
        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            Passes.Add(notes);
            return inner.Dispense(notes);
        }

        public bool Load(NoteCounts notes) => inner.Load(notes);
    }

    private sealed class FaultyDispenser(NoteCounts stock, int failOnDispenseCall, bool failLoad = false)
        : IMoneyDispenser
    {
        private readonly MoneyDispenser inner = new(stock);
        private int calls;

        public NoteCounts Dispensed { get; private set; }
        public NoteCounts Stock => inner.Stock;
        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            if (++calls == failOnDispenseCall || !inner.Dispense(notes)) return false;
            Dispensed += notes;
            return true;
        }

        public bool Load(NoteCounts notes) => !failLoad && inner.Load(notes);
    }
}
