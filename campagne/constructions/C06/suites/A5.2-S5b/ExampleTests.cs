using static Atm.Tests.Support;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class ExampleTests
{
    [Fact]
    public void Retrait_60_avec_seulement_un_50_et_des_20_rend_trois_20()
    {
        var (m, d) = Machine(N(1, 3, 0));
        var r = m.Withdraw(100, 0, 60);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, new AccountState(40, 60)), r);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_80_rend_un_billet_de_chaque()
    {
        var (m, d) = Machine(N(1, 1, 1));
        var r = m.Withdraw(100, 0, 80);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, new AccountState(20, 80)), r);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (m, _) = Machine(Full);
        var r = m.Withdraw(105, 0, 100);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, new AccountState(5, 100)), r);
    }

    [Fact]
    public void Retrait_110_avec_frais_de_5()
    {
        var (m, _) = Machine(Full);
        var r = m.Withdraw(115, 0, 110);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, new AccountState(0, 110)), r);
    }

    [Fact]
    public void Retrait_110_avec_solde_110_est_insuffisant_a_cause_des_frais()
    {
        var (m, d) = Machine(Full);
        AssertRefused(m.Withdraw(110, 0, 110), AtmStatus.InsufficientFunds, new(110, 0), d, Full);
    }

    [Fact]
    public void Retrait_60_avec_150_deja_retires_depasse_le_plafond()
    {
        var (m, d) = Machine(Full);
        AssertRefused(m.Withdraw(250, 150, 60), AtmStatus.DailyLimitExceeded, new(250, 150), d, Full);
    }

    [Fact]
    public void Retrait_50_avec_cassette_de_50_bloquee_est_impossible()
    {
        var (m, d) = Machine(N(1, 0, 0), Denomination.Fifty);
        AssertRefused(m.Withdraw(250, 0, 50), AtmStatus.CannotDispense, new(250, 0), d, N(1, 0, 0));
    }

    [Fact]
    public void Retrait_40_avec_cassette_de_50_bloquee_rend_deux_20()
    {
        var (m, d) = Machine(N(2, 2, 0), Denomination.Fifty);
        var r = m.Withdraw(250, 0, 40);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, new AccountState(210, 40)), r);
        Assert.Equal(N(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_5_est_un_montant_invalide()
    {
        var (m, d) = Machine(N(0, 0, 0));
        AssertRefused(m.Withdraw(0, 200, 5), AtmStatus.InvalidAmount, new(0, 200), d, N(0, 0, 0));
    }

    [Fact]
    public void Depot_d_un_20_dans_une_cassette_pleine()
    {
        var (m, d) = Machine(N(0, 5, 0));
        AssertRefused(m.Deposit(10, 0, N(0, 1, 0)), AtmStatus.CassetteFull, new(10, 0), d, N(0, 5, 0));
    }

    [Fact]
    public void Depot_d_un_20_dans_la_cassette_bloquee()
    {
        var (m, d) = Machine(N(0, 1, 0), Denomination.Twenty);
        AssertRefused(m.Deposit(10, 30, N(0, 1, 0)), AtmStatus.CassetteJammed, new(10, 30), d, N(0, 1, 0));
    }

    [Fact]
    public void Depot_d_un_50_et_d_un_10()
    {
        var (m, d) = Machine(N(1, 1, 0));
        var r = m.Deposit(10, 30, N(1, 0, 1));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(70, 30)), r);
        Assert.Equal(N(2, 1, 1), d.Stock);
    }
}
