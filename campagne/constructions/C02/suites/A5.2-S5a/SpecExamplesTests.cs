using static Atm.Tests.Atm;

namespace Atm.Tests;

/// <summary>Les exemples de la spécification, un par ligne.</summary>
public class SpecExamplesTests
{
    private static Denomination? J(int jammed) => jammed == 0 ? null : (Denomination)jammed;

    [Theory]
    // solde, retiré, stock (50,20,10), bloquée, montant, statut, billets, frais, solde après, retiré après
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
        int balance, int withdrawn, int sf, int st, int sd, int jammed, int amount,
        AtmStatus status, int nf, int nt, int nd, int fee, int balanceAfter, int withdrawnAfter)
    {
        var stock = Notes(sf, st, sd);
        var dispenser = new MoneyDispenser(stock, J(jammed));

        var result = new CashMachine(dispenser).Execute(Account(balance, withdrawn), Withdraw(amount));

        Assert.Equal(new AtmResult(status, Notes(nf, nt, nd), fee, Account(balanceAfter, withdrawnAfter)), result);
        Assert.Equal(stock - Notes(nf, nt, nd), dispenser.Stock);
    }

    [Theory]
    // solde, retiré, stock, bloquée, dépôt, statut, stock après, solde après
    [InlineData(10, 0, 0, 5, 0, 0, 0, 1, 0, AtmStatus.CassetteFull, 0, 5, 0, 10)]
    [InlineData(10, 30, 0, 1, 0, 20, 0, 1, 0, AtmStatus.CassetteJammed, 0, 1, 0, 10)]
    [InlineData(10, 30, 1, 1, 0, 0, 1, 0, 1, AtmStatus.Ok, 2, 1, 1, 70)]
    public void Deposit_examples(
        int balance, int withdrawn, int sf, int st, int sd, int jammed, int df, int dt, int dd,
        AtmStatus status, int af, int at, int ad, int balanceAfter)
    {
        var dispenser = new MoneyDispenser(Notes(sf, st, sd), J(jammed));

        var result = new CashMachine(dispenser).Execute(Account(balance, withdrawn), Deposit(df, dt, dd));

        Assert.Equal(new AtmResult(status, NoteCounts.None, 0, Account(balanceAfter, withdrawn)), result);
        Assert.Equal(Notes(af, at, ad), dispenser.Stock);
    }
}
