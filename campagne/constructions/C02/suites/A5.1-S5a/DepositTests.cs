using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    // ---------- Contrôle 1 : dépôt vide ----------

    [Fact]
    public void Depot_sans_billet_est_invalide()
    {
        var (machine, spy) = Spied(Notes(1, 1, 1));

        var result = machine.Execute(Account(100, 50), Deposit(NoteCounts.None));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Account(100, 50)), result);
        Assert.Empty(spy.Loaded);
        Assert.Equal(Notes(1, 1, 1), spy.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_passe_avant_cassette_bloquee(Denomination jammed)
    {
        var (machine, _) = Real(Notes(5, 5, 5), jammed);

        var result = machine.Execute(Account(100, 0), Deposit(NoteCounts.None));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    // ---------- Contrôle 2 : cassette bloquée ----------

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 1, 0)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Depot_touchant_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int n)
    {
        var stock = Notes(2, 2, 2);
        var (machine, spy) = Spied(stock, jammed);

        var result = machine.Execute(Account(100, 30), Deposit(Notes(f, t, n)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, Account(100, 30)), result);
        Assert.Empty(spy.Loaded);
        Assert.Equal(stock, spy.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    public void Cassette_bloquee_passe_avant_cassette_pleine(Denomination jammed, int f, int t, int n)
    {
        var (machine, _) = Real(Notes(5, 5, 5), jammed);

        var result = machine.Execute(Account(100, 0), Deposit(Notes(f, t, n)));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_une_autre_cassette_pleine()
    {
        // Le 50 ferait déborder la cassette de 50, mais le 10 vise la cassette bloquée.
        var (machine, _) = Real(Notes(5, 0, 0), Denomination.Ten);

        var result = machine.Execute(Account(100, 0), Deposit(Notes(1, 0, 1)));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1, 30)]
    [InlineData(Denomination.Twenty, 1, 0, 1, 60)]
    [InlineData(Denomination.Ten, 1, 1, 0, 70)]
    public void Depot_hors_cassette_bloquee_est_accepte(Denomination jammed, int f, int t, int n, int value)
    {
        var (machine, dispenser) = Real(Notes(2, 2, 2), jammed);

        var result = machine.Execute(Account(100, 30), Deposit(Notes(f, t, n)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(100 + value, 30)), result);
        Assert.Equal(Notes(2 + f, 2 + t, 2 + n), dispenser.Stock);
    }

    // ---------- Contrôle 3 : cassette pleine ----------

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)]  // une seule cassette pleine suffit, tout est refusé
    [InlineData(0, 4, 5, 0, 1, 1)]
    public void Depot_qui_ferait_deborder_une_cassette_est_refuse(int sf, int st, int sn, int f, int t, int n)
    {
        var stock = Notes(sf, st, sn);
        var (machine, spy) = Spied(stock);

        var result = machine.Execute(Account(100, 30), Deposit(Notes(f, t, n)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, Account(100, 30)), result);
        Assert.Empty(spy.Loaded);
        Assert.Equal(stock, spy.Stock);
    }

    [Fact]
    public void Remplir_une_cassette_jusqu_a_5_est_accepte()
    {
        var (machine, dispenser) = Real(Notes(4, 4, 4));

        var result = machine.Execute(Account(0, 0), Deposit(Notes(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(80, 0)), result);
        Assert.Equal(Notes(5, 5, 5), dispenser.Stock);
    }

    // ---------- Le succès ----------

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Depot_reussi_credite_la_valeur_et_range_les_billets(int f, int t, int n, int value)
    {
        var (machine, spy) = Spied(Notes(0, 0, 0));

        var result = machine.Execute(Account(15, 120), Deposit(Notes(f, t, n)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(15 + value, 120), result.Account);
        Assert.Equal(Notes(f, t, n), spy.Stock);
        Assert.Empty(spy.Dispensed);
    }

    [Fact]
    public void Depot_ne_touche_pas_au_retrait_du_jour_meme_au_plafond()
    {
        var (machine, _) = Real(Notes(0, 0, 0));

        var result = machine.Execute(Account(250, 200), Deposit(Notes(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(330, 200)), result);
    }
}
