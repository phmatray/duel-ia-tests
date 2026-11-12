namespace Atm.Tests;

/// <summary>Raccourcis pour écrire les jeux de données comme dans la spec.</summary>
internal static class Build
{
    /// <summary>Billets notés comme la spec : 50×a 20×b 10×c.</summary>
    public static NoteCounts Notes(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Account(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(int fifties, int twenties, int tens) => new Operation.Deposit(new(fifties, twenties, tens));
}

/// <summary>Un appel fait au module, avec sa réponse.</summary>
public sealed record DispenserCall(string Method, NoteCounts Notes, bool Result);

/// <summary>
/// Enrobe le vrai <see cref="MoneyDispenser"/> : journalise chaque appel et peut simuler une panne
/// (le module rend <c>false</c> sans rien changer) sur le n-ième passage ou sur le dépôt.
/// </summary>
public sealed class SpyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    public List<DispenserCall> Calls { get; } = [];

    /// <summary>Numéro (1, 2, ...) de l'appel à <c>Dispense</c> qui tombe en panne, ou <c>null</c>.</summary>
    public int? FailOnDispenseCall { get; init; }

    /// <summary>Si vrai, <c>Load</c> tombe en panne.</summary>
    public bool FailOnLoad { get; init; }

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public IEnumerable<DispenserCall> DispenseCalls => Calls.Where(c => c.Method == nameof(Dispense));

    public IEnumerable<DispenserCall> LoadCalls => Calls.Where(c => c.Method == nameof(Load));

    /// <summary>Somme des billets réellement sortis (passages réussis).</summary>
    public NoteCounts Dispensed =>
        DispenseCalls.Where(c => c.Result).Aggregate(NoteCounts.None, (sum, c) => sum + c.Notes);

    public bool Dispense(NoteCounts notes)
    {
        var number = DispenseCalls.Count() + 1;
        var result = number != FailOnDispenseCall && _inner.Dispense(notes);
        Calls.Add(new(nameof(Dispense), notes, result));
        return result;
    }

    public bool Load(NoteCounts notes)
    {
        var result = !FailOnLoad && _inner.Load(notes);
        Calls.Add(new(nameof(Load), notes, result));
        return result;
    }
}
