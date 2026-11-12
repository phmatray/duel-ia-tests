namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, RecordingDispenser Dispenser) Run(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, NoteCounts deposit)
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawnToday), new Operation.Deposit(deposit));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmResult result, RecordingDispenser dispenser, AtmStatus status,
        AccountState account, NoteCounts stock)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.DispenseCalls);
    }

    // ---- Règle 1 : dépôt vide ----

    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Ten)]
    public void Depot_sans_billet_est_InvalidAmount(Denomination? jammed)
    {
        var stock = new NoteCounts(5, 5, 5); // même cassettes pleines : le dépôt vide est refusé en premier
        var (result, dispenser) = Run(10, 30, stock, jammed, NoteCounts.None);
        AssertRefused(result, dispenser, AtmStatus.InvalidAmount, new(10, 30), stock);
    }

    // ---- Règle 2 : cassette bloquée ----

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Twenty, 1, 1, 1)]
    [InlineData(Denomination.Ten, 1, 0, 1)]
    public void Depot_touchant_la_cassette_bloquee_est_CassetteJammed(Denomination jammed, int f, int t, int d)
    {
        var stock = new NoteCounts(1, 1, 1);
        var (result, dispenser) = Run(10, 30, stock, jammed, new NoteCounts(f, t, d));
        AssertRefused(result, dispenser, AtmStatus.CassetteJammed, new(10, 30), stock);
    }

    [Fact]
    public void CassetteJammed_passe_avant_CassetteFull()
    {
        // La cassette des 20 est bloquée et pleine.
        var stock = new NoteCounts(0, 5, 0);
        var (result, dispenser) = Run(10, 0, stock, Denomination.Twenty, new NoteCounts(0, 1, 0));
        AssertRefused(result, dispenser, AtmStatus.CassetteJammed, new(10, 0), stock);
    }

    [Fact]
    public void CassetteJammed_meme_si_une_autre_cassette_deborderait()
    {
        var stock = new NoteCounts(5, 0, 0);
        var (result, dispenser) = Run(10, 0, stock, Denomination.Ten, new NoteCounts(1, 0, 1));
        AssertRefused(result, dispenser, AtmStatus.CassetteJammed, new(10, 0), stock);
    }

    // ---- Règle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(4, 4, 5, 1, 1, 1)] // une seule cassette déborde : tout est refusé
    public void Depot_faisant_depasser_5_billets_est_CassetteFull(int sf, int st, int sd, int f, int t, int d)
    {
        var stock = new NoteCounts(sf, st, sd);
        var (result, dispenser) = Run(10, 0, stock, null, new NoteCounts(f, t, d));
        AssertRefused(result, dispenser, AtmStatus.CassetteFull, new(10, 0), stock);
    }

    [Fact]
    public void Une_cassette_bloquee_pleine_n_empeche_pas_un_depot_dans_les_autres()
    {
        var stock = new NoteCounts(5, 4, 0);
        var (result, dispenser) = Run(0, 0, stock, Denomination.Fifty, new NoteCounts(0, 1, 1));
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(5, 5, 1), dispenser.Stock);
        Assert.Equal(30, result.Account.Balance);
    }

    // ---- Succès ----

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 1, 80)]
    [InlineData(1, 1, 0, 70)]
    public void Depot_reussi_credite_le_solde_et_remplit_les_cassettes(int f, int t, int d, int value)
    {
        var stock = new NoteCounts(4, 4, 4);
        var deposit = new NoteCounts(f, t, d);
        var (result, dispenser) = Run(100, 120, stock, null, deposit);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(100 + value, 120), result.Account);
        Assert.Equal(stock + deposit, dispenser.Stock);
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Fact]
    public void Depot_reussi_ne_touche_pas_au_plafond_du_jour()
    {
        var (result, _) = Run(250, 200, NoteCounts.None, null, new NoteCounts(1, 1, 1));
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(330, 200), result.Account);
    }
}
