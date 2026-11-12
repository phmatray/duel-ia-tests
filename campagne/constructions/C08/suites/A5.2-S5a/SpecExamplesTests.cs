namespace Atm.Tests;

using static T;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_seulement_un_billet_de_50_et_des_20_sort_trois_20()
    {
        var (machine, dispenser) = Real(N(1, 3, 0));

        var result = machine.Execute(Acc(100, 0), W(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, Acc(40, 60)), result);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_80_avec_un_billet_de_chaque_sort_les_trois()
    {
        var (machine, dispenser) = Real(N(1, 1, 1));

        var result = machine.Execute(Acc(100, 0), W(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, Acc(20, 80)), result);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_100_est_sans_frais()
    {
        var (machine, _) = Real(N(5, 5, 5));

        var result = machine.Execute(Acc(105, 0), W(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(5, 100)), result);
    }

    [Fact]
    public void Retrait_110_coute_5_de_frais()
    {
        var (machine, _) = Real(N(5, 5, 5));

        var result = machine.Execute(Acc(115, 0), W(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, Acc(0, 110)), result);
    }

    [Fact]
    public void Retrait_110_avec_solde_110_est_refuse_a_cause_des_frais()
    {
        var (machine, dispenser) = Real(N(5, 5, 5));

        var result = machine.Execute(Acc(110, 0), W(110));

        AssertRefused(result, AtmStatus.InsufficientFunds, Acc(110, 0));
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Retrait_60_apres_150_deja_retires_depasse_le_plafond()
    {
        var (machine, dispenser) = Real(N(5, 5, 5));

        var result = machine.Execute(Acc(250, 150), W(60));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, Acc(250, 150));
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Retrait_50_quand_le_seul_billet_est_dans_la_cassette_bloquee()
    {
        var (machine, dispenser) = Real(N(1, 0, 0), Denomination.Fifty);

        var result = machine.Execute(Acc(250, 0), W(50));

        AssertRefused(result, AtmStatus.CannotDispense, Acc(250, 0));
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_40_contourne_la_cassette_bloquee()
    {
        var (machine, dispenser) = Real(N(2, 2, 0), Denomination.Fifty);

        var result = machine.Execute(Acc(250, 0), W(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, Acc(210, 40)), result);
        Assert.Equal(N(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_5_est_un_montant_invalide()
    {
        var (machine, _) = Real(N(0, 0, 0));

        var result = machine.Execute(Acc(0, 200), W(5));

        AssertRefused(result, AtmStatus.InvalidAmount, Acc(0, 200));
    }

    [Fact]
    public void Depot_d_un_20_dans_une_cassette_de_20_pleine()
    {
        var (machine, dispenser) = Real(N(0, 5, 0));

        var result = machine.Execute(Acc(10, 0), D(0, 1, 0));

        AssertRefused(result, AtmStatus.CassetteFull, Acc(10, 0));
        Assert.Equal(N(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Depot_d_un_20_dans_la_cassette_de_20_bloquee()
    {
        var (machine, dispenser) = Real(N(0, 1, 0), Denomination.Twenty);

        var result = machine.Execute(Acc(10, 30), D(0, 1, 0));

        AssertRefused(result, AtmStatus.CassetteJammed, Acc(10, 30));
        Assert.Equal(N(0, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Depot_d_un_50_et_d_un_10()
    {
        var (machine, dispenser) = Real(N(1, 1, 0));

        var result = machine.Execute(Acc(10, 30), D(1, 0, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(70, 30)), result);
        Assert.Equal(N(2, 1, 1), dispenser.Stock);
    }
}
