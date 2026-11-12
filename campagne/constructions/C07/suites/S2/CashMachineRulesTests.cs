namespace Atm.Tests;

/// <summary>Règles ciblées : ordre des contrôles, frais, découpage, pannes.</summary>
public class CashMachineRulesTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static readonly NoteCounts Full = new(5, 5, 5);

    private static AtmResult Withdraw(int balance, int withdrawn, int amount, NoteCounts? stock = null, Denomination? jammed = null) =>
        new CashMachine(new MoneyDispenser(stock ?? Full, jammed)).Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void Withdraw_InvalidAmount(int amount)
    {
        Assert.Equal(AtmStatus.InvalidAmount, Withdraw(250, 0, amount).Status);
    }

    [Fact]
    public void Withdraw_InvalidAmount_TakesPrecedenceOverEverything()
    {
        Assert.Equal(AtmStatus.InvalidAmount, Withdraw(0, 200, -10, NoteCounts.None).Status);
    }

    [Theory]
    [InlineData(0, 200, AtmStatus.Ok)]
    [InlineData(0, 210, AtmStatus.DailyLimitExceeded)]
    [InlineData(0, 220, AtmStatus.DailyLimitExceeded)]
    [InlineData(190, 10, AtmStatus.Ok)]
    [InlineData(190, 20, AtmStatus.DailyLimitExceeded)]
    [InlineData(200, 10, AtmStatus.DailyLimitExceeded)]
    public void Withdraw_DailyLimitIsTwoHundred_FeeExcluded(int withdrawn, int amount, AtmStatus expected)
    {
        Assert.Equal(expected, Withdraw(250, withdrawn, amount).Status);
    }

    [Fact]
    public void Withdraw_DailyLimit_CheckedBeforeFunds()
    {
        Assert.Equal(AtmStatus.DailyLimitExceeded, Withdraw(0, 200, 10).Status);
    }

    [Fact]
    public void Withdraw_Funds_CheckedBeforeDispensability()
    {
        Assert.Equal(AtmStatus.InsufficientFunds, Withdraw(0, 0, 10, NoteCounts.None).Status);
    }

    [Theory]
    [InlineData(100, 100, 0, AtmStatus.Ok)]
    [InlineData(100, 110, 5, AtmStatus.InsufficientFunds)]
    [InlineData(115, 110, 5, AtmStatus.Ok)]
    [InlineData(205, 200, 5, AtmStatus.Ok)]
    [InlineData(200, 200, 5, AtmStatus.InsufficientFunds)]
    public void Withdraw_FeeAboveHundred(int balance, int amount, int fee, AtmStatus expected)
    {
        var result = Withdraw(balance, 0, amount);

        Assert.Equal(expected, result.Status);
        if (expected == AtmStatus.Ok)
        {
            Assert.Equal(fee, result.Fee);
            Assert.Equal(new AccountState(balance - amount - fee, amount), result.Account);
        }
    }

    [Fact]
    public void Withdraw_PrefersFewestNotes_EvenIfNotGreedy()
    {
        // 60 : 50+10 est impossible sans billet de 10, 20×3 est la seule.
        Assert.Equal(N(0, 3, 0), Withdraw(250, 0, 60, N(5, 5, 0)).Notes);
        // 60 avec tout : 50+10 (2 billets) bat 20×3.
        Assert.Equal(N(1, 0, 1), Withdraw(250, 0, 60, Full).Notes);
        // 80 sans 10 : 20×4 (le glouton 50 + ... échoue).
        Assert.Equal(N(0, 4, 0), Withdraw(250, 0, 80, N(5, 5, 0)).Notes);
    }

    [Fact]
    public void Withdraw_TenJammed_UsesFiftiesAndTwenties()
    {
        var result = Withdraw(250, 0, 90, Full, Denomination.Ten);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(1, 2, 0), result.Notes);
    }

    [Fact]
    public void Withdraw_TwentyJammed_UsesFiftiesAndTens()
    {
        var result = Withdraw(250, 0, 80, Full, Denomination.Twenty);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(1, 0, 3), result.Notes);
    }

    [Fact]
    public void Withdraw_NotEnoughStock_CannotDispense()
    {
        var dispenser = new MoneyDispenser(N(0, 1, 1));
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new AccountState(250, 0)), result);
        Assert.Equal(N(0, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_MoreThanFourNotes_IsSplitIntoPasses()
    {
        var recorder = new RecordingDispenser(new MoneyDispenser(N(0, 5, 5)));
        var result = new CashMachine(recorder).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(0, 5, 5), result.Notes);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(95, 150), result.Account);
        Assert.All(recorder.DispenseCalls, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(N(0, 5, 5), recorder.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(NoteCounts.None, recorder.Stock);
    }

    [Fact]
    public void Withdraw_DispenserFaultOnFirstPass_ReportsNothingDispensed()
    {
        var dispenser = new FaultyDispenser(Full, failOnDispenseCall: 1);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(250, 0)), result);
        Assert.Equal(1, dispenser.DispenseCalls);
    }

    [Fact]
    public void Withdraw_DispenserFaultMidway_ReportsNotesAlreadyOutAndStops()
    {
        var dispenser = new FaultyDispenser(N(0, 5, 5), failOnDispenseCall: 2);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
        Assert.Equal(dispenser.Dispensed, result.Notes);
        Assert.InRange(result.Notes.Total, 1, 4);
        Assert.Equal(2, dispenser.DispenseCalls);
    }

    [Fact]
    public void Deposit_Empty_IsInvalidAmount_EvenIfCassetteJammedAndFull()
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(Full, Denomination.Fifty));
        var result = new CashMachine(dispenser).Execute(new AccountState(10, 0), new Operation.Deposit(NoteCounts.None));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, new AccountState(10, 0)), result);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void Deposit_Jammed_CheckedBeforeFull()
    {
        var dispenser = new MoneyDispenser(N(5, 0, 0), Denomination.Fifty);
        var result = new CashMachine(dispenser).Execute(new AccountState(10, 0), new Operation.Deposit(N(1, 0, 0)));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
    }

    [Fact]
    public void Deposit_PartlyFull_RefusesWholeDeposit()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 5));
        var result = new CashMachine(dispenser).Execute(new AccountState(10, 0), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, new AccountState(10, 0)), result);
        Assert.Equal(N(0, 0, 5), dispenser.Stock);
    }

    [Fact]
    public void Deposit_OtherCassetteJammed_IsAccepted()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0), Denomination.Ten);
        var result = new CashMachine(dispenser).Execute(new AccountState(0, 50), new Operation.Deposit(N(1, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(70, 50)), result);
        Assert.Equal(N(1, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_DispenserFault_ChangesNothing()
    {
        var dispenser = new FaultyDispenser(N(1, 1, 1));
        var result = new CashMachine(dispenser).Execute(new AccountState(10, 0), new Operation.Deposit(N(1, 0, 0)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(10, 0)), result);
        Assert.Equal(1, dispenser.LoadCalls);
    }

    [Fact]
    public void Deposit_WithWorkingFake_Succeeds()
    {
        var dispenser = new FaultyDispenser(N(1, 1, 1), failLoad: false);
        var result = new CashMachine(dispenser).Execute(new AccountState(10, 0), new Operation.Deposit(N(1, 0, 0)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(60, 0)), result);
    }

    [Fact]
    public void Execute_UnknownOperation_Throws()
    {
        var machine = new CashMachine(new MoneyDispenser(Full));

        Assert.Throws<ArgumentException>(() => machine.Execute(new AccountState(0, 0), new UnknownOperation()));
    }

    [Fact]
    public void Constructor_NullDispenser_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CashMachine(null!));
    }
}
