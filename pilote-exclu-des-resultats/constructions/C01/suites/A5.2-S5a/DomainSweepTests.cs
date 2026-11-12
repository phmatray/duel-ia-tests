using static Atm.Tests.Fixture;

namespace Atm.Tests;

/// <summary>
/// Parcourt le domaine de la spec et compare la machine à un modèle de référence écrit d'après la spec.
/// Chaque test rassemble les écarts et en affiche les premiers.
/// </summary>
public class DomainSweepTests
{
    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];
    private static readonly int[] Jams = [0, 50, 20, 10];

    private static IEnumerable<NoteCounts> AllStocks() =>
        from f in Enumerable.Range(0, 6)
        from t in Enumerable.Range(0, 6)
        from te in Enumerable.Range(0, 6)
        select Notes(f, t, te);

    private static IEnumerable<NoteCounts> AllDeposits() =>
        from f in Enumerable.Range(0, 2)
        from t in Enumerable.Range(0, 2)
        from te in Enumerable.Range(0, 2)
        select Notes(f, t, te);

    // ---- Modèle de référence ----

    private static (AtmResult Result, NoteCounts StockAfter) ExpectedWithdraw(AccountState account, NoteCounts stock,
        Denomination? jammed, int amount)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (account.WithdrawnToday + amount > 200) return (Refuse(AtmStatus.DailyLimitExceeded), stock);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return (Refuse(AtmStatus.InsufficientFunds), stock);

        var available = Notes(
            jammed == Denomination.Fifty ? 0 : stock.Fifties,
            jammed == Denomination.Twenty ? 0 : stock.Twenties,
            jammed == Denomination.Ten ? 0 : stock.Tens);
        var best = AllStocks()
            .Where(c => c.Fifties <= available.Fifties && c.Twenties <= available.Twenties && c.Tens <= available.Tens)
            .Where(c => c.Value == amount)
            .OrderBy(c => c.Total)
            .ToList();
        if (best.Count == 0) return (Refuse(AtmStatus.CannotDispense), stock);

        var combination = best[0];
        return (new AtmResult(AtmStatus.Ok, combination, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount)), stock - combination);
    }

    private static (AtmResult Result, NoteCounts StockAfter) ExpectedDeposit(AccountState account, NoteCounts stock,
        Denomination? jammed, NoteCounts deposit)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (deposit.Total == 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (jammed is { } j && deposit.Of(j) > 0) return (Refuse(AtmStatus.CassetteJammed), stock);
        var after = stock + deposit;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (Refuse(AtmStatus.CassetteFull), stock);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + deposit.Value }),
            after);
    }

    // ---- Vérification d'un cas ----

    private static string? CheckWithdraw(AccountState account, NoteCounts stock, int jam, int amount)
    {
        var (machine, spy) = Machine(stock, Jam(jam));
        var (expected, expectedStock) = ExpectedWithdraw(account, stock, Jam(jam), amount);
        var label = $"Retrait {amount}, compte {account}, stock {stock}, bloquée {jam}";

        AtmResult actual;
        try
        {
            actual = machine.Execute(account, new Operation.Withdraw(amount));
        }
        catch (Exception e)
        {
            return $"{label} : exception {e.GetType().Name}";
        }

        if (actual != expected) return $"{label} : attendu {expected}, obtenu {actual}";
        if (spy.Stock != expectedStock) return $"{label} : stock attendu {expectedStock}, obtenu {spy.Stock}";
        if (spy.LoadCalls.Count > 0) return $"{label} : Load appelé lors d'un retrait";
        if (spy.DispenseCalls.Any(c => c.Notes.Total > 4 || !c.Result))
            return $"{label} : passage invalide {string.Join(", ", spy.DispenseCalls)}";
        if (expected.Status != AtmStatus.Ok && spy.DispenseCalls.Count > 0) return $"{label} : billets déplacés sur un refus";
        return null;
    }

    private static string? CheckDeposit(AccountState account, NoteCounts stock, int jam, NoteCounts deposit)
    {
        var (machine, spy) = Machine(stock, Jam(jam));
        var (expected, expectedStock) = ExpectedDeposit(account, stock, Jam(jam), deposit);
        var label = $"Dépôt {deposit}, compte {account}, stock {stock}, bloquée {jam}";

        AtmResult actual;
        try
        {
            actual = machine.Execute(account, new Operation.Deposit(deposit));
        }
        catch (Exception e)
        {
            return $"{label} : exception {e.GetType().Name}";
        }

        if (actual != expected) return $"{label} : attendu {expected}, obtenu {actual}";
        if (spy.Stock != expectedStock) return $"{label} : stock attendu {expectedStock}, obtenu {spy.Stock}";
        if (spy.DispenseCalls.Count > 0) return $"{label} : Dispense appelé lors d'un dépôt";
        if (expected.Status != AtmStatus.Ok && spy.LoadCalls.Count > 0) return $"{label} : Load appelé sur un refus";
        return null;
    }

    private static void AssertNoFailure(IEnumerable<string?> checks)
    {
        var failures = checks.OfType<string>().ToList();
        Assert.True(failures.Count == 0,
            $"{failures.Count} écart(s) avec la spec. Premiers :\n" + string.Join("\n", failures.Take(15)));
    }

    // ---- Balayages ----

    [Fact]
    public void Retrait_tous_comptes_tous_montants_stock_plein()
    {
        AssertNoFailure(
            from balance in Enumerable.Range(0, 51).Select(i => i * 5)
            from withdrawn in Enumerable.Range(0, 21).Select(i => i * 10)
            from jam in Jams
            from amount in Amounts
            select CheckWithdraw(new(balance, withdrawn), Full, jam, amount));
    }

    [Theory]
    [InlineData(250, 0)]
    [InlineData(115, 90)]
    [InlineData(60, 140)]
    public void Retrait_tous_stocks_toutes_cassettes_bloquees_tous_montants(int balance, int withdrawn)
    {
        AssertNoFailure(
            from stock in AllStocks()
            from jam in Jams
            from amount in Amounts
            select CheckWithdraw(new(balance, withdrawn), stock, jam, amount));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(125, 70)]
    [InlineData(250, 200)]
    public void Depot_tous_stocks_toutes_cassettes_bloquees_tous_depots(int balance, int withdrawn)
    {
        AssertNoFailure(
            from stock in AllStocks()
            from jam in Jams
            from deposit in AllDeposits()
            select CheckDeposit(new(balance, withdrawn), stock, jam, deposit));
    }
}
