namespace Atm.Tests;

internal static class Notes
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);
}

/// <summary>
/// Module de test : délègue au vrai <see cref="MoneyDispenser"/>, enregistre chaque appel
/// et peut simuler une panne (rendre <c>false</c>) sur un appel donné.
/// </summary>
internal sealed class SpyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    public List<NoteCounts> DispenseCalls { get; } = [];
    public List<NoteCounts> LoadCalls { get; } = [];
    public List<NoteCounts> SuccessfulPasses { get; } = [];

    /// <summary>Numéro (base 1) de l'appel à Dispense qui doit échouer, ou null.</summary>
    public int? FailDispenseOnCall { get; init; }

    public bool FailLoad { get; init; }

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (FailDispenseOnCall == DispenseCalls.Count)
        {
            return false;
        }

        var ok = _inner.Dispense(notes);
        if (ok)
        {
            SuccessfulPasses.Add(notes);
        }

        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        return !FailLoad && _inner.Load(notes);
    }
}

/// <summary>Modèle de référence écrit à partir de la spec.</summary>
internal static class Oracle
{
    public const int DailyLimit = 200;
    public const int Capacity = 5;

    public static int FeeFor(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var c = new NoteCounts(f, t, d);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total))
            {
                best = c;
            }
        }

        return best;
    }

    public static (AtmResult Result, NoteCounts StockAfter) Withdraw(
        AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (account.WithdrawnToday + amount > DailyLimit) return (Refuse(AtmStatus.DailyLimitExceeded), stock);
        var fee = FeeFor(amount);
        if (amount + fee > account.Balance) return (Refuse(AtmStatus.InsufficientFunds), stock);
        var combination = FewestNotes(amount, Available(stock, jammed));
        if (combination is not { } c) return (Refuse(AtmStatus.CannotDispense), stock);

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
        return (new AtmResult(AtmStatus.Ok, c, fee, after), stock - c);
    }

    public static (AtmResult Result, NoteCounts StockAfter) Deposit(
        AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (notes.Total == 0) return (Refuse(AtmStatus.InvalidAmount), stock);
        if (jammed is { } j && notes.Of(j) > 0) return (Refuse(AtmStatus.CassetteJammed), stock);
        var after = stock + notes;
        if (after.Fifties > Capacity || after.Twenties > Capacity || after.Tens > Capacity)
            return (Refuse(AtmStatus.CassetteFull), stock);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }), after);
    }
}
