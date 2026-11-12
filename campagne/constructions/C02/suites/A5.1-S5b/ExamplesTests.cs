namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class ExamplesTests
{
    private static (AtmResult Result, MoneyDispenser Dispenser) Run(int balance, int withdrawn, NoteCounts stock, Denomination? jammed, Operation op)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), op);
        return (result, dispenser);
    }

    [Fact]
    public void Withdraw60_WithoutTens_GivesThreeTwenties()
    {
        var (r, d) = Run(100, 0, N.Of(1, 3, 0), null, new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N.Of(0, 3, 0), 0, new(40, 60)), r);
        Assert.Equal(N.Of(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Withdraw80_GivesOneOfEach()
    {
        var (r, d) = Run(100, 0, N.Of(1, 1, 1), null, new Operation.Withdraw(80));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N.Of(1, 1, 1), 0, new(20, 80)), r);
        Assert.Equal(N.Of(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Withdraw100_NoFee()
    {
        var (r, _) = Run(105, 0, N.Of(5, 5, 5), null, new Operation.Withdraw(100));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N.Of(2, 0, 0), 0, new(5, 100)), r);
    }

    [Fact]
    public void Withdraw110_Fee5()
    {
        var (r, d) = Run(115, 0, N.Of(5, 5, 5), null, new Operation.Withdraw(110));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N.Of(2, 0, 1), 5, new(0, 110)), r);
        Assert.Equal(N.Of(3, 5, 4), d.Stock);
    }

    [Fact]
    public void Withdraw110_Balance110_InsufficientFunds()
    {
        var (r, d) = Run(110, 0, N.Of(5, 5, 5), null, new Operation.Withdraw(110));
        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, new(110, 0)), r);
        Assert.Equal(N.Of(5, 5, 5), d.Stock);
    }

    [Fact]
    public void Withdraw60_After150_DailyLimitExceeded()
    {
        var (r, d) = Run(250, 150, N.Of(5, 5, 5), null, new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, new(250, 150)), r);
        Assert.Equal(N.Of(5, 5, 5), d.Stock);
    }

    [Fact]
    public void Withdraw50_FiftyJammed_CannotDispense()
    {
        var (r, d) = Run(250, 0, N.Of(1, 0, 0), Denomination.Fifty, new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new(250, 0)), r);
        Assert.Equal(N.Of(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Withdraw40_FiftyJammed_UsesTwenties()
    {
        var (r, d) = Run(250, 0, N.Of(2, 2, 0), Denomination.Fifty, new Operation.Withdraw(40));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N.Of(0, 2, 0), 0, new(210, 40)), r);
        Assert.Equal(N.Of(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Withdraw5_InvalidAmount()
    {
        var (r, _) = Run(0, 200, N.Of(0, 0, 0), null, new Operation.Withdraw(5));
        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, new(0, 200)), r);
    }

    [Fact]
    public void Deposit_IntoFullCassette_CassetteFull()
    {
        var (r, d) = Run(10, 0, N.Of(0, 5, 0), null, new Operation.Deposit(N.Of(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, new(10, 0)), r);
        Assert.Equal(N.Of(0, 5, 0), d.Stock);
    }

    [Fact]
    public void Deposit_IntoJammedCassette_CassetteJammed()
    {
        var (r, d) = Run(10, 30, N.Of(0, 1, 0), Denomination.Twenty, new Operation.Deposit(N.Of(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, new(10, 30)), r);
        Assert.Equal(N.Of(0, 1, 0), d.Stock);
    }

    [Fact]
    public void Deposit_FiftyAndTen_Ok()
    {
        var (r, d) = Run(10, 30, N.Of(1, 1, 0), null, new Operation.Deposit(N.Of(1, 0, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(70, 30)), r);
        Assert.Equal(N.Of(2, 1, 1), d.Stock);
    }
}
