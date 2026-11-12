namespace Atm.Tests;

/// <summary>Petits outils partagés : construire un scénario, l'exécuter, lire le résultat.</summary>
internal static class TestKit
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    /// <summary>0 = aucune cassette bloquée, sinon 50, 20 ou 10.</summary>
    public static Denomination? Jam(int jammed) => jammed == 0 ? null : (Denomination)jammed;

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static (AtmResult Result, MoneyDispenser Dispenser) Withdraw(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(Acc(balance, withdrawnToday), new Operation.Withdraw(amount));
        return (result, dispenser);
    }

    public static (AtmResult Result, MoneyDispenser Dispenser) Deposit(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(Acc(balance, withdrawnToday), new Operation.Deposit(notes));
        return (result, dispenser);
    }

    /// <summary>Un refus : rien ne change, ni le compte, ni les billets.</summary>
    public static void AssertRefused(
        AtmStatus expected, AtmResult result, AccountState before, MoneyDispenser dispenser, NoteCounts stockBefore)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(before, result.Account);
        Assert.Equal(stockBefore, dispenser.Stock);
    }
}
