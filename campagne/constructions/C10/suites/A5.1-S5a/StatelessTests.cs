using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>La machine est sans mémoire : seuls le compte, l'opération et le module comptent.</summary>
public class StatelessTests
{
    [Fact]
    public void Un_refus_precedent_n_influence_pas_l_appel_suivant()
    {
        var machine = new CashMachine(new MoneyDispenser(N(5, 5, 5)));

        machine.Execute(Acc(0, 200), W(10));
        machine.Execute(Acc(10, 0), W(110));

        var r = machine.Execute(Acc(250, 0), W(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(150, 100)), r);
    }

    [Fact]
    public void Le_compte_vient_de_l_appel_et_non_d_un_appel_precedent()
    {
        var machine = new CashMachine(new MoneyDispenser(N(5, 5, 5)));

        var first = machine.Execute(Acc(250, 0), W(100));
        var second = machine.Execute(Acc(250, 0), W(100));

        Assert.Equal(Acc(150, 100), first.Account);
        Assert.Equal(Acc(150, 100), second.Account);
    }

    [Fact]
    public void Le_stock_lu_est_celui_du_module_au_moment_de_l_appel()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(250), W(50)).Status);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(Acc(250), W(50)).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(0), D(1, 0, 0)).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(250), W(50)).Status);
    }
}
