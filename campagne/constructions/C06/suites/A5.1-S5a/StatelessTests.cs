using static Atm.Tests.TestKit;

namespace Atm.Tests;

/// <summary>« La machine est sans mémoire entre deux appels : elle ne dépend que du compte, de l'opération et du module. »</summary>
public class StatelessTests
{
    [Fact]
    public void A_second_call_depends_only_on_the_account_given_not_on_the_previous_call()
    {
        var dispenser = new MoneyDispenser(FullStock);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        var first = machine.Execute(account, Withdraw(100));
        var second = machine.Execute(account, Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 0), 0, new AccountState(150, 100)), first);
        Assert.Equal(first, second);
        Assert.Equal(Notes(1, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void A_later_call_sees_the_current_stock_of_the_module()
    {
        var dispenser = new MoneyDispenser(Notes(2, 0, 0));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), Withdraw(100));
        var second = machine.Execute(new AccountState(250, 0), Withdraw(50));
        var third = machine.Execute(new AccountState(250, 0), Deposit(1, 0, 0));
        var fourth = machine.Execute(new AccountState(250, 0), Withdraw(50));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(AtmStatus.CannotDispense, second.Status);
        Assert.Equal(AtmStatus.Ok, third.Status);
        Assert.Equal(AtmStatus.Ok, fourth.Status);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void A_refused_call_does_not_affect_the_next_one()
    {
        var machine = new CashMachine(new MoneyDispenser(FullStock));

        machine.Execute(new AccountState(0, 200), Withdraw(50));
        var result = machine.Execute(new AccountState(250, 0), Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 0, 0), 0, new AccountState(200, 50)), result);
    }
}
