namespace Atm.Tests;

/// <summary>Enveloppe un module et enregistre chaque passage demandé.</summary>
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
/// Module en panne : se comporte comme le vrai, puis rend <c>false</c> à partir du n-ième appel
/// (1 = dès le premier). Sert à exercer la règle « DispenserFault », que le vrai module ne déclenche jamais.
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
        if (++_calls >= failOnCall) return false;
        var ok = _inner.Dispense(notes);
        if (ok) Dispensed.Add(notes);
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        if (++_calls >= failOnCall) return false;
        var ok = _inner.Load(notes);
        if (ok) Loaded.Add(notes);
        return ok;
    }
}
