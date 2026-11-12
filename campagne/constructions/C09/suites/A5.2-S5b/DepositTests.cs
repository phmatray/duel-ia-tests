using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    [Fact]
    public void Depot_vide_est_un_montant_invalide()
    {
        var (r, _) = Run(Acc(100, 0), N(0, 0, 0), null, Deposit(NoteCounts.None));

        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    [Fact]
    public void Depot_vide_passe_avant_cassette_bloquee_et_cassette_pleine()
    {
        var (r, _) = Run(Acc(100, 0), N(5, 5, 5), Denomination.Twenty, Deposit(NoteCounts.None));

        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Billet_de_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int n)
    {
        var (r, _) = Run(Acc(100, 0), N(0, 0, 0), jammed, Deposit(N(f, t, n)));

        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_cassette_pleine()
    {
        // La cassette de 20 est bloquée ET pleine.
        var (r, _) = Run(Acc(100, 0), N(0, 5, 0), Denomination.Twenty, Deposit(N(0, 1, 0)));

        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_une_autre_cassette_pleine()
    {
        // Le 50 irait dans une cassette pleine, le 10 dans la cassette bloquée.
        var (r, _) = Run(Acc(100, 0), N(5, 0, 0), Denomination.Ten, Deposit(N(1, 0, 1)));

        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)] // une seule cassette déborde : tout est refusé
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void Depasser_5_billets_dans_une_cassette_est_refuse(int sf, int st, int sn, int f, int t, int n)
    {
        var (r, d) = Run(Acc(100, 0), N(sf, st, sn), null, Deposit(N(f, t, n)));

        Assert.Equal(AtmStatus.CassetteFull, r.Status);
        Assert.Equal(N(sf, st, sn), d.Stock);
    }

    [Theory]
    [InlineData(4, 4, 4, 1, 1, 1)] // remplir jusqu'à 5 exactement
    [InlineData(0, 0, 0, 1, 0, 0)]
    [InlineData(0, 0, 0, 0, 1, 0)]
    [InlineData(0, 0, 0, 0, 0, 1)]
    [InlineData(2, 3, 1, 1, 1, 0)]
    public void Depot_reussi(int sf, int st, int sn, int f, int t, int n)
    {
        var deposit = N(f, t, n);
        var (r, d) = Run(Acc(100, 70), N(sf, st, sn), null, Deposit(deposit));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(100 + deposit.Value, 70), r.Account);
        Assert.Equal(N(sf, st, sn) + deposit, d.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void Depot_qui_evite_la_cassette_bloquee_est_accepte(Denomination jammed, int f, int t, int n)
    {
        var deposit = N(f, t, n);
        var (r, d) = Run(Acc(0, 0), N(2, 2, 2), jammed, Deposit(deposit));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Acc(deposit.Value, 0), r.Account);
        Assert.Equal(N(2, 2, 2) + deposit, d.Stock);
    }

    [Fact]
    public void Depot_reussi_passe_par_le_module_sans_rien_sortir()
    {
        var (r, d) = Run(Acc(0, 200), N(0, 0, 0), null, Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Acc(80, 200), r.Account);
        Assert.Contains(d.LoadCalls, c => c.Ok);
        Assert.Equal(N(1, 1, 1), d.LoadCalls.Where(c => c.Ok).Aggregate(NoteCounts.None, (a, c) => a + c.Notes));
        Assert.DoesNotContain(d.DispenseCalls, c => c.Ok);
    }
}
