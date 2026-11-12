namespace Atm.Tests;

/// <summary>
/// Parcourt de larges pans du domaine et compare la machine à un modèle de référence écrit directement
/// d'après la spécification (énumération brute de toutes les combinaisons).
/// </summary>
public class CashMachineDomainTests
{
    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

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

    private static (AtmResult Result, NoteCounts Stock) ExpectedWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        AtmResult Refused(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return (Refused(AtmStatus.InvalidAmount), stock);
        if (account.WithdrawnToday + amount > 200) return (Refused(AtmStatus.DailyLimitExceeded), stock);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return (Refused(AtmStatus.InsufficientFunds), stock);

        var avail = new NoteCounts(
            jammed == Denomination.Fifty ? 0 : stock.Fifties,
            jammed == Denomination.Twenty ? 0 : stock.Twenties,
            jammed == Denomination.Ten ? 0 : stock.Tens);

        var candidates = new List<NoteCounts>();
        for (var f = 0; f <= avail.Fifties; f++)
        for (var t = 0; t <= avail.Twenties; t++)
        for (var d = 0; d <= avail.Tens; d++)
        {
            var c = new NoteCounts(f, t, d);
            if (c.Value == amount) candidates.Add(c);
        }

        if (candidates.Count == 0) return (Refused(AtmStatus.CannotDispense), stock);

        var min = candidates.Min(c => c.Total);
        var best = Assert.Single(candidates, c => c.Total == min); // la spec garantit l'unicité

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
        return (new AtmResult(AtmStatus.Ok, best, fee, after), stock - best);
    }

    private static (AtmResult Result, NoteCounts Stock) ExpectedDeposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        AtmResult Refused(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (notes.Total == 0) return (Refused(AtmStatus.InvalidAmount), stock);
        if (jammed is { } j && notes.Of(j) > 0) return (Refused(AtmStatus.CassetteJammed), stock);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (Refused(AtmStatus.CassetteFull), stock);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }), after);
    }

    private static void CheckWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var recorder = new RecordingDispenser(new MoneyDispenser(stock, jammed));
        var result = new CashMachine(recorder).Execute(account, new Operation.Withdraw(amount));
        var (expected, expectedStock) = ExpectedWithdraw(account, stock, jammed, amount);

        var context = $"compte={account} stock={stock} bloquée={jammed} retrait={amount}";
        Assert.True(expected == result, $"{context} : attendu {expected}, obtenu {result}");
        Assert.True(expectedStock == recorder.Stock, $"{context} : stock attendu {expectedStock}, obtenu {recorder.Stock}");
        Assert.All(recorder.DispenseCalls, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Empty(recorder.LoadCalls);
    }

    [Fact]
    public void Withdraw_EveryStockJamAndAmount_MatchesReference()
    {
        foreach (var account in new[] { new AccountState(250, 0), new AccountState(105, 100), new AccountState(0, 0) })
        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var amount in Amounts)
            CheckWithdraw(account, stock, jammed, amount);
    }

    [Fact]
    public void Withdraw_EveryAccountAndAmount_MatchesReference()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in Amounts)
        {
            CheckWithdraw(new AccountState(balance, withdrawn), new NoteCounts(5, 5, 5), null, amount);
            CheckWithdraw(new AccountState(balance, withdrawn), new NoteCounts(1, 2, 0), Denomination.Twenty, amount);
        }
    }

    [Fact]
    public void Deposit_EveryStockJamAndDeposit_MatchesReference()
    {
        foreach (var account in new[] { new AccountState(0, 0), new AccountState(165, 200), new AccountState(250, 30) })
        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var notes in AllDeposits())
        {
            var recorder = new RecordingDispenser(new MoneyDispenser(stock, jammed));
            var result = new CashMachine(recorder).Execute(account, new Operation.Deposit(notes));
            var (expected, expectedStock) = ExpectedDeposit(account, stock, jammed, notes);

            var context = $"compte={account} stock={stock} bloquée={jammed} dépôt={notes}";
            Assert.True(expected == result, $"{context} : attendu {expected}, obtenu {result}");
            Assert.True(expectedStock == recorder.Stock, $"{context} : stock attendu {expectedStock}, obtenu {recorder.Stock}");
            Assert.Empty(recorder.DispenseCalls);
        }
    }
}
