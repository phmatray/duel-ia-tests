using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Balayage du domaine contre un oracle écrit à partir de la spec. Chaque test agrège les écarts et
/// en affiche les premiers, pour qu'un échec reste lisible.
/// </summary>
public class DomainSweepTests
{
    private const int MaxReported = 15;

    /// <summary>Tous les stocks, cassettes bloquées et montants ; compte large (250, 0).</summary>
    [Fact]
    public void Retrait_sur_tous_les_stocks_et_montants()
    {
        var failures = new List<string>();
        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var amount in DomainAmounts())
        {
            CheckWithdraw(Acc(250, 0), stock, jammed, amount, failures);
        }

        AssertNoFailures(failures);
    }

    /// <summary>Tous les comptes et montants, sur quelques stocks représentatifs : ordre des contrôles.</summary>
    [Fact]
    public void Retrait_sur_tous_les_comptes_et_montants()
    {
        (NoteCounts, Denomination?)[] setups =
        [
            (Full, null),
            (N(0, 0, 0), null),
            (N(1, 0, 0), Denomination.Fifty),
            (N(5, 5, 0), null),
            (N(2, 3, 1), Denomination.Ten),
        ];

        var failures = new List<string>();
        foreach (var (stock, jammed) in setups)
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in DomainAmounts())
        {
            CheckWithdraw(Acc(balance, withdrawn), stock, jammed, amount, failures);
        }

        AssertNoFailures(failures);
    }

    /// <summary>Tout le domaine du dépôt (le compte n'influe que sur le crédit).</summary>
    [Fact]
    public void Depot_sur_tout_le_domaine()
    {
        var failures = new List<string>();
        AccountState[] accounts = [Acc(0, 0), Acc(125, 70), Acc(250, 200)];
        foreach (var account in accounts)
        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var c = 0; c <= 1; c++)
        {
            var notes = N(f, t, c);
            var expected = Oracle.Deposit(account, stock, jammed, notes);
            var (actual, d) = Run(account, stock, jammed, Deposit(notes));
            var expectedStock = expected.Status == AtmStatus.Ok ? stock + notes : stock;

            if (actual != expected || d.Stock != expectedStock || d.Dispensed.Count > 0)
            {
                failures.Add($"compte={account} stock={stock} bloquée={jammed?.ToString() ?? "aucune"} dépôt={notes} : " +
                             $"attendu {expected} stock {expectedStock}, obtenu {actual} stock {d.Stock}");
            }
        }

        AssertNoFailures(failures);
    }

    private static void CheckWithdraw(
        AccountState account, NoteCounts stock, Denomination? jammed, int amount, List<string> failures)
    {
        var expected = Oracle.Withdraw(account, stock, jammed, amount);
        var (actual, d) = Run(account, stock, jammed, Withdraw(amount));
        var expectedStock = expected.Status == AtmStatus.Ok ? stock - expected.Notes : stock;

        var problems = new List<string>();
        if (actual != expected) problems.Add($"attendu {expected}, obtenu {actual}");
        if (d.Stock != expectedStock) problems.Add($"stock attendu {expectedStock}, obtenu {d.Stock}");
        if (d.Dispensed.Any(p => p.Total > MoneyDispenser.MaxNotesPerPass))
            problems.Add($"passage de plus de 4 billets : {string.Join(", ", d.Dispensed)}");
        if (d.Loaded.Count > 0) problems.Add("Load appelé pendant un retrait");

        if (problems.Count > 0)
        {
            failures.Add($"compte={account} stock={stock} bloquée={jammed?.ToString() ?? "aucune"} retrait={amount} : " +
                         string.Join(" ; ", problems));
        }
    }

    private static void AssertNoFailures(List<string> failures)
    {
        Assert.True(failures.Count == 0,
            $"{failures.Count} écart(s) avec la spec. Premiers :\n" + string.Join("\n", failures.Take(MaxReported)));
    }
}
