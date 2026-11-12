namespace Atm.Tests;

public class CashMachineTests
{
    private static Denomination? Jam(int jammed) => jammed == 0 ? null : (Denomination)jammed;

    private static (AtmResult Result, MoneyDispenser Dispenser) Run(
        int balance, int withdrawn, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), operation);
        return (result, dispenser);
    }

    private static void AssertRefused(AtmResult result, AtmStatus status, AccountState account)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
    }

    // ---------- Exemples de la spécification ----------

    [Theory]
    // solde, retiré, stock 50/20/10, bloquée, montant, billets attendus 50/20/10, frais, solde après, retiré après
    [InlineData(100, 0, 1, 3, 0, 0, 60, 0, 3, 0, 0, 40, 60)]
    [InlineData(100, 0, 1, 1, 1, 0, 80, 1, 1, 1, 0, 20, 80)]
    [InlineData(105, 0, 5, 5, 5, 0, 100, 2, 0, 0, 0, 5, 100)]
    [InlineData(115, 0, 5, 5, 5, 0, 110, 2, 0, 1, 5, 0, 110)]
    [InlineData(250, 0, 2, 2, 0, 50, 40, 0, 2, 0, 0, 210, 40)]
    public void Withdraw_examples_succeed(
        int balance, int withdrawn, int f, int t, int c, int jammed, int amount,
        int ef, int et, int ec, int fee, int balanceAfter, int withdrawnAfter)
    {
        var stock = new NoteCounts(f, t, c);
        var (result, dispenser) = Run(balance, withdrawn, stock, Jam(jammed), new Operation.Withdraw(amount));

        var expected = new NoteCounts(ef, et, ec);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(new AccountState(balanceAfter, withdrawnAfter), result.Account);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    [Theory]
    [InlineData(110, 0, 5, 5, 5, 0, 110, AtmStatus.InsufficientFunds)]
    [InlineData(250, 150, 5, 5, 5, 0, 60, AtmStatus.DailyLimitExceeded)]
    [InlineData(250, 0, 1, 0, 0, 50, 50, AtmStatus.CannotDispense)]
    [InlineData(0, 200, 0, 0, 0, 0, 5, AtmStatus.InvalidAmount)]
    public void Withdraw_examples_are_refused(
        int balance, int withdrawn, int f, int t, int c, int jammed, int amount, AtmStatus status)
    {
        var stock = new NoteCounts(f, t, c);
        var (result, dispenser) = Run(balance, withdrawn, stock, Jam(jammed), new Operation.Withdraw(amount));

        AssertRefused(result, status, new AccountState(balance, withdrawn));
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void Deposit_example_into_full_cassette_is_refused()
    {
        var stock = new NoteCounts(0, 5, 0);
        var (result, dispenser) = Run(10, 0, stock, null, new Operation.Deposit(new NoteCounts(0, 1, 0)));

        AssertRefused(result, AtmStatus.CassetteFull, new AccountState(10, 0));
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void Deposit_example_into_jammed_cassette_is_refused()
    {
        var stock = new NoteCounts(0, 1, 0);
        var (result, dispenser) = Run(10, 30, stock, Denomination.Twenty, new Operation.Deposit(new NoteCounts(0, 1, 0)));

        AssertRefused(result, AtmStatus.CassetteJammed, new AccountState(10, 30));
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void Deposit_example_succeeds()
    {
        var (result, dispenser) = Run(10, 30, new NoteCounts(1, 1, 0), null, new Operation.Deposit(new NoteCounts(1, 0, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(70, 30), result.Account);
        Assert.Equal(new NoteCounts(2, 1, 1), dispenser.Stock);
    }

    // ---------- Retrait : contrôles et leur ordre ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void Withdraw_invalid_amount_is_checked_first(int amount)
    {
        // Le plafond est déjà atteint et le solde est nul : InvalidAmount passe avant.
        var (result, _) = Run(0, 200, new NoteCounts(5, 5, 5), null, new Operation.Withdraw(amount));

        AssertRefused(result, AtmStatus.InvalidAmount, new AccountState(0, 200));
    }

    [Theory]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    [InlineData(0, 210)]
    [InlineData(100, 110)]
    public void Withdraw_at_most_the_daily_limit(int withdrawn, int amount)
    {
        var (result, _) = Run(0, withdrawn, NoteCounts.None, null, new Operation.Withdraw(amount));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, new AccountState(0, withdrawn));
    }

    [Fact]
    public void Withdraw_reaching_exactly_the_daily_limit_is_allowed()
    {
        var (result, _) = Run(250, 100, new NoteCounts(5, 5, 5), null, new Operation.Withdraw(100));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(150, 200), result.Account);
    }

    [Fact]
    public void Fee_does_not_count_toward_daily_limit()
    {
        var (result, _) = Run(250, 0, new NoteCounts(4, 0, 0), null, new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(45, 200), result.Account);
    }

    [Fact]
    public void Daily_limit_is_checked_before_funds()
    {
        var (result, _) = Run(0, 180, NoteCounts.None, null, new Operation.Withdraw(30));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, new AccountState(0, 180));
    }

    [Theory]
    [InlineData(100, 100, AtmStatus.Ok)]
    [InlineData(95, 100, AtmStatus.InsufficientFunds)]
    [InlineData(115, 110, AtmStatus.Ok)]
    [InlineData(110, 110, AtmStatus.InsufficientFunds)]
    [InlineData(105, 100, AtmStatus.Ok)]
    public void Funds_include_fee_only_above_100(int balance, int amount, AtmStatus status)
    {
        var (result, _) = Run(balance, 0, new NoteCounts(5, 5, 5), null, new Operation.Withdraw(amount));

        Assert.Equal(status, result.Status);
        Assert.Equal(status == AtmStatus.Ok && amount > 100 ? 5 : 0, result.Fee);
    }

    [Fact]
    public void Funds_are_checked_before_dispensability()
    {
        var (result, _) = Run(10, 0, NoteCounts.None, null, new Operation.Withdraw(20));

        AssertRefused(result, AtmStatus.InsufficientFunds, new AccountState(10, 0));
    }

    [Fact]
    public void Jammed_cassette_counts_as_empty()
    {
        var (result, _) = Run(250, 0, new NoteCounts(0, 0, 5), Denomination.Ten, new Operation.Withdraw(10));

        AssertRefused(result, AtmStatus.CannotDispense, new AccountState(250, 0));
    }

    [Fact]
    public void Cannot_dispense_when_amount_has_no_exact_combination()
    {
        // 30 € avec seulement des 20 et des 50.
        var (result, _) = Run(250, 0, new NoteCounts(5, 5, 0), null, new Operation.Withdraw(30));

        AssertRefused(result, AtmStatus.CannotDispense, new AccountState(250, 0));
    }

    [Fact]
    public void Fewest_notes_is_not_greedy()
    {
        // 60 € : le glouton 50+10 échoue sans billet de 10, il faut 3×20.
        var (result, _) = Run(250, 0, new NoteCounts(5, 5, 0), null, new Operation.Withdraw(60));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(0, 3, 0), result.Notes);
    }

    [Fact]
    public void Fewest_notes_prefers_fifties_when_available()
    {
        var (result, _) = Run(250, 0, new NoteCounts(5, 5, 5), null, new Operation.Withdraw(200));

        Assert.Equal(new NoteCounts(4, 0, 0), result.Notes);
    }

    // ---------- Sortie en passages ----------

    [Fact]
    public void Notes_leave_in_passes_of_at_most_four()
    {
        var dispenser = new RecordingDispenser(new NoteCounts(0, 5, 5));
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(0, 5, 5), result.Notes);
        Assert.True(dispenser.DispenseCalls.Count >= 3);
        Assert.All(dispenser.DispenseCalls, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(result.Notes, dispenser.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void Refusals_never_touch_the_dispenser()
    {
        var dispenser = new RecordingDispenser(new NoteCounts(1, 0, 0), Denomination.Fifty);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        machine.Execute(account, new Operation.Withdraw(5));
        machine.Execute(account, new Operation.Withdraw(50));
        machine.Execute(account, new Operation.Deposit(NoteCounts.None));
        machine.Execute(account, new Operation.Deposit(new NoteCounts(1, 0, 0)));

        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
    }

    // ---------- Dépôt ----------

    [Fact]
    public void Deposit_without_notes_is_invalid_even_if_cassette_jammed()
    {
        var (result, _) = Run(10, 0, new NoteCounts(5, 5, 5), Denomination.Fifty, new Operation.Deposit(NoteCounts.None));

        AssertRefused(result, AtmStatus.InvalidAmount, new AccountState(10, 0));
    }

    [Fact]
    public void Deposit_with_negative_counts_is_invalid()
    {
        var (result, dispenser) = Run(10, 0, new NoteCounts(1, 1, 1), null, new Operation.Deposit(new NoteCounts(1, -1, 1)));

        AssertRefused(result, AtmStatus.InvalidAmount, new AccountState(10, 0));
        Assert.Equal(new NoteCounts(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Jammed_is_checked_before_full()
    {
        var (result, _) = Run(10, 0, new NoteCounts(5, 5, 5), Denomination.Ten, new Operation.Deposit(new NoteCounts(1, 1, 1)));

        AssertRefused(result, AtmStatus.CassetteJammed, new AccountState(10, 0));
    }

    [Fact]
    public void Deposit_avoiding_jammed_cassette_succeeds()
    {
        var (result, dispenser) = Run(0, 50, new NoteCounts(4, 0, 0), Denomination.Ten, new Operation.Deposit(new NoteCounts(1, 1, 0)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(70, 50), result.Account);
        Assert.Equal(new NoteCounts(5, 1, 0), dispenser.Stock);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(4, 4, 5, 1, 1, 1)]
    public void Deposit_overflowing_any_cassette_is_refused(int f, int t, int c, int df, int dt, int dc)
    {
        var stock = new NoteCounts(f, t, c);
        var (result, dispenser) = Run(10, 0, stock, null, new Operation.Deposit(new NoteCounts(df, dt, dc)));

        AssertRefused(result, AtmStatus.CassetteFull, new AccountState(10, 0));
        Assert.Equal(stock, dispenser.Stock);
    }

    // ---------- Pannes du module ----------

    [Fact]
    public void Dispenser_fault_on_first_pass_returns_nothing()
    {
        var dispenser = new RecordingDispenser(new NoteCounts(5, 5, 5)) { FailDispenseOnCall = 1 };
        var account = new AccountState(250, 0);
        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(110));

        AssertRefused(result, AtmStatus.DispenserFault, account);
        Assert.Single(dispenser.DispenseCalls);
    }

    [Fact]
    public void Dispenser_fault_mid_withdrawal_reports_notes_already_out_and_stops()
    {
        // 150 € sans 50 : 5×20 + 5×10, soit au moins trois passages.
        var dispenser = new RecordingDispenser(new NoteCounts(0, 5, 5)) { FailDispenseOnCall = 2 };
        var account = new AccountState(250, 0);
        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(dispenser.DispenseCalls[0], result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(2, dispenser.DispenseCalls.Count);
        Assert.Equal(new NoteCounts(0, 5, 5) - result.Notes, dispenser.Stock);
    }

    [Fact]
    public void Dispenser_fault_on_deposit()
    {
        var dispenser = new RecordingDispenser(NoteCounts.None) { FailLoad = true };
        var account = new AccountState(10, 30);
        var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(new NoteCounts(1, 1, 1)));

        AssertRefused(result, AtmStatus.DispenserFault, account);
        Assert.Single(dispenser.LoadCalls);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    // ---------- Divers ----------

    [Fact]
    public void Machine_is_stateless_between_calls()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(5, 5, 5));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(100, 0);

        var first = machine.Execute(account, new Operation.Withdraw(50));
        var second = machine.Execute(account, new Operation.Withdraw(50));

        Assert.Equal(first, second);
        Assert.Equal(new NoteCounts(3, 5, 5), dispenser.Stock);
    }

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
}
