namespace Atm.Tests;

public class CashMachineTests
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

    private static void AssertRefused(AtmResult result, AtmStatus status, AccountState account,
        MoneyDispenser dispenser, NoteCounts stock)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
    }

    // ---- Exemples de la spécification ----

    [Fact]
    public void Example_Withdraw60_WithOnlyTwenties()
    {
        var stock = new NoteCounts(1, 3, 0);
        var result = Withdraw(100, 0, stock, null, 60, out var d);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(0, 3, 0), 0, new AccountState(40, 60)), result);
        Assert.Equal(new NoteCounts(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Example_Withdraw80_OneOfEach()
    {
        var result = Withdraw(100, 0, new NoteCounts(1, 1, 1), null, 80, out var d);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(1, 1, 1), 0, new AccountState(20, 80)), result);
        Assert.Equal(NoteCounts.None, d.Stock);
    }

    [Fact]
    public void Example_Withdraw100_NoFee()
    {
        var result = Withdraw(105, 0, Full, null, 100, out _);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(2, 0, 0), 0, new AccountState(5, 100)), result);
    }

    [Fact]
    public void Example_Withdraw110_WithFee()
    {
        var result = Withdraw(115, 0, Full, null, 110, out var d);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(2, 0, 1), 5, new AccountState(0, 110)), result);
        Assert.Equal(new NoteCounts(3, 5, 4), d.Stock);
    }

    [Fact]
    public void Example_Withdraw110_FeeMakesFundsInsufficient()
    {
        var result = Withdraw(110, 0, Full, null, 110, out var d);
        AssertRefused(result, AtmStatus.InsufficientFunds, new AccountState(110, 0), d, Full);
    }

    [Fact]
    public void Example_DailyLimitExceeded()
    {
        var result = Withdraw(250, 150, Full, null, 60, out var d);
        AssertRefused(result, AtmStatus.DailyLimitExceeded, new AccountState(250, 150), d, Full);
    }

    [Fact]
    public void Example_JammedCassetteCountsAsZero_CannotDispense()
    {
        var stock = new NoteCounts(1, 0, 0);
        var result = Withdraw(250, 0, stock, Denomination.Fifty, 50, out var d);
        AssertRefused(result, AtmStatus.CannotDispense, new AccountState(250, 0), d, stock);
    }

    [Fact]
    public void Example_JammedFifties_UsesTwenties()
    {
        var result = Withdraw(250, 0, new NoteCounts(2, 2, 0), Denomination.Fifty, 40, out var d);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(0, 2, 0), 0, new AccountState(210, 40)), result);
        Assert.Equal(new NoteCounts(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Example_Withdraw5_InvalidAmount()
    {
        var result = Withdraw(0, 200, NoteCounts.None, null, 5, out var d);
        AssertRefused(result, AtmStatus.InvalidAmount, new AccountState(0, 200), d, NoteCounts.None);
    }

    [Fact]
    public void Example_Deposit_CassetteFull()
    {
        var stock = new NoteCounts(0, 5, 0);
        var result = Deposit(10, 0, stock, null, new NoteCounts(0, 1, 0), out var d);
        AssertRefused(result, AtmStatus.CassetteFull, new AccountState(10, 0), d, stock);
    }

    [Fact]
    public void Example_Deposit_CassetteJammed()
    {
        var stock = new NoteCounts(0, 1, 0);
        var result = Deposit(10, 30, stock, Denomination.Twenty, new NoteCounts(0, 1, 0), out var d);
        AssertRefused(result, AtmStatus.CassetteJammed, new AccountState(10, 30), d, stock);
    }

    [Fact]
    public void Example_Deposit_Ok()
    {
        var result = Deposit(10, 30, new NoteCounts(1, 1, 0), null, new NoteCounts(1, 0, 1), out var d);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(70, 30)), result);
        Assert.Equal(new NoteCounts(2, 1, 1), d.Stock);
    }

    // ---- Retrait : contrôles ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void Withdraw_InvalidAmount(int amount)
    {
        var result = Withdraw(250, 0, Full, null, amount, out var d);
        AssertRefused(result, AtmStatus.InvalidAmount, new AccountState(250, 0), d, Full);
    }

    [Fact]
    public void Withdraw_InvalidAmount_CheckedBeforeDailyLimit()
    {
        var result = Withdraw(0, 200, Full, null, -10, out _);
        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void Withdraw_DailyLimit_ReachedExactly_IsAllowed()
    {
        var result = Withdraw(250, 100, Full, null, 100, out _);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(150, 200), result.Account);
    }

    [Fact]
    public void Withdraw_DailyLimit_CheckedBeforeFunds()
    {
        var result = Withdraw(0, 0, Full, null, 210, out _);
        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void Withdraw_FeeDoesNotCountTowardDailyLimit()
    {
        var result = Withdraw(250, 0, Full, null, 200, out _);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(45, 200), result.Account);
    }

    [Fact]
    public void Withdraw_FundsCheckedBeforeDispensability()
    {
        var result = Withdraw(10, 0, NoteCounts.None, null, 20, out _);
        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    [Fact]
    public void Withdraw_ExactBalance_IsAllowed()
    {
        var result = Withdraw(60, 0, Full, null, 60, out _);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(0, 60), result.Account);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Withdraw_CannotDispense_WhenNoCombination(Denomination? jammed)
    {
        var stock = new NoteCounts(1, 0, 1);
        var result = Withdraw(250, 0, stock, jammed, 30, out var d);
        AssertRefused(result, AtmStatus.CannotDispense, new AccountState(250, 0), d, stock);
    }

    [Fact]
    public void Withdraw_JammedTens_IgnoresThem()
    {
        var result = Withdraw(250, 0, new NoteCounts(0, 3, 5), Denomination.Ten, 60, out var d);

        Assert.Equal(new NoteCounts(0, 3, 0), result.Notes);
        Assert.Equal(new NoteCounts(0, 0, 5), d.Stock);
    }

    [Fact]
    public void Withdraw_JammedTwenties_IgnoresThem()
    {
        var result = Withdraw(250, 0, new NoteCounts(1, 5, 2), Denomination.Twenty, 70, out var d);

        Assert.Equal(new NoteCounts(1, 0, 2), result.Notes);
        Assert.Equal(new NoteCounts(0, 5, 0), d.Stock);
    }

    [Fact]
    public void Withdraw_ChoosesFewestNotes_NotGreedy()
    {
        // Glouton : 50 puis bloqué sur 10 ; la seule solution est 20×3.
        var result = Withdraw(250, 0, new NoteCounts(1, 3, 0), null, 60, out _);
        Assert.Equal(new NoteCounts(0, 3, 0), result.Notes);
    }

    [Fact]
    public void Withdraw_ChoosesFewestNotes_AmongSeveral()
    {
        // 80 = 50+20+10 (3) plutôt que 20×4 (4) ou 50+10×3 (4).
        var result = Withdraw(250, 0, Full, null, 80, out _);
        Assert.Equal(new NoteCounts(1, 1, 1), result.Notes);
    }

    // ---- Retrait : passages ----

    [Fact]
    public void Withdraw_ManyNotes_SplitIntoPassesOfAtMostFour()
    {
        var fake = new RecordingDispenser(new NoteCounts(0, 5, 5));
        var result = new CashMachine(fake).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(0, 5, 5), result.Notes);
        Assert.Equal(new AccountState(95, 150), result.Account);
        Assert.All(fake.Passes, p => Assert.InRange(p.Total, 1, 4));
        Assert.Equal(3, fake.Passes.Count);
        Assert.Equal(new NoteCounts(0, 5, 5), fake.Passes.Aggregate(NoteCounts.None, (a, b) => a + b));
    }

    [Fact]
    public void Withdraw_WithRealDispenser_LargeWithdrawal()
    {
        var result = Withdraw(250, 0, new NoteCounts(1, 5, 5), null, 200, out var d);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(1, 5, 5), result.Notes);
        Assert.Equal(NoteCounts.None, d.Stock);
    }

    // ---- Pannes ----

    [Fact]
    public void Withdraw_FaultOnFirstPass_NoNotes()
    {
        var fake = new RecordingDispenser(Full) { FailDispenseOnPass = 1 };
        var account = new AccountState(250, 0);
        var result = new CashMachine(fake).Execute(account, new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
    }

    [Fact]
    public void Withdraw_FaultOnLaterPass_ReturnsAlreadyDispensedNotes()
    {
        var fake = new RecordingDispenser(new NoteCounts(0, 5, 5)) { FailDispenseOnPass = 2 };
        var account = new AccountState(250, 0);
        var result = new CashMachine(fake).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(fake.Passes[0], result.Notes);
        Assert.Equal(4, result.Notes.Total);
        Assert.Equal(2, fake.Passes.Count);
    }

    [Fact]
    public void Deposit_Fault_NothingChanges()
    {
        var fake = new RecordingDispenser(NoteCounts.None) { FailLoad = true };
        var account = new AccountState(10, 30);
        var result = new CashMachine(fake).Execute(account, new Operation.Deposit(new NoteCounts(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
    }

    // ---- Dépôt ----

    [Fact]
    public void Deposit_Empty_InvalidAmount()
    {
        var result = Deposit(10, 0, NoteCounts.None, Denomination.Ten, NoteCounts.None, out var d);
        AssertRefused(result, AtmStatus.InvalidAmount, new AccountState(10, 0), d, NoteCounts.None);
    }

    [Fact]
    public void Deposit_Jammed_CheckedBeforeFull()
    {
        var stock = new NoteCounts(5, 0, 0);
        var result = Deposit(10, 0, stock, Denomination.Fifty, new NoteCounts(1, 0, 0), out var d);
        AssertRefused(result, AtmStatus.CassetteJammed, new AccountState(10, 0), d, stock);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)]
    public void Deposit_CassetteFull(int s50, int s20, int s10, int n50, int n20, int n10)
    {
        var stock = new NoteCounts(s50, s20, s10);
        var result = Deposit(10, 0, stock, null, new NoteCounts(n50, n20, n10), out var d);
        AssertRefused(result, AtmStatus.CassetteFull, new AccountState(10, 0), d, stock);
    }

    [Fact]
    public void Deposit_WithOtherCassetteJammed_IsAccepted()
    {
        var result = Deposit(0, 50, new NoteCounts(0, 0, 4), Denomination.Fifty, new NoteCounts(0, 1, 1), out var d);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(30, 50)), result);
        Assert.Equal(new NoteCounts(0, 1, 5), d.Stock);
    }

    // ---- Divers ----

    [Fact]
    public void UnknownOperation_Throws()
    {
        var machine = new CashMachine(new MoneyDispenser(Full));
        Assert.Throws<ArgumentOutOfRangeException>(() => machine.Execute(new AccountState(0, 0), null!));
    }

    [Fact]
    public void Machine_IsStateless_BetweenCalls()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(100, 0);

        var first = machine.Execute(account, new Operation.Withdraw(50));
        var second = machine.Execute(account, new Operation.Withdraw(50));

        Assert.Equal(first, second);
        Assert.Equal(new NoteCounts(3, 5, 5), dispenser.Stock);
    }

    // ---- Propriétés sur tout le domaine du stock ----

    public static IEnumerable<object?[]> StockDomain()
    {
        Denomination?[] jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];
        foreach (var jam in jams)
        {
            for (var f = 0; f <= 5; f++)
            for (var t = 0; t <= 5; t++)
            for (var c = 0; c <= 5; c++)
            {
                yield return [jam, f, t, c];
            }
        }
    }

    [Fact]
    public void Withdraw_AllStocks_MatchesBruteForceOracle()
    {
        foreach (var row in StockDomain())
        {
            var jam = (Denomination?)row[0];
            var stock = new NoteCounts((int)row[1]!, (int)row[2]!, (int)row[3]!);
            for (var amount = 10; amount <= 200; amount += 10)
            {
                var result = Withdraw(250, 0, stock, jam, amount, out var d);
                var expected = Oracle(amount, stock, jam);

                if (expected is { } notes)
                {
                    var fee = amount > 100 ? 5 : 0;
                    Assert.Equal(new AtmResult(AtmStatus.Ok, notes, fee, new AccountState(250 - amount - fee, amount)), result);
                    Assert.Equal(stock - notes, d.Stock);
                }
                else
                {
                    AssertRefused(result, AtmStatus.CannotDispense, new AccountState(250, 0), d, stock);
                }
            }
        }
    }

    private static NoteCounts? Oracle(int amount, NoteCounts stock, Denomination? jam)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var c = 0; c <= 5; c++)
        {
            var n = new NoteCounts(f, t, c);
            var ok = n.Value == amount && f <= stock.Fifties && t <= stock.Twenties && c <= stock.Tens
                     && (jam is not { } j || n.Of(j) == 0);
            if (ok && (best is null || n.Total < best.Value.Total))
            {
                best = n;
            }
        }

        return best;
    }

    private sealed class RecordingDispenser(NoteCounts stock) : IMoneyDispenser
    {
        public NoteCounts Stock { get; private set; } = stock;
        public Denomination? Jammed => null;
        public List<NoteCounts> Passes { get; } = [];
        public int FailDispenseOnPass { get; init; }
        public bool FailLoad { get; init; }

        public bool Dispense(NoteCounts notes)
        {
            Passes.Add(notes);
            if (Passes.Count == FailDispenseOnPass || notes.Total is < 1 or > 4)
            {
                return false;
            }

            Stock -= notes;
            return true;
        }

        public bool Load(NoteCounts notes)
        {
            if (FailLoad)
            {
                return false;
            }

            Stock += notes;
            return true;
        }
    }
}
