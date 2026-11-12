using System.Text;
using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Parcourt tout le domaine de la spec et compare la machine à un modèle de référence
/// écrit directement d'après la spec (contrôles dans l'ordre, frais, combinaison minimale,
/// refus sans effet).
/// </summary>
public class DomainSweepTests
{
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
            yield return N(f, t, n);
    }

    private static IEnumerable<NoteCounts> Deposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var n = 0; n <= 1; n++)
            yield return N(f, t, n);
    }

    // ---- Modèle de référence -----------------------------------------------------------------

    private static (AtmResult Result, NoteCounts Stock) ExpectedWithdraw(
        AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus s) => (new AtmResult(s, NoteCounts.None, 0, account), stock);

        if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds);

        var available = new NoteCounts(
            jammed == Denomination.Fifty ? 0 : stock.Fifties,
            jammed == Denomination.Twenty ? 0 : stock.Twenties,
            jammed == Denomination.Ten ? 0 : stock.Tens);

        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        {
            var rest = amount - 50 * f - 20 * t;
            if (rest < 0 || rest % 10 != 0 || rest / 10 > available.Tens) continue;
            var c = new NoteCounts(f, t, rest / 10);
            if (best is null || c.Total < best.Value.Total) best = c;
        }

        if (best is not { } notes) return Refuse(AtmStatus.CannotDispense);

        return (new AtmResult(AtmStatus.Ok, notes, fee,
                new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount)),
            stock - notes);
    }

    private static (AtmResult Result, NoteCounts Stock) ExpectedDeposit(
        AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus s) => (new AtmResult(s, NoteCounts.None, 0, account), stock);

        if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0,
                account with { Balance = account.Balance + notes.Value }),
            after);
    }

    // ---- Balayage ----------------------------------------------------------------------------

    private sealed class Mismatches(string what)
    {
        private const int MaxShown = 25;
        private readonly StringBuilder _text = new();
        public int Count { get; private set; }

        public void Check(string input, (AtmResult Result, NoteCounts Stock) expected, AtmResult actual, NoteCounts stock)
        {
            if (expected.Result == actual && expected.Stock == stock) return;
            if (++Count <= MaxShown)
                _text.AppendLine($"{input}\n    attendu : {expected.Result}, stock {expected.Stock}\n    obtenu  : {actual}, stock {stock}");
        }

        public void AssertNone(int total)
        {
            if (Count > 0)
                Assert.Fail($"{what} : {Count} écarts sur {total} cas (les {Math.Min(Count, MaxShown)} premiers) :\n{_text}");
        }
    }

    public static TheoryData<int?> JamCases => new() { (int?)null, 50, 20, 10 };

    [Theory]
    [MemberData(nameof(JamCases))]
    public void Retrait_sur_tout_le_domaine(int? jam)
    {
        var jammed = Jam(jam);
        var mismatches = new Mismatches($"Retrait, cassette bloquée {jammed?.ToString() ?? "aucune"}");
        var total = 0;

        foreach (var stock in Stocks())
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in Amounts)
        {
            total++;
            var account = Acc(balance, withdrawn);
            var expected = ExpectedWithdraw(account, stock, jammed, amount);
            AtmResult actual;
            NoteCounts after;
            try
            {
                (actual, var d) = Withdraw(balance, withdrawn, stock, jammed, amount);
                after = d.Stock;
            }
            catch (Exception e)
            {
                actual = default;
                after = default;
                mismatches.Check($"[exception {e.GetType().Name}: {e.Message}] ", expected, actual, after);
                continue;
            }

            mismatches.Check($"solde {balance}, retiré {withdrawn}, stock {stock}, retrait {amount}",
                expected, actual, after);
        }

        mismatches.AssertNone(total);
    }

    [Fact]
    public void Depot_sur_tout_le_domaine()
    {
        var mismatches = new Mismatches("Dépôt");
        var total = 0;

        foreach (var jammed in Jams)
        foreach (var stock in Stocks())
        foreach (var notes in Deposits())
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        {
            total++;
            var account = Acc(balance, withdrawn);
            var expected = ExpectedDeposit(account, stock, jammed, notes);
            var (actual, d) = Deposit(balance, withdrawn, stock, jammed, notes);
            mismatches.Check($"solde {balance}, retiré {withdrawn}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, dépôt {notes}",
                expected, actual, d.Stock);
        }

        mismatches.AssertNone(total);
    }
}
