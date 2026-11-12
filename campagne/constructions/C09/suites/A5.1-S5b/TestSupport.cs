namespace Atm.Tests;

internal static class N
{
    /// <summary>Raccourci : 50×f 20×t 10×n.</summary>
    public static NoteCounts C(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static readonly NoteCounts Full = new(5, 5, 5);
}

internal static class Run
{
    public static (AtmResult Result, MoneyDispenser Dispenser) Withdraw(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawnToday), new Operation.Withdraw(amount));
        return (result, dispenser);
    }

    public static (AtmResult Result, MoneyDispenser Dispenser) Deposit(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawnToday), new Operation.Deposit(notes));
        return (result, dispenser);
    }
}

internal static class AssertAtm
{
    /// <summary>Un refus : le statut attendu, rien ne change.</summary>
    public static void Refused(AtmStatus expected, AccountState before, NoteCounts stockBefore, AtmResult result, IMoneyDispenser dispenser)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(before, result.Account);
        Assert.Equal(stockBefore, dispenser.Stock);
    }
}

/// <summary>
/// Module factice qui respecte le contrat (tout ou rien, 4 billets au plus, cassette bloquée, stock),
/// mais qui peut rendre <c>false</c> sur demande pour simuler une panne.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _real = new(stock, jammed);

    /// <summary>Numéro (à partir de 1) de l'appel à Dispense qui échoue ; null = jamais.</summary>
    public int? FailDispenseOnCall { get; init; }

    public bool FailLoad { get; init; }

    public List<NoteCounts> DispenseCalls { get; } = [];

    public List<NoteCounts> SuccessfulPasses { get; } = [];

    public List<NoteCounts> LoadCalls { get; } = [];

    public NoteCounts Stock => _real.Stock;

    public Denomination? Jammed => _real.Jammed;

    public NoteCounts Dispensed => SuccessfulPasses.Aggregate(NoteCounts.None, (a, b) => a + b);

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (DispenseCalls.Count == FailDispenseOnCall)
        {
            return false;
        }

        var ok = _real.Dispense(notes);
        if (ok)
        {
            SuccessfulPasses.Add(notes);
        }

        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        return !FailLoad && _real.Load(notes);
    }
}
