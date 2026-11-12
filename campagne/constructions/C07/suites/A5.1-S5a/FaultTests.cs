using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Le module rend <c>false</c> malgré les contrôles : panne.
/// Hors du domaine du vrai module, d'où un module défaillant de test.
/// </summary>
public class FaultTests
{
    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5), null, failOnDispenseCall: 1, failOnLoad: false);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Single(dispenser.DispenseCalls);
    }

    [Fact]
    public void Panne_avec_frais_les_frais_ne_sont_pas_preleves()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5), null, failOnDispenseCall: 1, failOnLoad: false);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), Withdraw(200));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(250, 0)), r);
    }

    [Fact]
    public void Panne_en_cours_de_retrait_rend_les_billets_deja_sortis_et_s_arrete()
    {
        // 150 sans 50 : 10 billets, donc au moins 3 passages ; panne au 2e.
        var dispenser = new FaultyDispenser(N(5, 5, 5), Denomination.Fifty, failOnDispenseCall: 2, failOnLoad: false);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 0), r.Account);

        Assert.Equal(2, dispenser.DispenseCalls.Count); // on s'arrête aussitôt
        var firstPass = dispenser.DispenseCalls[0];
        Assert.InRange(firstPass.Total, 1, MoneyDispenser.MaxNotesPerPass);
        Assert.Equal(firstPass, r.Notes);
        Assert.Equal(N(5, 5, 5) - firstPass, dispenser.Stock);
    }

    [Fact]
    public void Panne_au_dernier_passage_rend_tout_sauf_ce_passage()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5), Denomination.Fifty, failOnDispenseCall: 3, failOnLoad: false);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 0), r.Account);
        Assert.Equal(3, dispenser.DispenseCalls.Count);
        Assert.Equal(dispenser.DispenseCalls[0] + dispenser.DispenseCalls[1], r.Notes);
        Assert.Equal(N(5, 5, 5) - r.Notes, dispenser.Stock);
    }

    [Fact]
    public void Panne_lors_d_un_depot()
    {
        var dispenser = new FaultyDispenser(N(1, 1, 1), null, failOnDispenseCall: int.MaxValue, failOnLoad: true);
        var r = new CashMachine(dispenser).Execute(Acc(10, 30), Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(10, 30)), r);
        Assert.Single(dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Les_refus_passent_avant_toute_sollicitation_du_module()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5), null, failOnDispenseCall: 1, failOnLoad: true);
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.InvalidAmount, machine.Execute(Acc(250, 0), Withdraw(5)).Status);
        Assert.Equal(AtmStatus.DailyLimitExceeded, machine.Execute(Acc(250, 200), Withdraw(10)).Status);
        Assert.Equal(AtmStatus.InsufficientFunds, machine.Execute(Acc(0, 0), Withdraw(10)).Status);
        Assert.Equal(AtmStatus.InvalidAmount, machine.Execute(Acc(0, 0), Deposit(NoteCounts.None)).Status);
        Assert.Equal(AtmStatus.CassetteFull, machine.Execute(Acc(0, 0), Deposit(N(1, 0, 0))).Status);

        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
    }
}
