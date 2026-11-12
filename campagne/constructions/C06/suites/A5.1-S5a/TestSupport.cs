namespace Atm.Tests;

/// <summary>Outils communs : construction des cas et espion du module de billets.</summary>
internal static class TestKit
{
    public static NoteCounts Notes(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static readonly NoteCounts FullStock = new(5, 5, 5);

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(int fifties, int twenties, int tens) => new Operation.Deposit(new NoteCounts(fifties, twenties, tens));

    /// <summary>Exécute une opération sur une machine neuve branchée sur le vrai module, observé par un espion.</summary>
    public static (AtmResult Result, SpyDispenser Dispenser) Run(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new SpyDispenser(new MoneyDispenser(stock, jammed));
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawnToday), operation);

        return (result, dispenser);
    }
}

/// <summary>Délègue au vrai module et note chaque appel, sans changer son comportement.</summary>
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

/// <summary>
/// Module en panne : se comporte comme le vrai module, sauf que l'appel n° <c>failOnCall</c>
/// (Dispense ou Load, compté à partir de 1) rend <c>false</c> sans rien changer.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, int failOnCall, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser inner = new(stock, jammed);
    private int calls;

    public List<NoteCounts> Accepted { get; } = [];

    public int Calls => calls;

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        if (++calls == failOnCall || !inner.Dispense(notes))
        {
            return false;
        }

        Accepted.Add(notes);
        return true;
    }

    public bool Load(NoteCounts notes)
    {
        if (++calls == failOnCall || !inner.Load(notes))
        {
            return false;
        }

        Accepted.Add(notes);
        return true;
    }
}
