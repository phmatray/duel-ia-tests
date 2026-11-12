using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// « Si le module rend false malgré ces contrôles, c'est une panne. » Le vrai module ne le fait jamais
/// dans le domaine : on simule la panne en enveloppant le vrai module.
/// </summary>
public class FaultTests
{
    [Fact]
    public void Withdraw_fault_on_first_pass_returns_DispenserFault_with_no_notes()
    {
        var spy = new SpyDispenser(N(5, 5, 5)) { FailDispenseFromCall = 1 };
        var machine = new CashMachine(spy);

        var result = machine.Execute(Acc(250, 40), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(250, 40)), result);
        Assert.Single(spy.DispenseCalls); // on s'arrête aussitôt
        Assert.Equal(N(5, 5, 5), spy.Stock);
    }

    [Fact]
    public void Withdraw_fault_mid_way_returns_notes_already_dispensed_and_stops()
    {
        // 200 € avec 50×1 20×5 10×5 : 11 billets, donc au moins 3 passages.
        var spy = new SpyDispenser(N(1, 5, 5)) { FailDispenseFromCall = 2 };
        var machine = new CashMachine(spy);

        var result = machine.Execute(Acc(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(2, spy.DispenseCalls.Count); // arrêt au premier échec
        Assert.Equal(spy.DispenseCalls[0], result.Notes);
        Assert.True(result.Notes.Total > 0);
        Assert.Equal(spy.SuccessfullyDispensed, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(250, 0), result.Account);
    }

    [Fact]
    public void Withdraw_fault_on_third_pass_returns_first_two_passes()
    {
        var spy = new SpyDispenser(N(1, 5, 5)) { FailDispenseFromCall = 3 };
        var machine = new CashMachine(spy);

        var result = machine.Execute(Acc(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(3, spy.DispenseCalls.Count);
        Assert.Equal(spy.DispenseCalls[0] + spy.DispenseCalls[1], result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(250, 0), result.Account);
    }

    [Fact]
    public void Deposit_fault_returns_DispenserFault_with_no_notes()
    {
        var spy = new SpyDispenser(N(1, 1, 1)) { FailLoad = true };
        var machine = new CashMachine(spy);

        var result = machine.Execute(Acc(10, 30), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(10, 30)), result);
        Assert.Equal(N(1, 1, 1), spy.Stock);
    }
}
