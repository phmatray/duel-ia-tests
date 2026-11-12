namespace Atm.Tests;

/// <summary>
/// Parcourt tout le domaine défini par la spec (solde, déjà retiré, stock, cassette bloquée) pour chaque
/// opération du domaine, et compare la machine à l'oracle <see cref="ReferenceModel"/>.
/// </summary>
public class ExhaustiveDomainTests
{
    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static TheoryData<int> Amounts()
    {
        var data = new TheoryData<int> { -10, 0, 5 };
        for (var amount = 10; amount <= 220; amount += 10) data.Add(amount);
        return data;
    }

    public static TheoryData<int, int, int> Deposits()
    {
        var data = new TheoryData<int, int, int>();
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            data.Add(f, t, d);
        return data;
    }

    [Theory]
    [MemberData(nameof(Amounts))]
    public void Withdraw_matches_the_spec_on_the_whole_domain(int amount) => CheckWholeDomain(Withdraw(amount));

    [Theory]
    [MemberData(nameof(Deposits))]
    public void Deposit_matches_the_spec_on_the_whole_domain(int fifties, int twenties, int tens) =>
        CheckWholeDomain(Deposit(fifties, twenties, tens));

    private static void CheckWholeDomain(Operation operation)
    {
        var failures = new List<string>();
        var checkedCases = 0;

        foreach (var jammed in JamOptions)
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        {
            checkedCases++;
            if (Check(Account(balance, withdrawn), Notes(f, t, d), jammed, operation) is { } problem)
            {
                failures.Add($"solde {balance}, retiré {withdrawn}, stock 50×{f} 20×{t} 10×{d}, " +
                             $"bloquée {jammed?.ToString() ?? "aucune"} : {problem}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{operation} : {failures.Count} écart(s) avec la spec sur {checkedCases} cas. Premiers :\n" +
            string.Join("\n", failures.Take(5)));
    }

    private static string? Check(AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var (expected, expectedStock) = ReferenceModel.Expected(account, stock, jammed, operation);
        var dispenser = new SpyDispenser(stock, jammed);

        AtmResult actual;
        try
        {
            actual = new CashMachine(dispenser).Execute(account, operation);
        }
        catch (Exception e)
        {
            return $"exception {e.GetType().Name} : {e.Message}";
        }

        if (actual != expected) return $"attendu {Show(expected)}, obtenu {Show(actual)} ; appels au module : {string.Join(", ", dispenser.Calls.Select(Show))}";
        if (dispenser.Stock != expectedStock) return $"stock attendu {expectedStock}, obtenu {dispenser.Stock}";
        if (dispenser.Calls.Any(c => !c.Result)) return "un appel au module a échoué : " + string.Join(", ", dispenser.Calls);
        if (dispenser.DispenseCalls.Any(c => c.Notes.Total > MoneyDispenser.MaxNotesPerPass))
            return "un passage compte plus de 4 billets";

        return null;
    }

    private static string Show(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";

    private static string Show(AtmResult r) =>
        $"{r.Status} [{Show(r.Notes)}, frais {r.Fee}, solde {r.Account.Balance}, retiré {r.Account.WithdrawnToday}]";

    private static string Show(DispenserCall c) => $"{c.Method}({Show(c.Notes)})={c.Result}";
}
