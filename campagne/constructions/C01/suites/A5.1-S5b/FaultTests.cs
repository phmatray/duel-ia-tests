namespace Atm.Tests;

/// <summary>Le module rend false malgré les contrôles : panne.</summary>
public class FaultTests
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    [Fact]
    public void Withdraw_failing_on_first_pass_is_a_fault_with_no_notes()
    {
        var dispenser = new FaultyDispenser(Full, failDispenseFromCall: 1);
        var account = new AccountState(250, 0);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(120));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Single(dispenser.Dispensed); // on s'arrête aussitôt
    }

    [Fact]
    public void Withdraw_failing_on_a_later_pass_returns_the_notes_already_out()
    {
        // 150 avec 20×5 et 10×5 : 10 billets, donc au moins 3 passages.
        var dispenser = new FaultyDispenser(new NoteCounts(0, 5, 5), failDispenseFromCall: 2);
        var account = new AccountState(250, 0);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(2, dispenser.Dispensed.Count); // arrêt après le passage en échec
        Assert.Single(dispenser.SucceededDispenses);
        Assert.Equal(dispenser.SucceededDispenses[0], result.Notes);
        Assert.InRange(result.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass);
    }

    [Fact]
    public void Withdraw_failing_on_the_third_pass_returns_two_passes_of_notes()
    {
        var dispenser = new FaultyDispenser(new NoteCounts(0, 5, 5), failDispenseFromCall: 3);
        var account = new AccountState(250, 40);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(3, dispenser.Dispensed.Count);
        Assert.Equal(dispenser.SucceededDispenses[0] + dispenser.SucceededDispenses[1], result.Notes);
        Assert.Equal(new NoteCounts(0, 5, 5) - result.Notes, dispenser.Stock);
    }

    [Fact]
    public void Deposit_failing_is_a_fault_with_no_notes()
    {
        var dispenser = new FaultyDispenser(new NoteCounts(1, 1, 1), failLoadFromCall: 1);
        var account = new AccountState(100, 30);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(new NoteCounts(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal(new NoteCounts(1, 1, 1), dispenser.Stock);
    }
}
