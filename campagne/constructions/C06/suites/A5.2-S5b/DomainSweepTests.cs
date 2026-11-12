using System.Text;

namespace Atm.Tests;

/// <summary>
/// Parcourt de larges portions du domaine et compare à un oracle écrit directement depuis la spec.
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
        for (var n = 0; n <= 5; n++)
            yield return new NoteCounts(f, t, n);
    }

    private static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var n = 0; n <= 1; n++)
            yield return new NoteCounts(f, t, n);
    }

    private static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    /// <summary>Oracle du retrait : rend le résultat attendu et le stock attendu après l'opération.</summary>
    private static (AtmResult Result, NoteCounts Stock) ExpectedWithdraw(AccountState a, NoteCounts stock, Denomination? jammed, int amount)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus s) => (new AtmResult(s, NoteCounts.None, 0, a), stock);

        if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
        if (a.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > a.Balance) return Refuse(AtmStatus.InsufficientFunds);

        var avail = Available(stock, jammed);
        NoteCounts? best = null;
        for (var f = 0; f <= avail.Fifties; f++)
        for (var t = 0; t <= avail.Twenties; t++)
        for (var n = 0; n <= avail.Tens; n++)
        {
            var c = new NoteCounts(f, t, n);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total)) best = c;
        }

        if (best is not { } notes) return Refuse(AtmStatus.CannotDispense);

        return (new AtmResult(AtmStatus.Ok, notes, fee, new AccountState(a.Balance - amount - fee, a.WithdrawnToday + amount)),
                stock - notes);
    }

    /// <summary>Oracle du dépôt.</summary>
    private static (AtmResult Result, NoteCounts Stock) ExpectedDeposit(AccountState a, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus s) => (new AtmResult(s, NoteCounts.None, 0, a), stock);

        if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, a with { Balance = a.Balance + notes.Value }), after);
    }

    private sealed class Failures
    {
        private readonly StringBuilder text = new();
        public int Count { get; private set; }

        public void Check<T>(T expected, T actual, string context)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual)) return;
            if (++Count <= 25) text.AppendLine($"{context}\n    attendu : {expected}\n    obtenu  : {actual}");
        }

        public void AssertNone()
        {
            if (Count > 0) Assert.Fail($"{Count} écart(s) avec la spec. Premiers cas :\n{text}");
        }
    }

    private static void CheckWithdraw(Failures failures, AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var (expected, expectedStock) = ExpectedWithdraw(account, stock, jammed, amount);
        var context = $"Retrait {amount}, compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}";
        AtmResult actual;
        try { actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount)); }
        catch (Exception e) { failures.Check(expected.ToString(), $"exception {e.GetType().Name}: {e.Message}", context); return; }
        failures.Check(expected, actual, context);
        failures.Check(expectedStock, dispenser.Stock, context + " [stock après]");
    }

    [Fact]
    public void Retrait_tous_stocks_toutes_cassettes_tous_montants_comptes_representatifs()
    {
        int[] balances = [0, 5, 10, 50, 95, 100, 105, 110, 115, 150, 155, 200, 205, 250];
        int[] withdrawn = [0, 10, 90, 100, 150, 190, 200];
        var failures = new Failures();

        foreach (var stock in AllStocks())
        foreach (var jammed in Jams)
        foreach (var amount in Amounts)
        foreach (var b in balances)
        foreach (var w in withdrawn)
            CheckWithdraw(failures, new AccountState(b, w), stock, jammed, amount);

        failures.AssertNone();
    }

    [Fact]
    public void Retrait_tous_comptes_tous_montants_stock_plein()
    {
        var failures = new Failures();
        foreach (var jammed in Jams)
        foreach (var amount in Amounts)
        for (var b = 0; b <= 250; b += 5)
        for (var w = 0; w <= 200; w += 10)
            CheckWithdraw(failures, new AccountState(b, w), Support.Full, jammed, amount);

        failures.AssertNone();
    }

    [Fact]
    public void Depot_tout_le_domaine_des_billets()
    {
        AccountState[] accounts = [new(0, 0), new(5, 200), new(115, 90), new(250, 150)];
        var failures = new Failures();

        foreach (var stock in AllStocks())
        foreach (var jammed in Jams)
        foreach (var notes in AllDeposits())
        foreach (var account in accounts)
        {
            var dispenser = new MoneyDispenser(stock, jammed);
            var (expected, expectedStock) = ExpectedDeposit(account, stock, jammed, notes);
            var context = $"Dépôt {notes}, compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}";
            AtmResult actual;
            try { actual = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes)); }
            catch (Exception e) { failures.Check(expected.ToString(), $"exception {e.GetType().Name}: {e.Message}", context); continue; }
            failures.Check(expected, actual, context);
            failures.Check(expectedStock, dispenser.Stock, context + " [stock après]");
        }

        failures.AssertNone();
    }
}
