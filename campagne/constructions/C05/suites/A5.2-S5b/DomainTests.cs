using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Parcourt tout le domaine de la spec et compare chaque résultat à l'oracle (<see cref="Oracle"/>) :
/// statut, billets, frais, compte, stock final, et passages de 4 billets au plus.
/// </summary>
public class DomainTests
{
    private const int MaxReported = 15;

    private static readonly int[] Jammeds = [0, 50, 20, 10];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return N(f, t, d);
    }

    private static IEnumerable<AccountState> Accounts()
    {
        for (var b = 0; b <= 250; b += 5)
        for (var w = 0; w <= 200; w += 10)
            yield return Acc(b, w);
    }

    private static IEnumerable<NoteCounts> Deposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return N(f, t, d);
    }

    public static TheoryData<int, int> WithdrawCases()
    {
        var data = new TheoryData<int, int>();
        foreach (var j in Jammeds)
        foreach (var a in Amounts)
            data.Add(j, a);
        return data;
    }

    public static TheoryData<int> JammedCases() => new(Jammeds);

    [Theory]
    [MemberData(nameof(WithdrawCases))]
    public void Retrait_sur_tout_le_domaine(int jammed, int amount)
    {
        var jam = Jam(jammed);
        var failures = new List<string>();
        var count = 0;

        foreach (var stock in Stocks())
        foreach (var account in Accounts())
        {
            var (expected, expectedStock) = Oracle.Withdraw(account, amount, stock, jam);
            var (machine, spy) = Machine(stock, jam);
            var actual = machine.Execute(account, new Operation.Withdraw(amount));

            var problem = Compare(expected, expectedStock, actual, spy);
            if (problem is not null)
            {
                count++;
                if (failures.Count < MaxReported)
                    failures.Add($"compte {account}, stock {stock}, retrait {amount} : {problem}");
            }
        }

        Assert.True(count == 0, $"{count} écart(s), cassette bloquée {jammed}, retrait {amount} :\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(JammedCases))]
    public void Depot_sur_tout_le_domaine(int jammed)
    {
        var jam = Jam(jammed);
        var failures = new List<string>();
        var count = 0;

        foreach (var stock in Stocks())
        foreach (var account in Accounts())
        foreach (var notes in Deposits())
        {
            var (expected, expectedStock) = Oracle.Deposit(account, notes, stock, jam);
            var (machine, spy) = Machine(stock, jam);
            var actual = machine.Execute(account, new Operation.Deposit(notes));

            var problem = Compare(expected, expectedStock, actual, spy);
            if (problem is null && spy.Dispenses.Count > 0)
                problem = "un dépôt ne doit rien sortir";
            if (problem is not null)
            {
                count++;
                if (failures.Count < MaxReported)
                    failures.Add($"compte {account}, stock {stock}, dépôt {notes} : {problem}");
            }
        }

        Assert.True(count == 0, $"{count} écart(s), cassette bloquée {jammed} :\n" + string.Join("\n", failures));
    }

    private static string? Compare(AtmResult expected, NoteCounts expectedStock, AtmResult actual, SpyDispenser spy)
    {
        if (actual != expected)
            return $"attendu {expected}, obtenu {actual}";
        if (spy.Stock != expectedStock)
            return $"stock final attendu {expectedStock}, obtenu {spy.Stock}";
        if (spy.Dispenses.FirstOrDefault(d => d.Notes.Total > MoneyDispenser.MaxNotesPerPass) is { Notes.Total: > 0 } big)
            return $"passage de {big.Notes.Total} billets";
        if (spy.Dispensed != actual.Notes)
            return $"billets sortis par le module {spy.Dispensed} ≠ Notes {actual.Notes}";
        return null;
    }
}
