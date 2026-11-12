namespace Atm.Tests;

using static T;

/// <summary>
/// Balayage du domaine de la spec, comparé à un oracle écrit directement depuis la spec
/// (recherche exhaustive de la combinaison, pas d'algorithme glouton).
/// </summary>
public class DomainSweepTests
{
    private static readonly int[] Amounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly int[] Balances = [.. Enumerable.Range(0, 51).Select(i => i * 5)];

    private static readonly int[] WithdrawnTodays = [.. Enumerable.Range(0, 21).Select(i => i * 10)];

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return N(f, t, d);
    }

    private static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return N(f, t, d);
    }

    public static TheoryData<int> JammedCases() => new() { -1, 50, 20, 10 };

    private static Denomination? ToJammed(int code) => code < 0 ? null : (Denomination)code;

    // --- Oracle -----------------------------------------------------------------------------

    private static (AtmResult Result, NoteCounts StockAfter) ExpectedWithdraw(
        AccountState account, int amount, NoteCounts stock, Denomination? jammed)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (account.WithdrawnToday + amount > 200) return (Refuse(AtmStatus.DailyLimitExceeded), stock);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return (Refuse(AtmStatus.InsufficientFunds), stock);

        var a50 = jammed == Denomination.Fifty ? 0 : stock.Fifties;
        var a20 = jammed == Denomination.Twenty ? 0 : stock.Twenties;
        var a10 = jammed == Denomination.Ten ? 0 : stock.Tens;

        var candidates = new List<NoteCounts>();
        for (var f = 0; f <= a50; f++)
        for (var t = 0; t <= a20; t++)
        for (var d = 0; d <= a10; d++)
        {
            if (f * 50 + t * 20 + d * 10 == amount) candidates.Add(N(f, t, d));
        }

        NoteCounts? best = null;
        if (candidates.Count > 0)
        {
            var fewest = candidates.Min(c => c.Total);
            var minimal = candidates.Where(c => c.Total == fewest).ToList();
            if (minimal.Count != 1)
            {
                throw new InvalidOperationException(
                    $"La spec affirme l'unicité de la combinaison minimale : {amount} = {string.Join(" = ", minimal)}");
            }

            best = minimal[0];
        }

        if (best is not { } combo) return (Refuse(AtmStatus.CannotDispense), stock);

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
        return (new AtmResult(AtmStatus.Ok, combo, fee, after), stock - combo);
    }

    private static (AtmResult Result, NoteCounts StockAfter) ExpectedDeposit(
        AccountState account, NoteCounts notes, NoteCounts stock, Denomination? jammed)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (notes.Total == 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (jammed is { } j && notes.Of(j) > 0) return (Refuse(AtmStatus.CassetteJammed), stock);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (Refuse(AtmStatus.CassetteFull), stock);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }), after);
    }

    // --- Balayages --------------------------------------------------------------------------

    private static void Check(
        List<string> failures, string label, (AtmResult Result, NoteCounts StockAfter) expected,
        AtmResult actual, NoteCounts actualStock)
    {
        if (expected.Result != actual || expected.StockAfter != actualStock)
        {
            failures.Add($"{label}\n    attendu {expected.Result}, stock {expected.StockAfter}\n    obtenu  {actual}, stock {actualStock}");
        }
    }

    private static void Report(List<string> failures, int total)
    {
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} cas sur {total} non conformes. Premiers :\n" + string.Join("\n", failures.Take(10)));
    }

    [Theory]
    [MemberData(nameof(JammedCases))]
    public void Retrait_sur_tous_les_comptes_du_domaine(int jammedCode)
    {
        var jammed = ToJammed(jammedCode);
        NoteCounts[] stocks = [N(5, 5, 5), N(0, 0, 0), N(1, 3, 0), N(2, 2, 2), N(0, 5, 5)];
        var failures = new List<string>();
        var total = 0;

        foreach (var stock in stocks)
        foreach (var balance in Balances)
        foreach (var withdrawn in WithdrawnTodays)
        foreach (var amount in Amounts)
        {
            total++;
            var account = Acc(balance, withdrawn);
            var (machine, dispenser) = Real(stock, jammed);
            var actual = machine.Execute(account, W(amount));
            Check(failures, $"compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, retrait {amount}",
                ExpectedWithdraw(account, amount, stock, jammed), actual, dispenser.Stock);
        }

        Report(failures, total);
    }

    [Theory]
    [MemberData(nameof(JammedCases))]
    public void Retrait_sur_tous_les_stocks_du_domaine(int jammedCode)
    {
        var jammed = ToJammed(jammedCode);
        AccountState[] accounts = [Acc(250, 0), Acc(250, 100), Acc(105, 0), Acc(205, 0)];
        var failures = new List<string>();
        var total = 0;

        foreach (var stock in AllStocks())
        foreach (var account in accounts)
        foreach (var amount in Amounts)
        {
            total++;
            var (machine, dispenser) = Real(stock, jammed);
            var actual = machine.Execute(account, W(amount));
            Check(failures, $"compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, retrait {amount}",
                ExpectedWithdraw(account, amount, stock, jammed), actual, dispenser.Stock);
        }

        Report(failures, total);
    }

    [Theory]
    [MemberData(nameof(JammedCases))]
    public void Depot_sur_tout_le_domaine(int jammedCode)
    {
        var jammed = ToJammed(jammedCode);
        AccountState[] accounts = [Acc(0, 0), Acc(10, 30), Acc(170, 200), Acc(250, 100)];
        var failures = new List<string>();
        var total = 0;

        foreach (var stock in AllStocks())
        foreach (var account in accounts)
        foreach (var deposit in AllDeposits())
        {
            total++;
            var (machine, dispenser) = Real(stock, jammed);
            var actual = machine.Execute(account, new Operation.Deposit(deposit));
            Check(failures, $"compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, dépôt {deposit}",
                ExpectedDeposit(account, deposit, stock, jammed), actual, dispenser.Stock);
        }

        Report(failures, total);
    }
}
