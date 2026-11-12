using static Atm.Tests.Fixture;

namespace Atm.Tests;

public class DepositTests
{
    private static void AssertRefused(AtmStatus expected, AccountState account, AtmResult result, SpyDispenser spy,
        NoteCounts stockBefore)
    {
        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
        Assert.Empty(spy.LoadCalls);
        Assert.Empty(spy.DispenseCalls);
        Assert.Equal(stockBefore, spy.Stock);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(20)]
    [InlineData(10)]
    public void Depot_vide_est_invalide(int jammed)
    {
        var (machine, spy) = Machine(Full, Jam(jammed));
        var account = new AccountState(100, 30);

        var result = machine.Execute(account, new Operation.Deposit(NoteCounts.None));

        AssertRefused(AtmStatus.InvalidAmount, account, result, spy, Full);
    }

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(10, 0, 1, 1)]
    public void Depot_touchant_la_cassette_bloquee_est_refuse(int jammed, int f, int t, int te)
    {
        var stock = Notes(1, 1, 1);
        var (machine, spy) = Machine(stock, Jam(jammed));
        var account = new AccountState(100, 30);

        var result = machine.Execute(account, new Operation.Deposit(Notes(f, t, te)));

        AssertRefused(AtmStatus.CassetteJammed, account, result, spy, stock);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_cassette_pleine()
    {
        // La cassette de 10 est pleine ET le dépôt touche la cassette de 20, bloquée.
        var stock = Notes(0, 0, 5);
        var (machine, spy) = Machine(stock, Denomination.Twenty);
        var account = new AccountState(100, 30);

        var result = machine.Execute(account, new Operation.Deposit(Notes(0, 1, 1)));

        AssertRefused(AtmStatus.CassetteJammed, account, result, spy, stock);
    }

    [Fact]
    public void Cassette_bloquee_et_pleine_donne_CassetteJammed()
    {
        var stock = Notes(5, 0, 0);
        var (machine, spy) = Machine(stock, Denomination.Fifty);
        var account = new AccountState(100, 30);

        var result = machine.Execute(account, new Operation.Deposit(Notes(1, 0, 0)));

        AssertRefused(AtmStatus.CassetteJammed, account, result, spy, stock);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)] // une seule cassette déborde : tout est refusé
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void Depot_faisant_deborder_une_cassette_est_refuse(int f, int t, int te, int df, int dt, int dte)
    {
        var stock = Notes(f, t, te);
        var (machine, spy) = Machine(stock);
        var account = new AccountState(100, 30);

        var result = machine.Execute(account, new Operation.Deposit(Notes(df, dt, dte)));

        AssertRefused(AtmStatus.CassetteFull, account, result, spy, stock);
    }

    [Fact]
    public void Cassette_pleine_non_concernee_par_le_depot_ne_bloque_pas()
    {
        var (machine, spy) = Machine(Notes(5, 0, 5));

        var result = machine.Execute(new(0, 0), new Operation.Deposit(Notes(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(20, 0)), result);
        Assert.Equal(Notes(5, 1, 5), spy.Stock);
    }

    [Fact]
    public void Remplir_une_cassette_jusqu_a_5_est_permis()
    {
        var (machine, spy) = Machine(Notes(4, 4, 4));

        var result = machine.Execute(new(0, 0), new Operation.Deposit(Notes(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(80, 0)), result);
        Assert.Equal(Full, spy.Stock);
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(20, 1, 0, 1)]  // la cassette bloquée n'est pas touchée
    [InlineData(50, 0, 1, 1)]
    [InlineData(10, 1, 1, 0)]
    public void Depot_reussi_credite_le_solde_et_remplit_le_module(int jammed, int f, int t, int te)
    {
        var stock = Notes(2, 2, 2);
        var deposit = Notes(f, t, te);
        var (machine, spy) = Machine(stock, Jam(jammed));

        var result = machine.Execute(new(100, 120), new Operation.Deposit(deposit));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(100 + deposit.Value, 120)), result);
        Assert.Equal(stock + deposit, spy.Stock);
        Assert.Equal([(deposit, true)], spy.LoadCalls);
        Assert.Empty(spy.DispenseCalls);
    }

    [Fact]
    public void Depot_ignore_le_plafond_et_le_solde()
    {
        var (machine, _) = Machine(Notes(0, 0, 0));

        var result = machine.Execute(new(250, 200), new Operation.Deposit(Notes(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(330, 200)), result);
    }
}
