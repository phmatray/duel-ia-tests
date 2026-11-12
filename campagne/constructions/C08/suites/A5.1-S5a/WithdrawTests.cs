using static Atm.Tests.TestKit;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    // ---------- Contrôle 1 : montant invalide ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Negative_zero_or_non_multiple_of_ten_is_InvalidAmount(int amount)
    {
        var (result, dispenser) = Withdraw(250, 0, Full, null, amount);

        AssertRefused(AtmStatus.InvalidAmount, result, Acc(250, 0), dispenser, Full);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void InvalidAmount_comes_before_every_other_check(int amount)
    {
        // Plafond atteint, solde nul, stock vide, cassette bloquée : seul le montant doit parler.
        var (result, dispenser) = Withdraw(0, 200, N(0, 0, 0), Denomination.Ten, amount);

        AssertRefused(AtmStatus.InvalidAmount, result, Acc(0, 200), dispenser, N(0, 0, 0));
    }

    // ---------- Contrôle 2 : plafond du jour ----------

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(190, 20)]
    [InlineData(200, 10)]
    public void Exceeding_200_per_day_is_DailyLimitExceeded(int withdrawn, int amount)
    {
        var (result, dispenser) = Withdraw(250, withdrawn, Full, null, amount);

        AssertRefused(AtmStatus.DailyLimitExceeded, result, Acc(250, withdrawn), dispenser, Full);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Reaching_exactly_200_per_day_is_allowed(int withdrawn, int amount)
    {
        var (result, _) = Withdraw(250, withdrawn, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(200, result.Account.WithdrawnToday);
    }

    [Fact]
    public void DailyLimitExceeded_comes_before_InsufficientFunds_and_CannotDispense()
    {
        var (result, dispenser) = Withdraw(0, 200, N(0, 0, 0), null, 10);

        AssertRefused(AtmStatus.DailyLimitExceeded, result, Acc(0, 200), dispenser, N(0, 0, 0));
    }

    [Fact]
    public void Fee_does_not_count_toward_daily_limit()
    {
        // 90 + 110 = 200 : pile au plafond, les 5 € de frais n'y entrent pas.
        var (result, _) = Withdraw(250, 90, Full, null, 110);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Acc(135, 200), result.Account);
    }

    // ---------- Contrôle 3 : fonds insuffisants ----------

    [Theory]
    [InlineData(50, 60)]
    [InlineData(0, 10)]
    [InlineData(100, 110)] // 110 + 5 > 100
    [InlineData(110, 110)] // 110 + 5 > 110 : les frais comptent
    [InlineData(200, 200)] // 200 + 5 > 200
    [InlineData(105, 110)]
    public void Amount_plus_fee_above_balance_is_InsufficientFunds(int balance, int amount)
    {
        var (result, dispenser) = Withdraw(balance, 0, Full, null, amount);

        AssertRefused(AtmStatus.InsufficientFunds, result, Acc(balance, 0), dispenser, Full);
    }

    [Theory]
    [InlineData(60, 60, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 5)]
    [InlineData(205, 200, 5)]
    public void Balance_exactly_equal_to_amount_plus_fee_is_enough(int balance, int amount, int fee)
    {
        var (result, _) = Withdraw(balance, 0, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(0, result.Account.Balance);
    }

    [Fact]
    public void InsufficientFunds_comes_before_CannotDispense()
    {
        var (result, dispenser) = Withdraw(20, 0, N(0, 0, 0), Denomination.Twenty, 30);

        AssertRefused(AtmStatus.InsufficientFunds, result, Acc(20, 0), dispenser, N(0, 0, 0));
    }

    // ---------- Les frais ----------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_is_5_strictly_above_100_else_0(int amount, int fee)
    {
        var (result, _) = Withdraw(250, 0, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(Acc(250 - amount - fee, amount), result.Account);
    }

    // ---------- Contrôle 4 : impossible à servir ----------

    [Theory]
    // stock (50,20,10), bloquée, montant
    [InlineData(0, 0, 0, 0, 10)]   // stock vide
    [InlineData(1, 0, 0, 0, 30)]   // aucune combinaison exacte
    [InlineData(0, 1, 0, 0, 10)]   // un 20 ne fait pas 10
    [InlineData(1, 1, 0, 0, 60)]   // 50+20 ou 20 : jamais 60
    [InlineData(0, 2, 0, 0, 60)]   // pas assez de billets
    [InlineData(5, 0, 0, 0, 30)]
    [InlineData(1, 0, 0, 50, 50)]  // la cassette bloquée compte pour zéro
    [InlineData(0, 5, 0, 20, 20)]
    [InlineData(0, 0, 5, 10, 10)]
    [InlineData(5, 5, 1, 10, 10)]  // 10 seulement dans la cassette bloquée
    public void No_exact_combination_from_available_notes_is_CannotDispense(
        int s50, int s20, int s10, int jammed, int amount)
    {
        var stock = N(s50, s20, s10);
        var (result, dispenser) = Withdraw(250, 0, stock, Jam(jammed), amount);

        AssertRefused(AtmStatus.CannotDispense, result, Acc(250, 0), dispenser, stock);
    }

    [Theory]
    // stock, bloquée, montant | billets attendus
    [InlineData(5, 5, 5, 50, 100, 0, 5, 0)]  // sans les 50 : 20×5
    [InlineData(5, 5, 5, 50, 60, 0, 3, 0)]   // sans les 50 : 20×3 (et non 20×2 10×2)
    [InlineData(5, 5, 5, 20, 80, 1, 0, 3)]   // sans les 20 : 50 + 10×3
    [InlineData(5, 5, 5, 10, 70, 1, 1, 0)]
    [InlineData(5, 5, 5, 10, 90, 1, 2, 0)]
    [InlineData(1, 5, 5, 20, 60, 1, 0, 1)]   // sans les 20 : 50 + 10
    [InlineData(5, 5, 5, 10, 30, 0, 0, 0)]   // sans les 10 : 30 impossible -> refus
    public void Jammed_cassette_is_never_used(int s50, int s20, int s10, int jammed, int amount, int n50, int n20, int n10)
    {
        var stock = N(s50, s20, s10);
        var (result, dispenser) = Withdraw(250, 0, stock, Jam(jammed), amount);

        if (N(n50, n20, n10) == NoteCounts.None)
        {
            AssertRefused(AtmStatus.CannotDispense, result, Acc(250, 0), dispenser, stock);
            return;
        }

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(n50, n20, n10), result.Notes);
        Assert.Equal(0, result.Notes.Of(Jam(jammed)!.Value));
        Assert.Equal(stock - result.Notes, dispenser.Stock);
    }

    // ---------- La combinaison : le moins de billets ----------

    [Theory]
    // stock, montant | billets
    [InlineData(5, 5, 5, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 20, 0, 1, 0)]
    [InlineData(5, 5, 5, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 50, 1, 0, 0)]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 70, 1, 1, 0)]
    [InlineData(5, 5, 5, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 100, 2, 0, 0)]
    [InlineData(5, 5, 5, 130, 2, 1, 1)]
    [InlineData(5, 5, 5, 140, 2, 2, 0)]
    [InlineData(5, 5, 5, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, 200, 4, 0, 0)]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // le glouton (50 d'abord) échoue
    [InlineData(1, 5, 0, 80, 0, 4, 0)]   // 50+20+10 impossible : 20×4
    [InlineData(0, 5, 5, 50, 0, 2, 1)]   // pas de 50
    [InlineData(0, 0, 5, 50, 0, 0, 5)]   // que des 10 : 5 billets
    [InlineData(0, 1, 5, 60, 0, 1, 4)]
    [InlineData(1, 0, 5, 100, 1, 0, 5)]
    [InlineData(5, 0, 5, 80, 1, 0, 3)]
    [InlineData(2, 5, 5, 200, 2, 5, 0)]  // 7 billets
    public void Combination_uses_the_fewest_notes(int s50, int s20, int s10, int amount, int n50, int n20, int n10)
    {
        var stock = N(s50, s20, s10);
        var (result, dispenser) = Withdraw(250, 0, stock, null, amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(n50, n20, n10), result.Notes);
        Assert.Equal(amount, result.Notes.Value);
        Assert.Equal(stock - result.Notes, dispenser.Stock);
    }

    // ---------- La sortie : plusieurs passages de 4 billets au plus ----------

    [Theory]
    // Combinaisons de plus de 4 billets : elles doivent sortir en plusieurs passages.
    [InlineData(0, 3, 2, 80, 0, 3, 2)]
    [InlineData(0, 4, 1, 90, 0, 4, 1)]
    [InlineData(1, 3, 1, 120, 1, 3, 1)]
    [InlineData(0, 5, 0, 100, 0, 5, 0)]
    [InlineData(0, 0, 5, 50, 0, 0, 5)]
    [InlineData(3, 2, 0, 190, 3, 2, 0)]
    [InlineData(1, 5, 5, 200, 1, 5, 5)]
    [InlineData(0, 5, 5, 150, 0, 5, 5)]
    [InlineData(2, 2, 2, 160, 2, 2, 2)]
    public void Combination_of_more_than_4_notes_is_dispensed_in_several_passes(
        int s50, int s20, int s10, int amount, int n50, int n20, int n10)
    {
        var stock = N(s50, s20, s10);
        var (result, dispenser) = Withdraw(250, 0, stock, null, amount);

        var fee = amount > 100 ? 5 : 0;
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(n50, n20, n10), result.Notes);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(Acc(250 - amount - fee, amount), result.Account);
        Assert.Equal(stock - result.Notes, dispenser.Stock);
    }

    [Fact]
    public void Each_pass_has_at_most_4_notes_and_together_they_make_the_combination()
    {
        var spy = new RecordingDispenser(N(0, 3, 2), null);

        var result = new CashMachine(spy).Execute(Acc(250, 0), new Operation.Withdraw(80));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.NotEmpty(spy.Passes);
        Assert.All(spy.Passes, pass => Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(N(0, 3, 2), spy.Passes.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Empty(spy.Loads);
    }

    // ---------- Le succès ----------

    [Fact]
    public void Success_debits_amount_plus_fee_and_adds_amount_to_withdrawn_today()
    {
        var (result, dispenser) = Withdraw(250, 40, Full, null, 150);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(3, 0, 0), result.Notes);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Acc(95, 190), result.Account);
        Assert.Equal(N(2, 5, 5), dispenser.Stock);
    }

    // ---------- Sans mémoire ----------

    [Fact]
    public void Machine_has_no_memory_between_calls_and_reads_the_module_each_time()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(Acc(100, 0), new Operation.Withdraw(50));
        // Même compte d'entrée : la machine ne se souvient pas du premier retrait ...
        var second = machine.Execute(Acc(100, 0), new Operation.Withdraw(50));
        // ... mais le module, lui, est vide.

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(Acc(50, 50), first.Account);
        Assert.Equal(AtmStatus.CannotDispense, second.Status);
        Assert.Equal(Acc(100, 0), second.Account);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Same_inputs_give_same_result()
    {
        var a = Withdraw(180, 30, N(2, 3, 1), Denomination.Ten, 120).Result;
        var b = Withdraw(180, 30, N(2, 3, 1), Denomination.Ten, 120).Result;

        Assert.Equal(a, b);
    }
}
