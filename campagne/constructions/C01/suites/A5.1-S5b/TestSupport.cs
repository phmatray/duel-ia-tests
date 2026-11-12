namespace Atm.Tests;

/// <summary>Enveloppe le vrai module et enregistre chaque appel, pour vérifier les passages.</summary>
public sealed class RecordingDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<NoteCounts> Dispensed { get; } = [];
    public List<NoteCounts> Loaded { get; } = [];

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        Dispensed.Add(notes);
        return inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        Loaded.Add(notes);
        return inner.Load(notes);
    }

    public static RecordingDispenser Real(NoteCounts stock, Denomination? jammed = null) =>
        new(new MoneyDispenser(stock, jammed));
}

/// <summary>Module en panne : rend false à partir du n-ième appel (1 = premier appel).</summary>
public sealed class FaultyDispenser(NoteCounts stock, int failDispenseFromCall = int.MaxValue,
    int failLoadFromCall = int.MaxValue, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);
    private int _dispenseCalls;
    private int _loadCalls;

    public List<NoteCounts> Dispensed { get; } = [];
    public List<NoteCounts> SucceededDispenses { get; } = [];

    public NoteCounts Stock => _inner.Stock;
    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        _dispenseCalls++;
        Dispensed.Add(notes);
        if (_dispenseCalls >= failDispenseFromCall) return false;
        var ok = _inner.Dispense(notes);
        if (ok) SucceededDispenses.Add(notes);
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        _loadCalls++;
        if (_loadCalls >= failLoadFromCall) return false;
        return _inner.Load(notes);
    }
}

/// <summary>Oracle écrit d'après la spec, uniquement pour le domaine.</summary>
public static class SpecOracle
{
    public const int DailyLimit = 200;

    public static int FeeFor(int amount) => amount > 100 ? 5 : 0;

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
            if (f * 50 + t * 20 + d * 10 != amount) continue;
            var candidate = new NoteCounts(f, t, d);
            if (best is null || candidate.Total < best.Value.Total) best = candidate;
        }
        return best;
    }

    public static AtmResult Withdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > DailyLimit) return Refuse(AtmStatus.DailyLimitExceeded);
        var fee = FeeFor(amount);
        if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds);
        var combo = FewestNotes(amount, Available(stock, jammed));
        if (combo is null) return Refuse(AtmStatus.CannotDispense);
        return new(AtmStatus.Ok, combo.Value, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    public static AtmResult Deposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);
        return new(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value });
    }
}

public static class Domain
{
    public static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return new NoteCounts(f, t, d);
    }

    public static IEnumerable<int> Amounts()
    {
        yield return -10;
        yield return 0;
        yield return 5;
        for (var a = 10; a <= 220; a += 10) yield return a;
    }

    public static IEnumerable<NoteCounts> Deposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return new NoteCounts(f, t, d);
    }

    public static IEnumerable<int> Balances()
    {
        for (var b = 0; b <= 250; b += 5) yield return b;
    }

    public static IEnumerable<int> WithdrawnTodays()
    {
        for (var w = 0; w <= 200; w += 10) yield return w;
    }
}
