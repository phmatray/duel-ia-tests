using static Atm.Tests.N;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = C(5, 5, 5);

    private static (AtmResult Result, MoneyDispenser Dispenser) Withdraw(
        int balance, int withdrawn, int amount, NoteCounts? stock = null, Denomination? jam = null)
    {
        var dispenser = new MoneyDispenser(stock ?? Full, jam);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));
        return (result, dispenser);
    }

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Invalid_amount_is_refused(int amount)
    {
        var (r, _) = Withdraw(250, 0, amount);
        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Invalid_amount_is_checked_before_everything_else(int amount)
    {
        // Plafond atteint, solde nul, stock vide, cassette bloquée : c'est quand même InvalidAmount.
        var (r, _) = Withdraw(0, 200, amount, C(0, 0, 0), Denomination.Ten);
        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    [InlineData(100, 100)]
    public void Reaching_exactly_the_daily_limit_is_allowed(int withdrawn, int amount)
    {
        var (r, _) = Withdraw(250, withdrawn, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(withdrawn + amount, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    public void Exceeding_the_daily_limit_is_refused(int withdrawn, int amount)
    {
        var (r, _) = Withdraw(250, withdrawn, amount);
        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    [Fact]
    public void Fee_does_not_count_in_the_daily_limit()
    {
        // 90 + 110 = 200 : pile le plafond, les 5 € de frais n'y comptent pas.
        var (r, _) = Withdraw(250, 90, 110);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(135, 200), r.Account);
    }

    [Fact]
    public void Daily_limit_is_checked_before_funds()
    {
        var (r, _) = Withdraw(0, 150, 60);
        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    [Fact]
    public void Daily_limit_is_checked_before_dispensability()
    {
        var (r, _) = Withdraw(250, 150, 60, C(0, 0, 0));
        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    // ---- Contrôle 3 : solde ----

    [Theory]
    [InlineData(55, 60)]
    [InlineData(0, 10)]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(150, 150)]
    [InlineData(200, 200)]
    public void Insufficient_funds_is_refused(int balance, int amount)
    {
        var (r, _) = Withdraw(balance, 0, amount);
        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    [Theory]
    [InlineData(60, 60, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 5)]
    [InlineData(155, 150, 5)]
    [InlineData(205, 200, 5)]
    public void Balance_exactly_covering_amount_and_fee_is_enough(int balance, int amount, int fee)
    {
        var (r, _) = Withdraw(balance, 0, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(0, r.Account.Balance);
    }

    [Fact]
    public void Funds_are_checked_before_dispensability()
    {
        var (r, _) = Withdraw(50, 0, 60, C(0, 0, 0));
        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    // ---- Frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_is_5_strictly_above_100(int amount, int fee)
    {
        var (r, _) = Withdraw(250, 0, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(250 - amount - fee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    // ---- Contrôle 4 : combinaison ----

    [Theory]
    [InlineData(30, 0, 5, 0)]   // 30 avec seulement des 20
    [InlineData(10, 5, 5, 0)]   // pas de 10
    [InlineData(60, 1, 0, 0)]   // un seul 50, ni 20 ni 10
    [InlineData(110, 2, 0, 0)]  // 50×2 = 100, pas de 10
    [InlineData(10, 0, 0, 0)]   // stock vide
    public void Cannot_dispense_when_no_exact_combination(int amount, int fifties, int twenties, int tens)
    {
        var (r, _) = Withdraw(250, 0, amount, C(fifties, twenties, tens));
        Assert.Equal(AtmStatus.CannotDispense, r.Status);
    }

    [Fact]
    public void Cannot_dispense_from_empty_stock()
    {
        var (r, _) = Withdraw(250, 0, 10, C(0, 0, 0));
        Assert.Equal(AtmStatus.CannotDispense, r.Status);
    }

    [Fact]
    public void Cannot_dispense_more_than_the_stock()
    {
        // Stock total = 50+20+10 = 80.
        var (r, _) = Withdraw(250, 0, 90, C(1, 1, 1));
        Assert.Equal(AtmStatus.CannotDispense, r.Status);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 20, 0, 1, 0)]
    [InlineData(Denomination.Ten, 10, 0, 0, 1)]
    [InlineData(Denomination.Ten, 30, 0, 1, 5)]
    public void Jammed_cassette_counts_as_zero(Denomination jam, int amount, int fifties, int twenties, int tens)
    {
        var (r, d) = Withdraw(250, 0, amount, C(fifties, twenties, tens), jam);
        Assert.Equal(AtmStatus.CannotDispense, r.Status);
        Assert.Equal(C(fifties, twenties, tens), d.Stock);
    }

    [Theory]
    [InlineData(Denomination.Ten, 60, 0, 3, 0)]     // 50+10 impossible -> 20×3
    [InlineData(Denomination.Fifty, 100, 0, 5, 0)]  // 50×2 impossible -> 20×5
    [InlineData(Denomination.Twenty, 70, 1, 0, 2)]  // 50+20 impossible -> 50+10×2
    public void Jammed_cassette_is_bypassed_when_possible(Denomination jam, int amount, int fifties, int twenties, int tens)
    {
        var (r, d) = Withdraw(250, 0, amount, Full, jam);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(fifties, twenties, tens), r.Notes);
        Assert.Equal(Full - r.Notes, d.Stock);
    }

    [Theory]
    [InlineData(60, 1, 0, 1)]    // et pas 20×3
    [InlineData(80, 1, 1, 1)]    // et pas 20×4
    [InlineData(40, 0, 2, 0)]    // et pas 10×4
    [InlineData(90, 1, 2, 0)]
    [InlineData(100, 2, 0, 0)]
    [InlineData(130, 2, 1, 1)]
    [InlineData(200, 4, 0, 0)]
    public void Combination_uses_the_fewest_notes(int amount, int fifties, int twenties, int tens)
    {
        var (r, d) = Withdraw(250, 0, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(fifties, twenties, tens), r.Notes);
        Assert.Equal(Full - r.Notes, d.Stock);
    }

    [Theory]
    [InlineData(60, 0, 5, 0, 0, 3, 0)]   // pas de 50 ni de 10
    [InlineData(150, 1, 5, 5, 1, 5, 0)]  // un seul 50 : 50 + 20×5
    [InlineData(150, 0, 5, 5, 0, 5, 5)]  // aucun 50 : 20×5 + 10×5
    [InlineData(100, 1, 0, 5, 1, 0, 5)]  // 50 + 10×5
    [InlineData(90, 1, 5, 1, 1, 2, 0)]   // 50 + 20×2
    [InlineData(120, 1, 1, 5, 1, 1, 5)]  // 50 + 20 + 10×5
    [InlineData(60, 3, 3, 0, 0, 3, 0)]   // pas de 10 : 20×3, malgré les 50
    public void Combination_is_limited_by_the_stock(
        int amount, int sf, int st, int s10, int fifties, int twenties, int tens)
    {
        var stock = C(sf, st, s10);
        var (r, d) = Withdraw(250, 0, amount, stock);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(fifties, twenties, tens), r.Notes);
        Assert.Equal(stock - r.Notes, d.Stock);
    }

    // ---- Sortie : passages de 4 billets au plus ----

    [Theory]
    [InlineData(150, 0, 5, 5)]   // 10 billets
    [InlineData(70, 0, 1, 5)]    // 20 + 10×5 = 6 billets
    [InlineData(200, 1, 5, 5)]   // 50 + 20×5 + 10×5 = 11 billets
    [InlineData(80, 1, 1, 1)]
    [InlineData(200, 5, 5, 5)]   // 4 billets
    public void Notes_leave_in_passes_of_at_most_4(int amount, int sf, int st, int s10)
    {
        var stock = C(sf, st, s10);
        var spy = new SpyDispenser(new MoneyDispenser(stock));
        var r = new CashMachine(spy).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Oracle.Combination(amount, stock), r.Notes);
        Assert.NotEmpty(spy.DispenseCalls);
        Assert.All(spy.DispenseCalls, call =>
        {
            Assert.True(call.Result, $"passage refusé par le module : {call.Notes}");
            Assert.InRange(call.Notes.Total, 1, 4);
        });
        var total = spy.DispenseCalls.Aggregate(NoteCounts.None, (acc, c) => acc + c.Notes);
        Assert.Equal(r.Notes, total);
        Assert.Empty(spy.LoadCalls);
        Assert.Equal(stock - r.Notes, spy.Stock);
    }
}
