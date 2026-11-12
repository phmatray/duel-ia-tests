using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Parcourt le domaine de la spec et compare la machine à un oracle écrit
/// directement à partir des règles (recherche exhaustive des combinaisons).
/// </summary>
public class DomainOracleTests
{
    private static readonly Denomination?[] JammedValues = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
        {
            yield return N(f, t, n);
        }
    }

    private static IEnumerable<NoteCounts> Deposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var n = 0; n <= 1; n++)
        {
            yield return N(f, t, n);
        }
    }

    /// <summary>Comptes choisis pour toucher chaque frontière (balance multiple de 5, retrait multiple de 10).</summary>
    public static TheoryData<int, int> Accounts() => new()
    {
        { 250, 0 }, { 0, 0 }, { 5, 0 }, { 105, 0 }, { 110, 0 }, { 115, 0 }, { 205, 0 },
        { 250, 150 }, { 250, 190 }, { 0, 200 }, { 145, 90 }, { 60, 40 }, { 55, 10 }, { 250, 200 },
    };

    [Theory]
    [MemberData(nameof(Accounts))]
    public void Retrait_conforme_a_l_oracle_sur_tout_le_domaine(int balance, int withdrawnToday)
    {
        var failures = new List<string>();
        var account = Acc(balance, withdrawnToday);

        foreach (var stock in Stocks())
        foreach (var jammed in JammedValues)
        foreach (var amount in Amounts)
        {
            var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
            var actual = new CashMachine(dispenser).Execute(account, Withdraw(amount));
            var (expected, expectedStock) = ExpectedWithdraw(account, stock, jammed, amount);

            if (actual != expected || dispenser.Stock != expectedStock
                || dispenser.Dispensed.Any(p => p.Total > MoneyDispenser.MaxNotesPerPass))
            {
                failures.Add($"{Fmt(account)} stock {Fmt(stock)} bloquée {Fmt(jammed)} retrait {amount} : " +
                             $"attendu {Fmt(expected)} stock {Fmt(expectedStock)}, obtenu {Fmt(actual)} stock {Fmt(dispenser.Stock)}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} écarts, dont :{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(10))}");
    }

    [Theory]
    [MemberData(nameof(Accounts))]
    public void Depot_conforme_a_l_oracle_sur_tout_le_domaine(int balance, int withdrawnToday)
    {
        var failures = new List<string>();
        var account = Acc(balance, withdrawnToday);

        foreach (var stock in Stocks())
        foreach (var jammed in JammedValues)
        foreach (var notes in Deposits())
        {
            var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
            var actual = new CashMachine(dispenser).Execute(account, Deposit(notes));
            var (expected, expectedStock) = ExpectedDeposit(account, stock, jammed, notes);

            if (actual != expected || dispenser.Stock != expectedStock || dispenser.Dispensed.Count != 0)
            {
                failures.Add($"{Fmt(account)} stock {Fmt(stock)} bloquée {Fmt(jammed)} dépôt {Fmt(notes)} : " +
                             $"attendu {Fmt(expected)} stock {Fmt(expectedStock)}, obtenu {Fmt(actual)} stock {Fmt(dispenser.Stock)}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} écarts, dont :{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(10))}");
    }

    private static string Fmt(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";

    private static string Fmt(AccountState a) => $"[solde {a.Balance}, retiré {a.WithdrawnToday}]";

    private static string Fmt(Denomination? d) => d is { } x ? ((int)x).ToString() : "aucune";

    private static string Fmt(AtmResult r) => $"{r.Status} ({Fmt(r.Notes)}, frais {r.Fee}, {Fmt(r.Account)})";

    // ---- Oracle ----

    private static (AtmResult, NoteCounts) ExpectedWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        AtmResult Refused(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0)
        {
            return (Refused(AtmStatus.InvalidAmount), stock);
        }

        if (account.WithdrawnToday + amount > 200)
        {
            return (Refused(AtmStatus.DailyLimitExceeded), stock);
        }

        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance)
        {
            return (Refused(AtmStatus.InsufficientFunds), stock);
        }

        var available = N(
            jammed == Denomination.Fifty ? 0 : stock.Fifties,
            jammed == Denomination.Twenty ? 0 : stock.Twenties,
            jammed == Denomination.Ten ? 0 : stock.Tens);

        var candidates = new List<NoteCounts>();
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var n = 0; n <= available.Tens; n++)
        {
            var c = N(f, t, n);
            if (c.Value == amount)
            {
                candidates.Add(c);
            }
        }

        if (candidates.Count == 0)
        {
            return (Refused(AtmStatus.CannotDispense), stock);
        }

        var fewest = candidates.Min(c => c.Total);
        var best = candidates.Where(c => c.Total == fewest).ToList();
        Assert.Single(best); // la spec garantit l'unicité

        var notes = best[0];
        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);

        return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
    }

    private static (AtmResult, NoteCounts) ExpectedDeposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        AtmResult Refused(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (notes.Total == 0)
        {
            return (Refused(AtmStatus.InvalidAmount), stock);
        }

        if (jammed is { } j && notes.Of(j) > 0)
        {
            return (Refused(AtmStatus.CassetteJammed), stock);
        }

        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5)
        {
            return (Refused(AtmStatus.CassetteFull), stock);
        }

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }), after);
    }
}
