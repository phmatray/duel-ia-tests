namespace Atm.Tests;

/// <summary>Raccourcis pour écrire les tests dans les termes de la spec.</summary>
internal static class T
{
    public static NoteCounts Notes(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Account(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(int fifties, int twenties, int tens) => new Operation.Deposit(new(fifties, twenties, tens));

    public static readonly NoteCounts Full = new(5, 5, 5);

    public static readonly NoteCounts Empty = NoteCounts.None;

    /// <summary>Construit la machine sur le vrai module, comme le prévoit le domaine.</summary>
    public static (CashMachine Machine, MoneyDispenser Dispenser) Real(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        return (new CashMachine(dispenser), dispenser);
    }

    /// <summary>Construit la machine sur un espion qui enveloppe le vrai module.</summary>
    public static (CashMachine Machine, SpyDispenser Spy) Spied(NoteCounts stock, Denomination? jammed = null, int? failOnCall = null)
    {
        var spy = new SpyDispenser(new MoneyDispenser(stock, jammed), failOnCall);
        return (new CashMachine(spy), spy);
    }

    public static void AssertRefused(AtmResult result, AtmStatus expected, AccountState before)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(before, result.Account);
    }
}

/// <summary>
/// Enveloppe le vrai module et enregistre chaque appel. Peut simuler une panne : l'appel numéro
/// <c>failOnCall</c> (à partir de 1, Dispense et Load confondus) rend <c>false</c> sans rien changer.
/// </summary>
internal sealed class SpyDispenser(IMoneyDispenser inner, int? failOnCall = null) : IMoneyDispenser
{
    public List<NoteCounts> Dispensed { get; } = [];

    public List<NoteCounts> Loaded { get; } = [];

    public int Calls { get; private set; }

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        Calls++;
        if (Calls == failOnCall)
        {
            return false;
        }

        var ok = inner.Dispense(notes);
        if (ok)
        {
            Dispensed.Add(notes);
        }

        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        Calls++;
        if (Calls == failOnCall)
        {
            return false;
        }

        var ok = inner.Load(notes);
        if (ok)
        {
            Loaded.Add(notes);
        }

        return ok;
    }
}
