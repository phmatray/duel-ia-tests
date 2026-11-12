namespace Atm.Tests;

internal static class Notes
{
    public static NoteCounts Of(int fifties, int twenties, int tens) => new(fifties, twenties, tens);
}

/// <summary>Module de billets simulé : enregistre les appels et peut tomber en panne.</summary>
internal sealed class FakeDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    public NoteCounts Stock { get; private set; } = stock;

    public Denomination? Jammed { get; } = jammed;

    /// <summary>Numéro (à partir de 1) du passage de distribution qui échoue, ou null.</summary>
    public int? FailDispenseOnPass { get; init; }

    public bool FailLoad { get; init; }

    public List<NoteCounts> DispenseCalls { get; } = [];

    public List<NoteCounts> LoadCalls { get; } = [];

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (DispenseCalls.Count == FailDispenseOnPass)
        {
            return false;
        }

        Stock -= notes;

        return true;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        if (FailLoad)
        {
            return false;
        }

        Stock += notes;

        return true;
    }
}

internal sealed record UnknownOperation : Operation;
