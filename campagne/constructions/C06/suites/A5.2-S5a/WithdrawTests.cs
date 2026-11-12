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
    public void Negative_zero_or_non_multiple_of_10_is_InvalidAmount(int amount)
    {
        var result = Withdraw(Acc(250, 0), Full, null, amount);

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(220)]
    public void Multiples_of_10_are_not_InvalidAmount(int amount)
    {
        var result = Withdraw(Acc(250, 0), Full, null, amount);

        Assert.NotEqual(AtmStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void InvalidAmount_is_checked_before_the_daily_limit_and_funds()
    {
        // Plafond atteint, solde nul, stock vide : c'est quand même le contrôle 1 qui parle.
        var result = Withdraw(Acc(0, 200), N(0, 0, 0), Denomination.Ten, -10);

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 200, AtmStatus.Ok)]
    [InlineData(0, 210, AtmStatus.DailyLimitExceeded)]
    [InlineData(0, 220, AtmStatus.DailyLimitExceeded)]
    [InlineData(150, 50, AtmStatus.Ok)]
    [InlineData(150, 60, AtmStatus.DailyLimitExceeded)]
    [InlineData(190, 10, AtmStatus.Ok)]
    [InlineData(200, 10, AtmStatus.DailyLimitExceeded)]
    public void Daily_limit_is_200_inclusive(int withdrawnToday, int amount, AtmStatus expected)
    {
        var result = Withdraw(Acc(250, withdrawnToday), Full, null, amount);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Fee_does_not_count_toward_the_daily_limit()
    {
        // 90 + 110 = 200 tout juste ; les 5 € de frais n'entrent pas dans le plafond.
        var result = Withdraw(Acc(250, 90), Full, null, 110);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Acc(135, 200), result.Account);
    }

    [Fact]
    public void Daily_limit_is_checked_before_funds_and_dispensability()
    {
        var result = Withdraw(Acc(0, 200), N(0, 0, 0), null, 10);

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    // ---- Contrôle 3 : fonds et frais ----

    [Theory]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(10, 0)]
    [InlineData(200, 5)]
    public void Fee_is_5_strictly_above_100_else_0(int amount, int expectedFee)
    {
        var result = Withdraw(Acc(250, 0), Full, null, amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(250 - amount - expectedFee, result.Account.Balance);
        Assert.Equal(amount, result.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(100, 100, AtmStatus.Ok)]
    [InlineData(95, 100, AtmStatus.InsufficientFunds)]
    [InlineData(115, 110, AtmStatus.Ok)]
    [InlineData(110, 110, AtmStatus.InsufficientFunds)]
    [InlineData(205, 200, AtmStatus.Ok)]
    [InlineData(200, 200, AtmStatus.InsufficientFunds)]
    [InlineData(0, 10, AtmStatus.InsufficientFunds)]
    public void Amount_plus_fee_must_not_exceed_balance(int balance, int amount, AtmStatus expected)
    {
        var result = Withdraw(Acc(balance, 0), Full, null, amount);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Funds_are_checked_before_dispensability()
    {
        var result = Withdraw(Acc(10, 0), N(0, 0, 0), null, 50);

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    // ---- Contrôle 4 : combinaison ----

    [Theory]
    [InlineData(0, 0, 0, 0, 10)]
    [InlineData(1, 0, 0, 0, 10)]   // 50 ne fait pas 10
    [InlineData(0, 1, 0, 0, 10)]   // 20 ne fait pas 10
    [InlineData(1, 1, 0, 0, 30)]   // ni 50, ni 20, ni 70 ne font 30
    [InlineData(0, 5, 0, 0, 110)]  // 20×5 = 100 < 110 et pas de 10
    public void No_exact_combination_is_CannotDispense(int s50, int s20, int s10, int jam, int amount)
    {
        var result = Withdraw(Acc(250, 0), N(s50, s20, s10), Jam(jam), amount);

        Assert.Equal(AtmStatus.CannotDispense, result.Status);
    }

    [Theory]
    [InlineData(1, 0, 0, 50, 50)]
    [InlineData(0, 1, 0, 20, 20)]
    [InlineData(0, 0, 1, 10, 10)]
    [InlineData(1, 1, 1, 50, 50)]
    [InlineData(1, 1, 1, 20, 20)]
    [InlineData(1, 1, 1, 10, 10)]
    public void Jammed_cassette_counts_as_zero(int s50, int s20, int s10, int jam, int amount)
    {
        var (machine, dispenser) = Machine(N(s50, s20, s10), Jam(jam));

        var result = machine.Execute(Acc(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.CannotDispense, result.Status);
        Assert.Equal(N(s50, s20, s10), dispenser.Stock);
    }

    [Fact]
    public void Jammed_cassette_is_bypassed_with_other_notes()
    {
        // 50 bloquée : 50 = 20×2 + 10×1
        var result = Withdraw(Acc(250, 0), N(5, 5, 5), Denomination.Fifty, 50);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(0, 2, 1), result.Notes);
    }

    [Theory]
    // stock, montant, combinaison attendue (le moins de billets)
    [InlineData(5, 5, 5, 60, 1, 0, 1)]   // 50+10, pas 20×3
    [InlineData(0, 5, 5, 60, 0, 3, 0)]   // 20×3, pas 20×2+10×2
    [InlineData(5, 5, 5, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, 90, 1, 2, 0)]   // 50+20+20, pas 50+20+10+10
    [InlineData(5, 5, 5, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 200, 4, 0, 0)]
    [InlineData(1, 5, 5, 200, 1, 5, 5)]  // 50+100+50
    [InlineData(1, 5, 0, 60, 0, 3, 0)]   // pas de 10 : 20×3
    [InlineData(5, 3, 0, 60, 0, 3, 0)]   // 50 inutilisable sans 10
    [InlineData(3, 4, 5, 160, 3, 0, 1)]  // 150+10 (4 billets), pas 100+60 (5 billets)
    [InlineData(2, 4, 5, 160, 2, 3, 0)]  // 100+60 (5 billets), pas 100+20+20+10+10 ou autre
    [InlineData(5, 0, 5, 40, 0, 0, 4)]
    [InlineData(2, 5, 0, 110, 1, 3, 0)]  // 50+60 = 4 billets
    public void Combination_uses_the_fewest_notes(int s50, int s20, int s10, int amount, int n50, int n20, int n10)
    {
        var (machine, dispenser) = Machine(N(s50, s20, s10));

        var result = machine.Execute(Acc(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(n50, n20, n10), result.Notes);
        Assert.Equal(N(s50, s20, s10) - N(n50, n20, n10), dispenser.Stock);
    }

    // ---- Sortie ----

    [Theory]
    [InlineData(5, 5, 5, 200)]  // 50×4
    [InlineData(1, 5, 5, 200)]  // 11 billets
    [InlineData(0, 5, 5, 150)]  // 10 billets
    [InlineData(0, 0, 5, 50)]
    public void Notes_go_out_in_passes_of_at_most_4_through_the_module(int s50, int s20, int s10, int amount)
    {
        var spy = new SpyDispenser(N(s50, s20, s10));
        var machine = new CashMachine(spy);

        var result = machine.Execute(Acc(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.NotEmpty(spy.DispenseCalls);
        Assert.All(spy.DispenseCalls, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(result.Notes, spy.DispenseCalls.Aggregate(NoteCounts.None, (acc, p) => acc + p));
        Assert.Equal(N(s50, s20, s10) - result.Notes, spy.Stock);
        Assert.Empty(spy.LoadCalls);
    }

    [Fact]
    public void Largest_possible_withdrawal_succeeds()
    {
        // 15 billets disponibles, 200 € max : 10×5 + 20×5 + 50×1 = 200 → 11 billets, 3 passages au moins.
        var result = Withdraw(Acc(250, 0), N(1, 5, 5), null, 200);

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 5, 5), 5, Acc(45, 200)), result);
    }

    // ---- Succès ----

    [Fact]
    public void Success_updates_account_and_returns_notes_and_fee()
    {
        var (machine, dispenser) = Machine(N(2, 2, 2));

        var result = machine.Execute(Acc(200, 40), new Operation.Withdraw(120));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 1, 0), 5, Acc(75, 160)), result);
        Assert.Equal(N(0, 1, 2), dispenser.Stock);
    }

    // ---- Refus : rien ne change ----

    [Theory]
    [InlineData(250, 0, 5, 5, 5, 0, -10)]   // InvalidAmount
    [InlineData(250, 200, 5, 5, 5, 0, 10)]  // DailyLimitExceeded
    [InlineData(5, 0, 5, 5, 5, 0, 10)]      // InsufficientFunds
    [InlineData(250, 0, 5, 0, 0, 50, 50)]   // CannotDispense
    [InlineData(250, 0, 0, 5, 0, 0, 30)]    // CannotDispense
    public void Refusal_changes_nothing(int balance, int withdrawn, int s50, int s20, int s10, int jam, int amount)
    {
        var spy = new SpyDispenser(N(s50, s20, s10), Jam(jam));
        var machine = new CashMachine(spy);

        var result = machine.Execute(Acc(balance, withdrawn), new Operation.Withdraw(amount));

        Assert.NotEqual(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(balance, withdrawn), result.Account);
        Assert.Equal(N(s50, s20, s10), spy.Stock);
        Assert.Empty(spy.DispenseCalls);
        Assert.Empty(spy.LoadCalls);
    }
}
