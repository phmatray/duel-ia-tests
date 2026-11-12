namespace Atm.Tests;

/// <summary>Raccourcis pour écrire les cas comme dans la spec.</summary>
internal static class S
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation W(int amount) => new Operation.Withdraw(amount);

    public static Operation D(NoteCounts notes) => new Operation.Deposit(notes);

    public static Denomination? Jam(int? value) => value is { } v ? (Denomination)v : null;

    public static (CashMachine Machine, MoneyDispenser Dispenser) Real(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        return (new CashMachine(dispenser), dispenser);
    }
}

/// <summary>Enveloppe le vrai module et note chaque appel.</summary>
internal sealed class RecordingDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<NoteCounts> Dispensed { get; } = [];
    public List<NoteCounts> Loaded { get; } = [];

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        Dispensed.Add(notes);
        return inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        Loaded.Add(notes);
        return inner.Load(notes);
    }
}

/// <summary>Module en panne : se comporte comme le vrai, mais rend false à partir du n-ième appel (1 = dès le premier).</summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed, int failFromCall) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);
    private int _calls;

    public List<NoteCounts> DispenseCalls { get; } = [];
    public List<NoteCounts> LoadCalls { get; } = [];
    public List<NoteCounts> Succeeded { get; } = [];

    public NoteCounts Stock => _inner.Stock;
    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (++_calls >= failFromCall) return false;
        var ok = _inner.Dispense(notes);
        if (ok) Succeeded.Add(notes);
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        if (++_calls >= failFromCall) return false;
        return _inner.Load(notes);
    }
}
