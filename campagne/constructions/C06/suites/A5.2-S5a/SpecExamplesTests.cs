using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Theory]
    // solde, retiré, stock 50/20/10, bloquée, montant, statut, billets 50/20/10, frais, solde après, retiré après
    [InlineData(100, 0, 1, 3, 0, 0, 60, AtmStatus.Ok, 0, 3, 0, 0, 40, 60)]
    [InlineData(100, 0, 1, 1, 1, 0, 80, AtmStatus.Ok, 1, 1, 1, 0, 20, 80)]
    [InlineData(105, 0, 5, 5, 5, 0, 100, AtmStatus.Ok, 2, 0, 0, 0, 5, 100)]
    [InlineData(115, 0, 5, 5, 5, 0, 110, AtmStatus.Ok, 2, 0, 1, 5, 0, 110)]
    [InlineData(110, 0, 5, 5, 5, 0, 110, AtmStatus.InsufficientFunds, 0, 0, 0, 0, 110, 0)]
    [InlineData(250, 150, 5, 5, 5, 0, 60, AtmStatus.DailyLimitExceeded, 0, 0, 0, 0, 250, 150)]
    [InlineData(250, 0, 1, 0, 0, 50, 50, AtmStatus.CannotDispense, 0, 0, 0, 0, 250, 0)]
    [InlineData(250, 0, 2, 2, 0, 50, 40, AtmStatus.Ok, 0, 2, 0, 0, 210, 40)]
    [InlineData(0, 200, 0, 0, 0, 0, 5, AtmStatus.InvalidAmount, 0, 0, 0, 0, 0, 200)]
    public void Withdraw_examples(
        int balance, int withdrawn, int s50, int s20, int s10, int jam, int amount,
        AtmStatus status, int n50, int n20, int n10, int fee, int balanceAfter, int withdrawnAfter)
    {
        var result = Withdraw(Acc(balance, withdrawn), N(s50, s20, s10), Jam(jam), amount);

        Assert.Equal(new AtmResult(status, N(n50, n20, n10), fee, Acc(balanceAfter, withdrawnAfter)), result);
    }

    [Theory]
    // solde, retiré, stock, bloquée, dépôt, statut, stock après, solde après
    [InlineData(10, 0, 0, 5, 0, 0, 0, 1, 0, AtmStatus.CassetteFull, 0, 5, 0, 10)]
    [InlineData(10, 30, 0, 1, 0, 20, 0, 1, 0, AtmStatus.CassetteJammed, 0, 1, 0, 10)]
    [InlineData(10, 30, 1, 1, 0, 0, 1, 0, 1, AtmStatus.Ok, 2, 1, 1, 70)]
    public void Deposit_examples(
        int balance, int withdrawn, int s50, int s20, int s10, int jam, int d50, int d20, int d10,
        AtmStatus status, int a50, int a20, int a10, int balanceAfter)
    {
        var (machine, dispenser) = Machine(N(s50, s20, s10), Jam(jam));

        var result = machine.Execute(Acc(balance, withdrawn), new Operation.Deposit(N(d50, d20, d10)));

        Assert.Equal(new AtmResult(status, NoteCounts.None, 0, Acc(balanceAfter, withdrawn)), result);
        Assert.Equal(N(a50, a20, a10), dispenser.Stock);
    }
}
