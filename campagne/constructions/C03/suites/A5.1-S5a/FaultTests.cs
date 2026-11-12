namespace Atm.Tests;

/// <summary>Le module rend <c>false</c> malgré les contrôles : c'est une panne.</summary>
public class FaultTests
{
    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var dispenser = new FaultyDispenser(new NoteCounts(5, 5, 5), null, failOnCall: 1);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(110));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
        Assert.Equal(1, dispenser.Calls); // on s'arrête aussitôt
    }

    [Fact]
    public void Panne_en_cours_de_retrait_rend_les_billets_deja_sortis()
    {
        // 150 avec seulement 20 et 10 : 20×5 10×5, soit 10 billets, donc plusieurs passages.
        var dispenser = new FaultyDispenser(new NoteCounts(0, 5, 5), null, failOnCall: 2);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(2, dispenser.Calls); // on s'arrête aussitôt, sans tenter de passage suivant
        var alreadyOut = Assert.Single(dispenser.SuccessfulDispenses);
        Assert.Equal(alreadyOut, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
    }

    [Fact]
    public void Panne_d_un_retrait_ne_preleve_pas_les_frais()
    {
        var dispenser = new FaultyDispenser(new NoteCounts(5, 5, 5), null, failOnCall: 1);
        var result = new CashMachine(dispenser).Execute(new AccountState(115, 40), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(115, 40)), result);
    }

    [Fact]
    public void Panne_lors_d_un_depot()
    {
        var dispenser = new FaultyDispenser(new NoteCounts(1, 1, 1), null, failOnCall: 1);
        var result = new CashMachine(dispenser).Execute(new AccountState(100, 50), new Operation.Deposit(new NoteCounts(1, 1, 1)));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(100, 50), result.Account);
        Assert.Equal(1, dispenser.Calls);
    }
}
