using static Atm.Tests.S;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_seulement_des_20_et_un_50()
    {
        var (m, d) = Real(N(1, 3, 0));
        var r = m.Execute(Acc(100, 0), W(60));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, Acc(40, 60)), r);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_80_une_coupure_de_chaque()
    {
        var (m, d) = Real(N(1, 1, 1));
        var r = m.Execute(Acc(100, 0), W(80));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, Acc(20, 80)), r);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (m, _) = Real(N(5, 5, 5));
        var r = m.Execute(Acc(105, 0), W(100));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(5, 100)), r);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var (m, _) = Real(N(5, 5, 5));
        var r = m.Execute(Acc(115, 0), W(110));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, Acc(0, 110)), r);
    }

    [Fact]
    public void Retrait_110_solde_110_insuffisant_a_cause_des_frais()
    {
        var (m, _) = Real(N(5, 5, 5));
        var r = m.Execute(Acc(110, 0), W(110));
        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, Acc(110, 0)), r);
    }

    [Fact]
    public void Retrait_60_apres_150_depasse_le_plafond()
    {
        var (m, _) = Real(N(5, 5, 5));
        var r = m.Execute(Acc(250, 150), W(60));
        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, Acc(250, 150)), r);
    }

    [Fact]
    public void Retrait_50_cassette_de_50_bloquee()
    {
        var (m, d) = Real(N(1, 0, 0), Denomination.Fifty);
        var r = m.Execute(Acc(250, 0), W(50));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_40_cassette_de_50_bloquee_sort_des_20()
    {
        var (m, d) = Real(N(2, 2, 0), Denomination.Fifty);
        var r = m.Execute(Acc(250, 0), W(40));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, Acc(210, 40)), r);
        Assert.Equal(N(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_5_montant_invalide()
    {
        var (m, _) = Real(N(0, 0, 0));
        var r = m.Execute(Acc(0, 200), W(5));
        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Acc(0, 200)), r);
    }

    [Fact]
    public void Depot_20_cassette_pleine()
    {
        var (m, d) = Real(N(0, 5, 0));
        var r = m.Execute(Acc(10, 0), D(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, Acc(10, 0)), r);
        Assert.Equal(N(0, 5, 0), d.Stock);
    }

    [Fact]
    public void Depot_20_cassette_bloquee()
    {
        var (m, d) = Real(N(0, 1, 0), Denomination.Twenty);
        var r = m.Execute(Acc(10, 30), D(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, Acc(10, 30)), r);
        Assert.Equal(N(0, 1, 0), d.Stock);
    }

    [Fact]
    public void Depot_50_et_10_reussi()
    {
        var (m, d) = Real(N(1, 1, 0));
        var r = m.Execute(Acc(10, 30), D(N(1, 0, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(70, 30)), r);
        Assert.Equal(N(2, 1, 1), d.Stock);
    }
}
