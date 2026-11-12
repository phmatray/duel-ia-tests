using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>« Si le module rend false malgré ces contrôles, c'est une panne. »</summary>
public class DispenserFaultTests
{
    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var dispenser = new FaultyDispenser(Notes(5, 5, 5), null, failOnCall: 1);
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(Account(250, 0), Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Account(250, 0)), result);
        Assert.Equal(1, dispenser.Calls);
        Assert.Equal(Notes(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Panne_en_cours_de_retrait_rend_les_billets_deja_sortis()
    {
        // 150 sans 50 : 20×5 + 10×5, 10 billets, donc 3 passages au moins.
        var dispenser = new FaultyDispenser(Notes(0, 5, 5), null, failOnCall: 2);
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(Account(250, 0), Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(250, 0), result.Account);
        Assert.Equal(dispenser.Dispensed[0], result.Notes);
        Assert.Equal(Notes(0, 5, 5) - result.Notes, dispenser.Stock);
    }

    [Fact]
    public void Panne_en_cours_de_retrait_arrete_aussitot()
    {
        var dispenser = new FaultyDispenser(Notes(0, 5, 5), null, failOnCall: 2);
        var machine = new CashMachine(dispenser);

        machine.Execute(Account(250, 0), Withdraw(150));

        Assert.Equal(2, dispenser.Calls);
        Assert.Empty(dispenser.Loaded);
    }

    [Fact]
    public void Panne_au_dernier_passage_rend_tous_les_passages_precedents()
    {
        var dispenser = new FaultyDispenser(Notes(0, 5, 5), null, failOnCall: 3);
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(Account(250, 0), Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(3, dispenser.Calls);
        Assert.Equal(dispenser.Dispensed[0] + dispenser.Dispensed[1], result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(250, 0), result.Account);
    }

    [Fact]
    public void Panne_lors_d_un_depot()
    {
        var dispenser = new FaultyDispenser(Notes(1, 1, 1), null, failOnCall: 1);
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(Account(100, 40), Deposit(Notes(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Account(100, 40)), result);
        Assert.Equal(1, dispenser.Calls);
        Assert.Equal(Notes(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Un_refus_ne_sollicite_pas_le_module()
    {
        var dispenser = new FaultyDispenser(Notes(5, 5, 5), null, failOnCall: 1);
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(Account(0, 0), Withdraw(10));

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
        Assert.Equal(0, dispenser.Calls);
    }
}
