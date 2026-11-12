namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExampleTests
{
    private static AtmResult Execute(int balance, int withdrawn, NoteCounts stock, Denomination? jammed, Operation op, out MoneyDispenser dispenser)
    {
        dispenser = new MoneyDispenser(stock, jammed);
        return new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), op);
    }

    [Fact]
    public void Retrait_60_avec_20x3()
    {
        var r = Execute(100, 0, new(1, 3, 0), null, new Operation.Withdraw(60), out var d);
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 3, 0), 0, new(40, 60)), r);
        Assert.Equal(new NoteCounts(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_80_avec_une_coupure_de_chaque()
    {
        var r = Execute(100, 0, new(1, 1, 1), null, new Operation.Withdraw(80), out var d);
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 1, 1), 0, new(20, 80)), r);
        Assert.Equal(NoteCounts.None, d.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var r = Execute(105, 0, new(5, 5, 5), null, new Operation.Withdraw(100), out _);
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 0), 0, new(5, 100)), r);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var r = Execute(115, 0, new(5, 5, 5), null, new Operation.Withdraw(110), out _);
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 1), 5, new(0, 110)), r);
    }

    [Fact]
    public void Retrait_110_solde_insuffisant_a_cause_des_frais()
    {
        var r = Execute(110, 0, new(5, 5, 5), null, new Operation.Withdraw(110), out _);
        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, new(110, 0)), r);
    }

    [Fact]
    public void Retrait_60_au_dela_du_plafond()
    {
        var r = Execute(250, 150, new(5, 5, 5), null, new Operation.Withdraw(60), out _);
        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, new(250, 150)), r);
    }

    [Fact]
    public void Retrait_50_cassette_50_bloquee()
    {
        var r = Execute(250, 0, new(1, 0, 0), Denomination.Fifty, new Operation.Withdraw(50), out var d);
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new(250, 0)), r);
        Assert.Equal(new NoteCounts(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_40_cassette_50_bloquee()
    {
        var r = Execute(250, 0, new(2, 2, 0), Denomination.Fifty, new Operation.Withdraw(40), out var d);
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 2, 0), 0, new(210, 40)), r);
        Assert.Equal(new NoteCounts(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Retrait_5_montant_invalide()
    {
        var r = Execute(0, 200, NoteCounts.None, null, new Operation.Withdraw(5), out _);
        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, new(0, 200)), r);
    }

    [Fact]
    public void Depot_20_cassette_pleine()
    {
        var r = Execute(10, 0, new(0, 5, 0), null, new Operation.Deposit(new(0, 1, 0)), out var d);
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, new(10, 0)), r);
        Assert.Equal(new NoteCounts(0, 5, 0), d.Stock);
    }

    [Fact]
    public void Depot_20_cassette_bloquee()
    {
        var r = Execute(10, 30, new(0, 1, 0), Denomination.Twenty, new Operation.Deposit(new(0, 1, 0)), out var d);
        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, new(10, 30)), r);
        Assert.Equal(new NoteCounts(0, 1, 0), d.Stock);
    }

    [Fact]
    public void Depot_50_et_10()
    {
        var r = Execute(10, 30, new(1, 1, 0), null, new Operation.Deposit(new(1, 0, 1)), out var d);
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(70, 30)), r);
        Assert.Equal(new NoteCounts(2, 1, 1), d.Stock);
    }
}
