namespace Atm.Tests;

using static Notes;

public class SpecExamplesTests
{
    [Fact]
    public void Withdraw60_UsesThreeTwenties()
    {
        var dispenser = new MoneyDispenser(Of(1, 3, 0));
        var result = new CashMachine(dispenser).Execute(new(100, 0), new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(0, 3, 0), 0, new(40, 60)), result);
        Assert.Equal(Of(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw80_UsesOneOfEach()
    {
        var dispenser = new MoneyDispenser(Of(1, 1, 1));
        var result = new CashMachine(dispenser).Execute(new(100, 0), new Operation.Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(1, 1, 1), 0, new(20, 80)), result);
        Assert.Equal(Of(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw100_HasNoFee()
    {
        var result = new CashMachine(new MoneyDispenser(Of(5, 5, 5))).Execute(new(105, 0), new Operation.Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(2, 0, 0), 0, new(5, 100)), result);
    }

    [Fact]
    public void Withdraw110_HasFee()
    {
        var result = new CashMachine(new MoneyDispenser(Of(5, 5, 5))).Execute(new(115, 0), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(2, 0, 1), 5, new(0, 110)), result);
    }

    [Fact]
    public void Withdraw110_FeeMakesFundsInsufficient()
    {
        var result = new CashMachine(new MoneyDispenser(Of(5, 5, 5))).Execute(new(110, 0), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, default, 0, new(110, 0)), result);
    }

    [Fact]
    public void Withdraw60_OverDailyLimit()
    {
        var result = new CashMachine(new MoneyDispenser(Of(5, 5, 5))).Execute(new(250, 150), new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, default, 0, new(250, 150)), result);
    }

    [Fact]
    public void Withdraw50_OnlyFromJammedCassette_CannotDispense()
    {
        var dispenser = new MoneyDispenser(Of(1, 0, 0), Denomination.Fifty);
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, default, 0, new(250, 0)), result);
        Assert.Equal(Of(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw40_AvoidsJammedCassette()
    {
        var dispenser = new MoneyDispenser(Of(2, 2, 0), Denomination.Fifty);
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(0, 2, 0), 0, new(210, 40)), result);
        Assert.Equal(Of(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw5_InvalidAmount()
    {
        var result = new CashMachine(new MoneyDispenser(Of(0, 0, 0))).Execute(new(0, 200), new Operation.Withdraw(5));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, default, 0, new(0, 200)), result);
    }

    [Fact]
    public void Deposit_IntoFullCassette()
    {
        var dispenser = new MoneyDispenser(Of(0, 5, 0));
        var result = new CashMachine(dispenser).Execute(new(10, 0), new Operation.Deposit(Of(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, default, 0, new(10, 0)), result);
        Assert.Equal(Of(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_IntoJammedCassette()
    {
        var dispenser = new MoneyDispenser(Of(0, 1, 0), Denomination.Twenty);
        var result = new CashMachine(dispenser).Execute(new(10, 30), new Operation.Deposit(Of(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, default, 0, new(10, 30)), result);
        Assert.Equal(Of(0, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_Succeeds()
    {
        var dispenser = new MoneyDispenser(Of(1, 1, 0));
        var result = new CashMachine(dispenser).Execute(new(10, 30), new Operation.Deposit(Of(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, default, 0, new(70, 30)), result);
        Assert.Equal(Of(2, 1, 1), dispenser.Stock);
    }
}
