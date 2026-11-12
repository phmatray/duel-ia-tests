namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, RecordingDispenser Dispenser) Run(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, NoteCounts notes, bool failLoad = false)
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed), failLoad: failLoad);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawnToday), new Operation.Deposit(notes));

        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult result, RecordingDispenser dispenser, AccountState account, NoteCounts stock)
    {
        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Fact]
    public void Deposit_IntoFullCassette_IsRefused()
    {
        var (result, dispenser) = Run(10, 0, new(0, 5, 0), null, new(0, 1, 0));

        AssertRefused(AtmStatus.CassetteFull, result, dispenser, new(10, 0), new(0, 5, 0));
    }

    [Fact]
    public void Deposit_IntoJammedCassette_IsRefused()
    {
        var (result, dispenser) = Run(10, 30, new(0, 1, 0), Denomination.Twenty, new(0, 1, 0));

        AssertRefused(AtmStatus.CassetteJammed, result, dispenser, new(10, 30), new(0, 1, 0));
    }

    [Fact]
    public void Deposit_Succeeds()
    {
        var (result, dispenser) = Run(10, 30, new(1, 1, 0), null, new(1, 0, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(70, 30)), result);
        Assert.Equal(new NoteCounts(2, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Deposit_WithJammedCassetteNotTouched_Succeeds()
    {
        var (result, dispenser) = Run(0, 0, new(5, 0, 0), Denomination.Fifty, new(0, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(30, 0)), result);
        Assert.Equal(new NoteCounts(5, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void EmptyDeposit_IsInvalidAmount_BeforeOtherChecks()
    {
        var (result, dispenser) = Run(10, 0, new(5, 5, 5), Denomination.Ten, NoteCounts.None);

        AssertRefused(AtmStatus.InvalidAmount, result, dispenser, new(10, 0), new(5, 5, 5));
    }

    [Fact]
    public void NegativeDeposit_IsInvalidAmount()
    {
        var (result, dispenser) = Run(10, 0, new(1, 1, 1), null, new(1, -1, 0));

        AssertRefused(AtmStatus.InvalidAmount, result, dispenser, new(10, 0), new(1, 1, 1));
    }

    [Fact]
    public void Jammed_IsCheckedBeforeFull()
    {
        var (result, _) = Run(10, 0, new(0, 0, 5), Denomination.Fifty, new(1, 0, 1));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
    }

    [Fact]
    public void Deposit_FillingCassetteExactly_Succeeds()
    {
        var (result, dispenser) = Run(0, 0, new(4, 4, 4), null, new(1, 1, 1));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(80, 0), result.Account);
        Assert.Equal(new NoteCounts(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void LoadFailure_IsDispenserFault()
    {
        var (result, dispenser) = Run(10, 20, new(1, 1, 1), null, new(1, 0, 0), failLoad: true);

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new(10, 20)), result);
        Assert.Single(dispenser.LoadCalls);
        Assert.Equal(new NoteCounts(1, 1, 1), dispenser.Stock);
    }
}
