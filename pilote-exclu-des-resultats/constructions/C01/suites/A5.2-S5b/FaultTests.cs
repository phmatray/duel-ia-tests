using static Atm.Tests.Notes;

namespace Atm.Tests;

/// <summary>« Si le module rend false malgré ces contrôles, c'est une panne. »</summary>
public class FaultTests
{
    [Fact]
    public void Retrait_panne_au_premier_passage_rien_n_est_sorti()
    {
        var dispenser = new SpyDispenser(N(5, 5, 5)) { FailDispenseOnCall = 1 };
        var account = new AccountState(250, 0);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Single(dispenser.DispenseCalls); // on s'arrête aussitôt
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Retrait_avec_frais_panne_au_premier_passage_aucun_frais()
    {
        var dispenser = new SpyDispenser(N(5, 5, 5)) { FailDispenseOnCall = 1 };
        var account = new AccountState(250, 50);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Single(dispenser.DispenseCalls);
    }

    [Theory]
    [InlineData(0, 5, 5, 150)] // 10 billets : au moins 3 passages
    [InlineData(1, 5, 0, 150)] // 6 billets : au moins 2 passages
    [InlineData(3, 5, 5, 200)]
    public void Retrait_panne_au_deuxieme_passage_rend_les_billets_deja_sortis(int sf, int st, int sd, int amount)
    {
        var stock = N(sf, st, sd);
        var dispenser = new SpyDispenser(stock) { FailDispenseOnCall = 2 };
        var account = new AccountState(250, 0);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(2, dispenser.DispenseCalls.Count); // arrêt immédiat après la panne
        var first = Assert.Single(dispenser.SuccessfulPasses);
        Assert.Equal(first, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock - first, dispenser.Stock);
    }

    [Fact]
    public void Retrait_panne_au_troisieme_passage_rend_les_deux_premiers()
    {
        var stock = N(0, 5, 5); // 150 = 20×5 + 10×5 : 10 billets, au moins 3 passages
        var dispenser = new SpyDispenser(stock) { FailDispenseOnCall = 3 };
        var account = new AccountState(250, 0);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(3, dispenser.DispenseCalls.Count);
        Assert.Equal(2, dispenser.SuccessfulPasses.Count);
        var dispensed = dispenser.SuccessfulPasses[0] + dispenser.SuccessfulPasses[1];
        Assert.Equal(dispensed, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock - dispensed, dispenser.Stock);
    }

    [Fact]
    public void Depot_panne_du_module()
    {
        var stock = N(1, 1, 1);
        var dispenser = new SpyDispenser(stock) { FailLoad = true };
        var account = new AccountState(100, 30);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Single(dispenser.LoadCalls);
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void Refus_de_retrait_n_appelle_pas_le_module()
    {
        var dispenser = new SpyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        machine.Execute(new AccountState(250, 0), new Operation.Withdraw(5));
        machine.Execute(new AccountState(250, 200), new Operation.Withdraw(10));
        machine.Execute(new AccountState(0, 0), new Operation.Withdraw(10));
        var empty = new SpyDispenser(N(0, 0, 0));
        new CashMachine(empty).Execute(new AccountState(250, 0), new Operation.Withdraw(10));

        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
        Assert.Empty(empty.DispenseCalls);
        Assert.Empty(empty.LoadCalls);
    }

    [Fact]
    public void Refus_de_depot_n_appelle_pas_le_module()
    {
        var dispenser = new SpyDispenser(N(5, 0, 0), Denomination.Ten);
        var machine = new CashMachine(dispenser);

        machine.Execute(new AccountState(100, 0), new Operation.Deposit(NoteCounts.None));
        machine.Execute(new AccountState(100, 0), new Operation.Deposit(N(0, 0, 1)));
        machine.Execute(new AccountState(100, 0), new Operation.Deposit(N(1, 0, 0)));

        Assert.Empty(dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
    }
}
