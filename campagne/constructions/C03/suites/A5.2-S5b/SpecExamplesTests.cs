using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Withdraw_60_without_tens_gives_three_twenties()
    {
        var (machine, dispenser) = Build(N(1, 3, 0));

        var result = machine.Execute(Acc(100, 0), Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, Acc(40, 60)), result);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_80_uses_one_note_of_each()
    {
        var (machine, dispenser) = Build(N(1, 1, 1));

        var result = machine.Execute(Acc(100, 0), Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, Acc(20, 80)), result);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_100_has_no_fee()
    {
        var (machine, dispenser) = Build(N(5, 5, 5));

        var result = machine.Execute(Acc(105, 0), Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(5, 100)), result);
        Assert.Equal(N(3, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_110_has_a_fee_of_5()
    {
        var (machine, dispenser) = Build(N(5, 5, 5));

        var result = machine.Execute(Acc(115, 0), Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, Acc(0, 110)), result);
        Assert.Equal(N(3, 5, 4), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_110_with_balance_110_is_insufficient_because_of_the_fee()
    {
        var (machine, dispenser) = Build(N(5, 5, 5));

        var result = machine.Execute(Acc(110, 0), Withdraw(110));

        Assert.Equal(Reference.Refused(AtmStatus.InsufficientFunds, Acc(110, 0)), result);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_over_daily_limit_is_refused()
    {
        var (machine, dispenser) = Build(N(5, 5, 5));

        var result = machine.Execute(Acc(250, 150), Withdraw(60));

        Assert.Equal(Reference.Refused(AtmStatus.DailyLimitExceeded, Acc(250, 150)), result);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_from_jammed_cassette_only_cannot_dispense()
    {
        var (machine, dispenser) = Build(N(1, 0, 0), Denomination.Fifty);

        var result = machine.Execute(Acc(250, 0), Withdraw(50));

        Assert.Equal(Reference.Refused(AtmStatus.CannotDispense, Acc(250, 0)), result);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_40_with_fifties_jammed_gives_two_twenties()
    {
        var (machine, dispenser) = Build(N(2, 2, 0), Denomination.Fifty);

        var result = machine.Execute(Acc(250, 0), Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, Acc(210, 40)), result);
        Assert.Equal(N(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_5_is_an_invalid_amount()
    {
        var (machine, dispenser) = Build(N(0, 0, 0));

        var result = machine.Execute(Acc(0, 200), Withdraw(5));

        Assert.Equal(Reference.Refused(AtmStatus.InvalidAmount, Acc(0, 200)), result);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_into_full_cassette_is_refused()
    {
        var (machine, dispenser) = Build(N(0, 5, 0));

        var result = machine.Execute(Acc(10, 0), Deposit(N(0, 1, 0)));

        Assert.Equal(Reference.Refused(AtmStatus.CassetteFull, Acc(10, 0)), result);
        Assert.Equal(N(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_into_jammed_cassette_is_refused()
    {
        var (machine, dispenser) = Build(N(0, 1, 0), Denomination.Twenty);

        var result = machine.Execute(Acc(10, 30), Deposit(N(0, 1, 0)));

        Assert.Equal(Reference.Refused(AtmStatus.CassetteJammed, Acc(10, 30)), result);
        Assert.Equal(N(0, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_of_fifty_and_ten_succeeds()
    {
        var (machine, dispenser) = Build(N(1, 1, 0));

        var result = machine.Execute(Acc(10, 30), Deposit(N(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(70, 30)), result);
        Assert.Equal(N(2, 1, 1), dispenser.Stock);
    }
}
