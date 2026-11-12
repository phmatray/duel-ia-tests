namespace Atm.Tests;

/// <summary>Enveloppe le vrai module et note chaque appel, pour vérifier les passages et les mouvements de billets.</summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public SpyDispenser(NoteCounts stock, Denomination? jammed = null) : this(new MoneyDispenser(stock, jammed)) { }

    public List<(NoteCounts Notes, bool Result)> Dispenses { get; } = [];
    public List<(NoteCounts Notes, bool Result)> Loads { get; } = [];

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var result = inner.Dispense(notes);
        Dispenses.Add((notes, result));
        return result;
    }

    public bool Load(NoteCounts notes)
    {
        var result = inner.Load(notes);
        Loads.Add((notes, result));
        return result;
    }

    public NoteCounts DispensedTotal =>
        Dispenses.Where(d => d.Result).Aggregate(NoteCounts.None, (acc, d) => acc + d.Notes);
}

/// <summary>Module en panne : se comporte comme le vrai, mais rend false à partir du n-ième appel (compté à partir de 1).</summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null, int failDispenseFrom = 1, int failLoadFrom = 1)
    : IMoneyDispenser
{
    private readonly MoneyDispenser inner = new(stock, jammed);
    private int dispenseCalls;
    private int loadCalls;

    public List<NoteCounts> SuccessfulDispenses { get; } = [];
    public int DispenseCalls => dispenseCalls;
    public int LoadCalls => loadCalls;

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        dispenseCalls++;
        if (dispenseCalls >= failDispenseFrom)
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
        loadCalls++;
        return loadCalls < failLoadFrom && inner.Load(notes);
    }
}

internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday = 0) => new(balance, withdrawnToday);

    public static Operation W(int amount) => new Operation.Withdraw(amount);

    public static Operation D(int fifties, int twenties, int tens) => new Operation.Deposit(N(fifties, twenties, tens));

    public static Denomination? Jam(int value) => value == 0 ? null : (Denomination)value;

    /// <summary>Oracle indépendant : la combinaison au plus petit nombre de billets qui fait exactement amount, ou null.</summary>
    public static NoteCounts? Fewest(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var x = 0; x <= available.Tens; x++)
        {
            var c = N(f, t, x);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total))
            {
                best = c;
            }
        }

        return best;
    }

    public static NoteCounts WithoutJammed(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    public static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var x = 0; x <= 5; x++)
        {
            yield return N(f, t, x);
        }
    }

    public static readonly Denomination?[] AllJams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static readonly int[] AllAmounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var x = 0; x <= 1; x++)
        {
            yield return N(f, t, x);
        }
    }
}
