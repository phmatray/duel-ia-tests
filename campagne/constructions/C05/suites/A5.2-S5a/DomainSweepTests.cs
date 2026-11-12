using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Parcourt le domaine de la spec et compare chaque résultat à un oracle écrit directement à partir
/// des règles (combinaison trouvée par force brute). Chaque test rapporte tous les écarts d'un coup.
/// </summary>
public class DomainSweepTests
{
    private static readonly Denomination?[] JammedValues = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static IEnumerable<NoteCounts> Stocks() =>
        from f in Enumerable.Range(0, 6)
        from t in Enumerable.Range(0, 6)
        from d in Enumerable.Range(0, 6)
        select Notes(f, t, d);

    private static IEnumerable<NoteCounts> Deposits() =>
        from f in Enumerable.Range(0, 2)
        from t in Enumerable.Range(0, 2)
        from d in Enumerable.Range(0, 2)
        select Notes(f, t, d);

    private static readonly AccountState[] Accounts =
    [
        Account(250, 0), Account(205, 0), Account(115, 90), Account(110, 0),
        Account(60, 140), Account(55, 0), Account(10, 30), Account(0, 200),
    ];

    [Fact]
    public void Retraits_sur_tout_le_stock_et_toutes_les_cassettes_bloquees()
    {
        var failures = new List<string>();
        foreach (var stock in Stocks())
        foreach (var jammed in JammedValues)
        foreach (var account in Accounts)
        foreach (var amount in Amounts)
        {
            Check(failures, account, stock, jammed, Withdraw(amount), ExpectedWithdraw(account, stock, jammed, amount));
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    [Fact]
    public void Retraits_sur_tous_les_comptes()
    {
        var failures = new List<string>();
        foreach (var stock in new[] { Full, Notes(1, 3, 1), Notes(0, 5, 0), Notes(2, 1, 2) })
        foreach (var jammed in JammedValues)
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in Amounts)
        {
            var account = Account(balance, withdrawn);
            Check(failures, account, stock, jammed, Withdraw(amount), ExpectedWithdraw(account, stock, jammed, amount));
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    [Fact]
    public void Depots_sur_tout_le_stock_et_toutes_les_cassettes_bloquees()
    {
        var failures = new List<string>();
        foreach (var stock in Stocks())
        foreach (var jammed in JammedValues)
        foreach (var account in Accounts)
        foreach (var deposit in Deposits())
        {
            Check(failures, account, stock, jammed, new Operation.Deposit(deposit), ExpectedDeposit(account, stock, jammed, deposit));
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    private static void Check(
        List<string> failures, AccountState account, NoteCounts stock, Denomination? jammed, Operation operation,
        (AtmResult Result, NoteCounts Stock) expected)
    {
        var spy = new SpyDispenser(new MoneyDispenser(stock, jammed));
        var machine = new CashMachine(spy);

        var actual = machine.Execute(account, operation);

        var passesOk = spy.Dispensed.All(p => p.Total is >= 1 and <= 4);
        if (actual != expected.Result || spy.Stock != expected.Stock || !passesOk)
        {
            failures.Add(
                $"{account} stock={stock} bloquée={jammed?.ToString() ?? "aucune"} {operation} : " +
                $"attendu {expected.Result} / stock {expected.Stock}, obtenu {actual} / stock {spy.Stock}" +
                (passesOk ? "" : " / passage de plus de 4 billets"));
        }
    }

    private static string Report(List<string> failures) =>
        $"{failures.Count} écart(s) avec la spec, dont {failures.Count(f => f.Contains("bloquée=Ten"))} avec la cassette de 10 bloquée. Premiers :{Environment.NewLine}" +
        string.Join(Environment.NewLine, failures.Take(15));

    // ---- Oracle ------------------------------------------------------------------------------

    private static (AtmResult, NoteCounts) ExpectedWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus status) => (new AtmResult(status, NoteCounts.None, 0, account), stock);

        if (amount <= 0 || amount % 10 != 0)
        {
            return Refuse(AtmStatus.InvalidAmount);
        }

        if (account.WithdrawnToday + amount > 200)
        {
            return Refuse(AtmStatus.DailyLimitExceeded);
        }

        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance)
        {
            return Refuse(AtmStatus.InsufficientFunds);
        }

        var available = Notes(
            jammed == Denomination.Fifty ? 0 : stock.Fifties,
            jammed == Denomination.Twenty ? 0 : stock.Twenties,
            jammed == Denomination.Ten ? 0 : stock.Tens);

        var best = (
            from f in Enumerable.Range(0, available.Fifties + 1)
            from t in Enumerable.Range(0, available.Twenties + 1)
            from d in Enumerable.Range(0, available.Tens + 1)
            let c = Notes(f, t, d)
            where c.Value == amount
            orderby c.Total
            select (NoteCounts?)c).FirstOrDefault();

        if (best is not { } combination)
        {
            return Refuse(AtmStatus.CannotDispense);
        }

        var after = Account(account.Balance - amount - fee, account.WithdrawnToday + amount);
        return (new AtmResult(AtmStatus.Ok, combination, fee, after), stock - combination);
    }

    private static (AtmResult, NoteCounts) ExpectedDeposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts deposit)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus status) => (new AtmResult(status, NoteCounts.None, 0, account), stock);

        if (deposit.Total == 0)
        {
            return Refuse(AtmStatus.InvalidAmount);
        }

        if (jammed is { } j && deposit.Of(j) > 0)
        {
            return Refuse(AtmStatus.CassetteJammed);
        }

        var after = stock + deposit;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5)
        {
            return Refuse(AtmStatus.CassetteFull);
        }

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + deposit.Value }), after);
    }
}
