using static Atm.Tests.Machine;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_trois_billets_de_20()
    {
        var (r, d) = Withdraw(100, 0, Notes(1, 3, 0), null, 60);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Notes(0, 3, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(40, 60), r.Account);
        Assert.Equal(Notes(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_80_avec_une_coupure_de_chaque()
    {
        var (r, d) = Withdraw(100, 0, Notes(1, 1, 1), null, 80);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Notes(1, 1, 1), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(20, 80), r.Account);
        Assert.Equal(Notes(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (r, _) = Withdraw(105, 0, Full, null, 100);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Notes(2, 0, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(5, 100), r.Account);
    }

    [Fact]
    public void Retrait_110_avec_frais_de_5()
    {
        var (r, _) = Withdraw(115, 0, Full, null, 110);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Notes(2, 0, 1), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(0, 110), r.Account);
    }

    [Fact]
    public void Retrait_110_refuse_car_les_frais_ne_sont_pas_couverts()
    {
        var (r, d) = Withdraw(110, 0, Full, null, 110);
        AssertRefused(AtmStatus.InsufficientFunds, r, d, 110, 0, Full);
    }

    [Fact]
    public void Retrait_60_refuse_par_le_plafond_du_jour()
    {
        var (r, d) = Withdraw(250, 150, Full, null, 60);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, d, 250, 150, Full);
    }

    [Fact]
    public void Retrait_50_impossible_si_la_seule_cassette_utile_est_bloquee()
    {
        var (r, d) = Withdraw(250, 0, Notes(1, 0, 0), Denomination.Fifty, 50);
        AssertRefused(AtmStatus.CannotDispense, r, d, 250, 0, Notes(1, 0, 0));
    }

    [Fact]
    public void Retrait_40_contourne_la_cassette_bloquee()
    {
        var (r, d) = Withdraw(250, 0, Notes(2, 2, 0), Denomination.Fifty, 40);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Notes(0, 2, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(210, 40), r.Account);
        Assert.Equal(Notes(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_5_montant_invalide_avant_tout_autre_controle()
    {
        var (r, d) = Withdraw(0, 200, Notes(0, 0, 0), null, 5);
        AssertRefused(AtmStatus.InvalidAmount, r, d, 0, 200, Notes(0, 0, 0));
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine()
    {
        var (r, d) = Deposit(10, 0, Notes(0, 5, 0), null, Notes(0, 1, 0));
        AssertRefused(AtmStatus.CassetteFull, r, d, 10, 0, Notes(0, 5, 0));
    }

    [Fact]
    public void Depot_dans_une_cassette_bloquee()
    {
        var (r, d) = Deposit(10, 30, Notes(0, 1, 0), Denomination.Twenty, Notes(0, 1, 0));
        AssertRefused(AtmStatus.CassetteJammed, r, d, 10, 30, Notes(0, 1, 0));
    }

    [Fact]
    public void Depot_de_50_et_10()
    {
        var (r, d) = Deposit(10, 30, Notes(1, 1, 0), null, Notes(1, 0, 1));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(70, 30), r.Account);
        Assert.Equal(Notes(2, 1, 1), d.Stock);
    }
}
