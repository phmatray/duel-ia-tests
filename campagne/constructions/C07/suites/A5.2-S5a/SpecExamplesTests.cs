using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_trois_billets_de_20()
    {
        var (machine, dispenser) = Real(N(1, 3, 0));

        var result = machine.Execute(Acc(100, 0), Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, Acc(40, 60)), result);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_80_avec_un_billet_de_chaque()
    {
        var (machine, dispenser) = Real(N(1, 1, 1));

        var result = machine.Execute(Acc(100, 0), Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, Acc(20, 80)), result);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (machine, _) = Real(N(5, 5, 5));

        var result = machine.Execute(Acc(105, 0), Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(5, 100)), result);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var (machine, _) = Real(N(5, 5, 5));

        var result = machine.Execute(Acc(115, 0), Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, Acc(0, 110)), result);
    }

    [Fact]
    public void Retrait_110_refuse_car_les_frais_depassent_le_solde()
    {
        var (machine, dispenser) = Real(N(5, 5, 5));

        var result = machine.Execute(Acc(110, 0), Withdraw(110));

        AssertRefused(result, AtmStatus.InsufficientFunds, Acc(110, 0));
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Retrait_60_refuse_par_le_plafond_du_jour()
    {
        var (machine, dispenser) = Real(N(5, 5, 5));

        var result = machine.Execute(Acc(250, 150), Withdraw(60));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, Acc(250, 150));
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Retrait_50_impossible_car_la_cassette_de_50_est_bloquee()
    {
        var (machine, dispenser) = Real(N(1, 0, 0), Denomination.Fifty);

        var result = machine.Execute(Acc(250, 0), Withdraw(50));

        AssertRefused(result, AtmStatus.CannotDispense, Acc(250, 0));
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_40_en_billets_de_20_quand_la_cassette_de_50_est_bloquee()
    {
        var (machine, dispenser) = Real(N(2, 2, 0), Denomination.Fifty);

        var result = machine.Execute(Acc(250, 0), Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, Acc(210, 40)), result);
        Assert.Equal(N(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_5_invalide_meme_avec_plafond_atteint_et_solde_nul()
    {
        var (machine, _) = Real(N(0, 0, 0));

        var result = machine.Execute(Acc(0, 200), Withdraw(5));

        AssertRefused(result, AtmStatus.InvalidAmount, Acc(0, 200));
    }

    [Fact]
    public void Depot_refuse_car_la_cassette_de_20_est_pleine()
    {
        var (machine, dispenser) = Real(N(0, 5, 0));

        var result = machine.Execute(Acc(10, 0), Deposit(N(0, 1, 0)));

        AssertRefused(result, AtmStatus.CassetteFull, Acc(10, 0));
        Assert.Equal(N(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Depot_refuse_car_la_cassette_de_20_est_bloquee()
    {
        var (machine, dispenser) = Real(N(0, 1, 0), Denomination.Twenty);

        var result = machine.Execute(Acc(10, 30), Deposit(N(0, 1, 0)));

        AssertRefused(result, AtmStatus.CassetteJammed, Acc(10, 30));
        Assert.Equal(N(0, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Depot_de_60_accepte()
    {
        var (machine, dispenser) = Real(N(1, 1, 0));

        var result = machine.Execute(Acc(10, 30), Deposit(N(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(70, 30)), result);
        Assert.Equal(N(2, 1, 1), dispenser.Stock);
    }
}
