namespace Atm.Tests;

/// <summary>Modèle de référence écrit à partir de la spec, indépendamment de l'implémentation.</summary>
public static class Oracle
{
    public static int FeeFor(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => new(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    /// <summary>Toutes les combinaisons disponibles qui font exactement le montant.</summary>
    public static IEnumerable<NoteCounts> Combinations(int amount, NoteCounts available)
    {
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var c = new NoteCounts(f, t, d);
            if (c.Value == amount)
            {
                yield return c;
            }
        }
    }

    public static NoteCounts? Fewest(int amount, NoteCounts available)
    {
        var all = Combinations(amount, available).ToList();
        if (all.Count == 0)
        {
            return null;
        }

        var min = all.Min(c => c.Total);
        return all.Single(c => c.Total == min);
    }

    public static (AtmResult Result, NoteCounts StockAfter) Expected(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        AtmResult Refused(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        switch (operation)
        {
            case Operation.Withdraw { Amount: var amount }:
            {
                if (amount <= 0 || amount % 10 != 0) return (Refused(AtmStatus.InvalidAmount), stock);
                if (account.WithdrawnToday + amount > 200) return (Refused(AtmStatus.DailyLimitExceeded), stock);
                var fee = FeeFor(amount);
                if (amount + fee > account.Balance) return (Refused(AtmStatus.InsufficientFunds), stock);
                if (Fewest(amount, Available(stock, jammed)) is not { } notes) return (Refused(AtmStatus.CannotDispense), stock);
                var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
                return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
            }

            case Operation.Deposit { Notes: var notes }:
            {
                if (notes.Total == 0) return (Refused(AtmStatus.InvalidAmount), stock);
                if (jammed is { } j && notes.Of(j) > 0) return (Refused(AtmStatus.CassetteJammed), stock);
                var s = stock + notes;
                if (s.Fifties > 5 || s.Twenties > 5 || s.Tens > 5) return (Refused(AtmStatus.CassetteFull), stock);
                var after = account with { Balance = account.Balance + notes.Value };
                return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, after), s);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}
