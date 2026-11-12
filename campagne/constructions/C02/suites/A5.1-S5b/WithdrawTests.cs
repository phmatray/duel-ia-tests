namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N.Of(5, 5, 5);

    private static (AtmResult Result, RecordingDispenser Dispenser) Run(
        int balance, int withdrawn, int amount, NoteCounts? stock = null, Denomination? jammed = null)
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock ?? Full, jammed));
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));
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

    // --- Règle 1 : montant invalide ---

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void InvalidAmount_IsRefused(int amount)
    {
        var (r, d) = Run(250, 0, amount);
        AssertRefused(AtmStatus.InvalidAmount, r, 250, 0, d, Full);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void InvalidAmount_IsCheckedBeforeEverythingElse(int amount)
    {
        // Plafond atteint, solde nul, stock vide : InvalidAmount prime.
        var (r, d) = Run(0, 200, amount, N.Of(0, 0, 0), Denomination.Ten);
        AssertRefused(AtmStatus.InvalidAmount, r, 0, 200, d, N.Of(0, 0, 0));
    }

    // --- Règle 2 : plafond du jour ---

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(110, 100)]
    public void DailyLimit_Exceeded_IsRefused(int withdrawn, int amount)
    {
        var (r, d) = Run(250, withdrawn, amount);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, 250, withdrawn, d, Full);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void DailyLimit_ReachedExactly_IsAllowed(int withdrawn, int amount)
    {
        var (r, _) = Run(250, withdrawn, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(withdrawn + amount, r.Account.WithdrawnToday);
    }

    [Fact]
    public void DailyLimit_DoesNotCountTheFee()
    {
        // 90 + 110 = 200 : autorisé, même si 110 + 5 de frais dépasse 200 - 90.
        var (r, _) = Run(250, 90, 110);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(135, 200), r.Account);
    }

    [Fact]
    public void DailyLimit_IsCheckedBeforeFunds()
    {
        var (r, d) = Run(0, 200, 10);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, 0, 200, d, Full);
    }

    [Fact]
    public void DailyLimit_IsCheckedBeforeDispensability()
    {
        var (r, d) = Run(250, 150, 60, N.Of(0, 0, 0));
        AssertRefused(AtmStatus.DailyLimitExceeded, r, 250, 150, d, N.Of(0, 0, 0));
    }

    // --- Règle 3 : fonds insuffisants (frais compris) ---

    [Theory]
    [InlineData(0, 10)]
    [InlineData(50, 60)]
    [InlineData(100, 110)]   // 110 + 5 > 100
    [InlineData(110, 110)]   // 110 + 5 > 110
    [InlineData(200, 200)]   // 200 + 5 > 200
    public void InsufficientFunds_IsRefused(int balance, int amount)
    {
        var (r, d) = Run(balance, 0, amount);
        AssertRefused(AtmStatus.InsufficientFunds, r, balance, 0, d, Full);
    }

    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 5)]
    [InlineData(205, 200, 5)]
    public void Funds_ExactlyEnough_IsAllowed(int balance, int amount, int fee)
    {
        var (r, _) = Run(balance, 0, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(0, r.Account.Balance);
    }

    [Fact]
    public void InsufficientFunds_IsCheckedBeforeDispensability()
    {
        var (r, d) = Run(30, 0, 40, N.Of(0, 0, 0));
        AssertRefused(AtmStatus.InsufficientFunds, r, 30, 0, d, N.Of(0, 0, 0));
    }

    // --- Frais ---

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_Is5OnlyAbove100(int amount, int expectedFee)
    {
        var (r, _) = Run(250, 0, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedFee, r.Fee);
        Assert.Equal(250 - amount - expectedFee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    // --- Règle 4 : impossible à servir ---

    [Theory]
    [InlineData(0, 0, 0, null, 10)]
    [InlineData(1, 0, 0, null, 10)]          // pas de 10
    [InlineData(0, 5, 0, null, 30)]          // que des 20
    [InlineData(5, 0, 0, null, 60)]          // que des 50
    [InlineData(1, 1, 1, null, 90)]          // stock total 80
    [InlineData(0, 0, 5, null, 60)]          // stock total 50
    [InlineData(5, 5, 5, Denomination.Ten, 10)]
    [InlineData(5, 0, 5, Denomination.Ten, 60)]
    [InlineData(1, 1, 0, Denomination.Twenty, 70)]
    [InlineData(0, 5, 0, Denomination.Twenty, 20)]
    public void CannotDispense_IsRefused(int f, int t, int d, Denomination? jammed, int amount)
    {
        var stock = N.Of(f, t, d);
        var (r, disp) = Run(250, 0, amount, stock, jammed);
        AssertRefused(AtmStatus.CannotDispense, r, 250, 0, disp, stock);
    }

    // --- Combinaison au moins de billets ---

    [Theory]
    [InlineData(5, 5, 5, null, 60, 1, 0, 1)]
    [InlineData(1, 3, 0, null, 60, 0, 3, 0)]      // le glouton échouerait
    [InlineData(2, 3, 0, null, 110, 1, 3, 0)]     // le glouton échouerait (50×2 puis 10)
    [InlineData(1, 4, 0, null, 80, 0, 4, 0)]
    [InlineData(5, 5, 5, null, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, null, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, null, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, null, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, null, 200, 4, 0, 0)]
    [InlineData(0, 5, 5, null, 140, 0, 5, 4)]
    [InlineData(5, 0, 5, null, 40, 0, 0, 4)]
    [InlineData(5, 5, 5, Denomination.Fifty, 100, 0, 5, 0)]
    [InlineData(5, 5, 5, Denomination.Twenty, 70, 1, 0, 2)]
    [InlineData(5, 5, 5, Denomination.Ten, 70, 1, 1, 0)]
    [InlineData(5, 5, 5, Denomination.Ten, 30, 0, 0, 0)] // 30 sans 10 : impossible
    public void Combination_HasFewestNotes(int f, int t, int d, Denomination? jammed, int amount, int ef, int et, int ed)
    {
        var stock = N.Of(f, t, d);
        var (r, disp) = Run(250, 0, amount, stock, jammed);
        var expected = Oracle.FewestNotes(amount, Oracle.Available(stock, jammed));
        if (expected is null)
        {
            Assert.Equal(AtmStatus.CannotDispense, r.Status);
            return;
        }
        Assert.Equal(N.Of(ef, et, ed), expected.Value); // garde-fou sur les données du test
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N.Of(ef, et, ed), r.Notes);
        Assert.Equal(stock - r.Notes, disp.Stock);
    }

    // --- Sortie en passages ---

    [Theory]
    [InlineData(200, 5, 5, 5)]   // 50×4
    [InlineData(140, 0, 5, 5)]   // 20×5 10×4 = 9 billets
    [InlineData(150, 0, 5, 5)]   // 20×5 10×5 = 10 billets
    [InlineData(190, 1, 5, 5)]   // 50 + 20×5 + 10×4
    [InlineData(40, 5, 0, 5)]    // 10×4
    [InlineData(50, 0, 0, 5)]    // 10×5
    [InlineData(100, 0, 5, 0)]   // 20×5
    public void Dispense_UsesPassesOfAtMost4Notes_ThatSumToTheCombination(int amount, int f, int t, int d)
    {
        var stock = N.Of(f, t, d);
        var (r, disp) = Run(250, 0, amount, stock);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(amount, r.Notes.Value);
        Assert.NotEmpty(disp.DispenseCalls);
        Assert.All(disp.DispenseCalls, p => Assert.InRange(p.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(r.Notes, disp.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Empty(disp.LoadCalls);
        Assert.Equal(stock - r.Notes, disp.Stock);
    }

    [Fact]
    public void Withdraw_DoesNotTouchTheJammedCassette()
    {
        var (r, disp) = Run(250, 0, 150, N.Of(5, 5, 5), Denomination.Fifty);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N.Of(0, 5, 5), r.Notes);
        Assert.All(disp.DispenseCalls, p => Assert.Equal(0, p.Fifties));
    }
}
