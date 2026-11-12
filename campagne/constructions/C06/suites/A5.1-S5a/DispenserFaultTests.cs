using static Atm.Tests.TestKit;

namespace Atm.Tests;

/// <summary>
/// « Si le module rend false malgré ces contrôles, c'est une panne. »
/// Le vrai module ne le fait jamais dans le domaine : on simule la panne avec un faux module.
/// La spec dit que ce cas « reste à traiter » et en décrit le résultat ; c'est ce qui est vérifié ici.
/// </summary>
public class DispenserFaultTests
{
    [Fact]
    public void Withdraw_fault_on_the_first_pass_returns_DispenserFault_with_no_notes()
    {
        var dispenser = new FaultyDispenser(FullStock, failOnCall: 1);

        var result = new CashMachine(dispenser).Execute(new AccountState(250, 40), Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(250, 40)), result);
        Assert.Equal(1, dispenser.Calls);
        Assert.Equal(FullStock, dispenser.Stock);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Withdraw_fault_mid_way_stops_at_once_and_returns_the_notes_already_out(int failOnCall)
    {
        // 150 avec 20×5 10×5 : 10 billets, donc au moins 3 passages.
        var stock = Notes(0, 5, 5);
        var dispenser = new FaultyDispenser(stock, failOnCall);

        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), Withdraw(150));

        var alreadyOut = dispenser.Accepted.Aggregate(NoteCounts.None, (a, b) => a + b);
        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(alreadyOut, result.Notes);
        Assert.Equal(stock - alreadyOut, dispenser.Stock);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
        Assert.Equal(failOnCall, dispenser.Calls); // on s'arrête aussitôt
    }

    [Fact]
    public void Deposit_fault_returns_DispenserFault_with_no_notes_and_the_account_unchanged()
    {
        var dispenser = new FaultyDispenser(Notes(1, 1, 1), failOnCall: 1);

        var result = new CashMachine(dispenser).Execute(new AccountState(100, 30), Deposit(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(100, 30)), result);
        Assert.Equal(Notes(1, 1, 1), dispenser.Stock);
    }
}
