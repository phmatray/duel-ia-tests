using System.Text;

namespace Atm.Tests;

/// <summary>
/// Parcourt tout le domaine de la spec et compare la machine à un oracle écrit directement
/// d'après la spec (force brute sur les combinaisons de billets).
/// </summary>
public class DomainSweepTests
{
    private static readonly int[] Balances = Enumerable.Range(0, 51).Select(i => i * 5).ToArray();
    private static readonly int[] WithdrawnValues = Enumerable.Range(0, 21).Select(i => i * 10).ToArray();
    private static readonly int[] Amounts = new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10)).ToArray();
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
            for (var t = 0; t <= 5; t++)
                for (var c = 0; c <= 5; c++)
                    yield return new NoteCounts(f, t, c);
    }

    private static IEnumerable<NoteCounts> Deposits()
    {
        for (var f = 0; f <= 1; f++)
            for (var t = 0; t <= 1; t++)
                for (var c = 0; c <= 1; c++)
                    yield return new NoteCounts(f, t, c);
    }

    // ---------- L'oracle ----------

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
                for (var c = 0; c <= available.Tens; c++)
                {
                    var n = new NoteCounts(f, t, c);
                    if (n.Value == amount && (best is null || n.Total < best.Value.Total))
                        best = n;
                }
        return best;
    }

    private static (AtmResult Result, NoteCounts Stock) ExpectedWithdraw(AccountState acc, NoteCounts stock, Denomination? jam, int amount)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, acc);

        if (amount <= 0 || amount % 10 != 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (acc.WithdrawnToday + amount > 200) return (Refuse(AtmStatus.DailyLimitExceeded), stock);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > acc.Balance) return (Refuse(AtmStatus.InsufficientFunds), stock);
        if (Fewest(amount, Available(stock, jam)) is not { } notes) return (Refuse(AtmStatus.CannotDispense), stock);

        var after = new AccountState(acc.Balance - amount - fee, acc.WithdrawnToday + amount);
        return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
    }

    private static (AtmResult Result, NoteCounts Stock) ExpectedDeposit(AccountState acc, NoteCounts stock, Denomination? jam, NoteCounts notes)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, acc);

        if (notes.Total == 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (jam is { } j && notes.Of(j) > 0) return (Refuse(AtmStatus.CassetteJammed), stock);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (Refuse(AtmStatus.CassetteFull), stock);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, acc with { Balance = acc.Balance + notes.Value }), after);
    }

    // ---------- Les balayages ----------

    [Fact]
    public void Every_withdrawal_in_the_domain_matches_the_spec()
    {
        var failures = new Failures();
        foreach (var balance in Balances.Reverse())
            foreach (var withdrawn in WithdrawnValues)
                foreach (var amount in Amounts)
                    foreach (var jam in Jams)
                        foreach (var stock in Stocks())
                        {
                            var acc = new AccountState(balance, withdrawn);
                            var expected = ExpectedWithdraw(acc, stock, jam, amount);
                            var dispenser = new MoneyDispenser(stock, jam);
                            var actual = new CashMachine(dispenser).Execute(acc, new Operation.Withdraw(amount));
                            failures.Check($"solde {balance}, retiré {withdrawn}, stock {Show(stock)}, bloquée {jam?.ToString() ?? "aucune"}, Retrait {amount}",
                                expected, (actual, dispenser.Stock));
                        }
        failures.AssertNone();
    }

    [Fact]
    public void Every_deposit_in_the_domain_matches_the_spec()
    {
        var failures = new Failures();
        foreach (var stock in Stocks())
            foreach (var jam in Jams)
                foreach (var notes in Deposits())
                    foreach (var balance in Balances)
                        foreach (var withdrawn in WithdrawnValues)
                        {
                            var acc = new AccountState(balance, withdrawn);
                            var expected = ExpectedDeposit(acc, stock, jam, notes);
                            var dispenser = new MoneyDispenser(stock, jam);
                            var actual = new CashMachine(dispenser).Execute(acc, new Operation.Deposit(notes));
                            failures.Check($"solde {balance}, retiré {withdrawn}, stock {Show(stock)}, bloquée {jam?.ToString() ?? "aucune"}, Dépôt {Show(notes)}",
                                expected, (actual, dispenser.Stock));
                        }
        failures.AssertNone();
    }

    private static string Show(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";

    private static string Show(AtmResult r) =>
        $"{r.Status}, billets {Show(r.Notes)}, frais {r.Fee}, solde {r.Account.Balance}, retiré {r.Account.WithdrawnToday}";

    private sealed class Failures
    {
        private const int Shown = 12;
        private readonly List<string> examples = [];
        private readonly Dictionary<string, int> byKind = [];
        private int count;

        public void Check(string scenario, (AtmResult Result, NoteCounts Stock) expected, (AtmResult Result, NoteCounts Stock) actual)
        {
            if (expected == actual) return;
            count++;
            var kind = $"attendu {expected.Result.Status}, obtenu {actual.Result.Status}";
            byKind[kind] = byKind.GetValueOrDefault(kind) + 1;
            if (examples.Count < Shown)
                examples.Add($"- {scenario}\n    attendu : {Show(expected.Result)} | stock après {Show(expected.Stock)}"
                    + $"\n    obtenu  : {Show(actual.Result)} | stock après {Show(actual.Stock)}");
        }

        public void AssertNone()
        {
            if (count == 0) return;
            var kinds = string.Join("\n", byKind.Select(k => $"  {k.Value} × {k.Key}"));
            Assert.Fail($"{count} cas du domaine diffèrent de la spec :\n{kinds}\nPremiers exemples :\n{string.Join("\n", examples)}");
        }
    }
}
