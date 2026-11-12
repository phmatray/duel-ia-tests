namespace Atm.Tests;

using static N;

/// <summary>La machine ne dépend que du compte, de l'opération et du module.</summary>
public class StatelessTests
{
    [Fact]
    public void Le_compte_vient_de_l_appel_pas_de_l_appel_precedent()
    {
        var machine = new CashMachine(new MoneyDispenser(Full));
        var first = machine.Execute(new(250, 0), new Operation.Withdraw(100));
        var second = machine.Execute(new(250, 0), new Operation.Withdraw(100));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Le_plafond_vient_du_compte_recu()
    {
        var machine = new CashMachine(new MoneyDispenser(Full));
        machine.Execute(new(250, 0), new Operation.Withdraw(200));
        var r = machine.Execute(new(250, 0), new Operation.Withdraw(200));
        Assert.Equal(AtmStatus.Ok, r.Status);
    }

    [Fact]
    public void Le_stock_est_lu_dans_le_module_a_chaque_appel()
    {
        var dispenser = new MoneyDispenser(C(1, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, machine.Execute(new(250, 0), new Operation.Withdraw(50)).Status);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(new(250, 0), new Operation.Withdraw(50)).Status);
    }

    [Fact]
    public void Un_changement_du_module_apres_construction_est_vu()
    {
        var dispenser = new MoneyDispenser(C(0, 0, 0));
        var machine = new CashMachine(dispenser);
        Assert.True(dispenser.Load(C(1, 0, 0)));

        var r = machine.Execute(new(250, 0), new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(1, 0, 0), r.Notes);
    }

    [Fact]
    public void Un_depot_alimente_les_retraits_suivants()
    {
        var dispenser = new MoneyDispenser(C(0, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, machine.Execute(new(0, 0), new Operation.Deposit(C(1, 1, 0))).Status);
        var r = machine.Execute(new(70, 0), new Operation.Withdraw(70));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(1, 1, 0), r.Notes);
        Assert.Equal(C(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Un_refus_prealable_ne_change_pas_le_resultat_suivant()
    {
        var reused = new CashMachine(new MoneyDispenser(C(2, 2, 2), Denomination.Ten));
        reused.Execute(new(0, 200), new Operation.Withdraw(5));
        reused.Execute(new(0, 200), new Operation.Withdraw(50));
        reused.Execute(new(0, 0), new Operation.Deposit(C(0, 0, 1)));
        reused.Execute(new(0, 0), new Operation.Deposit(NoteCounts.None));

        var fresh = new CashMachine(new MoneyDispenser(C(2, 2, 2), Denomination.Ten));
        var op = new Operation.Withdraw(90);
        Assert.Equal(fresh.Execute(new(100, 0), op), reused.Execute(new(100, 0), op));
    }
}
