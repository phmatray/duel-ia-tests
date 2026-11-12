namespace Atm.Tests;

/// <summary>Enveloppe un module réel et enregistre chaque appel et sa réponse.</summary>
public sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
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

    public NoteCounts SuccessfullyDispensed =>
        DispenseCalls.Where(c => c.Result).Aggregate(NoteCounts.None, (sum, c) => sum + c.Notes);
}

/// <summary>
/// Module réel qui tombe en panne : le n-ième appel (à partir de 1) à Dispense ou Load rend false sans rien changer.
/// </summary>
public sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null, int failOnDispenseCall = int.MaxValue,
    int failOnLoadCall = int.MaxValue) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);
    private int _dispenseCount;
    private int _loadCount;

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes) => ++_dispenseCount != failOnDispenseCall && _inner.Dispense(notes);

    public bool Load(NoteCounts notes) => ++_loadCount != failOnLoadCall && _inner.Load(notes);
}

public static class Fixture
{
    public static NoteCounts Notes(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static readonly NoteCounts Full = new(5, 5, 5);

    public static (CashMachine Machine, SpyDispenser Spy) Machine(NoteCounts stock, Denomination? jammed = null)
    {
        var spy = new SpyDispenser(new MoneyDispenser(stock, jammed));
        return (new CashMachine(spy), spy);
    }

    public static Denomination? Jam(int jammed) => jammed == 0 ? null : (Denomination)jammed;
}
