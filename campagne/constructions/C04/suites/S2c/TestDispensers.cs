namespace Atm.Tests;

/// <summary>Enveloppe le vrai module, enregistre les appels et peut tomber en panne au n-ième appel.</summary>
internal sealed class RecordingDispenser(IMoneyDispenser inner, int? failOnDispenseCall = null, bool failLoad = false)
    : IMoneyDispenser
{
    public List<NoteCounts> DispenseCalls { get; } = [];

    public List<NoteCounts> LoadCalls { get; } = [];

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);

        return DispenseCalls.Count != failOnDispenseCall && inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);

        return !failLoad && inner.Load(notes);
    }
}

internal sealed record UnknownOperation : Operation;
