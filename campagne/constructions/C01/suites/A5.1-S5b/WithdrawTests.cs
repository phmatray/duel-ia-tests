namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    private static (AtmResult Result, RecordingDispenser Dispenser) Run(
        int balance, int withdrawn, int amount, NoteCounts? stock = null, Denomination? jammed = null)
    {
        var dispenser = RecordingDispenser.Real(stock ?? Full, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, int balance, int withdrawn, int amount,
        NoteCounts? stock = null, Denomination? jammed = null)
    {
        var (result, dispenser) = Run(balance, withdrawn, amount, stock, jammed);
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(balance, withdrawn), result.Account);
        Assert.Equal(stock ?? Full, dispenser.Stock);
        Assert.Empty(dispenser.Dispensed);
        Assert.Empty(dispenser.Loaded);
    }

    // --- Règle 1 : montant invalide ---

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Invalid_amount_is_refused(int amount) =>
        AssertRefused(AtmStatus.InvalidAmount, 250, 0, amount);

    [Fact]
    public void Invalid_amount_is_checked_before_the_daily_limit() =>
        AssertRefused(AtmStatus.InvalidAmount, 250, 200, 5);

    [Fact]
    public void Invalid_amount_is_checked_before_funds_and_stock() =>
        AssertRefused(AtmStatus.InvalidAmount, 0, 0, -10, NoteCounts.None);

    // --- Règle 2 : plafond du jour ---

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    public void Exceeding_the_daily_limit_is_refused(int withdrawn, int amount) =>
        AssertRefused(AtmStatus.DailyLimitExceeded, 250, withdrawn, amount);

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Reaching_exactly_the_daily_limit_is_allowed(int withdrawn, int amount)
    {
        var (result, _) = Run(250, withdrawn, amount);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(withdrawn + amount, result.Account.WithdrawnToday);
    }

    [Fact]
    public void Fee_does_not_count_towards_the_daily_limit()
    {
        // 90 + 110 = 200 : pile au plafond, les 5 € de frais n'y entrent pas.
        var (result, _) = Run(250, 90, 110);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(135, 200), result.Account);
    }

    [Fact]
    public void Daily_limit_is_checked_before_funds() =>
        AssertRefused(AtmStatus.DailyLimitExceeded, 0, 200, 10);

    [Fact]
    public void Daily_limit_is_checked_before_stock() =>
        AssertRefused(AtmStatus.DailyLimitExceeded, 250, 0, 210, NoteCounts.None);

    // --- Règle 3 : fonds insuffisants ---

    [Theory]
    [InlineData(0, 10)]
    [InlineData(50, 60)]
    [InlineData(105, 110)] // 110 + 5 > 105
    [InlineData(110, 110)] // 110 + 5 > 110
    [InlineData(200, 200)] // 200 + 5 > 200
    public void Insufficient_funds_are_refused(int balance, int amount) =>
        AssertRefused(AtmStatus.InsufficientFunds, balance, 0, amount);

    [Theory]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 0)]
    [InlineData(205, 200, 0)]
    [InlineData(10, 10, 0)]
    public void Balance_covering_exactly_amount_plus_fee_is_allowed(int balance, int amount, int balanceAfter)
    {
        var (result, _) = Run(balance, 0, amount);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(balanceAfter, result.Account.Balance);
    }

    [Fact]
    public void Funds_are_checked_before_stock() =>
        AssertRefused(AtmStatus.InsufficientFunds, 0, 0, 10, NoteCounts.None);

    // --- Frais ---

    [Theory]
    [InlineData(10, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_is_5_strictly_above_100(int amount, int fee)
    {
        var (result, _) = Run(250, 0, amount);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(new AccountState(250 - amount - fee, amount), result.Account);
    }

    // --- Règle 4 : combinaison impossible ---

    [Theory]
    [InlineData(10, 0, 0, 0, 0)]           // stock vide
    [InlineData(30, 1, 1, 0, 0)]           // ni 30 ni 10 : 50 et 20 seulement
    [InlineData(10, 1, 1, 0, 0)]           // pas de 10
    [InlineData(200, 1, 2, 3, 0)]          // stock total 120
    [InlineData(50, 1, 0, 0, 50)]          // seul billet dans la cassette bloquée
    [InlineData(10, 0, 0, 5, 10)]          // 10 bloquée
    [InlineData(60, 0, 5, 0, 20)]          // 20 bloquée
    [InlineData(60, 1, 5, 0, 20)]          // 50 dispo mais pas de 10, 20 bloquée
    public void Unreachable_amount_is_refused(int amount, int f, int t, int d, int jam) =>
        AssertRefused(AtmStatus.CannotDispense, 250, 0, amount, new NoteCounts(f, t, d),
            jam == 0 ? null : (Denomination)jam);

    // --- Combinaison au moindre nombre de billets ---

    [Theory]
    // montant, stock, bloquée, billets attendus
    [InlineData(60, 5, 5, 5, 0, 1, 0, 1)]   // 50+10 plutôt que 20×3
    [InlineData(60, 1, 3, 0, 0, 0, 3, 0)]   // pas de 10 : 20×3
    [InlineData(60, 0, 5, 5, 0, 0, 3, 0)]   // pas de 50 : 20×3 plutôt que 20+10×4…
    [InlineData(80, 5, 5, 5, 0, 1, 1, 1)]   // 50+20+10 plutôt que 20×4
    [InlineData(80, 5, 5, 0, 0, 0, 4, 0)]   // pas de 10 : 20×4
    [InlineData(90, 5, 5, 5, 0, 1, 2, 0)]   // 50+20×2
    [InlineData(40, 0, 0, 5, 0, 0, 0, 4)]   // seulement des 10
    [InlineData(40, 5, 5, 5, 20, 0, 0, 4)]  // 20 bloquée
    [InlineData(100, 5, 5, 5, 50, 0, 5, 0)] // 50 bloquée : 20×5
    [InlineData(100, 5, 5, 5, 0, 2, 0, 0)]
    [InlineData(150, 0, 5, 5, 0, 0, 5, 5)]  // 10 billets
    [InlineData(190, 5, 5, 5, 0, 3, 2, 0)]
    [InlineData(200, 5, 5, 5, 0, 4, 0, 0)]
    [InlineData(200, 5, 5, 5, 10, 4, 0, 0)]
    [InlineData(110, 1, 5, 5, 0, 1, 3, 0)]  // un seul 50
    [InlineData(70, 5, 5, 5, 50, 0, 3, 1)]  // 50 bloquée : 20×3+10
    public void Fewest_notes_combination_is_dispensed(int amount, int f, int t, int d, int jam, int nf, int nt, int nd)
    {
        var stock = new NoteCounts(f, t, d);
        var expected = new NoteCounts(nf, nt, nd);
        var (result, dispenser) = Run(250, 0, amount, stock, jam == 0 ? null : (Denomination)jam);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    // --- Sortie en passages ---

    [Theory]
    [InlineData(150, 0, 5, 5)] // 10 billets
    [InlineData(190, 5, 5, 5)] // 5 billets
    [InlineData(40, 0, 0, 5)]  // 4 billets
    [InlineData(100, 0, 5, 5)] // 5 billets
    [InlineData(10, 5, 5, 5)]  // 1 billet
    public void Notes_leave_in_passes_of_at_most_four(int amount, int f, int t, int d)
    {
        var stock = new NoteCounts(f, t, d);
        var (result, dispenser) = Run(250, 0, amount, stock);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.NotEmpty(dispenser.Dispensed);
        Assert.All(dispenser.Dispensed, pass => Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(result.Notes, dispenser.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(stock - result.Notes, dispenser.Stock);
        Assert.Empty(dispenser.Loaded);
    }

    // --- Succès ---

    [Fact]
    public void Successful_withdrawal_updates_account_and_returns_notes()
    {
        var (result, dispenser) = Run(250, 30, 120);
        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(2, 1, 0), 5, new AccountState(125, 150)), result);
        Assert.Equal(new NoteCounts(3, 4, 5), dispenser.Stock);
    }

    // --- Sans mémoire ---

    [Fact]
    public void Machine_depends_only_on_the_given_account()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(100, 0);

        var first = machine.Execute(account, new Operation.Withdraw(50));
        var second = machine.Execute(account, new Operation.Withdraw(50));

        Assert.Equal(first, second);
        Assert.Equal(new AccountState(50, 50), second.Account);
        Assert.Equal(new NoteCounts(3, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void A_refusal_does_not_affect_the_next_call()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);

        var refused = machine.Execute(new AccountState(0, 0), new Operation.Withdraw(50));
        var ok = machine.Execute(new AccountState(100, 0), new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.InsufficientFunds, refused.Status);
        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(1, 0, 0), 0, new AccountState(50, 50)), ok);
    }

    [Fact]
    public void Machine_reads_the_current_stock_of_the_module()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(1, 0, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Withdraw(50)).Status);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(account, new Operation.Withdraw(50)).Status);
    }
}
