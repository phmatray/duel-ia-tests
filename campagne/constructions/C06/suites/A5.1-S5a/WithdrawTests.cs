using static Atm.Tests.TestKit;

namespace Atm.Tests;

public class WithdrawTests
{
    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Negative_zero_or_non_multiple_of_ten_is_InvalidAmount(int amount)
    {
        var (result, _) = Run(250, 0, FullStock, null, Withdraw(amount));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void InvalidAmount_is_checked_before_the_daily_limit_and_the_funds()
    {
        // Plafond atteint, solde nul, aucun billet : seul le montant compte.
        var (result, _) = Run(0, 200, NoteCounts.None, null, Withdraw(-10));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    public void Exceeding_200_per_day_is_DailyLimitExceeded(int withdrawnToday, int amount)
    {
        var (result, _) = Run(250, withdrawnToday, FullStock, null, Withdraw(amount));

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Reaching_exactly_200_per_day_is_allowed(int withdrawnToday, int amount)
    {
        var (result, _) = Run(250, withdrawnToday, FullStock, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(200, result.Account.WithdrawnToday);
    }

    [Fact]
    public void The_fee_does_not_count_in_the_daily_limit()
    {
        // 90 + 110 = 200 : pile au plafond, les 5 € de frais n'y entrent pas.
        var (result, _) = Run(250, 90, FullStock, null, Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 1), 5, new AccountState(135, 200)), result);
    }

    [Fact]
    public void DailyLimit_is_checked_before_the_funds_and_the_notes()
    {
        var (result, _) = Run(0, 150, NoteCounts.None, null, Withdraw(60));

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    // ---- Les frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_is_5_only_when_amount_is_strictly_above_100(int amount, int fee)
    {
        var (result, _) = Run(250, 0, FullStock, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(250 - amount - fee, result.Account.Balance);
    }

    [Theory]
    [InlineData(100, 50)]
    [InlineData(60, 50)]
    [InlineData(150, 50)]
    [InlineData(100, 100)]
    public void Fee_depends_on_the_amount_alone_not_on_what_was_already_withdrawn(int withdrawnToday, int amount)
    {
        var (result, _) = Run(250, withdrawnToday, FullStock, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250 - amount, withdrawnToday + amount), result.Account);
    }

    // ---- Contrôle 3 : solde ----

    [Theory]
    [InlineData(50, 60)]
    [InlineData(100, 110)]   // 110 + 5 > 110
    [InlineData(200, 200)]   // 200 + 5 > 200
    [InlineData(0, 10)]
    public void Amount_plus_fee_above_balance_is_InsufficientFunds(int balance, int amount)
    {
        var (result, _) = Run(balance, 0, FullStock, null, Withdraw(amount));

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    [Theory]
    [InlineData(60, 60, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 0)]
    [InlineData(205, 200, 0)]
    public void Amount_plus_fee_equal_to_balance_is_allowed(int balance, int amount, int balanceAfter)
    {
        var (result, _) = Run(balance, 0, FullStock, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(balanceAfter, result.Account.Balance);
    }

    [Fact]
    public void Balance_exactly_covering_a_small_withdrawal_after_earlier_withdrawals_is_allowed()
    {
        // 50 ≤ 100 : pas de frais, donc 50 € suffisent même avec 60 € déjà retirés.
        var (result, _) = Run(50, 60, FullStock, null, Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 0, 0), 0, new AccountState(0, 110)), result);
    }

    [Fact]
    public void InsufficientFunds_is_checked_before_the_notes()
    {
        var (result, _) = Run(10, 0, NoteCounts.None, null, Withdraw(50));

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    // ---- Contrôle 4 : billets ----

    [Theory]
    [InlineData(0, 0, 0, null, 10)]
    [InlineData(1, 0, 0, null, 30)]                 // une seule coupure de 50
    [InlineData(0, 5, 0, null, 30)]                 // il faut un billet de 10
    [InlineData(0, 0, 5, null, 60)]                 // 5 × 10 ne font que 50
    [InlineData(1, 1, 1, null, 90)]                 // 80 au plus
    [InlineData(0, 5, 5, Denomination.Ten, 30)]     // le 10 est bloqué
    [InlineData(5, 0, 0, Denomination.Fifty, 50)]
    [InlineData(1, 5, 0, Denomination.Twenty, 70)]  // 50 seul, il manque 20
    public void No_exact_combination_of_available_notes_is_CannotDispense(
        int s50, int s20, int s10, Denomination? jammed, int amount)
    {
        var (result, _) = Run(250, 0, Notes(s50, s20, s10), jammed, Withdraw(amount));

        Assert.Equal(AtmStatus.CannotDispense, result.Status);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 2, 1, 50)]
    [InlineData(Denomination.Twenty, 1, 0, 1, 60)]
    [InlineData(Denomination.Ten, 1, 1, 0, 70)]
    public void The_jammed_cassette_counts_for_zero_but_others_are_used(
        Denomination jammed, int n50, int n20, int n10, int amount)
    {
        var (result, dispenser) = Run(250, 0, FullStock, jammed, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(n50, n20, n10), result.Notes);
        Assert.Equal(0, result.Notes.Of(jammed));
        Assert.Equal(5, dispenser.Stock.Of(jammed));
    }

    // ---- La combinaison : le moins de billets ----

    [Theory]
    [InlineData(5, 5, 5, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]   // 50+10 plutôt que 20×3
    [InlineData(5, 5, 5, 80, 1, 1, 1)]   // 50+20+10 plutôt que 20×4
    [InlineData(5, 5, 5, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 200, 4, 0, 0)]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // pas de 10 : 20×3
    [InlineData(1, 5, 0, 80, 0, 4, 0)]   // 50 inutilisable sans 10
    [InlineData(3, 0, 5, 80, 1, 0, 3)]   // pas de 20
    [InlineData(0, 5, 5, 150, 0, 5, 5)]  // 10 billets
    [InlineData(1, 1, 5, 120, 1, 1, 5)]
    [InlineData(2, 0, 1, 60, 1, 0, 1)]
    public void Ok_dispenses_the_combination_with_the_fewest_notes(
        int s50, int s20, int s10, int amount, int n50, int n20, int n10)
    {
        var (result, dispenser) = Run(250, 0, Notes(s50, s20, s10), null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(n50, n20, n10), result.Notes);
        Assert.Equal(Notes(s50 - n50, s20 - n20, s10 - n10), dispenser.Stock);
    }

    // ---- La sortie ----

    [Theory]
    [InlineData(0, 5, 5, 150)]  // 10 billets
    [InlineData(4, 0, 0, 200)]  // 4 billets
    [InlineData(5, 5, 5, 190)]  // 50×3 20×2 : 5 billets
    [InlineData(2, 2, 1, 150)]  // 5 billets
    [InlineData(1, 1, 1, 80)]
    public void Notes_leave_in_non_empty_passes_of_at_most_4_through_the_module(int s50, int s20, int s10, int amount)
    {
        var (result, dispenser) = Run(250, 0, Notes(s50, s20, s10), null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.NotEmpty(dispenser.Dispensed);
        Assert.All(dispenser.Dispensed, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(result.Notes, dispenser.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(result.Notes.Value, amount);
        Assert.Empty(dispenser.Loaded);
    }

    // ---- Le succès ----

    [Theory]
    [InlineData(250, 0, 50)]
    [InlineData(250, 100, 100)]
    [InlineData(250, 0, 150)]
    [InlineData(155, 40, 150)]
    public void Ok_debits_amount_plus_fee_and_adds_amount_to_the_day(int balance, int withdrawnToday, int amount)
    {
        var fee = amount > 100 ? 5 : 0;

        var (result, _) = Run(balance, withdrawnToday, FullStock, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(amount, result.Notes.Value);
        Assert.Equal(new AccountState(balance - amount - fee, withdrawnToday + amount), result.Account);
    }
}
