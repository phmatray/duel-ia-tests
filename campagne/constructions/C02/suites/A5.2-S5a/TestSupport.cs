namespace Atm.Tests;

/// <summary>Enveloppe le vrai module et note chaque appel, sans rien changer à son comportement.</summary>
internal sealed class SpyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    public List<NoteCounts> DispenseCalls { get; } = [];

    public List<NoteCounts> LoadCalls { get; } = [];

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        return _inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        return _inner.Load(notes);
    }
}

/// <summary>Module scripté pour simuler une panne : rend les réponses prévues, dans l'ordre.</summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed, params bool[] answers) : IMoneyDispenser
{
    private int _call;

    public List<NoteCounts> DispenseCalls { get; } = [];

    public List<NoteCounts> LoadCalls { get; } = [];

    public NoteCounts Stock { get; private set; } = stock;

    public Denomination? Jammed { get; } = jammed;

    private bool Next() => _call < answers.Length ? answers[_call++] : true;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (!Next())
        {
            return false;
        }

        Stock -= notes;
        return true;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        if (!Next())
        {
            return false;
        }

        Stock += notes;
        return true;
    }
}

internal static class Atm
{
    public static NoteCounts Notes(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Account(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(int fifties, int twenties, int tens) => new Operation.Deposit(new(fifties, twenties, tens));

    public static NoteCounts Full => new(5, 5, 5);

    /// <summary>Les valeurs de cassette bloquée du domaine.</summary>
    public static IEnumerable<Denomination?> JammedValues =>
        [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    /// <summary>Tous les stocks du domaine : 0 à 5 billets par cassette.</summary>
    public static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
        {
            yield return new NoteCounts(f, t, d);
        }
    }

    /// <summary>Oracle par force brute : la combinaison au plus petit nombre de billets, ou null.</summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var candidate = new NoteCounts(f, t, d);
            if (candidate.Value == amount && (best is null || candidate.Total < best.Value.Total))
            {
                best = candidate;
            }
        }

        return best;
    }

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => new(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    public static void AssertRefused(AtmStatus expected, AccountState before, NoteCounts stockBefore, AtmResult result, SpyDispenser dispenser)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(before, result.Account);
        Assert.Equal(stockBefore, dispenser.Stock);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
    }
}
