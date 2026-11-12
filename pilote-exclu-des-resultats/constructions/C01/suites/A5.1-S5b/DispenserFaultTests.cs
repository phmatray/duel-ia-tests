namespace Atm.Tests;

/// <summary>Le module rend <c>false</c> alors que les contrôles sont passés : c'est une panne.</summary>
public class DispenserFaultTests
{
    [Fact]
    public void Withdraw_fault_on_the_only_pass_returns_no_notes_and_leaves_account_unchanged()
    {
        var account = Account(250, 0);
        var stock = Notes(5, 5, 5);
        var dispenser = new SpyDispenser(stock) { FailOnDispenseCall = 1 };

        var result = new CashMachine(dispenser).Execute(account, Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Single(dispenser.DispenseCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Withdraw_fault_returns_the_notes_already_dispensed_and_stops(int failingPass)
    {
        // 150 avec 20×5 10×5 : 10 billets, donc au moins 3 passages. Frais de 5 € normalement.
        var account = Account(250, 0);
        var stock = Notes(0, 5, 5);
        var dispenser = new SpyDispenser(stock) { FailOnDispenseCall = failingPass };

        var result = new CashMachine(dispenser).Execute(account, Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(dispenser.Dispensed, result.Notes);
        Assert.Equal(stock - dispenser.Stock, result.Notes);
        Assert.Equal(failingPass, dispenser.DispenseCalls.Count());
        Assert.Equal(failingPass - 1, dispenser.DispenseCalls.Count(c => c.Result));
        Assert.False(dispenser.DispenseCalls.Last().Result);
        if (failingPass > 1)
        {
            Assert.NotEqual(NoteCounts.None, result.Notes);
        }
    }

    [Fact]
    public void Withdraw_fault_on_the_second_pass_of_five_tens_returns_the_first_pass()
    {
        // 50 avec seulement des 10 : 10×5, une seule coupure, donc au moins 2 passages.
        var account = Account(250, 0);
        var stock = Notes(0, 0, 5);
        var dispenser = new SpyDispenser(stock) { FailOnDispenseCall = 2 };

        var result = new CashMachine(dispenser).Execute(account, Withdraw(50));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(dispenser.Dispensed, result.Notes);
        Assert.Equal(stock - dispenser.Stock, result.Notes);
        Assert.InRange(result.Notes.Tens, 1, 4);
        Assert.Equal(2, dispenser.DispenseCalls.Count());
    }

    [Fact]
    public void Withdraw_fault_on_the_second_pass_of_a_five_note_withdrawal()
    {
        var account = Account(250, 0);
        var stock = Notes(3, 4, 0);
        var dispenser = new SpyDispenser(stock) { FailOnDispenseCall = 2 };

        var result = new CashMachine(dispenser).Execute(account, Withdraw(130));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(dispenser.Dispensed, result.Notes);
        Assert.InRange(result.Notes.Total, 1, 4);
        Assert.Equal(2, dispenser.DispenseCalls.Count());
    }

    [Fact]
    public void Deposit_fault_returns_no_notes_and_leaves_account_and_stock_unchanged()
    {
        var account = Account(100, 30);
        var stock = Notes(1, 1, 1);
        var dispenser = new SpyDispenser(stock) { FailOnLoad = true };

        var result = new CashMachine(dispenser).Execute(account, Deposit(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Single(dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
    }
}
