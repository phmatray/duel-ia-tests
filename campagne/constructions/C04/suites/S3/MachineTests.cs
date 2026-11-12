namespace Atm.Tests;

public class MachineTests
{
    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> AllStocks() =>
        from f in Enumerable.Range(0, 6)
        from t in Enumerable.Range(0, 6)
        from n in Enumerable.Range(0, 6)
        select new NoteCounts(f, t, n);

    [Fact]
    public void NullOperation_Throws()
    {
        var machine = new CashMachine(new MoneyDispenser(NoteCounts.None));

        Assert.Throws<ArgumentNullException>(() => machine.Execute(new(0, 0), null!));
    }

    [Fact]
    public void UnknownOperation_Throws()
    {
        var machine = new CashMachine(new MoneyDispenser(NoteCounts.None));

        Assert.Throws<ArgumentException>(() => machine.Execute(new(0, 0), new UnknownOperation()));
    }

    [Fact]
    public void Machine_IsStatelessBetweenCalls()
    {
        var dispenser = new MoneyDispenser(new(5, 5, 5));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new(250, 0), new Operation.Withdraw(100));
        var second = machine.Execute(new(250, 0), new Operation.Withdraw(100));

        Assert.Equal(first, second);
        Assert.Equal(new NoteCounts(1, 5, 5), dispenser.Stock);
    }

    /// <summary>
    /// Parcourt tous les stocks, cassettes bloquées et montants du domaine, et compare le retrait à un oracle
    /// qui énumère toutes les combinaisons.
    /// </summary>
    [Fact]
    public void Withdraw_MatchesOracle_OverWholeStockDomain()
    {
        int[] amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var amount in amounts)
        {
            var account = new AccountState(250, 0);
            var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
            var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

            var expected = Oracle(account, stock, jammed, amount);
            Assert.Equal(expected, result);
            Assert.Equal(stock - result.Notes, dispenser.Stock);
            Assert.All(dispenser.DispenseCalls, pass => Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass));
        }
    }

    [Fact]
    public void Withdraw_RespectsAccountChecks_OverAccountDomain()
    {
        var stock = new NoteCounts(5, 5, 5);
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        for (var amount = 10; amount <= 220; amount += 10)
        {
            var account = new AccountState(balance, withdrawn);
            var result = new CashMachine(new MoneyDispenser(stock)).Execute(account, new Operation.Withdraw(amount));

            Assert.Equal(Oracle(account, stock, null, amount), result);
        }
    }

    [Fact]
    public void Deposit_MatchesOracle_OverWholeDomain()
    {
        var deposits =
            from f in Enumerable.Range(0, 2)
            from t in Enumerable.Range(0, 2)
            from n in Enumerable.Range(0, 2)
            select new NoteCounts(f, t, n);

        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var notes in deposits)
        {
            var account = new AccountState(100, 50);
            var dispenser = new MoneyDispenser(stock, jammed);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));

            AtmStatus status;
            if (notes.Total == 0) status = AtmStatus.InvalidAmount;
            else if (jammed is { } j && notes.Of(j) > 0) status = AtmStatus.CassetteJammed;
            else if ((stock + notes) is var after && (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5)) status = AtmStatus.CassetteFull;
            else status = AtmStatus.Ok;

            var ok = status == AtmStatus.Ok;
            Assert.Equal(new AtmResult(status, NoteCounts.None, 0, ok ? new(100 + notes.Value, 50) : account), result);
            Assert.Equal(ok ? stock + notes : stock, dispenser.Stock);
        }
    }

    private static AtmResult Oracle(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        AtmResult Refused(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return Refused(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return Refused(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return Refused(AtmStatus.InsufficientFunds);

        var f = jammed == Denomination.Fifty ? 0 : stock.Fifties;
        var t = jammed == Denomination.Twenty ? 0 : stock.Twenties;
        var n = jammed == Denomination.Ten ? 0 : stock.Tens;
        var candidates = (
            from a in Enumerable.Range(0, f + 1)
            from b in Enumerable.Range(0, t + 1)
            from c in Enumerable.Range(0, n + 1)
            let notes = new NoteCounts(a, b, c)
            where notes.Value == amount
            select notes).ToList();

        if (candidates.Count == 0) return Refused(AtmStatus.CannotDispense);

        var fewest = candidates.Min(x => x.Total);
        var best = Assert.Single(candidates, x => x.Total == fewest);

        return new AtmResult(AtmStatus.Ok, best, fee, new(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }
}
