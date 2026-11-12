namespace Atm.Tests;

public class DepositTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static (AtmResult Result, SpyDispenser Dispenser) Deposit(
        int balance, int withdrawnToday, NoteCounts stock, NoteCounts notes, Denomination? jammed = null, int? failOnCall = null)
    {
        var dispenser = new SpyDispenser(stock, jammed, failOnCall);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawnToday), new Operation.Deposit(notes));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult result, SpyDispenser dispenser,
        AccountState account, NoteCounts stock)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.SuccessfulLoads);
        Assert.Empty(dispenser.SuccessfulDispenses);
    }

    // ---------- Exemples de la spec ----------

    [Fact]
    public void Exemple_cassette_pleine()
    {
        var (r, d) = Deposit(10, 0, N(0, 5, 0), N(0, 1, 0));
        AssertRefused(AtmStatus.CassetteFull, r, d, new AccountState(10, 0), N(0, 5, 0));
    }

    [Fact]
    public void Exemple_cassette_bloquee()
    {
        var (r, d) = Deposit(10, 30, N(0, 1, 0), N(0, 1, 0), Denomination.Twenty);
        AssertRefused(AtmStatus.CassetteJammed, r, d, new AccountState(10, 30), N(0, 1, 0));
    }

    [Fact]
    public void Exemple_depot_reussi()
    {
        var (r, d) = Deposit(10, 30, N(1, 1, 0), N(1, 0, 1));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 1, 1), d.Stock);
        Assert.Equal(new AccountState(70, 30), r.Account);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
    }

    // ---------- Contrôle 1 : dépôt vide ----------

    [Fact]
    public void Depot_vide_est_invalide()
    {
        var (r, d) = Deposit(100, 50, N(2, 2, 2), NoteCounts.None);
        AssertRefused(AtmStatus.InvalidAmount, r, d, new AccountState(100, 50), N(2, 2, 2));
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_est_invalide_avant_cassette_bloquee_ou_pleine(Denomination jammed)
    {
        var (r, d) = Deposit(100, 50, N(5, 5, 5), NoteCounts.None, jammed);
        AssertRefused(AtmStatus.InvalidAmount, r, d, new AccountState(100, 50), N(5, 5, 5));
    }

    // ---------- Contrôle 2 : cassette bloquée ----------

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Ten, 1, 1, 1)]
    public void Billet_de_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int c)
    {
        var stock = N(1, 1, 1);
        var (r, d) = Deposit(100, 0, stock, N(f, t, c), jammed);
        AssertRefused(AtmStatus.CassetteJammed, r, d, new AccountState(100, 0), stock);
    }

    [Fact]
    public void Cassette_bloquee_controlee_avant_cassette_pleine()
    {
        // Les 10 débordent, mais les 50 sont bloqués : c'est le blocage qui l'emporte.
        var stock = N(0, 0, 5);
        var (r, d) = Deposit(100, 0, stock, N(1, 0, 1), Denomination.Fifty);
        AssertRefused(AtmStatus.CassetteJammed, r, d, new AccountState(100, 0), stock);
    }

    [Fact]
    public void Cassette_bloquee_et_pleine_en_meme_temps_donne_CassetteJammed()
    {
        var stock = N(5, 0, 0);
        var (r, d) = Deposit(100, 0, stock, N(1, 0, 0), Denomination.Fifty);
        AssertRefused(AtmStatus.CassetteJammed, r, d, new AccountState(100, 0), stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1, 30)]
    [InlineData(Denomination.Twenty, 1, 0, 1, 60)]
    [InlineData(Denomination.Ten, 1, 1, 0, 70)]
    public void Depot_sans_billet_de_la_cassette_bloquee_est_accepte(Denomination jammed, int f, int t, int c, int value)
    {
        var (r, d) = Deposit(100, 20, N(1, 1, 1), N(f, t, c), jammed);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(100 + value, 20), r.Account);
        Assert.Equal(N(1, 1, 1) + N(f, t, c), d.Stock);
    }

    // ---------- Contrôle 3 : cassette pleine ----------

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(4, 4, 5, 1, 1, 1)]
    public void Depasser_5_billets_dans_une_cassette_est_refuse(int sf, int st, int sc, int f, int t, int c)
    {
        var stock = N(sf, st, sc);
        var (r, d) = Deposit(100, 0, stock, N(f, t, c));
        AssertRefused(AtmStatus.CassetteFull, r, d, new AccountState(100, 0), stock);
    }

    [Fact]
    public void Remplir_une_cassette_jusqu_a_5_est_autorise()
    {
        var (r, d) = Deposit(0, 0, N(4, 4, 4), N(1, 1, 1));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(5, 5, 5), d.Stock);
        Assert.Equal(new AccountState(80, 0), r.Account);
    }

    // ---------- Succès ----------

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 1, 80)]
    public void Depot_reussi_credite_le_solde_de_la_valeur_des_billets(int f, int t, int c, int value)
    {
        var (r, d) = Deposit(250, 200, N(0, 0, 0), N(f, t, c));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(250 + value, 200), r.Account);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(N(f, t, c), d.Stock);
        Assert.Equal(N(f, t, c), d.SuccessfulLoads.Aggregate(NoteCounts.None, (a, n) => a + n));
        Assert.Empty(d.SuccessfulDispenses);
    }

    // ---------- Panne du module ----------

    [Fact]
    public void Panne_du_module_au_depot()
    {
        var stock = N(1, 1, 1);
        var (r, d) = Deposit(100, 30, stock, N(1, 1, 1), failOnCall: 1);
        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(100, 30), r.Account);
        Assert.Equal(stock, d.Stock);
        Assert.Single(d.Calls);
    }
}
