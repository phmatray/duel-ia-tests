namespace Atm.Tests;

internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation W(int amount) => new Operation.Withdraw(amount);

    public static Operation D(NoteCounts notes) => new Operation.Deposit(notes);

    public static readonly Denomination?[] AllJams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static readonly int[] DomainAmounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
            for (var t = 0; t <= 5; t++)
                for (var n = 0; n <= 5; n++)
                    yield return N(f, t, n);
    }

    public static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
            for (var t = 0; t <= 1; t++)
                for (var n = 0; n <= 1; n++)
                    yield return N(f, t, n);
    }

    /// <summary>Exécute une opération avec le vrai module, enveloppé dans un espion.</summary>
    public static (AtmResult Result, SpyDispenser Spy) Run(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var spy = new SpyDispenser(new MoneyDispenser(stock, jammed));
        var machine = new CashMachine(spy);
        return (machine.Execute(account, operation), spy);
    }
}

/// <summary>Enveloppe un module et journalise tous les appels.</summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<(NoteCounts Notes, bool Ok)> Dispensed { get; } = [];
    public List<(NoteCounts Notes, bool Ok)> Loaded { get; } = [];

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var ok = inner.Dispense(notes);
        Dispensed.Add((notes, ok));
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        var ok = inner.Load(notes);
        Loaded.Add((notes, ok));
        return ok;
    }

    public NoteCounts TotalDispensed =>
        Dispensed.Where(d => d.Ok).Aggregate(NoteCounts.None, (acc, d) => acc + d.Notes);
}

/// <summary>Module en panne : accepte les <c>okDispenses</c> premiers passages, puis rend false.</summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null, int okDispenses = 0, bool loadOk = false)
    : IMoneyDispenser
{
    public NoteCounts Stock { get; private set; } = stock;
    public Denomination? Jammed { get; } = jammed;
    public List<NoteCounts> DispenseCalls { get; } = [];
    public List<NoteCounts> LoadCalls { get; } = [];

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (DispenseCalls.Count > okDispenses) return false;
        Stock -= notes;
        return true;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        if (!loadOk) return false;
        Stock += notes;
        return true;
    }
}

/// <summary>Modèle de référence, écrit directement d'après spec.md.</summary>
internal static class Oracle
{
    public sealed record Expected(AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account, NoteCounts StockAfter);

    public static int Fee(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
            for (var t = 0; t <= available.Twenties; t++)
                for (var n = 0; n <= available.Tens; n++)
                {
                    var c = new NoteCounts(f, t, n);
                    if (c.Value == amount && (best is null || c.Total < best.Value.Total)) best = c;
                }
        return best;
    }

    public static Expected Withdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        Expected Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account, stock);

        if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
        var fee = Fee(amount);
        if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds);
        if (FewestNotes(amount, Available(stock, jammed)) is not { } notes) return Refuse(AtmStatus.CannotDispense);

        return new(AtmStatus.Ok, notes, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount), stock - notes);
    }

    public static Expected Deposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        Expected Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account, stock);

        if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);

        return new(AtmStatus.Ok, NoteCounts.None, 0,
            account with { Balance = account.Balance + notes.Value }, after);
    }
}
