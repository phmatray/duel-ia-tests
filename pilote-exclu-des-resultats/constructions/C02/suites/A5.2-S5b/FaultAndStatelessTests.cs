using static Atm.Tests.N;

namespace Atm.Tests;

public class DispenserFaultTests
{
    [Fact]
    public void Withdraw_FirstPassFails_DispenserFault_NothingOut()
    {
        var dispenser = new FaultyDispenser(Full, successfulDispenses: 0);
        var r = new CashMachine(dispenser).Execute(Account(250, 0), Withdraw(60));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Account(250, 0), r.Account);
        Assert.Single(dispenser.Dispenses); // on s'arrête aussitôt
        Assert.Empty(dispenser.Loads);
    }

    [Fact]
    public void Withdraw_WithFee_FirstPassFails_FeeIsZero()
    {
        var dispenser = new FaultyDispenser(Full, successfulDispenses: 0);
        var r = new CashMachine(dispenser).Execute(Account(250, 0), Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Account(250, 0), r.Account);
        Assert.Equal(NoteCounts.None, r.Notes);
    }

    [Fact]
    public void Withdraw_SecondPassFails_DispenserFault_NotesAreThoseAlreadyOut()
    {
        // 20×5 (cassette 50 bloquée) : au moins deux passages.
        var dispenser = new FaultyDispenser(Full, Denomination.Fifty, successfulDispenses: 1);
        var r = new CashMachine(dispenser).Execute(Account(250, 0), Withdraw(100));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(2, dispenser.Dispenses.Count); // arrêt aussitôt après l'échec
        Assert.Equal(dispenser.SuccessfulDispenses[0], r.Notes);
        Assert.NotEqual(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Account(250, 0), r.Account);
    }

    [Fact]
    public void Withdraw_ThirdPassFails_NotesAreSumOfTwoFirstPasses()
    {
        // 50 + 20×5 + 10×5 = 200 : 11 billets, au moins trois passages.
        var dispenser = new FaultyDispenser(Notes(1, 5, 5), successfulDispenses: 2);
        var r = new CashMachine(dispenser).Execute(Account(250, 0), Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(3, dispenser.Dispenses.Count);
        Assert.Equal(dispenser.SuccessfulDispenses[0] + dispenser.SuccessfulDispenses[1], r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Account(250, 0), r.Account);
    }

    [Fact]
    public void Deposit_LoadFails_DispenserFault_NotesEmpty_AccountUnchanged()
    {
        var dispenser = new FaultyDispenser(Notes(1, 1, 1), loadResult: false);
        var r = new CashMachine(dispenser).Execute(Account(100, 30), Deposit(1, 1, 1));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Account(100, 30), r.Account);
        Assert.NotEmpty(dispenser.Loads);
        Assert.Empty(dispenser.Dispenses);
    }

    [Fact]
    public void Deposit_LoadSucceeds_WithFakeDispenser_Ok()
    {
        var dispenser = new FaultyDispenser(Notes(1, 1, 1), loadResult: true);
        var r = new CashMachine(dispenser).Execute(Account(100, 30), Deposit(0, 1, 0));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Account(120, 30), r.Account);
    }
}

public class StatelessTests
{
    [Fact]
    public void SameAccountTwice_ResultsDependOnlyOnInputs_NotOnPreviousCalls()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(Account(250, 0), Withdraw(50));
        var second = machine.Execute(Account(250, 0), Withdraw(50));

        Assert.Equal(first, second);
        Assert.Equal(Account(200, 50), second.Account);
        Assert.Equal(Notes(3, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void PreviousWithdrawal_DoesNotCountTowardDailyLimit_OnlyAccountDoes()
    {
        var machine = new CashMachine(new MoneyDispenser(Full));

        Assert.Equal(AtmStatus.Ok, machine.Execute(Account(250, 0), Withdraw(150)).Status);
        var r = machine.Execute(Account(250, 0), Withdraw(150));

        Assert.Equal(AtmStatus.Ok, r.Status);
    }

    [Fact]
    public void Machine_ReadsCurrentStockOfModule_AtEachCall()
    {
        var dispenser = new MoneyDispenser(Notes(0, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(Account(250, 0), Withdraw(50)).Status);

        Assert.True(dispenser.Load(Notes(1, 0, 0)));
        var r = machine.Execute(Account(250, 0), Withdraw(50));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Notes(1, 0, 0), r.Notes);
    }

    [Fact]
    public void DepositThenWithdraw_UsesDepositedNotes()
    {
        var dispenser = new MoneyDispenser(Notes(0, 0, 0));
        var machine = new CashMachine(dispenser);

        var dep = machine.Execute(Account(0, 0), Deposit(1, 1, 1));
        Assert.Equal(AtmStatus.Ok, dep.Status);

        var wd = machine.Execute(dep.Account, Withdraw(80));
        Assert.Equal(AtmStatus.Ok, wd.Status);
        Assert.Equal(Notes(1, 1, 1), wd.Notes);
        Assert.Equal(Account(0, 80), wd.Account);
        Assert.Equal(Notes(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void RefusalThenValidCall_IsUnaffected()
    {
        var machine = new CashMachine(new MoneyDispenser(Full));

        Assert.Equal(AtmStatus.InvalidAmount, machine.Execute(Account(250, 0), Withdraw(5)).Status);
        var r = machine.Execute(Account(100, 0), Withdraw(60));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Notes(1, 0, 1), r.Notes);
        Assert.Equal(Account(40, 60), r.Account);
    }
}
