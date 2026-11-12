using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le module rend false malgré les contrôles : DispenserFault.</summary>
public class FaultTests
{
    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5), okDispenses: 0);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), W(60));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Single(dispenser.DispenseCalls); // on s'arrête aussitôt
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void Panne_au_deuxieme_passage_rend_les_billets_deja_sortis()
    {
        // 190 = 50×3 20×2 : 5 billets, donc au moins 2 passages.
        var dispenser = new FaultyDispenser(N(5, 5, 5), okDispenses: 1);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), W(190));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(2, dispenser.DispenseCalls.Count); // arrêt immédiat après l'échec
        Assert.Equal(dispenser.DispenseCalls[0], r.Notes); // seuls les billets du 1er passage sont sortis
        Assert.InRange(r.Notes.Total, 1, 4);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 0), r.Account);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void Panne_au_dernier_passage_rend_tous_les_passages_reussis()
    {
        // 10×5 depuis la seule cassette de 10 : au moins 2 passages.
        var probe = new SpyDispenser(new MoneyDispenser(N(0, 0, 5)));
        new CashMachine(probe).Execute(Acc(250, 0), W(50));
        var passes = probe.Dispensed.Count;
        Assert.True(passes >= 2);

        var dispenser = new FaultyDispenser(N(0, 0, 5), okDispenses: passes - 1);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), W(50));

        var alreadyOut = dispenser.DispenseCalls.Take(passes - 1).Aggregate(NoteCounts.None, (a, b) => a + b);
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, alreadyOut, 0, Acc(250, 0)), r);
        Assert.Equal(passes, dispenser.DispenseCalls.Count);
    }

    [Fact]
    public void Panne_lors_d_un_depot()
    {
        var dispenser = new FaultyDispenser(N(1, 1, 1), loadOk: false);
        var r = new CashMachine(dispenser).Execute(Acc(10, 30), D(N(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(10, 30)), r);
        Assert.Single(dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Fact]
    public void Un_retrait_avec_frais_en_panne_ne_preleve_pas_de_frais()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5), okDispenses: 0);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), W(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(250, 0)), r);
    }
}
