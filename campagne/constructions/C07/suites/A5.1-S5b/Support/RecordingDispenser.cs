namespace Atm.Tests.Support;

/// <summary>Enveloppe un module et note chaque appel avec sa réponse.</summary>
public sealed class RecordingDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<(NoteCounts Notes, bool Accepted)> Dispenses { get; } = [];

    public List<(NoteCounts Notes, bool Accepted)> Loads { get; } = [];

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var accepted = inner.Dispense(notes);
        Dispenses.Add((notes, accepted));
        return accepted;
    }

    public bool Load(NoteCounts notes)
    {
        var accepted = inner.Load(notes);
        Loads.Add((notes, accepted));
        return accepted;
    }

    public NoteCounts DispensedTotal =>
        Dispenses.Where(d => d.Accepted).Aggregate(NoteCounts.None, (acc, d) => acc + d.Notes);
}

/// <summary>Module qui tombe en panne : rend false à l'appel choisi, sans rien changer.</summary>
public sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    /// <summary>Numéro (à partir de 1) de l'appel à Dispense qui échoue, ou null.</summary>
    public int? FailDispenseAt { get; init; }

    public bool FailLoad { get; init; }

    public List<(NoteCounts Notes, bool Accepted)> Dispenses { get; } = [];

    public List<(NoteCounts Notes, bool Accepted)> Loads { get; } = [];

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var accepted = Dispenses.Count + 1 != FailDispenseAt && _inner.Dispense(notes);
        Dispenses.Add((notes, accepted));
        return accepted;
    }

    public bool Load(NoteCounts notes)
    {
        var accepted = !FailLoad && _inner.Load(notes);
        Loads.Add((notes, accepted));
        return accepted;
    }
}
