namespace Atm.Tests;

using static Notes;

public class DepositTests
{
    [Fact]
    public void EmptyDeposit_IsInvalid()
    {
        var dispenser = new FakeDispenser(Of(5, 5, 5), Denomination.Ten);
        var result = new CashMachine(dispenser).Execute(new(10, 0), new Operation.Deposit(NoteCounts.None));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, default, 0, new(10, 0)), result);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void NegativeCounts_AreInvalid()
    {
        var result = new CashMachine(new MoneyDispenser(Of(1, 1, 1))).Execute(new(10, 0), new Operation.Deposit(Of(1, -1, 1)));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void Jammed_IsCheckedBeforeFull()
    {
        var dispenser = new FakeDispenser(Of(5, 5, 5), Denomination.Fifty);
        var result = new CashMachine(dispenser).Execute(new(10, 0), new Operation.Deposit(Of(1, 1, 1)));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void JammedCassette_NotTouched_DepositSucceeds()
    {
        var dispenser = new MoneyDispenser(Of(0, 0, 0), Denomination.Fifty);
        var result = new CashMachine(dispenser).Execute(new(0, 100), new Operation.Deposit(Of(0, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, default, 0, new(30, 100)), result);
        Assert.Equal(Of(0, 1, 1), dispenser.Stock);
    }

    [Theory]
    [InlineData(5, 0, 0)]
    [InlineData(0, 5, 0)]
    [InlineData(0, 0, 5)]
    public void AnyFullCassette_IsRefused(int fifties, int twenties, int tens)
    {
        var dispenser = new FakeDispenser(Of(fifties, twenties, tens));
        var result = new CashMachine(dispenser).Execute(new(10, 0), new Operation.Deposit(Of(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, default, 0, new(10, 0)), result);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void FillingCassetteToCapacity_IsAllowed()
    {
        var dispenser = new MoneyDispenser(Of(4, 4, 4));
        var result = new CashMachine(dispenser).Execute(new(250, 200), new Operation.Deposit(Of(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, default, 0, new(330, 200)), result);
        Assert.Equal(Of(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Deposit_LoadsInOneCall()
    {
        var dispenser = new FakeDispenser(Of(0, 0, 0));
        new CashMachine(dispenser).Execute(new(0, 0), new Operation.Deposit(Of(1, 1, 1)));

        Assert.Equal([Of(1, 1, 1)], dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Fact]
    public void LoadFailure_IsADispenserFault()
    {
        var dispenser = new FakeDispenser(Of(0, 0, 0)) { FailLoad = true };
        var result = new CashMachine(dispenser).Execute(new(10, 30), new Operation.Deposit(Of(1, 0, 0)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, default, 0, new(10, 30)), result);
    }
}
