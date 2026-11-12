namespace Atm.Tests;

internal static class N
{
    public static NoteCounts Notes(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static readonly NoteCounts Full = new(5, 5, 5);

    public static AccountState Account(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(int fifties, int twenties, int tens) => new Operation.Deposit(new NoteCounts(fifties, twenties, tens));
}

/// <summary>Enveloppe un module et enregistre chaque appel à Dispense / Load.</summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<(NoteCounts Notes, bool Result)> Dispenses { get; } = [];

    public List<(NoteCounts Notes, bool Result)> Loads { get; } = [];

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public NoteCounts TotalDispensed =>
        Dispenses.Where(d => d.Result).Aggregate(NoteCounts.None, (acc, d) => acc + d.Notes);

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

    public static SpyDispenser Real(NoteCounts stock, Denomination? jammed = null) => new(new MoneyDispenser(stock, jammed));
}

/// <summary>
/// Faux module : annonce un stock et une cassette bloquée, mais ne réussit que les
/// <c>successfulDispenses</c> premiers passages (puis rend false), et rend <c>loadResult</c> au dépôt.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null, int successfulDispenses = 0, bool loadResult = false)
    : IMoneyDispenser
{
    public List<NoteCounts> Dispenses { get; } = [];

    public List<NoteCounts> Loads { get; } = [];

    public List<NoteCounts> SuccessfulDispenses { get; } = [];

    public NoteCounts Stock { get; private set; } = stock;

    public Denomination? Jammed { get; } = jammed;

    public bool Dispense(NoteCounts notes)
    {
        Dispenses.Add(notes);
        if (Dispenses.Count > successfulDispenses)
        {
            return false;
        }

        SuccessfulDispenses.Add(notes);
        Stock -= notes;
        return true;
    }

    public bool Load(NoteCounts notes)
    {
        Loads.Add(notes);
        if (loadResult)
        {
            Stock += notes;
        }

        return loadResult;
    }
}
