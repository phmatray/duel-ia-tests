namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts FullStock = Notes(5, 5, 5);

    private static (AtmResult Result, SpyDispenser Dispenser) Run(
        AccountState account, int amount, NoteCounts? stock = null, Denomination? jammed = null)
    {
        var dispenser = new SpyDispenser(stock ?? FullStock, jammed);
        var result = new CashMachine(dispenser).Execute(account, Withdraw(amount));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus status, AccountState account, NoteCounts stock, AtmResult result, SpyDispenser dispenser)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.DispenseCalls.Where(c => c.Result));
    }

    // --- Règle 1 : montant invalide ---

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Negative_zero_or_non_multiple_of_10_is_invalid(int amount)
    {
        var account = Account(250, 0);
        var (result, dispenser) = Run(account, amount);

        AssertRefused(AtmStatus.InvalidAmount, account, FullStock, result, dispenser);
    }

    [Fact]
    public void Invalid_amount_is_checked_before_daily_limit()
    {
        var account = Account(250, 200);
        var (result, dispenser) = Run(account, -10);

        AssertRefused(AtmStatus.InvalidAmount, account, FullStock, result, dispenser);
    }

    [Fact]
    public void Invalid_amount_is_checked_before_funds_and_stock()
    {
        var account = Account(0, 0);
        var (result, dispenser) = Run(account, 0, Notes(0, 0, 0));

        AssertRefused(AtmStatus.InvalidAmount, account, Notes(0, 0, 0), result, dispenser);
    }

    // --- Règle 2 : plafond du jour ---

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    public void Exceeding_200_per_day_is_refused(int withdrawn, int amount)
    {
        var account = Account(250, withdrawn);
        var (result, dispenser) = Run(account, amount);

        AssertRefused(AtmStatus.DailyLimitExceeded, account, FullStock, result, dispenser);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Reaching_exactly_200_per_day_is_allowed(int withdrawn, int amount)
    {
        var (result, _) = Run(Account(250, withdrawn), amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(200, result.Account.WithdrawnToday);
    }

    [Fact]
    public void Fee_does_not_count_towards_the_daily_limit()
    {
        // 90 + 110 = 200 : pile au plafond, les 5 € de frais n'entrent pas dans le calcul.
        var (result, _) = Run(Account(250, 90), 110);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 1), 5, Account(135, 200)), result);
    }

    [Fact]
    public void Daily_limit_is_checked_before_funds()
    {
        var account = Account(0, 200);
        var (result, dispenser) = Run(account, 10);

        AssertRefused(AtmStatus.DailyLimitExceeded, account, FullStock, result, dispenser);
    }

    [Fact]
    public void Daily_limit_is_checked_before_stock()
    {
        var account = Account(250, 160);
        var (result, dispenser) = Run(account, 50, Notes(0, 0, 0));

        AssertRefused(AtmStatus.DailyLimitExceeded, account, Notes(0, 0, 0), result, dispenser);
    }

    // --- Règle 3 : fonds insuffisants, frais compris ---

    [Theory]
    [InlineData(40, 50)]
    [InlineData(0, 10)]
    [InlineData(95, 100)]
    [InlineData(110, 110)] // 110 + 5 de frais > 110
    [InlineData(150, 150)]
    [InlineData(200, 200)]
    [InlineData(204, 200)]
    public void Amount_plus_fee_above_balance_is_refused(int balance, int amount)
    {
        var account = Account(balance, 0);
        var (result, dispenser) = Run(account, amount);

        AssertRefused(AtmStatus.InsufficientFunds, account, FullStock, result, dispenser);
    }

    [Theory]
    [InlineData(50, 50, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 5)]
    [InlineData(205, 200, 5)]
    public void Amount_plus_fee_equal_to_balance_is_allowed(int balance, int amount, int fee)
    {
        var (result, _) = Run(Account(balance, 0), amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(0, result.Account.Balance);
    }

    [Fact]
    public void Funds_are_checked_before_stock()
    {
        var account = Account(20, 0);
        var (result, dispenser) = Run(account, 50, Notes(0, 0, 0));

        AssertRefused(AtmStatus.InsufficientFunds, account, Notes(0, 0, 0), result, dispenser);
    }

    // --- Frais ---

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_is_5_strictly_above_100_and_0_otherwise(int amount, int expectedFee)
    {
        var (result, _) = Run(Account(250, 0), amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(250 - amount - expectedFee, result.Account.Balance);
        Assert.Equal(amount, result.Account.WithdrawnToday);
    }

    // --- Règle 4 : combinaison impossible ---

    [Theory]
    [InlineData(0, 0, 0, 10)]
    [InlineData(1, 0, 0, 10)]   // un 50 ne fait pas 10
    [InlineData(1, 0, 0, 60)]
    [InlineData(0, 2, 0, 30)]
    [InlineData(0, 2, 0, 50)]
    [InlineData(1, 1, 0, 80)]
    [InlineData(2, 0, 0, 150)]
    [InlineData(0, 0, 5, 60)]
    public void No_exact_combination_means_cannot_dispense(int fifties, int twenties, int tens, int amount)
    {
        var stock = Notes(fifties, twenties, tens);
        var account = Account(250, 0);
        var (result, dispenser) = Run(account, amount, stock);

        AssertRefused(AtmStatus.CannotDispense, account, stock, result, dispenser);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 5, 0, 0, 50)]
    [InlineData(Denomination.Twenty, 0, 5, 0, 20)]
    [InlineData(Denomination.Ten, 0, 0, 5, 10)]
    [InlineData(Denomination.Ten, 1, 1, 5, 80)]   // il faudrait un 10
    [InlineData(Denomination.Twenty, 1, 5, 0, 70)] // il faudrait un 20
    public void Jammed_cassette_counts_as_empty(Denomination jammed, int fifties, int twenties, int tens, int amount)
    {
        var stock = Notes(fifties, twenties, tens);
        var account = Account(250, 0);
        var (result, dispenser) = Run(account, amount, stock, jammed);

        AssertRefused(AtmStatus.CannotDispense, account, stock, result, dispenser);
    }

    // --- La combinaison : le moins de billets ---

    [Theory]
    [InlineData(5, 5, 5, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 200, 4, 0, 0)]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // le glouton (50 d'abord) échoue
    [InlineData(1, 4, 0, 80, 0, 4, 0)]   // idem
    [InlineData(3, 4, 0, 130, 1, 4, 0)]  // 50+80 : seule décomposition sans 10
    [InlineData(5, 0, 5, 40, 0, 0, 4)]
    [InlineData(0, 5, 5, 150, 0, 5, 5)]
    [InlineData(1, 5, 5, 200, 1, 5, 5)]
    [InlineData(2, 5, 0, 160, 2, 3, 0)]
    [InlineData(5, 1, 5, 70, 1, 1, 0)]
    public void Picks_the_combination_with_fewest_notes(
        int fifties, int twenties, int tens, int amount, int expFifties, int expTwenties, int expTens)
    {
        var stock = Notes(fifties, twenties, tens);
        var expected = Notes(expFifties, expTwenties, expTens);
        var (result, dispenser) = Run(Account(250, 0), amount, stock);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 5, 5, 5, 100, 0, 5, 0)]
    [InlineData(Denomination.Twenty, 5, 5, 5, 40, 0, 0, 4)]
    [InlineData(Denomination.Ten, 5, 5, 5, 70, 1, 1, 0)]
    [InlineData(Denomination.Ten, 5, 5, 5, 60, 0, 3, 0)]
    public void Fewest_notes_ignores_the_jammed_cassette(
        Denomination jammed, int fifties, int twenties, int tens, int amount, int expFifties, int expTwenties, int expTens)
    {
        var stock = Notes(fifties, twenties, tens);
        var expected = Notes(expFifties, expTwenties, expTens);
        var (result, dispenser) = Run(Account(250, 0), amount, stock, jammed);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    // --- La sortie : passages de 4 billets au plus ---

    [Theory]
    [InlineData(0, 1, 4, 60)]  // 5 billets sur deux coupures
    [InlineData(0, 5, 5, 150)] // 10 billets
    [InlineData(1, 5, 5, 200)] // 11 billets
    [InlineData(3, 4, 0, 130)] // 5 billets
    [InlineData(0, 0, 5, 50)]  // 5 billets d'une coupure
    [InlineData(5, 5, 5, 200)] // 4 billets
    public void Notes_leave_in_passes_of_at_most_4_and_add_up_to_the_combination(int fifties, int twenties, int tens, int amount)
    {
        var stock = Notes(fifties, twenties, tens);
        var (result, dispenser) = Run(Account(250, 0), amount, stock);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.NotEmpty(dispenser.DispenseCalls);
        Assert.All(dispenser.DispenseCalls, call =>
        {
            Assert.True(call.Result, $"passage refusé par le module : {call.Notes}");
            Assert.InRange(call.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass);
        });
        Assert.Equal(result.Notes, dispenser.Dispensed);
        Assert.Equal(stock - result.Notes, dispenser.Stock);
        Assert.Empty(dispenser.LoadCalls);
    }

    // --- Succès ---

    [Fact]
    public void Success_debits_amount_plus_fee_and_adds_amount_to_withdrawn_today()
    {
        var (result, _) = Run(Account(200, 30), 120);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 1, 0), 5, Account(75, 150)), result);
    }
}
