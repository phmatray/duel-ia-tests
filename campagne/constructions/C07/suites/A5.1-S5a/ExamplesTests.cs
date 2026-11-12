using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class ExamplesTests
{
    [Fact]
    public void Retrait_60_avec_20x3_seulement()
    {
        var (atm, dispenser) = Machine(N(1, 3, 0));
        var r = atm.Execute(Acc(100, 0), Withdraw(60));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 3, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(40, 60), r.Account);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_80_une_de_chaque()
    {
        var (atm, dispenser) = Machine(N(1, 1, 1));
        var r = atm.Execute(Acc(100, 0), Withdraw(80));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 1, 1), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(20, 80), r.Account);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (atm, _) = Machine(N(5, 5, 5));
        var r = atm.Execute(Acc(105, 0), Withdraw(100));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(5, 100), r.Account);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var (atm, _) = Machine(N(5, 5, 5));
        var r = atm.Execute(Acc(115, 0), Withdraw(110));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 1), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(0, 110), r.Account);
    }

    [Fact]
    public void Retrait_110_solde_110_fonds_insuffisants_a_cause_des_frais()
    {
        var (atm, dispenser) = Machine(N(5, 5, 5));
        var r = atm.Execute(Acc(110, 0), Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, Acc(110, 0)), r);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Retrait_60_deja_150_plafond()
    {
        var (atm, _) = Machine(N(5, 5, 5));
        var r = atm.Execute(Acc(250, 150), Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, Acc(250, 150)), r);
    }

    [Fact]
    public void Retrait_50_cassette_50_bloquee()
    {
        var (atm, dispenser) = Machine(N(1, 0, 0), Denomination.Fifty);
        var r = atm.Execute(Acc(250, 0), Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_40_contourne_la_cassette_bloquee()
    {
        var (atm, dispenser) = Machine(N(2, 2, 0), Denomination.Fifty);
        var r = atm.Execute(Acc(250, 0), Withdraw(40));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 2, 0), r.Notes);
        Assert.Equal(Acc(210, 40), r.Account);
        Assert.Equal(N(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_5_montant_invalide_avant_le_plafond()
    {
        var (atm, _) = Machine(N(0, 0, 0));
        var r = atm.Execute(Acc(0, 200), Withdraw(5));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Acc(0, 200)), r);
    }

    [Fact]
    public void Depot_dans_cassette_pleine()
    {
        var (atm, dispenser) = Machine(N(0, 5, 0));
        var r = atm.Execute(Acc(10, 0), Deposit(N(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, Acc(10, 0)), r);
        Assert.Equal(N(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Depot_dans_cassette_bloquee()
    {
        var (atm, dispenser) = Machine(N(0, 1, 0), Denomination.Twenty);
        var r = atm.Execute(Acc(10, 30), Deposit(N(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, Acc(10, 30)), r);
        Assert.Equal(N(0, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Depot_50_et_10()
    {
        var (atm, dispenser) = Machine(N(1, 1, 0));
        var r = atm.Execute(Acc(10, 30), Deposit(N(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(70, 30)), r);
        Assert.Equal(N(2, 1, 1), dispenser.Stock);
    }
}
