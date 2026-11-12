namespace Atm.Tests;

/// <summary>Le module rend false malgré les contrôles : DispenserFault.</summary>
public class FaultTests
{
    [Fact]
    public void Withdraw_FirstPassFails_NothingOut()
    {
        var dispenser = new FaultyDispenser(N.Of(5, 5, 5), null, failDispenseAt: 1);
        var account = new AccountState(250, 0);
        var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), r);
        Assert.Equal(1, dispenser.DispenseCallCount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Withdraw_LaterPassFails_NotesHoldWhatAlreadyCameOut_AndStops(int failAt)
    {
        // 150 = 20×5 10×5 : 10 billets, donc au moins 3 passages.
        var dispenser = new FaultyDispenser(N.Of(0, 5, 5), null, failDispenseAt: failAt);
        var account = new AccountState(250, 30);
        var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(dispenser.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b), r.Notes);
        Assert.Equal(failAt - 1, dispenser.Dispensed.Count);
        Assert.Equal(failAt, dispenser.DispenseCallCount); // on s'arrête aussitôt
        Assert.Equal(N.Of(0, 5, 5) - r.Notes, dispenser.Stock);
        Assert.Equal(0, dispenser.LoadCallCount);
    }

    [Fact]
    public void Withdraw_FaultWithFee_FeeIsZero()
    {
        var dispenser = new FaultyDispenser(N.Of(5, 5, 5), null, failDispenseAt: 1);
        var account = new AccountState(250, 0);
        var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(150));
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), r);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, 1)]
    public void Deposit_LoadFails_DispenserFault_NotesEmpty(int f, int t, int d)
    {
        var dispenser = new FaultyDispenser(N.Of(0, 0, 0), null, failLoadAt: 1);
        var account = new AccountState(100, 50);
        var r = new CashMachine(dispenser).Execute(account, new Operation.Deposit(N.Of(f, t, d)));
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), r);
        Assert.Equal(1, dispenser.LoadCallCount);
        Assert.Equal(0, dispenser.DispenseCallCount);
    }

    [Fact]
    public void Deposit_SecondLoadFails_DispenserFault_StopsAtOnce()
    {
        // Si la machine range les billets en plusieurs appels, une panne au 2e appel arrête tout.
        var dispenser = new FaultyDispenser(N.Of(0, 0, 0), null, failLoadAt: 2);
        var account = new AccountState(100, 50);
        var r = new CashMachine(dispenser).Execute(account, new Operation.Deposit(N.Of(1, 1, 1)));
        if (dispenser.LoadCallCount == 1)
        {
            Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(180, 50)), r);
        }
        else
        {
            Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), r);
            Assert.Equal(2, dispenser.LoadCallCount);
        }
    }
}
