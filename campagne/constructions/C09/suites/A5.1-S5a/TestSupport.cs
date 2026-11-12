namespace Atm.Tests;

/// <summary>Raccourcis communs aux tests.</summary>
internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(NoteCounts notes) => new Operation.Deposit(notes);

    public static readonly NoteCounts Full = new(5, 5, 5);

    public static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    /// <summary>Les montants du domaine : -10, 0, 5 et les multiples de 10 de 10 à 220.</summary>
    public static IEnumerable<int> DomainAmounts()
    {
        yield return -10;
        yield return 0;
        yield return 5;
        for (var a = 10; a <= 220; a += 10)
        {
            yield return a;
        }
    }

    public static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var c = 0; c <= 5; c++)
        {
            yield return new NoteCounts(f, t, c);
        }
    }

    /// <summary>Exécute une opération sur un vrai module espionné.</summary>
    public static (AtmResult Result, SpyDispenser Dispenser) Run(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new SpyDispenser(new MoneyDispenser(stock, jammed));
        var result = new CashMachine(dispenser).Execute(account, operation);
        return (result, dispenser);
    }
}

/// <summary>Enveloppe le vrai module et note chaque appel.</summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<NoteCounts> Dispensed { get; } = [];
    public List<NoteCounts> Loaded { get; } = [];
    public List<bool> DispenseResults { get; } = [];
    public List<bool> LoadResults { get; } = [];

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        Dispensed.Add(notes);
        var ok = inner.Dispense(notes);
        DispenseResults.Add(ok);
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        Loaded.Add(notes);
        var ok = inner.Load(notes);
        LoadResults.Add(ok);
        return ok;
    }
}

/// <summary>
/// Un module en panne : il annonce un stock et une cassette bloquée, accepte les
/// <c>Dispense</c> jusqu'au n-ième (exclu) puis rend <c>false</c> ; <c>Load</c> rend <c>LoadSucceeds</c>.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    /// <summary>Numéro (1-based) du premier appel à Dispense qui échoue. 1 = échoue tout de suite.</summary>
    public int FailOnDispenseCall { get; init; } = 1;

    public bool LoadSucceeds { get; init; }

    public List<NoteCounts> DispenseCalls { get; } = [];
    public List<NoteCounts> LoadCalls { get; } = [];

    public NoteCounts Stock { get; private set; } = stock;
    public Denomination? Jammed { get; } = jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (DispenseCalls.Count >= FailOnDispenseCall)
        {
            return false;
        }

        Stock -= notes;
        return true;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        if (!LoadSucceeds)
        {
            return false;
        }

        Stock += notes;
        return true;
    }
}

/// <summary>Oracle indépendant, écrit directement à partir de la spec, pour le retrait.</summary>
internal static class Oracle
{
    public static int Fee(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    /// <summary>Recherche exhaustive de la combinaison exacte qui compte le moins de billets.</summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var c = 0; c <= available.Tens; c++)
        {
            var candidate = new NoteCounts(f, t, c);
            if (candidate.Value == amount && (best is null || candidate.Total < best.Value.Total))
            {
                best = candidate;
            }
        }

        return best;
    }

    public static AtmResult Withdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
        var fee = Fee(amount);
        if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds);
        if (FewestNotes(amount, Available(stock, jammed)) is not { } notes) return Refuse(AtmStatus.CannotDispense);

        return new AtmResult(AtmStatus.Ok, notes, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    public static AtmResult Deposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value });
    }
}
