namespace Atm.Tests;

/// <summary>Enveloppe un module réel et enregistre chaque appel.</summary>
internal sealed class RecordingDispenser(IMoneyDispenser inner) : IMoneyDispenser
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

/// <summary>Module en panne : rend <c>false</c> à partir d'un appel donné.</summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null, int failOnDispenseCall = 1, bool failLoad = true)
    : IMoneyDispenser
{
    private int _dispenseCalls;

    public NoteCounts Stock { get; private set; } = stock;

    public Denomination? Jammed { get; } = jammed;

    public NoteCounts Dispensed { get; private set; }

    public int DispenseCalls => _dispenseCalls;

    public int LoadCalls { get; private set; }

    public bool Dispense(NoteCounts notes)
    {
        _dispenseCalls++;
        if (_dispenseCalls >= failOnDispenseCall)
        {
            return false;
        }

        Stock -= notes;
        Dispensed += notes;
        return true;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls++;
        if (failLoad)
        {
            return false;
        }

        Stock += notes;
        return true;
    }
}

internal sealed record UnknownOperation : Operation;
