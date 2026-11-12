namespace Atm.Tests;

using static N;

/// <summary>Le module rend <c>false</c> alors que les contrôles sont passés : panne.</summary>
public class FaultTests
{
    [Fact]
    public void Retrait_panne_au_premier_passage()
    {
        var dispenser = new FaultyDispenser(Full) { FailDispenseOnCall = 1 };
        var r = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(60));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(250, 0), r.Account);
        Assert.Single(dispenser.DispenseCalls); // on s'arrête aussitôt
        Assert.Equal(Full, dispenser.Stock);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Retrait_panne_en_cours_de_sortie_rend_les_billets_deja_sortis(int failingCall)
    {
        // 150 avec 20×5 10×5 : 10 billets, donc au moins 3 passages.
        var stock = C(0, 5, 5);
        var dispenser = new FaultyDispenser(stock) { FailDispenseOnCall = failingCall };
        var r = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(failingCall, dispenser.DispenseCalls.Count); // on s'arrête aussitôt
        Assert.Equal(failingCall - 1, dispenser.SuccessfulPasses.Count);
        Assert.Equal(dispenser.Dispensed, r.Notes);
        Assert.Equal(stock - r.Notes, dispenser.Stock);
        Assert.NotEqual(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(250, 0), r.Account);
    }

    [Fact]
    public void Retrait_avec_frais_en_panne_ne_preleve_pas_de_frais()
    {
        var dispenser = new FaultyDispenser(Full) { FailDispenseOnCall = 1 };
        var r = new CashMachine(dispenser).Execute(new(250, 50), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(250, 50), r.Account);
    }

    [Fact]
    public void Retrait_panne_au_dernier_passage()
    {
        // 200 avec 50×1 20×5 10×5 : 11 billets, au moins 3 passages ; on fait échouer le dernier.
        var stock = C(1, 5, 5);
        var probe = new FaultyDispenser(stock);
        var ok = new CashMachine(probe).Execute(new(250, 0), new Operation.Withdraw(200));
        Assert.Equal(AtmStatus.Ok, ok.Status);
        var passes = probe.DispenseCalls.Count;

        var dispenser = new FaultyDispenser(stock) { FailDispenseOnCall = passes };
        var r = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(dispenser.Dispensed, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(250, 0), r.Account);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, 1)]
    public void Depot_panne(int f, int t, int n)
    {
        var stock = C(1, 1, 1);
        var dispenser = new FaultyDispenser(stock) { FailLoad = true };
        var r = new CashMachine(dispenser).Execute(new(40, 30), new Operation.Deposit(C(f, t, n)));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(40, 30), r.Account);
        Assert.Single(dispenser.LoadCalls); // on s'arrête aussitôt
        Assert.Equal(stock, dispenser.Stock);
    }
}
