using static Atm.Tests.TestKit;

namespace Atm.Tests;

public class DepositTests
{
    // ---------- Contrôle 1 : dépôt vide ----------

    [Theory]
    [InlineData(5, 5, 5, 0)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(5, 5, 5, 20)] // bloquée et pleine : le dépôt vide parle d'abord
    [InlineData(5, 5, 5, 50)]
    public void Empty_deposit_is_InvalidAmount(int s50, int s20, int s10, int jammed)
    {
        var stock = N(s50, s20, s10);
        var (result, dispenser) = Deposit(100, 30, stock, Jam(jammed), NoteCounts.None);

        AssertRefused(AtmStatus.InvalidAmount, result, Acc(100, 30), dispenser, stock);
    }

    // ---------- Contrôle 2 : cassette bloquée ----------

    [Theory]
    // stock, bloquée, dépôt
    [InlineData(0, 0, 0, 50, 1, 0, 0)]
    [InlineData(0, 0, 0, 20, 0, 1, 0)]
    [InlineData(0, 0, 0, 10, 0, 0, 1)]
    [InlineData(0, 0, 0, 10, 1, 1, 1)] // un seul billet de la cassette bloquée suffit
    [InlineData(5, 5, 5, 20, 1, 1, 1)] // et passe avant CassetteFull
    [InlineData(0, 5, 0, 20, 0, 1, 0)] // bloquée et pleine : CassetteJammed d'abord
    [InlineData(5, 0, 0, 10, 1, 0, 1)] // 50 plein, 10 bloquée : CassetteJammed d'abord
    public void Deposit_with_a_note_of_the_jammed_cassette_is_CassetteJammed(
        int s50, int s20, int s10, int jammed, int d50, int d20, int d10)
    {
        var stock = N(s50, s20, s10);
        var (result, dispenser) = Deposit(100, 30, stock, Jam(jammed), N(d50, d20, d10));

        AssertRefused(AtmStatus.CassetteJammed, result, Acc(100, 30), dispenser, stock);
    }

    // ---------- Contrôle 3 : cassette pleine ----------

    [Theory]
    // stock, bloquée, dépôt
    [InlineData(5, 0, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 0, 1)]
    [InlineData(5, 0, 0, 0, 1, 1, 1)]  // une seule cassette qui déborde suffit : tout ou rien
    [InlineData(0, 0, 5, 0, 1, 1, 1)]
    [InlineData(4, 4, 5, 0, 1, 1, 1)]
    [InlineData(5, 5, 5, 0, 1, 1, 1)]
    [InlineData(5, 0, 0, 20, 1, 0, 0)] // cassette bloquée non concernée
    [InlineData(0, 0, 5, 50, 0, 1, 1)]
    public void Deposit_overflowing_a_cassette_is_CassetteFull(
        int s50, int s20, int s10, int jammed, int d50, int d20, int d10)
    {
        var stock = N(s50, s20, s10);
        var (result, dispenser) = Deposit(100, 30, stock, Jam(jammed), N(d50, d20, d10));

        AssertRefused(AtmStatus.CassetteFull, result, Acc(100, 30), dispenser, stock);
    }

    // ---------- Le succès ----------

    [Theory]
    // stock, bloquée, dépôt
    [InlineData(0, 0, 0, 0, 1, 0, 0)]
    [InlineData(0, 0, 0, 0, 0, 1, 0)]
    [InlineData(0, 0, 0, 0, 0, 0, 1)]
    [InlineData(0, 0, 0, 0, 1, 1, 1)]
    [InlineData(4, 4, 4, 0, 1, 1, 1)]  // remplir pile à 5
    [InlineData(4, 5, 0, 0, 1, 0, 1)]  // cassette pleine, mais non touchée
    [InlineData(1, 1, 0, 0, 1, 0, 1)]
    [InlineData(2, 2, 2, 20, 1, 0, 1)] // cassette bloquée non touchée
    [InlineData(2, 2, 2, 50, 0, 1, 1)]
    [InlineData(2, 2, 2, 10, 1, 1, 0)]
    public void Successful_deposit_loads_the_notes_and_credits_their_value(
        int s50, int s20, int s10, int jammed, int d50, int d20, int d10)
    {
        var stock = N(s50, s20, s10);
        var notes = N(d50, d20, d10);
        var (result, dispenser) = Deposit(100, 30, stock, Jam(jammed), notes);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(100 + notes.Value, 30), result.Account);
        Assert.Equal(stock + notes, dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 200)]
    [InlineData(250, 200)]
    [InlineData(170, 90)]
    public void Deposit_ignores_balance_and_daily_limit_and_never_changes_withdrawn_today(int balance, int withdrawn)
    {
        var (result, dispenser) = Deposit(balance, withdrawn, N(0, 0, 0), null, N(1, 1, 1));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(balance + 80, withdrawn), result.Account);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Deposit_goes_through_the_module_in_one_Load_and_never_dispenses()
    {
        var spy = new RecordingDispenser(N(1, 1, 1), null);

        var result = new CashMachine(spy).Execute(Acc(10, 0), new Operation.Deposit(N(1, 0, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal([N(1, 0, 1)], spy.Loads);
        Assert.Empty(spy.Passes);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]  // dépôt vide
    [InlineData(0, 1, 0, 20)] // cassette bloquée
    [InlineData(0, 1, 0, 0)]  // cassette pleine
    public void Refused_deposit_never_touches_the_module(int d50, int d20, int d10, int jammed)
    {
        var spy = new RecordingDispenser(N(0, 5, 0), Jam(jammed));

        var result = new CashMachine(spy).Execute(Acc(10, 0), new Operation.Deposit(N(d50, d20, d10)));

        Assert.NotEqual(AtmStatus.Ok, result.Status);
        Assert.Empty(spy.Loads);
        Assert.Empty(spy.Passes);
        Assert.Equal(N(0, 5, 0), spy.Stock);
    }
}
