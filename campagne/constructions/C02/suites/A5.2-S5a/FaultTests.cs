using static Atm.Tests.Atm;

namespace Atm.Tests;

/// <summary>
/// Le module rend false malgré les contrôles. Le vrai module ne le fait jamais dans le domaine :
/// on simule la panne avec un module scripté.
/// </summary>
public class FaultTests
{
    [Fact]
    public void Withdraw_fault_on_first_pass_returns_no_notes_and_unchanged_account()
    {
        var dispenser = new FaultyDispenser(Full, null, false);

        var result = new CashMachine(dispenser).Execute(Account(250, 0), Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Account(250, 0)), result);
        Assert.Single(dispenser.DispenseCalls);
    }

    [Fact]
    public void Withdraw_fault_mid_way_returns_notes_already_dispensed_and_stops()
    {
        // 200 avec 50×1 20×5 10×5 : 15 billets, donc au moins 4 passages.
        var dispenser = new FaultyDispenser(Notes(1, 5, 5), null, true, false, true, true, true);

        var result = new CashMachine(dispenser).Execute(Account(250, 0), Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee); // frais de 5 normalement
        Assert.Equal(Account(250, 0), result.Account);
        Assert.Equal(2, dispenser.DispenseCalls.Count); // arrêt aussitôt après la panne
        Assert.Equal(dispenser.DispenseCalls[0], result.Notes);
        Assert.Equal(Notes(1, 5, 5) - dispenser.DispenseCalls[0], dispenser.Stock);
    }

    [Fact]
    public void Deposit_fault_returns_no_notes_and_unchanged_account()
    {
        var dispenser = new FaultyDispenser(Notes(1, 1, 1), null, false);

        var result = new CashMachine(dispenser).Execute(Account(100, 30), Deposit(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Account(100, 30)), result);
        Assert.Single(dispenser.LoadCalls);
    }
}
