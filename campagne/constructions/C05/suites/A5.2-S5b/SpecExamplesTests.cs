using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Les exemples de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_trois_billets_de_20()
    {
        var (machine, spy) = Machine(N(1, 3, 0));
        var r = machine.Execute(Acc(100, 0), new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, Acc(40, 60)), r);
        Assert.Equal(N(1, 0, 0), spy.Stock);
    }

    [Fact]
    public void Retrait_80_avec_une_coupure_de_chaque()
    {
        var r = Withdraw(Acc(100, 0), 80, N(1, 1, 1));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, Acc(20, 80)), r);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var r = Withdraw(Acc(105, 0), 100, Full);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(5, 100)), r);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var r = Withdraw(Acc(115, 0), 110, Full);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, Acc(0, 110)), r);
    }

    [Fact]
    public void Retrait_110_refuse_car_les_frais_depassent_le_solde()
    {
        var r = Withdraw(Acc(110, 0), 110, Full);
        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, N(), 0, Acc(110, 0)), r);
    }

    [Fact]
    public void Retrait_60_au_dela_du_plafond()
    {
        var r = Withdraw(Acc(250, 150), 60, Full);
        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, N(), 0, Acc(250, 150)), r);
    }

    [Fact]
    public void Retrait_50_avec_la_cassette_de_50_bloquee()
    {
        var r = Withdraw(Acc(250, 0), 50, N(1, 0, 0), Denomination.Fifty);
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, N(), 0, Acc(250, 0)), r);
    }

    [Fact]
    public void Retrait_40_en_contournant_la_cassette_bloquee()
    {
        var r = Withdraw(Acc(250, 0), 40, N(2, 2, 0), Denomination.Fifty);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, Acc(210, 40)), r);
    }

    [Fact]
    public void Retrait_5_montant_invalide()
    {
        var r = Withdraw(Acc(0, 200), 5, N());
        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, N(), 0, Acc(0, 200)), r);
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine()
    {
        var (machine, spy) = Machine(N(0, 5, 0));
        var r = machine.Execute(Acc(10, 0), new Operation.Deposit(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, N(), 0, Acc(10, 0)), r);
        Assert.Equal(N(0, 5, 0), spy.Stock);
    }

    [Fact]
    public void Depot_dans_une_cassette_bloquee()
    {
        var (machine, spy) = Machine(N(0, 1, 0), Denomination.Twenty);
        var r = machine.Execute(Acc(10, 30), new Operation.Deposit(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, N(), 0, Acc(10, 30)), r);
        Assert.Equal(N(0, 1, 0), spy.Stock);
    }

    [Fact]
    public void Depot_reussi()
    {
        var (machine, spy) = Machine(N(1, 1, 0));
        var r = machine.Execute(Acc(10, 30), new Operation.Deposit(N(1, 0, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(), 0, Acc(70, 30)), r);
        Assert.Equal(N(2, 1, 1), spy.Stock);
    }
}
