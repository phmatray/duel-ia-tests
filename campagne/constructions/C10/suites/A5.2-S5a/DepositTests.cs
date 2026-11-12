namespace Atm.Tests;

public class DepositTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static (AtmResult Result, SpyDispenser Module) Deposit(
        int balance, int withdrawn, NoteCounts stock, NoteCounts notes, Denomination? jammed = null)
    {
        var module = new SpyDispenser(stock, jammed);
        var result = new CashMachine(module).Execute(new AccountState(balance, withdrawn), new Operation.Deposit(notes));
        return (result, module);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult result, AccountState account, SpyDispenser module, NoteCounts stock)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, module.Stock);
        Assert.DoesNotContain(module.LoadCalls, c => c.Result);
    }

    // ---- Contrôle 1 : dépôt vide ----

    [Fact]
    public void Depot_sans_billet_est_invalide()
    {
        var (r, m) = Deposit(100, 30, N(1, 1, 1), N(0, 0, 0));
        AssertRefused(AtmStatus.InvalidAmount, r, new AccountState(100, 30), m, N(1, 1, 1));
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_prime_sur_cassette_bloquee_et_pleine(Denomination jammed)
    {
        var (r, m) = Deposit(100, 30, N(5, 5, 5), N(0, 0, 0), jammed);
        AssertRefused(AtmStatus.InvalidAmount, r, new AccountState(100, 30), m, N(5, 5, 5));
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 1)]
    [InlineData(Denomination.Fifty, 1, 0, 1)]
    public void Depot_touchant_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int d)
    {
        var (r, m) = Deposit(100, 30, N(1, 1, 1), N(f, t, d), jammed);
        AssertRefused(AtmStatus.CassetteJammed, r, new AccountState(100, 30), m, N(1, 1, 1));
    }

    [Fact]
    public void Cassette_bloquee_prime_sur_cassette_pleine()
    {
        // Le 50 ferait déborder sa cassette, mais le 20 touche la cassette bloquée.
        var (r, m) = Deposit(100, 0, N(5, 0, 0), N(1, 1, 0), Denomination.Twenty);
        AssertRefused(AtmStatus.CassetteJammed, r, new AccountState(100, 0), m, N(5, 0, 0));
    }

    [Fact]
    public void Cassette_bloquee_et_pleine_donne_cassette_bloquee()
    {
        var (r, m) = Deposit(100, 0, N(0, 5, 0), N(0, 1, 0), Denomination.Twenty);
        AssertRefused(AtmStatus.CassetteJammed, r, new AccountState(100, 0), m, N(0, 5, 0));
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)] // une seule cassette déborde : tout est refusé
    [InlineData(4, 4, 5, 1, 1, 1)]
    public void Depot_qui_ferait_deborder_une_cassette_est_refuse(int sf, int st, int sd, int f, int t, int d)
    {
        var (r, m) = Deposit(100, 0, N(sf, st, sd), N(f, t, d));
        AssertRefused(AtmStatus.CassetteFull, r, new AccountState(100, 0), m, N(sf, st, sd));
    }

    [Fact]
    public void Remplir_une_cassette_jusqu_a_5_est_permis()
    {
        var (r, m) = Deposit(100, 0, N(4, 4, 4), N(1, 1, 1));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(5, 5, 5), m.Stock);
    }

    [Fact]
    public void Une_cassette_pleine_mais_non_touchee_n_empeche_pas_le_depot()
    {
        var (r, m) = Deposit(100, 0, N(5, 0, 5), N(0, 1, 0));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(5, 1, 5), m.Stock);
    }

    // ---- Le succès ----

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Depot_reussi_credite_la_valeur_et_range_les_billets(int f, int t, int d, int value)
    {
        var (r, m) = Deposit(100, 70, N(2, 2, 2), N(f, t, d));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(100 + value, 70)), r);
        Assert.Equal(N(2 + f, 2 + t, 2 + d), m.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void Depot_hors_de_la_cassette_bloquee_est_permis(Denomination jammed, int f, int t, int d)
    {
        var (r, m) = Deposit(0, 200, N(1, 1, 1), N(f, t, d), jammed);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, d).Value, r.Account.Balance);
        Assert.Equal(200, r.Account.WithdrawnToday);
        Assert.Equal(N(1 + f, 1 + t, 1 + d), m.Stock);
    }

    [Fact]
    public void Depot_dans_un_module_vide()
    {
        var (r, m) = Deposit(250, 0, N(0, 0, 0), N(1, 1, 1));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(330, 0)), r);
        Assert.Equal(N(1, 1, 1), m.Stock);
    }
}
