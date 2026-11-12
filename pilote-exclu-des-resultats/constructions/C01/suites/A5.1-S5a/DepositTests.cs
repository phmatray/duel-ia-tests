using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, SpyDispenser Spy) Run(AccountState account, NoteCounts notes, NoteCounts stock, Denomination? jammed = null)
    {
        var spy = new SpyDispenser(stock, jammed);
        var result = new CashMachine(spy).Execute(account, Deposit(notes));
        return (result, spy);
    }

    private static void AssertRefused(AtmStatus expected, AccountState account, AtmResult result, SpyDispenser spy, NoteCounts stockBefore)
    {
        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
        Assert.Empty(spy.LoadCalls);
        Assert.Empty(spy.DispenseCalls);
        Assert.Equal(stockBefore, spy.Stock);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(50)]
    public void Empty_deposit_is_invalid_amount(int? jammed)
    {
        var account = Acc(100, 50);
        var (result, spy) = Run(account, NoteCounts.None, N(5, 5, 5), Jam(jammed));
        AssertRefused(AtmStatus.InvalidAmount, account, result, spy, N(5, 5, 5));
    }

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(20, 1, 1, 0)]
    [InlineData(10, 0, 1, 1)]
    public void Deposit_touching_the_jammed_cassette_is_refused(int jammed, int d50, int d20, int d10)
    {
        var account = Acc(100, 50);
        var stock = N(1, 1, 1);
        var (result, spy) = Run(account, N(d50, d20, d10), stock, Jam(jammed));
        AssertRefused(AtmStatus.CassetteJammed, account, result, spy, stock);
    }

    [Theory]
    [InlineData(50, 0, 1, 0)]
    [InlineData(50, 0, 1, 1)]
    [InlineData(20, 1, 0, 1)]
    [InlineData(10, 1, 1, 0)]
    public void Deposit_avoiding_the_jammed_cassette_is_accepted(int jammed, int d50, int d20, int d10)
    {
        var account = Acc(100, 50);
        var notes = N(d50, d20, d10);
        var (result, spy) = Run(account, notes, N(1, 1, 1), Jam(jammed));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(100 + notes.Value, 50)), result);
        Assert.Equal(N(1, 1, 1) + notes, spy.Stock);
    }

    [Fact]
    public void Jammed_takes_precedence_over_full()
    {
        // La cassette de 20 est à la fois pleine et bloquée.
        var account = Acc(10, 0);
        var (result, spy) = Run(account, N(0, 1, 0), N(0, 5, 0), Denomination.Twenty);
        AssertRefused(AtmStatus.CassetteJammed, account, result, spy, N(0, 5, 0));
    }

    [Fact]
    public void Jammed_cassette_elsewhere_does_not_block_and_full_still_applies()
    {
        var account = Acc(10, 0);
        var (result, spy) = Run(account, N(1, 0, 0), N(5, 0, 0), Denomination.Ten);
        AssertRefused(AtmStatus.CassetteFull, account, result, spy, N(5, 0, 0));
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)]  // un seul billet déborde : tout est refusé
    [InlineData(4, 4, 5, 1, 1, 1)]
    public void Deposit_overflowing_a_cassette_is_refused(int s50, int s20, int s10, int d50, int d20, int d10)
    {
        var account = Acc(10, 0);
        var stock = N(s50, s20, s10);
        var (result, spy) = Run(account, N(d50, d20, d10), stock);
        AssertRefused(AtmStatus.CassetteFull, account, result, spy, stock);
    }

    [Theory]
    [InlineData(4, 4, 4, 1, 1, 1)]  // remplit chaque cassette à 5 pile
    [InlineData(0, 0, 0, 1, 1, 1)]
    [InlineData(5, 5, 4, 0, 0, 1)]
    [InlineData(5, 0, 5, 0, 1, 0)]
    public void Deposit_up_to_five_per_cassette_is_accepted(int s50, int s20, int s10, int d50, int d20, int d10)
    {
        var account = Acc(10, 0);
        var stock = N(s50, s20, s10);
        var notes = N(d50, d20, d10);
        var (result, spy) = Run(account, notes, stock);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(stock + notes, spy.Stock);
    }

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 1, 1, 80)]
    public void Success_credits_value_without_fee_and_keeps_withdrawn_today(int d50, int d20, int d10, int value)
    {
        var account = Acc(170, 120);
        var notes = N(d50, d20, d10);
        var (result, spy) = Run(account, notes, N(2, 2, 2));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(170 + value, 120)), result);
        Assert.Equal(N(2, 2, 2) + notes, spy.Stock);
        Assert.Equal(notes, spy.Loaded);
        Assert.Empty(spy.DispenseCalls);
    }

    [Fact]
    public void Deposit_is_accepted_whatever_the_daily_withdrawals_or_balance()
    {
        var (result, _) = Run(Acc(0, 200), N(1, 0, 0), N(0, 0, 0));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(50, 200)), result);
    }
}
