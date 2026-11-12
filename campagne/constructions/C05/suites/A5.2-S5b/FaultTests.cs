using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le module rend <c>false</c> malgré les contrôles : panne.</summary>
public class FaultTests
{
    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var dispenser = new FaultyDispenser(Full, failOnDispense: 1);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, N(), 0, Acc(250, 0)), r);
        Assert.Equal(1, dispenser.DispenseCalls);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Panne_en_cours_de_retrait_rend_les_billets_deja_sortis_et_s_arrete()
    {
        // 150 avec 20×5 et 10×5 : 10 billets, donc au moins 3 passages.
        var stock = N(0, 5, 5);
        var dispenser = new FaultyDispenser(stock, failOnDispense: 2);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 0), r.Account);
        Assert.Equal(2, dispenser.DispenseCalls);
        Assert.Equal(0, dispenser.DispenseCallsAfterFailure);

        // Notes = exactement ce qui est sorti au premier passage.
        var sorti = stock - dispenser.Stock;
        Assert.InRange(sorti.Total, 1, MoneyDispenser.MaxNotesPerPass);
        Assert.Equal(sorti, r.Notes);
    }

    [Fact]
    public void Panne_au_dernier_passage_d_un_retrait()
    {
        // 120 avec 20×5 et 10×5 : 20×5 + 10×2 = 7 billets, donc au moins 2 passages.
        var stock = N(0, 5, 5);
        var probe = Machine(stock);
        probe.Machine.Execute(Acc(250, 0), new Operation.Withdraw(120));
        var passes = probe.Spy.Dispenses.Count;
        Assert.True(passes >= 2);

        var dispenser = new FaultyDispenser(stock, failOnDispense: passes);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), new Operation.Withdraw(120));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 0), r.Account);
        Assert.Equal(stock - dispenser.Stock, r.Notes);
        Assert.True(r.Notes.Total > 0);
    }

    [Fact]
    public void Panne_d_un_retrait_avec_frais_ne_preleve_pas_les_frais()
    {
        var dispenser = new FaultyDispenser(Full, failOnDispense: 1);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, N(), 0, Acc(250, 0)), r);
    }

    [Fact]
    public void Panne_d_un_depot()
    {
        var stock = N(1, 1, 1);
        var dispenser = new FaultyDispenser(stock, failOnLoad: true);
        var r = new CashMachine(dispenser).Execute(Acc(100, 30), new Operation.Deposit(N(1, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, N(), 0, Acc(100, 30)), r);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Equal(1, dispenser.LoadCalls);
    }
}
