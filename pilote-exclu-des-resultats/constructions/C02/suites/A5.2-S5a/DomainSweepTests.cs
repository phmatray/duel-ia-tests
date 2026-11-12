using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Balayages du domaine défini par la spec, comparés à un oracle écrit directement d'après la spec.
/// Chaque test liste les premiers écarts trouvés.
/// </summary>
public class DomainSweepTests
{
    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];
    private static readonly int[] Balances = Enumerable.Range(0, 51).Select(i => i * 5).ToArray();
    private static readonly int[] Withdrawn = Enumerable.Range(0, 21).Select(i => i * 10).ToArray();

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
            yield return N(f, t, n);
    }

    private static NoteCounts Available(NoteCounts stock, Denomination? jam) => jam switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    /// <summary>Toutes les combinaisons exactes de billets disponibles ; garde celles au nombre minimal de billets.</summary>
    private static List<NoteCounts> FewestNotes(int amount, NoteCounts available)
    {
        var all = new List<NoteCounts>();
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var n = 0; n <= available.Tens; n++)
            if (50 * f + 20 * t + 10 * n == amount)
                all.Add(N(f, t, n));

        if (all.Count == 0)
        {
            return all;
        }

        var min = all.Min(c => c.Total);

        return all.Where(c => c.Total == min).ToList();
    }

    private static (AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account, NoteCounts Stock) OracleWithdraw(
        AccountState account, NoteCounts stock, Denomination? jam, int amount)
    {
        var refused = (AtmStatus s) => (s, NoteCounts.None, 0, account, stock);

        if (amount <= 0 || amount % 10 != 0) return refused(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return refused(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return refused(AtmStatus.InsufficientFunds);
        var best = FewestNotes(amount, Available(stock, jam));
        if (best.Count == 0) return refused(AtmStatus.CannotDispense);
        Assert.Single(best); // la spec affirme l'unicité

        var notes = best[0];

        return (AtmStatus.Ok, notes, fee, new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount), stock - notes);
    }

    private static (AtmStatus Status, AccountState Account, NoteCounts Stock) OracleDeposit(
        AccountState account, NoteCounts stock, Denomination? jam, NoteCounts notes)
    {
        if (notes.Total == 0) return (AtmStatus.InvalidAmount, account, stock);
        if (jam is { } j && notes.Of(j) > 0) return (AtmStatus.CassetteJammed, account, stock);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (AtmStatus.CassetteFull, account, stock);

        return (AtmStatus.Ok, account with { Balance = account.Balance + notes.Value }, after);
    }

    private static void CheckWithdraw(AccountState account, NoteCounts stock, Denomination? jam, int amount, List<string> failures)
    {
        var expected = OracleWithdraw(account, stock, jam, amount);
        var dispenser = new SpyDispenser(stock, jam);
        var result = new CashMachine(dispenser).Execute(account, W(amount));

        var passesOk = dispenser.DispenseCalls.All(p => p.Total is >= 1 and <= 4)
                       && Sum(dispenser.DispenseCalls) == expected.Notes
                       && dispenser.LoadCalls.Count == 0;

        if (result.Status != expected.Status || result.Notes != expected.Notes || result.Fee != expected.Fee
            || result.Account != expected.Account || dispenser.Stock != expected.Stock || !passesOk)
        {
            failures.Add($"compte={F(account)} stock={F(stock)} bloquée={jam?.ToString() ?? "aucune"} retrait {amount}: " +
                         $"attendu {expected.Status} {F(expected.Notes)} frais {expected.Fee} {F(expected.Account)}, " +
                         $"obtenu {result.Status} {F(result.Notes)} frais {result.Fee} {F(result.Account)} stock={F(dispenser.Stock)}" +
                         (passesOk ? "" : $" passages=[{string.Join("; ", dispenser.DispenseCalls.Select(F))}]"));
        }
    }

    private static string F(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";

    private static string F(AccountState a) => $"(solde {a.Balance}, retiré {a.WithdrawnToday})";

    private static void AssertNoFailures(List<string> failures) =>
        Assert.True(failures.Count == 0,
            $"{failures.Count} écart(s) avec la spec, par exemple :\n" + string.Join("\n", failures.Take(15)));

    [Fact]
    public void Withdraw_matches_spec_for_every_account_and_amount_with_full_stock()
    {
        var failures = new List<string>();
        foreach (var balance in Balances)
        foreach (var withdrawn in Withdrawn)
        foreach (var amount in Amounts)
            CheckWithdraw(Acc(balance, withdrawn), N(5, 5, 5), null, amount, failures);

        AssertNoFailures(failures);
    }

    [Fact]
    public void Withdraw_matches_spec_for_every_stock_jam_and_amount()
    {
        var failures = new List<string>();
        foreach (var stock in AllStocks())
        foreach (var jam in Jams)
        foreach (var amount in Amounts)
            CheckWithdraw(Acc(250, 0), stock, jam, amount, failures);

        AssertNoFailures(failures);
    }

    [Fact]
    public void Withdraw_matches_spec_on_a_sample_of_the_whole_domain()
    {
        var failures = new List<string>();
        var stocks = AllStocks().ToArray();
        var random = new Random(20261106);
        for (var i = 0; i < 50_000; i++)
        {
            var account = Acc(Balances[random.Next(Balances.Length)], Withdrawn[random.Next(Withdrawn.Length)]);
            CheckWithdraw(account, stocks[random.Next(stocks.Length)], Jams[random.Next(Jams.Length)],
                Amounts[random.Next(Amounts.Length)], failures);
        }

        AssertNoFailures(failures);
    }

    [Fact]
    public void Deposit_matches_spec_for_every_deposit_stock_and_jam()
    {
        var failures = new List<string>();
        var accounts = new[] { Acc(0, 0), Acc(10, 30), Acc(170, 200), Acc(250, 100) };
        foreach (var account in accounts)
        foreach (var stock in AllStocks())
        foreach (var jam in Jams)
        for (var mask = 0; mask < 8; mask++)
        {
            var notes = N(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
            var expected = OracleDeposit(account, stock, jam, notes);
            var dispenser = new SpyDispenser(stock, jam);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));

            var movedOk = expected.Status == AtmStatus.Ok
                ? Sum(dispenser.LoadCalls) == notes
                : dispenser.LoadCalls.Count == 0;

            if (result.Status != expected.Status || result.Notes != NoteCounts.None || result.Fee != 0
                || result.Account != expected.Account || dispenser.Stock != expected.Stock
                || dispenser.DispenseCalls.Count != 0 || !movedOk)
            {
                failures.Add($"compte={F(account)} stock={F(stock)} bloquée={jam?.ToString() ?? "aucune"} dépôt {F(notes)}: " +
                             $"attendu {expected.Status} {F(expected.Account)} stock={F(expected.Stock)}, " +
                             $"obtenu {result.Status} {F(result.Notes)} frais {result.Fee} {F(result.Account)} stock={F(dispenser.Stock)}");
            }
        }

        AssertNoFailures(failures);
    }
}
