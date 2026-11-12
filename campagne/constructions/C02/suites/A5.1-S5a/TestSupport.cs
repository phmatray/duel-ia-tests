namespace Atm.Tests;

/// <summary>Raccourcis pour écrire les tests comme la spec : stock « 50×a 20×b 10×c ».</summary>
internal static class T
{
    public static NoteCounts Notes(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Account(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(NoteCounts notes) => new Operation.Deposit(notes);

    /// <summary>Le vrai module et la machine branchée dessus.</summary>
    public static (CashMachine Machine, MoneyDispenser Dispenser) Real(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        return (new CashMachine(dispenser), dispenser);
    }

    /// <summary>Le vrai module, enveloppé dans un espion qui enregistre chaque appel.</summary>
    public static (CashMachine Machine, SpyDispenser Spy) Spied(NoteCounts stock, Denomination? jammed = null)
    {
        var spy = new SpyDispenser(new MoneyDispenser(stock, jammed));
        return (new CashMachine(spy), spy);
    }
}

/// <summary>Délègue au vrai module et enregistre les appels.</summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
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
/// Module en panne : se comporte comme le vrai module, sauf que l'appel numéro <c>failOnCall</c>
/// (1 = le premier) à <c>Dispense</c> ou <c>Load</c> rend <c>false</c> sans rien changer.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed, int failOnCall) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    private int _calls;

    public List<NoteCounts> Dispensed { get; } = [];

    public List<NoteCounts> Loaded { get; } = [];

    public int Calls => _calls;

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        Dispensed.Add(notes);
        return ++_calls != failOnCall && _inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        Loaded.Add(notes);
        return ++_calls != failOnCall && _inner.Load(notes);
    }
}
