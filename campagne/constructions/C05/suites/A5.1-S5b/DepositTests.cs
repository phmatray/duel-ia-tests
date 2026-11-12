using static Atm.Tests.S;

namespace Atm.Tests;

public class DepositTests
{
    // ---- Succès ----

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Depot_credite_la_valeur_et_range_les_billets(int f, int t, int d10, int value)
    {
        var stock = N(2, 3, 4);
        var (m, d) = Real(stock);
        var r = m.Execute(Acc(100, 70), D(N(f, t, d10)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(100 + value, 70)), r);
        Assert.Equal(stock + N(f, t, d10), d.Stock);
    }

    [Fact]
    public void Depot_remplit_une_cassette_jusqu_a_5_exactement()
    {
        var (m, d) = Real(N(4, 4, 4));
        var r = m.Execute(Acc(0, 0), D(N(1, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(80, 0)), r);
        Assert.Equal(N(5, 5, 5), d.Stock);
    }

    [Fact]
    public void Depot_ne_touchant_pas_la_cassette_bloquee_est_accepte()
    {
        var (m, d) = Real(N(0, 5, 0), Denomination.Twenty);
        var r = m.Execute(Acc(250, 200), D(N(1, 0, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(310, 200)), r);
        Assert.Equal(N(1, 5, 1), d.Stock);
    }

    [Fact]
    public void Depot_passe_par_le_module()
    {
        var recorder = new RecordingDispenser(new MoneyDispenser(N(0, 0, 0)));
        var r = new CashMachine(recorder).Execute(Acc(0, 0), D(N(1, 1, 1)));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 1, 1), recorder.Loaded.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Empty(recorder.Dispensed);
        Assert.Equal(N(1, 1, 1), recorder.Stock);
    }

    // ---- Contrôle 1 : dépôt vide ----

    [Theory]
    [InlineData(null)]
    [InlineData(50)]
    [InlineData(20)]
    [InlineData(10)]
    public void Depot_vide_est_invalide_avant_tout_autre_controle(int? jammed)
    {
        var (m, d) = Real(N(5, 5, 5), Jam(jammed));
        var r = m.Execute(Acc(30, 20), D(NoteCounts.None));
        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Acc(30, 20)), r);
        Assert.Equal(N(5, 5, 5), d.Stock);
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(20, 1, 1, 0)]
    [InlineData(10, 0, 1, 1)]
    public void Billet_de_la_cassette_bloquee_refuse_tout_le_depot(int jammed, int f, int t, int d10)
    {
        var stock = N(1, 1, 1);
        var (m, d) = Real(stock, Jam(jammed));
        var r = m.Execute(Acc(30, 20), D(N(f, t, d10)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, Acc(30, 20)), r);
        Assert.Equal(stock, d.Stock);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_cassette_pleine_sur_la_meme_cassette()
    {
        var (m, _) = Real(N(0, 5, 0), Denomination.Twenty);
        Assert.Equal(AtmStatus.CassetteJammed, m.Execute(Acc(0, 0), D(N(0, 1, 0))).Status);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_cassette_pleine_sur_une_autre_cassette()
    {
        var (m, d) = Real(N(5, 0, 0), Denomination.Ten);
        var r = m.Execute(Acc(0, 0), D(N(1, 0, 1)));
        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
        Assert.Equal(N(5, 0, 0), d.Stock);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)]  // une seule cassette pleine suffit
    [InlineData(5, 0, 0, 1, 0, 1)]
    public void Depot_qui_ferait_depasser_5_billets_est_refuse_entierement(int sf, int st, int s10, int f, int t, int d10)
    {
        var stock = N(sf, st, s10);
        var (m, d) = Real(stock);
        var r = m.Execute(Acc(30, 20), D(N(f, t, d10)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, Acc(30, 20)), r);
        Assert.Equal(stock, d.Stock);
    }

    [Fact]
    public void Cassette_pleine_refusee_meme_si_une_autre_cassette_est_bloquee()
    {
        // La cassette de 50 est bloquée ; le dépôt de 10 remplit une cassette pleine : CassetteFull.
        var (m, _) = Real(N(5, 0, 5), Denomination.Fifty);
        Assert.Equal(AtmStatus.CassetteFull, m.Execute(Acc(0, 0), D(N(0, 0, 1))).Status);
    }
}
