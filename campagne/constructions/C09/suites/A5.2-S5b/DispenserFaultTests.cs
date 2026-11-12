using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>« Si le module rend false malgré ces contrôles, c'est une panne. »</summary>
public class DispenserFaultTests
{
    [Fact]
    public void Retrait_premier_passage_refuse_donne_DispenserFault_sans_billet()
    {
        var account = Acc(250, 0);
        var dispenser = new FaultyDispenser(N(5, 5, 5), null, failDispenseFromCall: 1);

        var r = new CashMachine(dispenser).Execute(account, Withdraw(110));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Single(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Retrait_panne_en_cours_de_route_rend_les_billets_deja_sortis(int failingPass)
    {
        // 20×5 10×5 = 10 billets : au moins 3 passages.
        var account = Acc(250, 0);
        var dispenser = new FaultyDispenser(N(0, 5, 5), null, failDispenseFromCall: failingPass);

        var r = new CashMachine(dispenser).Execute(account, Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(failingPass, dispenser.DispenseCalls.Count); // on s'arrête aussitôt
        Assert.False(dispenser.DispenseCalls[^1].Ok);
        Assert.NotEqual(NoteCounts.None, r.Notes);
        Assert.Equal(dispenser.Dispensed, r.Notes);
        Assert.Equal(N(0, 5, 5) - dispenser.Dispensed, dispenser.Stock);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Empty(dispenser.LoadCalls); // pas de tentative de remise en cassette
    }

    [Fact]
    public void Retrait_les_passages_avant_la_panne_respectent_la_limite_de_4()
    {
        var dispenser = new FaultyDispenser(N(0, 5, 5), null, failDispenseFromCall: 3);

        new CashMachine(dispenser).Execute(Acc(250, 0), Withdraw(150));

        Assert.All(dispenser.DispenseCalls, c => Assert.InRange(c.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass));
    }

    [Fact]
    public void Depot_refuse_par_le_module_donne_DispenserFault()
    {
        var account = Acc(40, 30);
        var dispenser = new FaultyDispenser(N(1, 1, 1), null, failLoadFromCall: 1);

        var r = new CashMachine(dispenser).Execute(account, Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Single(dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Un_refus_reste_un_refus_meme_avec_un_module_en_panne()
    {
        var account = Acc(110, 0);
        var dispenser = new FaultyDispenser(N(5, 5, 5), null, failDispenseFromCall: 1, failLoadFromCall: 1);

        var r = new CashMachine(dispenser).Execute(account, Withdraw(110));

        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
        Assert.Empty(dispenser.DispenseCalls);
    }
}
