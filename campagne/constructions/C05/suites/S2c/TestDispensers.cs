namespace Atm.Tests;

/// <summary>Enveloppe le vrai module, enregistre les appels et peut simuler une panne.</summary>
internal sealed class RecordingDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    public List<NoteCounts> DispenseCalls { get; } = [];
    public List<NoteCounts> LoadCalls { get; } = [];

    /// <summary>Numéro (à partir de 1) de l'appel à <c>Dispense</c> qui échoue, ou <c>null</c>.</summary>
    public int? FailDispenseOnCall { get; init; }

    public bool FailLoad { get; init; }

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        return DispenseCalls.Count != FailDispenseOnCall && _inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        return !FailLoad && _inner.Load(notes);
    }
}

internal sealed record UnknownOperation : Operation;
