namespace Atm.Tests;

/// <summary>Raccourcis communs aux tests.</summary>
internal static class Outils
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Compte(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation Retrait(int amount) => new Operation.Withdraw(amount);

    public static Operation Depot(NoteCounts notes) => new Operation.Deposit(notes);

    public static readonly NoteCounts Plein = new(5, 5, 5);

    /// <summary>Exécute une opération sur une machine équipée du vrai module.</summary>
    public static (AtmResult Result, MoneyDispenser Dispenser) Executer(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, operation);

        return (result, dispenser);
    }

    /// <summary>Vérifie qu'un refus ne change rien : ni billets, ni frais, ni compte, ni stock.</summary>
    public static void AssertRefus(AtmStatus expected, AtmResult result, AccountState account, NoteCounts stockAvant, IMoneyDispenser dispenser)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stockAvant, dispenser.Stock);
    }
}

/// <summary>
/// Module espion : délègue au vrai module, enregistre chaque appel et peut simuler une panne.
/// </summary>
internal sealed class ModuleEspion(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _real = new(stock, jammed);

    /// <summary>Numéro (à partir de 1) de l'appel à Dispense qui rendra false ; null = jamais.</summary>
    public int? PanneAuPassage { get; init; }

    /// <summary>Si vrai, Load rend false sans rien changer.</summary>
    public bool PanneAuChargement { get; init; }

    public List<NoteCounts> Passages { get; } = [];

    public List<NoteCounts> Chargements { get; } = [];

    public NoteCounts Stock => _real.Stock;

    public Denomination? Jammed => _real.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        Passages.Add(notes);
        if (PanneAuPassage == Passages.Count)
        {
            return false;
        }

        return _real.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        Chargements.Add(notes);
        if (PanneAuChargement)
        {
            return false;
        }

        return _real.Load(notes);
    }
}
