using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_trois_billets_de_20()
    {
        var (r, spy) = Run(Acc(100, 0), N(1, 3, 0), null, W(60));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, Acc(40, 60)), r);
        Assert.Equal(N(1, 0, 0), spy.Stock);
    }

    [Fact]
    public void Retrait_80_un_billet_de_chaque()
    {
        var (r, spy) = Run(Acc(100, 0), N(1, 1, 1), null, W(80));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, Acc(20, 80)), r);
        Assert.Equal(N(0, 0, 0), spy.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (r, _) = Run(Acc(105, 0), N(5, 5, 5), null, W(100));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(5, 100)), r);
    }

    [Fact]
    public void Retrait_110_avec_frais_de_5()
    {
        var (r, _) = Run(Acc(115, 0), N(5, 5, 5), null, W(110));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, Acc(0, 110)), r);
    }

    [Fact]
    public void Retrait_110_solde_110_insuffisant_a_cause_des_frais()
    {
        var (r, _) = Run(Acc(110, 0), N(5, 5, 5), null, W(110));
        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, Acc(110, 0)), r);
    }

    [Fact]
    public void Retrait_60_apres_150_depasse_le_plafond()
    {
        var (r, _) = Run(Acc(250, 150), N(5, 5, 5), null, W(60));
        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, Acc(250, 150)), r);
    }

    [Fact]
    public void Retrait_50_cassette_de_50_bloquee()
    {
        var (r, spy) = Run(Acc(250, 0), N(1, 0, 0), Denomination.Fifty, W(50));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Equal(N(1, 0, 0), spy.Stock);
    }

    [Fact]
    public void Retrait_40_contourne_la_cassette_bloquee()
    {
        var (r, spy) = Run(Acc(250, 0), N(2, 2, 0), Denomination.Fifty, W(40));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, Acc(210, 40)), r);
        Assert.Equal(N(2, 0, 0), spy.Stock);
    }

    [Fact]
    public void Retrait_5_montant_invalide_avant_tout_autre_controle()
    {
        var (r, _) = Run(Acc(0, 200), N(0, 0, 0), null, W(5));
        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Acc(0, 200)), r);
    }

    [Fact]
    public void Depot_dans_cassette_pleine()
    {
        var (r, spy) = Run(Acc(10, 0), N(0, 5, 0), null, D(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, Acc(10, 0)), r);
        Assert.Equal(N(0, 5, 0), spy.Stock);
    }

    [Fact]
    public void Depot_dans_cassette_bloquee()
    {
        var (r, spy) = Run(Acc(10, 30), N(0, 1, 0), Denomination.Twenty, D(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, Acc(10, 30)), r);
        Assert.Equal(N(0, 1, 0), spy.Stock);
    }

    [Fact]
    public void Depot_50_et_10_reussi()
    {
        var (r, spy) = Run(Acc(10, 30), N(1, 1, 0), null, D(N(1, 0, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(70, 30)), r);
        Assert.Equal(N(2, 1, 1), spy.Stock);
    }
}
