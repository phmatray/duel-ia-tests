namespace Atm.Tests.Support;

/// <summary>Fabrique de notes et exécution d'un scénario sur le vrai module, enveloppé d'un espion.</summary>
public static class N
{
    public static NoteCounts Notes(int fifties = 0, int twenties = 0, int tens = 0) => new(fifties, twenties, tens);
}

public sealed record Run(AtmResult Result, RecordingDispenser Dispenser, NoteCounts InitialStock, AccountState InitialAccount);

public static class Scenario
{
    public static Run Execute(int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(balance, withdrawnToday);
        var result = machine.Execute(account, operation);
        return new Run(result, dispenser, stock, account);
    }

    public static Run Withdraw(int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount) =>
        Execute(balance, withdrawnToday, stock, jammed, new Operation.Withdraw(amount));

    public static Run Deposit(int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, NoteCounts notes) =>
        Execute(balance, withdrawnToday, stock, jammed, new Operation.Deposit(notes));

    public static Denomination?[] JammedValues => [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
            yield return new NoteCounts(f, t, n);
    }

    public static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var n = 0; n <= 1; n++)
            yield return new NoteCounts(f, t, n);
    }

    public static int[] DomainAmounts =>
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static int[] DomainBalances => Enumerable.Range(0, 51).Select(i => i * 5).ToArray();

    public static int[] DomainWithdrawn => Enumerable.Range(0, 21).Select(i => i * 10).ToArray();
}
