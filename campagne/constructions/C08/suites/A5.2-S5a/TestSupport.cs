namespace Atm.Tests;

/// <summary>Raccourcis pour construire les données de test.</summary>
internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation.Withdraw W(int amount) => new(amount);

    public static Operation.Deposit D(int fifties, int twenties, int tens) => new(N(fifties, twenties, tens));

    public static (CashMachine Machine, MoneyDispenser Dispenser) Real(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        return (new CashMachine(dispenser), dispenser);
    }

    public static (CashMachine Machine, SpyDispenser Spy) Spied(NoteCounts stock, Denomination? jammed = null)
    {
        var spy = new SpyDispenser(new MoneyDispenser(stock, jammed));
        return (new CashMachine(spy), spy);
    }

    /// <summary>Vérifie qu'un refus ne change rien : Notes vide, Fee 0, compte identique.</summary>
    public static void AssertRefused(AtmResult result, AtmStatus status, AccountState account)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
    }
}

/// <summary>Enveloppe un module réel et enregistre chaque appel.</summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<(NoteCounts Notes, bool Ok)> Dispenses { get; } = [];

    public List<(NoteCounts Notes, bool Ok)> Loads { get; } = [];

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var ok = inner.Dispense(notes);
        Dispenses.Add((notes, ok));
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        var ok = inner.Load(notes);
        Loads.Add((notes, ok));
        return ok;
    }

    public int Calls => Dispenses.Count + Loads.Count;
}

/// <summary>
/// Module en panne : se comporte comme le vrai module, mais rend <c>false</c> (sans rien changer)
/// au n-ième appel de <see cref="Dispense"/> et à tout appel de <see cref="Load"/> si demandé.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed, int failOnDispenseCall, bool failLoad = false)
    : IMoneyDispenser
{
    private readonly MoneyDispenser inner = new(stock, jammed);

    public List<(NoteCounts Notes, bool Ok)> Dispenses { get; } = [];

    public List<NoteCounts> Loads { get; } = [];

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var ok = Dispenses.Count + 1 != failOnDispenseCall && inner.Dispense(notes);
        Dispenses.Add((notes, ok));
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        Loads.Add(notes);
        return !failLoad && inner.Load(notes);
    }
}
