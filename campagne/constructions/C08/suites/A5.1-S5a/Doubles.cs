namespace Atm.Tests;

/// <summary>Le vrai module, enveloppé pour noter chaque appel.</summary>
internal sealed class RecordingDispenser(NoteCounts stock, Denomination? jammed) : IMoneyDispenser
{
    private readonly MoneyDispenser inner = new(stock, jammed);

    public List<NoteCounts> Passes { get; } = [];

    public List<NoteCounts> Loads { get; } = [];

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        Passes.Add(notes);
        return inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        Loads.Add(notes);
        return inner.Load(notes);
    }
}

/// <summary>
/// Un module en panne : il se comporte comme le vrai, sauf qu'il rend <c>false</c>
/// à partir du n-ième appel à <see cref="Dispense"/> (base 1), ou à tout <see cref="Load"/>.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed, int failOnDispenseCall = 1, bool failLoad = true)
    : IMoneyDispenser
{
    private readonly MoneyDispenser inner = new(stock, jammed);

    public int DispenseCalls { get; private set; }

    public int LoadCalls { get; private set; }

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls++;
        return DispenseCalls < failOnDispenseCall && inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls++;
        return !failLoad && inner.Load(notes);
    }
}
