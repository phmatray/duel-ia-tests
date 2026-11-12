using static Atm.Tests.Atm;

namespace Atm.Tests;

public class WithdrawTests
{
    private static (AtmResult Result, SpyDispenser Dispenser) Run(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount)
    {
        var dispenser = new SpyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(Account(balance, withdrawnToday), Withdraw(amount));
        return (result, dispenser);
    }

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Invalid_amount_is_refused(int amount)
    {
        var (result, dispenser) = Run(250, 0, Full, null, amount);

        AssertRefused(AtmStatus.InvalidAmount, Account(250, 0), Full, result, dispenser);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Invalid_amount_is_checked_before_daily_limit_funds_and_stock(int amount)
    {
        // Plafond atteint, solde nul, stock vide : seul le contrôle 1 doit parler.
        var (result, dispenser) = Run(0, 200, Notes(0, 0, 0), null, amount);

        AssertRefused(AtmStatus.InvalidAmount, Account(0, 200), Notes(0, 0, 0), result, dispenser);
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    public void Exceeding_the_daily_limit_is_refused(int withdrawnToday, int amount)
    {
        var (result, dispenser) = Run(250, withdrawnToday, Full, null, amount);

        AssertRefused(AtmStatus.DailyLimitExceeded, Account(250, withdrawnToday), Full, result, dispenser);
    }

    [Theory]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    [InlineData(0, 200)]
    public void Reaching_exactly_the_daily_limit_is_allowed(int withdrawnToday, int amount)
    {
        var (result, _) = Run(250, withdrawnToday, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(withdrawnToday + amount, result.Account.WithdrawnToday);
    }

    [Fact]
    public void Fee_does_not_count_toward_the_daily_limit()
    {
        // 90 + 110 = 200 : pile au plafond, les 5 € de frais n'y entrent pas.
        var (result, _) = Run(250, 90, Full, null, 110);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Account(135, 200), result.Account);
    }

    [Fact]
    public void Daily_limit_is_checked_before_funds_and_stock()
    {
        var (result, dispenser) = Run(0, 200, Notes(0, 0, 0), null, 10);

        AssertRefused(AtmStatus.DailyLimitExceeded, Account(0, 200), Notes(0, 0, 0), result, dispenser);
    }

    // ---- Contrôle 3 : solde insuffisant ----

    [Theory]
    [InlineData(50, 60)]
    [InlineData(0, 10)]
    [InlineData(110, 110)] // 110 + 5 de frais > 110
    [InlineData(105, 110)]
    [InlineData(200, 200)] // 200 + 5 > 200
    public void Insufficient_funds_is_refused(int balance, int amount)
    {
        var (result, dispenser) = Run(balance, 0, Full, null, amount);

        AssertRefused(AtmStatus.InsufficientFunds, Account(balance, 0), Full, result, dispenser);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(100, 100)] // pas de frais à 100
    [InlineData(115, 110)] // 110 + 5
    [InlineData(205, 200)]
    public void Balance_exactly_covering_amount_and_fee_is_allowed(int balance, int amount)
    {
        var (result, _) = Run(balance, 0, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Account.Balance);
    }

    [Theory]
    [InlineData(0, 10, 0, 0, 0, null)]          // solde nul, stock vide
    [InlineData(40, 50, 1, 0, 0, Denomination.Fifty)] // le seul billet est bloqué
    [InlineData(20, 30, 0, 5, 0, null)]         // 30 impossible avec des 20 seuls
    [InlineData(110, 110, 0, 0, 0, null)]       // frais compris, solde trop court
    public void Insufficient_funds_is_checked_before_dispensability(
        int balance, int amount, int fifties, int twenties, int tens, Denomination? jammed)
    {
        var stock = Notes(fifties, twenties, tens);
        var (result, dispenser) = Run(balance, 0, stock, jammed, amount);

        AssertRefused(AtmStatus.InsufficientFunds, Account(balance, 0), stock, result, dispenser);
    }

    // ---- Contrôle 4 : combinaison impossible ----

    [Theory]
    [InlineData(10, 0, 0, 0, null)]
    [InlineData(30, 0, 5, 0, null)]
    [InlineData(10, 5, 5, 0, null)]
    [InlineData(50, 1, 0, 0, Denomination.Fifty)]
    [InlineData(40, 5, 5, 0, Denomination.Twenty)]
    [InlineData(10, 5, 5, 5, Denomination.Ten)]
    [InlineData(200, 1, 2, 3, null)] // stock total 120
    public void No_exact_combination_is_refused(int amount, int fifties, int twenties, int tens, Denomination? jammed)
    {
        var stock = Notes(fifties, twenties, tens);
        var (result, dispenser) = Run(250, 0, stock, jammed, amount);

        AssertRefused(AtmStatus.CannotDispense, Account(250, 0), stock, result, dispenser);
    }

    [Fact]
    public void Jammed_cassette_counts_as_zero_even_when_it_would_give_fewer_notes()
    {
        var (result, dispenser) = Run(250, 0, Notes(2, 2, 2), Denomination.Fifty, 50);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(0, 2, 1), result.Notes);
        Assert.Equal(Notes(2, 0, 1), dispenser.Stock);
        Assert.All(dispenser.DispenseCalls, pass => Assert.Equal(0, pass.Fifties));
    }

    // ---- Les frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_is_5_strictly_above_100(int amount, int expectedFee)
    {
        var (result, _) = Run(250, 0, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(Account(250 - amount - expectedFee, amount), result.Account);
    }

    // ---- La combinaison : le moins de billets ----

    [Theory]
    [InlineData(60, 1, 3, 1, 1, 0, 1)]   // 50+10 plutôt que 20×3
    [InlineData(60, 1, 3, 0, 0, 3, 0)]   // sans 10, le glouton échoue : 20×3
    [InlineData(80, 1, 4, 0, 0, 4, 0)]   // 50 puis 30 impossible : 20×4
    [InlineData(130, 2, 5, 0, 1, 4, 0)]  // 50×2 puis 30 impossible : 50 + 20×4
    [InlineData(100, 5, 5, 5, 2, 0, 0)]
    [InlineData(90, 5, 5, 5, 1, 2, 0)]
    [InlineData(40, 5, 5, 5, 0, 2, 0)]
    [InlineData(40, 5, 1, 5, 0, 1, 2)]
    [InlineData(40, 5, 0, 5, 0, 0, 4)]
    [InlineData(50, 0, 5, 5, 0, 2, 1)]
    [InlineData(50, 0, 0, 5, 0, 0, 5)]
    [InlineData(200, 5, 5, 5, 4, 0, 0)]
    [InlineData(200, 1, 5, 5, 1, 5, 5)]  // 15 billets
    public void Combination_has_the_fewest_notes(
        int amount, int sf, int st, int sd, int ef, int et, int ed)
    {
        var (result, dispenser) = Run(250, 0, Notes(sf, st, sd), null, amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(ef, et, ed), result.Notes);
        Assert.Equal(Notes(sf, st, sd) - Notes(ef, et, ed), dispenser.Stock);
    }

    [Fact]
    public void Combination_matches_brute_force_oracle_over_the_whole_stock_domain()
    {
        var failures = new List<string>();
        foreach (var stock in AllStocks())
        foreach (var jammed in JammedValues)
        for (var amount = 10; amount <= 200; amount += 10)
        {
            var (result, dispenser) = Run(250, 0, stock, jammed, amount);
            var expected = FewestNotes(amount, Available(stock, jammed));

            if (expected is { } notes)
            {
                if (result.Status != AtmStatus.Ok || result.Notes != notes || dispenser.Stock != stock - notes)
                {
                    failures.Add($"{stock} bloquée={jammed} retrait {amount} : attendu Ok {notes}, obtenu {result.Status} {result.Notes}");
                }
            }
            else if (result.Status != AtmStatus.CannotDispense || dispenser.Stock != stock || dispenser.DispenseCalls.Count != 0)
            {
                failures.Add($"{stock} bloquée={jammed} retrait {amount} : attendu CannotDispense, obtenu {result.Status}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(20)));
    }

    // ---- La sortie ----

    [Theory]
    [InlineData(200, 1, 5, 5)]
    [InlineData(50, 0, 0, 5)]
    [InlineData(130, 2, 5, 0)]
    [InlineData(200, 5, 5, 5)]
    [InlineData(40, 0, 0, 5)]
    public void Notes_leave_in_passes_of_at_most_4_and_add_up_to_the_combination(int amount, int f, int t, int d)
    {
        var (result, dispenser) = Run(250, 0, Notes(f, t, d), null, amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.NotEmpty(dispenser.DispenseCalls);
        Assert.All(dispenser.DispenseCalls, pass =>
        {
            Assert.InRange(pass.Total, 1, 4);
            Assert.True(pass.Fifties >= 0 && pass.Twenties >= 0 && pass.Tens >= 0);
        });
        var total = dispenser.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b);
        Assert.Equal(result.Notes, total);
        Assert.Empty(dispenser.LoadCalls);
    }

    // ---- Le succès ----

    [Fact]
    public void Success_updates_account_and_returns_notes_and_fee()
    {
        var (result, dispenser) = Run(250, 40, Full, null, 160);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(3, 0, 1), result.Notes);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Account(85, 200), result.Account);
        Assert.Equal(Notes(2, 5, 4), dispenser.Stock);
    }

    // ---- Sans mémoire ----

    [Fact]
    public void Machine_depends_only_on_account_operation_and_dispenser()
    {
        var dispenser = new SpyDispenser(Full);
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(Account(100, 0), Withdraw(50));
        var refused = machine.Execute(Account(0, 0), Withdraw(10));
        var second = machine.Execute(Account(100, 0), Withdraw(50));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(AtmStatus.InsufficientFunds, refused.Status);
        Assert.Equal(first, second);
        Assert.Equal(Notes(3, 5, 5), dispenser.Stock);
    }
}
