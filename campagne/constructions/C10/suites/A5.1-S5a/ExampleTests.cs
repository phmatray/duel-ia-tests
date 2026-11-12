using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Les exemples de la spec, un par ligne du tableau.</summary>
public class ExampleTests
{
    private static (AtmResult Result, SpyDispenser Dispenser) Run(AccountState account, NoteCounts stock, Denomination? jammed, Operation op)
    {
        var dispenser = new SpyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, op);
        return (result, dispenser);
    }

    [Fact]
    public void Retrait_60_avec_trois_billets_de_20()
    {
        var (r, d) = Run(Acc(100, 0), N(1, 3, 0), null, W(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, Acc(40, 60)), r);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_80_avec_une_coupure_de_chaque()
    {
        var (r, d) = Run(Acc(100, 0), N(1, 1, 1), null, W(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, Acc(20, 80)), r);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (r, d) = Run(Acc(105, 0), N(5, 5, 5), null, W(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(5, 100)), r);
        Assert.Equal(N(3, 5, 5), d.Stock);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var (r, d) = Run(Acc(115, 0), N(5, 5, 5), null, W(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, Acc(0, 110)), r);
        Assert.Equal(N(3, 5, 4), d.Stock);
    }

    [Fact]
    public void Retrait_110_solde_insuffisant_a_cause_des_frais()
    {
        var (r, d) = Run(Acc(110, 0), N(5, 5, 5), null, W(110));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, Acc(110, 0)), r);
        Assert.Equal(N(5, 5, 5), d.Stock);
    }

    [Fact]
    public void Retrait_60_plafond_du_jour_depasse()
    {
        var (r, d) = Run(Acc(250, 150), N(5, 5, 5), null, W(60));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, Acc(250, 150)), r);
        Assert.Equal(N(5, 5, 5), d.Stock);
    }

    [Fact]
    public void Retrait_50_cassette_de_50_bloquee()
    {
        var (r, d) = Run(Acc(250, 0), N(1, 0, 0), Denomination.Fifty, W(50));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_40_contourne_la_cassette_bloquee()
    {
        var (r, d) = Run(Acc(250, 0), N(2, 2, 0), Denomination.Fifty, W(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, Acc(210, 40)), r);
        Assert.Equal(N(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_5_montant_invalide()
    {
        var (r, d) = Run(Acc(0, 200), N(0, 0, 0), null, W(5));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Acc(0, 200)), r);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine()
    {
        var (r, d) = Run(Acc(10, 0), N(0, 5, 0), null, D(0, 1, 0));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, Acc(10, 0)), r);
        Assert.Equal(N(0, 5, 0), d.Stock);
    }

    [Fact]
    public void Depot_dans_une_cassette_bloquee()
    {
        var (r, d) = Run(Acc(10, 30), N(0, 1, 0), Denomination.Twenty, D(0, 1, 0));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, Acc(10, 30)), r);
        Assert.Equal(N(0, 1, 0), d.Stock);
    }

    [Fact]
    public void Depot_reussi()
    {
        var (r, d) = Run(Acc(10, 30), N(1, 1, 0), null, D(1, 0, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(70, 30)), r);
        Assert.Equal(N(2, 1, 1), d.Stock);
    }
}
