namespace Atm.Tests;

/// <summary>Enveloppe le vrai module et enregistre chaque appel.</summary>
public sealed class SpyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser inner = new(stock, jammed);

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
}

/// <summary>Module en panne : accepte les passages jusqu'au n-ième, qui échoue (et tous les suivants).</summary>
public sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    /// <summary>Numéro (à partir de 1) du premier appel à <see cref="Dispense"/> qui rend false.</summary>
    public int FailDispenseAtCall { get; init; } = 1;

    public bool FailLoad { get; init; }

    public List<NoteCounts> DispenseCalls { get; } = [];

    public NoteCounts SuccessfullyDispensed { get; private set; }

    public int LoadCallCount { get; private set; }

    public NoteCounts Stock { get; private set; } = stock;

    public Denomination? Jammed { get; } = jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (DispenseCalls.Count >= FailDispenseAtCall)
        {
            return false;
        }

        Stock -= notes;
        SuccessfullyDispensed += notes;
        return true;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCallCount++;
        if (FailLoad)
        {
            return false;
        }

        Stock += notes;
        return true;
    }
}
