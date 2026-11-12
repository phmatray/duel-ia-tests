using static Atm.Tests.N;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    private static (AtmResult Result, MoneyDispenser Dispenser) Run(
        int balance, int withdrawn, NoteCounts stock, int? jam, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, Jam(jam));
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), operation);
        return (result, dispenser);
    }

    [Fact]
    public void Withdraw_60_with_twenties_only_gives_three_twenties()
    {
        var (r, d) = Run(100, 0, C(1, 3, 0), null, new Operation.Withdraw(60));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(0, 3, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(40, 60), r.Account);
        Assert.Equal(C(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Withdraw_80_uses_one_note_of_each()
    {
        var (r, d) = Run(100, 0, C(1, 1, 1), null, new Operation.Withdraw(80));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(1, 1, 1), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(20, 80), r.Account);
        Assert.Equal(C(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Withdraw_100_has_no_fee()
    {
        var (r, _) = Run(105, 0, C(5, 5, 5), null, new Operation.Withdraw(100));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(2, 0, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(5, 100), r.Account);
    }

    [Fact]
    public void Withdraw_110_has_a_5_euro_fee()
    {
        var (r, _) = Run(115, 0, C(5, 5, 5), null, new Operation.Withdraw(110));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(2, 0, 1), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(0, 110), r.Account);
    }

    [Fact]
    public void Withdraw_110_with_balance_110_is_insufficient_because_of_fee()
    {
        var (r, _) = Run(110, 0, C(5, 5, 5), null, new Operation.Withdraw(110));
        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    [Fact]
    public void Withdraw_60_after_150_exceeds_daily_limit()
    {
        var (r, _) = Run(250, 150, C(5, 5, 5), null, new Operation.Withdraw(60));
        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    [Fact]
    public void Withdraw_50_when_fifties_jammed_cannot_dispense()
    {
        var (r, _) = Run(250, 0, C(1, 0, 0), 50, new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.CannotDispense, r.Status);
    }

    [Fact]
    public void Withdraw_40_when_fifties_jammed_uses_twenties()
    {
        var (r, d) = Run(250, 0, C(2, 2, 0), 50, new Operation.Withdraw(40));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(0, 2, 0), r.Notes);
        Assert.Equal(new AccountState(210, 40), r.Account);
        Assert.Equal(C(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Withdraw_5_is_invalid_even_when_everything_else_fails()
    {
        var (r, _) = Run(0, 200, C(0, 0, 0), null, new Operation.Withdraw(5));
        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    [Fact]
    public void Deposit_into_full_cassette_is_refused()
    {
        var (r, _) = Run(10, 0, C(0, 5, 0), null, new Operation.Deposit(C(0, 1, 0)));
        Assert.Equal(AtmStatus.CassetteFull, r.Status);
    }

    [Fact]
    public void Deposit_into_jammed_cassette_is_refused()
    {
        var (r, _) = Run(10, 30, C(0, 1, 0), 20, new Operation.Deposit(C(0, 1, 0)));
        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Fact]
    public void Deposit_of_50_and_10_succeeds()
    {
        var (r, d) = Run(10, 30, C(1, 1, 0), null, new Operation.Deposit(C(1, 0, 1)));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(2, 1, 1), d.Stock);
        Assert.Equal(new AccountState(70, 30), r.Account);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
    }
}
