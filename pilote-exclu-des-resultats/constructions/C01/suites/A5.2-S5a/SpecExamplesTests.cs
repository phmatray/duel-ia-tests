using static Atm.Tests.Fixture;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_trois_billets_de_20()
    {
        var (machine, spy) = Machine(Notes(1, 3, 0));

        var result = machine.Execute(new(100, 0), new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 3, 0), 0, new(40, 60)), result);
        Assert.Equal(Notes(1, 0, 0), spy.Stock);
    }

    [Fact]
    public void Retrait_80_avec_un_billet_de_chaque()
    {
        var (machine, spy) = Machine(Notes(1, 1, 1));

        var result = machine.Execute(new(100, 0), new Operation.Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 1, 1), 0, new(20, 80)), result);
        Assert.Equal(Notes(0, 0, 0), spy.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (machine, _) = Machine(Full);

        var result = machine.Execute(new(105, 0), new Operation.Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 0), 0, new(5, 100)), result);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var (machine, _) = Machine(Full);

        var result = machine.Execute(new(115, 0), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 1), 5, new(0, 110)), result);
    }

    [Fact]
    public void Retrait_110_solde_110_insuffisant_a_cause_des_frais()
    {
        var (machine, _) = Machine(Full);

        var result = machine.Execute(new(110, 0), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, new(110, 0)), result);
    }

    [Fact]
    public void Retrait_au_dela_du_plafond_du_jour()
    {
        var (machine, _) = Machine(Full);

        var result = machine.Execute(new(250, 150), new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, new(250, 150)), result);
    }

    [Fact]
    public void Retrait_impossible_si_seule_la_cassette_bloquee_convient()
    {
        var (machine, spy) = Machine(Notes(1, 0, 0), Denomination.Fifty);

        var result = machine.Execute(new(250, 0), new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new(250, 0)), result);
        Assert.Equal(Notes(1, 0, 0), spy.Stock);
    }

    [Fact]
    public void Retrait_contourne_la_cassette_bloquee()
    {
        var (machine, spy) = Machine(Notes(2, 2, 0), Denomination.Fifty);

        var result = machine.Execute(new(250, 0), new Operation.Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 2, 0), 0, new(210, 40)), result);
        Assert.Equal(Notes(2, 0, 0), spy.Stock);
    }

    [Fact]
    public void Retrait_de_5_est_un_montant_invalide_meme_au_plafond()
    {
        var (machine, _) = Machine(Notes(0, 0, 0));

        var result = machine.Execute(new(0, 200), new Operation.Withdraw(5));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, new(0, 200)), result);
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine()
    {
        var (machine, spy) = Machine(Notes(0, 5, 0));

        var result = machine.Execute(new(10, 0), new Operation.Deposit(Notes(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, new(10, 0)), result);
        Assert.Equal(Notes(0, 5, 0), spy.Stock);
    }

    [Fact]
    public void Depot_dans_la_cassette_bloquee()
    {
        var (machine, spy) = Machine(Notes(0, 1, 0), Denomination.Twenty);

        var result = machine.Execute(new(10, 30), new Operation.Deposit(Notes(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, new(10, 30)), result);
        Assert.Equal(Notes(0, 1, 0), spy.Stock);
    }

    [Fact]
    public void Depot_reussi()
    {
        var (machine, spy) = Machine(Notes(1, 1, 0));

        var result = machine.Execute(new(10, 30), new Operation.Deposit(Notes(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(70, 30)), result);
        Assert.Equal(Notes(2, 1, 1), spy.Stock);
    }
}
