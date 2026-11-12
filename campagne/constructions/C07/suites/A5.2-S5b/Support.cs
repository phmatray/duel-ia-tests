namespace Atm.Tests;

/// <summary>Outils partagés : construction de la machine et assertions sur un résultat complet.</summary>
internal static class Support
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static NoteCounts Full => N(5, 5, 5);

    public static (CashMachine Machine, MoneyDispenser Dispenser) NewAtm(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        return (new CashMachine(dispenser), dispenser);
    }

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(NoteCounts notes) => new Operation.Deposit(notes);

    /// <summary>Un refus : statut donné, rien ne sort, pas de frais, compte et stock inchangés.</summary>
    public static void AssertRefused(
        AtmStatus expected, AtmResult result, AccountState account, MoneyDispenser dispenser, NoteCounts stockBefore)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stockBefore, dispenser.Stock);
    }

    /// <summary>Exécute une opération sur une machine neuve et vérifie qu'elle est refusée sans effet.</summary>
    public static void AssertRefused(
        AtmStatus expected, AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var (machine, dispenser) = NewAtm(stock, jammed);
        var result = machine.Execute(account, operation);
        AssertRefused(expected, result, account, dispenser, stock);
    }
}
