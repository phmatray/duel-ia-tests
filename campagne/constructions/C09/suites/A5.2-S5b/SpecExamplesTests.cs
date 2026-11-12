using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_50x1_20x3_sort_trois_billets_de_20()
    {
        var (r, d) = Run(Acc(100, 0), N(1, 3, 0), null, Withdraw(60));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 3, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(40, 60), r.Account);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_80_avec_un_billet_de_chaque()
    {
        var (r, d) = Run(Acc(100, 0), N(1, 1, 1), null, Withdraw(80));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 1, 1), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(20, 80), r.Account);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (r, _) = Run(Acc(105, 0), N(5, 5, 5), null, Withdraw(100));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(5, 100), r.Account);
    }

    [Fact]
    public void Retrait_110_avec_frais_de_5()
    {
        var (r, _) = Run(Acc(115, 0), N(5, 5, 5), null, Withdraw(110));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 1), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(0, 110), r.Account);
    }

    [Fact]
    public void Retrait_110_avec_solde_110_est_refuse_a_cause_des_frais()
    {
        var (r, _) = Run(Acc(110, 0), N(5, 5, 5), null, Withdraw(110));

        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    [Fact]
    public void Retrait_60_avec_deja_150_retires_depasse_le_plafond()
    {
        var (r, _) = Run(Acc(250, 150), N(5, 5, 5), null, Withdraw(60));

        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    [Fact]
    public void Retrait_50_quand_seule_la_cassette_de_50_bloquee_en_contient()
    {
        var (r, _) = Run(Acc(250, 0), N(1, 0, 0), Denomination.Fifty, Withdraw(50));

        Assert.Equal(AtmStatus.CannotDispense, r.Status);
    }

    [Fact]
    public void Retrait_40_contourne_la_cassette_bloquee()
    {
        var (r, d) = Run(Acc(250, 0), N(2, 2, 0), Denomination.Fifty, Withdraw(40));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 2, 0), r.Notes);
        Assert.Equal(Acc(210, 40), r.Account);
        Assert.Equal(N(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_5_est_un_montant_invalide()
    {
        var (r, _) = Run(Acc(0, 200), N(0, 0, 0), null, Withdraw(5));

        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine()
    {
        var (r, _) = Run(Acc(10, 0), N(0, 5, 0), null, Deposit(N(0, 1, 0)));

        Assert.Equal(AtmStatus.CassetteFull, r.Status);
    }

    [Fact]
    public void Depot_dans_la_cassette_bloquee()
    {
        var (r, _) = Run(Acc(10, 30), N(0, 1, 0), Denomination.Twenty, Deposit(N(0, 1, 0)));

        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Fact]
    public void Depot_reussi_de_60()
    {
        var (r, d) = Run(Acc(10, 30), N(1, 1, 0), null, Deposit(N(1, 0, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(70, 30), r.Account);
        Assert.Equal(N(2, 1, 1), d.Stock);
    }
}
