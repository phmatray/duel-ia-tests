namespace Atm.Tests;

/// <summary>
/// Balayage du domaine contre un modèle de référence écrit d'après la spec.
/// Vérifie à chaque fois : le résultat, le stock final, et (sur un succès de retrait) les passages.
/// </summary>
public class DomainSweepTests
{
    private static string F(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";

    private static string F(AtmResult r) =>
        $"{r.Status} billets=[{F(r.Notes)}] frais={r.Fee} solde={r.Account.Balance} retiré={r.Account.WithdrawnToday}";

    private static string F(Operation op) => op switch
    {
        Operation.Withdraw w => $"Retrait {w.Amount}",
        Operation.Deposit d => $"Dépôt [{F(d.Notes)}]",
        _ => op.ToString(),
    };

    private static string Describe(AccountState a, Operation op, NoteCounts stock, Denomination? jammed) =>
        $"solde={a.Balance} retiré={a.WithdrawnToday} stock=[{F(stock)}] bloquée={jammed?.ToString() ?? "aucune"} {F(op)}";

    private static string? Check(AccountState account, Operation op, NoteCounts stock, Denomination? jammed)
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
        AtmResult actual;
        try
        {
            actual = new CashMachine(dispenser).Execute(account, op);
        }
        catch (Exception e)
        {
            return $"{Describe(account, op, stock, jammed)} : exception {e.GetType().Name}";
        }

        var expected = Oracle.Execute(account, op, stock, jammed);
        if (actual != expected)
            return $"{Describe(account, op, stock, jammed)} : attendu {F(expected)} | obtenu {F(actual)}";

        var expectedStock = expected.Status != AtmStatus.Ok ? stock
            : op is Operation.Deposit d ? stock + d.Notes
            : stock - expected.Notes;
        if (dispenser.Stock != expectedStock)
            return $"{Describe(account, op, stock, jammed)} : stock attendu [{F(expectedStock)}], obtenu [{F(dispenser.Stock)}]";

        if (expected.Status != AtmStatus.Ok && (dispenser.DispenseCalls.Count > 0 || dispenser.LoadCalls.Count > 0))
            return $"{Describe(account, op, stock, jammed)} : le module a été sollicité sur un refus";

        if (op is Operation.Withdraw && dispenser.DispenseCalls.Any(p => p.Total > MoneyDispenser.MaxNotesPerPass))
            return $"{Describe(account, op, stock, jammed)} : passage de plus de 4 billets";

        return null;
    }

    private static void AssertNoFailures(IEnumerable<string?> failures)
    {
        var all = failures.OfType<string>().ToList();
        if (all.Count == 0) return;
        var categories = all
            .GroupBy(f => f[(f.IndexOf(" : ", StringComparison.Ordinal) + 3)..] switch
            {
                var m when m.StartsWith("attendu ") => m.Split(' ')[1] + " -> " + m.Split(" | obtenu ")[1].Split(' ')[0],
                var m when m.StartsWith("stock attendu") => "stock final faux",
                var m => m,
            })
            .Select(g => $"  {g.Count(),6} × {g.Key}");
        Assert.Fail($"{all.Count} écarts avec la spec.\nPar catégorie :\n{string.Join("\n", categories)}\n"
            + $"Exemples :\n{string.Join("\n", all.Take(15))}");
    }

    public static TheoryData<int, int> Accounts()
    {
        // Couvre les frontières de solde et de plafond.
        var data = new TheoryData<int, int>();
        foreach (var balance in new[] { 0, 5, 10, 45, 50, 95, 100, 105, 110, 115, 120, 150, 195, 200, 205, 250 })
        foreach (var withdrawn in new[] { 0, 10, 90, 100, 150, 190, 200 })
            data.Add(balance, withdrawn);
        return data;
    }

    [Theory]
    [MemberData(nameof(Accounts))]
    public void Withdraw_MatchesSpec_OverAllStocksJamsAndAmounts(int balance, int withdrawn)
    {
        var account = new AccountState(balance, withdrawn);
        AssertNoFailures(
            from stock in Domain.Stocks()
            from jammed in Domain.JamOptions
            from amount in Domain.Amounts()
            select Check(account, new Operation.Withdraw(amount), stock, jammed));
    }

    [Fact]
    public void Withdraw_MatchesSpec_OverAllAccounts_WithFullStock()
    {
        AssertNoFailures(
            from balance in Enumerable.Range(0, 51).Select(i => i * 5)
            from withdrawn in Enumerable.Range(0, 21).Select(i => i * 10)
            from jammed in Domain.JamOptions
            from amount in Domain.Amounts()
            select Check(new AccountState(balance, withdrawn), new Operation.Withdraw(amount), N.Of(5, 5, 5), jammed));
    }

    [Fact]
    public void Deposit_MatchesSpec_OverAllStocksJamsAndDeposits()
    {
        AssertNoFailures(
            from account in new[] { new AccountState(0, 0), new AccountState(130, 70), new AccountState(250, 200) }
            from stock in Domain.Stocks()
            from jammed in Domain.JamOptions
            from deposit in Domain.Deposits()
            select Check(account, new Operation.Deposit(deposit), stock, jammed));
    }
}
