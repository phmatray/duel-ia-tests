using static Atm.Tests.Helpers;

namespace Atm.Tests;

/// <summary>Les exemples du tableau « Exemples » de la spec, un par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_avec_trois_billets_de_20()
    {
        var (r, d) = Run(100, 0, N(1, 3, 0), null, new Operation.Withdraw(60));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 3, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(40, 60), r.Account);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_80_avec_une_coupure_de_chaque()
    {
        var (r, d) = Run(100, 0, N(1, 1, 1), null, new Operation.Withdraw(80));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 1, 1), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(20, 80), r.Account);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (r, d) = Run(105, 0, Full, null, new Operation.Withdraw(100));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(5, 100), r.Account);
        Assert.Equal(N(3, 5, 5), d.Stock);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var (r, d) = Run(115, 0, Full, null, new Operation.Withdraw(110));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 1), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(0, 110), r.Account);
        Assert.Equal(N(3, 5, 4), d.Stock);
    }

    [Fact]
    public void Retrait_110_refuse_car_les_frais_depassent_le_solde()
    {
        var (r, d) = Run(110, 0, Full, null, new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, new AccountState(110, 0)), r);
        Assert.Equal(Full, d.Stock);
    }

    [Fact]
    public void Retrait_60_refuse_par_le_plafond_du_jour()
    {
        var (r, d) = Run(250, 150, Full, null, new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, new AccountState(250, 150)), r);
        Assert.Equal(Full, d.Stock);
    }

    [Fact]
    public void Retrait_50_refuse_car_la_cassette_de_50_est_bloquee()
    {
        var (r, d) = Run(250, 0, N(1, 0, 0), Denomination.Fifty, new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new AccountState(250, 0)), r);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_40_contourne_la_cassette_bloquee()
    {
        var (r, d) = Run(250, 0, N(2, 2, 0), Denomination.Fifty, new Operation.Withdraw(40));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 2, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(210, 40), r.Account);
        Assert.Equal(N(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_5_montant_invalide()
    {
        var (r, d) = Run(0, 200, N(0, 0, 0), null, new Operation.Withdraw(5));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, new AccountState(0, 200)), r);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine()
    {
        var (r, d) = Run(10, 0, N(0, 5, 0), null, new Operation.Deposit(N(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, new AccountState(10, 0)), r);
        Assert.Equal(N(0, 5, 0), d.Stock);
    }

    [Fact]
    public void Depot_dans_la_cassette_bloquee()
    {
        var (r, d) = Run(10, 30, N(0, 1, 0), Denomination.Twenty, new Operation.Deposit(N(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, new AccountState(10, 30)), r);
        Assert.Equal(N(0, 1, 0), d.Stock);
    }

    [Fact]
    public void Depot_accepte()
    {
        var (r, d) = Run(10, 30, N(1, 1, 0), null, new Operation.Deposit(N(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(70, 30)), r);
        Assert.Equal(N(2, 1, 1), d.Stock);
    }
}
