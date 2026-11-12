namespace Atm.Tests;

/// <summary>
/// Balaye le domaine de la spec et compare la machine à un modèle de référence écrit
/// directement à partir des règles (contrôles ordonnés, frais, billets disponibles, combinaison minimale,
/// refus sans effet). Chaque test rapporte les premiers écarts trouvés.
/// </summary>
public class DomainSweepTests
{
    private static readonly int[] Amounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<int> Balances => Enumerable.Range(0, 51).Select(i => i * 5);

    private static IEnumerable<int> WithdrawnTodays => Enumerable.Range(0, 21).Select(i => i * 10);

    private static IEnumerable<NoteCounts> Stocks =>
        from f in Enumerable.Range(0, 6)
        from t in Enumerable.Range(0, 6)
        from n in Enumerable.Range(0, 6)
        select new NoteCounts(f, t, n);

    private static IEnumerable<NoteCounts> Deposits =>
        from f in Enumerable.Range(0, 2)
        from t in Enumerable.Range(0, 2)
        from n in Enumerable.Range(0, 2)
        select new NoteCounts(f, t, n);

    [Fact]
    public void Withdraw_every_account_and_amount_with_a_full_stock()
    {
        var cases =
            from b in Balances
            from w in WithdrawnTodays
            from a in Amounts
            from j in Jams
            select (new AccountState(b, w), new NoteCounts(5, 5, 5), j, (Operation)new Operation.Withdraw(a));

        Check(cases);
    }

    [Fact]
    public void Withdraw_every_stock_jam_and_amount()
    {
        var cases =
            from s in Stocks
            from j in Jams
            from a in Amounts
            from acc in new[] { new AccountState(250, 0), new AccountState(100, 50), new AccountState(115, 90) }
            select (acc, s, j, (Operation)new Operation.Withdraw(a));

        Check(cases);
    }

    [Fact]
    public void Deposit_every_stock_jam_and_deposit()
    {
        var cases =
            from s in Stocks
            from j in Jams
            from d in Deposits
            from acc in new[] { new AccountState(0, 0), new AccountState(135, 70), new AccountState(250, 200) }
            select (acc, s, j, (Operation)new Operation.Deposit(d));

        Check(cases);
    }

    private static void Check(IEnumerable<(AccountState Account, NoteCounts Stock, Denomination? Jammed, Operation Operation)> cases)
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var (account, stock, jammed, operation) in cases)
        {
            count++;
            var dispenser = new MoneyDispenser(stock, jammed);
            var actual = new CashMachine(dispenser).Execute(account, operation);
            var (expected, expectedStock) = Reference(account, stock, jammed, operation);

            if (actual != expected || dispenser.Stock != expectedStock)
            {
                failures.Add(
                    $"solde {account.Balance}, retiré {account.WithdrawnToday}, stock {Show(stock)}, " +
                    $"bloquée {jammed?.ToString() ?? "aucune"}, {operation}\n" +
                    $"    attendu {Show(expected)}, stock {Show(expectedStock)}\n" +
                    $"    obtenu  {Show(actual)}, stock {Show(dispenser.Stock)}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} écart(s) sur {count} cas. Premiers :\n" + string.Join("\n", failures.Take(10)));
    }

    private static string Show(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";

    private static string Show(AtmResult r) =>
        $"{r.Status}, billets {Show(r.Notes)}, frais {r.Fee}, solde {r.Account.Balance}, retiré {r.Account.WithdrawnToday}";

    /// <summary>Le modèle de référence, transcription de la spec.</summary>
    private static (AtmResult Result, NoteCounts StockAfter) Reference(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus status) => (new AtmResult(status, NoteCounts.None, 0, account), stock);

        switch (operation)
        {
            case Operation.Withdraw { Amount: var amount }:
            {
                if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
                if (account.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
                var fee = amount > 100 ? 5 : 0;
                if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds);

                var available = new NoteCounts(
                    jammed == Denomination.Fifty ? 0 : stock.Fifties,
                    jammed == Denomination.Twenty ? 0 : stock.Twenties,
                    jammed == Denomination.Ten ? 0 : stock.Tens);
                var best = (
                    from f in Enumerable.Range(0, available.Fifties + 1)
                    from t in Enumerable.Range(0, available.Twenties + 1)
                    from n in Enumerable.Range(0, available.Tens + 1)
                    let c = new NoteCounts(f, t, n)
                    where c.Value == amount
                    orderby c.Total
                    select (NoteCounts?)c).FirstOrDefault();
                if (best is not { } notes) return Refuse(AtmStatus.CannotDispense);

                return (new AtmResult(AtmStatus.Ok, notes, fee,
                    new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount)), stock - notes);
            }

            case Operation.Deposit { Notes: var notes }:
            {
                if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
                if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
                var after = stock + notes;
                if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);

                return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0,
                    account with { Balance = account.Balance + notes.Value }), after);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}
