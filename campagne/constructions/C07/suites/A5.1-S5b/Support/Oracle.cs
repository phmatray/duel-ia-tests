namespace Atm.Tests.Support;

/// <summary>Le comportement attendu, écrit directement d'après la spec.</summary>
public static class Oracle
{
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

    public static AtmResult Withdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        AtmResult Refuse(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
        var fee = FeeFor(amount);
        if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds);
        if (FewestNotes(amount, Available(stock, jammed)) is not { } notes) return Refuse(AtmStatus.CannotDispense);

        return new AtmResult(AtmStatus.Ok, notes, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    public static AtmResult Deposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        AtmResult Refuse(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value });
    }
}
