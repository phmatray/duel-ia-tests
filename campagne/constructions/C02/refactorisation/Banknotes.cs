namespace Atm;

/// <summary>Calculs sur les billets : ce qui est disponible, la combinaison à sortir, son découpage en passages.</summary>
internal static class Banknotes
{
    private const int MaxNotesPerPass = 4;

    /// <summary>Le stock, sans la cassette bloquée, qui compte pour zéro.</summary>
    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => new(
        CountUnlessJammed(stock.Fifties, Denomination.Fifty, jammed),
        CountUnlessJammed(stock.Twenties, Denomination.Twenty, jammed),
        CountUnlessJammed(stock.Tens, Denomination.Ten, jammed));

    private static int CountUnlessJammed(int count, Denomination cassette, Denomination? jammed) =>
        cassette == jammed ? 0 : Math.Max(0, count);

    /// <summary>La combinaison qui fait exactement <paramref name="amount"/> avec le moins de billets, ou null.</summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        foreach (var candidate in ExactCombinations(amount, available))
        {
            if (best is not { } current || candidate.Total < current.Total)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Toutes les combinaisons de billets disponibles qui font exactement <paramref name="amount"/>,
    /// en commençant par celles qui comptent le plus de billets de 50, puis de 20.
    /// </summary>
    private static IEnumerable<NoteCounts> ExactCombinations(int amount, NoteCounts available)
    {
        for (var fifties = Math.Min(available.Fifties, amount / 50); fifties >= 0; fifties--)
        {
            var afterFifties = amount - fifties * 50;
            for (var twenties = Math.Min(available.Twenties, afterFifties / 20); twenties >= 0; twenties--)
            {
                var rest = afterFifties - twenties * 20;
                if (rest % 10 == 0 && rest / 10 <= available.Tens)
                {
                    yield return new NoteCounts(fifties, twenties, rest / 10);
                }
            }
        }
    }

    /// <summary>Découpe les billets en passages d'au plus 4 billets, en remplissant d'abord avec les 50, puis les 20, puis les 10.</summary>
    public static IEnumerable<NoteCounts> Passes(NoteCounts notes)
    {
        var remaining = notes;
        while (remaining.Total > 0)
        {
            var fifties = Math.Min(MaxNotesPerPass, remaining.Fifties);
            var twenties = Math.Min(MaxNotesPerPass - fifties, remaining.Twenties);
            var tens = Math.Min(MaxNotesPerPass - fifties - twenties, remaining.Tens);

            var pass = new NoteCounts(fifties, twenties, tens);
            remaining -= pass;

            yield return pass;
        }
    }
}
