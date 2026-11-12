using static Atm.Tests.TestSupport;

namespace Atm.Tests;

public class DepositTests
{
    private static readonly AccountState Account = new(100, 60);

    // ---------- Contrôle 1 : dépôt vide ----------

    [Fact]
    public void Empty_deposit_is_refused()
    {
        var stock = N(1, 1, 1);

        var result = Deposit(stock, null, Account, NoteCounts.None, out var stockAfter);

        AssertRefused(AtmStatus.InvalidAmount, result, Account, stock, stockAfter);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(20)]
    [InlineData(10)]
    public void Empty_deposit_is_checked_before_jam_and_capacity(int jammed)
    {
        var stock = N(5, 5, 5);

        var result = Deposit(stock, Jam(jammed), Account, NoteCounts.None, out var stockAfter);

        AssertRefused(AtmStatus.InvalidAmount, result, Account, stock, stockAfter);
    }

    // ---------- Contrôle 2 : cassette bloquée ----------

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(20, 1, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(10, 0, 1, 1)]
    public void Deposit_containing_a_note_of_the_jammed_cassette_is_refused(int jammed, int d50, int d20, int d10)
    {
        var stock = N(2, 2, 2);

        var result = Deposit(stock, Jam(jammed), Account, N(d50, d20, d10), out var stockAfter);

        AssertRefused(AtmStatus.CassetteJammed, result, Account, stock, stockAfter);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(20)]
    [InlineData(10)]
    public void Jam_is_checked_before_capacity(int jammed)
    {
        var stock = N(5, 5, 5);

        var result = Deposit(stock, Jam(jammed), Account, N(1, 1, 1), out var stockAfter);

        AssertRefused(AtmStatus.CassetteJammed, result, Account, stock, stockAfter);
    }

    [Theory]
    [InlineData(50, 0, 1, 1, 0, 1, 1)]
    [InlineData(20, 1, 0, 1, 1, 0, 1)]
    [InlineData(10, 1, 1, 0, 1, 1, 0)]
    public void Deposit_avoiding_the_jammed_cassette_is_accepted(int jammed, int d50, int d20, int d10, int a50, int a20, int a10)
    {
        var result = Deposit(N(0, 0, 0), Jam(jammed), Account, N(d50, d20, d10), out var stockAfter);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(a50, a20, a10), stockAfter);
        Assert.Equal(Account with { Balance = Account.Balance + N(d50, d20, d10).Value }, result.Account);
    }

    [Fact]
    public void Full_jammed_cassette_does_not_block_a_deposit_into_other_cassettes()
    {
        var result = Deposit(N(5, 0, 0), Denomination.Fifty, Account, N(0, 1, 1), out var stockAfter);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(5, 1, 1), stockAfter);
    }

    // ---------- Contrôle 3 : cassette pleine ----------

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // une seule cassette pleine suffit : tout ou rien
    [InlineData(0, 0, 5, 1, 1, 1)]
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void Deposit_overflowing_a_cassette_is_refused(int s50, int s20, int s10, int d50, int d20, int d10)
    {
        var stock = N(s50, s20, s10);

        var result = Deposit(stock, null, Account, N(d50, d20, d10), out var stockAfter);

        AssertRefused(AtmStatus.CassetteFull, result, Account, stock, stockAfter);
    }

    [Fact]
    public void Deposit_filling_cassettes_to_exactly_five_is_accepted()
    {
        var result = Deposit(N(4, 4, 4), null, Account, N(1, 1, 1), out var stockAfter);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(5, 5, 5), stockAfter);
    }

    [Fact]
    public void Full_cassette_does_not_block_a_deposit_into_other_cassettes()
    {
        var result = Deposit(N(5, 5, 0), null, Account, N(0, 0, 1), out var stockAfter);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(5, 5, 1), stockAfter);
    }

    // ---------- Succès ----------

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Successful_deposit_credits_its_value_and_loads_the_notes(int d50, int d20, int d10, int value)
    {
        var stock = N(2, 3, 4);
        var notes = N(d50, d20, d10);

        var result = Deposit(stock, null, Account, notes, out var stockAfter);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(Account.Balance + value, Account.WithdrawnToday), result.Account);
        Assert.Equal(stock + notes, stockAfter);
    }

    [Fact]
    public void Deposit_does_not_touch_the_daily_withdrawal_counter()
    {
        var account = new AccountState(0, 200);

        var result = Deposit(N(0, 0, 0), null, account, N(1, 1, 1), out _);

        Assert.Equal(new AccountState(80, 200), result.Account);
    }

    [Fact]
    public void Successful_deposit_loads_the_notes_in_one_call_and_dispenses_nothing()
    {
        var spy = new SpyDispenser(new MoneyDispenser(N(0, 0, 0)));
        var machine = new CashMachine(spy);

        machine.Execute(Account, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal([N(1, 1, 1)], spy.Loaded);
        Assert.Empty(spy.Dispensed);
    }

    [Fact]
    public void Refused_deposit_moves_no_note_at_all()
    {
        var spy = new SpyDispenser(new MoneyDispenser(N(5, 0, 0), Denomination.Twenty));
        var machine = new CashMachine(spy);

        machine.Execute(Account, new Operation.Deposit(NoteCounts.None));
        machine.Execute(Account, new Operation.Deposit(N(0, 1, 0)));
        machine.Execute(Account, new Operation.Deposit(N(1, 0, 0)));

        Assert.Empty(spy.Loaded);
        Assert.Empty(spy.Dispensed);
        Assert.Equal(N(5, 0, 0), spy.Stock);
    }
}
