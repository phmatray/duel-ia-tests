namespace Atm.Tests;

using static T;

public class DepositTests
{
    // --- Contrôle 1 : dépôt vide -------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Ten)]
    public void Depot_sans_billet_est_invalide(Denomination? jammed)
    {
        var stock = N(5, 5, 5);
        var (machine, spy) = Spied(stock, jammed);

        var result = machine.Execute(Acc(10, 30), D(0, 0, 0));

        AssertRefused(result, AtmStatus.InvalidAmount, Acc(10, 30));
        Assert.Equal(stock, spy.Stock);
        Assert.Equal(0, spy.Calls);
    }

    // --- Contrôle 2 : cassette bloquée -------------------------------------------------------

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 1)]   // un seul billet de la cassette bloquée suffit
    [InlineData(Denomination.Fifty, 1, 0, 1)]
    public void Depot_touchant_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int d)
    {
        var stock = N(1, 1, 1);
        var (machine, spy) = Spied(stock, jammed);

        var result = machine.Execute(Acc(10, 30), D(f, t, d));

        AssertRefused(result, AtmStatus.CassetteJammed, Acc(10, 30));
        Assert.Equal(stock, spy.Stock);
        Assert.Equal(0, spy.Calls);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 1)]
    public void Cassette_bloquee_passe_avant_cassette_pleine(Denomination jammed, int f, int t, int d)
    {
        var stock = N(5, 5, 5);
        var (machine, spy) = Spied(stock, jammed);

        var result = machine.Execute(Acc(10, 30), D(f, t, d));

        AssertRefused(result, AtmStatus.CassetteJammed, Acc(10, 30));
        Assert.Equal(stock, spy.Stock);
    }

    // --- Contrôle 3 : cassette pleine --------------------------------------------------------

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 5, 5, 1, 1, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)]   // tout ou rien : 50 et 20 auraient tenu
    [InlineData(4, 4, 5, 1, 1, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)]
    public void Depot_faisant_depasser_5_billets_dans_une_cassette_est_refuse(
        int s50, int s20, int s10, int f, int t, int d)
    {
        var stock = N(s50, s20, s10);
        var (machine, spy) = Spied(stock);

        var result = machine.Execute(Acc(10, 30), D(f, t, d));

        AssertRefused(result, AtmStatus.CassetteFull, Acc(10, 30));
        Assert.Equal(stock, spy.Stock);
        Assert.Equal(0, spy.Calls);
    }

    [Fact]
    public void Cassette_de_10_pleine_avec_une_autre_cassette_bloquee()
    {
        var stock = N(0, 0, 5);
        var (machine, spy) = Spied(stock, Denomination.Fifty);

        var result = machine.Execute(Acc(10, 30), D(0, 1, 1));

        AssertRefused(result, AtmStatus.CassetteFull, Acc(10, 30));
        Assert.Equal(stock, spy.Stock);
    }

    // --- Succès ------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0, null, 1, 0, 0)]
    [InlineData(0, 0, 0, null, 0, 1, 0)]
    [InlineData(0, 0, 0, null, 0, 0, 1)]
    [InlineData(0, 0, 0, null, 1, 1, 1)]
    [InlineData(4, 4, 4, null, 1, 1, 1)]   // remplir jusqu'à 5 exactement est permis
    [InlineData(5, 5, 4, null, 0, 0, 1)]
    [InlineData(5, 2, 2, Denomination.Fifty, 0, 1, 1)]  // la cassette bloquée n'est pas touchée
    [InlineData(2, 5, 2, Denomination.Twenty, 1, 0, 1)]
    [InlineData(2, 2, 5, Denomination.Ten, 1, 1, 0)]
    public void Depot_accepte_credite_le_solde_et_range_les_billets(
        int s50, int s20, int s10, Denomination? jammed, int f, int t, int d)
    {
        var stock = N(s50, s20, s10);
        var deposit = N(f, t, d);
        var (machine, spy) = Spied(stock, jammed);

        var result = machine.Execute(Acc(10, 30), new Operation.Deposit(deposit));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(10 + deposit.Value, 30)), result);
        Assert.Equal(stock + deposit, spy.Stock);
        Assert.Empty(spy.Dispenses);
        Assert.All(spy.Loads, l => Assert.True(l.Ok));
        Assert.Equal(deposit, spy.Loads.Aggregate(NoteCounts.None, (acc, l) => acc + l.Notes));
    }

    [Fact]
    public void Depot_ne_change_pas_le_retire_du_jour_meme_au_plafond()
    {
        var (machine, _) = Real(N(0, 0, 0));

        var result = machine.Execute(Acc(170, 200), D(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(250, 200)), result);
    }
}
