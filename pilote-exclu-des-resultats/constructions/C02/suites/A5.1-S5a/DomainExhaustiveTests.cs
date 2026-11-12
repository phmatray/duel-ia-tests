namespace Atm.Tests;

/// <summary>
/// Parcourt tout le domaine de la spec et compare la machine à un oracle écrit directement à partir des règles.
/// </summary>
public class DomainExhaustiveTests
{
    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];
    private static readonly int[] Balances = [.. Enumerable.Range(0, 51).Select(i => i * 5)];
    private static readonly int[] Withdrawn = [.. Enumerable.Range(0, 21).Select(i => i * 10)];

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var a = 0; a <= 5; a++)
        for (var b = 0; b <= 5; b++)
        for (var c = 0; c <= 5; c++)
        {
            yield return new NoteCounts(a, b, c);
        }
    }

    private static IEnumerable<NoteCounts> Deposits()
    {
        for (var a = 0; a <= 1; a++)
        for (var b = 0; b <= 1; b++)
        for (var c = 0; c <= 1; c++)
        {
            yield return new NoteCounts(a, b, c);
        }
    }

    // ---------- Oracle ----------

    private static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        foreach (var candidate in Stocks())
        {
            if (candidate.Fifties <= available.Fifties && candidate.Twenties <= available.Twenties
                && candidate.Tens <= available.Tens && candidate.Value == amount
                && (best is null || candidate.Total < best.Value.Total))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    private static (AtmResult Result, NoteCounts StockAfter) ExpectedWithdraw(
        AccountState account, int amount, NoteCounts stock, Denomination? jammed)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (account.WithdrawnToday + amount > 200) return (Refuse(AtmStatus.DailyLimitExceeded), stock);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return (Refuse(AtmStatus.InsufficientFunds), stock);
        if (FewestNotes(amount, Available(stock, jammed)) is not { } notes) return (Refuse(AtmStatus.CannotDispense), stock);

        return (new AtmResult(AtmStatus.Ok, notes, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount)), stock - notes);
    }

    private static (AtmResult Result, NoteCounts StockAfter) ExpectedDeposit(
        AccountState account, NoteCounts deposit, NoteCounts stock, Denomination? jammed)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (deposit.Total == 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (jammed is { } j && deposit.Of(j) > 0) return (Refuse(AtmStatus.CassetteJammed), stock);
        var after = stock + deposit;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (Refuse(AtmStatus.CassetteFull), stock);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + deposit.Value }), after);
    }

    private static void Check(List<string> failures, string label, (AtmResult Result, NoteCounts StockAfter) expected,
        AtmResult actual, SpyDispenser dispenser)
    {
        if (!expected.Result.Equals(actual) || !expected.StockAfter.Equals(dispenser.Stock))
        {
            failures.Add($"{label}: attendu {expected.Result} stock {expected.StockAfter}, obtenu {actual} stock {dispenser.Stock}");
            return;
        }

        foreach (var pass in dispenser.DispenseCalls)
        {
            if (pass.Total is < 1 or > 4 || (dispenser.Jammed is { } j && pass.Of(j) > 0))
            {
                failures.Add($"{label}: passage invalide {pass}");
            }
        }

        if (actual.Status != AtmStatus.Ok && (dispenser.DispenseCalls.Count > 0 || dispenser.LoadCalls.Count > 0))
        {
            failures.Add($"{label}: refus mais le module a été sollicité");
        }
    }

    private static void AssertNoFailures(List<string> failures) =>
        Assert.True(failures.Count == 0, $"{failures.Count} écart(s) :\n" + string.Join("\n", failures.Take(5)));

    // ---------- Tests ----------

    [Fact]
    public void Withdraw_matches_spec_for_every_account_and_amount_with_full_stock()
    {
        var failures = new List<string>();
        foreach (var balance in Balances)
        foreach (var withdrawn in Withdrawn)
        foreach (var amount in Amounts)
        {
            var account = new AccountState(balance, withdrawn);
            var dispenser = new SpyDispenser(new NoteCounts(5, 5, 5));
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
            Check(failures, $"retrait {amount} compte {account}", ExpectedWithdraw(account, amount, new NoteCounts(5, 5, 5), null), actual, dispenser);
        }

        AssertNoFailures(failures);
    }

    [Fact]
    public void Withdraw_matches_spec_for_every_stock_jam_and_amount()
    {
        var failures = new List<string>();
        AccountState[] accounts = [new(250, 0), new(105, 0), new(100, 100), new(250, 90)];
        foreach (var account in accounts)
        foreach (var stock in Stocks())
        foreach (var jammed in Jams)
        foreach (var amount in Amounts)
        {
            var dispenser = new SpyDispenser(stock, jammed);
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
            Check(failures, $"retrait {amount} compte {account} stock {stock} bloquée {jammed}",
                ExpectedWithdraw(account, amount, stock, jammed), actual, dispenser);
        }

        AssertNoFailures(failures);
    }

    [Fact]
    public void Deposit_matches_spec_for_every_stock_jam_and_deposit()
    {
        var failures = new List<string>();
        AccountState[] accounts = [new(0, 0), new(170, 200), new(250, 30)];
        foreach (var account in accounts)
        foreach (var stock in Stocks())
        foreach (var jammed in Jams)
        foreach (var deposit in Deposits())
        {
            var dispenser = new SpyDispenser(stock, jammed);
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Deposit(deposit));
            Check(failures, $"dépôt {deposit} compte {account} stock {stock} bloquée {jammed}",
                ExpectedDeposit(account, deposit, stock, jammed), actual, dispenser);
        }

        AssertNoFailures(failures);
    }

    [Fact]
    public void Withdraw_fault_at_every_pass_matches_spec()
    {
        var failures = new List<string>();
        var account = new AccountState(250, 0);
        foreach (var stock in Stocks())
        foreach (var jammed in Jams)
        foreach (var amount in Amounts)
        {
            var (expected, _) = ExpectedWithdraw(account, amount, stock, jammed);
            if (expected.Status != AtmStatus.Ok)
            {
                continue;
            }

            var passes = new SpyDispenser(stock, jammed);
            new CashMachine(passes).Execute(account, new Operation.Withdraw(amount));

            for (var failing = 1; failing <= passes.DispenseCalls.Count; failing++)
            {
                var dispenser = new SpyDispenser(stock, jammed) { FailDispenseOnCall = failing };
                var actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
                var alreadyOut = dispenser.SuccessfulDispenses.Aggregate(NoteCounts.None, (a, b) => a + b);
                var want = new AtmResult(AtmStatus.DispenserFault, alreadyOut, 0, account);
                if (!want.Equals(actual) || dispenser.DispenseCalls.Count != failing)
                {
                    failures.Add($"panne au passage {failing}, retrait {amount} stock {stock} bloquée {jammed}: attendu {want}, obtenu {actual} ({dispenser.DispenseCalls.Count} appels)");
                }
            }
        }

        AssertNoFailures(failures);
    }
}
