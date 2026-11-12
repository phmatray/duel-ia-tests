namespace Atm.Tests;

public class CashMachineTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static readonly NoteCounts Full = N(5, 5, 5);

    private static (AtmResult Result, MoneyDispenser Dispenser) Run(
        int balance, int withdrawn, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), operation);

        return (result, dispenser);
    }

    private static void AssertRefused(AtmResult result, AtmStatus status, AccountState account, MoneyDispenser dispenser, NoteCounts stock)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
    }

    // ---- Exemples de la spécification ----

    [Theory]
    [InlineData(100, 0, 1, 3, 0, null, 60, 0, 3, 0, 0, 40, 60)]
    [InlineData(100, 0, 1, 1, 1, null, 80, 1, 1, 1, 0, 20, 80)]
    [InlineData(105, 0, 5, 5, 5, null, 100, 2, 0, 0, 0, 5, 100)]
    [InlineData(115, 0, 5, 5, 5, null, 110, 2, 0, 1, 5, 0, 110)]
    [InlineData(250, 0, 2, 2, 0, Denomination.Fifty, 40, 0, 2, 0, 0, 210, 40)]
    public void Withdraw_spec_examples_succeed(
        int balance, int withdrawn, int s50, int s20, int s10, Denomination? jammed, int amount,
        int n50, int n20, int n10, int fee, int expectedBalance, int expectedWithdrawn)
    {
        var stock = N(s50, s20, s10);
        var (result, dispenser) = Run(balance, withdrawn, stock, jammed, new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(n50, n20, n10), result.Notes);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(new AccountState(expectedBalance, expectedWithdrawn), result.Account);
        Assert.Equal(stock - result.Notes, dispenser.Stock);
    }

    [Theory]
    [InlineData(110, 0, 5, 5, 5, null, 110, AtmStatus.InsufficientFunds)]
    [InlineData(250, 150, 5, 5, 5, null, 60, AtmStatus.DailyLimitExceeded)]
    [InlineData(250, 0, 1, 0, 0, Denomination.Fifty, 50, AtmStatus.CannotDispense)]
    [InlineData(0, 200, 0, 0, 0, null, 5, AtmStatus.InvalidAmount)]
    public void Withdraw_spec_examples_refused(
        int balance, int withdrawn, int s50, int s20, int s10, Denomination? jammed, int amount, AtmStatus status)
    {
        var stock = N(s50, s20, s10);
        var (result, dispenser) = Run(balance, withdrawn, stock, jammed, new Operation.Withdraw(amount));

        AssertRefused(result, status, new AccountState(balance, withdrawn), dispenser, stock);
    }

    [Fact]
    public void Deposit_spec_example_cassette_full()
    {
        var (result, dispenser) = Run(10, 0, N(0, 5, 0), null, new Operation.Deposit(N(0, 1, 0)));

        AssertRefused(result, AtmStatus.CassetteFull, new AccountState(10, 0), dispenser, N(0, 5, 0));
    }

    [Fact]
    public void Deposit_spec_example_cassette_jammed()
    {
        var (result, dispenser) = Run(10, 30, N(0, 1, 0), Denomination.Twenty, new Operation.Deposit(N(0, 1, 0)));

        AssertRefused(result, AtmStatus.CassetteJammed, new AccountState(10, 30), dispenser, N(0, 1, 0));
    }

    [Fact]
    public void Deposit_spec_example_ok()
    {
        var (result, dispenser) = Run(10, 30, N(1, 1, 0), null, new Operation.Deposit(N(1, 0, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(70, 30), result.Account);
        Assert.Equal(N(2, 1, 1), dispenser.Stock);
    }

    // ---- Retrait : contrôles et ordre ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void Withdraw_invalid_amount(int amount)
    {
        var (result, dispenser) = Run(250, 0, Full, null, new Operation.Withdraw(amount));

        AssertRefused(result, AtmStatus.InvalidAmount, new AccountState(250, 0), dispenser, Full);
    }

    [Fact]
    public void Invalid_amount_is_checked_before_daily_limit()
    {
        var (result, _) = Run(0, 200, N(0, 0, 0), null, new Operation.Withdraw(-10));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Theory]
    [InlineData(0, 200, AtmStatus.Ok)]
    [InlineData(0, 210, AtmStatus.DailyLimitExceeded)]
    [InlineData(190, 10, AtmStatus.Ok)]
    [InlineData(190, 20, AtmStatus.DailyLimitExceeded)]
    [InlineData(200, 10, AtmStatus.DailyLimitExceeded)]
    public void Withdraw_daily_limit_boundary(int withdrawn, int amount, AtmStatus status)
    {
        var (result, _) = Run(250, withdrawn, Full, null, new Operation.Withdraw(amount));

        Assert.Equal(status, result.Status);
    }

    [Fact]
    public void Daily_limit_is_checked_before_funds_and_stock()
    {
        var (result, _) = Run(0, 200, N(0, 0, 0), null, new Operation.Withdraw(10));

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void Fee_does_not_count_in_daily_limit()
    {
        var (result, _) = Run(250, 0, Full, null, new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(45, 200), result.Account);
    }

    [Theory]
    [InlineData(100, 100, AtmStatus.Ok, 0)]
    [InlineData(110, 115, AtmStatus.Ok, 5)]
    [InlineData(110, 110, AtmStatus.InsufficientFunds, 0)]
    [InlineData(100, 95, AtmStatus.InsufficientFunds, 0)]
    public void Withdraw_fee_and_funds_boundary(int amount, int balance, AtmStatus status, int fee)
    {
        var (result, _) = Run(balance, 0, Full, null, new Operation.Withdraw(amount));

        Assert.Equal(status, result.Status);
        Assert.Equal(fee, result.Fee);
    }

    [Fact]
    public void Insufficient_funds_is_checked_before_stock()
    {
        var (result, _) = Run(10, 0, N(0, 0, 0), null, new Operation.Withdraw(20));

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    [Theory]
    [InlineData(0, 0, 0, null, 10)]
    [InlineData(0, 5, 0, null, 30)]
    [InlineData(1, 0, 0, null, 30)]
    [InlineData(5, 5, 5, Denomination.Ten, 10)]
    [InlineData(0, 3, 3, Denomination.Twenty, 40)]
    public void Withdraw_cannot_dispense(int s50, int s20, int s10, Denomination? jammed, int amount)
    {
        var stock = N(s50, s20, s10);
        var (result, dispenser) = Run(250, 0, stock, jammed, new Operation.Withdraw(amount));

        AssertRefused(result, AtmStatus.CannotDispense, new AccountState(250, 0), dispenser, stock);
    }

    [Theory]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // 50+10 impossible : 20×3
    [InlineData(5, 5, 5, 60, 1, 0, 1)]   // 50+10 bat 20×3
    [InlineData(5, 5, 5, 80, 1, 1, 1)]
    [InlineData(5, 5, 0, 130, 1, 4, 0)]  // pas de 10 : 50+20×4
    [InlineData(5, 5, 5, 130, 2, 1, 1)]
    [InlineData(0, 5, 5, 110, 0, 5, 1)]
    [InlineData(5, 0, 5, 40, 0, 0, 4)]
    public void Withdraw_uses_fewest_notes(int s50, int s20, int s10, int amount, int n50, int n20, int n10)
    {
        var (result, _) = Run(250, 0, N(s50, s20, s10), null, new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(n50, n20, n10), result.Notes);
    }

    [Fact]
    public void Withdraw_more_than_four_notes_uses_several_passes()
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(N(1, 5, 5)));
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(1, 5, 5), result.Notes);
        Assert.True(dispenser.Passes.Count >= 3);
        Assert.All(dispenser.Passes, pass => Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(result.Notes, dispenser.Passes.Aggregate(NoteCounts.None, (sum, pass) => sum + pass));
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    // ---- Dépôt : contrôles et ordre ----

    [Fact]
    public void Deposit_empty_is_invalid()
    {
        var (result, dispenser) = Run(10, 0, N(1, 1, 1), Denomination.Fifty, new Operation.Deposit(NoteCounts.None));

        AssertRefused(result, AtmStatus.InvalidAmount, new AccountState(10, 0), dispenser, N(1, 1, 1));
    }

    [Fact]
    public void Deposit_jammed_is_checked_before_full()
    {
        var (result, _) = Run(10, 0, N(5, 5, 5), Denomination.Ten, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
    }

    [Fact]
    public void Deposit_avoiding_jammed_cassette_is_accepted()
    {
        var (result, dispenser) = Run(0, 50, N(0, 0, 3), Denomination.Ten, new Operation.Deposit(N(1, 1, 0)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(70, 50), result.Account);
        Assert.Equal(N(1, 1, 3), dispenser.Stock);
    }

    [Fact]
    public void Deposit_filling_cassette_to_capacity_is_accepted()
    {
        var (result, dispenser) = Run(0, 0, N(4, 4, 4), null, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(80, 0), result.Account);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    // ---- Pannes du module ----

    [Fact]
    public void Withdraw_fault_on_first_pass_returns_no_notes()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5), failOnDispenseCall: 1);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(250, 0)), result);
    }

    [Fact]
    public void Withdraw_fault_on_later_pass_returns_notes_already_dispensed()
    {
        // 190 € = 50×3 20×2 : 5 billets, donc deux passages.
        var dispenser = new FaultyDispenser(N(5, 5, 5), failOnDispenseCall: 2);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(190));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
        Assert.Equal(dispenser.Dispensed, result.Notes);
        Assert.Equal(4, result.Notes.Total);
        Assert.Equal(2, dispenser.DispenseCalls);
    }

    [Fact]
    public void Deposit_fault_returns_no_notes_and_unchanged_account()
    {
        var dispenser = new FaultyDispenser(N(1, 1, 1), failOnDispenseCall: 0, failLoad: true);
        var result = new CashMachine(dispenser).Execute(new AccountState(10, 20), new Operation.Deposit(N(1, 0, 0)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(10, 20)), result);
    }

    // ---- Arguments ----

    [Fact]
    public void Constructor_rejects_null_dispenser() =>
        Assert.Throws<ArgumentNullException>(() => new CashMachine(null!));

    [Fact]
    public void Execute_rejects_null_operation() =>
        Assert.Throws<ArgumentNullException>(() =>
            new CashMachine(new MoneyDispenser(Full)).Execute(new AccountState(0, 0), null!));

    // ---- Parcours exhaustif du domaine contre un oracle indépendant ----

    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var a = 0; a <= 5; a++)
            for (var b = 0; b <= 5; b++)
                for (var c = 0; c <= 5; c++)
                    yield return N(a, b, c);
    }

    private static int[] DomainAmounts() => [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    /// <summary>Énumère toutes les combinaisons exactes ; vérifie au passage que la plus courte est unique.</summary>
    private static NoteCounts? Oracle(int amount, NoteCounts available)
    {
        var exact = AllStocks()
            .Where(n => n.Fifties <= available.Fifties && n.Twenties <= available.Twenties && n.Tens <= available.Tens)
            .Where(n => n.Value == amount)
            .ToList();
        if (exact.Count == 0)
        {
            return null;
        }

        var min = exact.Min(n => n.Total);
        var best = exact.Where(n => n.Total == min).ToList();
        Assert.Single(best);

        return best[0];
    }

    private static AtmStatus ExpectedWithdrawStatus(AccountState account, int amount, NoteCounts? combination)
    {
        var fee = amount > 100 ? 5 : 0;
        if (amount <= 0 || amount % 10 != 0) return AtmStatus.InvalidAmount;
        if (account.WithdrawnToday + amount > 200) return AtmStatus.DailyLimitExceeded;
        if (amount + fee > account.Balance) return AtmStatus.InsufficientFunds;
        if (combination is null) return AtmStatus.CannotDispense;
        return AtmStatus.Ok;
    }

    private static void CheckWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

        var combination = amount > 0 && amount % 10 == 0 ? Oracle(amount, Available(stock, jammed)) : null;
        var status = ExpectedWithdrawStatus(account, amount, combination);
        Assert.Equal(status, result.Status);

        if (status == AtmStatus.Ok)
        {
            var fee = amount > 100 ? 5 : 0;
            Assert.Equal(combination, result.Notes);
            Assert.Equal(fee, result.Fee);
            Assert.Equal(new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount), result.Account);
            Assert.Equal(stock - result.Notes, dispenser.Stock);
        }
        else
        {
            AssertRefused(result, status, account, dispenser, stock);
        }
    }

    [Fact]
    public void Withdraw_matches_oracle_for_every_stock_jam_and_amount()
    {
        foreach (var stock in AllStocks())
            foreach (var jammed in JamOptions)
                foreach (var amount in DomainAmounts())
                    CheckWithdraw(new AccountState(250, 0), stock, jammed, amount);
    }

    [Fact]
    public void Withdraw_matches_oracle_for_every_account_and_amount()
    {
        foreach (var stock in new[] { Full, N(1, 2, 1), N(0, 0, 0) })
            for (var balance = 0; balance <= 250; balance += 5)
                for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
                    foreach (var amount in DomainAmounts())
                        CheckWithdraw(new AccountState(balance, withdrawn), stock, null, amount);
    }

    [Fact]
    public void Deposit_matches_specification_over_whole_domain()
    {
        var account = new AccountState(100, 40);
        foreach (var stock in AllStocks())
            foreach (var jammed in JamOptions)
                for (var mask = 0; mask < 8; mask++)
                {
                    var notes = N(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
                    var (result, dispenser) = Run(account.Balance, account.WithdrawnToday, stock, jammed, new Operation.Deposit(notes));
                    var after = stock + notes;

                    var status =
                        notes.Total == 0 ? AtmStatus.InvalidAmount
                        : jammed is { } j && notes.Of(j) > 0 ? AtmStatus.CassetteJammed
                        : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
                        : AtmStatus.Ok;

                    if (status == AtmStatus.Ok)
                    {
                        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = 100 + notes.Value }), result);
                        Assert.Equal(after, dispenser.Stock);
                    }
                    else
                    {
                        AssertRefused(result, status, account, dispenser, stock);
                    }
                }
    }

    // ---- Doublures ----

    private sealed class RecordingDispenser(IMoneyDispenser inner) : IMoneyDispenser
    {
        public List<NoteCounts> Passes { get; } = [];

        public NoteCounts Stock => inner.Stock;

        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            Passes.Add(notes);
            return inner.Dispense(notes);
        }

        public bool Load(NoteCounts notes) => inner.Load(notes);
    }

    private sealed class FaultyDispenser(NoteCounts stock, int failOnDispenseCall, bool failLoad = false) : IMoneyDispenser
    {
        public NoteCounts Stock { get; private set; } = stock;

        public Denomination? Jammed => null;

        public int DispenseCalls { get; private set; }

        public NoteCounts Dispensed { get; private set; }

        public bool Dispense(NoteCounts notes)
        {
            DispenseCalls++;
            if (DispenseCalls == failOnDispenseCall)
            {
                return false;
            }

            Stock -= notes;
            Dispensed += notes;
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
