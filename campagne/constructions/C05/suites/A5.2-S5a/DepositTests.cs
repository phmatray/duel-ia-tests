using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    // ---- Contrôle 1 : dépôt vide -------------------------------------------------------------

    [Fact]
    public void Depot_sans_billet_est_invalide()
    {
        var (machine, spy) = Spied(Notes(1, 1, 1));

        var result = machine.Execute(Account(10, 30), Deposit(0, 0, 0));

        AssertRefused(result, AtmStatus.InvalidAmount, Account(10, 30));
        Assert.Equal(0, spy.Calls);
        Assert.Equal(Notes(1, 1, 1), spy.Stock);
    }

    [Fact]
    public void Depot_vide_passe_avant_la_cassette_bloquee_et_pleine()
    {
        var (machine, _) = Real(Notes(5, 5, 5), Denomination.Ten);

        var result = machine.Execute(Account(10, 30), Deposit(0, 0, 0));

        AssertRefused(result, AtmStatus.InvalidAmount, Account(10, 30));
    }

    // ---- Contrôle 2 : cassette bloquée -------------------------------------------------------

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Ten, 1, 0, 1)]
    public void Depot_touchant_la_cassette_bloquee_est_refuse(Denomination jammed, int fifties, int twenties, int tens)
    {
        var stock = Notes(2, 2, 2);
        var (machine, spy) = Spied(stock, jammed);

        var result = machine.Execute(Account(10, 30), Deposit(fifties, twenties, tens));

        AssertRefused(result, AtmStatus.CassetteJammed, Account(10, 30));
        Assert.Empty(spy.Loaded);
        Assert.Equal(stock, spy.Stock);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_cassette_pleine()
    {
        var (machine, _) = Real(Notes(0, 5, 0), Denomination.Twenty);

        var result = machine.Execute(Account(10, 0), Deposit(0, 1, 0));

        AssertRefused(result, AtmStatus.CassetteJammed, Account(10, 0));
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_une_autre_cassette_pleine()
    {
        var (machine, _) = Real(Notes(5, 0, 0), Denomination.Ten);

        var result = machine.Execute(Account(10, 0), Deposit(1, 0, 1));

        AssertRefused(result, AtmStatus.CassetteJammed, Account(10, 0));
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1, 30)]
    [InlineData(Denomination.Twenty, 1, 0, 1, 60)]
    [InlineData(Denomination.Ten, 1, 1, 0, 70)]
    public void Une_cassette_bloquee_n_empeche_pas_de_deposer_dans_les_autres(
        Denomination jammed, int fifties, int twenties, int tens, int value)
    {
        var stock = Notes(2, 2, 2);
        var (machine, dispenser) = Real(stock, jammed);

        var result = machine.Execute(Account(10, 30), Deposit(fifties, twenties, tens));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Empty, 0, Account(10 + value, 30)), result);
        Assert.Equal(stock + Notes(fifties, twenties, tens), dispenser.Stock);
    }

    // ---- Contrôle 3 : cassette pleine --------------------------------------------------------

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // une seule cassette pleine suffit, tout est refusé
    [InlineData(0, 0, 5, 1, 0, 1)]
    public void Depot_faisant_depasser_5_billets_dans_une_cassette_est_refuse(
        int fifties, int twenties, int tens, int dFifties, int dTwenties, int dTens)
    {
        var stock = Notes(fifties, twenties, tens);
        var (machine, spy) = Spied(stock);

        var result = machine.Execute(Account(10, 0), Deposit(dFifties, dTwenties, dTens));

        AssertRefused(result, AtmStatus.CassetteFull, Account(10, 0));
        Assert.Empty(spy.Loaded);
        Assert.Equal(stock, spy.Stock);
    }

    [Fact]
    public void Remplir_une_cassette_jusqu_a_5_est_permis()
    {
        var (machine, dispenser) = Real(Notes(4, 4, 4));

        var result = machine.Execute(Account(0, 0), Deposit(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Empty, 0, Account(80, 0)), result);
        Assert.Equal(Full, dispenser.Stock);
    }

    // ---- Succès ------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Depot_reussi_credite_la_valeur_sans_toucher_au_retire_du_jour(int fifties, int twenties, int tens, int value)
    {
        var deposit = Notes(fifties, twenties, tens);
        var (machine, spy) = Spied(Empty);

        var result = machine.Execute(Account(165, 120), new Operation.Deposit(deposit));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Empty, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(165 + value, 120), result.Account);
        Assert.Equal(deposit, spy.Stock);
        Assert.Equal([deposit], spy.Loaded);
        Assert.Empty(spy.Dispensed);
    }

    [Fact]
    public void Depot_avec_un_plafond_du_jour_atteint_et_un_solde_nul_reussit()
    {
        var (machine, _) = Real(Empty);

        var result = machine.Execute(Account(0, 200), Deposit(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Empty, 0, Account(80, 200)), result);
    }
}
