namespace Atm.Tests;

public class CashMachineTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static AccountState A(int balance, int withdrawn) => new(balance, withdrawn);

    private static (AtmResult Result, MoneyDispenser Dispenser) Run(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, operation);
        return (result, dispenser);
    }

    // ---- Exemples de la spécification -------------------------------------------------------------

    [Fact]
    public void Example_Withdraw60_WithTwenties()
    {
        var (r, d) = Run(A(100, 0), N(1, 3, 0), null, new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, A(40, 60)), r);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Example_Withdraw80_OneOfEach()
    {
        var (r, d) = Run(A(100, 0), N(1, 1, 1), null, new Operation.Withdraw(80));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, A(20, 80)), r);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Example_Withdraw100_NoFee()
    {
        var (r, _) = Run(A(105, 0), N(5, 5, 5), null, new Operation.Withdraw(100));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, A(5, 100)), r);
    }

    [Fact]
    public void Example_Withdraw110_WithFee()
    {
        var (r, d) = Run(A(115, 0), N(5, 5, 5), null, new Operation.Withdraw(110));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, A(0, 110)), r);
        Assert.Equal(N(3, 5, 4), d.Stock);
    }

    [Fact]
    public void Example_Withdraw110_FeeMakesFundsInsufficient()
    {
        var (r, d) = Run(A(110, 0), N(5, 5, 5), null, new Operation.Withdraw(110));
        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, A(110, 0)), r);
        Assert.Equal(N(5, 5, 5), d.Stock);
    }

    [Fact]
    public void Example_DailyLimitExceeded()
    {
        var (r, _) = Run(A(250, 150), N(5, 5, 5), null, new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, A(250, 150)), r);
    }

    [Fact]
    public void Example_JammedFifty_CannotDispense()
    {
        var (r, d) = Run(A(250, 0), N(1, 0, 0), Denomination.Fifty, new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, A(250, 0)), r);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Example_JammedFifty_UsesTwenties()
    {
        var (r, d) = Run(A(250, 0), N(2, 2, 0), Denomination.Fifty, new Operation.Withdraw(40));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, A(210, 40)), r);
        Assert.Equal(N(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Example_Withdraw5_InvalidAmount()
    {
        var (r, _) = Run(A(0, 200), N(0, 0, 0), null, new Operation.Withdraw(5));
        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, A(0, 200)), r);
    }

    [Fact]
    public void Example_Deposit_CassetteFull()
    {
        var (r, d) = Run(A(10, 0), N(0, 5, 0), null, new Operation.Deposit(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, A(10, 0)), r);
        Assert.Equal(N(0, 5, 0), d.Stock);
    }

    [Fact]
    public void Example_Deposit_CassetteJammed()
    {
        var (r, d) = Run(A(10, 30), N(0, 1, 0), Denomination.Twenty, new Operation.Deposit(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, A(10, 30)), r);
        Assert.Equal(N(0, 1, 0), d.Stock);
    }

    [Fact]
    public void Example_Deposit_Ok()
    {
        var (r, d) = Run(A(10, 30), N(1, 1, 0), null, new Operation.Deposit(N(1, 0, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(70, 30)), r);
        Assert.Equal(N(2, 1, 1), d.Stock);
    }

    // ---- Retrait : contrôles et ordre -------------------------------------------------------------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void Withdraw_InvalidAmount(int amount)
    {
        var (r, _) = Run(A(250, 0), N(5, 5, 5), null, new Operation.Withdraw(amount));
        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    [Fact]
    public void Withdraw_InvalidAmount_TakesPrecedenceOverDailyLimit()
    {
        var (r, _) = Run(A(0, 200), N(0, 0, 0), null, new Operation.Withdraw(-10));
        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    [Fact]
    public void Withdraw_DailyLimit_TakesPrecedenceOverFunds()
    {
        var (r, _) = Run(A(0, 0), N(0, 0, 0), null, new Operation.Withdraw(210));
        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    [Fact]
    public void Withdraw_ExactlyDailyLimit_IsAllowed_AndFeeNotCounted()
    {
        var (r, _) = Run(A(250, 90), N(5, 5, 5), null, new Operation.Withdraw(110));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, A(135, 200)), r);
    }

    [Fact]
    public void Withdraw_InsufficientFunds_TakesPrecedenceOverCannotDispense()
    {
        var (r, _) = Run(A(10, 0), N(0, 0, 0), null, new Operation.Withdraw(20));
        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    [Fact]
    public void Withdraw_ExactBalanceWithFee_IsAllowed()
    {
        var (r, _) = Run(A(205, 0), N(5, 5, 5), null, new Operation.Withdraw(200));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(4, 0, 0), 5, A(0, 200)), r);
    }

    [Fact]
    public void Withdraw_FewestNotes_PrefersFiftyAndTenOverThreeTwenties()
    {
        // 60 : 50+10 (2 billets) l'emporte sur 20×3 (3 billets).
        var (r, _) = Run(A(250, 0), N(1, 3, 1), null, new Operation.Withdraw(60));
        Assert.Equal(N(1, 0, 1), r.Notes);
    }

    [Fact]
    public void Withdraw_GreedyWouldFail_ButCombinationExists()
    {
        // 60 sans billet de 10 : 50 ne laisse pas de reste payable, il faut 20×3.
        var (r, _) = Run(A(250, 0), N(5, 5, 0), null, new Operation.Withdraw(60));
        Assert.Equal(N(0, 3, 0), r.Notes);
    }

    [Fact]
    public void Withdraw_ManyNotes_AreSplitIntoPassesOfAtMostFour()
    {
        var dispenser = new FaultyDispenser(N(0, 5, 5));
        var r = new CashMachine(dispenser).Execute(A(250, 0), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 5, 5), 5, A(95, 150)), r);
        Assert.All(dispenser.Dispensed, p => Assert.InRange(p.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(N(0, 5, 5), dispenser.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_JammedTens_CannotDispense()
    {
        var (r, _) = Run(A(250, 0), N(5, 5, 5), Denomination.Ten, new Operation.Withdraw(10));
        Assert.Equal(AtmStatus.CannotDispense, r.Status);
    }

    [Fact]
    public void Withdraw_JammedTwenty_UsesTens()
    {
        var (r, _) = Run(A(250, 0), N(0, 5, 5), Denomination.Twenty, new Operation.Withdraw(40));
        Assert.Equal(N(0, 0, 4), r.Notes);
    }

    // ---- Dépôt ------------------------------------------------------------------------------------

    [Fact]
    public void Deposit_Empty_InvalidAmount()
    {
        var (r, _) = Run(A(10, 0), N(0, 0, 0), null, new Operation.Deposit(NoteCounts.None));
        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, A(10, 0)), r);
    }

    [Fact]
    public void Deposit_NegativeNotes_InvalidAmount()
    {
        var (r, _) = Run(A(10, 0), N(1, 1, 1), null, new Operation.Deposit(N(-1, 1, 1)));
        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    [Fact]
    public void Deposit_Jammed_TakesPrecedenceOverFull()
    {
        var (r, _) = Run(A(10, 0), N(5, 5, 5), Denomination.Ten, new Operation.Deposit(N(1, 1, 1)));
        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Fact]
    public void Deposit_AllDenominations_Ok()
    {
        var (r, d) = Run(A(0, 200), N(4, 4, 4), null, new Operation.Deposit(N(1, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(80, 200)), r);
        Assert.Equal(N(5, 5, 5), d.Stock);
    }

    // ---- Pannes du module -------------------------------------------------------------------------

    [Fact]
    public void Withdraw_DispenserFailsOnFirstPass_NothingDispensed()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5)) { FailDispenseAt = 0 };
        var r = new CashMachine(dispenser).Execute(A(250, 0), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, A(250, 0)), r);
        Assert.Single(dispenser.Dispensed);
    }

    [Fact]
    public void Withdraw_DispenserFailsOnSecondPass_ReturnsAlreadyDispensedNotes()
    {
        var dispenser = new FaultyDispenser(N(0, 5, 5)) { FailDispenseAt = 1 };
        var r = new CashMachine(dispenser).Execute(A(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(A(250, 0), r.Account);
        Assert.Equal(4, r.Notes.Total);
        Assert.Equal(N(0, 5, 5) - r.Notes, dispenser.Stock);
        Assert.Equal(2, dispenser.Dispensed.Count);
    }

    [Fact]
    public void Deposit_LoadFails_DispenserFault()
    {
        var dispenser = new FaultyDispenser(N(1, 1, 1)) { FailLoad = true };
        var r = new CashMachine(dispenser).Execute(A(10, 30), new Operation.Deposit(N(1, 0, 0)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, A(10, 30)), r);
        Assert.Single(dispenser.Loaded);
    }

    [Fact]
    public void Refusal_DoesNotTouchDispenser()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        machine.Execute(A(0, 0), new Operation.Withdraw(50));
        machine.Execute(A(0, 0), new Operation.Deposit(N(1, 0, 0)));

        Assert.Empty(dispenser.Dispensed);
        Assert.Empty(dispenser.Loaded);
    }

    [Fact]
    public void UnknownOperation_Throws()
    {
        var machine = new CashMachine(new MoneyDispenser(N(0, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => machine.Execute(A(0, 0), null!));
    }

    // ---- Comparaison avec le modèle de référence, sur une large part du domaine -------------------

    public static TheoryData<Denomination?> Jams => new() { null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten };

    [Theory]
    [MemberData(nameof(Jams))]
    public void MatchesOracle_AcrossDomain(Denomination? jammed)
    {
        int[] balances = [0, 5, 10, 45, 50, 55, 100, 105, 110, 115, 150, 200, 205, 250];
        int[] withdrawnValues = [0, 10, 50, 100, 150, 190, 200];
        var amounts = new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));
        var operations = amounts.Select(a => (Operation)new Operation.Withdraw(a))
            .Concat(from f in new[] { 0, 1 } from t in new[] { 0, 1 } from c in new[] { 0, 1 }
                    select (Operation)new Operation.Deposit(N(f, t, c)))
            .ToArray();

        var failures = new List<string>();
        for (var sf = 0; sf <= 5; sf++)
        for (var st = 0; st <= 5; st++)
        for (var sc = 0; sc <= 5; sc++)
        foreach (var balance in balances)
        foreach (var withdrawn in withdrawnValues)
        foreach (var operation in operations)
        {
            var stock = N(sf, st, sc);
            var account = A(balance, withdrawn);
            var dispenser = new MoneyDispenser(stock, jammed);
            var actual = new CashMachine(dispenser).Execute(account, operation);
            var expected = Oracle.Expected(account, stock, jammed, operation);

            var expectedStock = actual.Status != AtmStatus.Ok ? stock
                : operation is Operation.Deposit(var deposited) ? stock + deposited
                : stock - expected.Notes;

            if (actual != expected || dispenser.Stock != expectedStock)
            {
                failures.Add($"{account} {stock} {jammed} {operation}: attendu {expected}, obtenu {actual}");
                if (failures.Count > 10) break;
            }
        }

        Assert.Empty(failures);
    }
}
