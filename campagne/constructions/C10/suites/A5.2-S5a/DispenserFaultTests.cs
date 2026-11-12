namespace Atm.Tests;

/// <summary>Le module rend false malgré les contrôles : panne.</summary>
public class DispenserFaultTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var module = new FaultyDispenser(N(5, 5, 5)) { FailDispenseAtCall = 1 };
        var account = new AccountState(250, 0);

        var r = new CashMachine(module).Execute(account, new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), r);
        Assert.Single(module.DispenseCalls); // on s'arrête aussitôt
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Panne_en_cours_de_retrait_rend_les_billets_deja_sortis(int failAt)
    {
        // 20×5 + 10×5 = 150 : 10 billets, donc au moins 3 passages.
        var module = new FaultyDispenser(N(0, 5, 5)) { FailDispenseAtCall = failAt };
        var account = new AccountState(250, 20);

        var r = new CashMachine(module).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(failAt, module.DispenseCalls.Count); // aucun passage après la panne
        Assert.Equal(module.SuccessfullyDispensed, r.Notes);
        Assert.True(r.Notes.Total > 0);
    }

    [Fact]
    public void Panne_en_cours_de_retrait_avec_passages_de_4_billets()
    {
        var module = new FaultyDispenser(N(0, 5, 5)) { FailDispenseAtCall = 2 };
        var account = new AccountState(250, 0);

        var r = new CashMachine(module).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.InRange(module.DispenseCalls[0].Total, 1, 4);
        Assert.Equal(module.DispenseCalls[0], r.Notes);
    }

    [Fact]
    public void Panne_au_depot()
    {
        var module = new FaultyDispenser(N(1, 1, 1)) { FailLoad = true };
        var account = new AccountState(100, 30);

        var r = new CashMachine(module).Execute(account, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), r);
        Assert.Equal(1, module.LoadCallCount);
    }

    [Fact]
    public void Un_refus_n_appelle_pas_le_module()
    {
        var module = new FaultyDispenser(N(5, 5, 5)) { FailDispenseAtCall = 1, FailLoad = true };
        var machine = new CashMachine(module);

        Assert.Equal(AtmStatus.InvalidAmount, machine.Execute(new AccountState(250, 0), new Operation.Withdraw(5)).Status);
        Assert.Equal(AtmStatus.DailyLimitExceeded, machine.Execute(new AccountState(250, 200), new Operation.Withdraw(10)).Status);
        Assert.Equal(AtmStatus.InsufficientFunds, machine.Execute(new AccountState(0, 0), new Operation.Withdraw(10)).Status);
        Assert.Equal(AtmStatus.InvalidAmount, machine.Execute(new AccountState(250, 0), new Operation.Deposit(N(0, 0, 0))).Status);
        Assert.Equal(AtmStatus.CassetteFull, machine.Execute(new AccountState(250, 0), new Operation.Deposit(N(1, 0, 0))).Status);
        Assert.Empty(module.DispenseCalls);
        Assert.Equal(0, module.LoadCallCount);
    }
}
