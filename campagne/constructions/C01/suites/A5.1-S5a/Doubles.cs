namespace Atm.Tests;

/// <summary>Enveloppe un module réel et enregistre chaque appel.</summary>
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

/// <summary>Module en panne : délègue au vrai module jusqu'à l'appel numéro <c>failOnCall</c>, qui rend false sans rien changer.</summary>
internal sealed class FaultyDispenser(IMoneyDispenser inner, int failOnCall = 1) : IMoneyDispenser
{
    private int _calls;

    public List<NoteCounts> Dispensed { get; } = [];
    public int Calls => _calls;

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        if (++_calls >= failOnCall) return false;
        if (!inner.Dispense(notes)) return false;
        Dispensed.Add(notes);
        return true;
    }

    public bool Load(NoteCounts notes) => ++_calls < failOnCall && inner.Load(notes);
}
