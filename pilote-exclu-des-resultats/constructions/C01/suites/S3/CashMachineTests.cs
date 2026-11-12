namespace Atm.Tests;

public sealed class CashMachineTests
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    private static AtmResult Withdraw(int balance, int withdrawn, NoteCounts stock, Denomination? jammed, int amount,
        out MoneyDispenser dispenser)
    {
        dispenser = new MoneyDispenser(stock, jammed);

        return new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));
    }

    private static AtmResult Deposit(int balance, int withdrawn, NoteCounts stock, Denomination? jammed, NoteCounts notes,
        out MoneyDispenser dispenser)
    {
        dispenser = new MoneyDispenser(stock, jammed);

        return new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Deposit(notes));
    }

    private static void AssertRefused(AtmResult result, AtmStatus status, AccountState account)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
    }

    // ---- Exemples de la spécification ----

    [Fact]
    public void Example_Withdraw60_UsesThreeTwenties()
    {
        var result = Withdraw(100, 0, new(1, 3, 0), null, 60, out var dispenser);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 3, 0), 0, new(40, 60)), result);
        Assert.Equal(new NoteCounts(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Example_Withdraw80_UsesOneOfEach()
    {
        var result = Withdraw(100, 0, new(1, 1, 1), null, 80, out var dispenser);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 1, 1), 0, new(20, 80)), result);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void Example_Withdraw100_NoFee()
    {
        var result = Withdraw(105, 0, Full, null, 100, out _);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 0), 0, new(5, 100)), result);
    }

    [Fact]
    public void Example_Withdraw110_ChargesFee()
    {
        var result = Withdraw(115, 0, Full, null, 110, out var dispenser);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 1), 5, new(0, 110)), result);
        Assert.Equal(new NoteCounts(3, 5, 4), dispenser.Stock);
    }

    [Fact]
    public void Example_Withdraw110_FeeMakesFundsInsufficient()
    {
        var result = Withdraw(110, 0, Full, null, 110, out var dispenser);

        AssertRefused(result, AtmStatus.InsufficientFunds, new(110, 0));
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Example_DailyLimitExceeded()
    {
        var result = Withdraw(250, 150, Full, null, 60, out var dispenser);

        AssertRefused(result, AtmStatus.DailyLimitExceeded, new(250, 150));
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Example_JammedCassetteCountsAsEmpty()
    {
        var result = Withdraw(250, 0, new(1, 0, 0), Denomination.Fifty, 50, out var dispenser);

        AssertRefused(result, AtmStatus.CannotDispense, new(250, 0));
        Assert.Equal(new NoteCounts(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Example_JammedFifty_UsesTwenties()
    {
        var result = Withdraw(250, 0, new(2, 2, 0), Denomination.Fifty, 40, out var dispenser);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 2, 0), 0, new(210, 40)), result);
        Assert.Equal(new NoteCounts(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Example_Withdraw5_InvalidAmount()
    {
        var result = Withdraw(0, 200, NoteCounts.None, null, 5, out _);

        AssertRefused(result, AtmStatus.InvalidAmount, new(0, 200));
    }

    [Fact]
    public void Example_Deposit_CassetteFull()
    {
        var result = Deposit(10, 0, new(0, 5, 0), null, new(0, 1, 0), out var dispenser);

        AssertRefused(result, AtmStatus.CassetteFull, new(10, 0));
        Assert.Equal(new NoteCounts(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Example_Deposit_CassetteJammed()
    {
        var result = Deposit(10, 30, new(0, 1, 0), Denomination.Twenty, new(0, 1, 0), out var dispenser);

        AssertRefused(result, AtmStatus.CassetteJammed, new(10, 30));
        Assert.Equal(new NoteCounts(0, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Example_Deposit_Ok()
    {
        var result = Deposit(10, 30, new(1, 1, 0), null, new(1, 0, 1), out var dispenser);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(70, 30)), result);
        Assert.Equal(new NoteCounts(2, 1, 1), dispenser.Stock);
    }

    // ---- Retrait : contrôles et ordre ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void Withdraw_InvalidAmount(int amount)
    {
        var result = Withdraw(250, 0, Full, null, amount, out var dispenser);

        AssertRefused(result, AtmStatus.InvalidAmount, new(250, 0));
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Withdraw_InvalidAmount_CheckedBeforeDailyLimit()
    {
        var result = Withdraw(0, 200, NoteCounts.None, null, -10, out _);

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void Withdraw_DailyLimit_CheckedBeforeFunds()
    {
        var result = Withdraw(0, 0, NoteCounts.None, null, 210, out _);

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void Withdraw_Funds_CheckedBeforeDispensability()
    {
        var result = Withdraw(10, 0, NoteCounts.None, null, 20, out _);

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    [Fact]
    public void Withdraw_DailyLimitReachedExactly_IsAccepted()
    {
        var result = Withdraw(250, 150, Full, null, 50, out _);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 0), 0, new(200, 200)), result);
    }

    [Fact]
    public void Withdraw_FeeIsNotCountedInDailyLimit()
    {
        var result = Withdraw(250, 0, Full, null, 200, out _);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(4, 0, 0), 5, new(45, 200)), result);
    }

    [Fact]
    public void Withdraw_BalanceExactlyEnoughWithoutFee()
    {
        var result = Withdraw(100, 0, Full, null, 100, out _);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 0), 0, new(0, 100)), result);
    }

    [Fact]
    public void Withdraw_NoExactCombination_CannotDispense()
    {
        var result = Withdraw(250, 0, new(1, 5, 0), null, 30, out var dispenser);

        AssertRefused(result, AtmStatus.CannotDispense, new(250, 0));
        Assert.Equal(new NoteCounts(1, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_NotEnoughNotes_CannotDispense()
    {
        var result = Withdraw(250, 0, new(1, 1, 1), null, 90, out _);

        Assert.Equal(AtmStatus.CannotDispense, result.Status);
    }

    [Fact]
    public void Withdraw_FewestNotes_AvoidsGreedyTrap()
    {
        // 60 € sans billet de 10 : le glouton (50 d'abord) échoue, 20×3 réussit.
        var result = Withdraw(250, 0, new(5, 5, 0), null, 60, out _);

        Assert.Equal(new NoteCounts(0, 3, 0), result.Notes);
    }

    [Fact]
    public void Withdraw_FewestNotes_PrefersFiftyAndTen()
    {
        var result = Withdraw(250, 0, Full, null, 60, out _);

        Assert.Equal(new NoteCounts(1, 0, 1), result.Notes);
    }

    [Fact]
    public void Withdraw_JammedTen_UsesOtherNotes()
    {
        var result = Withdraw(250, 0, Full, Denomination.Ten, 90, out var dispenser);

        Assert.Equal(new NoteCounts(1, 2, 0), result.Notes);
        Assert.Equal(new NoteCounts(4, 3, 5), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_JammedTwenty_UsesOtherNotes()
    {
        var result = Withdraw(250, 0, Full, Denomination.Twenty, 40, out var dispenser);

        Assert.Equal(new NoteCounts(0, 0, 4), result.Notes);
        Assert.Equal(new NoteCounts(5, 5, 1), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_ManyNotes_SplitIntoPassesOfAtMostFour()
    {
        var dispenser = new FakeDispenser(Full);

        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.All(dispenser.DispenseCalls, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(new NoteCounts(4, 0, 0), result.Notes);
    }

    [Fact]
    public void Withdraw_NineNotes_UsesThreePasses()
    {
        // 140 € sans billet de 50 : 20×5 + 10×4 = 9 billets → 3 passages.
        var dispenser = new FakeDispenser(Full, Denomination.Fifty);

        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(140));

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 5, 4), 5, new(105, 140)), result);
        Assert.Equal(3, dispenser.DispenseCalls.Count);
        Assert.All(dispenser.DispenseCalls, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(new NoteCounts(0, 5, 4), dispenser.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(new NoteCounts(5, 0, 1), dispenser.Stock);
    }

    // ---- Pannes ----

    [Fact]
    public void Withdraw_FaultOnFirstPass_NoNotes()
    {
        var dispenser = new FakeDispenser(Full) { FailDispenseAt = 1 };

        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(60));

        AssertRefused(result, AtmStatus.DispenserFault, new(250, 0));
        Assert.Single(dispenser.DispenseCalls);
    }

    [Fact]
    public void Withdraw_FaultOnLaterPass_ReturnsNotesAlreadyDispensed()
    {
        var dispenser = new FakeDispenser(Full, Denomination.Fifty) { FailDispenseAt = 2 };

        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(140));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(dispenser.DispenseCalls[0], result.Notes);
        Assert.Equal(4, result.Notes.Total);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
        Assert.Equal(2, dispenser.DispenseCalls.Count);
        Assert.Equal(Full - result.Notes, dispenser.Stock);
    }

    [Fact]
    public void Deposit_LoadFails_DispenserFault()
    {
        var dispenser = new FakeDispenser(NoteCounts.None) { FailLoad = true };

        var result = new CashMachine(dispenser).Execute(new(10, 30), new Operation.Deposit(new(1, 1, 1)));

        AssertRefused(result, AtmStatus.DispenserFault, new(10, 30));
        Assert.Single(dispenser.LoadCalls);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    // ---- Dépôt ----

    [Fact]
    public void Deposit_Empty_InvalidAmount()
    {
        var result = Deposit(10, 0, NoteCounts.None, Denomination.Ten, NoteCounts.None, out _);

        AssertRefused(result, AtmStatus.InvalidAmount, new(10, 0));
    }

    [Fact]
    public void Deposit_Jammed_CheckedBeforeFull()
    {
        var result = Deposit(10, 0, new(5, 0, 0), Denomination.Fifty, new(1, 0, 0), out _);

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
    }

    [Fact]
    public void Deposit_OtherCassetteJammed_IsAccepted()
    {
        var result = Deposit(0, 0, new(0, 0, 4), Denomination.Fifty, new(0, 1, 1), out var dispenser);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(30, 0)), result);
        Assert.Equal(new NoteCounts(0, 1, 5), dispenser.Stock);
    }

    [Fact]
    public void Deposit_FullTenCassette_RefusesEverything()
    {
        var result = Deposit(0, 0, new(0, 0, 5), null, new(1, 1, 1), out var dispenser);

        AssertRefused(result, AtmStatus.CassetteFull, new(0, 0));
        Assert.Equal(new NoteCounts(0, 0, 5), dispenser.Stock);
    }

    [Fact]
    public void Deposit_FullFiftyCassette()
    {
        var result = Deposit(0, 0, new(5, 0, 0), null, new(1, 0, 0), out _);

        Assert.Equal(AtmStatus.CassetteFull, result.Status);
    }

    // ---- Divers ----

    [Fact]
    public void Constructor_NullDispenser_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CashMachine(null!));
    }

    [Fact]
    public void Execute_NullOperation_Throws()
    {
        var machine = new CashMachine(new MoneyDispenser(Full));

        Assert.Throws<ArgumentNullException>(() => machine.Execute(new(0, 0), null!));
    }

    private sealed record UnknownOperation : Operation;

    [Fact]
    public void Execute_UnknownOperation_Throws()
    {
        var machine = new CashMachine(new MoneyDispenser(Full));

        Assert.Throws<ArgumentException>(() => machine.Execute(new(0, 0), new UnknownOperation()));
    }
}
