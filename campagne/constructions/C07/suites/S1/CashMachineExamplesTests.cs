namespace Atm.Tests;

/// <summary>Les exemples de la spécification, un par un.</summary>
public class CashMachineExamplesTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static (AtmResult Result, MoneyDispenser Dispenser) Run(
        int balance, int withdrawn, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), operation);
        return (result, dispenser);
    }

    private static void AssertRefused(AtmResult result, AtmStatus status, int balance, int withdrawn,
        MoneyDispenser dispenser, NoteCounts stock)
    {
        Assert.Equal(new AtmResult(status, NoteCounts.None, 0, new AccountState(balance, withdrawn)), result);
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void Withdraw60_WithOnlyTwenties_GivesThreeTwenties()
    {
        var (result, dispenser) = Run(100, 0, N(1, 3, 0), null, new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, new AccountState(40, 60)), result);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw80_UsesOneOfEach()
    {
        var (result, dispenser) = Run(100, 0, N(1, 1, 1), null, new Operation.Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, new AccountState(20, 80)), result);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw100_HasNoFee()
    {
        var (result, dispenser) = Run(105, 0, N(5, 5, 5), null, new Operation.Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, new AccountState(5, 100)), result);
        Assert.Equal(N(3, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Withdraw110_HasFiveEuroFee()
    {
        var (result, dispenser) = Run(115, 0, N(5, 5, 5), null, new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, new AccountState(0, 110)), result);
        Assert.Equal(N(3, 5, 4), dispenser.Stock);
    }

    [Fact]
    public void Withdraw110_WithoutRoomForFee_IsInsufficientFunds()
    {
        var (result, dispenser) = Run(110, 0, N(5, 5, 5), null, new Operation.Withdraw(110));

        AssertRefused(result, AtmStatus.InsufficientFunds, 110, 0, dispenser, N(5, 5, 5));
    }

    [Fact]
    public void Withdraw_OverDailyLimit_IsRefused()
    {
        var (result, dispenser) = Run(250, 150, N(5, 5, 5), null, new Operation.Withdraw(60));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, 250, 150, dispenser, N(5, 5, 5));
    }

    [Fact]
    public void Withdraw_FromJammedCassetteOnly_CannotDispense()
    {
        var (result, dispenser) = Run(250, 0, N(1, 0, 0), Denomination.Fifty, new Operation.Withdraw(50));

        AssertRefused(result, AtmStatus.CannotDispense, 250, 0, dispenser, N(1, 0, 0));
    }

    [Fact]
    public void Withdraw_AvoidsJammedCassette()
    {
        var (result, dispenser) = Run(250, 0, N(2, 2, 0), Denomination.Fifty, new Operation.Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, new AccountState(210, 40)), result);
        Assert.Equal(N(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw5_IsInvalidAmount_BeforeAnyOtherCheck()
    {
        var (result, dispenser) = Run(0, 200, N(0, 0, 0), null, new Operation.Withdraw(5));

        AssertRefused(result, AtmStatus.InvalidAmount, 0, 200, dispenser, N(0, 0, 0));
    }

    [Fact]
    public void Deposit_IntoFullCassette_IsCassetteFull()
    {
        var (result, dispenser) = Run(10, 0, N(0, 5, 0), null, new Operation.Deposit(N(0, 1, 0)));

        AssertRefused(result, AtmStatus.CassetteFull, 10, 0, dispenser, N(0, 5, 0));
    }

    [Fact]
    public void Deposit_IntoJammedCassette_IsCassetteJammed()
    {
        var (result, dispenser) = Run(10, 30, N(0, 1, 0), Denomination.Twenty, new Operation.Deposit(N(0, 1, 0)));

        AssertRefused(result, AtmStatus.CassetteJammed, 10, 30, dispenser, N(0, 1, 0));
    }

    [Fact]
    public void Deposit_LoadsNotesAndCreditsBalance()
    {
        var (result, dispenser) = Run(10, 30, N(1, 1, 0), null, new Operation.Deposit(N(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(70, 30)), result);
        Assert.Equal(N(2, 1, 1), dispenser.Stock);
    }
}
