namespace Atm.Tests;

/// <summary>Compare la machine à un oracle écrit directement depuis la spécification, sur tout le domaine du stock.</summary>
public sealed class ExhaustiveTests
{
    private static readonly Denomination?[] JamStates = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
            for (var t = 0; t <= 5; t++)
                for (var c = 0; c <= 5; c++)
                    yield return new NoteCounts(f, t, c);
    }

    private static IEnumerable<int> AllAmounts() =>
        new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));

    private static NoteCounts? Oracle(int amount, NoteCounts stock, Denomination? jammed)
    {
        var all = new List<NoteCounts>();
        for (var f = 0; f <= (jammed == Denomination.Fifty ? 0 : stock.Fifties); f++)
            for (var t = 0; t <= (jammed == Denomination.Twenty ? 0 : stock.Twenties); t++)
                for (var c = 0; c <= (jammed == Denomination.Ten ? 0 : stock.Tens); c++)
                {
                    var n = new NoteCounts(f, t, c);
                    if (n.Value == amount) all.Add(n);
                }

        if (all.Count == 0) return null;
        var min = all.Min(n => n.Total);
        var best = all.Where(n => n.Total == min).ToList();
        Assert.Single(best); // la spécification garantit l'unicité

        return best[0];
    }

    [Theory]
    [InlineData(250, 0)]
    [InlineData(250, 150)]
    [InlineData(120, 0)]
    [InlineData(105, 40)]
    [InlineData(0, 0)]
    public void Withdraw_MatchesOracle(int balance, int withdrawn)
    {
        foreach (var jammed in JamStates)
        foreach (var stock in AllStocks())
        foreach (var amount in AllAmounts())
        {
            var dispenser = new MoneyDispenser(stock, jammed);
            var account = new AccountState(balance, withdrawn);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

            var fee = amount > 100 ? 5 : 0;
            AtmStatus expected;
            NoteCounts? combination = null;
            if (amount <= 0 || amount % 10 != 0) expected = AtmStatus.InvalidAmount;
            else if (withdrawn + amount > 200) expected = AtmStatus.DailyLimitExceeded;
            else if (amount + fee > balance) expected = AtmStatus.InsufficientFunds;
            else if ((combination = Oracle(amount, stock, jammed)) is null) expected = AtmStatus.CannotDispense;
            else expected = AtmStatus.Ok;

            if (expected == AtmStatus.Ok)
            {
                var notes = combination!.Value;
                Assert.Equal(new AtmResult(AtmStatus.Ok, notes, fee, new(balance - amount - fee, withdrawn + amount)), result);
                Assert.Equal(stock - notes, dispenser.Stock);
            }
            else
            {
                Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
                Assert.Equal(stock, dispenser.Stock);
            }
        }
    }

    [Fact]
    public void Deposit_MatchesOracle()
    {
        foreach (var jammed in JamStates)
        foreach (var stock in AllStocks())
        for (var mask = 0; mask < 8; mask++)
        {
            var notes = new NoteCounts(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
            var dispenser = new MoneyDispenser(stock, jammed);
            var account = new AccountState(100, 50);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));

            var after = stock + notes;
            AtmStatus expected =
                notes.Total == 0 ? AtmStatus.InvalidAmount
                : jammed is { } j && notes.Of(j) > 0 ? AtmStatus.CassetteJammed
                : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
                : AtmStatus.Ok;

            if (expected == AtmStatus.Ok)
            {
                Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(100 + notes.Value, 50)), result);
                Assert.Equal(after, dispenser.Stock);
            }
            else
            {
                Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
                Assert.Equal(stock, dispenser.Stock);
            }
        }
    }
}
