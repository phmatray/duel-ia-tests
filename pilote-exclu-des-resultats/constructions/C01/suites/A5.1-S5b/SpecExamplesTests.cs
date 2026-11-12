namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    private static (AtmResult Result, SpyDispenser Dispenser) Run(
        int balance, int withdrawn, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new SpyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(Account(balance, withdrawn), operation);
        return (result, dispenser);
    }

    [Fact]
    public void Withdraw_60_with_only_twenties_usable_gives_three_twenties()
    {
        var (result, dispenser) = Run(100, 0, Notes(1, 3, 0), null, Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 3, 0), 0, Account(40, 60)), result);
        Assert.Equal(Notes(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_80_gives_one_note_of_each()
    {
        var (result, dispenser) = Run(100, 0, Notes(1, 1, 1), null, Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 1, 1), 0, Account(20, 80)), result);
        Assert.Equal(Notes(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_100_has_no_fee()
    {
        var (result, _) = Run(105, 0, Notes(5, 5, 5), null, Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 0), 0, Account(5, 100)), result);
    }

    [Fact]
    public void Withdraw_110_has_a_5_euro_fee()
    {
        var (result, _) = Run(115, 0, Notes(5, 5, 5), null, Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 1), 5, Account(0, 110)), result);
    }

    [Fact]
    public void Withdraw_110_with_balance_110_is_refused_because_of_the_fee()
    {
        var (result, _) = Run(110, 0, Notes(5, 5, 5), null, Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, Account(110, 0)), result);
    }

    [Fact]
    public void Withdraw_over_daily_limit_is_refused()
    {
        var (result, _) = Run(250, 150, Notes(5, 5, 5), null, Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, Account(250, 150)), result);
    }

    [Fact]
    public void Withdraw_from_jammed_cassette_only_cannot_dispense()
    {
        var (result, dispenser) = Run(250, 0, Notes(1, 0, 0), Denomination.Fifty, Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Account(250, 0)), result);
        Assert.Equal(Notes(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_avoids_the_jammed_cassette()
    {
        var (result, dispenser) = Run(250, 0, Notes(2, 2, 0), Denomination.Fifty, Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 2, 0), 0, Account(210, 40)), result);
        Assert.Equal(Notes(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_5_is_an_invalid_amount_even_when_everything_else_would_fail()
    {
        var (result, _) = Run(0, 200, Notes(0, 0, 0), null, Withdraw(5));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Account(0, 200)), result);
    }

    [Fact]
    public void Deposit_into_full_cassette_is_refused()
    {
        var (result, dispenser) = Run(10, 0, Notes(0, 5, 0), null, Deposit(0, 1, 0));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, Account(10, 0)), result);
        Assert.Equal(Notes(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_into_jammed_cassette_is_refused()
    {
        var (result, dispenser) = Run(10, 30, Notes(0, 1, 0), Denomination.Twenty, Deposit(0, 1, 0));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, Account(10, 30)), result);
        Assert.Equal(Notes(0, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_50_and_10_succeeds()
    {
        var (result, dispenser) = Run(10, 30, Notes(1, 1, 0), null, Deposit(1, 0, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(70, 30)), result);
        Assert.Equal(Notes(2, 1, 1), dispenser.Stock);
    }
}
