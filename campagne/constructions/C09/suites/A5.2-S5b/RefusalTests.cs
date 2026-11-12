using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>« Sur un refus, rien ne change » : Notes vide, Fee à 0, compte identique, aucun billet déplacé.</summary>
public class RefusalTests
{
    [Theory]
    [InlineData(250, 0, 5, 5, 5, null, 0, AtmStatus.InvalidAmount)]
    [InlineData(250, 0, 5, 5, 5, null, -10, AtmStatus.InvalidAmount)]
    [InlineData(250, 0, 5, 5, 5, null, 5, AtmStatus.InvalidAmount)]
    [InlineData(250, 150, 5, 5, 5, null, 60, AtmStatus.DailyLimitExceeded)]
    [InlineData(250, 0, 5, 5, 5, null, 220, AtmStatus.DailyLimitExceeded)]
    [InlineData(110, 0, 5, 5, 5, null, 110, AtmStatus.InsufficientFunds)]
    [InlineData(0, 0, 5, 5, 5, null, 10, AtmStatus.InsufficientFunds)]
    [InlineData(250, 0, 1, 0, 0, Denomination.Fifty, 50, AtmStatus.CannotDispense)]
    [InlineData(250, 0, 5, 5, 0, null, 30, AtmStatus.CannotDispense)]
    [InlineData(250, 0, 2, 0, 5, null, 190, AtmStatus.CannotDispense)]
    public void Retrait_refuse_ne_change_rien(int balance, int withdrawnToday, int f, int t, int n,
        Denomination? jammed, int amount, AtmStatus expected)
    {
        var account = Acc(balance, withdrawnToday);
        var stock = N(f, t, n);

        var (r, d) = Run(account, stock, jammed, Withdraw(amount));

        Assert.Equal(expected, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(stock, d.Stock);
        Assert.DoesNotContain(d.DispenseCalls, c => c.Ok);
        Assert.DoesNotContain(d.LoadCalls, c => c.Ok);
    }

    [Theory]
    [InlineData(0, 0, 0, null, 0, 0, 0, AtmStatus.InvalidAmount)]
    [InlineData(2, 2, 2, Denomination.Twenty, 1, 1, 1, AtmStatus.CassetteJammed)]
    [InlineData(2, 2, 2, Denomination.Fifty, 1, 0, 0, AtmStatus.CassetteJammed)]
    [InlineData(5, 0, 0, null, 1, 1, 1, AtmStatus.CassetteFull)]
    [InlineData(0, 5, 0, Denomination.Fifty, 0, 1, 1, AtmStatus.CassetteFull)]
    public void Depot_refuse_ne_change_rien(int f, int t, int n, Denomination? jammed, int df, int dt, int dn,
        AtmStatus expected)
    {
        var account = Acc(50, 30);
        var stock = N(f, t, n);

        var (r, d) = Run(account, stock, jammed, Deposit(N(df, dt, dn)));

        Assert.Equal(expected, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(stock, d.Stock);
        Assert.DoesNotContain(d.DispenseCalls, c => c.Ok);
        Assert.DoesNotContain(d.LoadCalls, c => c.Ok);
    }
}
