namespace Atm;

/// <summary>Calculs purs sur les billets : choix de la combinaison et découpage en passages.</summary>
internal static class NotePlanner
{
    /// <summary>
    /// La combinaison qui fait exactement <paramref name="amount"/> avec le moins de billets
    /// pris dans <paramref name="available"/>, ou <c>null</c> s'il n'y en a aucune.
    /// </summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        foreach (var candidate in ExactCombinations(amount, available))
        {
            if (best is null || candidate.Total < best.Value.Total)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Découpe <paramref name="notes"/> en passages d'au plus <paramref name="maxPerPass"/> billets.</summary>
    public static IEnumerable<NoteCounts> SplitIntoPasses(NoteCounts notes, int maxPerPass)
    {
        var remaining = notes;
        while (remaining.Total > 0)
        {
            var pass = TakeUpTo(maxPerPass, remaining);
            remaining -= pass;
            yield return pass;
        }
    }

    /// <summary>
    /// Toutes les combinaisons qui font exactement <paramref name="amount"/>,
    /// des plus riches en billets de 50 aux plus pauvres, puis de même pour les billets de 20.
    /// </summary>
    private static IEnumerable<NoteCounts> ExactCombinations(int amount, NoteCounts available)
    {
        for (var fifties = Math.Min(available.Fifties, amount / 50); fifties >= 0; fifties--)
        {
            var afterFifties = amount - fifties * 50;
            for (var twenties = Math.Min(available.Twenties, afterFifties / 20); twenties >= 0; twenties--)
            {
                var rest = afterFifties - twenties * 20;
                var tens = rest / 10;
                if (rest % 10 == 0 && tens <= available.Tens)
                {
                    yield return new NoteCounts(fifties, twenties, tens);
                }
            }
        }
    }

    /// <summary>Prend au plus <paramref name="count"/> billets, les plus grosses coupures d'abord.</summary>
    private static NoteCounts TakeUpTo(int count, NoteCounts notes)
    {
        var fifties = Math.Min(count, notes.Fifties);
        var twenties = Math.Min(count - fifties, notes.Twenties);
        var tens = Math.Min(count - fifties - twenties, notes.Tens);
        return new NoteCounts(fifties, twenties, tens);
    }
}
