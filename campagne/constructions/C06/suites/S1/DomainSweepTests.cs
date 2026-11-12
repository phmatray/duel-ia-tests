namespace Atm.Tests;

/// <summary>Balaye le domaine de la spécification et compare à un oracle écrit indépendamment.</summary>
public class DomainSweepTests
{
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
        {
            yield return new NoteCounts(f, t, n);
        }
    }

    private static AtmResult ExpectedWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var refused = (AtmStatus s) => new AtmResult(s, NoteCounts.None, 0, account);
        if (amount <= 0 || amount % 10 != 0) return refused(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return refused(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return refused(AtmStatus.InsufficientFunds);

        var available = new NoteCounts(
            jammed == Denomination.Fifty ? 0 : stock.Fifties,
            jammed == Denomination.Twenty ? 0 : stock.Twenties,
            jammed == Denomination.Ten ? 0 : stock.Tens);
        var candidates = Stocks()
            .Where(c => c.Fifties <= available.Fifties && c.Twenties <= available.Twenties && c.Tens <= available.Tens)
            .Where(c => c.Value == amount)
            .ToList();
        if (candidates.Count == 0) return refused(AtmStatus.CannotDispense);

        var min = candidates.Min(c => c.Total);
        var best = Assert.Single(candidates, c => c.Total == min);

        return new AtmResult(AtmStatus.Ok, best, fee, new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    private static void CheckWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var dispenser = new SpyDispenser(stock, jammed);
        var actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
        var expected = ExpectedWithdraw(account, stock, jammed, amount);

        Assert.Equal(expected, actual);
        Assert.Equal(stock - expected.Notes, dispenser.Stock);
        Assert.All(dispenser.Dispensed, pass => Assert.InRange(pass.Total, 1, 4));
    }

    [Fact]
    public void Withdraw_matches_oracle_for_every_stock_jam_and_amount()
    {
        foreach (var account in new[] { new AccountState(250, 0), new AccountState(105, 60), new AccountState(0, 0) })
        foreach (var stock in Stocks())
        foreach (var jammed in Jams)
        foreach (var amount in Amounts)
        {
            CheckWithdraw(account, stock, jammed, amount);
        }
    }

    [Fact]
    public void Withdraw_matches_oracle_for_every_account_and_amount()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in Amounts)
        {
            CheckWithdraw(new AccountState(balance, withdrawn), new NoteCounts(5, 5, 5), null, amount);
            CheckWithdraw(new AccountState(balance, withdrawn), new NoteCounts(1, 2, 3), Denomination.Twenty, amount);
        }
    }

    [Fact]
    public void Deposit_matches_oracle_over_the_domain()
    {
        var account = new AccountState(35, 70);
        foreach (var stock in Stocks())
        foreach (var jammed in Jams)
        for (var mask = 0; mask < 8; mask++)
        {
            var notes = new NoteCounts(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
            var dispenser = new SpyDispenser(stock, jammed);
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));

            var after = stock + notes;
            AtmStatus status =
                notes.Total == 0 ? AtmStatus.InvalidAmount
                : jammed is { } j && notes.Of(j) > 0 ? AtmStatus.CassetteJammed
                : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
                : AtmStatus.Ok;

            var ok = status == AtmStatus.Ok;
            Assert.Equal(new AtmResult(status, NoteCounts.None, 0, ok ? account with { Balance = 35 + notes.Value } : account), actual);
            Assert.Equal(ok ? after : stock, dispenser.Stock);
        }
    }
}
