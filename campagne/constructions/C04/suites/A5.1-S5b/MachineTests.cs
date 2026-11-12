using static Atm.Tests.T;

namespace Atm.Tests;

public class MachineTests
{
    [Fact]
    public void Implemente_ICashMachine()
    {
        ICashMachine machine = new CashMachine(new MoneyDispenser(N(1, 1, 1)));
        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(100, 0), W(10)).Status);
    }

    [Fact]
    public void Sans_memoire_entre_deux_appels()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        // Un refus puis un succès, puis une opération rejouée sur un module dans le même état.
        Assert.Equal(AtmStatus.InsufficientFunds, machine.Execute(Acc(0, 0), W(50)).Status);
        var first = machine.Execute(Acc(250, 0), W(80));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, Acc(170, 80)), first);
        Assert.Equal(N(4, 4, 4), dispenser.Stock);

        // Le compte rendu n'est pas réutilisé : c'est celui passé en argument qui compte.
        var second = machine.Execute(Acc(250, 0), W(80));
        Assert.Equal(first, second);
        Assert.Equal(N(3, 3, 3), dispenser.Stock);

        // Le stock, lui, vient du module : la machine le relit à chaque appel.
        dispenser.Dispense(N(3, 0, 0));
        var third = machine.Execute(Acc(250, 0), W(70));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 1), 0, Acc(180, 70)), third);
        Assert.Equal(N(0, 0, 2), dispenser.Stock);
    }

    [Fact]
    public void Un_depot_puis_un_retrait_utilisent_le_meme_module()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(Acc(250, 0), W(50)).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(0, 0), D(N(1, 0, 0))).Status);
        var r = machine.Execute(Acc(250, 0), W(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, Acc(200, 50)), r);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }
}
