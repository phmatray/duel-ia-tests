using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    // ---- Contrôle 1 : dépôt vide ----

    [Fact]
    public void Empty_deposit_is_refused()
    {
        var stock = N(1, 1, 1);
        var dispenser = new SpyDispenser(stock);
        var account = Acc(10, 30);

        var result = new CashMachine(dispenser).Execute(account, D(0, 0, 0));

        AssertRefusal(result, AtmStatus.InvalidAmount, account, dispenser, stock);
    }

    [Fact]
    public void Empty_deposit_is_checked_before_jam_and_capacity()
    {
        var stock = N(5, 5, 5);
        var dispenser = new SpyDispenser(stock, Denomination.Twenty);
        var account = Acc(10, 30);

        var result = new CashMachine(dispenser).Execute(account, D(0, 0, 0));

        AssertRefusal(result, AtmStatus.InvalidAmount, account, dispenser, stock);
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(20, 1, 1, 0)]
    [InlineData(10, 0, 1, 1)]
    public void Deposit_containing_a_note_of_the_jammed_cassette_is_refused(int jammed, int f, int t, int n)
    {
        var stock = N(1, 1, 1);
        var dispenser = new SpyDispenser(stock, (Denomination)jammed);
        var account = Acc(10, 30);

        var result = new CashMachine(dispenser).Execute(account, D(f, t, n));

        AssertRefusal(result, AtmStatus.CassetteJammed, account, dispenser, stock);
    }

    [Fact]
    public void Jam_is_checked_before_capacity()
    {
        var stock = N(5, 5, 5);
        var dispenser = new SpyDispenser(stock, Denomination.Twenty);
        var account = Acc(10, 30);

        var result = new CashMachine(dispenser).Execute(account, D(1, 1, 1));

        AssertRefusal(result, AtmStatus.CassetteJammed, account, dispenser, stock);
    }

    [Theory]
    [InlineData(50, 0, 1, 1)]
    [InlineData(20, 1, 0, 1)]
    [InlineData(10, 1, 1, 0)]
    public void Deposit_avoiding_the_jammed_cassette_is_accepted(int jammed, int f, int t, int n)
    {
        var stock = N(1, 1, 1);
        var dispenser = new SpyDispenser(stock, (Denomination)jammed);
        var notes = N(f, t, n);

        var result = new CashMachine(dispenser).Execute(Acc(10, 30), new Operation.Deposit(notes));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(stock + notes, dispenser.Stock);
        Assert.Equal(Acc(10 + notes.Value, 30), result.Account);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)]  // une seule cassette déborde : tout est refusé
    [InlineData(4, 4, 5, 1, 1, 1)]
    public void Deposit_overflowing_a_cassette_is_refused(int sf, int st, int sn, int f, int t, int n)
    {
        var stock = N(sf, st, sn);
        var dispenser = new SpyDispenser(stock);
        var account = Acc(10, 30);

        var result = new CashMachine(dispenser).Execute(account, D(f, t, n));

        AssertRefusal(result, AtmStatus.CassetteFull, account, dispenser, stock);
    }

    [Fact]
    public void Filling_a_cassette_up_to_exactly_5_is_accepted()
    {
        var dispenser = new SpyDispenser(N(4, 4, 4));

        var result = new CashMachine(dispenser).Execute(Acc(0, 0), D(1, 1, 1));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
        Assert.Equal(Acc(80, 0), result.Account);
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
    public void Successful_deposit_loads_notes_and_credits_balance(int f, int t, int n)
    {
        var stock = N(2, 2, 2);
        var dispenser = new SpyDispenser(stock);
        var notes = N(f, t, n);

        var result = new CashMachine(dispenser).Execute(Acc(15, 120), new Operation.Deposit(notes));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(15 + 50 * f + 20 * t + 10 * n, 120), result.Account);
        Assert.Equal(stock + notes, dispenser.Stock);
        Assert.Equal(notes, Sum(dispenser.LoadCalls));
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Fact]
    public void Deposit_into_empty_machine_works_with_the_real_module()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));

        var result = new CashMachine(dispenser).Execute(Acc(0, 200), D(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(80, 200)), result);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }
}
