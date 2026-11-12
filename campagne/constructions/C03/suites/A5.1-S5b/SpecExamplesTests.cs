using static Atm.Tests.TestSupport;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_trois_billets_de_20()
    {
        var (r, d) = Withdraw(100, 0, N(1, 3, 0), null, 60);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, new AccountState(40, 60)), r);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_80_avec_un_billet_de_chaque()
    {
        var (r, d) = Withdraw(100, 0, N(1, 1, 1), null, 80);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, new AccountState(20, 80)), r);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (r, d) = Withdraw(105, 0, FullStock, null, 100);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, new AccountState(5, 100)), r);
        Assert.Equal(N(3, 5, 5), d.Stock);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var (r, d) = Withdraw(115, 0, FullStock, null, 110);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, new AccountState(0, 110)), r);
        Assert.Equal(N(3, 5, 4), d.Stock);
    }

    [Fact]
    public void Retrait_110_solde_110_fonds_insuffisants_a_cause_des_frais() =>
        AssertRefused(AtmStatus.InsufficientFunds, Withdraw(110, 0, FullStock, null, 110), 110, 0, FullStock);

    [Fact]
    public void Retrait_60_deja_150_retires_plafond_depasse() =>
        AssertRefused(AtmStatus.DailyLimitExceeded, Withdraw(250, 150, FullStock, null, 60), 250, 150, FullStock);

    [Fact]
    public void Retrait_50_seul_billet_dans_la_cassette_bloquee() =>
        AssertRefused(AtmStatus.CannotDispense, Withdraw(250, 0, N(1, 0, 0), Denomination.Fifty, 50), 250, 0, N(1, 0, 0));

    [Fact]
    public void Retrait_40_cassette_50_bloquee()
    {
        var (r, d) = Withdraw(250, 0, N(2, 2, 0), Denomination.Fifty, 40);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, new AccountState(210, 40)), r);
        Assert.Equal(N(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_5_montant_invalide() =>
        AssertRefused(AtmStatus.InvalidAmount, Withdraw(0, 200, N(0, 0, 0), null, 5), 0, 200, N(0, 0, 0));

    [Fact]
    public void Depot_20_cassette_pleine() =>
        AssertRefused(AtmStatus.CassetteFull, Deposit(10, 0, N(0, 5, 0), null, N(0, 1, 0)), 10, 0, N(0, 5, 0));

    [Fact]
    public void Depot_20_cassette_bloquee() =>
        AssertRefused(AtmStatus.CassetteJammed, Deposit(10, 30, N(0, 1, 0), Denomination.Twenty, N(0, 1, 0)), 10, 30, N(0, 1, 0));

    [Fact]
    public void Depot_50_et_10()
    {
        var (r, d) = Deposit(10, 30, N(1, 1, 0), null, N(1, 0, 1));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(70, 30)), r);
        Assert.Equal(N(2, 1, 1), d.Stock);
    }
}
