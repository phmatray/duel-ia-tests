using static Atm.Tests.Notes;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class ExamplesTests
{
    public static TheoryData<int, int, NoteCounts, Denomination?, int, AtmStatus, NoteCounts, int, int, int> Withdrawals => new()
    {
        // solde, retiré, stock, bloquée, montant -> statut, billets, frais, solde après, retiré après
        { 100, 0, N(1, 3, 0), null, 60, AtmStatus.Ok, N(0, 3, 0), 0, 40, 60 },
        { 100, 0, N(1, 1, 1), null, 80, AtmStatus.Ok, N(1, 1, 1), 0, 20, 80 },
        { 105, 0, N(5, 5, 5), null, 100, AtmStatus.Ok, N(2, 0, 0), 0, 5, 100 },
        { 115, 0, N(5, 5, 5), null, 110, AtmStatus.Ok, N(2, 0, 1), 5, 0, 110 },
        { 110, 0, N(5, 5, 5), null, 110, AtmStatus.InsufficientFunds, NoteCounts.None, 0, 110, 0 },
        { 250, 150, N(5, 5, 5), null, 60, AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, 250, 150 },
        { 250, 0, N(1, 0, 0), Denomination.Fifty, 50, AtmStatus.CannotDispense, NoteCounts.None, 0, 250, 0 },
        { 250, 0, N(2, 2, 0), Denomination.Fifty, 40, AtmStatus.Ok, N(0, 2, 0), 0, 210, 40 },
        { 0, 200, N(0, 0, 0), null, 5, AtmStatus.InvalidAmount, NoteCounts.None, 0, 0, 200 },
    };

    [Theory]
    [MemberData(nameof(Withdrawals))]
    public void Retrait(int balance, int withdrawn, NoteCounts stock, Denomination? jammed, int amount,
        AtmStatus status, NoteCounts notes, int fee, int balanceAfter, int withdrawnAfter)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));

        Assert.Equal(status, result.Status);
        Assert.Equal(notes, result.Notes);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(new AccountState(balanceAfter, withdrawnAfter), result.Account);
        Assert.Equal(stock - notes, dispenser.Stock);
    }

    public static TheoryData<int, int, NoteCounts, Denomination?, NoteCounts, AtmStatus, NoteCounts, int, int> Deposits => new()
    {
        // solde, retiré, stock, bloquée, dépôt -> statut, stock après, solde après, retiré après
        { 10, 0, N(0, 5, 0), null, N(0, 1, 0), AtmStatus.CassetteFull, N(0, 5, 0), 10, 0 },
        { 10, 30, N(0, 1, 0), Denomination.Twenty, N(0, 1, 0), AtmStatus.CassetteJammed, N(0, 1, 0), 10, 30 },
        { 10, 30, N(1, 1, 0), null, N(1, 0, 1), AtmStatus.Ok, N(2, 1, 1), 70, 30 },
    };

    [Theory]
    [MemberData(nameof(Deposits))]
    public void Depot(int balance, int withdrawn, NoteCounts stock, Denomination? jammed, NoteCounts deposit,
        AtmStatus status, NoteCounts stockAfter, int balanceAfter, int withdrawnAfter)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(new AccountState(balance, withdrawn), new Operation.Deposit(deposit));

        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(balanceAfter, withdrawnAfter), result.Account);
        Assert.Equal(stockAfter, dispenser.Stock);
    }
}
