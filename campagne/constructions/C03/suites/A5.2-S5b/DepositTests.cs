using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    [Fact]
    public void Empty_deposit_is_invalid()
    {
        var (machine, dispenser) = Build(N(1, 1, 1));

        var result = machine.Execute(Acc(100, 50), Deposit(NoteCounts.None));

        Assert.Equal(Reference.Refused(AtmStatus.InvalidAmount, Acc(100, 50)), result);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(20)]
    [InlineData(10)]
    public void Empty_deposit_is_invalid_even_with_jammed_or_full_cassettes(int jam)
    {
        var (machine, _) = Build(N(5, 5, 5), Jam(jam));

        var result = machine.Execute(Acc(100, 0), Deposit(NoteCounts.None));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(20, 1, 1, 0)]
    [InlineData(10, 0, 1, 1)]
    public void Deposit_containing_a_jammed_note_is_refused(int jam, int f, int t, int n)
    {
        var stock = N(0, 0, 0);
        var (machine, dispenser) = Build(stock, Jam(jam));

        var result = machine.Execute(Acc(100, 0), Deposit(N(f, t, n)));

        Assert.Equal(Reference.Refused(AtmStatus.CassetteJammed, Acc(100, 0)), result);
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void Jammed_is_checked_before_full()
    {
        // La cassette des 10 est pleine, celle des 20 est bloquée.
        var (machine, _) = Build(N(0, 0, 5), Denomination.Twenty);

        var result = machine.Execute(Acc(100, 0), Deposit(N(0, 1, 1)));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)]  // une seule cassette pleine suffit : tout ou rien
    public void Deposit_overflowing_a_cassette_is_refused(int sf, int st, int sn, int f, int t, int n)
    {
        var stock = N(sf, st, sn);
        var (machine, dispenser) = Build(stock);

        var result = machine.Execute(Acc(100, 0), Deposit(N(f, t, n)));

        Assert.Equal(Reference.Refused(AtmStatus.CassetteFull, Acc(100, 0)), result);
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void Deposit_filling_a_cassette_to_exactly_five_is_allowed()
    {
        var (machine, dispenser) = Build(N(4, 4, 4));

        var result = machine.Execute(Acc(0, 0), Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(80, 0)), result);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 1, 80)]
    public void Successful_deposit_credits_value_and_loads_notes(int f, int t, int n, int value)
    {
        var (machine, dispenser) = Build(N(2, 2, 2));

        var result = machine.Execute(Acc(250, 120), Deposit(N(f, t, n)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(250 + value, 120)), result);
        Assert.Equal(N(2, 2, 2) + N(f, t, n), dispenser.Stock);
        Assert.Empty(dispenser.Dispensed);
    }

    [Theory]
    [InlineData(50, 0, 1, 1)]
    [InlineData(20, 1, 0, 1)]
    [InlineData(10, 1, 1, 0)]
    public void Deposit_avoiding_the_jammed_cassette_succeeds(int jam, int f, int t, int n)
    {
        var (machine, dispenser) = Build(N(0, 0, 0), Jam(jam));

        var result = machine.Execute(Acc(0, 0), Deposit(N(f, t, n)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(f, t, n).Value, result.Account.Balance);
        Assert.Equal(N(f, t, n), dispenser.Stock);
    }
}
