using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>La machine est sans mémoire : seuls le compte, l'opération et le module comptent.</summary>
public class StatelessTests
{
    [Fact]
    public void Le_plafond_vient_du_compte_recu_pas_des_appels_precedents()
    {
        var (machine, _) = Machine(Full);
        var first = machine.Execute(Acc(250, 0), new Operation.Withdraw(200));
        Assert.Equal(AtmStatus.Ok, first.Status);

        // Nouveau compte, rien retiré : la machine ne doit pas se souvenir des 200 précédents.
        var second = machine.Execute(Acc(100, 0), new Operation.Withdraw(10));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 0, 1), 0, Acc(90, 10)), second);
    }

    [Fact]
    public void Le_solde_vient_du_compte_recu()
    {
        var (machine, _) = Machine(Full);
        machine.Execute(Acc(10, 0), new Operation.Deposit(N(1, 0, 0)));

        var r = machine.Execute(Acc(10, 0), new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    [Fact]
    public void Le_stock_est_lu_dans_le_module_a_chaque_appel()
    {
        var (machine, spy) = Machine(N());
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(Acc(250, 0), new Operation.Withdraw(10)).Status);

        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(250, 0), new Operation.Deposit(N(0, 0, 1))).Status);

        var r = machine.Execute(Acc(250, 0), new Operation.Withdraw(10));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 0, 1), 0, Acc(240, 10)), r);
        Assert.Equal(N(), spy.Stock);
    }

    [Fact]
    public void Un_retrait_vide_le_stock_pour_le_suivant()
    {
        var (machine, _) = Machine(N(1, 0, 0));
        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(250, 0), new Operation.Withdraw(50)).Status);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(Acc(250, 0), new Operation.Withdraw(50)).Status);
    }

    [Fact]
    public void Un_refus_n_influence_pas_l_appel_suivant()
    {
        var (machine, _) = Machine(Full);
        Assert.Equal(AtmStatus.DailyLimitExceeded, machine.Execute(Acc(250, 200), new Operation.Withdraw(10)).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(250, 0), new Operation.Withdraw(10)).Status);
    }
}
