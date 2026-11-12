using static Atm.Tests.N;

namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, MoneyDispenser Dispenser) Deposit(
        AccountState account, NoteCounts notes, NoteCounts stock, Denomination? jam = null)
    {
        var dispenser = new MoneyDispenser(stock, jam);
        var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));
        return (result, dispenser);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Ten)]
    public void Empty_deposit_is_invalid(Denomination? jam)
    {
        var (r, d) = Deposit(new AccountState(100, 20), NoteCounts.None, C(5, 5, 5), jam);
        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
        Assert.Equal(new AccountState(100, 20), r.Account);
        Assert.Equal(C(5, 5, 5), d.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 1)]
    [InlineData(Denomination.Fifty, 1, 0, 1)]
    public void Deposit_touching_the_jammed_cassette_is_refused(Denomination jam, int f, int t, int ten)
    {
        var (r, d) = Deposit(new AccountState(10, 0), C(f, t, ten), C(0, 0, 0), jam);
        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
        Assert.Equal(C(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Jammed_is_checked_before_full()
    {
        // Le dépôt touche la cassette bloquée ET une cassette pleine.
        var (r, d) = Deposit(new AccountState(10, 0), C(1, 1, 0), C(0, 5, 0), Denomination.Fifty);
        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
        Assert.Equal(C(0, 5, 0), d.Stock);
    }

    [Fact]
    public void Jammed_is_checked_before_full_on_the_same_cassette()
    {
        var (r, _) = Deposit(new AccountState(10, 0), C(0, 1, 0), C(0, 5, 0), Denomination.Twenty);
        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Theory]
    [InlineData(1, 0, 0, 5, 0, 0)]
    [InlineData(0, 1, 0, 0, 5, 0)]
    [InlineData(0, 0, 1, 0, 0, 5)]
    [InlineData(1, 1, 1, 4, 4, 5)]   // une seule cassette déborde : tout est refusé
    public void Deposit_overflowing_a_cassette_is_refused(int f, int t, int ten, int sf, int st, int s10)
    {
        var (r, d) = Deposit(new AccountState(10, 0), C(f, t, ten), C(sf, st, s10));
        Assert.Equal(AtmStatus.CassetteFull, r.Status);
        Assert.Equal(new AccountState(10, 0), r.Account);
        Assert.Equal(C(sf, st, s10), d.Stock);
    }

    [Fact]
    public void Filling_a_cassette_up_to_exactly_5_is_allowed()
    {
        var (r, d) = Deposit(new AccountState(0, 0), C(1, 1, 1), C(4, 4, 4));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(5, 5, 5), d.Stock);
        Assert.Equal(new AccountState(80, 0), r.Account);
    }

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Successful_deposit_credits_its_value(int f, int t, int ten, int value)
    {
        var account = new AccountState(150, 200);
        var (r, d) = Deposit(account, C(f, t, ten), C(2, 2, 2));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(150 + value, 200), r.Account);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(C(2, 2, 2) + C(f, t, ten), d.Stock);
    }

    [Fact]
    public void Deposit_avoiding_the_jammed_cassette_is_accepted()
    {
        var (r, d) = Deposit(new AccountState(0, 50), C(1, 0, 1), C(0, 0, 0), Denomination.Twenty);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(60, 50), r.Account);
        Assert.Equal(C(1, 0, 1), d.Stock);
    }

    [Fact]
    public void Deposit_goes_through_the_module_in_one_load()
    {
        var spy = new SpyDispenser(new MoneyDispenser(C(0, 0, 0)));
        var r = new CashMachine(spy).Execute(new AccountState(0, 0), new Operation.Deposit(C(1, 1, 1)));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Empty(spy.DispenseCalls);
        var loaded = spy.LoadCalls.Where(c => c.Result).Aggregate(NoteCounts.None, (acc, c) => acc + c.Notes);
        Assert.Equal(C(1, 1, 1), loaded);
    }
}
