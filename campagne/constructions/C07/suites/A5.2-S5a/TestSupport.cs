namespace Atm.Tests;

/// <summary>Outils communs aux tests : construction de la machine, opérations, module espion ou défaillant.</summary>
internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(NoteCounts notes) => new Operation.Deposit(notes);

    public static (CashMachine Machine, MoneyDispenser Dispenser) Real(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        return (new CashMachine(dispenser), dispenser);
    }

    public static (CashMachine Machine, SpyDispenser Spy) Spied(NoteCounts stock, Denomination? jammed = null)
    {
        var spy = new SpyDispenser(new MoneyDispenser(stock, jammed));
        return (new CashMachine(spy), spy);
    }

    public static Denomination? Jam(int code) => code switch
    {
        50 => Denomination.Fifty,
        20 => Denomination.Twenty,
        10 => Denomination.Ten,
        _ => null,
    };

    /// <summary>Le résultat d'un refus : rien ne change.</summary>
    public static void AssertRefused(AtmResult result, AtmStatus status, AccountState account)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
    }
}

/// <summary>Enveloppe le vrai module et enregistre chaque appel.</summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
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
}

/// <summary>Module qui se comporte normalement, sauf qu'il rend <c>false</c> à partir d'un appel donné.</summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    /// <summary>Numéro (à partir de 1) du premier appel à <see cref="Dispense"/> qui échoue ; 0 = jamais.</summary>
    public int FailDispenseFromCall { get; init; }

    public bool FailLoad { get; init; }

    public List<NoteCounts> Dispensed { get; } = [];

    public List<NoteCounts> Loaded { get; } = [];

    public NoteCounts Stock { get; private set; } = stock;

    public Denomination? Jammed { get; } = jammed;

    public bool Dispense(NoteCounts notes)
    {
        if (FailDispenseFromCall > 0 && Dispensed.Count + 1 >= FailDispenseFromCall)
        {
            return false;
        }

        Dispensed.Add(notes);
        Stock -= notes;
        return true;
    }

    public bool Load(NoteCounts notes)
    {
        if (FailLoad)
        {
            return false;
        }

        Loaded.Add(notes);
        Stock += notes;
        return true;
    }
}
