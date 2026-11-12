namespace Atm.Tests;

/// <summary>Enveloppe le vrai module, enregistre les appels et peut simuler une panne au n-ième appel.</summary>
internal sealed class SpyDispenser(NoteCounts stock, Denomination? jammed = null, int? failOnCall = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);
    private int _calls;

    public List<NoteCounts> Dispensed { get; } = [];

    public List<NoteCounts> Loaded { get; } = [];

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public int Calls => _calls;

    public bool Dispense(NoteCounts notes)
    {
        if (++_calls == failOnCall)
        {
            return false;
        }

        Dispensed.Add(notes);

        return _inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        if (++_calls == failOnCall)
        {
            return false;
        }

        Loaded.Add(notes);

        return _inner.Load(notes);
    }
}
