namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static (AtmResult Result, IMoneyDispenser Module) Run(
        int balance, int withdrawn, NoteCounts stock, Denomination? jammed, Operation op)
    {
        var module = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(module).Execute(new AccountState(balance, withdrawn), op);
        return (result, module);
    }

    [Fact]
    public void Retrait_60_avec_seulement_des_20_et_un_50()
    {
        var (r, m) = Run(100, 0, N(1, 3, 0), null, new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, new AccountState(40, 60)), r);
        Assert.Equal(N(1, 0, 0), m.Stock);
    }

    [Fact]
    public void Retrait_80_avec_un_billet_de_chaque()
    {
        var (r, m) = Run(100, 0, N(1, 1, 1), null, new Operation.Withdraw(80));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, new AccountState(20, 80)), r);
        Assert.Equal(N(0, 0, 0), m.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais()
    {
        var (r, m) = Run(105, 0, N(5, 5, 5), null, new Operation.Withdraw(100));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, new AccountState(5, 100)), r);
        Assert.Equal(N(3, 5, 5), m.Stock);
    }

    [Fact]
    public void Retrait_110_avec_frais()
    {
        var (r, m) = Run(115, 0, N(5, 5, 5), null, new Operation.Withdraw(110));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, new AccountState(0, 110)), r);
        Assert.Equal(N(3, 5, 4), m.Stock);
    }

    [Fact]
    public void Retrait_110_solde_110_insuffisant_a_cause_des_frais()
    {
        var (r, m) = Run(110, 0, N(5, 5, 5), null, new Operation.Withdraw(110));
        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, N(0, 0, 0), 0, new AccountState(110, 0)), r);
        Assert.Equal(N(5, 5, 5), m.Stock);
    }

    [Fact]
    public void Retrait_60_deja_150_retires_depasse_le_plafond()
    {
        var (r, m) = Run(250, 150, N(5, 5, 5), null, new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, N(0, 0, 0), 0, new AccountState(250, 150)), r);
        Assert.Equal(N(5, 5, 5), m.Stock);
    }

    [Fact]
    public void Retrait_50_seul_billet_dans_la_cassette_bloquee()
    {
        var (r, m) = Run(250, 0, N(1, 0, 0), Denomination.Fifty, new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, N(0, 0, 0), 0, new AccountState(250, 0)), r);
        Assert.Equal(N(1, 0, 0), m.Stock);
    }

    [Fact]
    public void Retrait_40_contourne_la_cassette_bloquee()
    {
        var (r, m) = Run(250, 0, N(2, 2, 0), Denomination.Fifty, new Operation.Withdraw(40));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, new AccountState(210, 40)), r);
        Assert.Equal(N(2, 0, 0), m.Stock);
    }

    [Fact]
    public void Retrait_5_montant_invalide_avant_tout_autre_controle()
    {
        var (r, m) = Run(0, 200, N(0, 0, 0), null, new Operation.Withdraw(5));
        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, N(0, 0, 0), 0, new AccountState(0, 200)), r);
        Assert.Equal(N(0, 0, 0), m.Stock);
    }

    [Fact]
    public void Depot_dans_une_cassette_pleine()
    {
        var (r, m) = Run(10, 0, N(0, 5, 0), null, new Operation.Deposit(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, N(0, 0, 0), 0, new AccountState(10, 0)), r);
        Assert.Equal(N(0, 5, 0), m.Stock);
    }

    [Fact]
    public void Depot_dans_la_cassette_bloquee()
    {
        var (r, m) = Run(10, 30, N(0, 1, 0), Denomination.Twenty, new Operation.Deposit(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, N(0, 0, 0), 0, new AccountState(10, 30)), r);
        Assert.Equal(N(0, 1, 0), m.Stock);
    }

    [Fact]
    public void Depot_reussi()
    {
        var (r, m) = Run(10, 30, N(1, 1, 0), null, new Operation.Deposit(N(1, 0, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 0, 0), 0, new AccountState(70, 30)), r);
        Assert.Equal(N(2, 1, 1), m.Stock);
    }
}
