namespace Atm.Tests;

/// <summary>Parcourt le domaine des retraits et dépôts et compare à un oracle écrit indépendamment.</summary>
public class DomainPropertyTests
{
    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly AccountState[] Accounts = [new(0, 0), new(55, 150), new(105, 0), new(115, 90), new(250, 0), new(250, 200)];

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return new NoteCounts(f, t, d);
    }

    private static IEnumerable<int> Amounts() => new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));

    private static NoteCounts Available(NoteCounts stock, Denomination? jammed) => new(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    private static NoteCounts? Oracle(int amount, NoteCounts available)
    {
        var all = new List<NoteCounts>();
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var c = new NoteCounts(f, t, d);
            if (c.Value == amount) all.Add(c);
        }

        if (all.Count == 0) return null;
        var min = all.Min(c => c.Total);
        return Assert.Single(all, c => c.Total == min);
    }

    [Fact]
    public void Withdraw_MatchesOracleOverDomain()
    {
        foreach (var account in Accounts)
        foreach (var stock in Stocks())
        foreach (var jammed in JamOptions)
        foreach (var amount in Amounts())
        {
            var dispenser = new MoneyDispenser(stock, jammed);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

            var fee = amount > 100 ? 5 : 0;
            AtmStatus expected;
            NoteCounts? combination = null;
            if (amount <= 0 || amount % 10 != 0) expected = AtmStatus.InvalidAmount;
            else if (account.WithdrawnToday + amount > 200) expected = AtmStatus.DailyLimitExceeded;
            else if (amount + fee > account.Balance) expected = AtmStatus.InsufficientFunds;
            else if ((combination = Oracle(amount, Available(stock, jammed))) is null) expected = AtmStatus.CannotDispense;
            else expected = AtmStatus.Ok;

            if (expected == AtmStatus.Ok)
            {
                var notes = combination!.Value;
                Assert.Equal(
                    new AtmResult(AtmStatus.Ok, notes, fee, new(account.Balance - amount - fee, account.WithdrawnToday + amount)),
                    result);
                Assert.Equal(stock - notes, dispenser.Stock);
            }
            else
            {
                Assert.Equal(new AtmResult(expected, default, 0, account), result);
                Assert.Equal(stock, dispenser.Stock);
            }
        }
    }

    [Fact]
    public void Deposit_MatchesOracleOverDomain()
    {
        var account = new AccountState(100, 50);
        foreach (var stock in Stocks())
        foreach (var jammed in JamOptions)
        for (var mask = 0; mask < 8; mask++)
        {
            var deposit = new NoteCounts(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
            var dispenser = new MoneyDispenser(stock, jammed);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(deposit));

            var after = stock + deposit;
            AtmStatus expected =
                deposit.Total == 0 ? AtmStatus.InvalidAmount
                : jammed is { } j && deposit.Of(j) > 0 ? AtmStatus.CassetteJammed
                : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
                : AtmStatus.Ok;

            if (expected == AtmStatus.Ok)
            {
                Assert.Equal(new AtmResult(AtmStatus.Ok, default, 0, account with { Balance = 100 + deposit.Value }), result);
                Assert.Equal(after, dispenser.Stock);
            }
            else
            {
                Assert.Equal(new AtmResult(expected, default, 0, account), result);
                Assert.Equal(stock, dispenser.Stock);
            }
        }
    }
}
