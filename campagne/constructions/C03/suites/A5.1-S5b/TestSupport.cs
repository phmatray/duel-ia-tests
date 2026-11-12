namespace Atm.Tests;

/// <summary>Outils communs : construction du scénario et modèle de référence tiré de la spec.</summary>
internal static class TestSupport
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static readonly NoteCounts FullStock = N(5, 5, 5);

    public static (AtmResult Result, MoneyDispenser Dispenser) Run(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var machine = new CashMachine(dispenser);
        var result = machine.Execute(new AccountState(balance, withdrawnToday), operation);
        return (result, dispenser);
    }

    public static (AtmResult Result, MoneyDispenser Dispenser) Withdraw(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount) =>
        Run(balance, withdrawnToday, stock, jammed, new Operation.Withdraw(amount));

    public static (AtmResult Result, MoneyDispenser Dispenser) Deposit(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, NoteCounts notes) =>
        Run(balance, withdrawnToday, stock, jammed, new Operation.Deposit(notes));

    /// <summary>Sur un refus, rien ne change : Notes vide, Fee à 0, compte identique, aucun billet déplacé.</summary>
    public static void AssertRefused(
        AtmStatus expected, (AtmResult Result, MoneyDispenser Dispenser) run,
        int balance, int withdrawnToday, NoteCounts stock)
    {
        var (result, dispenser) = run;
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(balance, withdrawnToday), result.Account);
        Assert.Equal(stock, dispenser.Stock);
    }

    // ---------------------------------------------------------------------------------------------
    // Modèle de référence, écrit uniquement à partir de spec.md.
    // ---------------------------------------------------------------------------------------------

    public sealed record Expected(AtmResult Result, NoteCounts StockAfter);

    public static int FeeFor(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    /// <summary>La combinaison exacte qui compte le moins de billets, ou null.</summary>
    public static NoteCounts? FewestNotes(NoteCounts available, int amount)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var n = 0; n <= available.Tens; n++)
        {
            var candidate = N(f, t, n);
            if (candidate.Value == amount && (best is null || candidate.Total < best.Value.Total))
            {
                best = candidate;
            }
        }

        return best;
    }

    public static Expected ExpectedWithdraw(int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount)
    {
        var account = new AccountState(balance, withdrawnToday);
        Expected Refuse(AtmStatus s) => new(new AtmResult(s, NoteCounts.None, 0, account), stock);

        if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
        if (withdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
        var fee = FeeFor(amount);
        if (amount + fee > balance) return Refuse(AtmStatus.InsufficientFunds);
        if (FewestNotes(Available(stock, jammed), amount) is not { } notes) return Refuse(AtmStatus.CannotDispense);

        return new(
            new AtmResult(AtmStatus.Ok, notes, fee, new AccountState(balance - amount - fee, withdrawnToday + amount)),
            stock - notes);
    }

    public static Expected ExpectedDeposit(int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        var account = new AccountState(balance, withdrawnToday);
        Expected Refuse(AtmStatus s) => new(new AtmResult(s, NoteCounts.None, 0, account), stock);

        if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);

        return new(
            new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(balance + notes.Value, withdrawnToday)),
            after);
    }

    // ---------------------------------------------------------------------------------------------
    // Le domaine.
    // ---------------------------------------------------------------------------------------------

    public static readonly Denomination?[] JammedValues = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static IEnumerable<int> Balances => Enumerable.Range(0, 51).Select(i => i * 5);

    public static IEnumerable<int> WithdrawnTodayValues => Enumerable.Range(0, 21).Select(i => i * 10);

    public static IEnumerable<int> Amounts => new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));

    public static IEnumerable<NoteCounts> Stocks =>
        from f in Enumerable.Range(0, 6)
        from t in Enumerable.Range(0, 6)
        from n in Enumerable.Range(0, 6)
        select N(f, t, n);

    public static IEnumerable<NoteCounts> DepositValues =>
        from f in Enumerable.Range(0, 2)
        from t in Enumerable.Range(0, 2)
        from n in Enumerable.Range(0, 2)
        select N(f, t, n);
}
