using static Atm.Tests.Fixture;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_seulement_des_20_sort_trois_billets_de_20()
    {
        var (result, dispenser) = Withdraw(Account(100, 0), 60, Notes(1, 3, 0));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(0, 3, 0), result.Notes);
        Assert.Equal(Account(40, 60), result.Account);
        Assert.Equal(Notes(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_80_utilise_un_billet_de_chaque_coupure()
    {
        var (result, dispenser) = Withdraw(Account(100, 0), 80, Notes(1, 1, 1));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(1, 1, 1), result.Notes);
        Assert.Equal(Account(20, 80), result.Account);
        Assert.Equal(Notes(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (result, _) = Withdraw(Account(105, 0), 100, Full);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(2, 0, 0), result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(5, 100), result.Account);
    }

    [Fact]
    public void Retrait_110_avec_frais_de_5()
    {
        var (result, _) = Withdraw(Account(115, 0), 110, Full);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(2, 0, 1), result.Notes);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Account(0, 110), result.Account);
    }

    [Fact]
    public void Retrait_110_refuse_quand_le_solde_ne_couvre_pas_les_frais()
    {
        var account = Account(110, 0);
        var (result, dispenser) = Withdraw(account, 110, Full);

        AssertRefused(AtmStatus.InsufficientFunds, result, account, dispenser, Full);
    }

    [Fact]
    public void Retrait_60_refuse_au_dela_du_plafond_du_jour()
    {
        var account = Account(250, 150);
        var (result, dispenser) = Withdraw(account, 60, Full);

        AssertRefused(AtmStatus.DailyLimitExceeded, result, account, dispenser, Full);
    }

    [Fact]
    public void Retrait_50_refuse_quand_la_seule_cassette_utile_est_bloquee()
    {
        var account = Account(250, 0);
        var stock = Notes(1, 0, 0);
        var (result, dispenser) = Withdraw(account, 50, stock, Denomination.Fifty);

        AssertRefused(AtmStatus.CannotDispense, result, account, dispenser, stock);
    }

    [Fact]
    public void Retrait_40_contourne_la_cassette_bloquee()
    {
        var (result, dispenser) = Withdraw(Account(250, 0), 40, Notes(2, 2, 0), Denomination.Fifty);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(0, 2, 0), result.Notes);
        Assert.Equal(Account(210, 40), result.Account);
        Assert.Equal(Notes(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_5_est_un_montant_invalide()
    {
        var account = Account(0, 200);
        var stock = Notes(0, 0, 0);
        var (result, dispenser) = Withdraw(account, 5, stock);

        AssertRefused(AtmStatus.InvalidAmount, result, account, dispenser, stock);
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine_est_refuse()
    {
        var account = Account(10, 0);
        var stock = Notes(0, 5, 0);
        var (result, dispenser) = Deposit(account, Notes(0, 1, 0), stock);

        AssertRefused(AtmStatus.CassetteFull, result, account, dispenser, stock);
    }

    [Fact]
    public void Depot_dans_une_cassette_bloquee_est_refuse()
    {
        var account = Account(10, 30);
        var stock = Notes(0, 1, 0);
        var (result, dispenser) = Deposit(account, Notes(0, 1, 0), stock, Denomination.Twenty);

        AssertRefused(AtmStatus.CassetteJammed, result, account, dispenser, stock);
    }

    [Fact]
    public void Depot_de_50_et_10_credite_le_compte_et_remplit_le_module()
    {
        var (result, dispenser) = Deposit(Account(10, 30), Notes(1, 0, 1), Notes(1, 1, 0));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(70, 30), result.Account);
        Assert.Equal(Notes(2, 1, 1), dispenser.Stock);
    }
}
