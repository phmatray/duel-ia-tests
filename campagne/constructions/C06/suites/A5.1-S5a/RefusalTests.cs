using static Atm.Tests.TestKit;

namespace Atm.Tests;

/// <summary>« Sur un refus, rien ne change : Notes vide, Fee à 0, compte identique, aucun billet déplacé. »</summary>
public class RefusalTests
{
    public static TheoryData<int, int, NoteCounts, Denomination?, Operation, AtmStatus> Refusals => new()
    {
        { 250, 0, FullStock, null, Withdraw(-10), AtmStatus.InvalidAmount },
        { 250, 0, FullStock, null, Withdraw(0), AtmStatus.InvalidAmount },
        { 250, 0, FullStock, null, Withdraw(5), AtmStatus.InvalidAmount },
        { 250, 150, FullStock, null, Withdraw(60), AtmStatus.DailyLimitExceeded },
        { 250, 0, FullStock, null, Withdraw(220), AtmStatus.DailyLimitExceeded },
        { 110, 0, FullStock, null, Withdraw(110), AtmStatus.InsufficientFunds },
        { 40, 0, FullStock, null, Withdraw(50), AtmStatus.InsufficientFunds },
        { 250, 0, Notes(1, 0, 0), Denomination.Fifty, Withdraw(50), AtmStatus.CannotDispense },
        { 250, 0, Notes(5, 5, 0), null, Withdraw(30), AtmStatus.CannotDispense },
        { 100, 30, FullStock, null, Deposit(0, 0, 0), AtmStatus.InvalidAmount },
        { 100, 30, Notes(1, 1, 1), Denomination.Fifty, Deposit(1, 1, 1), AtmStatus.CassetteJammed },
        { 100, 30, Notes(5, 0, 0), null, Deposit(1, 1, 1), AtmStatus.CassetteFull },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void A_refusal_changes_nothing(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, Operation operation, AtmStatus status)
    {
        var (result, dispenser) = Run(balance, withdrawnToday, stock, jammed, operation);

        Assert.Equal(new AtmResult(status, NoteCounts.None, 0, new AccountState(balance, withdrawnToday)), result);
        Assert.Equal(stock, dispenser.Stock); // aucun billet déplacé
    }
}
