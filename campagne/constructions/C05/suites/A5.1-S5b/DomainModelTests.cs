using System.Collections.Concurrent;
using static Atm.Tests.S;

namespace Atm.Tests;

/// <summary>
/// Compare la machine à un modèle écrit directement depuis la spec, sur tout le domaine :
/// soldes, retraits du jour, stocks, cassette bloquée, montants et dépôts.
/// </summary>
public class DomainModelTests
{
    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> Stocks() =>
        from f in Enumerable.Range(0, 6)
        from t in Enumerable.Range(0, 6)
        from d in Enumerable.Range(0, 6)
        select N(f, t, d);

    private static IEnumerable<NoteCounts> Deposits() =>
        from f in Enumerable.Range(0, 2)
        from t in Enumerable.Range(0, 2)
        from d in Enumerable.Range(0, 2)
        select N(f, t, d);

    private static (AtmResult Result, NoteCounts StockAfter) ModelWithdraw(AccountState acc, int amount, NoteCounts stock, Denomination? jam)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, acc);

        if (amount <= 0 || amount % 10 != 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (acc.WithdrawnToday + amount > 200) return (Refuse(AtmStatus.DailyLimitExceeded), stock);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > acc.Balance) return (Refuse(AtmStatus.InsufficientFunds), stock);

        var avail = N(
            jam == Denomination.Fifty ? 0 : stock.Fifties,
            jam == Denomination.Twenty ? 0 : stock.Twenties,
            jam == Denomination.Ten ? 0 : stock.Tens);

        NoteCounts? best = null;
        for (var f = 0; f <= avail.Fifties; f++)
        for (var t = 0; t <= avail.Twenties; t++)
        {
            var rest = amount - 50 * f - 20 * t;
            if (rest < 0 || rest % 10 != 0 || rest / 10 > avail.Tens) continue;
            var c = N(f, t, rest / 10);
            if (best is null || c.Total < best.Value.Total) best = c;
        }

        if (best is not { } notes) return (Refuse(AtmStatus.CannotDispense), stock);
        return (new AtmResult(AtmStatus.Ok, notes, fee, new AccountState(acc.Balance - amount - fee, acc.WithdrawnToday + amount)), stock - notes);
    }

    private static (AtmResult Result, NoteCounts StockAfter) ModelDeposit(AccountState acc, NoteCounts dep, NoteCounts stock, Denomination? jam)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, acc);

        if (dep.Total == 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (jam is { } j && dep.Of(j) > 0) return (Refuse(AtmStatus.CassetteJammed), stock);
        var after = stock + dep;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (Refuse(AtmStatus.CassetteFull), stock);
        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, acc with { Balance = acc.Balance + dep.Value }), after);
    }

    private static void Check(Func<AccountState, NoteCounts, Denomination?, (Operation Op, (AtmResult, NoteCounts) Expected)[]> cases)
    {
        // Écarts groupés par (statut attendu → statut obtenu), avec quelques exemples par groupe.
        var groups = new ConcurrentDictionary<string, ConcurrentQueue<string>>();
        var counts = new ConcurrentDictionary<string, int>();
        var balances = Enumerable.Range(0, 51).Select(i => i * 5).ToArray();

        void Record(string key, string example)
        {
            counts.AddOrUpdate(key, 1, (_, c) => c + 1);
            var q = groups.GetOrAdd(key, _ => new ConcurrentQueue<string>());
            if (q.Count < 3) q.Enqueue(example);
        }

        Parallel.ForEach(balances, balance =>
        {
            for (var wt = 0; wt <= 200; wt += 10)
            foreach (var jam in Jams)
            foreach (var stock in Stocks())
            {
                var acc = new AccountState(balance, wt);
                foreach (var (op, (expected, expectedStock)) in cases(acc, stock, jam))
                {
                    var dispenser = new MoneyDispenser(stock, jam);
                    AtmResult actual;
                    try { actual = new CashMachine(dispenser).Execute(acc, op); }
                    catch (Exception e)
                    {
                        Record($"{expected.Status} → exception {e.GetType().Name}", Describe(acc, stock, jam, op));
                        continue;
                    }

                    if (actual != expected || dispenser.Stock != expectedStock)
                    {
                        var key = actual.Status == expected.Status ? $"{expected.Status} : contenu différent" : $"{expected.Status} → {actual.Status}";
                        Record(key, $"{Describe(acc, stock, jam, op)}\n      attendu {Short(expected, expectedStock)}\n      obtenu  {Short(actual, dispenser.Stock)}");
                    }
                }
            }
        });

        var total = counts.Values.Sum();
        var report = string.Join("\n", counts.OrderByDescending(kv => kv.Value).Select(kv =>
            $"  [{kv.Value}] {kv.Key}\n    " + string.Join("\n    ", groups[kv.Key])));
        Assert.True(total == 0, $"{total} écarts avec la spec :\n{report}");
    }

    private static string Notes(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";

    private static string Short(AtmResult r, NoteCounts stock) =>
        $"{r.Status}, billets {Notes(r.Notes)}, frais {r.Fee}, solde {r.Account.Balance}, retiré {r.Account.WithdrawnToday}, stock {Notes(stock)}";

    private static string Describe(AccountState acc, NoteCounts stock, Denomination? jam, Operation op) =>
        $"solde {acc.Balance}, retiré {acc.WithdrawnToday}, stock {Notes(stock)}, bloquée {(jam is { } j ? (int)j : "aucune")}, {op}";

    [Fact]
    public void Retrait_conforme_a_la_spec_sur_tout_le_domaine() =>
        Check((acc, stock, jam) => Amounts
            .Select(a => ((Operation)W(a), ModelWithdraw(acc, a, stock, jam)))
            .ToArray());

    [Fact]
    public void Depot_conforme_a_la_spec_sur_tout_le_domaine() =>
        Check((acc, stock, jam) => Deposits()
            .Select(d => ((Operation)D(d), ModelDeposit(acc, d, stock, jam)))
            .ToArray());
}
