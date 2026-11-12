namespace Atm.Tests;

/// <summary>La machine ne garde rien entre deux appels : seul compte le compte, l'opération et le module.</summary>
public class StatelessTests
{
    [Fact]
    public void SecondCall_UsesOnlyItsOwnAccount()
    {
        var machine = new CashMachine(new MoneyDispenser(N.Of(5, 5, 5)));
        machine.Execute(new AccountState(250, 0), new Operation.Withdraw(100));

        var r = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(100));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N.Of(2, 0, 0), 0, new(150, 100)), r);
    }

    [Fact]
    public void RefusalDoesNotAffectNextCall()
    {
        var machine = new CashMachine(new MoneyDispenser(N.Of(5, 5, 5)));
        machine.Execute(new AccountState(0, 200), new Operation.Withdraw(10));
        var r = machine.Execute(new AccountState(100, 0), new Operation.Withdraw(10));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N.Of(0, 0, 1), 0, new(90, 10)), r);
    }

    [Fact]
    public void StockIsReadFromTheModuleOnEachCall()
    {
        var machine = new CashMachine(new MoneyDispenser(N.Of(1, 0, 1)));
        Assert.Equal(AtmStatus.Ok, machine.Execute(new(250, 0), new Operation.Withdraw(60)).Status);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(new(250, 0), new Operation.Withdraw(10)).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(new(250, 0), new Operation.Deposit(N.Of(0, 0, 1))).Status);
        var r = machine.Execute(new(250, 0), new Operation.Withdraw(10));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N.Of(0, 0, 1), 0, new(240, 10)), r);
    }

    [Fact]
    public void StockChangedOutsideTheMachine_IsSeen()
    {
        var dispenser = new MoneyDispenser(N.Of(0, 0, 0));
        var machine = new CashMachine(dispenser);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(new(250, 0), new Operation.Withdraw(20)).Status);
        Assert.True(dispenser.Load(N.Of(0, 1, 0)));
        Assert.Equal(AtmStatus.Ok, machine.Execute(new(250, 0), new Operation.Withdraw(20)).Status);
    }
}
