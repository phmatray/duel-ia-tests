using static Atm.Tests.Atm;

namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, SpyDispenser Dispenser) Run(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, NoteCounts deposit)
    {
        var dispenser = new SpyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(Account(balance, withdrawnToday), new Operation.Deposit(deposit));
        return (result, dispenser);
    }

    // ---- Contrôle 1 : dépôt vide ----

    [Fact]
    public void Empty_deposit_is_refused()
    {
        var (result, dispenser) = Run(100, 30, Notes(1, 1, 1), null, NoteCounts.None);

        AssertRefused(AtmStatus.InvalidAmount, Account(100, 30), Notes(1, 1, 1), result, dispenser);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Empty_deposit_is_checked_before_jammed_cassette(Denomination jammed)
    {
        var (result, dispenser) = Run(100, 30, Full, jammed, NoteCounts.None);

        AssertRefused(AtmStatus.InvalidAmount, Account(100, 30), Full, result, dispenser);
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Twenty, 1, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Deposit_touching_the_jammed_cassette_is_refused(Denomination jammed, int f, int t, int d)
    {
        var stock = Notes(1, 1, 1);
        var (result, dispenser) = Run(100, 30, stock, jammed, Notes(f, t, d));

        AssertRefused(AtmStatus.CassetteJammed, Account(100, 30), stock, result, dispenser);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 1, 1, 1)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    public void Jammed_cassette_is_checked_before_full_cassette(Denomination jammed, int f, int t, int d)
    {
        var (result, dispenser) = Run(100, 30, Full, jammed, Notes(f, t, d));

        AssertRefused(AtmStatus.CassetteJammed, Account(100, 30), Full, result, dispenser);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void Deposit_avoiding_the_jammed_cassette_is_accepted(Denomination jammed, int f, int t, int d)
    {
        var deposit = Notes(f, t, d);
        var (result, dispenser) = Run(100, 30, Notes(1, 1, 1), jammed, deposit);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(1, 1, 1) + deposit, dispenser.Stock);
        Assert.Equal(Account(100 + deposit.Value, 30), result.Account);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // un seul billet de trop suffit à tout refuser
    [InlineData(4, 4, 5, 1, 1, 1)]
    public void Deposit_overflowing_a_cassette_is_refused(int sf, int st, int sd, int f, int t, int d)
    {
        var stock = Notes(sf, st, sd);
        var (result, dispenser) = Run(100, 30, stock, null, Notes(f, t, d));

        AssertRefused(AtmStatus.CassetteFull, Account(100, 30), stock, result, dispenser);
    }

    [Fact]
    public void Deposit_filling_cassettes_exactly_to_5_is_accepted()
    {
        var (result, dispenser) = Run(0, 0, Notes(4, 4, 4), null, Notes(1, 1, 1));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Full, dispenser.Stock);
        Assert.Equal(Account(80, 0), result.Account);
    }

    // ---- Le succès ----

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    public void Successful_deposit_loads_notes_and_credits_their_value(int f, int t, int d)
    {
        var deposit = Notes(f, t, d);
        var (result, dispenser) = Run(170, 200, Notes(2, 3, 0), null, deposit);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(170 + deposit.Value, 200), result.Account);
        Assert.Equal(Notes(2, 3, 0) + deposit, dispenser.Stock);
        Assert.Equal([deposit], dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
    }
}
