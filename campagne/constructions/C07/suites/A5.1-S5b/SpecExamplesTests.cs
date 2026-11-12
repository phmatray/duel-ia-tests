using Atm.Tests.Support;
using static Atm.Tests.Support.N;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_50x1_20x3_sort_trois_billets_de_20()
    {
        var run = Scenario.Withdraw(100, 0, Notes(1, 3, 0), null, 60);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 3, 0), 0, new AccountState(40, 60)), run.Result);
    }

    [Fact]
    public void Retrait_80_avec_un_billet_de_chaque_sort_les_trois()
    {
        var run = Scenario.Withdraw(100, 0, Notes(1, 1, 1), null, 80);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 1, 1), 0, new AccountState(20, 80)), run.Result);
    }

    [Fact]
    public void Retrait_100_est_sans_frais()
    {
        var run = Scenario.Withdraw(105, 0, Notes(5, 5, 5), null, 100);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 0), 0, new AccountState(5, 100)), run.Result);
    }

    [Fact]
    public void Retrait_110_preleve_5_de_frais()
    {
        var run = Scenario.Withdraw(115, 0, Notes(5, 5, 5), null, 110);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 1), 5, new AccountState(0, 110)), run.Result);
    }

    [Fact]
    public void Retrait_110_avec_solde_110_est_refuse_a_cause_des_frais()
    {
        var run = Scenario.Withdraw(110, 0, Notes(5, 5, 5), null, 110);

        Assert.Equal(AtmStatus.InsufficientFunds, run.Result.Status);
    }

    [Fact]
    public void Retrait_60_apres_150_depasse_le_plafond()
    {
        var run = Scenario.Withdraw(250, 150, Notes(5, 5, 5), null, 60);

        Assert.Equal(AtmStatus.DailyLimitExceeded, run.Result.Status);
    }

    [Fact]
    public void Retrait_50_quand_seule_la_cassette_de_50_est_garnie_et_bloquee()
    {
        var run = Scenario.Withdraw(250, 0, Notes(1, 0, 0), Denomination.Fifty, 50);

        Assert.Equal(AtmStatus.CannotDispense, run.Result.Status);
    }

    [Fact]
    public void Retrait_40_contourne_la_cassette_bloquee()
    {
        var run = Scenario.Withdraw(250, 0, Notes(2, 2, 0), Denomination.Fifty, 40);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 2, 0), 0, new AccountState(210, 40)), run.Result);
    }

    [Fact]
    public void Retrait_5_est_un_montant_invalide()
    {
        var run = Scenario.Withdraw(0, 200, Notes(0, 0, 0), null, 5);

        Assert.Equal(AtmStatus.InvalidAmount, run.Result.Status);
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine()
    {
        var run = Scenario.Deposit(10, 0, Notes(0, 5, 0), null, Notes(0, 1, 0));

        Assert.Equal(AtmStatus.CassetteFull, run.Result.Status);
    }

    [Fact]
    public void Depot_dans_la_cassette_bloquee()
    {
        var run = Scenario.Deposit(10, 30, Notes(0, 1, 0), Denomination.Twenty, Notes(0, 1, 0));

        Assert.Equal(AtmStatus.CassetteJammed, run.Result.Status);
    }

    [Fact]
    public void Depot_de_50_et_10_credite_le_compte_et_garnit_le_stock()
    {
        var run = Scenario.Deposit(10, 30, Notes(1, 1, 0), null, Notes(1, 0, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(70, 30)), run.Result);
        Assert.Equal(Notes(2, 1, 1), run.Dispenser.Stock);
    }
}
