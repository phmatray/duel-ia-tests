using static Atm.Tests.Harness;

namespace Atm.Tests;

/// <summary>La machine est sans mémoire : seuls le compte, l'opération et le module comptent.</summary>
public class StatelessTests
{
    [Fact]
    public void SameMachine_SuccessiveWithdrawals_DependOnlyOnGivenAccount()
    {
        var dispenser = new MoneyDispenser(FullStock);
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(100, 0), new Operation.Withdraw(60));
        var second = machine.Execute(new AccountState(100, 0), new Operation.Withdraw(60));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(new AccountState(40, 60), first.Account);
        Assert.Equal(new AccountState(40, 60), second.Account);
        Assert.Equal(N(1, 0, 1), second.Notes);
        Assert.Equal(N(3, 5, 3), dispenser.Stock);
    }

    [Fact]
    public void SameMachine_RefusalThenSuccess_IsUnaffectedByPreviousCall()
    {
        var dispenser = new MoneyDispenser(FullStock);
        var machine = new CashMachine(dispenser);

        var refused = machine.Execute(new AccountState(0, 0), new Operation.Withdraw(50));
        var ok = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.InsufficientFunds, refused.Status);
        Assert.Equal(AtmStatus.Ok, ok.Status);
        Assert.Equal(new AccountState(200, 50), ok.Account);
    }

    [Fact]
    public void SameMachine_UsesCurrentModuleStock()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        var deposit = machine.Execute(new AccountState(250, 0), new Operation.Deposit(N(1, 0, 0)));
        var third = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(AtmStatus.CannotDispense, second.Status);
        Assert.Equal(AtmStatus.Ok, deposit.Status);
        Assert.Equal(AtmStatus.Ok, third.Status);
        Assert.Equal(N(), dispenser.Stock);
    }
}
