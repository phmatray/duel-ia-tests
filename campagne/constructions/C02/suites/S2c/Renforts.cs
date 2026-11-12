namespace Atm.Tests;

using static Notes;

/// <summary>
/// Tests de renfort : couvrent les comptes de tout le domaine, le découpage en passages,
/// les pannes à chaque passage, la réutilisation de la machine et l'absence d'appel au module sur un refus.
/// </summary>
public class Renforts
{
    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return new NoteCounts(f, t, d);
    }

    private static IEnumerable<AccountState> Accounts()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
            yield return new AccountState(balance, withdrawn);
    }

    private static IEnumerable<int> Amounts() => new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));

    private static IEnumerable<NoteCounts> Deposits()
    {
        for (var mask = 0; mask < 8; mask++)
            yield return new NoteCounts(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
    }

    private static NoteCounts Available(NoteCounts stock, Denomination? jammed) => new(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    /// <summary>Combinaison au plus petit nombre de billets, par recherche exhaustive.</summary>
    private static NoteCounts? Combination(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var c = new NoteCounts(f, t, d);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total)) best = c;
        }

        return best;
    }

    private static NoteCounts Sum(IEnumerable<NoteCounts> passes) => passes.Aggregate(NoteCounts.None, (a, b) => a + b);

    // ----- Contrôles du retrait sur tous les comptes du domaine -----

    [Fact]
    public void Withdraw_AllAccountsAndAmounts_WithFullStock_FollowTheChecksInOrder()
    {
        var full = Of(5, 5, 5);
        foreach (var account in Accounts())
        foreach (var amount in Amounts())
        {
            var dispenser = new MoneyDispenser(full);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

            var fee = amount > 100 ? 5 : 0;
            AtmStatus expected =
                amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
                : account.WithdrawnToday + amount > 200 ? AtmStatus.DailyLimitExceeded
                : amount + fee > account.Balance ? AtmStatus.InsufficientFunds
                : AtmStatus.Ok;

            if (expected == AtmStatus.Ok)
            {
                var notes = Combination(amount, full)!.Value;
                Assert.Equal(
                    new AtmResult(AtmStatus.Ok, notes, fee, new(account.Balance - amount - fee, account.WithdrawnToday + amount)),
                    result);
                Assert.Equal(full - notes, dispenser.Stock);
            }
            else
            {
                Assert.Equal(new AtmResult(expected, default, 0, account), result);
                Assert.Equal(full, dispenser.Stock);
            }
        }
    }

    [Fact]
    public void Withdraw_AllAccounts_WithEmptyStock_CannotDispenseOnlyAfterOtherChecks()
    {
        foreach (var account in Accounts())
        foreach (var amount in Amounts())
        {
            var dispenser = new FakeDispenser(Of(0, 0, 0));
            var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

            var fee = amount > 100 ? 5 : 0;
            AtmStatus expected =
                amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
                : account.WithdrawnToday + amount > 200 ? AtmStatus.DailyLimitExceeded
                : amount + fee > account.Balance ? AtmStatus.InsufficientFunds
                : AtmStatus.CannotDispense;

            Assert.Equal(new AtmResult(expected, default, 0, account), result);
            Assert.Empty(dispenser.DispenseCalls);
            Assert.Empty(dispenser.LoadCalls);
        }
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(120, 5)]
    [InlineData(200, 5)]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    public void Fee_DependsOnlyOnAmount(int amount, int fee)
    {
        var result = new CashMachine(new MoneyDispenser(Of(5, 5, 5))).Execute(new(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(250 - amount - fee, result.Account.Balance);
        Assert.Equal(amount, result.Account.WithdrawnToday);
    }

    [Fact]
    public void FeeOneEuroShort_IsInsufficientFunds()
    {
        var result = new CashMachine(new MoneyDispenser(Of(5, 5, 5))).Execute(new(200, 0), new Operation.Withdraw(200));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, default, 0, new(200, 0)), result);
    }

    // ----- Découpage en passages, sur tout le domaine -----

    [Fact]
    public void Withdraw_Passes_AreValidForTheRealModule_OverDomain()
    {
        var account = new AccountState(250, 0);
        foreach (var stock in Stocks())
        foreach (var jammed in JamOptions)
        for (var amount = 10; amount <= 200; amount += 10)
        {
            if (Combination(amount, Available(stock, jammed)) is not { } expected) continue;

            var dispenser = new FakeDispenser(stock, jammed);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

            Assert.Equal(AtmStatus.Ok, result.Status);
            Assert.Equal(expected, result.Notes);
            Assert.Equal(expected, Sum(dispenser.DispenseCalls));
            Assert.Equal((expected.Total + 3) / 4, dispenser.DispenseCalls.Count);
            Assert.Empty(dispenser.LoadCalls);

            var remaining = stock;
            foreach (var pass in dispenser.DispenseCalls)
            {
                Assert.InRange(pass.Total, 1, 4);
                Assert.True(pass.Fifties >= 0 && pass.Twenties >= 0 && pass.Tens >= 0);
                if (jammed is { } j) Assert.Equal(0, pass.Of(j));
                Assert.True(pass.Fifties <= remaining.Fifties && pass.Twenties <= remaining.Twenties && pass.Tens <= remaining.Tens);
                remaining -= pass;
            }
        }
    }

    // ----- Pannes à chaque passage -----

    [Fact]
    public void FaultOnThirdPass_ReturnsTheNotesOfBothPreviousPasses()
    {
        var dispenser = new FakeDispenser(Of(1, 5, 5)) { FailDispenseOnPass = 3 };
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(190));

        Assert.Equal(3, dispenser.DispenseCalls.Count);
        Assert.Equal(
            new AtmResult(AtmStatus.DispenserFault, dispenser.DispenseCalls[0] + dispenser.DispenseCalls[1], 0, new(250, 0)),
            result);
        Assert.Equal(8, result.Notes.Total);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void FaultOnAnyPass_OverDomain_ReturnsNotesAlreadyDispensedAndStops()
    {
        var account = new AccountState(250, 30);
        foreach (var stock in Stocks())
        foreach (var jammed in JamOptions)
        for (var amount = 10; amount <= 170; amount += 10)
        {
            if (Combination(amount, Available(stock, jammed)) is not { } expected) continue;

            var passes = (expected.Total + 3) / 4;
            for (var failing = 1; failing <= passes; failing++)
            {
                var dispenser = new FakeDispenser(stock, jammed) { FailDispenseOnPass = failing };
                var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

                Assert.Equal(failing, dispenser.DispenseCalls.Count);
                var already = Sum(dispenser.DispenseCalls.Take(failing - 1));
                Assert.Equal(new AtmResult(AtmStatus.DispenserFault, already, 0, account), result);
                Assert.Empty(dispenser.LoadCalls);
            }
        }
    }

    // ----- Refus : aucun appel au module -----

    [Fact]
    public void Withdraw_Refusals_OverDomain_NeverCallTheModule()
    {
        AccountState[] accounts = [new(0, 0), new(50, 0), new(105, 100), new(250, 150), new(250, 0)];
        foreach (var account in accounts)
        foreach (var stock in Stocks().Where((_, i) => i % 7 == 0))
        foreach (var jammed in JamOptions)
        foreach (var amount in Amounts())
        {
            var dispenser = new FakeDispenser(stock, jammed);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
            if (result.Status == AtmStatus.Ok) continue;

            Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
            Assert.Equal(new AtmResult(result.Status, default, 0, account), result);
            Assert.Empty(dispenser.DispenseCalls);
            Assert.Empty(dispenser.LoadCalls);
            Assert.Equal(stock, dispenser.Stock);
        }
    }

    // ----- Dépôt -----

    [Fact]
    public void Deposit_AllAccounts_CreditOnlyTheBalance()
    {
        foreach (var account in Accounts())
        foreach (var deposit in Deposits().Skip(1))
        {
            var dispenser = new MoneyDispenser(Of(2, 2, 2));
            var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(deposit));

            Assert.Equal(
                new AtmResult(AtmStatus.Ok, default, 0, new(account.Balance + deposit.Value, account.WithdrawnToday)),
                result);
            Assert.Equal(Of(2, 2, 2) + deposit, dispenser.Stock);
        }
    }

    [Fact]
    public void Deposit_OverDomain_CallsTheModuleOnlyOnSuccessPath_WithExactlyTheNotes()
    {
        var account = new AccountState(35, 120);
        foreach (var stock in Stocks())
        foreach (var jammed in JamOptions)
        foreach (var deposit in Deposits())
        {
            var after = stock + deposit;
            var accepted = deposit.Total > 0
                && (jammed is not { } j || deposit.Of(j) == 0)
                && after.Fifties <= 5 && after.Twenties <= 5 && after.Tens <= 5;

            var dispenser = new FakeDispenser(stock, jammed);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(deposit));

            Assert.Empty(dispenser.DispenseCalls);
            if (accepted)
            {
                Assert.Equal(new AtmResult(AtmStatus.Ok, default, 0, account with { Balance = 35 + deposit.Value }), result);
                Assert.Equal([deposit], dispenser.LoadCalls);
            }
            else
            {
                Assert.NotEqual(AtmStatus.Ok, result.Status);
                Assert.Equal(new AtmResult(result.Status, default, 0, account), result);
                Assert.Empty(dispenser.LoadCalls);
            }

            // Panne au chargement : on s'arrête, rien ne change côté compte.
            var failing = new FakeDispenser(stock, jammed) { FailLoad = true };
            var faulty = new CashMachine(failing).Execute(account, new Operation.Deposit(deposit));
            if (accepted)
            {
                Assert.Equal(new AtmResult(AtmStatus.DispenserFault, default, 0, account), faulty);
                Assert.Single(failing.LoadCalls);
            }
            else
            {
                Assert.Equal(result, faulty);
                Assert.Empty(failing.LoadCalls);
            }

            Assert.Empty(failing.DispenseCalls);
        }
    }

    [Fact]
    public void Deposit_JammedIsCheckedBeforeFull_WithTheRealModule()
    {
        var dispenser = new MoneyDispenser(Of(0, 5, 0), Denomination.Ten);
        var result = new CashMachine(dispenser).Execute(new(10, 0), new Operation.Deposit(Of(0, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, default, 0, new(10, 0)), result);
        Assert.Equal(Of(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_FullCassetteOtherThanTheJammedOne_IsCassetteFull()
    {
        var dispenser = new MoneyDispenser(Of(5, 0, 0), Denomination.Ten);
        var result = new CashMachine(dispenser).Execute(new(10, 0), new Operation.Deposit(Of(1, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, default, 0, new(10, 0)), result);
        Assert.Equal(Of(5, 0, 0), dispenser.Stock);
    }

    // ----- Machine sans mémoire : elle relit le module à chaque appel -----

    [Fact]
    public void ReusedMachine_SeesTheStockLeftByAPreviousWithdrawal()
    {
        var dispenser = new MoneyDispenser(Of(1, 0, 0));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new(250, 0), new Operation.Withdraw(50));
        var second = machine.Execute(first.Account, new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(1, 0, 0), 0, new(200, 50)), first);
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, default, 0, new(200, 50)), second);
        Assert.Equal(Of(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void ReusedMachine_SeesTheStockLeftByAPreviousDeposit()
    {
        var dispenser = new MoneyDispenser(Of(0, 4, 0));
        var machine = new CashMachine(dispenser);

        var deposit = machine.Execute(new(0, 0), new Operation.Deposit(Of(1, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, default, 0, new(70, 0)), deposit);

        var full = machine.Execute(deposit.Account, new Operation.Deposit(Of(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, default, 0, new(70, 0)), full);

        var withdraw = machine.Execute(deposit.Account, new Operation.Withdraw(70));
        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(1, 1, 0), 0, new(0, 70)), withdraw);
        Assert.Equal(Of(0, 4, 0), dispenser.Stock);
    }

    [Fact]
    public void ReusedMachine_DoesNotRememberAccounts()
    {
        var machine = new CashMachine(new MoneyDispenser(Of(5, 5, 5)));

        var first = machine.Execute(new(250, 150), new Operation.Withdraw(50));
        var second = machine.Execute(new(250, 150), new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(1, 0, 0), 0, new(200, 200)), first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ReusedMachine_AfterAFault_StartsFresh()
    {
        var dispenser = new FakeDispenser(Of(5, 5, 5)) { FailDispenseOnPass = 1 };
        var machine = new CashMachine(dispenser);

        var faulty = machine.Execute(new(250, 0), new Operation.Withdraw(60));
        var next = machine.Execute(new(250, 0), new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, default, 0, new(250, 0)), faulty);
        Assert.Equal(new AtmResult(AtmStatus.Ok, Of(1, 0, 1), 0, new(190, 60)), next);
        Assert.Equal([Of(1, 0, 1), Of(1, 0, 1)], dispenser.DispenseCalls);
    }
}
