using static Atm.Tests.Helpers;

namespace Atm.Tests;

public class DepositTests
{
    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Depot_accepte_credite_la_valeur_et_range_les_billets(int f, int t, int d, int value)
    {
        var stock = N(2, 3, 4);
        var notes = N(f, t, d);
        var (r, disp) = Run(100, 50, stock, null, new Operation.Deposit(notes));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(100 + value, 50), r.Account);
        Assert.Equal(stock + notes, disp.Stock);
    }

    [Fact]
    public void Depot_vide_est_invalide()
    {
        var (r, d) = Run(100, 50, N(2, 3, 4), null, new Operation.Deposit(NoteCounts.None));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, new AccountState(100, 50)), r);
        Assert.Equal(N(2, 3, 4), d.Stock);
    }

    [Fact]
    public void Depot_vide_passe_avant_la_cassette_bloquee_et_pleine()
    {
        var (r, _) = Run(100, 50, Full, Denomination.Fifty, new Operation.Deposit(NoteCounts.None));

        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(10, 0, 1, 1)]
    public void Billet_de_la_cassette_bloquee_est_refuse(int jammed, int f, int t, int d)
    {
        var stock = N(1, 1, 1);
        var (r, disp) = Run(100, 50, stock, J(jammed), new Operation.Deposit(N(f, t, d)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, new AccountState(100, 50)), r);
        Assert.Equal(stock, disp.Stock);
    }

    [Theory]
    [InlineData(50, 0, 1, 1)]
    [InlineData(20, 1, 0, 1)]
    [InlineData(10, 1, 1, 0)]
    public void Cassette_bloquee_non_touchee_n_empeche_pas_le_depot(int jammed, int f, int t, int d)
    {
        var stock = N(1, 1, 1);
        var notes = N(f, t, d);
        var (r, disp) = Run(100, 50, stock, J(jammed), new Operation.Deposit(notes));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(100 + notes.Value, 50), r.Account);
        Assert.Equal(stock + notes, disp.Stock);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_cassette_pleine()
    {
        // La cassette de 20 est bloquée ET pleine.
        var (r, _) = Run(100, 50, N(0, 5, 0), Denomination.Twenty, new Operation.Deposit(N(0, 1, 0)));

        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_une_autre_cassette_pleine()
    {
        // 50 pleine, 10 bloquée, le dépôt touche les deux.
        var (r, _) = Run(100, 50, N(5, 0, 0), Denomination.Ten, new Operation.Deposit(N(1, 0, 1)));

        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)] // une seule cassette pleine suffit : tout ou rien
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void Depasser_5_billets_dans_une_cassette_est_refuse(int sf, int st, int sd, int f, int t, int d)
    {
        var stock = N(sf, st, sd);
        var (r, disp) = Run(100, 50, stock, null, new Operation.Deposit(N(f, t, d)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, new AccountState(100, 50)), r);
        Assert.Equal(stock, disp.Stock);
    }

    [Fact]
    public void Remplir_une_cassette_jusqu_a_5_est_permis()
    {
        var (r, d) = Run(0, 0, N(4, 4, 4), null, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(80, 0), r.Account);
        Assert.Equal(Full, d.Stock);
    }

    [Fact]
    public void Le_depot_ne_change_pas_le_retire_du_jour()
    {
        var (r, _) = Run(250, 200, N(0, 0, 0), null, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(200, r.Account.WithdrawnToday);
        Assert.Equal(330, r.Account.Balance);
    }

    [Fact]
    public void Depot_accepte_passe_par_le_module_en_un_seul_chargement()
    {
        var spy = new SpyDispenser(new MoneyDispenser(N(1, 1, 1)));
        var r = new CashMachine(spy).Execute(new AccountState(0, 0), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 1, 1), spy.Loaded.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Empty(spy.Dispensed);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]  // InvalidAmount
    [InlineData(1, 0, 0, 50)] // CassetteJammed
    [InlineData(0, 1, 0, 0)]  // CassetteFull (20 pleine)
    public void Un_refus_de_depot_ne_touche_pas_au_module(int f, int t, int d, int jammed)
    {
        var stock = N(1, 5, 1);
        var spy = new SpyDispenser(new MoneyDispenser(stock, J(jammed)));
        var r = new CashMachine(spy).Execute(new AccountState(100, 50), new Operation.Deposit(N(f, t, d)));

        Assert.NotEqual(AtmStatus.Ok, r.Status);
        Assert.Equal(new AtmResult(r.Status, NoteCounts.None, 0, new AccountState(100, 50)), r);
        Assert.Empty(spy.Loaded);
        Assert.Empty(spy.Dispensed);
        Assert.Equal(stock, spy.Stock);
    }
}
