namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, SpyDispenser Dispenser) Run(
        AccountState account, NoteCounts deposit, NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new SpyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(deposit));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus status, AccountState account, NoteCounts stock, AtmResult result, SpyDispenser dispenser)
    {
        Assert.Equal(new AtmResult(status, NoteCounts.None, 0, account), result);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.Calls.Where(c => c.Result));
    }

    // --- Règle 1 : dépôt vide ---

    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Empty_deposit_is_invalid_even_with_a_jammed_cassette(Denomination? jammed)
    {
        var account = Account(100, 50);
        var (result, dispenser) = Run(account, NoteCounts.None, Notes(1, 1, 1), jammed);

        AssertRefused(AtmStatus.InvalidAmount, account, Notes(1, 1, 1), result, dispenser);
    }

    [Fact]
    public void Empty_deposit_is_invalid_even_when_cassettes_are_full()
    {
        var account = Account(100, 50);
        var (result, dispenser) = Run(account, NoteCounts.None, Notes(5, 5, 5));

        AssertRefused(AtmStatus.InvalidAmount, account, Notes(5, 5, 5), result, dispenser);
    }

    // --- Règle 2 : cassette bloquée ---

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)] // les autres billets iraient bien, mais tout ou rien
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Deposit_containing_a_note_of_the_jammed_cassette_is_refused(Denomination jammed, int fifties, int twenties, int tens)
    {
        var account = Account(100, 50);
        var stock = Notes(1, 1, 1);
        var (result, dispenser) = Run(account, Notes(fifties, twenties, tens), stock, jammed);

        AssertRefused(AtmStatus.CassetteJammed, account, stock, result, dispenser);
    }

    [Fact]
    public void Jammed_is_checked_before_full()
    {
        var account = Account(100, 50);
        var stock = Notes(5, 5, 5);
        var (result, dispenser) = Run(account, Notes(1, 1, 1), stock, Denomination.Ten);

        AssertRefused(AtmStatus.CassetteJammed, account, stock, result, dispenser);
    }

    [Fact]
    public void Jammed_cassette_does_not_block_deposits_of_other_denominations()
    {
        var (result, dispenser) = Run(Account(0, 0), Notes(1, 0, 1), Notes(0, 0, 0), Denomination.Twenty);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(60, 0)), result);
        Assert.Equal(Notes(1, 0, 1), dispenser.Stock);
    }

    // --- Règle 3 : cassette pleine ---

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // une seule cassette déborde : tout ou rien
    [InlineData(0, 0, 5, 1, 1, 1)]
    public void Deposit_that_would_exceed_5_notes_in_a_cassette_is_refused(
        int stockFifties, int stockTwenties, int stockTens, int fifties, int twenties, int tens)
    {
        var account = Account(100, 50);
        var stock = Notes(stockFifties, stockTwenties, stockTens);
        var (result, dispenser) = Run(account, Notes(fifties, twenties, tens), stock);

        AssertRefused(AtmStatus.CassetteFull, account, stock, result, dispenser);
    }

    [Fact]
    public void Deposit_up_to_exactly_5_notes_per_cassette_is_allowed()
    {
        var (result, dispenser) = Run(Account(0, 0), Notes(1, 1, 1), Notes(4, 4, 4));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(80, 0)), result);
        Assert.Equal(Notes(5, 5, 5), dispenser.Stock);
    }

    // --- Succès ---

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Success_credits_the_value_and_loads_the_notes(int fifties, int twenties, int tens, int value)
    {
        var deposit = Notes(fifties, twenties, tens);
        var stock = Notes(2, 3, 4);
        var (result, dispenser) = Run(Account(25, 140), deposit, stock);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(25 + value, 140), result.Account);
        Assert.Equal(stock + deposit, dispenser.Stock);
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Fact]
    public void Deposit_is_not_limited_by_daily_limit_or_balance()
    {
        var (result, _) = Run(Account(250, 200), Notes(1, 1, 1), Notes(0, 0, 0));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(330, 200)), result);
    }
}
