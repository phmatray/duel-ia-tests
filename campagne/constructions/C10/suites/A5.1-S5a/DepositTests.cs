using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, SpyDispenser Dispenser) Run(AccountState account, Operation op, NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new SpyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, op);
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AccountState account, AtmResult r, SpyDispenser d, NoteCounts stockBefore)
    {
        Assert.Equal(expected, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(stockBefore, d.Stock);
        Assert.DoesNotContain(d.Loads, x => x.Result);
        Assert.DoesNotContain(d.Dispenses, x => x.Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(20)]
    [InlineData(10)]
    public void Depot_vide_est_invalide(int jam)
    {
        var account = Acc(100, 50);
        var (r, d) = Run(account, D(0, 0, 0), N(5, 5, 5), Jam(jam));

        AssertRefused(AtmStatus.InvalidAmount, account, r, d, N(5, 5, 5));
    }

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(20, 1, 1, 0)]
    [InlineData(10, 0, 1, 1)]
    public void Billet_de_la_cassette_bloquee_est_refuse(int jam, int f, int t, int x)
    {
        var account = Acc(100, 50);
        var (r, d) = Run(account, D(f, t, x), N(0, 0, 0), Jam(jam));

        AssertRefused(AtmStatus.CassetteJammed, account, r, d, N(0, 0, 0));
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_cassette_pleine()
    {
        // La cassette de 20 est pleine ET bloquée ; celle de 10 aussi serait pleine.
        var account = Acc(100, 0);
        var (r, d) = Run(account, D(0, 1, 1), N(0, 5, 5), Denomination.Twenty);

        AssertRefused(AtmStatus.CassetteJammed, account, r, d, N(0, 5, 5));
    }

    [Theory]
    [InlineData(50, 0, 1, 1, 30)]
    [InlineData(20, 1, 0, 1, 60)]
    [InlineData(10, 1, 1, 0, 70)]
    public void Depot_hors_cassette_bloquee_est_accepte(int jam, int f, int t, int x, int value)
    {
        var (r, d) = Run(Acc(100, 50), D(f, t, x), N(2, 2, 2), Jam(jam));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(100 + value, 50)), r);
        Assert.Equal(N(2, 2, 2) + N(f, t, x), d.Stock);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // une seule cassette déborde : tout est refusé
    [InlineData(0, 0, 5, 1, 1, 1)]
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void Depot_qui_fait_depasser_5_billets_est_refuse(int sf, int st, int sx, int f, int t, int x)
    {
        var stock = N(sf, st, sx);
        var account = Acc(100, 0);
        var (r, d) = Run(account, D(f, t, x), stock);

        AssertRefused(AtmStatus.CassetteFull, account, r, d, stock);
    }

    [Fact]
    public void Remplir_exactement_jusqu_a_5_billets_est_permis()
    {
        var (r, d) = Run(Acc(0, 0), D(1, 1, 1), N(4, 4, 4));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(80, 0)), r);
        Assert.Equal(N(5, 5, 5), d.Stock);
    }

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Depot_reussi_credite_la_valeur_et_range_les_billets(int f, int t, int x, int value)
    {
        var (r, d) = Run(Acc(250, 200), D(f, t, x), N(0, 0, 0));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250 + value, 200), r.Account);
        Assert.Equal(N(f, t, x), d.Stock);
        Assert.Empty(d.Dispenses);
    }
}
