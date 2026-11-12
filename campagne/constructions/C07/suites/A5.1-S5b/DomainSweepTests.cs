using Atm.Tests.Support;

namespace Atm.Tests;

/// <summary>
/// Balayage du domaine contre un oracle écrit d'après la spec : statut, billets, frais, compte, stock final
/// et taille des passages.
/// </summary>
public class DomainSweepTests
{
    private sealed record Failure(string Kind, string Detail);

    private static Failure? Check(Run run, AtmResult expected, NoteCounts expectedStock, string label)
    {
        var actual = run.Result;
        if (actual.Status != expected.Status)
            return new($"statut attendu {expected.Status}, obtenu {actual.Status}", $"{label} : obtenu {actual}");
        if (actual.Notes != expected.Notes)
            return new($"{expected.Status} : mauvais billets", $"{label} : attendu {expected.Notes}, obtenu {actual.Notes}");
        if (actual.Fee != expected.Fee)
            return new($"{expected.Status} : mauvais frais", $"{label} : attendu {expected.Fee}, obtenu {actual.Fee}");
        if (actual.Account != expected.Account)
            return new($"{expected.Status} : mauvais compte", $"{label} : attendu {expected.Account}, obtenu {actual.Account}");
        if (run.Dispenser.Stock != expectedStock)
            return new($"{expected.Status} : mauvais stock final", $"{label} : attendu {expectedStock}, obtenu {run.Dispenser.Stock}");
        if (run.Dispenser.Dispenses.Any(d => d.Notes.Total is < 1 or > MoneyDispenser.MaxNotesPerPass))
            return new("passage hors de 1 à 4 billets", label);
        return null;
    }

    /// <summary>Un message par sorte d'écart, avec son nombre et quelques exemples.</summary>
    private static void AssertNoFailure(List<Failure> failures)
    {
        var report = failures
            .GroupBy(f => f.Kind)
            .Select(g => $"[{g.Count()}×] {g.Key}\n    " + string.Join("\n    ", g.Take(3).Select(f => f.Detail)));
        Assert.True(failures.Count == 0, $"{failures.Count} écart(s) :\n{string.Join("\n", report)}");
    }

    [Fact]
    public void Retrait_sur_tous_les_comptes_et_montants_avec_stock_plein()
    {
        var failures = new List<Failure>();
        var stock = new NoteCounts(5, 5, 5);

        foreach (var jammed in Scenario.JammedValues)
        foreach (var balance in Scenario.DomainBalances)
        foreach (var today in Scenario.DomainWithdrawn)
        foreach (var amount in Scenario.DomainAmounts)
        {
            var account = new AccountState(balance, today);
            var expected = Oracle.Withdraw(account, stock, jammed, amount);
            var run = Scenario.Withdraw(balance, today, stock, jammed, amount);
            var label = $"retrait {amount}, solde {balance}, retiré {today}, bloquée {jammed}";
            if (Check(run, expected, stock - expected.Notes, label) is { } failure) failures.Add(failure);
        }

        AssertNoFailure(failures);
    }

    [Fact]
    public void Retrait_sur_tous_les_stocks_cassettes_bloquees_et_montants()
    {
        var failures = new List<Failure>();
        AccountState[] accounts = [new(250, 0), new(105, 0), new(250, 100), new(60, 150)];

        foreach (var stock in Scenario.AllStocks())
        foreach (var jammed in Scenario.JammedValues)
        foreach (var account in accounts)
        foreach (var amount in Scenario.DomainAmounts)
        {
            var expected = Oracle.Withdraw(account, stock, jammed, amount);
            var run = Scenario.Withdraw(account.Balance, account.WithdrawnToday, stock, jammed, amount);
            var label = $"retrait {amount}, {account}, stock {stock}, bloquée {jammed}";
            if (Check(run, expected, stock - expected.Notes, label) is { } failure) failures.Add(failure);
        }

        AssertNoFailure(failures);
    }

    [Fact]
    public void Depot_sur_tous_les_stocks_cassettes_bloquees_et_depots()
    {
        var failures = new List<Failure>();
        AccountState[] accounts = [new(0, 0), new(125, 70), new(250, 200)];

        foreach (var stock in Scenario.AllStocks())
        foreach (var jammed in Scenario.JammedValues)
        foreach (var account in accounts)
        foreach (var deposit in Scenario.AllDeposits())
        {
            var expected = Oracle.Deposit(account, stock, jammed, deposit);
            var expectedStock = expected.Status == AtmStatus.Ok ? stock + deposit : stock;
            var run = Scenario.Deposit(account.Balance, account.WithdrawnToday, stock, jammed, deposit);
            var label = $"dépôt {deposit}, {account}, stock {stock}, bloquée {jammed}";
            if (Check(run, expected, expectedStock, label) is { } failure) failures.Add(failure);
            else if (run.Dispenser.Dispenses.Count > 0) failures.Add(new("Dispense appelé pendant un dépôt", label));
        }

        AssertNoFailure(failures);
    }
}
