namespace Atm.Tests.Support;

/// <summary>Raccourcis pour écrire les tests dans la notation de la spec (« 50×a 20×b 10×c »).</summary>
public static class Notes
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static readonly Denomination?[] JamCases = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
        {
            yield return N(f, t, n);
        }
    }

    /// <summary>Dépôts du domaine : 0 ou 1 billet par coupure.</summary>
    public static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var n = 0; n <= 1; n++)
        {
            yield return N(f, t, n);
        }
    }

    public static readonly int[] DomainAmounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static readonly int[] DomainBalances = Enumerable.Range(0, 51).Select(i => i * 5).ToArray();

    public static readonly int[] DomainWithdrawnToday = Enumerable.Range(0, 21).Select(i => i * 10).ToArray();

    public static string Show(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";

    public static string Show(Denomination? jam) => jam is { } d ? ((int)d).ToString() : "aucune";
}

/// <summary>Enveloppe le vrai module et note chaque appel, pour vérifier les passages et les mouvements de billets.</summary>
public sealed class RecordingDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    public List<(NoteCounts Notes, bool Ok)> DispenseCalls { get; } = [];

    public List<(NoteCounts Notes, bool Ok)> LoadCalls { get; } = [];

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var ok = _inner.Dispense(notes);
        DispenseCalls.Add((notes, ok));
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        var ok = _inner.Load(notes);
        LoadCalls.Add((notes, ok));
        return ok;
    }
}

/// <summary>
/// Module en panne : se comporte comme le vrai, sauf qu'il rend <c>false</c> (sans rien changer)
/// au n-ième appel de <c>Dispense</c> ou de <c>Load</c>. Note tous les appels.
/// </summary>
public sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    /// <summary>Numéro (à partir de 1) de l'appel à <c>Dispense</c> qui échoue ; 0 = aucun.</summary>
    public int FailDispenseAtCall { get; init; }

    /// <summary>Numéro (à partir de 1) de l'appel à <c>Load</c> qui échoue ; 0 = aucun.</summary>
    public int FailLoadAtCall { get; init; }

    public List<(NoteCounts Notes, bool Ok)> DispenseCalls { get; } = [];

    public List<(NoteCounts Notes, bool Ok)> LoadCalls { get; } = [];

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var ok = DispenseCalls.Count + 1 != FailDispenseAtCall && _inner.Dispense(notes);
        DispenseCalls.Add((notes, ok));
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        var ok = LoadCalls.Count + 1 != FailLoadAtCall && _inner.Load(notes);
        LoadCalls.Add((notes, ok));
        return ok;
    }
}

/// <summary>Modèle de référence écrit directement à partir de la spec.</summary>
public static class Oracle
{
    public static int FeeFor(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    /// <summary>Toutes les combinaisons de billets disponibles qui font exactement <paramref name="amount"/> avec le moins de billets.</summary>
    public static List<NoteCounts> FewestNotes(int amount, NoteCounts available)
    {
        var all = new List<NoteCounts>();
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var n = 0; n <= available.Tens; n++)
        {
            if (f * 50 + t * 20 + n * 10 == amount)
            {
                all.Add(new NoteCounts(f, t, n));
            }
        }

        if (all.Count == 0)
        {
            return all;
        }

        var min = all.Min(c => c.Total);
        return all.Where(c => c.Total == min).ToList();
    }

    public readonly record struct Expected(AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account, NoteCounts StockAfter);

    public static Expected Withdraw(AccountState account, int amount, NoteCounts stock, Denomination? jammed)
    {
        Expected Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account, stock);

        if (amount <= 0 || amount % 10 != 0)
        {
            return Refuse(AtmStatus.InvalidAmount);
        }

        if (account.WithdrawnToday + amount > 200)
        {
            return Refuse(AtmStatus.DailyLimitExceeded);
        }

        var fee = FeeFor(amount);
        if (amount + fee > account.Balance)
        {
            return Refuse(AtmStatus.InsufficientFunds);
        }

        var best = FewestNotes(amount, Available(stock, jammed));
        if (best.Count == 0)
        {
            return Refuse(AtmStatus.CannotDispense);
        }

        if (best.Count != 1)
        {
            throw new InvalidOperationException("La spec affirme que la combinaison est unique.");
        }

        var notes = best[0];
        return new Expected(
            AtmStatus.Ok,
            notes,
            fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount),
            stock - notes);
    }

    public static Expected Deposit(AccountState account, NoteCounts notes, NoteCounts stock, Denomination? jammed)
    {
        Expected Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account, stock);

        if (notes.Total == 0)
        {
            return Refuse(AtmStatus.InvalidAmount);
        }

        if (jammed is { } j && notes.Of(j) > 0)
        {
            return Refuse(AtmStatus.CassetteJammed);
        }

        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5)
        {
            return Refuse(AtmStatus.CassetteFull);
        }

        return new Expected(
            AtmStatus.Ok,
            NoteCounts.None,
            0,
            account with { Balance = account.Balance + notes.Value },
            after);
    }
}
