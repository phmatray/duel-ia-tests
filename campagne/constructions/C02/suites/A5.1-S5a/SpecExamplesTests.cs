using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_seulement_des_20_donne_20x3()
    {
        var (machine, dispenser) = Real(Notes(1, 3, 0));

        var result = machine.Execute(Account(100, 0), Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 3, 0), 0, Account(40, 60)), result);
        Assert.Equal(Notes(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_80_utilise_une_coupure_de_chaque()
    {
        var (machine, dispenser) = Real(Notes(1, 1, 1));

        var result = machine.Execute(Account(100, 0), Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 1, 1), 0, Account(20, 80)), result);
        Assert.Equal(Notes(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (machine, _) = Real(Notes(5, 5, 5));

        var result = machine.Execute(Account(105, 0), Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 0), 0, Account(5, 100)), result);
    }

    [Fact]
    public void Retrait_110_avec_frais_de_5()
    {
        var (machine, _) = Real(Notes(5, 5, 5));

        var result = machine.Execute(Account(115, 0), Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 1), 5, Account(0, 110)), result);
    }

    [Fact]
    public void Retrait_110_avec_solde_110_est_refuse_a_cause_des_frais()
    {
        var (machine, _) = Real(Notes(5, 5, 5));

        var result = machine.Execute(Account(110, 0), Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, Account(110, 0)), result);
    }

    [Fact]
    public void Retrait_au_dela_du_plafond_du_jour()
    {
        var (machine, _) = Real(Notes(5, 5, 5));

        var result = machine.Execute(Account(250, 150), Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, Account(250, 150)), result);
    }

    [Fact]
    public void Retrait_avec_seule_cassette_utile_bloquee()
    {
        var (machine, _) = Real(Notes(1, 0, 0), Denomination.Fifty);

        var result = machine.Execute(Account(250, 0), Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Account(250, 0)), result);
    }

    [Fact]
    public void Retrait_contourne_la_cassette_bloquee()
    {
        var (machine, dispenser) = Real(Notes(2, 2, 0), Denomination.Fifty);

        var result = machine.Execute(Account(250, 0), Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 2, 0), 0, Account(210, 40)), result);
        Assert.Equal(Notes(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_de_5_est_un_montant_invalide()
    {
        var (machine, _) = Real(Notes(0, 0, 0));

        var result = machine.Execute(Account(0, 200), Withdraw(5));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Account(0, 200)), result);
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine()
    {
        var (machine, dispenser) = Real(Notes(0, 5, 0));

        var result = machine.Execute(Account(10, 0), Deposit(Notes(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, Account(10, 0)), result);
        Assert.Equal(Notes(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Depot_dans_une_cassette_bloquee()
    {
        var (machine, dispenser) = Real(Notes(0, 1, 0), Denomination.Twenty);

        var result = machine.Execute(Account(10, 30), Deposit(Notes(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, Account(10, 30)), result);
        Assert.Equal(Notes(0, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Depot_reussi()
    {
        var (machine, dispenser) = Real(Notes(1, 1, 0));

        var result = machine.Execute(Account(10, 30), Deposit(Notes(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(70, 30)), result);
        Assert.Equal(Notes(2, 1, 1), dispenser.Stock);
    }
}
