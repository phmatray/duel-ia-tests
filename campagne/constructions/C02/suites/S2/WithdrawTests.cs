namespace Atm.Tests;

using static Notes;

public class WithdrawTests
{
    private static readonly NoteCounts Full = Of(5, 5, 5);

    private static AtmResult Withdraw(int amount, AccountState account, NoteCounts stock, Denomination? jammed = null) =>
        new CashMachine(new MoneyDispenser(stock, jammed)).Execute(account, new Operation.Withdraw(amount));

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void InvalidAmounts_AreRefused(int amount)
    {
        Assert.Equal(AtmStatus.InvalidAmount, Withdraw(amount, new(250, 0), Full).Status);
    }

    [Fact]
    public void InvalidAmount_IsCheckedBeforeEverythingElse()
    {
        // Plafond atteint, solde nul et stock vide : le montant invalide l'emporte.
        Assert.Equal(AtmStatus.InvalidAmount, Withdraw(5, new(0, 200), Of(0, 0, 0)).Status);
    }

    [Fact]
    public void DailyLimit_IsCheckedBeforeFunds()
    {
        Assert.Equal(AtmStatus.DailyLimitExceeded, Withdraw(60, new(0, 150), Full).Status);
    }

    [Fact]
    public void Funds_AreCheckedBeforeDispensability()
    {
        Assert.Equal(AtmStatus.InsufficientFunds, Withdraw(60, new(50, 0), Of(0, 0, 0)).Status);
    }

    [Fact]
    public void DailyLimit_ExactlyReached_IsAllowed()
    {
        var result = Withdraw(50, new(250, 150), Full);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(1, 0, 0), 0, new(200, 200)), result);
    }

    [Fact]
    public void Withdraw220_ExceedsDailyLimit()
    {
        Assert.Equal(AtmStatus.DailyLimitExceeded, Withdraw(220, new(250, 0), Full).Status);
    }

    [Fact]
    public void Fee_IsNotCountedInDailyLimit()
    {
        var result = Withdraw(200, new(250, 0), Full);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(4, 0, 0), 5, new(45, 200)), result);
    }

    [Fact]
    public void BalanceExactlyCoveringAmountAndFee_IsAllowed()
    {
        var result = Withdraw(150, new(155, 0), Full);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(3, 0, 0), 5, new(0, 150)), result);
    }

    [Fact]
    public void BalanceExactlyCoveringAmountWithoutFee_IsAllowed()
    {
        var result = Withdraw(100, new(100, 0), Full);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(2, 0, 0), 0, new(0, 100)), result);
    }

    [Theory]
    [InlineData(10, 0, 0, 1)]
    [InlineData(30, 0, 1, 1)]
    [InlineData(60, 1, 0, 1)]
    [InlineData(90, 1, 2, 0)]
    [InlineData(130, 2, 1, 1)]
    [InlineData(200, 4, 0, 0)]
    public void FullStock_UsesFewestNotes(int amount, int fifties, int twenties, int tens)
    {
        Assert.Equal(Of(fifties, twenties, tens), Withdraw(amount, new(250, 0), Full).Notes);
    }

    [Fact]
    public void FewestNotes_UsesTensWhenNoTwenties()
    {
        Assert.Equal(Of(1, 0, 3), Withdraw(80, new(250, 0), Of(5, 0, 5)).Notes);
    }

    [Fact]
    public void FewestNotes_SkipsFiftyWhenItLeavesAnUnpayableRest()
    {
        // 60 = 50 + 10 impossible sans billet de 10 : il faut 20×3.
        Assert.Equal(Of(0, 3, 0), Withdraw(60, new(250, 0), Of(5, 5, 0)).Notes);
    }

    [Fact]
    public void JammedTens_AreNotUsed()
    {
        Assert.Equal(AtmStatus.CannotDispense, Withdraw(10, new(250, 0), Of(5, 5, 5), Denomination.Ten).Status);
        Assert.Equal(Of(0, 2, 0), Withdraw(40, new(250, 0), Of(5, 5, 5), Denomination.Ten).Notes);
    }

    [Fact]
    public void JammedTwenties_AreNotUsed()
    {
        Assert.Equal(Of(0, 0, 4), Withdraw(40, new(250, 0), Of(5, 5, 5), Denomination.Twenty).Notes);
    }

    [Fact]
    public void NotEnoughNotes_CannotDispense()
    {
        var dispenser = new MoneyDispenser(Of(1, 1, 1));
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(90));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, default, 0, new(250, 0)), result);
        Assert.Equal(Of(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void LargeWithdrawal_IsSplitIntoPassesOfAtMostFourNotes()
    {
        var dispenser = new FakeDispenser(Of(1, 5, 5));
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(190));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(1, 5, 4), 5, new(55, 190)), result);
        Assert.All(dispenser.DispenseCalls, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(Of(1, 5, 4), dispenser.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(3, dispenser.DispenseCalls.Count);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void LargeWithdrawal_WorksWithTheRealDispenser()
    {
        var dispenser = new MoneyDispenser(Of(1, 5, 5));
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(190));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Of(0, 0, 1), dispenser.Stock);
    }

    [Fact]
    public void Refusal_DoesNotTouchTheDispenser()
    {
        var dispenser = new FakeDispenser(Full);
        new CashMachine(dispenser).Execute(new(10, 0), new Operation.Withdraw(60));

        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void FaultOnFirstPass_ReturnsNoNotesAndUnchangedAccount()
    {
        var dispenser = new FakeDispenser(Full) { FailDispenseOnPass = 1 };
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, default, 0, new(250, 0)), result);
        Assert.Single(dispenser.DispenseCalls);
    }

    [Fact]
    public void FaultOnLaterPass_ReturnsNotesAlreadyDispensedAndStops()
    {
        var dispenser = new FakeDispenser(Of(1, 5, 5)) { FailDispenseOnPass = 2 };
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(190));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(dispenser.DispenseCalls[0], result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
        Assert.Equal(2, dispenser.DispenseCalls.Count);
    }
}
