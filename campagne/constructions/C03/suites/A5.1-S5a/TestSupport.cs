namespace Atm.Tests;

/// <summary>Enveloppe le vrai module et enregistre chaque appel, pour vérifier les mouvements de billets.</summary>
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

/// <summary>
/// Module en panne : accepte tous les appels jusqu'au n-ième (1 = premier appel), qui rend <c>false</c>.
/// Volontairement permissif avant la panne (aucune limite de passage) pour isoler le traitement de la panne.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed, int failOnCall) : IMoneyDispenser
{
    public List<NoteCounts> SuccessfulDispenses { get; } = [];
    public int Calls { get; private set; }

    public NoteCounts Stock { get; private set; } = stock;
    public Denomination? Jammed => jammed;

    public bool Dispense(NoteCounts notes)
    {
        if (++Calls >= failOnCall) return false;
        Stock -= notes;
        SuccessfulDispenses.Add(notes);
        return true;
    }

    public bool Load(NoteCounts notes)
    {
        if (++Calls >= failOnCall) return false;
        Stock += notes;
        return true;
    }
}

internal static class Domain
{
    public static readonly Denomination?[] JammedValues = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static readonly int[] Amounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return new NoteCounts(f, t, d);
    }

    public static IEnumerable<NoteCounts> Deposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return new NoteCounts(f, t, d);
    }

    /// <summary>Oracle indépendant : la combinaison qui fait exactement <paramref name="amount"/> avec le moins de billets.</summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts stock, Denomination? jammed)
    {
        int Avail(Denomination d) => jammed == d ? 0 : stock.Of(d);
        NoteCounts? best = null;
        for (var f = 0; f <= Avail(Denomination.Fifty); f++)
        for (var t = 0; t <= Avail(Denomination.Twenty); t++)
        for (var d = 0; d <= Avail(Denomination.Ten); d++)
        {
            var c = new NoteCounts(f, t, d);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total)) best = c;
        }
        return best;
    }

    public static string Show(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";
    public static string Show(Denomination? j) => j is null ? "aucune" : ((int)j).ToString();
}
