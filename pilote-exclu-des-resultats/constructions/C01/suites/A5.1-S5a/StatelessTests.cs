using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>La machine ne garde rien entre deux appels : seul le module évolue.</summary>
public class StatelessTests
{
    [Fact]
    public void Withdrawn_today_comes_only_from_the_account_passed_in()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(250, 0), Withdraw(150)).Status);

        // Même machine, compte « neuf » : le retrait précédent n'est pas mémorisé.
        var second = machine.Execute(Acc(250, 0), Withdraw(150));
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(Acc(95, 150), second.Account);
    }

    [Fact]
    public void A_refusal_leaves_no_trace_for_the_next_call()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.DailyLimitExceeded, machine.Execute(Acc(250, 200), Withdraw(10)).Status);

        var next = machine.Execute(Acc(250, 0), Withdraw(10));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 0, 1), 0, Acc(240, 10)), next);
    }

    [Fact]
    public void Successive_calls_see_the_current_stock_of_the_module()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(250, 0), Withdraw(50)).Status);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(Acc(250, 0), Withdraw(50)).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(250, 0), Deposit(N(1, 0, 0))).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(250, 0), Withdraw(50)).Status);
    }

    [Fact]
    public void Same_inputs_give_same_result_on_a_fresh_machine()
    {
        var a = new CashMachine(new MoneyDispenser(N(3, 2, 1), Denomination.Twenty)).Execute(Acc(200, 20), Withdraw(110));
        var b = new CashMachine(new MoneyDispenser(N(3, 2, 1), Denomination.Twenty)).Execute(Acc(200, 20), Withdraw(110));
        Assert.Equal(a, b);
    }
}
