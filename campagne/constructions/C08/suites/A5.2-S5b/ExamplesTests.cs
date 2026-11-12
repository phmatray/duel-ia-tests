using static Atm.Tests.Harness;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class ExamplesTests
{
    [Fact]
    public void Withdraw60_WithoutTens_GivesThreeTwenties()
    {
        var stock = N(1, 3, 0);
        var run = Withdraw(100, 0, stock, 60);
        AssertWithdrawOk(run, 100, 0, stock, 60, N(0, 3, 0), 0);
        Assert.Equal(new AccountState(40, 60), run.Result.Account);
    }

    [Fact]
    public void Withdraw80_GivesOneOfEach()
    {
        var stock = N(1, 1, 1);
        var run = Withdraw(100, 0, stock, 80);
        AssertWithdrawOk(run, 100, 0, stock, 80, N(1, 1, 1), 0);
        Assert.Equal(new AccountState(20, 80), run.Result.Account);
    }

    [Fact]
    public void Withdraw100_NoFee()
    {
        var run = Withdraw(105, 0, FullStock, 100);
        AssertWithdrawOk(run, 105, 0, FullStock, 100, N(2, 0, 0), 0);
        Assert.Equal(5, run.Result.Account.Balance);
    }

    [Fact]
    public void Withdraw110_FeeOfFive()
    {
        var run = Withdraw(115, 0, FullStock, 110);
        AssertWithdrawOk(run, 115, 0, FullStock, 110, N(2, 0, 1), 5);
        Assert.Equal(new AccountState(0, 110), run.Result.Account);
    }

    [Fact]
    public void Withdraw110_FeeMakesItInsufficient()
    {
        AssertRefused(AtmStatus.InsufficientFunds, Withdraw(110, 0, FullStock, 110), 110, 0, FullStock);
    }

    [Fact]
    public void Withdraw60_AfterAlready150_DailyLimitExceeded()
    {
        AssertRefused(AtmStatus.DailyLimitExceeded, Withdraw(250, 150, FullStock, 60), 250, 150, FullStock);
    }

    [Fact]
    public void Withdraw50_OnlyFiftyIsJammed_CannotDispense()
    {
        var stock = N(1, 0, 0);
        AssertRefused(AtmStatus.CannotDispense, Withdraw(250, 0, stock, 50, Denomination.Fifty), 250, 0, stock);
    }

    [Fact]
    public void Withdraw40_FiftyJammed_GivesTwoTwenties()
    {
        var stock = N(2, 2, 0);
        var run = Withdraw(250, 0, stock, 40, Denomination.Fifty);
        AssertWithdrawOk(run, 250, 0, stock, 40, N(0, 2, 0), 0);
        Assert.Equal(210, run.Result.Account.Balance);
    }

    [Fact]
    public void Withdraw5_InvalidAmount()
    {
        AssertRefused(AtmStatus.InvalidAmount, Withdraw(0, 200, N(), 5), 0, 200, N());
    }

    [Fact]
    public void DepositTwenty_IntoFullCassette_CassetteFull()
    {
        var stock = N(0, 5, 0);
        AssertRefused(AtmStatus.CassetteFull, Deposit(10, 0, stock, N(0, 1, 0)), 10, 0, stock);
    }

    [Fact]
    public void DepositTwenty_IntoJammedCassette_CassetteJammed()
    {
        var stock = N(0, 1, 0);
        AssertRefused(AtmStatus.CassetteJammed, Deposit(10, 30, stock, N(0, 1, 0), Denomination.Twenty), 10, 30, stock);
    }

    [Fact]
    public void DepositFiftyAndTen_Ok()
    {
        var stock = N(1, 1, 0);
        var run = Deposit(10, 30, stock, N(1, 0, 1));
        AssertDepositOk(run, 10, 30, stock, N(1, 0, 1));
        Assert.Equal(N(2, 1, 1), run.Dispenser.Stock);
        Assert.Equal(new AccountState(70, 30), run.Result.Account);
    }
}
