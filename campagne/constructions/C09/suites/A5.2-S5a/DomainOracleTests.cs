namespace Atm.Tests;

/// <summary>
/// Compare la machine à un oracle écrit directement d'après la spec, sur de larges tranches du domaine :
/// tous les stocks x toutes les cassettes bloquées x tous les montants, et tous les comptes x tous les montants.
/// </summary>
public class DomainOracleTests
{
    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];
    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return new NoteCounts(f, t, d);
    }

    private static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return new NoteCounts(f, t, d);
    }

    // ---- Oracle ----

    private static (AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account, NoteCounts Stock) ExpectedWithdraw(
        AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var refused = (AtmStatus s) => (s, NoteCounts.None, 0, account, stock);

        if (amount <= 0 || amount % 10 != 0) return refused(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return refused(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return refused(AtmStatus.InsufficientFunds);

        var available = new NoteCounts(
            jammed == Denomination.Fifty ? 0 : stock.Fifties,
            jammed == Denomination.Twenty ? 0 : stock.Twenties,
            jammed == Denomination.Ten ? 0 : stock.Tens);

        NoteCounts? best = null;
        var bestCount = 0;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var c = new NoteCounts(f, t, d);
            if (c.Value != amount) continue;
            if (best is null || c.Total < best.Value.Total)
            {
                best = c;
                bestCount = 1;
            }
            else if (c.Total == best.Value.Total)
            {
                bestCount++;
            }
        }

        if (best is not { } combination) return refused(AtmStatus.CannotDispense);
        Assert.Equal(1, bestCount); // la spec affirme l'unicité

        return (AtmStatus.Ok, combination, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount), stock - combination);
    }

    private static (AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account, NoteCounts Stock) ExpectedDeposit(
        AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        var refused = (AtmStatus s) => (s, NoteCounts.None, 0, account, stock);

        if (notes.Total == 0) return refused(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return refused(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return refused(AtmStatus.CassetteFull);

        return (AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }, after);
    }

    private static string Check<T>(string context, T expected, T actual) =>
        EqualityComparer<T>.Default.Equals(expected, actual) ? "" : $"{context}: attendu {expected}, obtenu {actual}";

    private static void AssertNoMismatch(List<string> mismatches) =>
        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} écart(s) avec la spec, par exemple :\n" + string.Join("\n", mismatches.Take(15)));

    // ---- Tests ----

    [Fact]
    public void Retrait_tous_stocks_toutes_cassettes_tous_montants()
    {
        // Comptes choisis pour exercer chaque contrôle : large, limite du jour, solde serré.
        AccountState[] accounts = [new(250, 0), new(250, 90), new(250, 150), new(115, 0), new(110, 0), new(0, 0), new(250, 200)];
        var mismatches = new List<string>();

        foreach (var account in accounts)
        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var amount in Amounts)
        {
            var expected = ExpectedWithdraw(account, stock, jammed, amount);
            var dispenser = new MoneyDispenser(stock, jammed);
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
            var ctx = $"{account} stock {stock} bloquée {jammed?.ToString() ?? "aucune"} retrait {amount}";
            var diff = Check(ctx, (expected.Status, expected.Notes, expected.Fee, expected.Account, expected.Stock),
                (actual.Status, actual.Notes, actual.Fee, actual.Account, dispenser.Stock));
            if (diff != "") mismatches.Add(diff);
        }

        AssertNoMismatch(mismatches);
    }

    [Fact]
    public void Retrait_tous_comptes_tous_montants()
    {
        var mismatches = new List<string>();

        foreach (var stock in new[] { new NoteCounts(5, 5, 5), new NoteCounts(1, 5, 5) })
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in Amounts)
        {
            var account = new AccountState(balance, withdrawn);
            var expected = ExpectedWithdraw(account, stock, null, amount);
            var dispenser = new MoneyDispenser(stock);
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
            var ctx = $"{account} stock {stock} retrait {amount}";
            var diff = Check(ctx, (expected.Status, expected.Notes, expected.Fee, expected.Account, expected.Stock),
                (actual.Status, actual.Notes, actual.Fee, actual.Account, dispenser.Stock));
            if (diff != "") mismatches.Add(diff);
        }

        AssertNoMismatch(mismatches);
    }

    [Fact]
    public void Depot_tous_stocks_toutes_cassettes_tous_depots()
    {
        AccountState[] accounts = [new(0, 0), new(170, 200), new(250, 30)];
        var mismatches = new List<string>();

        foreach (var account in accounts)
        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var notes in AllDeposits())
        {
            var expected = ExpectedDeposit(account, stock, jammed, notes);
            var dispenser = new MoneyDispenser(stock, jammed);
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));
            var ctx = $"{account} stock {stock} bloquée {jammed?.ToString() ?? "aucune"} dépôt {notes}";
            var diff = Check(ctx, (expected.Status, expected.Notes, expected.Fee, expected.Account, expected.Stock),
                (actual.Status, actual.Notes, actual.Fee, actual.Account, dispenser.Stock));
            if (diff != "") mismatches.Add(diff);
        }

        AssertNoMismatch(mismatches);
    }
}
