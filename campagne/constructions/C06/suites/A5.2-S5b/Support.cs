namespace Atm.Tests;

internal static class Support
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static readonly NoteCounts Full = new(5, 5, 5);

    public static (CashMachine Machine, MoneyDispenser Dispenser) Machine(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        return (new CashMachine(dispenser), dispenser);
    }

    public static AtmResult Withdraw(this CashMachine machine, int balance, int withdrawnToday, int amount) =>
        machine.Execute(new AccountState(balance, withdrawnToday), new Operation.Withdraw(amount));

    public static AtmResult Deposit(this CashMachine machine, int balance, int withdrawnToday, NoteCounts notes) =>
        machine.Execute(new AccountState(balance, withdrawnToday), new Operation.Deposit(notes));

    /// <summary>Vérifie qu'un refus ne change rien : statut, Notes vide, Fee 0, compte identique, stock intact.</summary>
    public static void AssertRefused(AtmResult result, AtmStatus expected, AccountState account, MoneyDispenser dispenser, NoteCounts stockBefore)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stockBefore, dispenser.Stock);
    }
}

/// <summary>
/// Module de billets factice pour simuler une panne : il se comporte comme le vrai module,
/// mais rend false (sans rien changer) à partir de l'appel numéro <c>failOnCall</c>.
/// </summary>
internal sealed class FailingDispenser(NoteCounts stock, Denomination? jammed, int failOnCall) : IMoneyDispenser
{
    private readonly MoneyDispenser real = new(stock, jammed);
    private int calls;

    public List<NoteCounts> Dispensed { get; } = [];
    public List<NoteCounts> Loaded { get; } = [];
    public int Calls => calls;

    public NoteCounts Stock => real.Stock;
    public Denomination? Jammed => real.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        if (++calls >= failOnCall) return false;
        var ok = real.Dispense(notes);
        if (ok) Dispensed.Add(notes);
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        if (++calls >= failOnCall) return false;
        var ok = real.Load(notes);
        if (ok) Loaded.Add(notes);
        return ok;
    }
}

/// <summary>Enveloppe le vrai module et enregistre chaque passage (comportement identique au vrai module).</summary>
internal sealed class RecordingDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser real = new(stock, jammed);

    public List<NoteCounts> DispenseCalls { get; } = [];
    public List<NoteCounts> LoadCalls { get; } = [];

    public NoteCounts Stock => real.Stock;
    public Denomination? Jammed => real.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        return real.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        return real.Load(notes);
    }
}
