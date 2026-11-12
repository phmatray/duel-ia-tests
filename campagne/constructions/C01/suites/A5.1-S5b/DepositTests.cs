namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, RecordingDispenser Dispenser) Run(
        AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        var dispenser = RecordingDispenser.Real(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        var account = new AccountState(100, 50);
        var (result, dispenser) = Run(account, stock, jammed, notes);
        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.Dispensed);
    }

    [Fact]
    public void Empty_deposit_is_invalid() =>
        AssertRefused(AtmStatus.InvalidAmount, new NoteCounts(1, 1, 1), null, NoteCounts.None);

    [Theory]
    [InlineData(50)]
    [InlineData(20)]
    [InlineData(10)]
    public void Empty_deposit_is_checked_before_jam_and_capacity(int jam) =>
        AssertRefused(AtmStatus.InvalidAmount, new NoteCounts(5, 5, 5), (Denomination)jam, NoteCounts.None);

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(20, 1, 1, 0)]
    [InlineData(10, 0, 1, 1)]
    public void Deposit_touching_the_jammed_cassette_is_refused(int jam, int f, int t, int d) =>
        AssertRefused(AtmStatus.CassetteJammed, new NoteCounts(1, 1, 1), (Denomination)jam, new NoteCounts(f, t, d));

    [Fact]
    public void Jam_is_checked_before_capacity_on_the_same_cassette() =>
        AssertRefused(AtmStatus.CassetteJammed, new NoteCounts(0, 5, 0), Denomination.Twenty, new NoteCounts(0, 1, 0));

    [Fact]
    public void Jam_is_checked_before_capacity_on_another_cassette() =>
        AssertRefused(AtmStatus.CassetteJammed, new NoteCounts(5, 0, 0), Denomination.Ten, new NoteCounts(1, 0, 1));

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // un seul billet en trop suffit, tout le dépôt est refusé
    [InlineData(0, 0, 5, 1, 1, 1)]
    public void Deposit_overflowing_a_cassette_is_refused(int sf, int st, int sd, int f, int t, int d) =>
        AssertRefused(AtmStatus.CassetteFull, new NoteCounts(sf, st, sd), null, new NoteCounts(f, t, d));

    [Fact]
    public void Deposit_overflowing_a_cassette_while_another_is_jammed_is_full() =>
        AssertRefused(AtmStatus.CassetteFull, new NoteCounts(5, 0, 0), Denomination.Ten, new NoteCounts(1, 1, 0));

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 1, 1, 80)]
    public void Successful_deposit_credits_balance_and_loads_notes(int f, int t, int d, int value)
    {
        var stock = new NoteCounts(4, 4, 4);
        var notes = new NoteCounts(f, t, d);
        var account = new AccountState(35, 120);

        var (result, dispenser) = Run(account, stock, null, notes);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(35 + value, 120)), result);
        Assert.Equal(stock + notes, dispenser.Stock);
        Assert.Empty(dispenser.Dispensed);
    }

    [Fact]
    public void Deposit_filling_a_cassette_exactly_is_allowed()
    {
        var (result, dispenser) = Run(new AccountState(0, 0), new NoteCounts(4, 4, 4), null, new NoteCounts(1, 1, 1));
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Deposit_avoiding_the_jammed_cassette_is_allowed()
    {
        var (result, dispenser) = Run(new AccountState(250, 200), new NoteCounts(0, 5, 0), Denomination.Twenty,
            new NoteCounts(1, 0, 1));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(310, 200)), result);
        Assert.Equal(new NoteCounts(1, 5, 1), dispenser.Stock);
    }
}
