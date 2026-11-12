namespace Atm;

/// <summary>Les calculs sur les billets d'un retrait : quelle combinaison sortir, et en quels passages.</summary>
internal static class NotePlanner
{
    private const int Fifty = (int)Denomination.Fifty;
    private const int Twenty = (int)Denomination.Twenty;
    private const int Ten = (int)Denomination.Ten;

    /// <summary>
    /// La combinaison prise dans <paramref name="available"/> qui fait exactement <paramref name="amount"/> avec le moins
    /// de billets, ou <c>null</c> s'il n'y en a aucune. À nombre égal, la première énumérée l'emporte.
    /// </summary>
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
    /// Découpe les billets en passages non vides d'au plus <paramref name="maxNotesPerPass"/> billets,
    /// en prenant d'abord les 50, puis les 20, puis les 10.
    /// </summary>
    public static IEnumerable<NoteCounts> SplitIntoPasses(NoteCounts notes, int maxNotesPerPass)
    {
        var remaining = notes;
        while (remaining.Total > 0)
        {
            var room = maxNotesPerPass;
            var fifties = Take(remaining.Fifties, ref room);
            var twenties = Take(remaining.Twenties, ref room);
            var tens = Take(remaining.Tens, ref room);

            var pass = new NoteCounts(fifties, twenties, tens);
            remaining -= pass;

            yield return pass;
        }
    }

    /// <summary>
    /// Toutes les combinaisons disponibles qui font exactement <paramref name="amount"/>,
    /// du plus grand nombre de 50 au plus petit, puis du plus grand nombre de 20 au plus petit.
    /// </summary>
    private static IEnumerable<NoteCounts> ExactCombinations(int amount, NoteCounts available)
    {
        for (var fifties = MaxUsable(available.Fifties, Fifty, amount); fifties >= 0; fifties--)
        {
            var afterFifties = amount - fifties * Fifty;
            for (var twenties = MaxUsable(available.Twenties, Twenty, afterFifties); twenties >= 0; twenties--)
            {
                var rest = afterFifties - twenties * Twenty;
                if (rest % Ten == 0 && rest / Ten <= available.Tens)
                {
                    yield return new NoteCounts(fifties, twenties, rest / Ten);
                }
            }
        }
    }

    /// <summary>Le plus de billets de cette valeur qu'on peut utiliser : ni plus que le stock, ni plus que le montant.</summary>
    private static int MaxUsable(int inStock, int noteValue, int amount) => Math.Min(Math.Max(inStock, 0), amount / noteValue);

    /// <summary>Prend jusqu'à <paramref name="count"/> billets dans la place restante du passage.</summary>
    private static int Take(int count, ref int room)
    {
        var taken = Math.Min(count, room);
        room -= taken;

        return taken;
    }
}
