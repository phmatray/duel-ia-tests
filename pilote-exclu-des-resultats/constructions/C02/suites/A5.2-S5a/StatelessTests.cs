using static Atm.Tests.T;

namespace Atm.Tests;

public class StatelessTests
{
    [Fact]
    public void Machine_has_no_memory_of_previous_accounts()
    {
        var dispenser = new SpyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(Acc(250, 0), W(200));
        var second = machine.Execute(Acc(250, 0), W(50));

        Assert.Equal(AtmStatus.Ok, first.Status);
        // Le second appel ne voit pas le retrait du premier : seul le compte passé compte.
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(Acc(200, 50), second.Account);
        Assert.Equal(N(0, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Machine_follows_the_module_stock_between_calls()
    {
        var dispenser = new SpyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(250, 0), W(50)).Status);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(Acc(250, 0), W(50)).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(0, 0), D(1, 0, 0)).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(250, 0), W(50)).Status);
    }

    [Fact]
    public void Same_inputs_give_same_result()
    {
        var a = new CashMachine(new MoneyDispenser(N(3, 2, 4), Denomination.Fifty)).Execute(Acc(135, 40), W(70));
        var b = new CashMachine(new MoneyDispenser(N(3, 2, 4), Denomination.Fifty)).Execute(Acc(135, 40), W(70));

        Assert.Equal(a, b);
    }
}
