using static Atm.Tests.Support;

namespace Atm.Tests;

/// <summary>Propriétés générales : la machine est sans mémoire, et tout passe par le module.</summary>
public class MachineTests
{
    [Fact]
    public void La_machine_ne_garde_rien_entre_deux_appels_apres_un_refus()
    {
        var (machine, _) = NewAtm(Full);
        machine.Execute(new AccountState(0, 0), Withdraw(100)); // refusé : solde insuffisant

        var result = machine.Execute(new AccountState(250, 0), Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, new AccountState(150, 100)), result);
    }

    [Fact]
    public void La_machine_ne_garde_rien_entre_deux_appels_apres_un_succes()
    {
        var (machine, dispenser) = NewAtm(Full);
        machine.Execute(new AccountState(250, 0), Withdraw(200));

        // Le compte passé est celui de l'appelant, pas celui du premier appel : le plafond n'est pas atteint.
        var result = machine.Execute(new AccountState(250, 0), Withdraw(60));

        // Il ne reste qu'un billet de 50 : 50 + 10.
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 1), 0, new AccountState(190, 60)), result);
        Assert.Equal(N(0, 5, 4), dispenser.Stock);
    }

    [Fact]
    public void Le_retrait_suivant_voit_le_stock_reel_du_module()
    {
        var (machine, dispenser) = NewAtm(N(1, 0, 0));
        Assert.Equal(AtmStatus.Ok, machine.Execute(new AccountState(250, 0), Withdraw(50)).Status);

        var result = machine.Execute(new AccountState(250, 0), Withdraw(50));

        AssertRefused(AtmStatus.CannotDispense, result, new AccountState(250, 0), dispenser, N(0, 0, 0));
    }

    [Fact]
    public void Un_depot_rend_les_billets_disponibles_pour_un_retrait()
    {
        var (machine, dispenser) = NewAtm(N(0, 0, 0));
        Assert.Equal(AtmStatus.Ok, machine.Execute(new AccountState(0, 0), Deposit(N(1, 1, 1))).Status);

        var result = machine.Execute(new AccountState(80, 0), Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, new AccountState(0, 80)), result);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void La_machine_lit_le_module_a_chaque_appel()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var machine = new CashMachine(dispenser);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(new AccountState(250, 0), Withdraw(20)).Status);

        Assert.True(dispenser.Load(N(0, 1, 0))); // chargé hors de la machine

        var result = machine.Execute(new AccountState(250, 0), Withdraw(20));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 1, 0), 0, new AccountState(230, 20)), result);
    }
}
