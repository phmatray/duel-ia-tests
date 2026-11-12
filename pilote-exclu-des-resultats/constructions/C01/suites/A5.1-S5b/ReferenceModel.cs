namespace Atm.Tests;

/// <summary>
/// Traduction directe de la spec, indépendante de l'implémentation. Sert d'oracle au test exhaustif.
/// </summary>
internal static class ReferenceModel
{
    public const int DailyLimit = 200;

    public static int FeeFor(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    /// <summary>La combinaison exacte qui compte le moins de billets, ou null.</summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var candidate = new NoteCounts(f, t, d);
            if (candidate.Value == amount && (best is null || candidate.Total < best.Value.Total))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Résultat attendu et stock attendu après l'opération.</summary>
    public static (AtmResult Result, NoteCounts StockAfter) Expected(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation) => operation switch
    {
        Operation.Withdraw w => ExpectedWithdraw(account, stock, jammed, w.Amount),
        Operation.Deposit d => ExpectedDeposit(account, stock, jammed, d.Notes),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static (AtmResult, NoteCounts) ExpectedWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        if (amount <= 0 || amount % 10 != 0) return (Refused(AtmStatus.InvalidAmount, account), stock);
        if (account.WithdrawnToday + amount > DailyLimit) return (Refused(AtmStatus.DailyLimitExceeded, account), stock);
        var fee = FeeFor(amount);
        if (amount + fee > account.Balance) return (Refused(AtmStatus.InsufficientFunds, account), stock);
        if (FewestNotes(amount, Available(stock, jammed)) is not { } combination)
            return (Refused(AtmStatus.CannotDispense, account), stock);

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
        return (new AtmResult(AtmStatus.Ok, combination, fee, after), stock - combination);
    }

    private static (AtmResult, NoteCounts) ExpectedDeposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        if (notes.Total == 0) return (Refused(AtmStatus.InvalidAmount, account), stock);
        if (jammed is { } j && notes.Of(j) > 0) return (Refused(AtmStatus.CassetteJammed, account), stock);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (Refused(AtmStatus.CassetteFull, account), stock);

        var account2 = account with { Balance = account.Balance + notes.Value };
        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account2), after);
    }

    private static AtmResult Refused(AtmStatus status, AccountState account) => new(status, NoteCounts.None, 0, account);
}
