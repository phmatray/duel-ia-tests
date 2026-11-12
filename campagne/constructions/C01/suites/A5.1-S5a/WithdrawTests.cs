using static Atm.Tests.TestSupport;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts FullStock = N(5, 5, 5);
    private static readonly AccountState Rich = new(250, 0);

    // ---------- Contrôle 1 : montant invalide ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Invalid_amount_is_refused(int amount)
    {
        var result = Withdraw(FullStock, null, Rich, amount, out var stockAfter);

        AssertRefused(AtmStatus.InvalidAmount, result, Rich, FullStock, stockAfter);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Invalid_amount_is_checked_before_every_other_rule(int amount)
    {
        // Plafond atteint, solde nul, stock vide, cassette bloquée : tout le reste échouerait aussi.
        var account = new AccountState(0, 200);
        var stock = N(0, 0, 0);

        var result = Withdraw(stock, Denomination.Ten, account, amount, out var stockAfter);

        AssertRefused(AtmStatus.InvalidAmount, result, account, stock, stockAfter);
    }

    // ---------- Contrôle 2 : plafond du jour ----------

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Reaching_exactly_the_daily_limit_is_allowed(int withdrawn, int amount)
    {
        var account = new AccountState(250, withdrawn);

        var result = Withdraw(FullStock, null, account, amount, out _);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(200, result.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    public void Exceeding_the_daily_limit_is_refused(int withdrawn, int amount)
    {
        var account = new AccountState(250, withdrawn);

        var result = Withdraw(FullStock, null, account, amount, out var stockAfter);

        AssertRefused(AtmStatus.DailyLimitExceeded, result, account, FullStock, stockAfter);
    }

    [Fact]
    public void Daily_limit_is_checked_before_funds_and_dispensability()
    {
        var account = new AccountState(0, 200);
        var stock = N(0, 0, 0);

        var result = Withdraw(stock, Denomination.Fifty, account, 10, out var stockAfter);

        AssertRefused(AtmStatus.DailyLimitExceeded, result, account, stock, stockAfter);
    }

    [Fact]
    public void Fee_does_not_count_toward_the_daily_limit()
    {
        // 90 + 110 = 200 : au plafond pile ; les 5 € de frais ne s'y ajoutent pas.
        var account = new AccountState(250, 90);

        var result = Withdraw(FullStock, null, account, 110, out _);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(135, 200), result.Account);
    }

    // ---------- Frais ----------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_is_5_only_strictly_above_100(int amount, int fee)
    {
        var result = Withdraw(FullStock, null, Rich, amount, out _);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(new AccountState(250 - amount - fee, amount), result.Account);
    }

    // ---------- Contrôle 3 : solde insuffisant ----------

    [Theory]
    [InlineData(60, 60)]
    [InlineData(100, 100)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    public void Balance_exactly_covering_amount_plus_fee_is_enough(int balance, int amount)
    {
        var result = Withdraw(FullStock, null, new AccountState(balance, 0), amount, out _);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Account.Balance);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(55, 60)]
    [InlineData(95, 100)]
    [InlineData(110, 110)] // couvre le montant mais pas les frais
    [InlineData(200, 200)]
    public void Balance_not_covering_amount_plus_fee_is_refused(int balance, int amount)
    {
        var account = new AccountState(balance, 0);

        var result = Withdraw(FullStock, null, account, amount, out var stockAfter);

        AssertRefused(AtmStatus.InsufficientFunds, result, account, FullStock, stockAfter);
    }

    [Fact]
    public void Insufficient_funds_is_checked_before_dispensability()
    {
        var account = new AccountState(10, 0);
        var stock = N(0, 0, 0);

        var result = Withdraw(stock, null, account, 50, out var stockAfter);

        AssertRefused(AtmStatus.InsufficientFunds, result, account, stock, stockAfter);
    }

    // ---------- Contrôle 4 : impossible à servir ----------

    [Fact]
    public void Empty_dispenser_cannot_dispense()
    {
        var stock = N(0, 0, 0);

        var result = Withdraw(stock, null, Rich, 10, out var stockAfter);

        AssertRefused(AtmStatus.CannotDispense, result, Rich, stock, stockAfter);
    }

    [Theory]
    [InlineData(0, 5, 0, 10)]  // que des 20 : 10 impossible
    [InlineData(0, 5, 0, 30)]  // que des 20 : 30 impossible
    [InlineData(1, 0, 0, 60)]  // 50 seul ne fait pas 60
    [InlineData(1, 1, 0, 80)]  // 50+20 = 70, ni 80
    [InlineData(0, 2, 1, 60)]  // 20+20+10 = 50 au plus
    [InlineData(2, 0, 0, 150)] // stock insuffisant en valeur
    public void No_exact_combination_in_stock_cannot_dispense(int s50, int s20, int s10, int amount)
    {
        var stock = N(s50, s20, s10);

        var result = Withdraw(stock, null, Rich, amount, out var stockAfter);

        AssertRefused(AtmStatus.CannotDispense, result, Rich, stock, stockAfter);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(20, 40)]
    [InlineData(10, 10)]
    public void Jammed_cassette_counts_as_empty(int jammed, int amount)
    {
        // Seule la cassette bloquée pourrait servir le montant.
        var stock = N(jammed == 50 ? 5 : 0, jammed == 20 ? 5 : 0, jammed == 10 ? 5 : 0);

        var result = Withdraw(stock, Jam(jammed), Rich, amount, out var stockAfter);

        AssertRefused(AtmStatus.CannotDispense, result, Rich, stock, stockAfter);
    }

    [Theory]
    //          bloquée  stock(50,20,10)  montant  billets(50,20,10)
    [InlineData(50,      5, 5, 5,         100,     0, 5, 0)] // 50×2 exclu -> 20×5
    [InlineData(20,      5, 5, 5,         60,      1, 0, 1)] // 50+10, sans les 20
    [InlineData(20,      5, 5, 5,         90,      1, 0, 4)] // 50+20×2 exclu -> 50+10×4
    [InlineData(10,      5, 5, 5,         70,      1, 1, 0)] // 50+20, sans les 10
    [InlineData(10,      5, 5, 5,         60,      0, 3, 0)] // 50+10 exclu -> 20×3
    [InlineData(10,      0, 5, 5,         60,      0, 3, 0)] // 20×3
    public void Jammed_cassette_is_avoided_when_another_combination_exists(
        int jammed, int s50, int s20, int s10, int amount, int n50, int n20, int n10)
    {
        var stock = N(s50, s20, s10);

        var result = Withdraw(stock, Jam(jammed), Rich, amount, out var stockAfter);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(n50, n20, n10), result.Notes);
        Assert.Equal(stock - result.Notes, stockAfter);
    }

    // ---------- La combinaison : le moins de billets ----------

    [Theory]
    [InlineData(10, 0, 0, 1)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(30, 0, 1, 1)]
    [InlineData(40, 0, 2, 0)]
    [InlineData(50, 1, 0, 0)]
    [InlineData(60, 1, 0, 1)]
    [InlineData(70, 1, 1, 0)]
    [InlineData(80, 1, 1, 1)]
    [InlineData(90, 1, 2, 0)]
    [InlineData(100, 2, 0, 0)]
    [InlineData(110, 2, 0, 1)]
    [InlineData(120, 2, 1, 0)]
    [InlineData(130, 2, 1, 1)]
    [InlineData(140, 2, 2, 0)]
    [InlineData(150, 3, 0, 0)]
    [InlineData(160, 3, 0, 1)]
    [InlineData(170, 3, 1, 0)]
    [InlineData(180, 3, 1, 1)]
    [InlineData(190, 3, 2, 0)]
    [InlineData(200, 4, 0, 0)]
    public void Full_stock_gives_the_fewest_notes(int amount, int n50, int n20, int n10)
    {
        var result = Withdraw(FullStock, null, Rich, amount, out var stockAfter);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(n50, n20, n10), result.Notes);
        Assert.Equal(FullStock - result.Notes, stockAfter);
    }

    [Theory]
    [InlineData(0, 5, 5, 60, 0, 3, 0)]   // pas de 50 : 20×3 plutôt que 20×2+10×2
    [InlineData(5, 5, 0, 60, 0, 3, 0)]   // pas de 10 : 50+10 impossible -> 20×3
    [InlineData(5, 5, 0, 80, 0, 4, 0)]   // pas de 10 : 20×4
    [InlineData(5, 0, 5, 40, 0, 0, 4)]   // pas de 20 : 10×4
    [InlineData(5, 0, 5, 70, 1, 0, 2)]   // pas de 20 : 50+10×2
    [InlineData(1, 5, 5, 150, 1, 5, 0)]  // un seul 50 : 50+20×5
    [InlineData(5, 1, 5, 90, 1, 1, 2)]   // un seul 20 : 50+20+10×2
    [InlineData(5, 5, 0, 110, 1, 3, 0)]  // pas de 10 : 50+20×3 (et non 50×2+10)
    public void Limited_stock_gives_the_fewest_notes_among_available_ones(
        int s50, int s20, int s10, int amount, int n50, int n20, int n10)
    {
        var stock = N(s50, s20, s10);

        var result = Withdraw(stock, null, Rich, amount, out var stockAfter);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(n50, n20, n10), result.Notes);
        Assert.Equal(stock - result.Notes, stockAfter);
    }

    // ---------- La sortie : plusieurs passages de 4 billets au plus ----------

    [Theory]
    [InlineData(0, 5, 0, 100, 0, 5, 0)]  // 5 billets d'une seule coupure
    [InlineData(0, 5, 1, 110, 0, 5, 1)]  // 6 billets, deux coupures
    [InlineData(0, 4, 1, 90, 0, 4, 1)]   // 5 billets, deux coupures
    [InlineData(1, 3, 1, 120, 1, 3, 1)]  // 5 billets, trois coupures
    [InlineData(3, 2, 1, 200, 3, 2, 1)]  // 6 billets, trois coupures
    [InlineData(1, 5, 0, 150, 1, 5, 0)]  // 6 billets
    [InlineData(0, 0, 5, 50, 0, 0, 5)]   // pas de 50 ni 20 : 10×5
    [InlineData(1, 5, 5, 180, 1, 5, 3)]  // 9 billets
    public void Withdrawal_of_more_than_four_notes_is_split_into_several_passes(
        int s50, int s20, int s10, int amount, int n50, int n20, int n10)
    {
        var stock = N(s50, s20, s10);

        var result = Withdraw(stock, null, Rich, amount, out var stockAfter);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(n50, n20, n10), result.Notes);
        Assert.Equal(ExpectedFee(amount), result.Fee);
        Assert.Equal(new AccountState(250 - amount - ExpectedFee(amount), amount), result.Account);
        Assert.Equal(stock - result.Notes, stockAfter);
    }

    [Fact]
    public void Every_pass_has_at_most_four_notes_and_passes_add_up_to_the_combination()
    {
        var spy = new SpyDispenser(new MoneyDispenser(N(1, 5, 5)));
        var machine = new CashMachine(spy);

        var result = machine.Execute(Rich, new Operation.Withdraw(180));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.All(spy.Dispensed, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(result.Notes, spy.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Empty(spy.Loaded);
    }

    // ---------- Succès ----------

    [Fact]
    public void Success_updates_account_and_dispenser()
    {
        var account = new AccountState(200, 40);

        var result = Withdraw(FullStock, null, account, 130, out var stockAfter);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(2, 1, 1), result.Notes);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(65, 170), result.Account);
        Assert.Equal(N(3, 4, 4), stockAfter);
    }

    [Fact]
    public void Refusal_moves_no_note_at_all()
    {
        var spy = new SpyDispenser(new MoneyDispenser(FullStock));
        var machine = new CashMachine(spy);

        machine.Execute(new AccountState(250, 0), new Operation.Withdraw(5));
        machine.Execute(new AccountState(250, 200), new Operation.Withdraw(10));
        machine.Execute(new AccountState(0, 0), new Operation.Withdraw(10));

        Assert.Empty(spy.Dispensed);
        Assert.Empty(spy.Loaded);
        Assert.Equal(FullStock, spy.Stock);
    }

    // ---------- Sans mémoire ----------

    [Fact]
    public void Machine_has_no_memory_of_previous_accounts()
    {
        var (machine, dispenser) = Build(FullStock);
        var account = new AccountState(100, 0);

        var first = machine.Execute(account, new Operation.Withdraw(60));
        var second = machine.Execute(account, new Operation.Withdraw(60));

        Assert.Equal(first, second);
        Assert.Equal(new AccountState(40, 60), second.Account);
        Assert.Equal(N(3, 5, 3), dispenser.Stock);
    }

    [Fact]
    public void Machine_reads_the_current_dispenser_stock_on_each_call()
    {
        var (machine, _) = Build(N(1, 0, 0));
        var account = new AccountState(250, 0);

        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Withdraw(50)).Status);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(account, new Operation.Withdraw(50)).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Deposit(N(1, 0, 0))).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Withdraw(50)).Status);
    }
}
