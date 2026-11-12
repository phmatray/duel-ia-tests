namespace Atm.Tests;

public class WithdrawTests
{
    private static NoteCounts Full => new(5, 5, 5);

    private static (AtmResult Result, RecordingDispenser Dispenser) Run(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount)
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawnToday), new Operation.Withdraw(amount));

        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult result, RecordingDispenser dispenser, AccountState account, NoteCounts stock)
    {
        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
    }

    // Exemples de la spécification.

    [Fact]
    public void Withdraw60_WithOnlyTwenties_GivesThreeTwenties()
    {
        var (result, dispenser) = Run(100, 0, new(1, 3, 0), null, 60);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 3, 0), 0, new(40, 60)), result);
        Assert.Equal(new NoteCounts(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw80_UsesOneOfEach()
    {
        var (result, dispenser) = Run(100, 0, new(1, 1, 1), null, 80);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 1, 1), 0, new(20, 80)), result);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void Withdraw100_HasNoFee()
    {
        var (result, _) = Run(105, 0, Full, null, 100);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 0), 0, new(5, 100)), result);
    }

    [Fact]
    public void Withdraw110_HasFiveEuroFee()
    {
        var (result, dispenser) = Run(115, 0, Full, null, 110);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 1), 5, new(0, 110)), result);
        Assert.Equal(new NoteCounts(3, 5, 4), dispenser.Stock);
    }

    [Fact]
    public void Withdraw110_WithoutRoomForFee_IsInsufficientFunds()
    {
        var (result, dispenser) = Run(110, 0, Full, null, 110);

        AssertRefused(AtmStatus.InsufficientFunds, result, dispenser, new(110, 0), Full);
    }

    [Fact]
    public void Withdraw_OverDailyLimit_IsRefused()
    {
        var (result, dispenser) = Run(250, 150, Full, null, 60);

        AssertRefused(AtmStatus.DailyLimitExceeded, result, dispenser, new(250, 150), Full);
    }

    [Fact]
    public void Withdraw_FromJammedCassetteOnly_CannotDispense()
    {
        var (result, dispenser) = Run(250, 0, new(1, 0, 0), Denomination.Fifty, 50);

        AssertRefused(AtmStatus.CannotDispense, result, dispenser, new(250, 0), new(1, 0, 0));
    }

    [Fact]
    public void Withdraw_AvoidsJammedCassette()
    {
        var (result, dispenser) = Run(250, 0, new(2, 2, 0), Denomination.Fifty, 40);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 2, 0), 0, new(210, 40)), result);
        Assert.Equal(new NoteCounts(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw5_IsInvalidAmount()
    {
        var (result, dispenser) = Run(0, 200, NoteCounts.None, null, 5);

        AssertRefused(AtmStatus.InvalidAmount, result, dispenser, new(0, 200), NoteCounts.None);
    }

    // Contrôles et leur ordre.

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void InvalidAmount_IsCheckedFirst(int amount)
    {
        // Le compte dépasse aussi le plafond et n'a pas de fonds : le montant est contrôlé avant.
        var (result, dispenser) = Run(0, 200, NoteCounts.None, null, amount);

        AssertRefused(AtmStatus.InvalidAmount, result, dispenser, new(0, 200), NoteCounts.None);
    }

    [Fact]
    public void DailyLimit_IsCheckedBeforeFunds()
    {
        var (result, _) = Run(0, 200, NoteCounts.None, null, 10);

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void DailyLimit_ReachedExactly_IsAllowed()
    {
        var (result, _) = Run(250, 150, Full, null, 50);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 0), 0, new(200, 200)), result);
    }

    [Fact]
    public void Withdraw220_ExceedsDailyLimit()
    {
        var (result, _) = Run(250, 0, Full, null, 220);

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void Funds_AreCheckedBeforeDispensability()
    {
        var (result, _) = Run(20, 0, NoteCounts.None, null, 30);

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    [Fact]
    public void Funds_ExactBalanceWithoutFee_IsAllowed()
    {
        var (result, _) = Run(100, 0, Full, null, 100);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 0), 0, new(0, 100)), result);
    }

    [Fact]
    public void Fee_DoesNotCountTowardDailyLimit()
    {
        var (result, _) = Run(250, 0, Full, null, 200);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(4, 0, 0), 5, new(45, 200)), result);
    }

    [Fact]
    public void CannotDispense_WhenNoExactCombination()
    {
        var (result, dispenser) = Run(250, 0, new(1, 0, 0), null, 30);

        AssertRefused(AtmStatus.CannotDispense, result, dispenser, new(250, 0), new(1, 0, 0));
    }

    [Fact]
    public void FewestNotes_PrefersFiftyAndTenOverThreeTwenties()
    {
        var (result, _) = Run(250, 0, Full, null, 60);

        Assert.Equal(new NoteCounts(1, 0, 1), result.Notes);
    }

    [Fact]
    public void FewestNotes_UsesTwentiesWhenFiftyWouldStrandAnOddRemainder()
    {
        // 80 = 50 + 20 + 10 (3 billets) ou 20×4 (4 billets) ; sans billet de 10, seule 20×4 convient.
        var (result, _) = Run(250, 0, new(5, 5, 0), null, 80);

        Assert.Equal(new NoteCounts(0, 4, 0), result.Notes);
    }

    [Fact]
    public void FewestNotes_WithJammedTwenties_FallsBackOnTens()
    {
        var (result, _) = Run(250, 0, Full, Denomination.Twenty, 90);

        Assert.Equal(new NoteCounts(1, 0, 4), result.Notes);
    }

    // Passages.

    [Fact]
    public void LargeCombination_IsSplitIntoPassesOfAtMostFourNotes()
    {
        // 190 = 50×3 + 20×2 : 5 billets, donc au moins deux passages.
        var (result, dispenser) = Run(250, 0, Full, null, 190);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(3, 2, 0), 5, new(55, 190)), result);
        Assert.True(dispenser.DispenseCalls.Count >= 2);
        Assert.All(dispenser.DispenseCalls, pass => Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(result.Notes, dispenser.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(new NoteCounts(2, 3, 5), dispenser.Stock);
    }

    [Fact]
    public void TenNotes_NeedThreePasses()
    {
        // 150 sans billet de 50 : 20×5 + 10×5 = 10 billets.
        var (result, dispenser) = Run(250, 0, new(0, 5, 5), null, 150);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 5, 5), 5, new(95, 150)), result);
        Assert.Equal(3, dispenser.DispenseCalls.Count);
        Assert.All(dispenser.DispenseCalls, pass => Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    // Pannes.

    [Fact]
    public void DispenserFault_OnFirstPass_ReturnsNoNotes()
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(Full), failOnDispenseCall: 1);
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new(250, 0)), result);
        Assert.Single(dispenser.DispenseCalls);
    }

    [Fact]
    public void DispenserFault_OnLaterPass_ReturnsNotesAlreadyDispensedAndStops()
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(new(0, 5, 5)), failOnDispenseCall: 2);
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
        Assert.Equal(dispenser.DispenseCalls[0], result.Notes);
        Assert.Equal(2, dispenser.DispenseCalls.Count);
        Assert.Equal(new NoteCounts(0, 5, 5) - result.Notes, dispenser.Stock);
    }
}
