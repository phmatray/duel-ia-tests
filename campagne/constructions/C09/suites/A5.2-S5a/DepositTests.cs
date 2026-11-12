using static Atm.Tests.Machine;

namespace Atm.Tests;

public class DepositTests
{
    [Fact]
    public void Depot_vide_est_invalide()
    {
        var (r, d) = Deposit(100, 30, Notes(1, 1, 1), null, NoteCounts.None);
        AssertRefused(AtmStatus.InvalidAmount, r, d, 100, 30, Notes(1, 1, 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_est_invalide_avant_tout_autre_controle(Denomination? jammed)
    {
        var (r, d) = Deposit(0, 0, Full, jammed, NoteCounts.None);
        AssertRefused(AtmStatus.InvalidAmount, r, d, 0, 0, Full);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Billet_de_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int d10)
    {
        var stock = Notes(1, 1, 1);
        var (r, d) = Deposit(50, 20, stock, jammed, Notes(f, t, d10));
        AssertRefused(AtmStatus.CassetteJammed, r, d, 50, 20, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Cassette_bloquee_passe_avant_cassette_pleine(Denomination jammed)
    {
        // Toutes les cassettes sont pleines, le dépôt touche la cassette bloquée.
        var (r, d) = Deposit(50, 20, Full, jammed, Notes(1, 1, 1));
        AssertRefused(AtmStatus.CassetteJammed, r, d, 50, 20, Full);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)] // une seule cassette déborde : tout est refusé
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void Depot_faisant_deborder_une_cassette_est_refuse(int sf, int st, int s10, int f, int t, int d10)
    {
        var stock = Notes(sf, st, s10);
        var (r, d) = Deposit(50, 20, stock, null, Notes(f, t, d10));
        AssertRefused(AtmStatus.CassetteFull, r, d, 50, 20, stock);
    }

    [Fact]
    public void Cassette_pleine_meme_avec_une_autre_cassette_bloquee()
    {
        var stock = Notes(0, 5, 0);
        var (r, d) = Deposit(50, 20, stock, Denomination.Fifty, Notes(0, 1, 1));
        AssertRefused(AtmStatus.CassetteFull, r, d, 50, 20, stock);
    }

    [Theory]
    [InlineData(4, 4, 4, 1, 1, 1)] // remplit exactement à 5
    [InlineData(0, 0, 0, 1, 0, 0)]
    [InlineData(0, 0, 0, 0, 1, 0)]
    [InlineData(0, 0, 0, 0, 0, 1)]
    [InlineData(5, 5, 0, 0, 0, 1)] // les autres cassettes pleines ne gênent pas
    public void Depot_reussi(int sf, int st, int s10, int f, int t, int d10)
    {
        var stock = Notes(sf, st, s10);
        var notes = Notes(f, t, d10);
        var (r, d) = Deposit(100, 40, stock, null, notes);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(100 + notes.Value, 40), r.Account);
        Assert.Equal(stock + notes, d.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void Depot_reussi_avec_une_autre_cassette_bloquee(Denomination jammed, int f, int t, int d10)
    {
        var notes = Notes(f, t, d10);
        var (r, d) = Deposit(0, 200, Notes(2, 2, 2), jammed, notes);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(notes.Value, 200), r.Account);
        Assert.Equal(Notes(2, 2, 2) + notes, d.Stock);
    }

    [Fact]
    public void Le_depot_passe_par_le_module_en_un_seul_chargement()
    {
        var spy = new SpyDispenser(new MoneyDispenser(Notes(1, 1, 1)));
        var r = new CashMachine(spy).Execute(new AccountState(0, 0), new Operation.Deposit(Notes(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Notes(2, 2, 2), spy.Stock);
        Assert.Equal(Notes(1, 1, 1), spy.LoadCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Empty(spy.DispenseCalls);
    }

    [Fact]
    public void Un_depot_refuse_ne_touche_pas_au_module()
    {
        var jammed = new SpyDispenser(new MoneyDispenser(Notes(0, 0, 0), Denomination.Ten));
        var full = new SpyDispenser(new MoneyDispenser(Full));
        var empty = new SpyDispenser(new MoneyDispenser(Notes(0, 0, 0)));

        Assert.Equal(AtmStatus.CassetteJammed,
            new CashMachine(jammed).Execute(new AccountState(0, 0), new Operation.Deposit(Notes(0, 0, 1))).Status);
        Assert.Equal(AtmStatus.CassetteFull,
            new CashMachine(full).Execute(new AccountState(0, 0), new Operation.Deposit(Notes(1, 0, 0))).Status);
        Assert.Equal(AtmStatus.InvalidAmount,
            new CashMachine(empty).Execute(new AccountState(0, 0), new Operation.Deposit(NoteCounts.None)).Status);

        foreach (var spy in new[] { jammed, full, empty })
        {
            Assert.Empty(spy.LoadCalls);
            Assert.Empty(spy.DispenseCalls);
        }
    }
}
