using System.Text;
using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Balayage du domaine de la spec contre un oracle écrit directement à partir des règles
/// (indépendant de l'implémentation). Chaque test liste les cas en échec.
/// </summary>
public class DomainSweepTests
{
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

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

    // ---- Oracle ----

    private static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    private static NoteCounts? Fewest(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var c = N(f, t, d);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total))
                best = c;
        }

        return best;
    }

    private static (AtmResult Result, NoteCounts StockAfter) ExpectedWithdraw(
        AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (account.WithdrawnToday + amount > 200) return (Refuse(AtmStatus.DailyLimitExceeded), stock);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return (Refuse(AtmStatus.InsufficientFunds), stock);
        if (Fewest(amount, Available(stock, jammed)) is not { } notes) return (Refuse(AtmStatus.CannotDispense), stock);

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
        return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
    }

    private static (AtmResult Result, NoteCounts StockAfter) ExpectedDeposit(
        AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (notes.Total == 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (jammed is { } j && notes.Of(j) > 0) return (Refuse(AtmStatus.CassetteJammed), stock);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (Refuse(AtmStatus.CassetteFull), stock);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }), after);
    }

    // ---- Vérification ----

    private sealed class Failures
    {
        private readonly StringBuilder _text = new();

        public int Count { get; private set; }

        public int OversizedPasses { get; set; }

        public void Add(string line)
        {
            if (Count++ < 25) _text.AppendLine(line);
        }

        public void AssertNone(int total)
        {
            if (Count > 0)
                Assert.Fail(
                    $"{Count} cas en échec sur {total}, dont {OversizedPasses} avec un passage de plus de 4 billets. " +
                    $"Premiers cas :\n{_text}");
        }
    }

    private static void Check(
        Failures failures, string label, AccountState account, NoteCounts stock, Denomination? jammed, Operation op,
        (AtmResult Result, NoteCounts StockAfter) expected)
    {
        var spy = new SpyDispenser(new MoneyDispenser(stock, jammed));
        AtmResult actual;
        try
        {
            actual = new CashMachine(spy).Execute(account, op);
        }
        catch (Exception e)
        {
            failures.Add($"{label} : exception {e.GetType().Name} {e.Message}");
            return;
        }

        var problems = new List<string>();
        if (actual != expected.Result) problems.Add($"attendu {expected.Result}, obtenu {actual}");
        if (spy.Stock != expected.StockAfter) problems.Add($"stock attendu {expected.StockAfter}, obtenu {spy.Stock}");
        if (spy.Dispensed.Any(p => p.Total > 4)) failures.OversizedPasses++;
        if (spy.Dispensed.Any(p => p.Total > 4)) problems.Add($"passage de plus de 4 billets : {string.Join(", ", spy.Dispensed)}");
        if (problems.Count > 0) failures.Add($"{label} : {string.Join(" ; ", problems)}");
    }

    private static string Describe(AccountState a, NoteCounts s, Denomination? j, string op) =>
        $"solde {a.Balance}, retiré {a.WithdrawnToday}, stock 50×{s.Fifties} 20×{s.Twenties} 10×{s.Tens}, " +
        $"bloquée {(j is { } d ? ((int)d).ToString() : "aucune")}, {op}";

    [Fact]
    public void Retrait_sur_tous_les_stocks_cassettes_bloquees_et_montants()
    {
        var failures = new Failures();
        var total = 0;
        var account = Acc(250, 0);
        foreach (var stock in AllStocks())
        foreach (var jammed in Jams)
        foreach (var amount in Amounts)
        {
            total++;
            Check(failures, Describe(account, stock, jammed, $"retrait {amount}"), account, stock, jammed,
                Withdraw(amount), ExpectedWithdraw(account, stock, jammed, amount));
        }

        failures.AssertNone(total);
    }

    [Fact]
    public void Retrait_sur_tous_les_comptes_et_montants()
    {
        var failures = new Failures();
        var total = 0;
        var stocks = new[] { N(5, 5, 5), N(1, 1, 1), N(0, 5, 5) };
        foreach (var stock in stocks)
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in Amounts)
        {
            total++;
            var account = Acc(balance, withdrawn);
            Check(failures, Describe(account, stock, null, $"retrait {amount}"), account, stock, null,
                Withdraw(amount), ExpectedWithdraw(account, stock, null, amount));
        }

        failures.AssertNone(total);
    }

    [Fact]
    public void Depot_sur_tous_les_stocks_cassettes_bloquees_et_depots()
    {
        var failures = new Failures();
        var total = 0;
        AccountState[] accounts = [Acc(0, 0), Acc(125, 70), Acc(250, 200)];
        foreach (var account in accounts)
        foreach (var stock in AllStocks())
        foreach (var jammed in Jams)
        foreach (var notes in AllDeposits())
        {
            total++;
            Check(failures, Describe(account, stock, jammed, $"dépôt 50×{notes.Fifties} 20×{notes.Twenties} 10×{notes.Tens}"),
                account, stock, jammed, Deposit(notes), ExpectedDeposit(account, stock, jammed, notes));
        }

        failures.AssertNone(total);
    }
}
