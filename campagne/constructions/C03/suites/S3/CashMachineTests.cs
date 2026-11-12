namespace Atm.Tests;

public class CashMachineTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static (AtmResult Result, MoneyDispenser Dispenser) Run(
        int balance, int withdrawn, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), operation);
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult result, AccountState account,
        MoneyDispenser dispenser, NoteCounts stock)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
    }

    // ---- Exemples de la spécification -------------------------------------------------------------

    public static TheoryData<int, int, NoteCounts, Denomination?, int, NoteCounts, int, int, int> WithdrawOkExamples => new()
    {
        { 100, 0, N(1, 3, 0), null, 60, N(0, 3, 0), 0, 40, 60 },
        { 100, 0, N(1, 1, 1), null, 80, N(1, 1, 1), 0, 20, 80 },
        { 105, 0, N(5, 5, 5), null, 100, N(2, 0, 0), 0, 5, 100 },
        { 115, 0, N(5, 5, 5), null, 110, N(2, 0, 1), 5, 0, 110 },
        { 250, 0, N(2, 2, 0), Denomination.Fifty, 40, N(0, 2, 0), 0, 210, 40 },
    };

    [Theory]
    [MemberData(nameof(WithdrawOkExamples))]
    public void Withdraw_spec_examples_succeed(int balance, int withdrawn, NoteCounts stock, Denomination? jammed,
        int amount, NoteCounts notes, int fee, int newBalance, int newWithdrawn)
    {
        var (result, dispenser) = Run(balance, withdrawn, stock, jammed, new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(notes, result.Notes);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(new AccountState(newBalance, newWithdrawn), result.Account);
        Assert.Equal(stock - notes, dispenser.Stock);
    }

    public static TheoryData<int, int, NoteCounts, Denomination?, int, AtmStatus> WithdrawRefusedExamples => new()
    {
        { 110, 0, N(5, 5, 5), null, 110, AtmStatus.InsufficientFunds },
        { 250, 150, N(5, 5, 5), null, 60, AtmStatus.DailyLimitExceeded },
        { 250, 0, N(1, 0, 0), Denomination.Fifty, 50, AtmStatus.CannotDispense },
        { 0, 200, N(0, 0, 0), null, 5, AtmStatus.InvalidAmount },
    };

    [Theory]
    [MemberData(nameof(WithdrawRefusedExamples))]
    public void Withdraw_spec_examples_are_refused(int balance, int withdrawn, NoteCounts stock, Denomination? jammed,
        int amount, AtmStatus expected)
    {
        var (result, dispenser) = Run(balance, withdrawn, stock, jammed, new Operation.Withdraw(amount));

        AssertRefused(expected, result, new AccountState(balance, withdrawn), dispenser, stock);
    }

    [Fact]
    public void Deposit_into_full_cassette_is_refused()
    {
        var (result, dispenser) = Run(10, 0, N(0, 5, 0), null, new Operation.Deposit(N(0, 1, 0)));

        AssertRefused(AtmStatus.CassetteFull, result, new AccountState(10, 0), dispenser, N(0, 5, 0));
    }

    [Fact]
    public void Deposit_into_jammed_cassette_is_refused()
    {
        var (result, dispenser) = Run(10, 30, N(0, 1, 0), Denomination.Twenty, new Operation.Deposit(N(0, 1, 0)));

        AssertRefused(AtmStatus.CassetteJammed, result, new AccountState(10, 30), dispenser, N(0, 1, 0));
    }

    [Fact]
    public void Deposit_succeeds_and_loads_notes()
    {
        var (result, dispenser) = Run(10, 30, N(1, 1, 0), null, new Operation.Deposit(N(1, 0, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(70, 30), result.Account);
        Assert.Equal(N(2, 1, 1), dispenser.Stock);
    }

    // ---- Retrait : contrôles et ordre -------------------------------------------------------------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void Withdraw_invalid_amount(int amount)
    {
        var (result, dispenser) = Run(250, 0, N(5, 5, 5), null, new Operation.Withdraw(amount));

        AssertRefused(AtmStatus.InvalidAmount, result, new AccountState(250, 0), dispenser, N(5, 5, 5));
    }

    [Fact]
    public void Invalid_amount_takes_precedence_over_every_other_check()
    {
        var (result, _) = Run(0, 200, N(0, 0, 0), Denomination.Ten, new Operation.Withdraw(-10));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void Daily_limit_is_checked_before_funds_and_notes()
    {
        var (result, _) = Run(0, 200, N(0, 0, 0), null, new Operation.Withdraw(10));

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void Daily_limit_reached_exactly_is_allowed()
    {
        var (result, _) = Run(250, 150, N(5, 5, 5), null, new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(200, 200), result.Account);
    }

    [Fact]
    public void Withdraw_above_daily_limit_from_zero_is_refused()
    {
        var (result, _) = Run(250, 0, N(5, 5, 5), null, new Operation.Withdraw(210));

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void Fee_does_not_count_towards_daily_limit()
    {
        var (result, _) = Run(250, 0, N(5, 5, 5), null, new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(45, 200), result.Account);
        Assert.Equal(N(4, 0, 0), result.Notes);
    }

    [Fact]
    public void Insufficient_funds_is_checked_before_dispensability()
    {
        var (result, _) = Run(10, 0, N(0, 0, 0), null, new Operation.Withdraw(20));

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    [Theory]
    [InlineData(100, 100, AtmStatus.Ok, 0)]
    [InlineData(105, 100, AtmStatus.Ok, 0)]
    [InlineData(115, 110, AtmStatus.Ok, 5)]
    [InlineData(114, 110, AtmStatus.InsufficientFunds, 0)]
    [InlineData(99, 100, AtmStatus.InsufficientFunds, 0)]
    public void Fee_applies_strictly_above_100(int balance, int amount, AtmStatus expected, int fee)
    {
        var (result, _) = Run(balance, 0, N(5, 5, 5), null, new Operation.Withdraw(amount));

        Assert.Equal(expected, result.Status);
        Assert.Equal(fee, result.Fee);
    }

    [Fact]
    public void Jammed_cassette_counts_as_empty()
    {
        var (result, dispenser) = Run(250, 0, N(5, 5, 5), Denomination.Fifty, new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(0, 2, 1), result.Notes);
        Assert.Equal(N(5, 3, 4), dispenser.Stock);
    }

    [Fact]
    public void Cannot_dispense_when_no_exact_combination()
    {
        var (result, dispenser) = Run(250, 0, N(1, 1, 0), null, new Operation.Withdraw(30));

        AssertRefused(AtmStatus.CannotDispense, result, new AccountState(250, 0), dispenser, N(1, 1, 0));
    }

    [Fact]
    public void Fewest_notes_prefers_twenties_over_a_fifty_with_tens()
    {
        // 60 = 20×3 (3 billets) ou 50×1 10×1 (2 billets) : on prend 2 billets.
        var (result, _) = Run(250, 0, N(1, 3, 1), null, new Operation.Withdraw(60));
        Assert.Equal(N(1, 0, 1), result.Notes);

        // Sans billet de 10, 60 = 20×3 même s'il y a un 50.
        (result, _) = Run(250, 0, N(1, 3, 0), null, new Operation.Withdraw(60));
        Assert.Equal(N(0, 3, 0), result.Notes);
    }

    [Fact]
    public void Withdraw_needing_many_notes_is_split_into_passes()
    {
        // 150 sans billet de 50 : 20×5 10×5, soit 10 billets, en plusieurs passages.
        var (result, dispenser) = Run(250, 0, N(0, 5, 5), null, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(0, 5, 5), result.Notes);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
        Assert.Equal(new AccountState(95, 150), result.Account);
    }

    [Fact]
    public void Withdraw_passes_never_exceed_four_notes()
    {
        var spy = new SpyDispenser(N(5, 5, 5));
        var result = new CashMachine(spy).Execute(new AccountState(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.All(spy.Passes, p => Assert.InRange(p.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(result.Notes, spy.Passes.Aggregate(NoteCounts.None, (a, b) => a + b));
    }

    // ---- Dépôt -------------------------------------------------------------------------------------

    [Fact]
    public void Empty_deposit_is_invalid()
    {
        var (result, dispenser) = Run(10, 0, N(5, 5, 5), Denomination.Ten, new Operation.Deposit(NoteCounts.None));

        AssertRefused(AtmStatus.InvalidAmount, result, new AccountState(10, 0), dispenser, N(5, 5, 5));
    }

    [Fact]
    public void Jammed_is_checked_before_full()
    {
        var (result, _) = Run(10, 0, N(0, 5, 0), Denomination.Twenty, new Operation.Deposit(N(0, 1, 0)));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
    }

    [Fact]
    public void Deposit_avoiding_jammed_cassette_succeeds()
    {
        var (result, dispenser) = Run(0, 200, N(0, 0, 0), Denomination.Twenty, new Operation.Deposit(N(1, 0, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(60, 200), result.Account);
        Assert.Equal(N(1, 0, 1), dispenser.Stock);
    }

    [Fact]
    public void Deposit_filling_cassette_to_capacity_succeeds()
    {
        var (result, dispenser) = Run(0, 0, N(4, 4, 4), null, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(80, 0), result.Account);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    // ---- Pannes ------------------------------------------------------------------------------------

    [Fact]
    public void Dispenser_failing_on_first_pass_is_a_fault_with_no_notes()
    {
        var spy = new SpyDispenser(N(5, 5, 5)) { FailDispenseAt = 0 };
        var account = new AccountState(250, 0);
        var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
    }

    [Fact]
    public void Dispenser_failing_mid_withdrawal_reports_notes_already_dispensed()
    {
        var spy = new SpyDispenser(N(0, 5, 5)) { FailDispenseAt = 1 };
        var account = new AccountState(250, 0);
        var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(spy.Passes[0], result.Notes);
        Assert.Equal(4, result.Notes.Total);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Single(spy.Passes);
    }

    [Fact]
    public void Dispenser_failing_on_load_is_a_fault()
    {
        var spy = new SpyDispenser(N(0, 0, 0)) { FailLoad = true };
        var account = new AccountState(10, 30);
        var result = new CashMachine(spy).Execute(account, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
    }

    // ---- Arguments ---------------------------------------------------------------------------------

    [Fact]
    public void Null_dispenser_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => new CashMachine(null!));

    [Fact]
    public void Null_operation_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() =>
            new CashMachine(new MoneyDispenser(NoteCounts.None)).Execute(default, null!));

    [Fact]
    public void Unknown_operation_is_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            new CashMachine(new MoneyDispenser(NoteCounts.None)).Execute(default, new UnknownOperation()));

    private sealed record UnknownOperation : Operation;

    // ---- Vérification exhaustive sur le domaine du retrait ----------------------------------------

    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static NoteCounts? ReferenceCombination(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var c = 0; c <= available.Tens; c++)
        {
            var candidate = N(f, t, c);
            if (candidate.Value == amount && (best is null || candidate.Total < best.Value.Total))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static AtmStatus ExpectedStatus(AccountState account, int amount, NoteCounts available)
    {
        if (amount <= 0 || amount % 10 != 0) return AtmStatus.InvalidAmount;
        if (account.WithdrawnToday + amount > 200) return AtmStatus.DailyLimitExceeded;
        if (amount + (amount > 100 ? 5 : 0) > account.Balance) return AtmStatus.InsufficientFunds;
        return ReferenceCombination(amount, available) is null ? AtmStatus.CannotDispense : AtmStatus.Ok;
    }

    [Fact]
    public void Withdraw_matches_reference_model_over_domain()
    {
        int[] balances = [0, 5, 50, 100, 105, 110, 115, 200, 205, 250];
        int[] withdrawns = [0, 50, 100, 150, 190, 200];

        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var c = 0; c <= 5; c++)
        foreach (var jammed in JamOptions)
        foreach (var amount in Amounts)
        foreach (var balance in balances)
        foreach (var withdrawn in withdrawns)
        {
            var stock = N(f, t, c);
            var available = N(
                jammed == Denomination.Fifty ? 0 : f,
                jammed == Denomination.Twenty ? 0 : t,
                jammed == Denomination.Ten ? 0 : c);
            var account = new AccountState(balance, withdrawn);
            var expected = ExpectedStatus(account, amount, available);

            var (result, dispenser) = Run(balance, withdrawn, stock, jammed, new Operation.Withdraw(amount));

            Assert.Equal(expected, result.Status);
            if (expected == AtmStatus.Ok)
            {
                var fee = amount > 100 ? 5 : 0;
                var notes = ReferenceCombination(amount, available)!.Value;
                Assert.Equal(notes, result.Notes);
                Assert.Equal(fee, result.Fee);
                Assert.Equal(new AccountState(balance - amount - fee, withdrawn + amount), result.Account);
                Assert.Equal(stock - notes, dispenser.Stock);
            }
            else
            {
                AssertRefused(expected, result, account, dispenser, stock);
            }
        }
    }

    [Fact]
    public void Deposit_matches_reference_model_over_domain()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var c = 0; c <= 5; c++)
        foreach (var jammed in JamOptions)
        for (var mask = 0; mask < 8; mask++)
        {
            var stock = N(f, t, c);
            var deposit = N(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
            var account = new AccountState(100, 50);

            var expected =
                deposit.Total == 0 ? AtmStatus.InvalidAmount
                : jammed is { } j && deposit.Of(j) > 0 ? AtmStatus.CassetteJammed
                : (stock + deposit) is { Fifties: > 5 } or { Twenties: > 5 } or { Tens: > 5 } ? AtmStatus.CassetteFull
                : AtmStatus.Ok;

            var (result, dispenser) = Run(account.Balance, account.WithdrawnToday, stock, jammed, new Operation.Deposit(deposit));

            Assert.Equal(expected, result.Status);
            if (expected == AtmStatus.Ok)
            {
                Assert.Equal(NoteCounts.None, result.Notes);
                Assert.Equal(0, result.Fee);
                Assert.Equal(account with { Balance = account.Balance + deposit.Value }, result.Account);
                Assert.Equal(stock + deposit, dispenser.Stock);
            }
            else
            {
                AssertRefused(expected, result, account, dispenser, stock);
            }
        }
    }

    // ---- Doublure ----------------------------------------------------------------------------------

    private sealed class SpyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
    {
        private readonly MoneyDispenser _real = new(stock, jammed);

        public int FailDispenseAt { get; init; } = -1;

        public bool FailLoad { get; init; }

        public List<NoteCounts> Passes { get; } = [];

        public NoteCounts Stock => _real.Stock;

        public Denomination? Jammed => _real.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            if (Passes.Count == FailDispenseAt || !_real.Dispense(notes))
            {
                return false;
            }

            Passes.Add(notes);
            return true;
        }

        public bool Load(NoteCounts notes) => !FailLoad && _real.Load(notes);
    }
}
