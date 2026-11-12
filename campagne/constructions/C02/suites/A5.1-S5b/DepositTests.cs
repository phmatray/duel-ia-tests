namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, RecordingDispenser Dispenser) Run(
        int balance, int withdrawn, NoteCounts deposit, NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Deposit(deposit));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult r, int balance, int withdrawn, RecordingDispenser d, NoteCounts stock)
    {
        Assert.Equal(expected, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(balance, withdrawn), r.Account);
        Assert.Equal(stock, d.Stock);
        Assert.Empty(d.DispenseCalls);
        Assert.Empty(d.LoadCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Ten)]
    public void EmptyDeposit_IsInvalidAmount(Denomination? jammed)
    {
        var (r, d) = Run(100, 30, NoteCounts.None, N.Of(5, 5, 5), jammed);
        AssertRefused(AtmStatus.InvalidAmount, r, 100, 30, d, N.Of(5, 5, 5));
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 1)]
    [InlineData(Denomination.Fifty, 1, 0, 1)]
    public void DepositWithJammedDenomination_IsCassetteJammed(Denomination jammed, int f, int t, int d)
    {
        var stock = N.Of(1, 1, 1);
        var (r, disp) = Run(100, 30, N.Of(f, t, d), stock, jammed);
        AssertRefused(AtmStatus.CassetteJammed, r, 100, 30, disp, stock);
    }

    [Fact]
    public void CassetteJammed_IsCheckedBeforeCassetteFull()
    {
        // Le 50 déborderait, mais le 20 touche la cassette bloquée.
        var stock = N.Of(5, 5, 5);
        var (r, d) = Run(100, 0, N.Of(1, 1, 0), stock, Denomination.Twenty);
        AssertRefused(AtmStatus.CassetteJammed, r, 100, 0, d, stock);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)]  // un seul billet déborde : tout est refusé
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void Overflow_IsCassetteFull(int sf, int st, int sd, int f, int t, int d)
    {
        var stock = N.Of(sf, st, sd);
        var (r, disp) = Run(100, 0, N.Of(f, t, d), stock);
        AssertRefused(AtmStatus.CassetteFull, r, 100, 0, disp, stock);
    }

    [Fact]
    public void CassetteFull_AppliesEvenWhenAnotherCassetteIsJammed()
    {
        var stock = N.Of(0, 5, 0);
        var (r, d) = Run(100, 0, N.Of(0, 1, 0), stock, Denomination.Fifty);
        AssertRefused(AtmStatus.CassetteFull, r, 100, 0, d, stock);
    }

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 1, 1, 80)]
    public void Success_CreditsBalance_AndLoadsNotes(int f, int t, int d, int value)
    {
        var stock = N.Of(4, 4, 4);
        var deposit = N.Of(f, t, d);
        var (r, disp) = Run(100, 70, deposit, stock);
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(100 + value, 70)), r);
        Assert.Equal(stock + deposit, disp.Stock);
        Assert.Empty(disp.DispenseCalls);
    }

    [Fact]
    public void Success_FillingCassetteToExactly5_IsAllowed()
    {
        var (r, d) = Run(0, 0, N.Of(1, 1, 1), N.Of(4, 4, 4));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N.Of(5, 5, 5), d.Stock);
    }

    [Fact]
    public void Success_WithJammedCassetteNotInvolved()
    {
        var (r, d) = Run(250, 200, N.Of(1, 0, 1), N.Of(0, 3, 0), Denomination.Twenty);
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(310, 200)), r);
        Assert.Equal(N.Of(1, 3, 1), d.Stock);
    }

    [Fact]
    public void Success_DoesNotDependOnDailyLimitOrBalance()
    {
        var (r, _) = Run(0, 200, N.Of(0, 0, 1), N.Of(0, 0, 0));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(10, 200)), r);
    }
}
