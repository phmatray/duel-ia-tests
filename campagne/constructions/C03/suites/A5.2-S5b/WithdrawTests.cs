using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    // --- Contrôle 1 : montant invalide ---

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Negative_zero_or_non_multiple_of_ten_is_invalid(int amount)
    {
        var (machine, dispenser) = Build(Full);

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(Reference.Refused(AtmStatus.InvalidAmount, Acc(250, 0)), result);
        Assert.Empty(dispenser.Dispensed);
        Assert.Empty(dispenser.Loaded);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Invalid_amount_is_checked_before_everything_else(int amount)
    {
        // Plafond atteint, solde nul, stock vide : tous les autres contrôles échoueraient aussi.
        var (machine, _) = Build(N(0, 0, 0));

        var result = machine.Execute(Acc(0, 200), Withdraw(amount));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    // --- Contrôle 2 : plafond du jour ---

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Reaching_exactly_the_daily_limit_is_allowed(int withdrawnToday, int amount)
    {
        var (machine, _) = Build(Full);

        var result = machine.Execute(Acc(250, withdrawnToday), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(withdrawnToday + amount, result.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    public void Exceeding_the_daily_limit_is_refused(int withdrawnToday, int amount)
    {
        var (machine, dispenser) = Build(Full);

        var result = machine.Execute(Acc(250, withdrawnToday), Withdraw(amount));

        Assert.Equal(Reference.Refused(AtmStatus.DailyLimitExceeded, Acc(250, withdrawnToday)), result);
        Assert.Empty(dispenser.Dispensed);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Fee_does_not_count_toward_daily_limit()
    {
        // 90 + 110 = 200 : pile au plafond, les 5 € de frais n'y entrent pas.
        var (machine, _) = Build(Full);

        var result = machine.Execute(Acc(250, 90), Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, Acc(135, 200)), result);
    }

    [Fact]
    public void Daily_limit_is_checked_before_funds_and_notes()
    {
        var (machine, _) = Build(N(0, 0, 0));

        var result = machine.Execute(Acc(0, 200), Withdraw(10));

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    // --- Frais ---

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_is_5_only_strictly_above_100(int amount, int expectedFee)
    {
        var (machine, _) = Build(Full);

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(250 - amount - expectedFee, result.Account.Balance);
        Assert.Equal(amount, result.Account.WithdrawnToday);
    }

    // --- Contrôle 3 : solde ---

    [Theory]
    [InlineData(100, 100)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    [InlineData(10, 10)]
    public void Amount_plus_fee_exactly_equal_to_balance_is_allowed(int balance, int amount)
    {
        var (machine, _) = Build(Full);

        var result = machine.Execute(Acc(balance, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Account.Balance);
    }

    [Theory]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(200, 200)]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    public void Amount_plus_fee_above_balance_is_refused(int balance, int amount)
    {
        var (machine, dispenser) = Build(Full);

        var result = machine.Execute(Acc(balance, 0), Withdraw(amount));

        Assert.Equal(Reference.Refused(AtmStatus.InsufficientFunds, Acc(balance, 0)), result);
        Assert.Empty(dispenser.Dispensed);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Funds_are_checked_before_notes()
    {
        var (machine, _) = Build(N(0, 0, 0));

        var result = machine.Execute(Acc(0, 0), Withdraw(10));

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    // --- Contrôle 4 : billets ---

    [Theory]
    [InlineData(0, 0, 0, 0, 10)]
    [InlineData(1, 0, 0, 0, 30)]   // pas de 20 ni de 10
    [InlineData(0, 5, 0, 0, 30)]   // 20 seuls ne font pas 30
    [InlineData(0, 0, 5, 0, 60)]   // pas assez de 10
    public void No_exact_combination_cannot_dispense(int fifties, int twenties, int tens, int jam, int amount)
    {
        var stock = N(fifties, twenties, tens);
        var (machine, dispenser) = Build(stock, Jam(jam));

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(Reference.Refused(AtmStatus.CannotDispense, Acc(250, 0)), result);
        Assert.Empty(dispenser.Dispensed);
        Assert.Equal(stock, dispenser.Stock);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(20, 20)]
    [InlineData(10, 10)]
    public void Jammed_cassette_counts_as_empty(int jam, int amount)
    {
        // Seule la cassette bloquée pourrait servir.
        var stock = N(jam == 50 ? 5 : 0, jam == 20 ? 5 : 0, jam == 10 ? 5 : 0);
        var (machine, dispenser) = Build(stock, Jam(jam));

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(Reference.Refused(AtmStatus.CannotDispense, Acc(250, 0)), result);
        Assert.Equal(stock, dispenser.Stock);
    }

    [Theory]
    [InlineData(50, 100, 0, 5, 0)]  // 50 bloqué : 20×5
    [InlineData(20, 60, 1, 0, 1)]   // 20 bloqué : 50+10
    [InlineData(10, 70, 1, 1, 0)]   // 10 bloqué : 50+20
    public void Jammed_cassette_is_bypassed_with_other_notes(int jam, int amount, int f, int t, int n)
    {
        var (machine, dispenser) = Build(Full, Jam(jam));

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(f, t, n), result.Notes);
        Assert.Equal(Full - N(f, t, n), dispenser.Stock);
    }

    // --- Combinaison : le moins de billets ---

    [Theory]
    // stock (50, 20, 10), montant, combinaison attendue (50, 20, 10)
    [InlineData(5, 5, 5, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 20, 0, 1, 0)]
    [InlineData(5, 5, 5, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 50, 1, 0, 0)]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 200, 4, 0, 0)]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]    // l'approche gloutonne (50 d'abord) échoue
    [InlineData(1, 4, 0, 80, 0, 4, 0)]    // idem
    [InlineData(1, 5, 5, 100, 1, 2, 1)]   // 4 billets plutôt que 20×5
    [InlineData(0, 5, 5, 150, 0, 5, 5)]   // tout le stock
    [InlineData(0, 0, 5, 50, 0, 0, 5)]    // que des 10
    [InlineData(5, 0, 5, 80, 1, 0, 3)]    // pas de 20
    [InlineData(2, 5, 5, 200, 2, 5, 0)]   // 50 limités
    public void Notes_are_the_exact_combination_with_fewest_notes(
        int sf, int st, int sn, int amount, int f, int t, int n)
    {
        var stock = N(sf, st, sn);
        var (machine, dispenser) = Build(stock);

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(f, t, n), result.Notes);
        Assert.Equal(stock - N(f, t, n), dispenser.Stock);
    }

    // --- Sortie des billets ---

    [Theory]
    [InlineData(0, 5, 5, 150)]  // 10 billets
    [InlineData(1, 5, 0, 150)]  // 6 billets
    [InlineData(0, 0, 5, 50)]   // 5 billets
    [InlineData(4, 0, 0, 200)]  // 4 billets
    [InlineData(0, 1, 4, 60)]   // 5 billets de deux coupures
    [InlineData(1, 1, 3, 100)]  // 5 billets de trois coupures
    [InlineData(2, 2, 2, 160)]  // 6 billets de trois coupures
    public void Notes_come_out_through_the_dispenser_in_passes_of_at_most_four(int f, int t, int n, int amount)
    {
        var stock = N(f, t, n);
        var (machine, dispenser) = Build(stock);

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(stock, result.Notes);
        Assert.NotEmpty(dispenser.Dispensed);
        Assert.All(dispenser.Dispensed, pass =>
        {
            Assert.True(pass.Result, $"Passage refusé par le module : {pass.Notes}");
            Assert.InRange(pass.Notes.Total, 1, 4);
        });
        Assert.Equal(result.Notes, dispenser.TotalDispensed);
        Assert.Empty(dispenser.Loaded);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    // --- Succès ---

    [Fact]
    public void Success_updates_account_and_reports_notes_and_fee()
    {
        var (machine, dispenser) = Build(Full);

        var result = machine.Execute(Acc(200, 40), Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(3, 0, 0), 5, Acc(45, 190)), result);
        Assert.Equal(N(2, 5, 5), dispenser.Stock);
    }
}
