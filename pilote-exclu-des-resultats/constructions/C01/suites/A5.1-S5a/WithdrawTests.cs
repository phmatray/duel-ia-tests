using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    private static (AtmResult Result, SpyDispenser Spy) Run(
        AccountState account, int amount, NoteCounts? stock = null, Denomination? jammed = null)
    {
        var spy = new SpyDispenser(stock ?? Full, jammed);
        var result = new CashMachine(spy).Execute(account, Withdraw(amount));
        return (result, spy);
    }

    private static void AssertRefused(AtmStatus expected, AccountState account, AtmResult result, SpyDispenser spy, NoteCounts stockBefore)
    {
        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
        Assert.Empty(spy.DispenseCalls);
        Assert.Empty(spy.LoadCalls);
        Assert.Equal(stockBefore, spy.Stock);
    }

    // ---------- Règle 1 : montant invalide ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Invalid_amount_is_refused(int amount)
    {
        var account = Acc(250, 0);
        var (result, spy) = Run(account, amount);
        AssertRefused(AtmStatus.InvalidAmount, account, result, spy, Full);
    }

    [Fact]
    public void Invalid_amount_takes_precedence_over_every_other_check()
    {
        // Plafond atteint, solde nul, stock vide : c'est quand même InvalidAmount.
        var account = Acc(0, 200);
        var (result, spy) = Run(account, -10, N(0, 0, 0), Denomination.Ten);
        AssertRefused(AtmStatus.InvalidAmount, account, result, spy, N(0, 0, 0));
    }

    // ---------- Règle 2 : plafond du jour ----------

    [Theory]
    [InlineData(0, 200, AtmStatus.Ok)]
    [InlineData(0, 210, AtmStatus.DailyLimitExceeded)]
    [InlineData(0, 220, AtmStatus.DailyLimitExceeded)]
    [InlineData(190, 10, AtmStatus.Ok)]
    [InlineData(190, 20, AtmStatus.DailyLimitExceeded)]
    [InlineData(200, 10, AtmStatus.DailyLimitExceeded)]
    [InlineData(150, 50, AtmStatus.Ok)]
    [InlineData(150, 60, AtmStatus.DailyLimitExceeded)]
    public void Daily_limit_is_200_inclusive(int withdrawn, int amount, AtmStatus expected)
    {
        var account = Acc(250, withdrawn);
        var (result, _) = Run(account, amount);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Fee_does_not_count_towards_the_daily_limit()
    {
        // 90 + 110 = 200 : pile au plafond ; les 5 € de frais n'y entrent pas.
        var (result, _) = Run(Acc(250, 90), 110);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Acc(135, 200), result.Account);
    }

    [Fact]
    public void Daily_limit_takes_precedence_over_insufficient_funds_and_cannot_dispense()
    {
        var account = Acc(0, 200);
        var (result, spy) = Run(account, 10, N(0, 0, 0));
        AssertRefused(AtmStatus.DailyLimitExceeded, account, result, spy, N(0, 0, 0));
    }

    // ---------- Règle 3 : solde insuffisant, frais compris ----------

    [Theory]
    [InlineData(100, 100, AtmStatus.Ok)]                 // pas de frais à 100
    [InlineData(95, 100, AtmStatus.InsufficientFunds)]
    [InlineData(115, 110, AtmStatus.Ok)]                 // 110 + 5
    [InlineData(110, 110, AtmStatus.InsufficientFunds)]  // les frais font déborder
    [InlineData(205, 200, AtmStatus.Ok)]
    [InlineData(200, 200, AtmStatus.InsufficientFunds)]
    [InlineData(10, 10, AtmStatus.Ok)]
    [InlineData(5, 10, AtmStatus.InsufficientFunds)]
    [InlineData(0, 10, AtmStatus.InsufficientFunds)]
    public void Balance_must_cover_amount_plus_fee(int balance, int amount, AtmStatus expected)
    {
        var account = Acc(balance, 0);
        var (result, spy) = Run(account, amount);
        Assert.Equal(expected, result.Status);
        if (expected != AtmStatus.Ok)
        {
            AssertRefused(expected, account, result, spy, Full);
        }
    }

    [Fact]
    public void Insufficient_funds_takes_precedence_over_cannot_dispense()
    {
        var account = Acc(0, 0);
        var (result, spy) = Run(account, 10, N(0, 0, 0));
        AssertRefused(AtmStatus.InsufficientFunds, account, result, spy, N(0, 0, 0));
    }

    // ---------- Frais ----------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_is_5_strictly_above_100(int amount, int fee)
    {
        var (result, _) = Run(Acc(250, 0), amount);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(Acc(250 - amount - fee, amount), result.Account);
    }

    // ---------- Règle 4 : combinaison impossible ----------

    [Theory]
    [InlineData(0, 0, 0, null, 10)]
    [InlineData(1, 0, 0, null, 10)]   // seulement un 50
    [InlineData(1, 0, 0, null, 30)]
    [InlineData(0, 5, 0, null, 30)]   // que des 20 : impair de dizaines
    [InlineData(0, 5, 0, null, 110)]  // plus que le stock
    [InlineData(1, 0, 0, 50, 50)]     // la seule cassette utile est bloquée
    [InlineData(5, 0, 5, 10, 60)]     // 50+10 impossible sans les 10
    [InlineData(0, 5, 5, 20, 70)]     // seulement 5 × 10 = 50 disponibles
    public void Cannot_dispense_when_no_exact_combination_exists(int s50, int s20, int s10, int? jammed, int amount)
    {
        var stock = N(s50, s20, s10);
        var account = Acc(250, 0);
        var (result, spy) = Run(account, amount, stock, Jam(jammed));
        AssertRefused(AtmStatus.CannotDispense, account, result, spy, stock);
    }

    [Fact]
    public void Jammed_cassette_counts_as_zero_even_if_full()
    {
        // 50 € existe en un billet, mais la cassette de 50 est bloquée : 20+20+10.
        var (result, spy) = Run(Acc(250, 0), 50, N(5, 5, 5), Denomination.Fifty);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(0, 2, 1), result.Notes);
        Assert.Equal(0, spy.Dispensed.Fifties);
        Assert.All(spy.DispenseCalls, c => Assert.Equal(0, c.Fifties));
    }

    // ---------- Combinaison : le moins de billets ----------

    [Theory]
    [InlineData(5, 5, 5, null, 60, 1, 0, 1)]   // 50+10 plutôt que 20×3
    [InlineData(1, 3, 0, null, 60, 0, 3, 0)]   // pas de 10 : 20×3
    [InlineData(5, 5, 5, null, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, null, 90, 1, 2, 0)]   // 50+20+20 plutôt que 50+20+10+10
    [InlineData(5, 5, 5, null, 100, 2, 0, 0)]
    [InlineData(5, 5, 5, null, 130, 2, 1, 1)]
    [InlineData(5, 5, 5, null, 160, 3, 0, 1)]
    [InlineData(5, 5, 5, null, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, null, 200, 4, 0, 0)]
    [InlineData(0, 5, 5, null, 100, 0, 5, 0)]
    [InlineData(1, 5, 5, null, 200, 1, 5, 5)]   // 11 billets, seule combinaison
    [InlineData(5, 5, 5, 20, 80, 1, 0, 3)]
    [InlineData(5, 5, 5, 10, 60, 0, 3, 0)]     // 50+10 interdit : 20×3
    [InlineData(5, 0, 5, null, 40, 0, 0, 4)]
    public void Combination_uses_fewest_notes(
        int s50, int s20, int s10, int? jammed, int amount, int n50, int n20, int n10)
    {
        var stock = N(s50, s20, s10);
        var expectedNotes = N(n50, n20, n10);
        var account = Acc(250, 0);
        var (result, spy) = Run(account, amount, stock, Jam(jammed));

        Assert.Equal(expectedNotes.Value, amount); // garde-fou sur les données du test
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedNotes, result.Notes);
        Assert.Equal(expectedNotes, spy.Dispensed);
        Assert.Equal(stock - expectedNotes, spy.Stock);
    }

    // ---------- Sortie : passages de 4 billets au plus ----------

    [Theory]
    [InlineData(5, 5, 5, null, 190)]  // 50×3 20×2 : 5 billets
    [InlineData(5, 5, 5, null, 180)]  // 50×3 20×1 10×1
    [InlineData(1, 5, 5, null, 200)]  // 50×1 20×5 10×5 : 11 billets
    [InlineData(0, 5, 5, null, 150)]  // 20×5 10×5 : 10 billets
    [InlineData(0, 2, 3, null, 70)]   // 20×2 10×3 : 5 billets
    [InlineData(0, 2, 4, null, 80)]   // 20×2 10×4 : 6 billets
    [InlineData(0, 1, 4, null, 60)]   // 20×1 10×4 : 5 billets
    [InlineData(5, 3, 2, 50, 80)]     // 20×3 10×2 : 5 billets
    [InlineData(5, 5, 5, 50, 150)]    // 20×5 10×5 : 10 billets
    [InlineData(5, 0, 5, null, 190)]  // 50×3 10×4 : 7 billets
    [InlineData(0, 4, 1, null, 90)]   // 20×4 10×1 : 5 billets
    public void Notes_leave_in_passes_of_at_most_four(int s50, int s20, int s10, int? jammed, int amount)
    {
        var stock = N(s50, s20, s10);
        var account = Acc(250, 0);
        var expectedNotes = FewestNotes(amount, Available(stock, Jam(jammed)))!.Value;

        var (result, spy) = Run(account, amount, stock, Jam(jammed));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedNotes, result.Notes);
        Assert.All(spy.DispenseCalls, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(expectedNotes, spy.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(stock - expectedNotes, spy.Stock);
        Assert.Equal(Acc(250 - amount - Fee(amount), amount), result.Account);
    }

    // ---------- Succès ----------

    [Fact]
    public void Success_updates_account_and_stock_and_never_loads()
    {
        var account = Acc(200, 40);
        var (result, spy) = Run(account, 130);

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 1, 1), 5, Acc(65, 170)), result);
        Assert.Equal(N(3, 4, 4), spy.Stock);
        Assert.Empty(spy.LoadCalls);
    }

    [Fact]
    public void Withdrawal_can_empty_the_balance_exactly()
    {
        var (result, _) = Run(Acc(115, 0), 110);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Acc(0, 110), result.Account);
    }
}
