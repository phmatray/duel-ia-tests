using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Parcourt le domaine de la spec et compare la machine à un oracle écrit directement à partir des règles
/// (contrôles dans l'ordre, frais, billets disponibles, combinaison au moins de billets, refus sans effet).
/// </summary>
public class DomainOracleTests
{
    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly int[] Balances = [.. Enumerable.Range(0, 51).Select(i => i * 5)];

    private static readonly int[] WithdrawnTodays = [.. Enumerable.Range(0, 21).Select(i => i * 10)];

    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
        {
            yield return Notes(f, t, n);
        }
    }

    private static IEnumerable<NoteCounts> DepositsInDomain()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var n = 0; n <= 1; n++)
        {
            yield return Notes(f, t, n);
        }
    }

    public static TheoryData<int> AmountData() => new(Amounts);

    public static TheoryData<int, int, int> DepositData()
    {
        var data = new TheoryData<int, int, int>();
        foreach (var d in DepositsInDomain())
        {
            data.Add(d.Fifties, d.Twenties, d.Tens);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AmountData))]
    public void Retrait_conforme_a_la_spec_sur_tout_le_domaine(int amount)
    {
        var failures = new List<string>();
        var kinds = new Dictionary<string, int>();
        var checkedCases = 0;

        foreach (var stock in Stocks())
        foreach (var jammed in Jams)
        foreach (var withdrawn in WithdrawnTodays)
        foreach (var balance in Balances)
        {
            var account = Account(balance, withdrawn);
            var (expected, expectedStock) = ExpectedWithdraw(account, amount, stock, jammed);
            var (machine, dispenser) = Spied(stock, jammed);

            var actual = machine.Execute(account, Withdraw(amount));
            checkedCases++;

            var passesOk = dispenser.Dispensed.All(p => p.Total is >= 1 and <= MoneyDispenser.MaxNotesPerPass);
            if (actual != expected || dispenser.Stock != expectedStock || !passesOk || dispenser.Loaded.Count > 0)
            {
                kinds[Kind(expected, actual)] = kinds.GetValueOrDefault(Kind(expected, actual)) + 1;
                failures.Add(
                    $"solde {balance}, retiré {withdrawn}, stock {Show(stock)}, bloquée {jammed?.ToString() ?? "aucune"} : " +
                    $"attendu {Show(expected)} / stock {Show(expectedStock)}, obtenu {Show(actual)} / stock {Show(dispenser.Stock)}");
            }
        }

        Assert.True(failures.Count == 0,
            $"Retrait {amount} : {failures.Count} écart(s) sur {checkedCases} cas.\n" +
            $"Par type : {string.Join(" ; ", kinds.Select(k => $"{k.Key} ×{k.Value}"))}\nExemples :\n" +
            string.Join("\n", failures.Take(5)));
    }

    [Theory]
    [MemberData(nameof(DepositData))]
    public void Depot_conforme_a_la_spec_sur_tout_le_domaine(int fifties, int twenties, int tens)
    {
        var deposit = Notes(fifties, twenties, tens);
        var failures = new List<string>();
        var kinds = new Dictionary<string, int>();
        var checkedCases = 0;

        foreach (var stock in Stocks())
        foreach (var jammed in Jams)
        foreach (var withdrawn in WithdrawnTodays)
        foreach (var balance in Balances)
        {
            var account = Account(balance, withdrawn);
            var (expected, expectedStock) = ExpectedDeposit(account, deposit, stock, jammed);
            var (machine, dispenser) = Spied(stock, jammed);

            var actual = machine.Execute(account, Deposit(deposit));
            checkedCases++;

            if (actual != expected || dispenser.Stock != expectedStock || dispenser.Dispensed.Count > 0)
            {
                kinds[Kind(expected, actual)] = kinds.GetValueOrDefault(Kind(expected, actual)) + 1;
                failures.Add(
                    $"solde {balance}, retiré {withdrawn}, stock {Show(stock)}, bloquée {jammed?.ToString() ?? "aucune"} : " +
                    $"attendu {Show(expected)} / stock {Show(expectedStock)}, obtenu {Show(actual)} / stock {Show(dispenser.Stock)}");
            }
        }

        Assert.True(failures.Count == 0,
            $"Dépôt {Show(deposit)} : {failures.Count} écart(s) sur {checkedCases} cas.\n" +
            $"Par type : {string.Join(" ; ", kinds.Select(k => $"{k.Key} ×{k.Value}"))}\nExemples :\n" +
            string.Join("\n", failures.Take(5)));
    }

    // ---------- L'oracle ----------

    private static (AtmResult, NoteCounts) ExpectedWithdraw(AccountState account, int amount, NoteCounts stock, Denomination? jammed)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus status) => (new AtmResult(status, NoteCounts.None, 0, account), stock);

        if (amount <= 0 || amount % 10 != 0)
        {
            return Refuse(AtmStatus.InvalidAmount);
        }

        if (account.WithdrawnToday + amount > 200)
        {
            return Refuse(AtmStatus.DailyLimitExceeded);
        }

        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance)
        {
            return Refuse(AtmStatus.InsufficientFunds);
        }

        var available = Notes(
            jammed == Denomination.Fifty ? 0 : stock.Fifties,
            jammed == Denomination.Twenty ? 0 : stock.Twenties,
            jammed == Denomination.Ten ? 0 : stock.Tens);

        var candidates = new List<NoteCounts>();
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var n = 0; n <= available.Tens; n++)
        {
            if (f * 50 + t * 20 + n * 10 == amount)
            {
                candidates.Add(Notes(f, t, n));
            }
        }

        if (candidates.Count == 0)
        {
            return Refuse(AtmStatus.CannotDispense);
        }

        var fewest = candidates.Min(c => c.Total);
        var best = candidates.Where(c => c.Total == fewest).ToList();
        Assert.Single(best); // la spec affirme l'unicité : on le vérifie au passage

        var after = Account(account.Balance - amount - fee, account.WithdrawnToday + amount);
        return (new AtmResult(AtmStatus.Ok, best[0], fee, after), stock - best[0]);
    }

    private static (AtmResult, NoteCounts) ExpectedDeposit(AccountState account, NoteCounts notes, NoteCounts stock, Denomination? jammed)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus status) => (new AtmResult(status, NoteCounts.None, 0, account), stock);

        if (notes.Total == 0)
        {
            return Refuse(AtmStatus.InvalidAmount);
        }

        if (jammed is { } j && notes.Of(j) > 0)
        {
            return Refuse(AtmStatus.CassetteJammed);
        }

        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5)
        {
            return Refuse(AtmStatus.CassetteFull);
        }

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }), after);
    }

    private static string Kind(AtmResult expected, AtmResult actual) =>
        expected.Status == actual.Status
            ? $"{expected.Status} avec un contenu différent"
            : $"attendu {expected.Status}, obtenu {actual.Status}";

    private static string Show(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";

    private static string Show(AtmResult r) =>
        $"{r.Status} [{Show(r.Notes)}, frais {r.Fee}, solde {r.Account.Balance}, retiré {r.Account.WithdrawnToday}]";
}
