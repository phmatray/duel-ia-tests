namespace Atm.Tests;

/// <summary>
/// Enveloppe le vrai <see cref="MoneyDispenser"/> : enregistre chaque appel et peut simuler une panne
/// (le module rend <c>false</c> malgré les contrôles de la machine).
/// </summary>
internal sealed class SpyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser inner = new(stock, jammed);

    /// <summary>Numéro (à partir de 1) de l'appel à <c>Dispense</c> qui tombe en panne.</summary>
    public int? FailDispenseOnCall { get; init; }

    public bool FailLoad { get; init; }

    public List<NoteCounts> DispenseCalls { get; } = [];

    public List<NoteCounts> SuccessfulDispenses { get; } = [];

    public List<NoteCounts> LoadCalls { get; } = [];

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (FailDispenseOnCall == DispenseCalls.Count)
        {
            return false;
        }

        var ok = inner.Dispense(notes);
        if (ok)
        {
            SuccessfulDispenses.Add(notes);
        }

        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);

        return !FailLoad && inner.Load(notes);
    }
}
