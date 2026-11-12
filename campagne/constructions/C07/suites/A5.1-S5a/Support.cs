namespace Atm.Tests;

/// <summary>Raccourcis de construction pour les tests.</summary>
internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(NoteCounts notes) => new Operation.Deposit(notes);

    public static (CashMachine Machine, MoneyDispenser Dispenser) Machine(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        return (new CashMachine(dispenser), dispenser);
    }

    public static (CashMachine Machine, RecordingDispenser Dispenser) RecordingAtm(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
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

/// <summary>
/// Module défaillant : se comporte comme le vrai, sauf qu'il rend <c>false</c>
/// à partir du n-ième appel à <see cref="Dispense"/> (base 1), ou à tout <see cref="Load"/>.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed, int failOnDispenseCall, bool failOnLoad)
    : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    public List<NoteCounts> DispenseCalls { get; } = [];

    public List<NoteCounts> LoadCalls { get; } = [];

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (DispenseCalls.Count >= failOnDispenseCall)
        {
            return false;
        }

        return _inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        return !failOnLoad && _inner.Load(notes);
    }
}
