using static Atm.Tests.Support;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_seulement_des_20_et_un_50_sort_trois_20()
    {
        var (atm, dispenser) = Machine(Notes(1, 3, 0));

        var result = atm.Execute(Account(100, 0), Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 3, 0), 0, Account(40, 60)), result);
        Assert.Equal(Notes(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_80_sort_un_billet_de_chaque()
    {
        var (atm, dispenser) = Machine(Notes(1, 1, 1));

        var result = atm.Execute(Account(100, 0), Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 1, 1), 0, Account(20, 80)), result);
        Assert.Equal(Notes(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_100_est_sans_frais()
    {
        var (atm, _) = Machine(Full);

        var result = atm.Execute(Account(105, 0), Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 0), 0, Account(5, 100)), result);
    }

    [Fact]
    public void Retrait_110_preleve_5_de_frais()
    {
        var (atm, _) = Machine(Full);

        var result = atm.Execute(Account(115, 0), Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 1), 5, Account(0, 110)), result);
    }

    [Fact]
    public void Retrait_110_avec_solde_110_est_refuse_a_cause_des_frais()
    {
        var (atm, dispenser) = Machine(Full);

        var result = atm.Execute(Account(110, 0), Withdraw(110));

        AssertRefused(AtmStatus.InsufficientFunds, result, Account(110, 0), dispenser, Full);
    }

    [Fact]
    public void Retrait_qui_depasse_le_plafond_du_jour()
    {
        var (atm, dispenser) = Machine(Full);

        var result = atm.Execute(Account(250, 150), Withdraw(60));

        AssertRefused(AtmStatus.DailyLimitExceeded, result, Account(250, 150), dispenser, Full);
    }

    [Fact]
    public void Retrait_impossible_quand_la_seule_cassette_utile_est_bloquee()
    {
        var (atm, dispenser) = Machine(Notes(1, 0, 0), Denomination.Fifty);

        var result = atm.Execute(Account(250, 0), Withdraw(50));

        AssertRefused(AtmStatus.CannotDispense, result, Account(250, 0), dispenser, Notes(1, 0, 0));
    }

    [Fact]
    public void Retrait_40_avec_les_50_bloques_sort_deux_20()
    {
        var (atm, dispenser) = Machine(Notes(2, 2, 0), Denomination.Fifty);

        var result = atm.Execute(Account(250, 0), Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 2, 0), 0, Account(210, 40)), result);
        Assert.Equal(Notes(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_de_5_est_un_montant_invalide_avant_tout_autre_controle()
    {
        var (atm, dispenser) = Machine(Notes(0, 0, 0));

        var result = atm.Execute(Account(0, 200), Withdraw(5));

        AssertRefused(AtmStatus.InvalidAmount, result, Account(0, 200), dispenser, Notes(0, 0, 0));
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine()
    {
        var (atm, dispenser) = Machine(Notes(0, 5, 0));

        var result = atm.Execute(Account(10, 0), Deposit(Notes(0, 1, 0)));

        AssertRefused(AtmStatus.CassetteFull, result, Account(10, 0), dispenser, Notes(0, 5, 0));
    }

    [Fact]
    public void Depot_dans_une_cassette_bloquee()
    {
        var (atm, dispenser) = Machine(Notes(0, 1, 0), Denomination.Twenty);

        var result = atm.Execute(Account(10, 30), Deposit(Notes(0, 1, 0)));

        AssertRefused(AtmStatus.CassetteJammed, result, Account(10, 30), dispenser, Notes(0, 1, 0));
    }

    [Fact]
    public void Depot_reussi_range_les_billets_et_credite_le_compte()
    {
        var (atm, dispenser) = Machine(Notes(1, 1, 0));

        var result = atm.Execute(Account(10, 30), Deposit(Notes(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(70, 30)), result);
        Assert.Equal(Notes(2, 1, 1), dispenser.Stock);
    }
}
