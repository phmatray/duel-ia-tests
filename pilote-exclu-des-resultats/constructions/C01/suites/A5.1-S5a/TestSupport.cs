namespace Atm.Tests;

/// <summary>
/// Enveloppe le vrai <see cref="MoneyDispenser"/> et enregistre chaque appel.
/// Peut simuler une panne (le module rend <c>false</c>) sur un appel donné.
/// </summary>
internal sealed class SpyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    /// <summary>Numéro (à partir de 1) de l'appel à <c>Dispense</c> qui doit échouer.</summary>
    public int? FailDispenseAtCall { get; init; }

    /// <summary>Si vrai, tout appel à <c>Load</c> échoue.</summary>
    public bool FailLoad { get; init; }

    public List<NoteCounts> DispenseCalls { get; } = [];

    public List<NoteCounts> LoadCalls { get; } = [];

    /// <summary>Somme des billets réellement sortis (passages acceptés).</summary>
    public NoteCounts Dispensed { get; private set; }

    /// <summary>Somme des billets réellement rangés (dépôts acceptés).</summary>
    public NoteCounts Loaded { get; private set; }

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (FailDispenseAtCall == DispenseCalls.Count)
        {
            return false;
        }

        var ok = _inner.Dispense(notes);
        if (ok)
        {
            Dispensed += notes;
        }

        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        if (FailLoad)
        {
            return false;
        }

        var ok = _inner.Load(notes);
        if (ok)
        {
            Loaded += notes;
        }

        return ok;
    }
}

internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Denomination? Jam(int? value) => value is null ? null : (Denomination)value.Value;

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(NoteCounts notes) => new Operation.Deposit(notes);

    public static readonly Denomination?[] AllJams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static readonly int[] DomainAmounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return N(f, t, d);
    }

    public static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return N(f, t, d);
    }

    /// <summary>Billets disponibles : le stock, la cassette bloquée comptant pour zéro.</summary>
    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    /// <summary>Recherche exhaustive de la combinaison exacte qui compte le moins de billets.</summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        var ties = 0;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var c = N(f, t, d);
            if (c.Value != amount)
            {
                continue;
            }

            if (best is not { } b || c.Total < b.Total)
            {
                best = c;
                ties = 0;
            }
            else if (c.Total == b.Total)
            {
                ties++;
            }
        }

        if (ties > 0)
        {
            throw new InvalidOperationException($"Combinaison non unique pour {amount} avec {available}.");
        }

        return best;
    }

    public static int Fee(int amount) => amount > 100 ? 5 : 0;

    /// <summary>Le statut attendu d'un retrait, d'après la spec.</summary>
    public static AtmStatus ExpectedWithdrawStatus(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        if (amount <= 0 || amount % 10 != 0) return AtmStatus.InvalidAmount;
        if (account.WithdrawnToday + amount > 200) return AtmStatus.DailyLimitExceeded;
        if (amount + Fee(amount) > account.Balance) return AtmStatus.InsufficientFunds;
        if (FewestNotes(amount, Available(stock, jammed)) is null) return AtmStatus.CannotDispense;
        return AtmStatus.Ok;
    }

    /// <summary>Le statut attendu d'un dépôt, d'après la spec.</summary>
    public static AtmStatus ExpectedDepositStatus(NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        if (notes.Total == 0) return AtmStatus.InvalidAmount;
        if (jammed is { } j && notes.Of(j) > 0) return AtmStatus.CassetteJammed;
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return AtmStatus.CassetteFull;
        return AtmStatus.Ok;
    }
}
