using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Règles du modèle : le module détient les billets, la machine est sans mémoire.</summary>
public class MachineModelTests
{
    [Fact]
    public void Machine_reads_the_current_stock_of_the_module_at_each_call()
    {
        var (machine, dispenser) = Machine(N(0, 0, 0));

        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(Acc(250, 0), new Operation.Withdraw(50)).Status);

        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(250, 0), new Operation.Deposit(N(1, 0, 0))).Status);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);

        var result = machine.Execute(Acc(250, 0), new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, Acc(200, 50)), result);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Machine_keeps_no_account_memory_between_calls()
    {
        var (machine, _) = Machine(N(5, 5, 5));

        var first = machine.Execute(Acc(100, 0), new Operation.Withdraw(50));
        var second = machine.Execute(Acc(100, 0), new Operation.Withdraw(50));

        Assert.Equal(first, second);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, Acc(50, 50)), second);
    }

    [Fact]
    public void Same_inputs_on_fresh_modules_give_same_result()
    {
        var a = Withdraw(Acc(250, 0), N(3, 3, 3), Denomination.Twenty, 170);
        var b = Withdraw(Acc(250, 0), N(3, 3, 3), Denomination.Twenty, 170);

        Assert.Equal(a, b);
    }
}
