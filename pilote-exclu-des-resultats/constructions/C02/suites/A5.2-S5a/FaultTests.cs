using static Atm.Tests.T;

namespace Atm.Tests;

public class FaultTests
{
    [Fact]
    public void Withdrawal_fault_on_first_pass_returns_DispenserFault_with_no_notes()
    {
        var stock = N(5, 5, 5);
        var dispenser = new SpyDispenser(stock) { FailDispenseOnCall = 1 };
        var account = Acc(250, 0);

        var result = new CashMachine(dispenser).Execute(account, W(60));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Single(dispenser.DispenseCalls); // on s'arrête aussitôt
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Withdrawal_fault_mid_way_returns_notes_already_dispensed_and_stops(int failingCall)
    {
        // 150 sans billet de 50 : 20×5 10×5, 10 billets, donc 3 passages au moins.
        var stock = N(0, 5, 5);
        var dispenser = new SpyDispenser(stock) { FailDispenseOnCall = failingCall };
        var account = Acc(250, 20);

        var result = new CashMachine(dispenser).Execute(account, W(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(failingCall, dispenser.DispenseCalls.Count); // aucun passage après la panne
        Assert.Equal(failingCall - 1, dispenser.SuccessfulPasses.Count);
        Assert.Equal(Sum(dispenser.SuccessfulPasses), result.Notes);
        Assert.NotEqual(NoteCounts.None, result.Notes);
        Assert.Equal(stock - result.Notes, dispenser.Stock);
        Assert.Equal(0, result.Fee); // pas de frais, même au-delà de 100 €
        Assert.Equal(account, result.Account);
    }

    [Fact]
    public void Deposit_fault_returns_DispenserFault_with_unchanged_account()
    {
        var stock = N(1, 1, 1);
        var dispenser = new SpyDispenser(stock) { FailLoad = true };
        var account = Acc(10, 30);

        var result = new CashMachine(dispenser).Execute(account, D(1, 1, 1));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Single(dispenser.LoadCalls);
    }
}
