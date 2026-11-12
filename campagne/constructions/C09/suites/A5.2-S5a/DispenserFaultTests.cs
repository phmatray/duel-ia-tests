using static Atm.Tests.Machine;

namespace Atm.Tests;

/// <summary>
/// « Si le module rend false malgré ces contrôles, c'est une panne. » Le vrai module ne le fait jamais dans le domaine,
/// on simule donc la panne avec un module de test enveloppant le vrai.
/// </summary>
public class DispenserFaultTests
{
    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var faulty = new FaultyDispenser(new MoneyDispenser(Full), failOnDispenseCall: 1);
        var r = new CashMachine(faulty).Execute(new AccountState(250, 0), new Operation.Withdraw(190));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(250, 0), r.Account);
        Assert.Equal(1, faulty.DispenseCalls); // on s'arrête aussitôt
        Assert.Equal(Full, faulty.Stock);
    }

    [Theory]
    [InlineData(1, 5, 5, 190)] // 10 billets : au moins 3 passages
    [InlineData(0, 5, 5, 150)] // 10 billets : au moins 3 passages
    public void Panne_en_cours_de_retrait_rend_les_billets_deja_sortis(int sf, int st, int s10, int amount)
    {
        var stock = Notes(sf, st, s10);
        var faulty = new FaultyDispenser(new MoneyDispenser(stock), failOnDispenseCall: 2);
        var r = new CashMachine(faulty).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(faulty.Dispensed, r.Notes);
        Assert.InRange(r.Notes.Total, 1, 4);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(250, 0), r.Account);
        Assert.Equal(2, faulty.DispenseCalls); // aucun passage après la panne
        Assert.Equal(stock - r.Notes, faulty.Stock);
    }

    [Fact]
    public void Panne_au_troisieme_passage_rend_les_deux_premiers()
    {
        // 190 avec 50x1 20x5 10x5 : 50 + 5 x 20 + 4 x 10, 10 billets, donc au moins 3 passages.
        var stock = Notes(1, 5, 5);
        var faulty2 = new FaultyDispenser(new MoneyDispenser(stock), failOnDispenseCall: 3);
        var r2 = new CashMachine(faulty2).Execute(new AccountState(250, 0), new Operation.Withdraw(190));

        Assert.Equal(AtmStatus.DispenserFault, r2.Status);
        Assert.Equal(faulty2.Dispensed, r2.Notes);
        Assert.InRange(r2.Notes.Total, 2, 8);
        Assert.Equal(0, r2.Fee);
        Assert.Equal(new AccountState(250, 0), r2.Account);
        Assert.Equal(3, faulty2.DispenseCalls);
        Assert.Equal(stock - r2.Notes, faulty2.Stock);
    }

    [Fact]
    public void Panne_lors_d_un_depot()
    {
        var faulty = new FaultyDispenser(new MoneyDispenser(Notes(1, 1, 1)), failLoad: true);
        var r = new CashMachine(faulty).Execute(new AccountState(100, 40), new Operation.Deposit(Notes(1, 1, 1)));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(100, 40), r.Account);
        Assert.Equal(1, faulty.LoadCalls);
        Assert.Equal(Notes(1, 1, 1), faulty.Stock);
    }
}
