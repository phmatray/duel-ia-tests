using static Atm.Tests.N;

namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, SpyDispenser Dispenser) Run(
        AccountState account, NoteCounts deposit, NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new SpyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(deposit));

        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AccountState account, AtmResult result, SpyDispenser dispenser, NoteCounts stockBefore)
    {
        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
        Assert.Equal(stockBefore, dispenser.Stock);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
    }

    // ---- Contrôle 1 : dépôt vide ----

    [Theory]
    [InlineData(0, 0, 0, null)]
    [InlineData(5, 5, 5, null)]
    [InlineData(1, 1, 1, 20)]
    public void Empty_deposit_is_invalid(int s50, int s20, int s10, int? jammed)
    {
        var account = Account(50, 30);
        var stock = Notes(s50, s20, s10);
        var (result, dispenser) = Run(account, NoteCounts.None, stock, Jam(jammed));

        AssertRefused(AtmStatus.InvalidAmount, account, result, dispenser, stock);
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(10, 1, 1, 1)]
    public void Deposit_containing_a_note_of_the_jammed_cassette_is_refused(int jammed, int d50, int d20, int d10)
    {
        var account = Account(50, 30);
        var stock = Notes(1, 1, 1);
        var (result, dispenser) = Run(account, Notes(d50, d20, d10), stock, Jam(jammed));

        AssertRefused(AtmStatus.CassetteJammed, account, result, dispenser, stock);
    }

    [Fact]
    public void Jammed_is_checked_before_full()
    {
        var account = Account(50, 30);
        var stock = Notes(5, 5, 5);
        var (result, dispenser) = Run(account, Notes(1, 1, 1), stock, Denomination.Ten);

        AssertRefused(AtmStatus.CassetteJammed, account, result, dispenser, stock);
    }

    [Fact]
    public void Jammed_is_checked_before_full_when_another_cassette_would_overflow()
    {
        var account = Account(0, 0);
        var stock = Notes(5, 0, 0);
        var (result, dispenser) = Run(account, Notes(1, 1, 0), stock, Denomination.Twenty);

        AssertRefused(AtmStatus.CassetteJammed, account, result, dispenser, stock);
    }

    [Fact]
    public void Deposit_avoiding_the_jammed_cassette_is_accepted()
    {
        var (result, dispenser) = Run(Account(50, 30), Notes(1, 0, 1), Notes(1, 1, 1), Denomination.Twenty);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(110, 30)), result);
        Assert.Equal(Notes(2, 1, 2), dispenser.Stock);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(4, 4, 5, 1, 1, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)]
    public void Deposit_overflowing_a_cassette_is_refused(int s50, int s20, int s10, int d50, int d20, int d10)
    {
        var account = Account(50, 30);
        var stock = Notes(s50, s20, s10);
        var (result, dispenser) = Run(account, Notes(d50, d20, d10), stock);

        AssertRefused(AtmStatus.CassetteFull, account, result, dispenser, stock);
    }

    [Fact]
    public void Deposit_filling_cassettes_exactly_to_five_is_accepted()
    {
        var (result, dispenser) = Run(Account(0, 200), Notes(1, 1, 1), Notes(4, 4, 4));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(80, 200)), result);
        Assert.Equal(Full, dispenser.Stock);
    }

    // ---- Le succès ----

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 1, 1, 80)]
    [InlineData(0, 1, 1, 30)]
    public void Successful_deposit_credits_value_and_loads_notes(int d50, int d20, int d10, int value)
    {
        var stock = Notes(2, 3, 0);
        var deposit = Notes(d50, d20, d10);
        var (result, dispenser) = Run(Account(25, 120), deposit, stock);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(25 + value, 120)), result);
        Assert.Equal(stock + deposit, dispenser.Stock);
        Assert.Equal(deposit, dispenser.LoadCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Empty(dispenser.DispenseCalls);
    }

    // ---- Pannes ----

    [Fact]
    public void Load_failure_is_a_dispenser_fault_with_nothing_changed()
    {
        var account = Account(50, 30);
        var stock = Notes(1, 1, 1);
        var dispenser = new SpyDispenser(stock) { FailLoad = true };

        var result = new CashMachine(dispenser).Execute(account, Deposit(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Single(dispenser.LoadCalls);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.DispenseCalls);
    }
}
