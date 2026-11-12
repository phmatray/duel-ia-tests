namespace Atm.Tests;

public class CashMachineTests
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    private static AtmResult Run(int balance, int withdrawn, NoteCounts stock, Denomination? jammed, Operation op, out SpyDispenser spy)
    {
        spy = new SpyDispenser(stock, jammed);

        return new CashMachine(spy).Execute(new AccountState(balance, withdrawn), op);
    }

    private static void AssertRefused(AtmResult result, AtmStatus status, int balance, int withdrawn, NoteCounts stock, SpyDispenser spy)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(balance, withdrawn), result.Account);
        Assert.Equal(stock, spy.Stock);
        Assert.Equal(0, spy.Calls);
    }

    // ---- Exemples de la spécification ----

    public static TheoryData<int, int, NoteCounts, Denomination?, int, NoteCounts, int, int, int> WithdrawOkExamples => new()
    {
        { 100, 0, new(1, 3, 0), null, 60, new(0, 3, 0), 0, 40, 60 },
        { 100, 0, new(1, 1, 1), null, 80, new(1, 1, 1), 0, 20, 80 },
        { 105, 0, Full, null, 100, new(2, 0, 0), 0, 5, 100 },
        { 115, 0, Full, null, 110, new(2, 0, 1), 5, 0, 110 },
        { 250, 0, new(2, 2, 0), Denomination.Fifty, 40, new(0, 2, 0), 0, 210, 40 },
    };

    [Theory]
    [MemberData(nameof(WithdrawOkExamples))]
    public void Withdraw_spec_examples_succeed(
        int balance, int withdrawn, NoteCounts stock, Denomination? jammed, int amount,
        NoteCounts expectedNotes, int expectedFee, int expectedBalance, int expectedWithdrawn)
    {
        var result = Run(balance, withdrawn, stock, jammed, new Operation.Withdraw(amount), out var spy);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedNotes, result.Notes);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(new AccountState(expectedBalance, expectedWithdrawn), result.Account);
        Assert.Equal(stock - expectedNotes, spy.Stock);
    }

    public static TheoryData<int, int, NoteCounts, Denomination?, int, AtmStatus> WithdrawRefusedExamples => new()
    {
        { 110, 0, Full, null, 110, AtmStatus.InsufficientFunds },
        { 250, 150, Full, null, 60, AtmStatus.DailyLimitExceeded },
        { 250, 0, new(1, 0, 0), Denomination.Fifty, 50, AtmStatus.CannotDispense },
        { 0, 200, new(0, 0, 0), null, 5, AtmStatus.InvalidAmount },
    };

    [Theory]
    [MemberData(nameof(WithdrawRefusedExamples))]
    public void Withdraw_spec_examples_refused(int balance, int withdrawn, NoteCounts stock, Denomination? jammed, int amount, AtmStatus status)
    {
        var result = Run(balance, withdrawn, stock, jammed, new Operation.Withdraw(amount), out var spy);

        AssertRefused(result, status, balance, withdrawn, stock, spy);
    }

    [Fact]
    public void Deposit_spec_example_cassette_full()
    {
        var stock = new NoteCounts(0, 5, 0);
        var result = Run(10, 0, stock, null, new Operation.Deposit(new(0, 1, 0)), out var spy);

        AssertRefused(result, AtmStatus.CassetteFull, 10, 0, stock, spy);
    }

    [Fact]
    public void Deposit_spec_example_cassette_jammed()
    {
        var stock = new NoteCounts(0, 1, 0);
        var result = Run(10, 30, stock, Denomination.Twenty, new Operation.Deposit(new(0, 1, 0)), out var spy);

        AssertRefused(result, AtmStatus.CassetteJammed, 10, 30, stock, spy);
    }

    [Fact]
    public void Deposit_spec_example_ok()
    {
        var result = Run(10, 30, new(1, 1, 0), null, new Operation.Deposit(new(1, 0, 1)), out var spy);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(70, 30), result.Account);
        Assert.Equal(new NoteCounts(2, 1, 1), spy.Stock);
    }

    // ---- Retrait : contrôles et leur ordre ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void Withdraw_invalid_amount(int amount)
    {
        var result = Run(250, 0, Full, null, new Operation.Withdraw(amount), out var spy);

        AssertRefused(result, AtmStatus.InvalidAmount, 250, 0, Full, spy);
    }

    [Fact]
    public void Withdraw_invalid_amount_is_checked_before_daily_limit()
    {
        var result = Run(0, 200, new(0, 0, 0), null, new Operation.Withdraw(-10), out var spy);

        AssertRefused(result, AtmStatus.InvalidAmount, 0, 200, new(0, 0, 0), spy);
    }

    [Theory]
    [InlineData(0, 200, true)]
    [InlineData(0, 210, false)]
    [InlineData(150, 50, true)]
    [InlineData(150, 60, false)]
    [InlineData(200, 10, false)]
    public void Withdraw_daily_limit_boundary(int withdrawn, int amount, bool allowed)
    {
        var result = Run(250, withdrawn, Full, null, new Operation.Withdraw(amount), out _);

        Assert.Equal(allowed ? AtmStatus.Ok : AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void Withdraw_fee_does_not_count_toward_daily_limit()
    {
        var result = Run(250, 0, Full, null, new Operation.Withdraw(200), out _);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(45, 200), result.Account);
    }

    [Fact]
    public void Withdraw_daily_limit_is_checked_before_funds()
    {
        var result = Run(0, 150, Full, null, new Operation.Withdraw(60), out var spy);

        AssertRefused(result, AtmStatus.DailyLimitExceeded, 0, 150, Full, spy);
    }

    [Theory]
    [InlineData(100, 100, 0, true)]
    [InlineData(95, 100, 0, false)]
    [InlineData(115, 110, 5, true)]
    [InlineData(114, 110, 5, false)]
    [InlineData(110, 110, 5, false)]
    public void Withdraw_funds_include_fee(int balance, int amount, int fee, bool allowed)
    {
        var result = Run(balance, 0, Full, null, new Operation.Withdraw(amount), out _);

        Assert.Equal(allowed ? AtmStatus.Ok : AtmStatus.InsufficientFunds, result.Status);
        if (allowed)
        {
            Assert.Equal(fee, result.Fee);
            Assert.Equal(balance - amount - fee, result.Account.Balance);
        }
    }

    [Fact]
    public void Withdraw_funds_are_checked_before_dispensability()
    {
        var empty = new NoteCounts(0, 0, 0);
        var result = Run(10, 0, empty, null, new Operation.Withdraw(20), out var spy);

        AssertRefused(result, AtmStatus.InsufficientFunds, 10, 0, empty, spy);
    }

    [Theory]
    [InlineData(0, 0, 0, null, 10)]
    [InlineData(5, 5, 0, null, 10)]
    [InlineData(0, 5, 5, Denomination.Ten, 30)]
    [InlineData(0, 5, 5, Denomination.Twenty, 60)]
    [InlineData(1, 0, 0, null, 30)]
    public void Withdraw_cannot_dispense(int fifties, int twenties, int tens, Denomination? jammed, int amount)
    {
        var stock = new NoteCounts(fifties, twenties, tens);
        var result = Run(250, 0, stock, jammed, new Operation.Withdraw(amount), out var spy);

        AssertRefused(result, AtmStatus.CannotDispense, 250, 0, stock, spy);
    }

    [Fact]
    public void Withdraw_picks_the_combination_with_fewest_notes_not_greedy()
    {
        // 60 : la gloutonne prendrait 50 puis échouerait sans billets de 10 ; il faut 20×3.
        var result = Run(250, 0, new(1, 5, 0), null, new Operation.Withdraw(60), out _);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(0, 3, 0), result.Notes);
    }

    [Fact]
    public void Withdraw_prefers_fewer_notes_over_large_denominations_first()
    {
        // 80 : 50+10+10+10 (4 billets) contre 20×4 (4) ou 50+20+10 (3).
        var result = Run(250, 0, new(1, 1, 3), null, new Operation.Withdraw(80), out _);

        Assert.Equal(new NoteCounts(1, 1, 1), result.Notes);
    }

    [Fact]
    public void Withdraw_large_amount_is_split_into_passes_of_at_most_four_notes()
    {
        var result = Run(250, 0, Full, null, new Operation.Withdraw(200), out var spy);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(4, 0, 0), result.Notes);
        Assert.All(spy.Dispensed, pass => Assert.InRange(pass.Total, 1, 4));
    }

    [Fact]
    public void Withdraw_with_many_notes_uses_several_passes()
    {
        // 150 sans billets de 50 : 20×5 + 10×5, soit 10 billets, donc au moins 3 passages.
        var result = Run(250, 0, new(0, 5, 5), null, new Operation.Withdraw(150), out var spy);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(0, 5, 5), result.Notes);
        Assert.True(spy.Dispensed.Count >= 3);
        Assert.All(spy.Dispensed, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(result.Notes, spy.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(new NoteCounts(0, 0, 0), spy.Stock);
    }

    // ---- Dépôt : contrôles et leur ordre ----

    [Fact]
    public void Deposit_empty_is_invalid_amount()
    {
        var result = Run(10, 0, Full, Denomination.Ten, new Operation.Deposit(NoteCounts.None), out var spy);

        AssertRefused(result, AtmStatus.InvalidAmount, 10, 0, Full, spy);
    }

    [Fact]
    public void Deposit_negative_counts_are_invalid()
    {
        var stock = new NoteCounts(1, 1, 1);
        var result = Run(10, 0, stock, null, new Operation.Deposit(new(-1, 1, 1)), out var spy);

        AssertRefused(result, AtmStatus.InvalidAmount, 10, 0, stock, spy);
    }

    [Fact]
    public void Deposit_jammed_is_checked_before_full()
    {
        var result = Run(10, 0, Full, Denomination.Fifty, new Operation.Deposit(new(1, 1, 1)), out var spy);

        AssertRefused(result, AtmStatus.CassetteJammed, 10, 0, Full, spy);
    }

    [Fact]
    public void Deposit_full_in_one_cassette_refuses_whole_deposit()
    {
        var stock = new NoteCounts(0, 0, 5);
        var result = Run(10, 0, stock, Denomination.Twenty, new Operation.Deposit(new(1, 0, 1)), out var spy);

        AssertRefused(result, AtmStatus.CassetteFull, 10, 0, stock, spy);
    }

    [Fact]
    public void Deposit_with_jammed_cassette_not_touched_succeeds()
    {
        var result = Run(0, 200, new(4, 0, 4), Denomination.Twenty, new Operation.Deposit(new(1, 0, 1)), out var spy);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(60, 200), result.Account);
        Assert.Equal(new NoteCounts(5, 0, 5), spy.Stock);
    }

    // ---- Pannes ----

    [Fact]
    public void Withdraw_fault_on_first_pass_returns_no_notes()
    {
        var spy = new SpyDispenser(Full, failOnCall: 1);
        var account = new AccountState(250, 0);

        var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(200));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal(1, spy.Calls);
    }

    [Fact]
    public void Withdraw_fault_on_later_pass_returns_notes_already_dispensed_and_stops()
    {
        var spy = new SpyDispenser(new(0, 5, 5), failOnCall: 2);
        var account = new AccountState(250, 0);

        var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(spy.Dispensed.Single(), result.Notes);
        Assert.Equal(new NoteCounts(0, 5, 5) - result.Notes, spy.Stock);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(2, spy.Calls);
    }

    [Fact]
    public void Deposit_fault_returns_no_notes_and_unchanged_account()
    {
        var spy = new SpyDispenser(new(0, 0, 0), failOnCall: 1);
        var account = new AccountState(10, 30);

        var result = new CashMachine(spy).Execute(account, new Operation.Deposit(new(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal(new NoteCounts(0, 0, 0), spy.Stock);
    }

    [Fact]
    public void Unknown_operation_is_rejected()
    {
        var machine = new CashMachine(new MoneyDispenser(Full));

        Assert.Throws<ArgumentException>(() => machine.Execute(new AccountState(0, 0), new UnknownOperation()));
    }

    private sealed record UnknownOperation : Operation;
}
