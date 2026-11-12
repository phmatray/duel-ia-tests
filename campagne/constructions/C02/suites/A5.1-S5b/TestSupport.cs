namespace Atm.Tests;

internal static class N
{
    public static NoteCounts Of(int fifties, int twenties, int tens) => new(fifties, twenties, tens);
}

/// <summary>Enveloppe le vrai module et enregistre chaque appel.</summary>
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

/// <summary>Module en panne : rend false à partir du n-ième appel (1-based).</summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed, int failDispenseAt = int.MaxValue, int failLoadAt = int.MaxValue)
    : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    public List<NoteCounts> Dispensed { get; } = [];
    public int DispenseCallCount { get; private set; }
    public int LoadCallCount { get; private set; }

    public NoteCounts Stock => _inner.Stock;
    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCallCount++;
        if (DispenseCallCount >= failDispenseAt) return false;
        var ok = _inner.Dispense(notes);
        if (ok) Dispensed.Add(notes);
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCallCount++;
        if (LoadCallCount >= failLoadAt) return false;
        return _inner.Load(notes);
    }
}

/// <summary>Modèle de référence écrit directement à partir de la spec.</summary>
internal static class Oracle
{
    public static AtmResult Execute(AccountState account, Operation op, NoteCounts stock, Denomination? jammed) => op switch
    {
        Operation.Withdraw w => Withdraw(account, w.Amount, stock, jammed),
        Operation.Deposit d => Deposit(account, d.Notes, stock, jammed),
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

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
        for (var d = 0; d <= available.Tens; d++)
        {
            var c = new NoteCounts(f, t, d);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total)) best = c;
        }
        return best;
    }

    private static AtmResult Refuse(AtmStatus s, AccountState a) => new(s, NoteCounts.None, 0, a);

    private static AtmResult Withdraw(AccountState a, int amount, NoteCounts stock, Denomination? jammed)
    {
        if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount, a);
        if (a.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded, a);
        var fee = Fee(amount);
        if (amount + fee > a.Balance) return Refuse(AtmStatus.InsufficientFunds, a);
        if (FewestNotes(amount, Available(stock, jammed)) is not { } notes) return Refuse(AtmStatus.CannotDispense, a);
        return new(AtmStatus.Ok, notes, fee, new(a.Balance - amount - fee, a.WithdrawnToday + amount));
    }

    private static AtmResult Deposit(AccountState a, NoteCounts notes, NoteCounts stock, Denomination? jammed)
    {
        if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount, a);
        if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed, a);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull, a);
        return new(AtmStatus.Ok, NoteCounts.None, 0, new(a.Balance + notes.Value, a.WithdrawnToday));
    }
}

internal static class Domain
{
    public static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static IEnumerable<int> Amounts() => new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));

    public static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return new(f, t, d);
    }

    public static IEnumerable<NoteCounts> Deposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return new(f, t, d);
    }
}
