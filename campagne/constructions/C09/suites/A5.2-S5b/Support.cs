namespace Atm.Tests;

/// <summary>Raccourcis pour écrire les tests.</summary>
internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(NoteCounts notes) => new Operation.Deposit(notes);

    public static readonly Denomination?[] JammedValues = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static readonly int[] AmountDomain =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static IEnumerable<NoteCounts> DepositDomain()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var n = 0; n <= 1; n++)
            yield return N(f, t, n);
    }

    public static IEnumerable<NoteCounts> StockDomain()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
            yield return N(f, t, n);
    }

    /// <summary>Exécute une opération sur une vraie machine branchée sur un vrai module espionné.</summary>
    public static (AtmResult Result, SpyDispenser Dispenser) Run(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new SpyDispenser(new MoneyDispenser(stock, jammed));
        var result = new CashMachine(dispenser).Execute(account, operation);
        return (result, dispenser);
    }
}

/// <summary>Enveloppe un module réel et enregistre tous les appels.</summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<(NoteCounts Notes, bool Ok)> DispenseCalls { get; } = [];
    public List<(NoteCounts Notes, bool Ok)> LoadCalls { get; } = [];

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public NoteCounts Dispensed =>
        DispenseCalls.Where(c => c.Ok).Aggregate(NoteCounts.None, (acc, c) => acc + c.Notes);

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
}

/// <summary>
/// Module en panne : se comporte comme le vrai, mais rend <c>false</c> (sans rien changer)
/// à partir du n-ième appel de Dispense ou de Load (1 = dès le premier).
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed, int failDispenseFromCall = int.MaxValue,
    int failLoadFromCall = int.MaxValue) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    public List<(NoteCounts Notes, bool Ok)> DispenseCalls { get; } = [];
    public List<(NoteCounts Notes, bool Ok)> LoadCalls { get; } = [];

    public NoteCounts Stock => _inner.Stock;
    public Denomination? Jammed => _inner.Jammed;

    public NoteCounts Dispensed =>
        DispenseCalls.Where(c => c.Ok).Aggregate(NoteCounts.None, (acc, c) => acc + c.Notes);

    public bool Dispense(NoteCounts notes)
    {
        var ok = DispenseCalls.Count + 1 < failDispenseFromCall && _inner.Dispense(notes);
        DispenseCalls.Add((notes, ok));
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        var ok = LoadCalls.Count + 1 < failLoadFromCall && _inner.Load(notes);
        LoadCalls.Add((notes, ok));
        return ok;
    }
}

/// <summary>Oracle écrit directement à partir de la spec, utilisé par les tests exhaustifs.</summary>
internal static class Oracle
{
    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    public static int Fee(int amount) => amount > 100 ? 5 : 0;

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

    public static AtmStatus WithdrawStatus(AccountState account, int amount, NoteCounts stock, Denomination? jammed)
    {
        if (amount <= 0 || amount % 10 != 0) return AtmStatus.InvalidAmount;
        if (account.WithdrawnToday + amount > 200) return AtmStatus.DailyLimitExceeded;
        if (amount + Fee(amount) > account.Balance) return AtmStatus.InsufficientFunds;
        if (FewestNotes(amount, Available(stock, jammed)) is null) return AtmStatus.CannotDispense;
        return AtmStatus.Ok;
    }

    public static AtmStatus DepositStatus(NoteCounts notes, NoteCounts stock, Denomination? jammed)
    {
        if (notes.Total == 0) return AtmStatus.InvalidAmount;
        if (jammed is { } j && notes.Of(j) > 0) return AtmStatus.CassetteJammed;
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return AtmStatus.CassetteFull;
        return AtmStatus.Ok;
    }
}
