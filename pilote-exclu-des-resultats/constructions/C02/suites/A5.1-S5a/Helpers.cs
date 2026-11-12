namespace Atm.Tests;

internal static class N
{
    public static NoteCounts Notes(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static readonly NoteCounts Full = new(5, 5, 5);

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(int fifties, int twenties, int tens) => new Operation.Deposit(new(fifties, twenties, tens));

    public static AccountState Account(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Denomination? Jam(int? denomination) => denomination is { } d ? (Denomination)d : null;
}
