using static Atm.Tests.Outils;

namespace Atm.Tests;

/// <summary>Le tableau d'exemples de la spec, ligne par ligne.</summary>
public class ExemplesTests
{
    [Fact]
    public void Retrait_60_avec_trois_billets_de_20()
    {
        var (r, d) = Executer(Compte(100, 0), N(1, 3, 0), null, Retrait(60));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 3, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(40, 60), r.Account);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_80_avec_une_coupure_de_chaque()
    {
        var (r, d) = Executer(Compte(100, 0), N(1, 1, 1), null, Retrait(80));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 1, 1), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(20, 80), r.Account);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (r, _) = Executer(Compte(105, 0), Plein, null, Retrait(100));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(5, 100), r.Account);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var (r, _) = Executer(Compte(115, 0), Plein, null, Retrait(110));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 1), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Compte(0, 110), r.Account);
    }

    [Fact]
    public void Retrait_110_solde_110_fonds_insuffisants_a_cause_des_frais()
    {
        var (r, d) = Executer(Compte(110, 0), Plein, null, Retrait(110));

        AssertRefus(AtmStatus.InsufficientFunds, r, Compte(110, 0), Plein, d);
    }

    [Fact]
    public void Retrait_60_deja_150_retires_plafond_depasse()
    {
        var (r, d) = Executer(Compte(250, 150), Plein, null, Retrait(60));

        AssertRefus(AtmStatus.DailyLimitExceeded, r, Compte(250, 150), Plein, d);
    }

    [Fact]
    public void Retrait_50_cassette_de_50_bloquee_impossible()
    {
        var (r, d) = Executer(Compte(250, 0), N(1, 0, 0), Denomination.Fifty, Retrait(50));

        AssertRefus(AtmStatus.CannotDispense, r, Compte(250, 0), N(1, 0, 0), d);
    }

    [Fact]
    public void Retrait_40_cassette_de_50_bloquee_servi_en_20()
    {
        var (r, d) = Executer(Compte(250, 0), N(2, 2, 0), Denomination.Fifty, Retrait(40));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 2, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(210, 40), r.Account);
        Assert.Equal(N(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_5_montant_invalide()
    {
        var (r, d) = Executer(Compte(0, 200), N(0, 0, 0), null, Retrait(5));

        AssertRefus(AtmStatus.InvalidAmount, r, Compte(0, 200), N(0, 0, 0), d);
    }

    [Fact]
    public void Depot_20_cassette_pleine()
    {
        var (r, d) = Executer(Compte(10, 0), N(0, 5, 0), null, Depot(N(0, 1, 0)));

        AssertRefus(AtmStatus.CassetteFull, r, Compte(10, 0), N(0, 5, 0), d);
    }

    [Fact]
    public void Depot_20_cassette_bloquee()
    {
        var (r, d) = Executer(Compte(10, 30), N(0, 1, 0), Denomination.Twenty, Depot(N(0, 1, 0)));

        AssertRefus(AtmStatus.CassetteJammed, r, Compte(10, 30), N(0, 1, 0), d);
    }

    [Fact]
    public void Depot_50_et_10_accepte()
    {
        var (r, d) = Executer(Compte(10, 30), N(1, 1, 0), null, Depot(N(1, 0, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(70, 30), r.Account);
        Assert.Equal(N(2, 1, 1), d.Stock);
    }
}
