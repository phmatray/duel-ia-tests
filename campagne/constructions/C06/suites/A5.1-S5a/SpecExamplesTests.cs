using static Atm.Tests.TestKit;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Theory]
    //          solde retiré  stock     bloquée              montant  statut                         billets   frais solde' retiré'
    [InlineData(100, 0,   1, 3, 0, null,               60,  AtmStatus.Ok,                 0, 3, 0,  0, 40,  60)]
    [InlineData(100, 0,   1, 1, 1, null,               80,  AtmStatus.Ok,                 1, 1, 1,  0, 20,  80)]
    [InlineData(105, 0,   5, 5, 5, null,               100, AtmStatus.Ok,                 2, 0, 0,  0, 5,   100)]
    [InlineData(115, 0,   5, 5, 5, null,               110, AtmStatus.Ok,                 2, 0, 1,  5, 0,   110)]
    [InlineData(110, 0,   5, 5, 5, null,               110, AtmStatus.InsufficientFunds,  0, 0, 0,  0, 110, 0)]
    [InlineData(250, 150, 5, 5, 5, null,               60,  AtmStatus.DailyLimitExceeded, 0, 0, 0,  0, 250, 150)]
    [InlineData(250, 0,   1, 0, 0, Denomination.Fifty, 50,  AtmStatus.CannotDispense,     0, 0, 0,  0, 250, 0)]
    [InlineData(250, 0,   2, 2, 0, Denomination.Fifty, 40,  AtmStatus.Ok,                 0, 2, 0,  0, 210, 40)]
    [InlineData(0,   200, 0, 0, 0, null,               5,   AtmStatus.InvalidAmount,      0, 0, 0,  0, 0,   200)]
    public void Withdraw_examples(
        int balance, int withdrawn, int s50, int s20, int s10, Denomination? jammed, int amount,
        AtmStatus status, int n50, int n20, int n10, int fee, int balanceAfter, int withdrawnAfter)
    {
        var (result, _) = Run(balance, withdrawn, Notes(s50, s20, s10), jammed, Withdraw(amount));

        Assert.Equal(
            new AtmResult(status, Notes(n50, n20, n10), fee, new AccountState(balanceAfter, withdrawnAfter)),
            result);
    }

    [Fact]
    public void Deposit_into_a_full_cassette_is_CassetteFull()
    {
        var (result, dispenser) = Run(10, 0, Notes(0, 5, 0), null, Deposit(0, 1, 0));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, new AccountState(10, 0)), result);
        Assert.Equal(Notes(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_into_the_jammed_cassette_is_CassetteJammed()
    {
        var (result, dispenser) = Run(10, 30, Notes(0, 1, 0), Denomination.Twenty, Deposit(0, 1, 0));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, new AccountState(10, 30)), result);
        Assert.Equal(Notes(0, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_of_fifty_and_ten_succeeds()
    {
        var (result, dispenser) = Run(10, 30, Notes(1, 1, 0), null, Deposit(1, 0, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(70, 30)), result);
        Assert.Equal(Notes(2, 1, 1), dispenser.Stock);
    }
}
