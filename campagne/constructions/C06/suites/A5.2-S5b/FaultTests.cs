using static Atm.Tests.Support;

namespace Atm.Tests;

/// <summary>
/// « Si le module rend false malgré ces contrôles, c'est une panne » : DispenserFault, arrêt immédiat,
/// Fee 0, compte identique, Notes = billets déjà sortis (retrait) ou vide (dépôt).
/// Le vrai module ne le fait jamais dans le domaine : on le simule avec un module factice.
/// </summary>
public class FaultTests
{
    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var dispenser = new FailingDispenser(Full, null, failOnCall: 1);
        var r = new CashMachine(dispenser).Withdraw(250, 0, 50);
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(250, 0)), r);
        Assert.Equal(1, dispenser.Calls);
    }

    [Fact]
    public void Panne_au_deuxieme_passage_rend_les_billets_deja_sortis_et_s_arrete()
    {
        // 150 sans 50 = 20×5 + 10×5 : 10 billets, donc au moins 3 passages.
        var dispenser = new FailingDispenser(Full, Denomination.Fifty, failOnCall: 2);
        var r = new CashMachine(dispenser).Withdraw(250, 0, 150);
        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(250, 0), r.Account);
        Assert.Single(dispenser.Dispensed);
        Assert.Equal(dispenser.Dispensed[0], r.Notes);
        Assert.Equal(2, dispenser.Calls); // on s'arrête aussitôt
        Assert.Equal(Full - r.Notes, dispenser.Stock);
    }

    [Fact]
    public void Panne_au_dernier_passage_rend_tous_les_billets_sauf_le_dernier_passage()
    {
        var dispenser = new FailingDispenser(Full, Denomination.Fifty, failOnCall: 3);
        var r = new CashMachine(dispenser).Withdraw(250, 0, 150);
        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(250, 0), r.Account);
        Assert.Equal(2, dispenser.Dispensed.Count);
        Assert.Equal(dispenser.Dispensed[0] + dispenser.Dispensed[1], r.Notes);
        Assert.Equal(3, dispenser.Calls);
    }

    [Fact]
    public void Panne_lors_d_un_depot()
    {
        var dispenser = new FailingDispenser(N(1, 1, 1), null, failOnCall: 1);
        var r = new CashMachine(dispenser).Deposit(10, 30, N(1, 1, 1));
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(10, 30)), r);
        Assert.Equal(1, dispenser.Calls);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }
}
