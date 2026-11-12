namespace Atm.Tests;

/// <summary>Parcourt une large part du domaine et compare à un modèle de référence écrit indépendamment.</summary>
public class DomainSweepTests
{
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
            for (var t = 0; t <= 5; t++)
                for (var c = 0; c <= 5; c++)
                    yield return new NoteCounts(f, t, c);
    }

    private static AtmResult ExpectedWithdraw(AccountState a, NoteCounts stock, Denomination? jam, int amount)
    {
        AtmResult Refused(AtmStatus s) => new(s, NoteCounts.None, 0, a);

        if (amount <= 0 || amount % 10 != 0) return Refused(AtmStatus.InvalidAmount);
        if (a.WithdrawnToday + amount > 200) return Refused(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > a.Balance) return Refused(AtmStatus.InsufficientFunds);

        var usable = new NoteCounts(
            jam == Denomination.Fifty ? 0 : stock.Fifties,
            jam == Denomination.Twenty ? 0 : stock.Twenties,
            jam == Denomination.Ten ? 0 : stock.Tens);

        var candidates = (
            from f in Enumerable.Range(0, usable.Fifties + 1)
            from t in Enumerable.Range(0, usable.Twenties + 1)
            from c in Enumerable.Range(0, usable.Tens + 1)
            let n = new NoteCounts(f, t, c)
            where n.Value == amount
            orderby n.Total
            select n).ToList();

        if (candidates.Count == 0) return Refused(AtmStatus.CannotDispense);
        if (candidates.Count > 1) Assert.NotEqual(candidates[0].Total, candidates[1].Total); // unicité annoncée

        return new AtmResult(AtmStatus.Ok, candidates[0], fee,
            new AccountState(a.Balance - amount - fee, a.WithdrawnToday + amount));
    }

    [Fact]
    public void Withdraw_matches_reference_model()
    {
        int[] amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];
        AccountState[] accounts = [new(250, 0), new(115, 0), new(100, 50), new(0, 0), new(250, 190), new(205, 0)];

        foreach (var stock in AllStocks())
        foreach (var jam in Jams)
        foreach (var amount in amounts)
        foreach (var account in accounts)
        {
            var dispenser = new RecordingDispenser(stock, jam);
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
            var expected = ExpectedWithdraw(account, stock, jam, amount);

            Assert.Equal(expected, actual);
            Assert.Equal(stock - actual.Notes, dispenser.Stock);
            Assert.All(dispenser.DispenseCalls, p => Assert.InRange(p.Total, 1, 4));
        }
    }

    [Fact]
    public void Deposit_matches_reference_model()
    {
        var account = new AccountState(55, 70);

        foreach (var stock in AllStocks())
        foreach (var jam in Jams)
        for (var mask = 0; mask < 8; mask++)
        {
            var notes = new NoteCounts(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
            var dispenser = new MoneyDispenser(stock, jam);
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));

            var after = stock + notes;
            var expectedStatus =
                notes.Total == 0 ? AtmStatus.InvalidAmount
                : jam is { } j && notes.Of(j) > 0 ? AtmStatus.CassetteJammed
                : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
                : AtmStatus.Ok;

            Assert.Equal(expectedStatus, actual.Status);
            Assert.Equal(NoteCounts.None, actual.Notes);
            Assert.Equal(0, actual.Fee);
            if (expectedStatus == AtmStatus.Ok)
            {
                Assert.Equal(account with { Balance = account.Balance + notes.Value }, actual.Account);
                Assert.Equal(after, dispenser.Stock);
            }
            else
            {
                Assert.Equal(account, actual.Account);
                Assert.Equal(stock, dispenser.Stock);
            }
        }
    }
}
