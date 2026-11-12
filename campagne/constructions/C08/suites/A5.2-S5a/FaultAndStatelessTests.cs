namespace Atm.Tests;

using static T;

/// <summary>Pannes du module (rend false malgré les contrôles) et absence de mémoire de la machine.</summary>
public class FaultAndStatelessTests
{
    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5), null, failOnDispenseCall: 1);
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(Acc(250, 0), W(60));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(250, 0)), result);
        Assert.Single(dispenser.Dispenses);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Panne_en_cours_de_retrait_rend_les_billets_deja_sortis_et_s_arrete(int failingPass)
    {
        // 150 sans 50 : 20×5 + 10×5, 10 billets, donc au moins trois passages.
        var dispenser = new FaultyDispenser(N(5, 5, 5), Denomination.Fifty, failOnDispenseCall: failingPass);
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(Acc(250, 0), W(150));

        var alreadyOut = dispenser.Dispenses.Where(d => d.Ok).Aggregate(NoteCounts.None, (acc, d) => acc + d.Notes);
        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(250, 0), result.Account);
        Assert.Equal(alreadyOut, result.Notes);
        Assert.Equal(N(5, 5, 5) - alreadyOut, dispenser.Stock);
        // On s'arrête aussitôt : le passage refusé est le dernier appel.
        Assert.Equal(failingPass, dispenser.Dispenses.Count);
        Assert.False(dispenser.Dispenses[^1].Ok);
    }

    [Fact]
    public void Panne_lors_d_un_depot()
    {
        var dispenser = new FaultyDispenser(N(1, 1, 1), null, failOnDispenseCall: 0, failLoad: true);
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(Acc(10, 30), D(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(10, 30)), result);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
        Assert.Empty(dispenser.Dispenses);
    }

    [Fact]
    public void La_machine_ne_depend_que_du_compte_de_l_operation_et_du_module()
    {
        var (machine, dispenser) = Real(N(1, 3, 0));

        // Un refus puis un succès sur la même machine…
        var refused = machine.Execute(Acc(0, 0), W(60));
        var first = machine.Execute(Acc(100, 0), W(60));
        // …puis la même opération : le stock a changé, le résultat suit le module.
        var second = machine.Execute(Acc(100, 0), W(60));

        Assert.Equal(AtmStatus.InsufficientFunds, refused.Status);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, Acc(40, 60)), first);
        AssertRefused(second, AtmStatus.CannotDispense, Acc(100, 0));

        // Une machine neuve sur un module dans le même état donne le même résultat.
        var fresh = new CashMachine(new MoneyDispenser(dispenser.Stock));
        Assert.Equal(second, fresh.Execute(Acc(100, 0), W(60)));
    }

    [Fact]
    public void Le_resultat_ne_depend_pas_des_comptes_precedents()
    {
        var (machine, _) = Real(N(5, 5, 5));

        machine.Execute(Acc(250, 190), W(10));
        var result = machine.Execute(Acc(250, 0), W(200));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(4, 0, 0), 5, Acc(45, 200)), result);
    }
}
