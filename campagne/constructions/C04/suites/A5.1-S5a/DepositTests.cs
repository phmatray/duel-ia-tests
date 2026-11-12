using static Atm.Tests.TestKit;

namespace Atm.Tests;

public class DepositTests
{
    // ---- Contrôle 1 : dépôt vide ----

    [Fact]
    public void Empty_deposit_is_InvalidAmount()
    {
        var account = Account(100, 30);
        var stock = N(1, 1, 1);
        var (result, dispenser) = Run(account, stock, null, Deposit(NoteCounts.None));

        AssertRefused(AtmStatus.InvalidAmount, result, account, stock, dispenser);
    }

    [Fact]
    public void Empty_deposit_is_InvalidAmount_even_with_jammed_and_full_cassettes()
    {
        var account = Account(100, 30);
        var stock = N(5, 5, 5);
        var (result, dispenser) = Run(account, stock, Denomination.Ten, Deposit(NoteCounts.None));

        AssertRefused(AtmStatus.InvalidAmount, result, account, stock, dispenser);
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(10, 1, 1, 1)]
    [InlineData(50, 1, 1, 0)]
    public void Deposit_touching_jammed_cassette_is_CassetteJammed(int jammed, int f, int t, int d)
    {
        var account = Account(100, 30);
        var stock = N(1, 1, 1);
        var (result, dispenser) = Run(account, stock, ParseJammed(jammed), Deposit(N(f, t, d)));

        AssertRefused(AtmStatus.CassetteJammed, result, account, stock, dispenser);
    }

    [Fact]
    public void CassetteJammed_is_checked_before_CassetteFull()
    {
        var account = Account(100, 30);
        var stock = N(5, 5, 5);
        var (result, dispenser) = Run(account, stock, Denomination.Fifty, Deposit(N(1, 1, 0)));

        AssertRefused(AtmStatus.CassetteJammed, result, account, stock, dispenser);
    }

    [Fact]
    public void Deposit_avoiding_jammed_cassette_is_accepted()
    {
        var (result, dispenser) = Run(Account(100, 30), N(1, 1, 1), Denomination.Fifty, Deposit(N(0, 1, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Account(130, 30), result.Account);
        Assert.Equal(N(1, 2, 2), dispenser.Stock);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(4, 4, 5, 1, 1, 1)]   // une seule cassette déborde : tout est refusé
    public void Deposit_overflowing_a_cassette_is_CassetteFull(int sf, int st, int sd, int f, int t, int d)
    {
        var account = Account(100, 30);
        var stock = N(sf, st, sd);
        var (result, dispenser) = Run(account, stock, null, Deposit(N(f, t, d)));

        AssertRefused(AtmStatus.CassetteFull, result, account, stock, dispenser);
    }

    [Fact]
    public void Filling_a_cassette_up_to_5_is_accepted()
    {
        var (result, dispenser) = Run(Account(0, 0), N(4, 4, 4), null, Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Account(80, 0), result.Account);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    // ---- Le succès ----

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 1, 1, 80)]
    public void Success_credits_the_value_and_loads_the_notes(int f, int t, int d, int value)
    {
        var notes = N(f, t, d);
        var stock = N(2, 0, 3);
        var spy = new SpyDispenser(new MoneyDispenser(stock));
        var result = new CashMachine(spy).Execute(Account(150, 120), Deposit(notes));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(150 + value, 120), result.Account);
        Assert.Equal(stock + notes, spy.Stock);
        Assert.Empty(spy.Dispenses);
    }

    [Fact]
    public void Deposit_ignores_daily_limit_and_has_no_fee()
    {
        var (result, _) = Run(Account(250, 200), N(0, 0, 0), null, Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(330, 200), result.Account);
    }
}
