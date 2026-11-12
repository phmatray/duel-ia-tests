using static Atm.Tests.Support;

namespace Atm.Tests;

/// <summary>
/// Balayage du domaine de la spec : chaque cas est comparé à un modèle de référence écrit
/// directement à partir des règles (contrôles dans l'ordre, frais, combinaison au plus petit
/// nombre de billets, refus sans effet). Le stock du module est vérifié après chaque opération.
/// </summary>
public class DomainSweepTests
{
    private static readonly int[] Amounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly Denomination?[] JammedValues =
        [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return N(f, t, d);
    }

    private static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return N(f, t, d);
    }

    private static IEnumerable<AccountState> AllAccounts()
    {
        for (var b = 0; b <= 250; b += 5)
        for (var w = 0; w <= 200; w += 10)
            yield return new AccountState(b, w);
    }

    // Comptes choisis autour des seuils du plafond, du solde et des frais.
    private static readonly AccountState[] BoundaryAccounts =
    [
        new(250, 0), new(250, 100), new(250, 150), new(250, 190), new(250, 200),
        new(0, 0), new(50, 0), new(100, 0), new(105, 0), new(110, 0), new(115, 0), new(205, 0), new(200, 0),
    ];

    // ---------- Modèle de référence ----------

    private static (AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account, NoteCounts StockAfter) Expected(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var refused = (AtmStatus status) => (status, NoteCounts.None, 0, account, stock);

        switch (operation)
        {
            case Operation.Withdraw { Amount: var amount }:
            {
                if (amount <= 0 || amount % 10 != 0) return refused(AtmStatus.InvalidAmount);
                if (account.WithdrawnToday + amount > 200) return refused(AtmStatus.DailyLimitExceeded);
                var fee = amount > 100 ? 5 : 0;
                if (amount + fee > account.Balance) return refused(AtmStatus.InsufficientFunds);

                var available = N(
                    jammed == Denomination.Fifty ? 0 : stock.Fifties,
                    jammed == Denomination.Twenty ? 0 : stock.Twenties,
                    jammed == Denomination.Ten ? 0 : stock.Tens);

                NoteCounts? best = null;
                for (var f = 0; f <= available.Fifties; f++)
                for (var t = 0; t <= available.Twenties; t++)
                for (var d = 0; d <= available.Tens; d++)
                {
                    var candidate = N(f, t, d);
                    if (candidate.Value == amount && (best is null || candidate.Total < best.Value.Total))
                        best = candidate;
                }

                if (best is not { } notes) return refused(AtmStatus.CannotDispense);

                return (AtmStatus.Ok, notes, fee,
                    new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount), stock - notes);
            }

            case Operation.Deposit { Notes: var notes }:
            {
                if (notes.Total == 0) return refused(AtmStatus.InvalidAmount);
                if (jammed is { } j && notes.Of(j) > 0) return refused(AtmStatus.CassetteJammed);
                var after = stock + notes;
                if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return refused(AtmStatus.CassetteFull);

                return (AtmStatus.Ok, NoteCounts.None, 0,
                    new AccountState(account.Balance + notes.Value, account.WithdrawnToday), after);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static void Sweep(IEnumerable<(AccountState Account, NoteCounts Stock, Denomination? Jammed, Operation Operation)> cases)
    {
        var failures = new List<string>();
        var count = 0;

        foreach (var (account, stock, jammed, operation) in cases)
        {
            count++;
            var expected = Expected(account, stock, jammed, operation);
            var (machine, dispenser) = NewAtm(stock, jammed);

            string actualText;
            try
            {
                var result = machine.Execute(account, operation);
                var actual = (result.Status, result.Notes, result.Fee, result.Account, dispenser.Stock);
                if (actual == expected) continue;
                actualText = actual.ToString();
            }
            catch (Exception e)
            {
                actualText = $"exception {e.GetType().Name}: {e.Message}";
            }

            failures.Add(
                $"{operation} | compte {account} | stock {stock} | bloquée {jammed?.ToString() ?? "aucune"}\n" +
                $"    attendu (Status, Notes, Fee, Account, Stock après) = {expected}\n" +
                $"    obtenu  = {actualText}");
        }

        Assert.True(count > 0);
        Assert.True(failures.Count == 0,
            $"{failures.Count} écart(s) sur {count} cas. Premiers :\n" + string.Join("\n", failures.Take(15)));
    }

    // ---------- Balayages ----------

    [Fact]
    public void Retrait_tous_les_stocks_cassettes_bloquees_et_montants() =>
        Sweep(
            from account in BoundaryAccounts
            from stock in AllStocks()
            from jammed in JammedValues
            from amount in Amounts
            select (account, stock, jammed, Withdraw(amount)));

    [Fact]
    public void Retrait_tous_les_comptes_et_montants() =>
        Sweep(
            from account in AllAccounts()
            from setup in new (NoteCounts Stock, Denomination? Jammed)[]
            {
                (Full, null), (N(0, 0, 0), null), (N(1, 3, 0), null), (Full, Denomination.Fifty), (N(2, 5, 5), Denomination.Ten),
            }
            from amount in Amounts
            select (account, setup.Stock, setup.Jammed, Withdraw(amount)));

    [Fact]
    public void Depot_tous_les_stocks_cassettes_bloquees_et_depots() =>
        Sweep(
            from account in new AccountState[] { new(0, 0), new(10, 30), new(250, 200), new(125, 100) }
            from stock in AllStocks()
            from jammed in JammedValues
            from notes in AllDeposits()
            select (account, stock, jammed, Deposit(notes)));
}
