namespace Atm.Tests;

/// <summary>Petits utilitaires pour écrire les tests dans le vocabulaire de la spec.</summary>
internal static class Support
{
    public static NoteCounts Notes(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Account(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static (CashMachine Machine, MoneyDispenser Dispenser) Machine(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        return (new CashMachine(dispenser), dispenser);
    }

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(NoteCounts notes) => new Operation.Deposit(notes);

    public static readonly NoteCounts Full = new(5, 5, 5);

    /// <summary>Vérifie un refus : rien ne change, ni le compte, ni les billets.</summary>
    public static void AssertRefused(AtmStatus expected, AtmResult result, AccountState before, MoneyDispenser dispenser, NoteCounts stockBefore)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(before, result.Account);
        Assert.Equal(stockBefore, dispenser.Stock);
    }
}

/// <summary>
/// Modèle de référence écrit directement à partir de la spec (v0.2), sans regarder l'implémentation.
/// Il sert aux tests exhaustifs sur le domaine.
/// </summary>
internal static class SpecModel
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

    /// <summary>La combinaison qui fait exactement <paramref name="amount"/> avec le moins de billets, ou null.</summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        {
            for (var t = 0; t <= available.Twenties; t++)
            {
                var rest = amount - 50 * f - 20 * t;
                if (rest < 0 || rest % 10 != 0)
                {
                    continue;
                }

                var c = rest / 10;
                if (c > available.Tens)
                {
                    continue;
                }

                var candidate = new NoteCounts(f, t, c);
                if (best is null || candidate.Total < best.Value.Total)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    /// <summary>Résultat attendu et stock attendu du module après l'opération.</summary>
    public static (AtmResult Result, NoteCounts StockAfter) Withdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus status) => (new AtmResult(status, NoteCounts.None, 0, account), stock);

        if (amount <= 0 || amount % 10 != 0)
        {
            return Refuse(AtmStatus.InvalidAmount);
        }

        if (account.WithdrawnToday + amount > DailyLimit)
        {
            return Refuse(AtmStatus.DailyLimitExceeded);
        }

        var fee = FeeFor(amount);
        if (amount + fee > account.Balance)
        {
            return Refuse(AtmStatus.InsufficientFunds);
        }

        if (FewestNotes(amount, Available(stock, jammed)) is not { } notes)
        {
            return Refuse(AtmStatus.CannotDispense);
        }

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
        return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
    }

    public static (AtmResult Result, NoteCounts StockAfter) Deposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus status) => (new AtmResult(status, NoteCounts.None, 0, account), stock);

        if (notes.Total == 0)
        {
            return Refuse(AtmStatus.InvalidAmount);
        }

        if (jammed is { } cassette && notes.Of(cassette) > 0)
        {
            return Refuse(AtmStatus.CassetteJammed);
        }

        var sum = stock + notes;
        if (sum.Fifties > 5 || sum.Twenties > 5 || sum.Tens > 5)
        {
            return Refuse(AtmStatus.CassetteFull);
        }

        var after = account with { Balance = account.Balance + notes.Value };
        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, after), sum);
    }
}

/// <summary>Les valeurs du domaine de la spec.</summary>
internal static class Domain
{
    public static IEnumerable<int> Balances => Enumerable.Range(0, 51).Select(i => i * 5);

    public static IEnumerable<int> WithdrawnTodays => Enumerable.Range(0, 21).Select(i => i * 10);

    public static IEnumerable<NoteCounts> Stocks =>
        from f in Enumerable.Range(0, 6)
        from t in Enumerable.Range(0, 6)
        from c in Enumerable.Range(0, 6)
        select new NoteCounts(f, t, c);

    public static readonly Denomination?[] JammedCassettes = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static IEnumerable<int> Amounts => new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));

    public static IEnumerable<NoteCounts> Deposits =>
        from f in new[] { 0, 1 }
        from t in new[] { 0, 1 }
        from c in new[] { 0, 1 }
        select new NoteCounts(f, t, c);
}
