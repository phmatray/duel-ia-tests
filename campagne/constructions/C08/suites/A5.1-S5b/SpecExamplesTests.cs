using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Les exemples de la spécification, un par un.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_trois_billets_de_20()
    {
        var (r, d) = Withdraw(100, 0, N(1, 3, 0), null, 60);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 3, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(40, 60), r.Account);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_80_avec_une_coupure_de_chaque()
    {
        var (r, d) = Withdraw(100, 0, N(1, 1, 1), null, 80);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 1, 1), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(20, 80), r.Account);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (r, d) = Withdraw(105, 0, N(5, 5, 5), null, 100);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(5, 100), r.Account);
        Assert.Equal(N(3, 5, 5), d.Stock);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var (r, d) = Withdraw(115, 0, N(5, 5, 5), null, 110);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 1), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(0, 110), r.Account);
        Assert.Equal(N(3, 5, 4), d.Stock);
    }

    [Fact]
    public void Retrait_110_refuse_car_les_frais_depassent_le_solde()
    {
        var (r, d) = Withdraw(110, 0, N(5, 5, 5), null, 110);
        AssertRefused(AtmStatus.InsufficientFunds, r, d, Acc(110, 0), N(5, 5, 5));
    }

    [Fact]
    public void Retrait_60_refuse_par_le_plafond_du_jour()
    {
        var (r, d) = Withdraw(250, 150, N(5, 5, 5), null, 60);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, d, Acc(250, 150), N(5, 5, 5));
    }

    [Fact]
    public void Retrait_50_impossible_si_la_cassette_de_50_est_bloquee()
    {
        var (r, d) = Withdraw(250, 0, N(1, 0, 0), Denomination.Fifty, 50);
        AssertRefused(AtmStatus.CannotDispense, r, d, Acc(250, 0), N(1, 0, 0));
    }

    [Fact]
    public void Retrait_40_en_billets_de_20_si_la_cassette_de_50_est_bloquee()
    {
        var (r, d) = Withdraw(250, 0, N(2, 2, 0), Denomination.Fifty, 40);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 2, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(210, 40), r.Account);
        Assert.Equal(N(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_5_montant_invalide_avant_tout_autre_controle()
    {
        var (r, d) = Withdraw(0, 200, N(0, 0, 0), null, 5);
        AssertRefused(AtmStatus.InvalidAmount, r, d, Acc(0, 200), N(0, 0, 0));
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine()
    {
        var (r, d) = Deposit(10, 0, N(0, 5, 0), null, N(0, 1, 0));
        AssertRefused(AtmStatus.CassetteFull, r, d, Acc(10, 0), N(0, 5, 0));
    }

    [Fact]
    public void Depot_dans_une_cassette_bloquee()
    {
        var (r, d) = Deposit(10, 30, N(0, 1, 0), Denomination.Twenty, N(0, 1, 0));
        AssertRefused(AtmStatus.CassetteJammed, r, d, Acc(10, 30), N(0, 1, 0));
    }

    [Fact]
    public void Depot_reussi_de_50_et_10()
    {
        var (r, d) = Deposit(10, 30, N(1, 1, 0), null, N(1, 0, 1));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(70, 30), r.Account);
        Assert.Equal(N(2, 1, 1), d.Stock);
    }
}
