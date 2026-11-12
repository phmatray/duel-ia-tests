namespace Atm.Tests;

/// <summary>Outils communs : notation du stock, espion du module, oracle de la spec.</summary>
internal static class N
{
    /// <summary>Notation « 50×a 20×b 10×c » de la spec.</summary>
    public static NoteCounts C(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static Denomination? Jam(int? value) => value switch
    {
        null => null,
        50 => Denomination.Fifty,
        20 => Denomination.Twenty,
        10 => Denomination.Ten,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static readonly Denomination?[] AllJams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static IEnumerable<NoteCounts> AllStocks()
    {
        for (var a = 0; a <= 5; a++)
            for (var b = 0; b <= 5; b++)
                for (var c = 0; c <= 5; c++)
                    yield return new NoteCounts(a, b, c);
    }

    public static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var a = 0; a <= 1; a++)
            for (var b = 0; b <= 1; b++)
                for (var c = 0; c <= 1; c++)
                    yield return new NoteCounts(a, b, c);
    }

    public static readonly int[] AllAmounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];
}

/// <summary>
/// Enveloppe le vrai module et note chaque appel, pour vérifier les passages et l'absence de mouvement.
/// </summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<(NoteCounts Notes, bool Result)> DispenseCalls { get; } = [];
    public List<(NoteCounts Notes, bool Result)> LoadCalls { get; } = [];

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var result = inner.Dispense(notes);
        DispenseCalls.Add((notes, result));
        return result;
    }

    public bool Load(NoteCounts notes)
    {
        var result = inner.Load(notes);
        LoadCalls.Add((notes, result));
        return result;
    }
}

/// <summary>
/// Module en panne : délègue au vrai module, mais rend <c>false</c> (sans rien changer) à partir de l'appel numéro N.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser inner = new(stock, jammed);

    /// <summary>Numéro (à partir de 1) du premier passage de Dispense qui échoue ; null = jamais.</summary>
    public int? FailDispenseFromCall { get; init; }

    public bool FailLoad { get; init; }

    public int DispenseCallCount { get; private set; }
    public int CallsAfterFault { get; private set; }
    public List<NoteCounts> Dispensed { get; } = [];
    private bool faulted;

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        if (faulted) CallsAfterFault++;
        DispenseCallCount++;
        if (FailDispenseFromCall is { } n && DispenseCallCount >= n)
        {
            faulted = true;
            return false;
        }

        var ok = inner.Dispense(notes);
        if (ok) Dispensed.Add(notes);
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        if (faulted) CallsAfterFault++;
        if (FailLoad)
        {
            faulted = true;
            return false;
        }

        return inner.Load(notes);
    }
}

/// <summary>Oracle écrit directement à partir de la spec, par force brute.</summary>
internal static class Oracle
{
    public static int Fee(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    /// <summary>La combinaison qui fait exactement amount avec le moins de billets, ou null.</summary>
    public static NoteCounts? Combination(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var a = 0; a <= available.Fifties; a++)
            for (var b = 0; b <= available.Twenties; b++)
                for (var c = 0; c <= available.Tens; c++)
                {
                    var candidate = new NoteCounts(a, b, c);
                    if (candidate.Value == amount && (best is null || candidate.Total < best.Value.Total))
                        best = candidate;
                }

        return best;
    }

    public static AtmStatus WithdrawStatus(AccountState account, int amount, NoteCounts stock, Denomination? jammed)
    {
        if (amount <= 0 || amount % 10 != 0) return AtmStatus.InvalidAmount;
        if (account.WithdrawnToday + amount > 200) return AtmStatus.DailyLimitExceeded;
        if (amount + Fee(amount) > account.Balance) return AtmStatus.InsufficientFunds;
        if (Combination(amount, Available(stock, jammed)) is null) return AtmStatus.CannotDispense;
        return AtmStatus.Ok;
    }

    public static AtmStatus DepositStatus(NoteCounts notes, NoteCounts stock, Denomination? jammed)
    {
        if (notes.Total == 0) return AtmStatus.InvalidAmount;
        if (jammed is { } j && notes.Of(j) > 0) return AtmStatus.CassetteJammed;
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return AtmStatus.CassetteFull;
        return AtmStatus.Ok;
    }
}
