namespace Atm.Tests;

/// <summary>Raccourcis communs : on construit toujours le vrai module, comme l'exige le domaine.</summary>
internal static class Fixture
{
    public static readonly NoteCounts Full = new(5, 5, 5);

    public static NoteCounts Notes(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Account(int balance, int withdrawnToday = 0) => new(balance, withdrawnToday);

    public static (CashMachine Machine, MoneyDispenser Dispenser) Machine(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        return (new CashMachine(dispenser), dispenser);
    }

    public static (AtmResult Result, MoneyDispenser Dispenser) Withdraw(
        AccountState account, int amount, NoteCounts stock, Denomination? jammed = null)
    {
        var (machine, dispenser) = Machine(stock, jammed);
        return (machine.Execute(account, new Operation.Withdraw(amount)), dispenser);
    }

    public static (AtmResult Result, MoneyDispenser Dispenser) Deposit(
        AccountState account, NoteCounts notes, NoteCounts stock, Denomination? jammed = null)
    {
        var (machine, dispenser) = Machine(stock, jammed);
        return (machine.Execute(account, new Operation.Deposit(notes)), dispenser);
    }

    /// <summary>Un refus : statut donné, rien sorti, aucun frais, compte et stock intacts.</summary>
    public static void AssertRefused(
        AtmStatus expected, AtmResult result, AccountState account, MoneyDispenser dispenser, NoteCounts stock)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
    }

    // Valeurs du domaine.
    public static IEnumerable<int> Balances => Enumerable.Range(0, 51).Select(i => i * 5);
    public static IEnumerable<int> WithdrawnValues => Enumerable.Range(0, 21).Select(i => i * 10);
    public static IEnumerable<int> Amounts => new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));
    public static IEnumerable<Denomination?> JammedValues => [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static IEnumerable<NoteCounts> Stocks =>
        from f in Enumerable.Range(0, 6)
        from t in Enumerable.Range(0, 6)
        from n in Enumerable.Range(0, 6)
        select new NoteCounts(f, t, n);

    public static IEnumerable<NoteCounts> DepositValues =>
        from f in Enumerable.Range(0, 2)
        from t in Enumerable.Range(0, 2)
        from n in Enumerable.Range(0, 2)
        select new NoteCounts(f, t, n);
}
