using static Atm.Tests.N;

namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, SpyDispenser Dispenser) Run(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, NoteCounts deposit)
    {
        var dispenser = SpyDispenser.Real(stock, jammed);
        var machine = new CashMachine(dispenser);
        var result = machine.Execute(Account(balance, withdrawnToday), new Operation.Deposit(deposit));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmResult result, AtmStatus status, AccountState account, SpyDispenser dispenser, NoteCounts stock)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.Loads);
        Assert.Empty(dispenser.Dispenses);
    }

    // ---------- Exemples de la spec ----------

    [Fact]
    public void Example_Deposit20_TwentyCassetteFull_CassetteFull()
    {
        var stock = Notes(0, 5, 0);
        var (r, d) = Run(10, 0, stock, null, Notes(0, 1, 0));
        AssertRefused(r, AtmStatus.CassetteFull, Account(10, 0), d, stock);
    }

    [Fact]
    public void Example_Deposit20_TwentyCassetteJammed_CassetteJammed()
    {
        var stock = Notes(0, 1, 0);
        var (r, d) = Run(10, 30, stock, Denomination.Twenty, Notes(0, 1, 0));
        AssertRefused(r, AtmStatus.CassetteJammed, Account(10, 30), d, stock);
    }

    [Fact]
    public void Example_Deposit50And10_Ok()
    {
        var (r, d) = Run(10, 30, Notes(1, 1, 0), null, Notes(1, 0, 1));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Account(70, 30), r.Account);
        Assert.Equal(Notes(2, 1, 1), d.Stock);
        Assert.Empty(d.Dispenses);
    }

    // ---------- Contrôle 1 : dépôt vide ----------

    [Fact]
    public void EmptyDeposit_InvalidAmount()
    {
        var stock = Notes(1, 1, 1);
        var (r, d) = Run(10, 30, stock, null, NoteCounts.None);
        AssertRefused(r, AtmStatus.InvalidAmount, Account(10, 30), d, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void EmptyDeposit_InvalidAmount_EvenWithJammedAndFullCassettes(Denomination jammed)
    {
        var (r, d) = Run(10, 30, Full, jammed, NoteCounts.None);
        AssertRefused(r, AtmStatus.InvalidAmount, Account(10, 30), d, Full);
    }

    // ---------- Contrôle 2 : cassette bloquée ----------

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 1, 1)]
    [InlineData(Denomination.Ten, 1, 1, 1)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void DepositTouchingJammedCassette_CassetteJammed(Denomination jammed, int f, int t, int n)
    {
        var stock = Notes(0, 0, 0);
        var (r, d) = Run(100, 0, stock, jammed, Notes(f, t, n));
        AssertRefused(r, AtmStatus.CassetteJammed, Account(100, 0), d, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void CassetteJammed_IsCheckedBeforeCassetteFull(Denomination jammed)
    {
        // Toutes les cassettes sont pleines ; le dépôt touche aussi la bloquée.
        var (r, d) = Run(100, 0, Full, jammed, Notes(1, 1, 1));
        AssertRefused(r, AtmStatus.CassetteJammed, Account(100, 0), d, Full);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1, 30)]
    [InlineData(Denomination.Twenty, 1, 0, 1, 60)]
    [InlineData(Denomination.Ten, 1, 1, 0, 70)]
    public void DepositAvoidingJammedCassette_IsOk(Denomination jammed, int f, int t, int n, int value)
    {
        var stock = Notes(2, 2, 2);
        var (r, d) = Run(100, 50, stock, jammed, Notes(f, t, n));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Account(100 + value, 50), r.Account);
        Assert.Equal(stock + Notes(f, t, n), d.Stock);
    }

    // ---------- Contrôle 3 : cassette pleine ----------

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 4, 4, 1, 1, 1)] // une seule cassette déborde : tout est refusé
    [InlineData(4, 4, 5, 1, 1, 1)]
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void DepositOverflowingACassette_CassetteFull(int sf, int st, int sn, int f, int t, int n)
    {
        var stock = Notes(sf, st, sn);
        var (r, d) = Run(100, 0, stock, null, Notes(f, t, n));
        AssertRefused(r, AtmStatus.CassetteFull, Account(100, 0), d, stock);
    }

    [Fact]
    public void DepositOverflowingOtherCassetteThanJammed_CassetteFull()
    {
        var stock = Notes(5, 0, 0);
        var (r, d) = Run(100, 0, stock, Denomination.Ten, Notes(1, 1, 0));
        AssertRefused(r, AtmStatus.CassetteFull, Account(100, 0), d, stock);
    }

    // ---------- Succès ----------

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Deposit_Ok_AddsValueToBalance_NotesJoinModule(int f, int t, int n, int value)
    {
        var stock = Notes(4, 4, 4); // la cassette atteint pile 5 : autorisé
        var (r, d) = Run(150, 120, stock, null, Notes(f, t, n));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Account(150 + value, 120), r.Account);
        Assert.Equal(stock + Notes(f, t, n), d.Stock);
        Assert.Empty(d.Dispenses);
        Assert.Equal(Notes(f, t, n), d.Loads.Where(l => l.Result).Aggregate(NoteCounts.None, (a, l) => a + l.Notes));
    }

    [Fact]
    public void Deposit_DoesNotChangeWithdrawnToday_EvenAtDailyLimit()
    {
        var (r, _) = Run(0, 200, Notes(0, 0, 0), null, Notes(1, 1, 1));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Account(80, 200), r.Account);
    }

    [Fact]
    public void Deposit_IntoEmptyMachine_IsOk()
    {
        var (r, d) = Run(0, 0, Notes(0, 0, 0), null, Notes(1, 1, 1));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Account(80, 0), r.Account);
        Assert.Equal(Notes(1, 1, 1), d.Stock);
    }
}
