namespace Atm.Tests;

/// <summary>
/// Parcourt tout le domaine de la spec et compare la machine au modèle de référence (<see cref="Oracle"/>).
/// Sur le domaine, le vrai module ne tombe jamais en panne : aucun résultat attendu n'est DispenserFault.
/// </summary>
public class DomainExhaustiveTests
{
    private static readonly Denomination?[] JammedValues = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return new NoteCounts(f, t, d);
    }

    private static IEnumerable<Operation> Operations()
    {
        foreach (var amount in new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10)))
            yield return new Operation.Withdraw(amount);

        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return new Operation.Deposit(new NoteCounts(f, t, d));
    }

    private static IEnumerable<AccountState> Accounts()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
            yield return new AccountState(balance, withdrawn);
    }

    [Fact]
    public void Tout_le_domaine_est_conforme_a_la_spec()
    {
        var failures = new List<string>();
        var byKind = new Dictionary<string, int>();
        var count = 0;
        var operations = Operations().ToList();
        var accounts = Accounts().ToList();

        foreach (var stock in Stocks())
        foreach (var jammed in JammedValues)
        foreach (var op in operations)
        foreach (var account in accounts)
        {
            count++;
            var module = new MoneyDispenser(stock, jammed);
            var actual = new CashMachine(module).Execute(account, op);
            var (expected, expectedStock) = Oracle.Expected(account, stock, jammed, op);

            if (actual != expected || module.Stock != expectedStock)
            {
                var kind = $"{op.GetType().Name} : attendu {expected.Status}, obtenu {actual.Status}";
                byKind[kind] = byKind.GetValueOrDefault(kind) + 1;
                failures.Add($"compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, {op} : " +
                             $"attendu {expected} / stock {expectedStock}, obtenu {actual} / stock {module.Stock}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} cas non conformes sur {count}.\n" +
            string.Join("\n", byKind.Select(k => $"  {k.Value} × {k.Key}")) +
            "\nPremiers cas :\n" + string.Join("\n", failures.Take(10)));
    }

    [Fact]
    public void Sur_le_domaine_la_combinaison_la_plus_courte_est_toujours_unique()
    {
        // Hypothèse de la spec sur laquelle repose l'oracle.
        foreach (var stock in Stocks())
        for (var amount = 10; amount <= 220; amount += 10)
        {
            var all = Oracle.Combinations(amount, stock).ToList();
            if (all.Count == 0) continue;
            var min = all.Min(c => c.Total);
            Assert.Single(all, c => c.Total == min);
        }
    }
}
