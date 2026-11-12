using static Atm.Tests.Notes;

namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, SpyDispenser Dispenser) Deposit(
        NoteCounts notes, NoteCounts stock, Denomination? jammed = null, int balance = 100, int withdrawn = 40)
    {
        var dispenser = new SpyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Deposit(notes));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult result, SpyDispenser dispenser, NoteCounts stock)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(100, 40), result.Account);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.SuccessfulPasses);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_est_InvalidAmount(Denomination? jammed)
    {
        var stock = N(2, 2, 2);
        var (result, dispenser) = Deposit(NoteCounts.None, stock, jammed);
        AssertRefused(AtmStatus.InvalidAmount, result, dispenser, stock);
    }

    [Fact]
    public void Depot_vide_avec_stock_plein_est_InvalidAmount()
    {
        var stock = N(5, 5, 5);
        var (result, dispenser) = Deposit(NoteCounts.None, stock);
        AssertRefused(AtmStatus.InvalidAmount, result, dispenser, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 1, 0)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Depot_contenant_un_billet_de_la_cassette_bloquee_est_CassetteJammed(
        Denomination jammed, int f, int t, int d)
    {
        var stock = N(1, 1, 1);
        var (result, dispenser) = Deposit(N(f, t, d), stock, jammed);
        AssertRefused(AtmStatus.CassetteJammed, result, dispenser, stock);
    }

    [Fact]
    public void Ordre_CassetteJammed_avant_CassetteFull_meme_cassette()
    {
        var stock = N(0, 5, 0);
        var (result, dispenser) = Deposit(N(0, 1, 0), stock, Denomination.Twenty);
        AssertRefused(AtmStatus.CassetteJammed, result, dispenser, stock);
    }

    [Fact]
    public void Ordre_CassetteJammed_avant_CassetteFull_autre_cassette()
    {
        // La cassette des 50 est pleine, celle des 10 est bloquée.
        var stock = N(5, 0, 0);
        var (result, dispenser) = Deposit(N(1, 0, 1), stock, Denomination.Ten);
        AssertRefused(AtmStatus.CassetteJammed, result, dispenser, stock);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // une seule cassette déborde : tout est refusé
    [InlineData(0, 0, 5, 1, 1, 1)]
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void Depot_qui_ferait_depasser_5_billets_est_CassetteFull(int sf, int st, int sd, int f, int t, int d)
    {
        var stock = N(sf, st, sd);
        var (result, dispenser) = Deposit(N(f, t, d), stock);
        AssertRefused(AtmStatus.CassetteFull, result, dispenser, stock);
    }

    [Fact]
    public void CassetteFull_avec_une_cassette_bloquee_non_concernee()
    {
        var stock = N(0, 5, 0);
        var (result, dispenser) = Deposit(N(1, 1, 0), stock, Denomination.Ten);
        AssertRefused(AtmStatus.CassetteFull, result, dispenser, stock);
    }

    [Theory]
    [InlineData(4, 4, 4, 1, 1, 1, 180)] // remplir jusqu'à 5 exactement
    [InlineData(0, 0, 0, 1, 0, 0, 150)]
    [InlineData(0, 0, 0, 0, 1, 0, 120)]
    [InlineData(0, 0, 0, 0, 0, 1, 110)]
    [InlineData(5, 5, 0, 0, 0, 1, 110)] // les cassettes pleines non concernées n'empêchent rien
    [InlineData(1, 1, 0, 1, 0, 1, 160)]
    public void Depot_reussi(int sf, int st, int sd, int f, int t, int d, int balanceAfter)
    {
        var stock = N(sf, st, sd);
        var notes = N(f, t, d);
        var (result, dispenser) = Deposit(notes, stock);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(balanceAfter, 40), result.Account);
        Assert.Equal(stock + notes, dispenser.Stock);
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void Depot_reussi_si_aucun_billet_de_la_cassette_bloquee(Denomination jammed, int f, int t, int d)
    {
        var stock = N(2, 2, 2);
        var notes = N(f, t, d);
        var (result, dispenser) = Deposit(notes, stock, jammed);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(100 + notes.Value, 40), result.Account);
        Assert.Equal(stock + notes, dispenser.Stock);
    }

    [Fact]
    public void Depot_ne_change_pas_WithdrawnToday_ni_ne_controle_le_plafond()
    {
        var (result, _) = Deposit(N(1, 1, 1), N(0, 0, 0), balance: 0, withdrawn: 200);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(80, 200), result.Account);
    }

    [Fact]
    public void Depot_maximal_sur_solde_maximal()
    {
        var (result, _) = Deposit(N(1, 1, 1), N(0, 0, 0), balance: 250, withdrawn: 0);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(330, 0), result.Account);
    }
}
