namespace Atm.Tests;

/// <summary>Raccourcis communs aux tests.</summary>
internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation W(int amount) => new Operation.Withdraw(amount);

    public static Operation D(NoteCounts notes) => new Operation.Deposit(notes);

    public static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    /// <summary>Crée un vrai module enveloppé d'un espion, et la machine qui l'utilise.</summary>
    public static (CashMachine Machine, SpyDispenser Spy) Build(NoteCounts stock, Denomination? jammed = null)
    {
        var spy = new SpyDispenser(new MoneyDispenser(stock, jammed));
        return (new CashMachine(spy), spy);
    }
}

/// <summary>
/// Enveloppe transparente d'un module : délègue tout, et enregistre les appels.
/// Elle ne change pas le comportement du vrai module.
/// </summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<(NoteCounts Notes, bool Ok)> DispenseCalls { get; } = [];
    public List<(NoteCounts Notes, bool Ok)> LoadCalls { get; } = [];

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var ok = inner.Dispense(notes);
        DispenseCalls.Add((notes, ok));
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        var ok = inner.Load(notes);
        LoadCalls.Add((notes, ok));
        return ok;
    }

    public NoteCounts Dispensed => DispenseCalls.Where(c => c.Ok).Aggregate(NoteCounts.None, (a, c) => a + c.Notes);
    public NoteCounts Loaded => LoadCalls.Where(c => c.Ok).Aggregate(NoteCounts.None, (a, c) => a + c.Notes);
    public bool AnyMove => DispenseCalls.Any(c => c.Ok) || LoadCalls.Any(c => c.Ok);
}

/// <summary>
/// Module défaillant (hors domaine) : se comporte comme le vrai, sauf qu'il rend <c>false</c>
/// à partir du n-ième appel à Dispense (1 = le premier), ou sur Load.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed, int failOnDispenseCall = int.MaxValue, bool failOnLoad = false)
    : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);
    public List<(NoteCounts Notes, bool Ok)> DispenseCalls { get; } = [];
    public List<(NoteCounts Notes, bool Ok)> LoadCalls { get; } = [];

    public NoteCounts Stock => _inner.Stock;
    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var ok = DispenseCalls.Count + 1 < failOnDispenseCall && _inner.Dispense(notes);
        DispenseCalls.Add((notes, ok));
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        var ok = !failOnLoad && _inner.Load(notes);
        LoadCalls.Add((notes, ok));
        return ok;
    }

    public NoteCounts Dispensed => DispenseCalls.Where(c => c.Ok).Aggregate(NoteCounts.None, (a, c) => a + c.Notes);
}

/// <summary>
/// Oracle écrit directement depuis la spec, par force brute, indépendamment du code testé.
/// </summary>
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

    /// <summary>La combinaison qui fait exactement amount avec le moins de billets, ou null.</summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var n = 0; n <= available.Tens; n++)
        {
            var c = new NoteCounts(f, t, n);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total))
            {
                best = c;
            }
        }

        return best;
    }

    public static Expected Compute(AccountState account, NoteCounts stock, Denomination? jammed, Operation op)
    {
        Expected Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account, stock);

        switch (op)
        {
            case Operation.Withdraw(var amount):
            {
                if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
                if (account.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
                var fee = Fee(amount);
                if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds);
                if (FewestNotes(amount, Available(stock, jammed)) is not { } combo) return Refuse(AtmStatus.CannotDispense);
                return new(AtmStatus.Ok, combo, fee,
                    new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount), stock - combo);
            }
            case Operation.Deposit(var notes):
            {
                if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
                if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
                var after = stock + notes;
                if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);
                return new(AtmStatus.Ok, NoteCounts.None, 0,
                    account with { Balance = account.Balance + notes.Value }, after);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(op));
        }
    }
}
