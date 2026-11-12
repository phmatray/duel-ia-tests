using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_seulement_des_20_sort_trois_20()
    {
        var (r, d) = Run(Acc(100, 0), N(1, 3, 0), null, Withdraw(60));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 3, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(40, 60), r.Account);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_80_sort_un_billet_de_chaque()
    {
        var (r, d) = Run(Acc(100, 0), N(1, 1, 1), null, Withdraw(80));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 1, 1), r.Notes);
        Assert.Equal(Acc(20, 80), r.Account);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_100_est_sans_frais()
    {
        var (r, _) = Run(Acc(105, 0), Full, null, Withdraw(100));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(5, 100), r.Account);
    }

    [Fact]
    public void Retrait_110_preleve_5_de_frais()
    {
        var (r, _) = Run(Acc(115, 0), Full, null, Withdraw(110));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 1), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(0, 110), r.Account);
    }

    [Fact]
    public void Retrait_110_avec_solde_110_est_insuffisant_a_cause_des_frais()
    {
        var (r, d) = Run(Acc(110, 0), Full, null, Withdraw(110));

        AssertRefused(r, AtmStatus.InsufficientFunds, Acc(110, 0));
        Assert.Empty(d.Dispensed);
    }

    [Fact]
    public void Retrait_au_dela_du_plafond_du_jour()
    {
        var (r, d) = Run(Acc(250, 150), Full, null, Withdraw(60));

        AssertRefused(r, AtmStatus.DailyLimitExceeded, Acc(250, 150));
        Assert.Empty(d.Dispensed);
    }

    [Fact]
    public void Retrait_impossible_si_le_seul_billet_utile_est_bloque()
    {
        var (r, d) = Run(Acc(250, 0), N(1, 0, 0), Denomination.Fifty, Withdraw(50));

        AssertRefused(r, AtmStatus.CannotDispense, Acc(250, 0));
        Assert.Empty(d.Dispensed);
        Assert.Equal(N(1, 0, 0), d.Stock);
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
    public void Retrait_de_5_est_un_montant_invalide()
    {
        var (r, _) = Run(Acc(0, 200), N(0, 0, 0), null, Withdraw(5));

        AssertRefused(r, AtmStatus.InvalidAmount, Acc(0, 200));
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine()
    {
        var (r, d) = Run(Acc(10, 0), N(0, 5, 0), null, Deposit(N(0, 1, 0)));

        AssertRefused(r, AtmStatus.CassetteFull, Acc(10, 0));
        Assert.Equal(N(0, 5, 0), d.Stock);
    }

    [Fact]
    public void Depot_dans_une_cassette_bloquee()
    {
        var (r, d) = Run(Acc(10, 30), N(0, 1, 0), Denomination.Twenty, Deposit(N(0, 1, 0)));

        AssertRefused(r, AtmStatus.CassetteJammed, Acc(10, 30));
        Assert.Equal(N(0, 1, 0), d.Stock);
    }

    [Fact]
    public void Depot_reussi_credite_le_compte_et_remplit_les_cassettes()
    {
        var (r, d) = Run(Acc(10, 30), N(1, 1, 0), null, Deposit(N(1, 0, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(70, 30), r.Account);
        Assert.Equal(N(2, 1, 1), d.Stock);
    }

    internal static void AssertRefused(AtmResult r, AtmStatus status, AccountState account)
    {
        Assert.Equal(status, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
    }
}
