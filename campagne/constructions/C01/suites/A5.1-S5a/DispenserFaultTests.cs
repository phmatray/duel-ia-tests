using static Atm.Tests.TestSupport;

namespace Atm.Tests;

/// <summary>
/// « Si le module rend false malgré ces contrôles, c'est une panne. »
/// Le vrai module ne le fait jamais dans le domaine : on simule la panne avec un module de test.
/// </summary>
public class DispenserFaultTests
{
    [Fact]
    public void Withdrawal_failing_on_the_first_pass_is_a_fault_with_no_notes()
    {
        var dispenser = new FaultyDispenser(new MoneyDispenser(N(5, 5, 5)), failOnCall: 1);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        var result = machine.Execute(account, new Operation.Withdraw(60));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(1, dispenser.Calls);
    }

    [Theory]
    [InlineData(0, 5, 0, 100)] // 20×5 : au moins deux passages
    [InlineData(1, 5, 0, 150)] // 50×1 20×5 : au moins deux passages
    public void Withdrawal_failing_on_a_later_pass_returns_the_notes_already_dispensed_and_stops(int s50, int s20, int s10, int amount)
    {
        var dispenser = new FaultyDispenser(new MoneyDispenser(N(s50, s20, s10)), failOnCall: 2);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        var result = machine.Execute(account, new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(2, dispenser.Calls); // on s'arrête aussitôt
        var single = Assert.Single(dispenser.Dispensed);
        Assert.Equal(single, result.Notes);
        Assert.InRange(result.Notes.Total, 1, 4);
    }

    [Fact]
    public void Fee_is_not_charged_on_a_fault()
    {
        var dispenser = new FaultyDispenser(new MoneyDispenser(N(5, 5, 5)), failOnCall: 1);
        var account = new AccountState(250, 0);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
    }

    [Fact]
    public void Deposit_failing_to_load_is_a_fault_with_no_notes()
    {
        var dispenser = new FaultyDispenser(new MoneyDispenser(N(0, 0, 0)), failOnCall: 1);
        var account = new AccountState(100, 60);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }
}
