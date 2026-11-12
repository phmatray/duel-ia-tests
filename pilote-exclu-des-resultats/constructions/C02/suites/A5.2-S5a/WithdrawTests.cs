using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Invalid_amount_is_refused(int amount)
    {
        var dispenser = new SpyDispenser(Full);
        var account = Acc(250, 0);

        var result = new CashMachine(dispenser).Execute(account, W(amount));

        AssertRefusal(result, AtmStatus.InvalidAmount, account, dispenser, Full);
    }

    [Fact]
    public void Invalid_amount_is_checked_before_daily_limit_funds_and_stock()
    {
        var dispenser = new SpyDispenser(N(0, 0, 0));
        var account = Acc(0, 200);

        var result = new CashMachine(dispenser).Execute(account, W(5));

        AssertRefusal(result, AtmStatus.InvalidAmount, account, dispenser, N(0, 0, 0));
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(190, 20)]
    [InlineData(200, 10)]
    public void Exceeding_daily_limit_is_refused(int withdrawnToday, int amount)
    {
        var dispenser = new SpyDispenser(Full);
        var account = Acc(250, withdrawnToday);

        var result = new CashMachine(dispenser).Execute(account, W(amount));

        AssertRefusal(result, AtmStatus.DailyLimitExceeded, account, dispenser, Full);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(100, 100)]
    [InlineData(190, 10)]
    public void Reaching_exactly_the_daily_limit_is_allowed(int withdrawnToday, int amount)
    {
        var dispenser = new SpyDispenser(Full);
        var account = Acc(250, withdrawnToday);

        var result = new CashMachine(dispenser).Execute(account, W(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(withdrawnToday + amount, result.Account.WithdrawnToday);
    }

    [Fact]
    public void Daily_limit_is_checked_before_funds_and_stock()
    {
        var dispenser = new SpyDispenser(N(0, 0, 0));
        var account = Acc(0, 150);

        var result = new CashMachine(dispenser).Execute(account, W(60));

        AssertRefusal(result, AtmStatus.DailyLimitExceeded, account, dispenser, N(0, 0, 0));
    }

    [Fact]
    public void Fee_does_not_count_towards_daily_limit()
    {
        // 200 + 5 € de frais : le plafond porte sur amount seul.
        var dispenser = new SpyDispenser(Full);

        var result = new CashMachine(dispenser).Execute(Acc(205, 0), W(200));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Acc(0, 200), result.Account);
    }

    // ---- Contrôle 3 : solde insuffisant ----

    [Theory]
    [InlineData(50, 60)]
    [InlineData(0, 10)]
    [InlineData(100, 110)] // 110 > 100 même sans frais
    [InlineData(110, 110)] // 110 + 5 > 110
    [InlineData(200, 200)] // 200 + 5 > 200
    public void Insufficient_funds_is_refused(int balance, int amount)
    {
        var dispenser = new SpyDispenser(Full);
        var account = Acc(balance, 0);

        var result = new CashMachine(dispenser).Execute(account, W(amount));

        AssertRefusal(result, AtmStatus.InsufficientFunds, account, dispenser, Full);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(100, 100)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    public void Balance_exactly_covering_amount_plus_fee_is_enough(int balance, int amount)
    {
        var dispenser = new SpyDispenser(Full);

        var result = new CashMachine(dispenser).Execute(Acc(balance, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Account.Balance);
    }

    [Fact]
    public void Insufficient_funds_is_checked_before_stock()
    {
        var dispenser = new SpyDispenser(N(0, 0, 0));
        var account = Acc(10, 0);

        var result = new CashMachine(dispenser).Execute(account, W(50));

        AssertRefusal(result, AtmStatus.InsufficientFunds, account, dispenser, N(0, 0, 0));
    }

    // ---- Les frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_is_5_only_when_amount_is_strictly_above_100(int amount, int expectedFee)
    {
        var dispenser = new SpyDispenser(Full);

        var result = new CashMachine(dispenser).Execute(Acc(250, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(250 - amount - expectedFee, result.Account.Balance);
    }

    [Theory]
    [InlineData(50, 60)]
    [InlineData(100, 100)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Fee_depends_on_amount_only_not_on_what_was_already_withdrawn(int withdrawnToday, int amount)
    {
        var dispenser = new SpyDispenser(Full);

        var result = new CashMachine(dispenser).Execute(Acc(250, withdrawnToday), W(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(250 - amount, withdrawnToday + amount), result.Account);
    }

    [Fact]
    public void Fee_free_withdrawal_with_balance_exactly_amount_succeeds_even_if_already_withdrawn()
    {
        var dispenser = new SpyDispenser(Full);

        var result = new CashMachine(dispenser).Execute(Acc(60, 50), W(60));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(0, 110), result.Account);
    }

    // ---- Contrôle 4 : combinaison impossible ----

    [Theory]
    [InlineData(0, 0, 0, 0, 10)]
    [InlineData(1, 0, 0, 0, 10)]   // seul un 50 : pas de 10
    [InlineData(0, 5, 0, 0, 10)]   // seuls des 20
    [InlineData(0, 5, 0, 0, 30)]
    [InlineData(1, 0, 0, 50, 50)]  // le 50 est bloqué
    [InlineData(5, 0, 0, 0, 60)]
    [InlineData(2, 0, 5, 10, 70)]  // les 10 bloqués : 50+? impossible
    [InlineData(0, 5, 5, 20, 60)]  // 20 bloqués, 10×5 = 50 seulement
    public void No_exact_combination_is_refused(int fifties, int twenties, int tens, int jammed, int amount)
    {
        var stock = N(fifties, twenties, tens);
        var dispenser = new SpyDispenser(stock, Jam(jammed));
        var account = Acc(250, 0);

        var result = new CashMachine(dispenser).Execute(account, W(amount));

        AssertRefusal(result, AtmStatus.CannotDispense, account, dispenser, stock);
    }

    [Fact]
    public void Total_stock_value_too_low_is_refused()
    {
        var stock = N(1, 1, 1); // 80 €
        var dispenser = new SpyDispenser(stock);
        var account = Acc(250, 0);

        var result = new CashMachine(dispenser).Execute(account, W(90));

        AssertRefusal(result, AtmStatus.CannotDispense, account, dispenser, stock);
    }

    // ---- La combinaison : le moins de billets parmi les billets disponibles ----

    [Theory]
    [InlineData(5, 5, 5, 0, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 0, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 0, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 0, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 0, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, 0, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 0, 100, 2, 0, 0)]
    [InlineData(5, 5, 5, 0, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, 0, 200, 4, 0, 0)]
    [InlineData(1, 3, 0, 0, 60, 0, 3, 0)]   // pas de 10 : 20×3 au lieu de 50+10
    [InlineData(1, 5, 0, 0, 80, 0, 4, 0)]   // 50+?30 impossible sans 10 -> 20×4
    [InlineData(0, 5, 5, 0, 150, 0, 5, 5)]  // pas de 50
    [InlineData(1, 0, 5, 0, 100, 1, 0, 5)]  // un seul 50
    [InlineData(5, 0, 1, 0, 110, 2, 0, 1)]
    [InlineData(0, 0, 5, 0, 50, 0, 0, 5)]
    public void Chooses_combination_with_fewest_notes_among_available(
        int fifties, int twenties, int tens, int jammed, int amount, int eF, int eT, int eN)
    {
        var stock = N(fifties, twenties, tens);
        var dispenser = new SpyDispenser(stock, Jam(jammed));
        var expected = N(eF, eT, eN);

        var result = new CashMachine(dispenser).Execute(Acc(250, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    [Theory]
    [InlineData(50, 2, 2, 2, 40, 0, 2, 0)]   // 50 bloquée
    [InlineData(50, 5, 1, 2, 40, 0, 1, 2)]
    [InlineData(20, 2, 5, 3, 60, 1, 0, 1)]   // 20 bloquée
    [InlineData(20, 1, 5, 5, 40, 0, 0, 4)]
    [InlineData(10, 3, 5, 5, 60, 0, 3, 0)]   // 10 bloquée : 20×3 au lieu de 50+10
    public void Jammed_cassette_counts_as_empty(int jammed, int fifties, int twenties, int tens, int amount, int eF, int eT, int eN)
    {
        var stock = N(fifties, twenties, tens);
        var dispenser = new SpyDispenser(stock, (Denomination)jammed);
        var expected = N(eF, eT, eN);

        var result = new CashMachine(dispenser).Execute(Acc(250, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Notes.Of((Denomination)jammed));
        Assert.Equal(expected, result.Notes);
        Assert.Equal(stock - expected, dispenser.Stock);
        Assert.DoesNotContain(dispenser.DispenseCalls, pass => pass.Of((Denomination)jammed) > 0);
    }

    // ---- La sortie ----

    [Theory]
    [InlineData(5, 5, 5, 200)]  // 50×4
    [InlineData(0, 5, 5, 150)]  // 20×5 10×5 : 10 billets
    [InlineData(0, 5, 0, 100)]  // 20×5 : 5 billets
    [InlineData(0, 0, 5, 50)]   // 10×5
    [InlineData(3, 5, 1, 200)]  // 50×3 20×2 10×1 : 6 billets
    [InlineData(5, 5, 5, 10)]
    public void Notes_come_out_in_passes_of_at_most_4_notes(int fifties, int twenties, int tens, int amount)
    {
        var stock = N(fifties, twenties, tens);
        var dispenser = new SpyDispenser(stock);

        var result = new CashMachine(dispenser).Execute(Acc(250, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.NotEmpty(dispenser.DispenseCalls);
        Assert.All(dispenser.DispenseCalls, pass =>
        {
            Assert.InRange(pass.Total, 1, 4);
            Assert.True(pass.Fifties >= 0 && pass.Twenties >= 0 && pass.Tens >= 0);
        });
        Assert.Equal(result.Notes, Sum(dispenser.DispenseCalls));
        Assert.Equal(amount, result.Notes.Value);
        Assert.Equal(stock - result.Notes, dispenser.Stock);
        Assert.Empty(dispenser.LoadCalls);
    }

    // ---- Le succès ----

    [Fact]
    public void Successful_withdrawal_updates_account_and_returns_notes_and_fee()
    {
        var dispenser = new SpyDispenser(Full);

        var result = new CashMachine(dispenser).Execute(Acc(200, 40), W(130));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(2, 1, 1), result.Notes);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Acc(65, 170), result.Account);
        Assert.Equal(N(3, 4, 4), dispenser.Stock);
    }

    [Fact]
    public void Withdrawal_works_with_the_real_module()
    {
        var dispenser = new MoneyDispenser(N(1, 3, 0));

        var result = new CashMachine(dispenser).Execute(Acc(100, 0), W(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, Acc(40, 60)), result);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }
}
