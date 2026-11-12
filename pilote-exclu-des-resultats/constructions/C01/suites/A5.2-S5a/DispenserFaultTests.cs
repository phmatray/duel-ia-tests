using static Atm.Tests.Fixture;

namespace Atm.Tests;

/// <summary>Le module rend false alors que les contrôles de la machine sont passés.</summary>
public class DispenserFaultTests
{
    /// <summary>Espionne un module défaillant pour voir les appels.</summary>
    private static (CashMachine Machine, SpyDispenser Spy) FaultyMachine(NoteCounts stock, Denomination? jammed = null,
        int failOnDispenseCall = int.MaxValue, int failOnLoadCall = int.MaxValue)
    {
        var spy = new SpyDispenser(new FaultyDispenser(stock, jammed, failOnDispenseCall, failOnLoadCall));
        return (new CashMachine(spy), spy);
    }

    [Theory]
    [InlineData(5, 5, 5, 50)]   // un seul passage
    [InlineData(5, 5, 5, 200)]  // 50×4, un seul passage
    [InlineData(0, 0, 5, 50)]   // 10×5, deux passages
    public void Panne_au_premier_passage_aucun_billet_sorti(int f, int t, int te, int amount)
    {
        var stock = Notes(f, t, te);
        var (machine, spy) = FaultyMachine(stock, failOnDispenseCall: 1);
        var account = new AccountState(250, 0);

        var result = machine.Execute(account, new Operation.Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Single(spy.DispenseCalls);
        Assert.Equal(stock, spy.Stock);
    }

    [Theory]
    [InlineData(0, 0, 5, 0, 50)]    // 10×5
    [InlineData(0, 5, 0, 0, 100)]   // 20×5
    [InlineData(0, 5, 5, 0, 110)]   // 20×5 10×1
    [InlineData(1, 5, 5, 0, 150)]   // 50×1 20×5
    [InlineData(5, 5, 5, 50, 100)]  // 20×5, 50 bloqué
    public void Panne_au_deuxieme_passage_rend_les_billets_deja_sortis(int f, int t, int te, int jammed, int amount)
    {
        var stock = Notes(f, t, te);
        var (machine, spy) = FaultyMachine(stock, Jam(jammed), failOnDispenseCall: 2);
        var account = new AccountState(250, 0);

        var result = machine.Execute(account, new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        // Exactement deux appels : un passage réussi, puis la panne, puis plus rien.
        Assert.Equal(2, spy.DispenseCalls.Count);
        Assert.True(spy.DispenseCalls[0].Result);
        Assert.False(spy.DispenseCalls[1].Result);
        Assert.InRange(spy.DispenseCalls[0].Notes.Total, 1, MoneyDispenser.MaxNotesPerPass);
        Assert.Equal(spy.DispenseCalls[0].Notes, result.Notes);
        Assert.Equal(stock - result.Notes, spy.Stock);
    }

    [Fact]
    public void Panne_on_s_arrete_aussitot()
    {
        var (machine, spy) = FaultyMachine(Notes(0, 0, 5), failOnDispenseCall: 1);

        machine.Execute(new(250, 0), new Operation.Withdraw(50));

        Assert.Single(spy.DispenseCalls);
        Assert.Empty(spy.LoadCalls);
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 1, 1, 1)]
    [InlineData(20, 1, 0, 1)]
    public void Panne_au_depot(int jammed, int f, int t, int te)
    {
        var stock = Notes(1, 1, 1);
        var (machine, spy) = FaultyMachine(stock, Jam(jammed), failOnLoadCall: 1);
        var account = new AccountState(100, 30);

        var result = machine.Execute(account, new Operation.Deposit(Notes(f, t, te)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Single(spy.LoadCalls);
        Assert.Empty(spy.DispenseCalls);
        Assert.Equal(stock, spy.Stock);
    }
}
