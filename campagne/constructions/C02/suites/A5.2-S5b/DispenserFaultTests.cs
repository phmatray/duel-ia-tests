using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// « Refus et pannes » : le module rend false malgré les contrôles.
/// Le vrai module ne le fait jamais dans le domaine ; on le simule ici avec un module défaillant.
/// </summary>
public class DispenserFaultTests
{
    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var d = new FaultyDispenser(N(5, 5, 5), null, failOnDispenseCall: 1);
        var r = new CashMachine(d).Execute(Acc(250, 0), W(60));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Single(d.DispenseCalls); // on s'arrête aussitôt
    }

    [Fact]
    public void Panne_au_second_passage_rend_les_billets_deja_sortis()
    {
        // 190 = 50×3 + 20×2 : 5 billets, donc 2 passages au moins.
        var d = new FaultyDispenser(N(5, 5, 5), null, failOnDispenseCall: 2);
        var r = new CashMachine(d).Execute(Acc(250, 0), W(190));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 0), r.Account);
        Assert.Equal(2, d.DispenseCalls.Count); // aucun passage après la panne
        Assert.Equal(d.DispenseCalls[0].Notes, r.Notes);
        Assert.Equal(d.Dispensed, r.Notes);
        Assert.InRange(r.Notes.Total, 1, 4);
    }

    [Fact]
    public void Panne_au_troisieme_passage_rend_les_deux_premiers()
    {
        // 140 sans 50 : 20×5 + 10×4, 9 billets, donc 3 passages au moins.
        var d = new FaultyDispenser(N(5, 5, 5), Denomination.Fifty, failOnDispenseCall: 3);
        var r = new CashMachine(d).Execute(Acc(250, 0), W(140));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 0), r.Account);
        Assert.Equal(3, d.DispenseCalls.Count);
        Assert.Equal(d.DispenseCalls[0].Notes + d.DispenseCalls[1].Notes, r.Notes);
        Assert.Equal(d.Dispensed, r.Notes);
    }

    [Fact]
    public void Panne_lors_d_un_depot()
    {
        var d = new FaultyDispenser(N(1, 1, 1), null, failOnLoad: true);
        var r = new CashMachine(d).Execute(Acc(100, 30), D(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(100, 30)), r);
        Assert.Equal(N(1, 1, 1), d.Stock);
    }

    [Fact]
    public void Un_retrait_sans_panne_via_le_module_simule_reste_normal()
    {
        // Contrôle du module simulé lui-même.
        var d = new FaultyDispenser(N(5, 5, 5), null);
        var r = new CashMachine(d).Execute(Acc(250, 0), W(190));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(3, 2, 0), 5, Acc(55, 190)), r);
    }
}
