using static Atm.Tests.TestKit;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts FullStock = N(5, 5, 5);

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Negative_zero_or_non_multiple_of_ten_is_InvalidAmount(int amount)
    {
        var account = Account(250, 0);
        var (result, dispenser) = Run(account, FullStock, null, Withdraw(amount));

        AssertRefused(AtmStatus.InvalidAmount, result, account, FullStock, dispenser);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(200)]
    public void Positive_multiples_of_ten_are_valid_amounts(int amount)
    {
        var (result, _) = Run(Account(250, 0), FullStock, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
    }

    [Fact]
    public void InvalidAmount_is_checked_before_every_other_rule()
    {
        // Plafond atteint, solde nul, stock vide : c'est quand même le montant qui est refusé en premier.
        var account = Account(0, 200);
        var (result, dispenser) = Run(account, N(0, 0, 0), Denomination.Ten, Withdraw(-10));

        AssertRefused(AtmStatus.InvalidAmount, result, account, N(0, 0, 0), dispenser);
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 200, AtmStatus.Ok)]
    [InlineData(150, 50, AtmStatus.Ok)]
    [InlineData(150, 60, AtmStatus.DailyLimitExceeded)]
    [InlineData(200, 10, AtmStatus.DailyLimitExceeded)]
    [InlineData(0, 210, AtmStatus.DailyLimitExceeded)]
    [InlineData(0, 220, AtmStatus.DailyLimitExceeded)]
    public void Daily_limit_is_200_inclusive(int withdrawn, int amount, AtmStatus expected)
    {
        var (result, _) = Run(Account(250, withdrawn), FullStock, null, Withdraw(amount));

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Fee_does_not_count_in_daily_limit()
    {
        // 90 + 110 = 200 : pile au plafond ; les 5 € de frais n'y comptent pas.
        var (result, _) = Run(Account(250, 90), FullStock, null, Withdraw(110));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Account(135, 200), result.Account);
    }

    [Fact]
    public void DailyLimit_is_checked_before_funds_and_notes()
    {
        var account = Account(0, 200);
        var (result, dispenser) = Run(account, N(0, 0, 0), null, Withdraw(10));

        AssertRefused(AtmStatus.DailyLimitExceeded, result, account, N(0, 0, 0), dispenser);
    }

    // ---- Contrôle 3 : solde insuffisant ----

    [Theory]
    [InlineData(100, 100, AtmStatus.Ok)]                // pas de frais à 100 €
    [InlineData(95, 100, AtmStatus.InsufficientFunds)]
    [InlineData(115, 110, AtmStatus.Ok)]                // 110 + 5 = 115
    [InlineData(110, 110, AtmStatus.InsufficientFunds)] // 110 + 5 > 110
    [InlineData(205, 200, AtmStatus.Ok)]
    [InlineData(200, 200, AtmStatus.InsufficientFunds)]
    [InlineData(0, 10, AtmStatus.InsufficientFunds)]
    [InlineData(10, 10, AtmStatus.Ok)]
    public void Amount_plus_fee_must_not_exceed_balance(int balance, int amount, AtmStatus expected)
    {
        var (result, _) = Run(Account(balance, 0), FullStock, null, Withdraw(amount));

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void InsufficientFunds_is_checked_before_CannotDispense()
    {
        var account = Account(20, 0);
        var (result, dispenser) = Run(account, N(0, 0, 0), null, Withdraw(30));

        AssertRefused(AtmStatus.InsufficientFunds, result, account, N(0, 0, 0), dispenser);
    }

    // ---- Les frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(200, 5)]
    public void Fee_is_5_strictly_above_100(int amount, int fee)
    {
        var (result, _) = Run(Account(250, 0), FullStock, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(Account(250 - amount - fee, amount), result.Account);
    }

    // ---- Contrôle 4 : combinaison impossible ----

    [Theory]
    [InlineData(0, 0, 0, 0, 10)]      // stock vide
    [InlineData(1, 0, 0, 0, 30)]      // seulement un 50
    [InlineData(0, 5, 0, 0, 10)]      // pas de 10
    [InlineData(0, 1, 0, 0, 30)]
    [InlineData(1, 1, 0, 0, 60)]      // 50 + 20 ne font pas 60
    [InlineData(5, 0, 0, 0, 80)]
    [InlineData(5, 5, 0, 0, 10)]
    [InlineData(1, 0, 0, 50, 50)]     // le seul 50 est bloqué
    [InlineData(0, 5, 1, 20, 40)]     // les 20 sont bloqués
    [InlineData(1, 1, 5, 10, 30)]     // les 10 sont bloqués
    [InlineData(0, 0, 0, 0, 200)]
    public void No_exact_combination_is_CannotDispense(int f, int t, int d, int jammed, int amount)
    {
        var account = Account(250, 0);
        var stock = N(f, t, d);
        var (result, dispenser) = Run(account, stock, ParseJammed(jammed), Withdraw(amount));

        AssertRefused(AtmStatus.CannotDispense, result, account, stock, dispenser);
    }

    // ---- La combinaison : le moins de billets ----

    [Theory]
    // stock (50,20,10), bloquée, montant, combinaison attendue (50,20,10)
    [InlineData(5, 5, 5, 0, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 0, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 0, 60, 1, 0, 1)]   // 50 + 10 (2 billets) plutôt que 20×3
    [InlineData(1, 3, 0, 0, 60, 0, 3, 0)]   // sans 10 : 20×3
    [InlineData(5, 5, 5, 0, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 0, 200, 4, 0, 0)]
    [InlineData(0, 5, 5, 0, 100, 0, 5, 0)]  // sans 50 : 20×5 plutôt que 20×4 + 10×2
    [InlineData(1, 5, 5, 0, 100, 1, 2, 1)]  // un seul 50
    [InlineData(5, 5, 5, 50, 100, 0, 5, 0)] // les 50 bloqués comptent pour zéro
    [InlineData(5, 5, 5, 20, 80, 1, 0, 3)]  // les 20 bloqués
    [InlineData(5, 5, 5, 10, 90, 1, 2, 0)]
    [InlineData(5, 0, 5, 0, 80, 1, 0, 3)]
    [InlineData(5, 3, 5, 0, 180, 3, 1, 1)]
    [InlineData(0, 0, 5, 0, 50, 0, 0, 5)]
    public void Chooses_the_combination_with_fewest_notes(
        int f, int t, int d, int jammed, int amount, int ef, int et, int ed)
    {
        var expected = N(ef, et, ed);
        Assert.Equal(amount, expected.Value); // garde-fou sur les données

        var stock = N(f, t, d);
        var (result, dispenser) = Run(Account(250, 0), stock, ParseJammed(jammed), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    // ---- La sortie : plusieurs passages de 4 billets au plus ----

    [Theory]
    // stock (50,20,10), bloquée, montant, combinaison attendue (50,20,10)
    [InlineData(0, 5, 0, 0, 100, 0, 5, 0)]   // 5 billets : 2 passages
    [InlineData(0, 4, 1, 0, 90, 0, 4, 1)]    // 20×4 + 10×1
    [InlineData(2, 2, 1, 0, 150, 2, 2, 1)]   // 50×2 + 20×2 + 10×1
    [InlineData(1, 5, 5, 0, 150, 1, 5, 0)]
    [InlineData(5, 5, 5, 50, 110, 0, 5, 1)]  // 50 bloqués : 20×5 + 10×1
    [InlineData(5, 4, 5, 50, 130, 0, 4, 5)]  // 9 billets : 3 passages
    [InlineData(0, 5, 5, 0, 150, 0, 5, 5)]   // 10 billets
    [InlineData(3, 2, 1, 0, 200, 3, 2, 1)]
    public void Large_combinations_are_dispensed_in_several_passes(
        int f, int t, int d, int jammed, int amount, int ef, int et, int ed)
    {
        var expected = N(ef, et, ed);
        Assert.Equal(amount, expected.Value);

        var stock = N(f, t, d);
        var spy = new SpyDispenser(new MoneyDispenser(stock, ParseJammed(jammed)));
        var result = new CashMachine(spy).Execute(Account(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(Oracle.Fee(amount), result.Fee);
        Assert.Equal(Account(250 - amount - Oracle.Fee(amount), amount), result.Account);
        Assert.Equal(stock - expected, spy.Stock);
        Assert.All(spy.Dispenses, pass =>
        {
            Assert.True(pass.Accepted);
            Assert.InRange(pass.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass);
        });
        Assert.Equal(expected, spy.Dispenses.Aggregate(NoteCounts.None, (sum, p) => sum + p.Notes));
        Assert.Empty(spy.Loads);
    }

    // ---- Le succès ----

    [Fact]
    public void Success_updates_account_and_dispenses_the_combination()
    {
        var stock = N(5, 5, 5);
        var (result, dispenser) = Run(Account(200, 50), stock, null, Withdraw(130));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(2, 1, 1), result.Notes);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Account(65, 180), result.Account);
        Assert.Equal(N(3, 4, 4), dispenser.Stock);
    }

    // ---- Machine sans mémoire ----

    [Fact]
    public void Machine_relies_on_the_current_state_of_the_module()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(Account(250, 0), Withdraw(50));
        var second = machine.Execute(Account(250, 0), Withdraw(50));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(AtmStatus.CannotDispense, second.Status);
        Assert.Equal(Account(250, 0), second.Account);
    }

    [Fact]
    public void Machine_only_depends_on_the_account_passed_in()
    {
        var machine = new CashMachine(new MoneyDispenser(N(5, 5, 5)));

        var first = machine.Execute(Account(250, 0), Withdraw(50));
        // Un compte différent, sans lien avec l'appel précédent.
        var second = machine.Execute(Account(60, 160), Withdraw(40));

        Assert.Equal(Account(200, 50), first.Account);
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(Account(20, 200), second.Account);
    }
}
