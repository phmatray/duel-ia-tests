using static Atm.Tests.N;

namespace Atm.Tests;

public class WithdrawTests
{
    private static (AtmResult Result, SpyDispenser Dispenser) Run(
        AccountState account, int amount, NoteCounts? stock = null, Denomination? jammed = null)
    {
        var dispenser = new SpyDispenser(stock ?? Full, jammed);
        var result = new CashMachine(dispenser).Execute(account, Withdraw(amount));

        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AccountState account, AtmResult result, SpyDispenser dispenser, NoteCounts stockBefore)
    {
        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
        Assert.Equal(stockBefore, dispenser.Stock);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
    }

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(-5)]
    public void Invalid_amount_is_refused(int amount)
    {
        var account = Account(250, 0);
        var (result, dispenser) = Run(account, amount);

        AssertRefused(AtmStatus.InvalidAmount, account, result, dispenser, Full);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(150)]
    [InlineData(200)]
    public void Positive_multiples_of_ten_are_valid_amounts(int amount)
    {
        var (result, _) = Run(Account(250, 0), amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
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
        var account = Account(250, withdrawnToday);
        var (result, dispenser) = Run(account, amount);

        if (expected == AtmStatus.Ok)
        {
            Assert.Equal(AtmStatus.Ok, result.Status);
        }
        else
        {
            AssertRefused(expected, account, result, dispenser, Full);
        }
    }

    [Theory]
    [InlineData(90, 110)]
    [InlineData(50, 150)]
    [InlineData(0, 200)]
    public void Fee_does_not_count_toward_daily_limit(int withdrawnToday, int amount)
    {
        var (result, _) = Run(Account(250, withdrawnToday), amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(200, result.Account.WithdrawnToday);
    }

    // ---- Contrôle 3 : fonds insuffisants, frais compris ----

    [Theory]
    [InlineData(100, 100, AtmStatus.Ok)]
    [InlineData(90, 100, AtmStatus.InsufficientFunds)]
    [InlineData(95, 100, AtmStatus.InsufficientFunds)]
    [InlineData(115, 110, AtmStatus.Ok)]
    [InlineData(110, 110, AtmStatus.InsufficientFunds)]
    [InlineData(205, 200, AtmStatus.Ok)]
    [InlineData(200, 200, AtmStatus.InsufficientFunds)]
    [InlineData(0, 10, AtmStatus.InsufficientFunds)]
    [InlineData(10, 10, AtmStatus.Ok)]
    public void Balance_must_cover_amount_plus_fee(int balance, int amount, AtmStatus expected)
    {
        var account = Account(balance, 0);
        var (result, dispenser) = Run(account, amount);

        if (expected == AtmStatus.Ok)
        {
            Assert.Equal(AtmStatus.Ok, result.Status);
            Assert.Equal(balance - amount - result.Fee, result.Account.Balance);
        }
        else
        {
            AssertRefused(expected, account, result, dispenser, Full);
        }
    }

    // ---- Les frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_is_5_strictly_above_100(int amount, int fee)
    {
        var (result, _) = Run(Account(250, 0), amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(250 - amount - fee, result.Account.Balance);
        Assert.Equal(amount, result.Account.WithdrawnToday);
    }

    // ---- Contrôle 4 : impossible à servir ----

    [Theory]
    [InlineData(0, 0, 0, null, 10)]
    [InlineData(1, 0, 0, null, 30)]
    [InlineData(0, 5, 0, null, 10)]
    [InlineData(0, 5, 0, null, 30)]
    [InlineData(0, 1, 0, null, 50)]
    [InlineData(5, 0, 0, 50, 50)]
    [InlineData(0, 5, 5, 10, 30)]
    [InlineData(0, 5, 5, 20, 70)]
    [InlineData(1, 0, 2, null, 80)]
    [InlineData(5, 5, 5, 50, 160)]
    public void Cannot_dispense_when_no_combination_of_available_notes(int s50, int s20, int s10, int? jammed, int amount)
    {
        var account = Account(250, 0);
        var stock = Notes(s50, s20, s10);
        var (result, dispenser) = Run(account, amount, stock, Jam(jammed));

        AssertRefused(AtmStatus.CannotDispense, account, result, dispenser, stock);
    }

    [Fact]
    public void Jammed_cassette_counts_as_empty_even_when_its_notes_would_be_best()
    {
        var (result, dispenser) = Run(Account(250, 0), 100, Notes(5, 5, 5), Denomination.Fifty);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(0, 5, 0), result.Notes);
        Assert.All(dispenser.DispenseCalls, pass => Assert.Equal(0, pass.Fifties));
    }

    // ---- Ordre des contrôles ----

    [Theory]
    // invalide + plafond + fonds + impossible -> invalide
    [InlineData(0, 200, 0, 0, 0, -10, AtmStatus.InvalidAmount)]
    [InlineData(0, 200, 0, 0, 0, 0, AtmStatus.InvalidAmount)]
    [InlineData(0, 200, 0, 0, 0, 5, AtmStatus.InvalidAmount)]
    // plafond + fonds + impossible -> plafond
    [InlineData(0, 200, 0, 0, 0, 10, AtmStatus.DailyLimitExceeded)]
    [InlineData(0, 0, 0, 0, 0, 220, AtmStatus.DailyLimitExceeded)]
    // fonds + impossible -> fonds
    [InlineData(0, 0, 0, 0, 0, 10, AtmStatus.InsufficientFunds)]
    [InlineData(110, 0, 0, 0, 0, 110, AtmStatus.InsufficientFunds)]
    // impossible seul
    [InlineData(250, 0, 0, 0, 0, 10, AtmStatus.CannotDispense)]
    public void Checks_are_applied_in_order(int balance, int withdrawn, int s50, int s20, int s10, int amount, AtmStatus expected)
    {
        var account = Account(balance, withdrawn);
        var stock = Notes(s50, s20, s10);
        var (result, dispenser) = Run(account, amount, stock);

        AssertRefused(expected, account, result, dispenser, stock);
    }

    // ---- La combinaison : le moins de billets ----

    [Theory]
    [InlineData(5, 5, 5, null, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, null, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, null, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, null, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, null, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, null, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, null, 100, 2, 0, 0)]
    [InlineData(5, 5, 5, null, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, null, 200, 4, 0, 0)]
    [InlineData(1, 3, 0, null, 60, 0, 3, 0)]   // 50+10 impossible faute de 10
    [InlineData(1, 5, 0, null, 110, 1, 3, 0)]  // 50+20+20+20
    [InlineData(5, 5, 0, null, 80, 0, 4, 0)]   // 50+? impossible sans 10 -> 20x4
    [InlineData(0, 2, 5, null, 50, 0, 2, 1)]
    [InlineData(0, 0, 5, null, 50, 0, 0, 5)]
    [InlineData(5, 5, 5, 20, 90, 1, 0, 4)]
    [InlineData(5, 5, 5, 10, 60, 0, 3, 0)]
    [InlineData(1, 5, 5, null, 200, 1, 5, 5)]
    public void Uses_the_combination_with_fewest_notes_among_available(
        int s50, int s20, int s10, int? jammed, int amount, int n50, int n20, int n10)
    {
        var stock = Notes(s50, s20, s10);
        var (result, dispenser) = Run(Account(250, 0), amount, stock, Jam(jammed));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(n50, n20, n10), result.Notes);
        Assert.Equal(amount, result.Notes.Value);
        Assert.Equal(stock - result.Notes, dispenser.Stock);
    }

    // ---- La sortie : passages d'au plus 4 billets ----

    [Theory]
    [InlineData(5, 5, 5, null, 10)]
    [InlineData(5, 5, 5, null, 80)]
    [InlineData(5, 5, 5, null, 190)]
    [InlineData(0, 5, 0, null, 100)]
    [InlineData(0, 0, 5, null, 50)]
    [InlineData(1, 5, 5, null, 200)]
    [InlineData(5, 5, 5, 50, 140)]
    [InlineData(5, 5, 5, 20, 190)]
    public void Notes_come_out_in_passes_of_at_most_four_without_touching_jammed_cassette(
        int s50, int s20, int s10, int? jammed, int amount)
    {
        var (result, dispenser) = Run(Account(250, 0), amount, Notes(s50, s20, s10), Jam(jammed));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.NotEmpty(dispenser.DispenseCalls);
        Assert.All(dispenser.DispenseCalls, pass =>
        {
            Assert.InRange(pass.Total, 1, 4);
            Assert.True(pass.Fifties >= 0 && pass.Twenties >= 0 && pass.Tens >= 0);
            if (jammed is { } d)
            {
                Assert.Equal(0, pass.Of((Denomination)d));
            }
        });
        Assert.Equal(result.Notes, dispenser.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(dispenser.DispenseCalls.Count, dispenser.SuccessfulDispenses.Count);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void Five_notes_need_at_least_two_passes()
    {
        var (result, dispenser) = Run(Account(250, 0), 100, Notes(0, 5, 0));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 5, 0), 0, Account(150, 100)), result);
        Assert.True(dispenser.DispenseCalls.Count >= 2);
        Assert.Equal(Notes(0, 0, 0), dispenser.Stock);
    }

    // ---- Le succès ----

    [Fact]
    public void Success_updates_balance_and_withdrawn_today()
    {
        var (result, dispenser) = Run(Account(200, 40), 150);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(3, 0, 0), 5, Account(45, 190)), result);
        Assert.Equal(Notes(2, 5, 5), dispenser.Stock);
    }

    // ---- Pannes ----

    [Theory]
    [InlineData(100, 0, 5, 0)] // 20x5 : deux passages, sans frais
    [InlineData(190, 5, 5, 0)] // 50x3 20x2 : deux passages, avec frais
    [InlineData(200, 1, 5, 5)] // 50x1 20x5 10x5 : 11 billets, au moins trois passages, avec frais
    public void Dispenser_fault_on_first_pass_stops_with_nothing_dispensed(int amount, int s50, int s20, int s10)
    {
        var account = Account(250, 0);
        var dispenser = new SpyDispenser(Notes(s50, s20, s10)) { FailDispenseOnCall = 1 };

        var result = new CashMachine(dispenser).Execute(account, Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Single(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Theory]
    [InlineData(100, 0, 5, 0, 2)]
    [InlineData(190, 5, 5, 0, 2)]
    [InlineData(200, 1, 5, 5, 2)]
    [InlineData(200, 1, 5, 5, 3)]
    public void Dispenser_fault_mid_withdrawal_reports_notes_already_out_and_no_fee(
        int amount, int s50, int s20, int s10, int failingCall)
    {
        var account = Account(250, 0);
        var stock = Notes(s50, s20, s10);
        var dispenser = new SpyDispenser(stock) { FailDispenseOnCall = failingCall };

        var result = new CashMachine(dispenser).Execute(account, Withdraw(amount));

        var alreadyOut = dispenser.SuccessfulDispenses.Aggregate(NoteCounts.None, (a, b) => a + b);
        Assert.Equal(failingCall - 1, dispenser.SuccessfulDispenses.Count);
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, alreadyOut, 0, account), result);
        Assert.Equal(stock - alreadyOut, dispenser.Stock);
        Assert.Equal(failingCall, dispenser.DispenseCalls.Count); // on s'arrête aussitôt
    }

    [Fact]
    public void Dispenser_fault_with_fee_bearing_amount_keeps_fee_at_zero()
    {
        var account = Account(250, 0);
        var dispenser = new SpyDispenser(Full) { FailDispenseOnCall = 1 };

        var result = new CashMachine(dispenser).Execute(account, Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(NoteCounts.None, result.Notes);
    }

    // ---- Sans mémoire ----

    [Fact]
    public void Machine_has_no_memory_between_calls()
    {
        var shared = new MoneyDispenser(Full);
        var machine = new CashMachine(shared);

        var first = machine.Execute(Account(250, 0), Withdraw(200));
        var second = machine.Execute(Account(100, 0), Withdraw(60));

        var fresh = new CashMachine(new MoneyDispenser(Full - first.Notes));
        Assert.Equal(fresh.Execute(Account(100, 0), Withdraw(60)), second);

        var refused1 = machine.Execute(Account(0, 0), Withdraw(10));
        var refused2 = machine.Execute(Account(0, 0), Withdraw(10));
        Assert.Equal(refused1, refused2);
    }
}
