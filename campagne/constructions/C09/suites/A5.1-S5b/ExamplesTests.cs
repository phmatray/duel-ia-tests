namespace Atm.Tests;

using static N;

/// <summary>Les exemples de la spécification, un par un.</summary>
public class ExamplesTests
{
    [Fact]
    public void Retrait_60_avec_trois_billets_de_20()
    {
        var (r, d) = Run.Withdraw(100, 0, C(1, 3, 0), null, 60);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(0, 3, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(40, 60), r.Account);
        Assert.Equal(C(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_80_avec_un_billet_de_chaque()
    {
        var (r, d) = Run.Withdraw(100, 0, C(1, 1, 1), null, 80);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(1, 1, 1), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(20, 80), r.Account);
        Assert.Equal(C(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (r, _) = Run.Withdraw(105, 0, Full, null, 100);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(2, 0, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(5, 100), r.Account);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var (r, _) = Run.Withdraw(115, 0, Full, null, 110);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(2, 0, 1), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(0, 110), r.Account);
    }

    [Fact]
    public void Retrait_110_solde_110_insuffisant_a_cause_des_frais()
    {
        var (r, d) = Run.Withdraw(110, 0, Full, null, 110);
        AssertAtm.Refused(AtmStatus.InsufficientFunds, new(110, 0), Full, r, d);
    }

    [Fact]
    public void Retrait_60_deja_150_plafond_depasse()
    {
        var (r, d) = Run.Withdraw(250, 150, Full, null, 60);
        AssertAtm.Refused(AtmStatus.DailyLimitExceeded, new(250, 150), Full, r, d);
    }

    [Fact]
    public void Retrait_50_cassette_de_50_bloquee()
    {
        var (r, d) = Run.Withdraw(250, 0, C(1, 0, 0), Denomination.Fifty, 50);
        AssertAtm.Refused(AtmStatus.CannotDispense, new(250, 0), C(1, 0, 0), r, d);
    }

    [Fact]
    public void Retrait_40_cassette_de_50_bloquee_sort_des_20()
    {
        var (r, d) = Run.Withdraw(250, 0, C(2, 2, 0), Denomination.Fifty, 40);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(0, 2, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(210, 40), r.Account);
        Assert.Equal(C(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_5_montant_invalide()
    {
        var (r, d) = Run.Withdraw(0, 200, C(0, 0, 0), null, 5);
        AssertAtm.Refused(AtmStatus.InvalidAmount, new(0, 200), C(0, 0, 0), r, d);
    }

    [Fact]
    public void Depot_20_cassette_pleine()
    {
        var (r, d) = Run.Deposit(10, 0, C(0, 5, 0), null, C(0, 1, 0));
        AssertAtm.Refused(AtmStatus.CassetteFull, new(10, 0), C(0, 5, 0), r, d);
    }

    [Fact]
    public void Depot_20_cassette_bloquee()
    {
        var (r, d) = Run.Deposit(10, 30, C(0, 1, 0), Denomination.Twenty, C(0, 1, 0));
        AssertAtm.Refused(AtmStatus.CassetteJammed, new(10, 30), C(0, 1, 0), r, d);
    }

    [Fact]
    public void Depot_50_et_10_reussi()
    {
        var (r, d) = Run.Deposit(10, 30, C(1, 1, 0), null, C(1, 0, 1));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(70, 30), r.Account);
        Assert.Equal(C(2, 1, 1), d.Stock);
    }
}
