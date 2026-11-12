using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    [Fact]
    public void Depot_vide_est_invalide()
    {
        var (machine, spy) = Spied(N(1, 1, 1));

        var result = machine.Execute(Acc(10, 30), Deposit(NoteCounts.None));

        AssertRefused(result, AtmStatus.InvalidAmount, Acc(10, 30));
        Assert.Equal(N(1, 1, 1), spy.Stock);
        Assert.Empty(spy.Loaded);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(20)]
    [InlineData(10)]
    public void Depot_vide_est_invalide_avant_la_cassette_bloquee(int jam)
    {
        var (machine, _) = Real(N(5, 5, 5), Jam(jam));

        var result = machine.Execute(Acc(10, 0), Deposit(NoteCounts.None));

        AssertRefused(result, AtmStatus.InvalidAmount, Acc(10, 0));
    }

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(20, 1, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(10, 0, 1, 1)]
    public void Depot_avec_un_billet_de_la_cassette_bloquee_est_refuse(int jam, int fifties, int twenties, int tens)
    {
        var stock = N(2, 2, 2);
        var (machine, spy) = Spied(stock, Jam(jam));

        var result = machine.Execute(Acc(10, 30), Deposit(N(fifties, twenties, tens)));

        AssertRefused(result, AtmStatus.CassetteJammed, Acc(10, 30));
        Assert.Equal(stock, spy.Stock);
        Assert.Empty(spy.Loaded);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_cassette_pleine()
    {
        // La cassette de 20 est à la fois pleine et bloquée.
        var (machine, _) = Real(N(0, 5, 0), Denomination.Twenty);

        var result = machine.Execute(Acc(10, 0), Deposit(N(0, 1, 0)));

        AssertRefused(result, AtmStatus.CassetteJammed, Acc(10, 0));
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_une_autre_cassette_pleine()
    {
        // Le 10 vise la cassette bloquée, le 50 une cassette pleine.
        var (machine, _) = Real(N(5, 0, 0), Denomination.Ten);

        var result = machine.Execute(Acc(10, 0), Deposit(N(1, 0, 1)));

        AssertRefused(result, AtmStatus.CassetteJammed, Acc(10, 0));
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // une seule cassette déborde suffit
    [InlineData(0, 0, 5, 1, 1, 1)]
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void Depot_qui_ferait_depasser_5_billets_est_refuse(
        int stockFifties, int stockTwenties, int stockTens, int fifties, int twenties, int tens)
    {
        var stock = N(stockFifties, stockTwenties, stockTens);
        var (machine, spy) = Spied(stock);

        var result = machine.Execute(Acc(10, 30), Deposit(N(fifties, twenties, tens)));

        AssertRefused(result, AtmStatus.CassetteFull, Acc(10, 30));
        Assert.Equal(stock, spy.Stock);
        Assert.Empty(spy.Loaded);
    }

    [Fact]
    public void Cassette_pleine_ignoree_si_on_n_y_depose_rien()
    {
        var (machine, dispenser) = Real(N(5, 4, 5));

        var result = machine.Execute(Acc(0, 0), Deposit(N(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(20, 0)), result);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Cassette_bloquee_ignoree_si_on_n_y_depose_rien()
    {
        var (machine, dispenser) = Real(N(0, 0, 0), Denomination.Twenty);

        var result = machine.Execute(Acc(0, 0), Deposit(N(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(60, 0)), result);
        Assert.Equal(N(1, 0, 1), dispenser.Stock);
    }

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Depot_accepte_credite_sa_valeur_et_range_les_billets(int fifties, int twenties, int tens, int value)
    {
        var stock = N(4, 4, 4);
        var notes = N(fifties, twenties, tens);
        var (machine, spy) = Spied(stock);

        var result = machine.Execute(Acc(100, 120), Deposit(notes));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(100 + value, 120), result.Account);
        Assert.Equal(stock + notes, spy.Stock);
        Assert.Equal(notes, spy.Loaded.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Empty(spy.Dispensed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public void Depot_ne_change_pas_le_retire_du_jour(int withdrawn)
    {
        var (machine, _) = Real(N(0, 0, 0));

        var result = machine.Execute(Acc(250, withdrawn), Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(withdrawn, result.Account.WithdrawnToday);
        Assert.Equal(330, result.Account.Balance);
    }

    [Fact]
    public void Depot_remplit_une_cassette_jusqu_a_5_exactement()
    {
        var (machine, dispenser) = Real(N(4, 4, 4));

        var result = machine.Execute(Acc(0, 0), Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }
}
