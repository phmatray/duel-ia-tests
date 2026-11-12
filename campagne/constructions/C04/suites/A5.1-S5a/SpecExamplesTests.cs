using static Atm.Tests.TestKit;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Theory]
    // solde, retiré, stock (50,20,10), bloquée, montant, statut, billets (50,20,10), frais, solde après, retiré après
    [InlineData(100, 0, 1, 3, 0, 0, 60, AtmStatus.Ok, 0, 3, 0, 0, 40, 60)]
    [InlineData(100, 0, 1, 1, 1, 0, 80, AtmStatus.Ok, 1, 1, 1, 0, 20, 80)]
    [InlineData(105, 0, 5, 5, 5, 0, 100, AtmStatus.Ok, 2, 0, 0, 0, 5, 100)]
    [InlineData(115, 0, 5, 5, 5, 0, 110, AtmStatus.Ok, 2, 0, 1, 5, 0, 110)]
    [InlineData(250, 0, 2, 2, 0, 50, 40, AtmStatus.Ok, 0, 2, 0, 0, 210, 40)]
    public void Withdraw_examples_succeed(
        int balance, int withdrawn, int sf, int st, int sd, int jammed, int amount,
        AtmStatus status, int nf, int nt, int nd, int fee, int balanceAfter, int withdrawnAfter)
    {
        var stock = N(sf, st, sd);
        var (result, dispenser) = Run(Account(balance, withdrawn), stock, ParseJammed(jammed), Withdraw(amount));

        Assert.Equal(status, result.Status);
        Assert.Equal(N(nf, nt, nd), result.Notes);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(Account(balanceAfter, withdrawnAfter), result.Account);
        Assert.Equal(stock - N(nf, nt, nd), dispenser.Stock);
    }

    [Theory]
    [InlineData(110, 0, 5, 5, 5, 0, 110, AtmStatus.InsufficientFunds)]
    [InlineData(250, 150, 5, 5, 5, 0, 60, AtmStatus.DailyLimitExceeded)]
    [InlineData(250, 0, 1, 0, 0, 50, 50, AtmStatus.CannotDispense)]
    [InlineData(0, 200, 0, 0, 0, 0, 5, AtmStatus.InvalidAmount)]
    public void Withdraw_examples_are_refused(
        int balance, int withdrawn, int sf, int st, int sd, int jammed, int amount, AtmStatus status)
    {
        var account = Account(balance, withdrawn);
        var stock = N(sf, st, sd);
        var (result, dispenser) = Run(account, stock, ParseJammed(jammed), Withdraw(amount));

        AssertRefused(status, result, account, stock, dispenser);
    }

    [Fact]
    public void Deposit_example_full_cassette()
    {
        var account = Account(10, 0);
        var stock = N(0, 5, 0);
        var (result, dispenser) = Run(account, stock, null, Deposit(N(0, 1, 0)));

        AssertRefused(AtmStatus.CassetteFull, result, account, stock, dispenser);
    }

    [Fact]
    public void Deposit_example_jammed_cassette()
    {
        var account = Account(10, 30);
        var stock = N(0, 1, 0);
        var (result, dispenser) = Run(account, stock, Denomination.Twenty, Deposit(N(0, 1, 0)));

        AssertRefused(AtmStatus.CassetteJammed, result, account, stock, dispenser);
    }

    [Fact]
    public void Deposit_example_succeeds()
    {
        var (result, dispenser) = Run(Account(10, 30), N(1, 1, 0), null, Deposit(N(1, 0, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(70, 30), result.Account);
        Assert.Equal(N(2, 1, 1), dispenser.Stock);
    }
}
