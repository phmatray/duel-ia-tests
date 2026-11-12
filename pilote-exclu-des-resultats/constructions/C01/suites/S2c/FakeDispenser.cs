namespace Atm.Tests;

/// <summary>Module de billets simulé : délègue au vrai module, mais peut tomber en panne sur demande.</summary>
internal sealed class FakeDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    /// <summary>Numéro (à partir de 1) de l'appel à <see cref="Dispense"/> qui échoue, ou <c>null</c>.</summary>
    public int? FailDispenseAt { get; init; }

    public bool FailLoad { get; init; }

    public List<NoteCounts> DispenseCalls { get; } = [];

    public List<NoteCounts> LoadCalls { get; } = [];

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);

        return DispenseCalls.Count != FailDispenseAt && _inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);

        return !FailLoad && _inner.Load(notes);
    }
}
