using System.Text;

namespace Atm.Tests;

/// <summary>
/// Parcourt le domaine de la spec et compare chaque résultat à l'oracle tiré de la spec,
/// y compris le stock du module après l'opération et la taille des passages.
/// </summary>
public class ExhaustiveDomainTests
{
    private const int MaxReported = 10;

    private static string Category(AtmResult expected, string problem) =>
        $"attendu {expected.Status} ({expected.Notes.Total} billets) : {(problem.StartsWith("attendu") ? problem[(problem.IndexOf("obtenu") + 7)..].Split(',')[0] : problem.Split(' ')[0])}";

    private static string Report(int count, Dictionary<string, int> categories, StringBuilder failures) =>
        $"{count} cas en écart avec la spec.\nPar catégorie :\n"
        + string.Join("\n", categories.OrderByDescending(c => c.Value).Select(c => $"  {c.Value,7} × {c.Key}"))
        + $"\nPremiers cas :\n{failures}";

    [Fact]
    public void Withdraw_matches_the_spec_over_the_whole_domain()
    {
        var failures = new StringBuilder();
        var count = 0;
        var categories = new Dictionary<string, int>();

        foreach (var stock in Domain.Stocks())
        foreach (var jammed in Domain.Jams)
        foreach (var amount in Domain.Amounts())
        foreach (var balance in Domain.Balances())
        foreach (var withdrawn in Domain.WithdrawnTodays())
        {
            var account = new AccountState(balance, withdrawn);
            var expected = SpecOracle.Withdraw(account, stock, jammed, amount);
            var dispenser = RecordingDispenser.Real(stock, jammed);

            string? problem;
            try
            {
                var actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
                problem = actual != expected ? $"attendu {expected}, obtenu {actual}"
                    : dispenser.Stock != stock - expected.Notes ? $"stock {dispenser.Stock}, attendu {stock - expected.Notes}"
                    : dispenser.Dispensed.Any(p => p.Total > MoneyDispenser.MaxNotesPerPass) ? "passage de plus de 4 billets"
                    : dispenser.Loaded.Count > 0 ? "Load appelé lors d'un retrait"
                    : null;
            }
            catch (Exception e)
            {
                problem = $"exception {e.GetType().Name}: {e.Message}";
            }

            if (problem is not null)
            {
                var key = Category(expected, problem);
                categories[key] = categories.GetValueOrDefault(key) + 1;
            }

            if (problem is not null && ++count <= MaxReported)
                failures.AppendLine($"Retrait {amount}, {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"} : {problem}");
        }

        Assert.True(count == 0, Report(count, categories, failures));
    }

    [Fact]
    public void Deposit_matches_the_spec_over_the_whole_domain()
    {
        var failures = new StringBuilder();
        var count = 0;
        var categories = new Dictionary<string, int>();

        foreach (var stock in Domain.Stocks())
        foreach (var jammed in Domain.Jams)
        foreach (var notes in Domain.Deposits())
        foreach (var balance in Domain.Balances())
        foreach (var withdrawn in Domain.WithdrawnTodays())
        {
            var account = new AccountState(balance, withdrawn);
            var expected = SpecOracle.Deposit(account, stock, jammed, notes);
            var expectedStock = expected.Status == AtmStatus.Ok ? stock + notes : stock;
            var dispenser = RecordingDispenser.Real(stock, jammed);

            string? problem;
            try
            {
                var actual = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));
                problem = actual != expected ? $"attendu {expected}, obtenu {actual}"
                    : dispenser.Stock != expectedStock ? $"stock {dispenser.Stock}, attendu {expectedStock}"
                    : dispenser.Dispensed.Count > 0 ? "Dispense appelé lors d'un dépôt"
                    : null;
            }
            catch (Exception e)
            {
                problem = $"exception {e.GetType().Name}: {e.Message}";
            }

            if (problem is not null)
            {
                var key = Category(expected, problem);
                categories[key] = categories.GetValueOrDefault(key) + 1;
            }

            if (problem is not null && ++count <= MaxReported)
                failures.AppendLine($"Dépôt {notes}, {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"} : {problem}");
        }

        Assert.True(count == 0, Report(count, categories, failures));
    }
}
