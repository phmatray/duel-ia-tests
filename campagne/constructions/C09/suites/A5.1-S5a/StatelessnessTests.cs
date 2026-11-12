using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>La machine est sans mémoire : elle ne dépend que du compte, de l'opération et du module.</summary>
public class StatelessnessTests
{
    [Fact]
    public void Un_appel_precedent_n_influe_que_par_le_module()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(Acc(250, 0), Withdraw(150));
        Assert.Equal(AtmStatus.Ok, first.Status);

        // Second appel avec un compte neuf : le retiré du jour vient du compte passé, pas de la mémoire de la machine.
        var second = machine.Execute(Acc(250, 0), Withdraw(150));
        var fresh = new CashMachine(new MoneyDispenser(dispenser.Stock)).Execute(Acc(250, 0), Withdraw(150));

        Assert.Equal(fresh, second);
    }

    [Fact]
    public void Un_refus_n_altere_pas_les_appels_suivants()
    {
        var dispenser = new MoneyDispenser(N(0, 5, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.CassetteFull, machine.Execute(Acc(10, 0), Deposit(N(0, 1, 0))).Status);
        var r = machine.Execute(Acc(10, 0), Withdraw(10));

        Assert.Equal(AtmStatus.CannotDispense, r.Status);
        Assert.Equal(N(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Le_stock_lu_est_celui_du_module_au_moment_de_l_appel()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(Acc(250, 0), Withdraw(50)).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(Acc(0, 0), Deposit(N(1, 0, 0))).Status);
        var r = machine.Execute(Acc(250, 0), Withdraw(50));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 0, 0), r.Notes);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }
}
