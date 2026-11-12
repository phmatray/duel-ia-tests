namespace Atm.Tests;

/// <summary>Petits outils partagés par les tests.</summary>
internal static class T
{
    public static NoteCounts N(int fifties = 0, int twenties = 0, int tens = 0) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Denomination? Jam(int? value) => value is { } v ? (Denomination)v : null;

    /// <summary>Exécute une opération sur une machine neuve branchée sur le vrai module.</summary>
    public static (AtmResult Result, MoneyDispenser Dispenser) Run(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var machine = new CashMachine(dispenser);
        var result = machine.Execute(account, operation);
        return (result, dispenser);
    }

    public static (AtmResult Result, MoneyDispenser Dispenser) Withdraw(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount) =>
        Run(Acc(balance, withdrawnToday), stock, jammed, new Operation.Withdraw(amount));

    public static (AtmResult Result, MoneyDispenser Dispenser) Deposit(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, NoteCounts notes) =>
        Run(Acc(balance, withdrawnToday), stock, jammed, new Operation.Deposit(notes));

    /// <summary>Un refus ne change rien : pas de billets, pas de frais, compte et stock identiques.</summary>
    public static void AssertRefused(
        AtmStatus expected, AtmResult result, MoneyDispenser dispenser, AccountState account, NoteCounts stock)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
    }

    /// <summary>Un module espion qui délègue au vrai module et enregistre les appels.</summary>
    public sealed class SpyDispenser(MoneyDispenser inner) : IMoneyDispenser
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
}
