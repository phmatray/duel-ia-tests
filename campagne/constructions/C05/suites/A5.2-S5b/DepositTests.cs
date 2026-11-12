using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(20)]
    [InlineData(10)]
    public void Depot_vide_est_invalide_meme_cassette_bloquee(int jammed)
    {
        var (machine, spy) = Machine(Full, Jam(jammed));
        var r = machine.Execute(Acc(100, 30), new Operation.Deposit(N()));
        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, N(), 0, Acc(100, 30)), r);
        Assert.Equal(Full, spy.Stock);
    }

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(20, 1, 1, 0)]
    [InlineData(10, 0, 1, 1)]
    public void Depot_touchant_la_cassette_bloquee_est_refuse(int jammed, int f, int t, int d)
    {
        var stock = N(1, 1, 1);
        var (machine, spy) = Machine(stock, Jam(jammed));
        var r = machine.Execute(Acc(100, 30), new Operation.Deposit(N(f, t, d)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, N(), 0, Acc(100, 30)), r);
        Assert.Equal(stock, spy.Stock);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_cassette_pleine()
    {
        // La cassette de 20 est pleine et bloquée.
        var r = Deposit(Acc(100, 0), N(0, 1, 0), N(0, 5, 0), Denomination.Twenty);
        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_une_autre_cassette_pleine()
    {
        // La 10 est bloquée et touchée, la 50 serait pleine.
        var r = Deposit(Acc(100, 0), N(1, 0, 1), N(5, 0, 0), Denomination.Ten);
        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)]   // une seule cassette déborde : tout est refusé
    [InlineData(0, 0, 5, 1, 1, 1)]
    public void Depot_faisant_deborder_une_cassette_est_refuse(int sf, int st, int sd, int f, int t, int d)
    {
        var (machine, spy) = Machine(N(sf, st, sd));
        var r = machine.Execute(Acc(100, 30), new Operation.Deposit(N(f, t, d)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, N(), 0, Acc(100, 30)), r);
        Assert.Equal(N(sf, st, sd), spy.Stock);
    }

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 1, 80)]
    [InlineData(0, 1, 1, 30)]
    public void Depot_reussi_credite_la_valeur_des_billets(int f, int t, int d, int value)
    {
        var stock = N(4, 4, 4);
        var (machine, spy) = Machine(stock);
        var r = machine.Execute(Acc(100, 70), new Operation.Deposit(N(f, t, d)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(), 0, Acc(100 + value, 70)), r);
        Assert.Equal(stock + N(f, t, d), spy.Stock);
        Assert.Equal(N(f, t, d), spy.Loads.Where(l => l.Result).Aggregate(N(), (a, l) => a + l.Notes));
        Assert.Empty(spy.Dispenses);
    }

    [Fact]
    public void Remplir_une_cassette_jusqu_a_5_est_permis()
    {
        var (machine, spy) = Machine(N(4, 4, 4));
        var r = machine.Execute(Acc(0, 0), new Operation.Deposit(N(1, 1, 1)));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Full, spy.Stock);
    }

    [Fact]
    public void Depot_accepte_quand_une_autre_cassette_est_bloquee()
    {
        var (machine, spy) = Machine(N(0, 0, 0), Denomination.Fifty);
        var r = machine.Execute(Acc(0, 200), new Operation.Deposit(N(0, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(), 0, Acc(30, 200)), r);
        Assert.Equal(N(0, 1, 1), spy.Stock);
    }

    [Fact]
    public void Depot_accepte_meme_si_une_autre_cassette_est_pleine()
    {
        var (machine, spy) = Machine(N(5, 0, 5));
        var r = machine.Execute(Acc(10, 0), new Operation.Deposit(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(), 0, Acc(30, 0)), r);
        Assert.Equal(N(5, 1, 5), spy.Stock);
    }
}
