namespace Atm.Tests;

/// <summary>Outils communs aux tests : construction de la machine et modèle de référence tiré de la spec.</summary>
internal static class TestSupport
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static Denomination? Jam(int value) => value == 0 ? null : (Denomination)value;

    public static (CashMachine Machine, MoneyDispenser Dispenser) Build(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        return (new CashMachine(dispenser), dispenser);
    }

    public static AtmResult Withdraw(NoteCounts stock, Denomination? jammed, AccountState account, int amount, out NoteCounts stockAfter)
    {
        var (machine, dispenser) = Build(stock, jammed);
        var result = machine.Execute(account, new Operation.Withdraw(amount));
        stockAfter = dispenser.Stock;
        return result;
    }

    public static AtmResult Deposit(NoteCounts stock, Denomination? jammed, AccountState account, NoteCounts notes, out NoteCounts stockAfter)
    {
        var (machine, dispenser) = Build(stock, jammed);
        var result = machine.Execute(account, new Operation.Deposit(notes));
        stockAfter = dispenser.Stock;
        return result;
    }

    /// <summary>Vérifie l'invariant « sur un refus, rien ne change ».</summary>
    public static void AssertRefused(AtmStatus expected, AtmResult result, AccountState account, NoteCounts stockBefore, NoteCounts stockAfter)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stockBefore, stockAfter);
    }

    // ---- Modèle de référence, écrit directement d'après spec.md, sans regarder l'implémentation ----

    public static int ExpectedFee(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => new(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    /// <summary>Recherche exhaustive de la combinaison exacte qui compte le moins de billets.</summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var n = 0; n <= available.Tens; n++)
        {
            var candidate = new NoteCounts(f, t, n);
            if (candidate.Value == amount && (best is null || candidate.Total < best.Value.Total))
            {
                best = candidate;
            }
        }

        return best;
    }

    public static (AtmResult Result, NoteCounts StockAfter) ExpectedWithdraw(NoteCounts stock, Denomination? jammed, AccountState account, int amount)
    {
        AtmResult Refuse(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (account.WithdrawnToday + amount > 200) return (Refuse(AtmStatus.DailyLimitExceeded), stock);
        var fee = ExpectedFee(amount);
        if (amount + fee > account.Balance) return (Refuse(AtmStatus.InsufficientFunds), stock);
        if (FewestNotes(amount, Available(stock, jammed)) is not { } notes) return (Refuse(AtmStatus.CannotDispense), stock);

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
        return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
    }

    public static (AtmResult Result, NoteCounts StockAfter) ExpectedDeposit(NoteCounts stock, Denomination? jammed, AccountState account, NoteCounts notes)
    {
        AtmResult Refuse(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (notes.Total == 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (jammed is { } j && notes.Of(j) > 0) return (Refuse(AtmStatus.CassetteJammed), stock);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (Refuse(AtmStatus.CassetteFull), stock);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }), after);
    }

    // ---- Énumération du domaine ----

    public static readonly Denomination?[] JammedValues = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
            yield return new NoteCounts(f, t, n);
    }

    public static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var n = 0; n <= 1; n++)
            yield return new NoteCounts(f, t, n);
    }

    public static IEnumerable<int> Balances => Enumerable.Range(0, 51).Select(i => i * 5);

    public static IEnumerable<int> WithdrawnTodayValues => Enumerable.Range(0, 21).Select(i => i * 10);
}
