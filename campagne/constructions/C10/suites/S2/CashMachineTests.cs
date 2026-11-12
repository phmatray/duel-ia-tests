namespace Atm.Tests;

public class CashMachineTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static AccountState A(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    private static (AtmResult Result, MoneyDispenser Dispenser) Run(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, operation);

        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var (result, dispenser) = Run(account, stock, jammed, operation);

        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
        Assert.Equal(stock, dispenser.Stock);
    }

    // ---------- Exemples de la spécification ----------

    [Fact]
    public void Spec_Withdraw60_PrefersThreeTwentiesOverUnreachableFifty()
    {
        var (result, dispenser) = Run(A(100, 0), N(1, 3, 0), null, new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, A(40, 60)), result);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Spec_Withdraw80_UsesOneOfEach()
    {
        var (result, dispenser) = Run(A(100, 0), N(1, 1, 1), null, new Operation.Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, A(20, 80)), result);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Spec_Withdraw100_HasNoFee()
    {
        var (result, _) = Run(A(105, 0), N(5, 5, 5), null, new Operation.Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, A(5, 100)), result);
    }

    [Fact]
    public void Spec_Withdraw110_ChargesFee()
    {
        var (result, dispenser) = Run(A(115, 0), N(5, 5, 5), null, new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, A(0, 110)), result);
        Assert.Equal(N(3, 5, 4), dispenser.Stock);
    }

    [Fact]
    public void Spec_Withdraw110_FeeMakesFundsInsufficient() =>
        AssertRefused(AtmStatus.InsufficientFunds, A(110, 0), N(5, 5, 5), null, new Operation.Withdraw(110));

    [Fact]
    public void Spec_Withdraw60_ExceedsDailyLimit() =>
        AssertRefused(AtmStatus.DailyLimitExceeded, A(250, 150), N(5, 5, 5), null, new Operation.Withdraw(60));

    [Fact]
    public void Spec_Withdraw50_OnlyFiftyIsJammed() =>
        AssertRefused(AtmStatus.CannotDispense, A(250, 0), N(1, 0, 0), Denomination.Fifty, new Operation.Withdraw(50));

    [Fact]
    public void Spec_Withdraw40_AvoidsJammedCassette()
    {
        var (result, dispenser) = Run(A(250, 0), N(2, 2, 0), Denomination.Fifty, new Operation.Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, A(210, 40)), result);
        Assert.Equal(N(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Spec_Withdraw5_IsInvalid() =>
        AssertRefused(AtmStatus.InvalidAmount, A(0, 200), N(0, 0, 0), null, new Operation.Withdraw(5));

    [Fact]
    public void Spec_Deposit_CassetteFull() =>
        AssertRefused(AtmStatus.CassetteFull, A(10, 0), N(0, 5, 0), null, new Operation.Deposit(N(0, 1, 0)));

    [Fact]
    public void Spec_Deposit_CassetteJammed() =>
        AssertRefused(AtmStatus.CassetteJammed, A(10, 30), N(0, 1, 0), Denomination.Twenty, new Operation.Deposit(N(0, 1, 0)));

    [Fact]
    public void Spec_Deposit_Ok()
    {
        var (result, dispenser) = Run(A(10, 30), N(1, 1, 0), null, new Operation.Deposit(N(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(70, 30)), result);
        Assert.Equal(N(2, 1, 1), dispenser.Stock);
    }

    // ---------- Retrait : contrôles et ordre ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Withdraw_InvalidAmount(int amount) =>
        AssertRefused(AtmStatus.InvalidAmount, A(250, 0), N(5, 5, 5), null, new Operation.Withdraw(amount));

    [Fact]
    public void Withdraw_InvalidAmount_CheckedBeforeDailyLimit() =>
        AssertRefused(AtmStatus.InvalidAmount, A(250, 200), N(5, 5, 5), null, new Operation.Withdraw(-10));

    [Fact]
    public void Withdraw_DailyLimit_CheckedBeforeFunds() =>
        AssertRefused(AtmStatus.DailyLimitExceeded, A(0, 0), N(5, 5, 5), null, new Operation.Withdraw(210));

    [Fact]
    public void Withdraw_Funds_CheckedBeforeDispensability() =>
        AssertRefused(AtmStatus.InsufficientFunds, A(20, 0), N(0, 0, 0), null, new Operation.Withdraw(30));

    [Fact]
    public void Withdraw_ExactlyDailyLimit_IsAllowed()
    {
        var (result, _) = Run(A(250, 0), N(5, 5, 5), null, new Operation.Withdraw(200));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(4, 0, 0), 5, A(45, 200)), result);
    }

    [Fact]
    public void Withdraw_DailyLimitIgnoresFee()
    {
        var (result, _) = Run(A(250, 90), N(5, 5, 5), null, new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, A(135, 200)), result);
    }

    [Fact]
    public void Withdraw_ExactBalanceWithFee_IsAllowed()
    {
        var (result, _) = Run(A(125, 0), N(5, 5, 5), null, new Operation.Withdraw(120));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 1, 0), 5, A(0, 120)), result);
    }

    [Fact]
    public void Withdraw_CannotDispense_WhenStockTooSmall() =>
        AssertRefused(AtmStatus.CannotDispense, A(250, 0), N(0, 1, 0), null, new Operation.Withdraw(30));

    [Fact]
    public void Withdraw_CannotDispense_WhenTensJammed() =>
        AssertRefused(AtmStatus.CannotDispense, A(250, 0), N(1, 1, 5), Denomination.Ten, new Operation.Withdraw(10));

    [Fact]
    public void Withdraw_AvoidsJammedTwenties()
    {
        var (result, _) = Run(A(250, 0), N(1, 5, 5), Denomination.Twenty, new Operation.Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 0, 4), 0, A(210, 40)), result);
    }

    [Fact]
    public void Withdraw_ManyNotes_SplitsIntoPassesOfAtMostFour()
    {
        var recorder = new RecordingDispenser(new MoneyDispenser(N(0, 5, 5)));
        var result = new CashMachine(recorder).Execute(A(250, 0), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 5, 5), 5, A(95, 150)), result);
        Assert.All(recorder.Dispensed, pass => Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(3, recorder.Dispensed.Count);
        Assert.Equal(N(0, 5, 5), recorder.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(N(0, 0, 0), recorder.Stock);
    }

    [Fact]
    public void Refusals_NeverTouchTheDispenser()
    {
        var recorder = new RecordingDispenser(new MoneyDispenser(N(0, 0, 0)));
        var machine = new CashMachine(recorder);

        machine.Execute(A(250, 0), new Operation.Withdraw(5));
        machine.Execute(A(250, 200), new Operation.Withdraw(10));
        machine.Execute(A(0, 0), new Operation.Withdraw(10));
        machine.Execute(A(250, 0), new Operation.Withdraw(10));
        machine.Execute(A(250, 0), new Operation.Deposit(NoteCounts.None));

        Assert.Empty(recorder.Dispensed);
        Assert.Empty(recorder.Loaded);
    }

    /// <summary>
    /// Parcourt tous les stocks, cassettes bloquées et montants valides avec un compte suffisant :
    /// le retrait réussit si et seulement si une combinaison existe, et rend alors la plus courte.
    /// </summary>
    [Fact]
    public void Withdraw_AllStocks_ReturnsFewestNotesCombination()
    {
        Denomination?[] jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];
        foreach (var jammed in jams)
        foreach (var stock in AllStocks())
        for (var amount = 10; amount <= 200; amount += 10)
        {
            var account = A(250, 0);
            var (result, dispenser) = Run(account, stock, jammed, new Operation.Withdraw(amount));
            var expected = Reference(amount, stock, jammed);

            if (expected is { } notes)
            {
                var fee = amount > 100 ? 5 : 0;
                Assert.Equal(new AtmResult(AtmStatus.Ok, notes, fee, A(250 - amount - fee, amount)), result);
                Assert.Equal(stock - notes, dispenser.Stock);
            }
            else
            {
                Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, account), result);
                Assert.Equal(stock, dispenser.Stock);
            }
        }
    }

    // ---------- Dépôt ----------

    [Fact]
    public void Deposit_Empty_IsInvalid() =>
        AssertRefused(AtmStatus.InvalidAmount, A(10, 0), N(0, 0, 0), Denomination.Ten, new Operation.Deposit(NoteCounts.None));

    [Fact]
    public void Deposit_Jammed_CheckedBeforeFull() =>
        AssertRefused(AtmStatus.CassetteJammed, A(10, 0), N(5, 5, 5), Denomination.Fifty, new Operation.Deposit(N(1, 1, 1)));

    [Fact]
    public void Deposit_OtherCassetteJammed_IsAccepted()
    {
        var (result, dispenser) = Run(A(0, 200), N(0, 0, 4), Denomination.Fifty, new Operation.Deposit(N(0, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(30, 200)), result);
        Assert.Equal(N(0, 1, 5), dispenser.Stock);
    }

    [Fact]
    public void Deposit_FullTens_IsRefused() =>
        AssertRefused(AtmStatus.CassetteFull, A(10, 0), N(0, 0, 5), null, new Operation.Deposit(N(1, 1, 1)));

    // ---------- Pannes du module ----------

    [Fact]
    public void Withdraw_DispenserFailsOnSecondPass_ReturnsNotesAlreadyOut()
    {
        var faulty = new FaultyDispenser(N(0, 5, 5), failDispenseOnCall: 2);
        var account = A(250, 0);

        var result = new CashMachine(faulty).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(faulty.Dispensed.Single(), result.Notes);
        Assert.Equal(4, result.Notes.Total);
    }

    [Fact]
    public void Withdraw_DispenserFailsOnFirstPass_ReturnsNoNotes()
    {
        var faulty = new FaultyDispenser(N(5, 5, 5), failDispenseOnCall: 1);
        var account = A(250, 0);

        var result = new CashMachine(faulty).Execute(account, new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
    }

    [Fact]
    public void Deposit_DispenserFails_ReturnsFault()
    {
        var faulty = new FaultyDispenser(N(0, 0, 0), failLoad: true);
        var account = A(10, 0);

        var result = new CashMachine(faulty).Execute(account, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
    }

    [Fact]
    public void UnknownOperation_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            new CashMachine(new MoneyDispenser(N(0, 0, 0))).Execute(A(0, 0), new UnknownOperation()));

    // ---------- Outils ----------

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
        {
            yield return N(f, t, d);
        }
    }

    /// <summary>Modèle de référence naïf : énumère toutes les combinaisons du stock disponible.</summary>
    private static NoteCounts? Reference(int amount, NoteCounts stock, Denomination? jammed)
    {
        var candidates = AllStocks()
            .Where(c => c.Value == amount)
            .Where(c => c.Fifties <= stock.Fifties && c.Twenties <= stock.Twenties && c.Tens <= stock.Tens)
            .Where(c => jammed is not { } j || c.Of(j) == 0)
            .OrderBy(c => c.Total)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0];
    }

    private sealed record UnknownOperation : Operation;

    private sealed class RecordingDispenser(IMoneyDispenser inner) : IMoneyDispenser
    {
        public List<NoteCounts> Dispensed { get; } = [];

        public List<NoteCounts> Loaded { get; } = [];

        public NoteCounts Stock => inner.Stock;

        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            Dispensed.Add(notes);
            return inner.Dispense(notes);
        }

        public bool Load(NoteCounts notes)
        {
            Loaded.Add(notes);
            return inner.Load(notes);
        }
    }

    private sealed class FaultyDispenser(NoteCounts stock, int failDispenseOnCall = 0, bool failLoad = false) : IMoneyDispenser
    {
        private int _dispenseCalls;

        public List<NoteCounts> Dispensed { get; } = [];

        public NoteCounts Stock { get; private set; } = stock;

        public Denomination? Jammed => null;

        public bool Dispense(NoteCounts notes)
        {
            if (++_dispenseCalls == failDispenseOnCall)
            {
                return false;
            }

            Dispensed.Add(notes);
            Stock -= notes;
            return true;
        }

        public bool Load(NoteCounts notes)
        {
            if (failLoad)
            {
                return false;
            }

            Stock += notes;
            return true;
        }
    }
}
