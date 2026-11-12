using static Atm.Tests.Helpers;

namespace Atm.Tests;

/// <summary>
/// Balayage du domaine, comparé à un oracle écrit d'après la spec (voir <see cref="Helpers"/>).
/// Chaque test agrège les écarts pour en montrer plusieurs d'un coup.
/// </summary>
public class DomainSweepTests
{
    private static string Describe(string op, AccountState a, NoteCounts stock, Denomination? jammed,
        AtmResult expected, AtmResult actual, NoteCounts expectedStock, NoteCounts actualStock) =>
        $"{op} | compte {a} | stock {stock} | bloquée {jammed?.ToString() ?? "aucune"}\n" +
        $"    attendu {expected}, stock {expectedStock}\n    obtenu  {actual}, stock {actualStock}";

    private static void AssertNoFailure(List<string> failures) =>
        Assert.True(failures.Count == 0,
            $"{failures.Count} écart(s) avec la spec, par exemple :\n" + string.Join("\n", failures.Take(10)));

    /// <summary>Tous les stocks, cassettes bloquées et montants ; compte qui ne limite rien.</summary>
    [Fact]
    public void Retrait_sur_tous_les_stocks_et_montants()
    {
        var account = new AccountState(250, 0);
        var failures = new List<string>();
        foreach (var stock in AllStocks())
        foreach (var jammed in AllJammed)
        foreach (var amount in AllAmounts())
        {
            var expected = ExpectedWithdraw(account, stock, jammed, amount);
            var expectedStock = stock - expected.Notes;
            var (actual, d) = Run(account.Balance, account.WithdrawnToday, stock, jammed, new Operation.Withdraw(amount));
            if (actual != expected || d.Stock != expectedStock)
                failures.Add(Describe($"Retrait {amount}", account, stock, jammed, expected, actual, expectedStock, d.Stock));
        }
        AssertNoFailure(failures);
    }

    /// <summary>Tous les soldes, retraits du jour et montants ; stock plein, sans cassette bloquée.</summary>
    [Fact]
    public void Retrait_sur_tous_les_comptes_et_montants()
    {
        var failures = new List<string>();
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in AllAmounts())
        {
            var account = new AccountState(balance, withdrawn);
            var expected = ExpectedWithdraw(account, Full, null, amount);
            var expectedStock = Full - expected.Notes;
            var (actual, d) = Run(balance, withdrawn, Full, null, new Operation.Withdraw(amount));
            if (actual != expected || d.Stock != expectedStock)
                failures.Add(Describe($"Retrait {amount}", account, Full, null, expected, actual, expectedStock, d.Stock));
        }
        AssertNoFailure(failures);
    }

    /// <summary>Tous les stocks, cassettes bloquées et dépôts.</summary>
    [Fact]
    public void Depot_sur_tous_les_stocks_et_depots()
    {
        var account = new AccountState(100, 50);
        var failures = new List<string>();
        foreach (var stock in AllStocks())
        foreach (var jammed in AllJammed)
        foreach (var notes in AllDeposits())
        {
            var expected = ExpectedDeposit(account, stock, jammed, notes);
            var expectedStock = expected.Status == AtmStatus.Ok ? stock + notes : stock;
            var (actual, d) = Run(account.Balance, account.WithdrawnToday, stock, jammed, new Operation.Deposit(notes));
            if (actual != expected || d.Stock != expectedStock)
                failures.Add(Describe($"Dépôt {notes}", account, stock, jammed, expected, actual, expectedStock, d.Stock));
        }
        AssertNoFailure(failures);
    }
}
