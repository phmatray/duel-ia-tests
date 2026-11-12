namespace Atm.Tests;

/// <summary>Raccourcis communs aux tests.</summary>
internal static class T
{
    public static NoteCounts N(int fifties = 0, int twenties = 0, int tens = 0) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Denomination? Jam(int jammed) => jammed switch
    {
        0 => null,
        50 => Denomination.Fifty,
        20 => Denomination.Twenty,
        10 => Denomination.Ten,
        _ => throw new ArgumentOutOfRangeException(nameof(jammed)),
    };

    /// <summary>Construit une machine sur le vrai module, enveloppé dans un espion.</summary>
    public static (CashMachine Machine, SpyDispenser Spy) Machine(NoteCounts stock, Denomination? jammed = null)
    {
        var spy = new SpyDispenser(new MoneyDispenser(stock, jammed));
        return (new CashMachine(spy), spy);
    }

    public static AtmResult Withdraw(AccountState account, int amount, NoteCounts stock, Denomination? jammed = null) =>
        Machine(stock, jammed).Machine.Execute(account, new Operation.Withdraw(amount));

    public static AtmResult Deposit(AccountState account, NoteCounts notes, NoteCounts stock, Denomination? jammed = null) =>
        Machine(stock, jammed).Machine.Execute(account, new Operation.Deposit(notes));

    public static readonly NoteCounts Full = new(5, 5, 5);
}

/// <summary>Délègue au vrai module et enregistre chaque appel, pour vérifier que les billets passent par lui.</summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<(NoteCounts Notes, bool Result)> Dispenses { get; } = [];

    public List<(NoteCounts Notes, bool Result)> Loads { get; } = [];

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public NoteCounts Dispensed => Dispenses.Where(d => d.Result).Aggregate(NoteCounts.None, (a, d) => a + d.Notes);

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
}

/// <summary>
/// Module en panne : se comporte comme le vrai, sauf que le passage n° <c>failOnDispense</c> (à partir de 1)
/// ou le dépôt rend <c>false</c> sans rien changer.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null, int failOnDispense = 0, bool failOnLoad = false)
    : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    public int DispenseCalls { get; private set; }

    public int DispenseCallsAfterFailure { get; private set; }

    public int LoadCalls { get; private set; }

    private bool _failed;

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls++;
        if (_failed)
        {
            DispenseCallsAfterFailure++;
        }

        if (DispenseCalls == failOnDispense)
        {
            _failed = true;
            return false;
        }

        return _inner.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls++;
        return !failOnLoad && _inner.Load(notes);
    }
}

/// <summary>Le résultat attendu, calculé directement à partir de la spec.</summary>
internal static class Oracle
{
    public const int DailyLimit = 200;
    public const int Capacity = 5;

    public static int Fee(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    /// <summary>La combinaison exacte qui compte le moins de billets, par recherche exhaustive.</summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var c = new NoteCounts(f, t, d);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total))
            {
                best = c;
            }
        }

        return best;
    }

    public static (AtmResult Result, NoteCounts StockAfter) Withdraw(AccountState account, int amount, NoteCounts stock, Denomination? jammed)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus s) => (new AtmResult(s, NoteCounts.None, 0, account), stock);

        if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > DailyLimit) return Refuse(AtmStatus.DailyLimitExceeded);
        var fee = Fee(amount);
        if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds);
        if (FewestNotes(amount, Available(stock, jammed)) is not { } notes) return Refuse(AtmStatus.CannotDispense);

        return (new AtmResult(AtmStatus.Ok, notes, fee, new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount)),
            stock - notes);
    }

    public static (AtmResult Result, NoteCounts StockAfter) Deposit(AccountState account, NoteCounts notes, NoteCounts stock, Denomination? jammed)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus s) => (new AtmResult(s, NoteCounts.None, 0, account), stock);

        if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > Capacity || after.Twenties > Capacity || after.Tens > Capacity) return Refuse(AtmStatus.CassetteFull);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }), after);
    }
}
