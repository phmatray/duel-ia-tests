namespace Atm.Tests;

/// <summary>Module de billets instrumenté : délègue au vrai module, enregistre les appels, et peut simuler une panne.</summary>
internal sealed class SpyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser inner = new(stock, jammed);

    public List<NoteCounts> DispenseCalls { get; } = [];

    public List<NoteCounts> SuccessfulPasses { get; } = [];

    public List<NoteCounts> LoadCalls { get; } = [];

    /// <summary>Numéro (1-based) de l'appel à Dispense qui renverra false sans rien changer.</summary>
    public int? FailDispenseOnCall { get; init; }

    public bool FailLoad { get; init; }

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (FailDispenseOnCall == DispenseCalls.Count)
        {
            return false;
        }

        var ok = inner.Dispense(notes);
        if (ok)
        {
            SuccessfulPasses.Add(notes);
        }

        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);

        return !FailLoad && inner.Load(notes);
    }
}

internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation W(int amount) => new Operation.Withdraw(amount);

    public static Operation D(int fifties, int twenties, int tens) => new Operation.Deposit(new NoteCounts(fifties, twenties, tens));

    public static NoteCounts Sum(IEnumerable<NoteCounts> passes) => passes.Aggregate(NoteCounts.None, (a, b) => a + b);

    public static Denomination? Jam(int value) => value == 0 ? null : (Denomination)value;

    /// <summary>Vérifie qu'un refus ne change rien : Notes vide, Fee 0, compte identique, aucun billet déplacé.</summary>
    public static void AssertRefusal(AtmResult result, AtmStatus status, AccountState account, SpyDispenser dispenser, NoteCounts stockBefore)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stockBefore, dispenser.Stock);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
    }
}
