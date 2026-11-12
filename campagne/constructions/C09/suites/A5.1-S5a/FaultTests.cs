using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le module rend <c>false</c> malgré les contrôles : panne.</summary>
public class FaultTests
{
    [Fact]
    public void Retrait_premier_passage_refuse_donne_une_panne_sans_billets()
    {
        var dispenser = new FaultyDispenser(Full) { FailOnDispenseCall = 1 };
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), Withdraw(60));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 0), r.Account);
        Assert.Single(dispenser.DispenseCalls);
    }

    [Fact]
    public void Retrait_panne_au_second_passage_rend_les_billets_deja_sortis_et_s_arrete()
    {
        // 100 sans 50 : 20×5, donc au moins deux passages.
        var dispenser = new FaultyDispenser(N(0, 5, 5)) { FailOnDispenseCall = 2 };
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), Withdraw(100));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(2, dispenser.DispenseCalls.Count); // arrêt aussitôt après l'échec
        Assert.Equal(dispenser.DispenseCalls[0], r.Notes); // seul le premier passage est sorti
        Assert.True(r.Notes.Total > 0);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 0), r.Account);
    }

    [Fact]
    public void Retrait_avec_frais_en_panne_ne_preleve_pas_les_frais()
    {
        var dispenser = new FaultyDispenser(Full) { FailOnDispenseCall = 1 };
        var r = new CashMachine(dispenser).Execute(Acc(250, 50), Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 50), r.Account);
    }

    [Fact]
    public void Retrait_panne_au_troisieme_passage_cumule_les_deux_premiers()
    {
        // 140 sans 50 : 20×5 10×4 = 9 billets, donc au moins trois passages de 4 billets au plus.
        var dispenser = new FaultyDispenser(N(0, 5, 5)) { FailOnDispenseCall = 3 };
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), Withdraw(140));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(3, dispenser.DispenseCalls.Count);
        Assert.Equal(dispenser.DispenseCalls[0] + dispenser.DispenseCalls[1], r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 0), r.Account);
    }

    [Fact]
    public void Retrait_refuse_par_les_controles_n_appelle_pas_le_module()
    {
        var dispenser = new FaultyDispenser(Full);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), Withdraw(5));

        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void Depot_refuse_par_le_module_donne_une_panne()
    {
        var dispenser = new FaultyDispenser(N(1, 1, 1)) { LoadSucceeds = false };
        var r = new CashMachine(dispenser).Execute(Acc(100, 30), Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(100, 30), r.Account);
        Assert.Single(dispenser.LoadCalls);
    }

    [Fact]
    public void Depot_refuse_par_les_controles_n_appelle_pas_le_module()
    {
        var dispenser = new FaultyDispenser(N(0, 5, 0)) { LoadSucceeds = true };
        var r = new CashMachine(dispenser).Execute(Acc(100, 30), Deposit(N(0, 1, 0)));

        Assert.Equal(AtmStatus.CassetteFull, r.Status);
        Assert.Empty(dispenser.LoadCalls);
    }
}
