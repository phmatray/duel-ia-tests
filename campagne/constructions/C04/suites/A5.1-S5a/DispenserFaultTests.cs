using static Atm.Tests.TestKit;

namespace Atm.Tests;

/// <summary>Le module rend <c>false</c> malgré les contrôles : c'est une panne.</summary>
public class DispenserFaultTests
{
    [Fact]
    public void Withdraw_fault_on_first_pass_returns_DispenserFault_with_no_notes()
    {
        var account = Account(250, 0);
        var dispenser = new FaultyDispenser(N(5, 5, 5), null, acceptedPasses: 0);

        var result = new CashMachine(dispenser).Execute(account, Withdraw(60));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(1, dispenser.DispenseCalls); // on s'arrête aussitôt
    }

    [Theory]
    [InlineData(5, 5, 5, 190)]  // 50×3 + 20×2 : 5 billets, au moins 2 passages
    [InlineData(0, 5, 5, 150)]  // 20×5 + 10×5 : 10 billets, au moins 3 passages
    [InlineData(3, 2, 1, 200)]
    public void Withdraw_fault_after_some_passes_returns_notes_already_dispensed(int f, int t, int d, int amount)
    {
        var account = Account(250, 0);
        var dispenser = new FaultyDispenser(N(f, t, d), null, acceptedPasses: 1);

        var result = new CashMachine(dispenser).Execute(account, Withdraw(amount));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Single(dispenser.AcceptedPasses);
        Assert.Equal(dispenser.AcceptedPasses[0], result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(2, dispenser.DispenseCalls); // aucun passage après la panne
    }

    [Fact]
    public void Withdraw_fault_on_third_pass_returns_the_two_first_passes()
    {
        var account = Account(250, 0);
        var dispenser = new FaultyDispenser(N(0, 5, 5), null, acceptedPasses: 2);

        var result = new CashMachine(dispenser).Execute(account, Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(2, dispenser.AcceptedPasses.Count);
        Assert.Equal(dispenser.AcceptedPasses[0] + dispenser.AcceptedPasses[1], result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(3, dispenser.DispenseCalls);
    }

    [Fact]
    public void Deposit_fault_returns_DispenserFault_with_nothing_changed()
    {
        var account = Account(100, 30);
        var dispenser = new FaultyDispenser(N(1, 1, 1), null, acceptedPasses: 0, loadResult: false);

        var result = new CashMachine(dispenser).Execute(account, Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(1, dispenser.LoadCalls);
    }

    [Fact]
    public void Refusals_move_no_notes_even_with_a_permissive_module()
    {
        // Ce module accepte tout : le moindre appel déplacerait des billets.
        var stock = N(0, 0, 5);
        var dispenser = new FaultyDispenser(stock, Denomination.Twenty, acceptedPasses: int.MaxValue, loadResult: true);
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.InvalidAmount, machine.Execute(Account(250, 0), Withdraw(5)).Status);
        Assert.Equal(AtmStatus.DailyLimitExceeded, machine.Execute(Account(250, 200), Withdraw(10)).Status);
        Assert.Equal(AtmStatus.InsufficientFunds, machine.Execute(Account(0, 0), Withdraw(10)).Status);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(Account(250, 0), Withdraw(70)).Status);
        Assert.Equal(AtmStatus.InvalidAmount, machine.Execute(Account(250, 0), Deposit(NoteCounts.None)).Status);
        Assert.Equal(AtmStatus.CassetteJammed, machine.Execute(Account(250, 0), Deposit(N(0, 1, 0))).Status);
        Assert.Equal(AtmStatus.CassetteFull, machine.Execute(Account(250, 0), Deposit(N(0, 0, 1))).Status);

        Assert.Equal(stock, dispenser.Stock);
    }
}
