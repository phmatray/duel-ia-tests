namespace Atm.Tests;

/// <summary>Raccourcis pour construire une machine avec le vrai module, comme le domaine de la spec.</summary>
internal static class Machine
{
    public static NoteCounts Notes(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static readonly NoteCounts Full = new(5, 5, 5);

    public static (AtmResult Result, MoneyDispenser Dispenser) Withdraw(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawnToday), new Operation.Withdraw(amount));
        return (result, dispenser);
    }

    public static (AtmResult Result, MoneyDispenser Dispenser) Deposit(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawnToday), new Operation.Deposit(notes));
        return (result, dispenser);
    }

    /// <summary>Vérifie la règle « sur un refus, rien ne change ».</summary>
    public static void AssertRefused(
        AtmStatus expected, AtmResult result, MoneyDispenser dispenser, int balance, int withdrawnToday, NoteCounts stock)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(balance, withdrawnToday), result.Account);
        Assert.Equal(stock, dispenser.Stock);
    }
}

/// <summary>Enveloppe le vrai module et enregistre chaque appel, sans changer son comportement.</summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<NoteCounts> DispenseCalls { get; } = [];
    public List<NoteCounts> LoadCalls { get; } = [];

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        return inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        return inner.Load(notes);
    }
}

/// <summary>Module en panne : rend <c>false</c> à partir du n-ième passage (1 = le premier).</summary>
internal sealed class FaultyDispenser(IMoneyDispenser inner, int failOnDispenseCall = 1, bool failLoad = true) : IMoneyDispenser
{
    private int _dispenseCalls;

    public NoteCounts Dispensed { get; private set; } = NoteCounts.None;
    public int DispenseCalls => _dispenseCalls;
    public int LoadCalls { get; private set; }

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        _dispenseCalls++;
        if (_dispenseCalls >= failOnDispenseCall)
        {
            return false;
        }

        var ok = inner.Dispense(notes);
        if (ok)
        {
            Dispensed += notes;
        }

        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls++;
        return !failLoad && inner.Load(notes);
    }
}
