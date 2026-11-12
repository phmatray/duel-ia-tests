using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le module rend false malgré les contrôles : panne.</summary>
public class FaultTests
{
    [Fact]
    public void Retrait_panne_au_premier_passage()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5), failDispenseFrom: 1);
        var account = Acc(250, 20);

        var r = new CashMachine(dispenser).Execute(account, W(110));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), r);
        Assert.Equal(1, dispenser.DispenseCalls);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Retrait_panne_en_cours_rend_les_billets_deja_sortis_et_s_arrete()
    {
        // 200 avec 50×3 20×5 10×0 : 50×2 20×5, 7 billets, donc au moins deux passages.
        var dispenser = new FaultyDispenser(N(3, 5, 0), failDispenseFrom: 2);
        var account = Acc(250, 0);

        var r = new CashMachine(dispenser).Execute(account, W(200));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Single(dispenser.SuccessfulDispenses);
        Assert.Equal(dispenser.SuccessfulDispenses[0], r.Notes);
        Assert.NotEqual(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(2, dispenser.DispenseCalls); // arrêt aussitôt après l'échec
        Assert.Equal(N(3, 5, 0) - r.Notes, dispenser.Stock);
    }

    [Fact]
    public void Retrait_panne_en_cours_avec_des_billets_de_10()
    {
        // 100 avec 50×0 20×3 10×5 : 20×3 10×4, 7 billets.
        var dispenser = new FaultyDispenser(N(0, 3, 5), failDispenseFrom: 2);
        var account = Acc(250, 0);

        var r = new CashMachine(dispenser).Execute(account, W(100));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Single(dispenser.SuccessfulDispenses);
        Assert.Equal(dispenser.SuccessfulDispenses[0], r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(2, dispenser.DispenseCalls);
    }

    [Fact]
    public void Depot_panne_au_chargement()
    {
        var dispenser = new FaultyDispenser(N(1, 1, 1), failLoadFrom: 1);
        var account = Acc(100, 30);

        var r = new CashMachine(dispenser).Execute(account, D(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), r);
        Assert.Equal(1, dispenser.LoadCalls);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Refus_avant_le_module_ne_le_sollicite_pas_meme_en_panne()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.InvalidAmount, machine.Execute(Acc(100), W(5)).Status);
        Assert.Equal(AtmStatus.DailyLimitExceeded, machine.Execute(Acc(250, 200), W(10)).Status);
        Assert.Equal(AtmStatus.InsufficientFunds, machine.Execute(Acc(0), W(10)).Status);
        Assert.Equal(AtmStatus.InvalidAmount, machine.Execute(Acc(100), D(0, 0, 0)).Status);
        Assert.Equal(AtmStatus.CassetteFull, machine.Execute(Acc(100), D(1, 0, 0)).Status);
    }
}
