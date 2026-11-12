using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    // ---------- Contrôle 1 : dépôt vide ----------

    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_sans_billet_est_invalide(Denomination? jam)
    {
        var (m, spy) = Build(N(2, 2, 2), jam);
        var r = m.Execute(Acc(100, 50), D(NoteCounts.None));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Acc(100, 50)), r);
        Assert.Equal(N(2, 2, 2), spy.Stock);
        Assert.False(spy.AnyMove);
    }

    // ---------- Contrôle 2 : cassette bloquée ----------

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 1)]  // un seul billet fautif suffit
    [InlineData(Denomination.Fifty, 1, 0, 1)]
    public void Depot_touchant_la_cassette_bloquee_est_refuse(Denomination jam, int f, int t, int n)
    {
        var (m, spy) = Build(N(1, 1, 1), jam);
        var r = m.Execute(Acc(100, 50), D(N(f, t, n)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, Acc(100, 50)), r);
        Assert.Equal(N(1, 1, 1), spy.Stock);
        Assert.False(spy.AnyMove);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_cassette_pleine()
    {
        // La cassette de 20 est à la fois bloquée et pleine.
        var (m, _) = Build(N(0, 5, 0), Denomination.Twenty);
        var r = m.Execute(Acc(10, 0), D(N(0, 1, 0)));

        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_une_autre_cassette_pleine()
    {
        // Le 50 vise une cassette pleine, le 10 vise la cassette bloquée.
        var (m, _) = Build(N(5, 0, 0), Denomination.Ten);
        var r = m.Execute(Acc(10, 0), D(N(1, 0, 1)));

        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void Depot_hors_de_la_cassette_bloquee_est_accepte(Denomination jam, int f, int t, int n)
    {
        var (m, spy) = Build(N(1, 1, 1), jam);
        var notes = N(f, t, n);
        var r = m.Execute(Acc(100, 50), D(notes));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(100 + notes.Value, 50)), r);
        Assert.Equal(N(1, 1, 1) + notes, spy.Stock);
    }

    // ---------- Contrôle 3 : cassette pleine ----------

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)]  // une seule cassette qui déborde suffit
    [InlineData(0, 0, 5, 1, 0, 1)]
    public void Depot_faisant_deborder_une_cassette_est_refuse(int sf, int st, int sn, int f, int t, int n)
    {
        var stock = N(sf, st, sn);
        var (m, spy) = Build(stock);
        var r = m.Execute(Acc(100, 50), D(N(f, t, n)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, Acc(100, 50)), r);
        Assert.Equal(stock, spy.Stock);
        Assert.False(spy.AnyMove);
    }

    [Fact]
    public void Depot_qui_remplit_exactement_une_cassette_est_accepte()
    {
        var (m, spy) = Build(N(4, 4, 4));
        var r = m.Execute(Acc(0, 0), D(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(80, 0)), r);
        Assert.Equal(N(5, 5, 5), spy.Stock);
    }

    // ---------- Succès ----------

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Depot_reussi_credite_la_valeur_et_range_les_billets(int f, int t, int n, int value)
    {
        var notes = N(f, t, n);
        var (m, spy) = Build(N(0, 0, 0));
        var r = m.Execute(Acc(250, 200), D(notes));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250 + value, 200), r.Account); // WithdrawnToday inchangé
        Assert.Equal(notes, spy.Stock);
        Assert.Equal(notes, spy.Loaded);
        Assert.Empty(spy.DispenseCalls);
    }
}
