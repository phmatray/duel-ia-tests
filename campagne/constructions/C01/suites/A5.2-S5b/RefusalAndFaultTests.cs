using static Atm.Tests.N;

namespace Atm.Tests;

/// <summary>« Refus et pannes » : sur un refus rien ne change ; sur une panne du module, DispenserFault.</summary>
public class RefusalAndFaultTests
{
    public static TheoryData<int, int, NoteCounts, Denomination?, Operation, AtmStatus> Refusals => new()
    {
        { 250, 0, C(5, 5, 5), null, new Operation.Withdraw(-10), AtmStatus.InvalidAmount },
        { 250, 0, C(5, 5, 5), null, new Operation.Withdraw(0), AtmStatus.InvalidAmount },
        { 250, 0, C(5, 5, 5), null, new Operation.Withdraw(5), AtmStatus.InvalidAmount },
        { 250, 150, C(5, 5, 5), null, new Operation.Withdraw(60), AtmStatus.DailyLimitExceeded },
        { 110, 0, C(5, 5, 5), null, new Operation.Withdraw(110), AtmStatus.InsufficientFunds },
        { 250, 0, C(1, 0, 0), Denomination.Fifty, new Operation.Withdraw(50), AtmStatus.CannotDispense },
        { 250, 0, C(5, 0, 0), null, new Operation.Withdraw(60), AtmStatus.CannotDispense },
        { 100, 20, C(1, 1, 1), null, new Operation.Deposit(NoteCounts.None), AtmStatus.InvalidAmount },
        { 100, 20, C(1, 1, 1), Denomination.Ten, new Operation.Deposit(C(1, 0, 1)), AtmStatus.CassetteJammed },
        { 100, 20, C(1, 5, 1), null, new Operation.Deposit(C(1, 1, 1)), AtmStatus.CassetteFull },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Refusal_changes_nothing(
        int balance, int withdrawn, NoteCounts stock, Denomination? jam, Operation operation, AtmStatus expected)
    {
        var spy = new SpyDispenser(new MoneyDispenser(stock, jam));
        var account = new AccountState(balance, withdrawn);

        var r = new CashMachine(spy).Execute(account, operation);

        Assert.Equal(expected, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(stock, spy.Stock);
        Assert.DoesNotContain(spy.DispenseCalls, c => c.Result);
        Assert.DoesNotContain(spy.LoadCalls, c => c.Result);
    }

    // ---- Pannes : le module rend false malgré les contrôles ----

    [Fact]
    public void Withdraw_fault_on_first_pass_returns_DispenserFault_with_no_notes()
    {
        var dispenser = new FaultyDispenser(C(5, 5, 5)) { FailDispenseFromCall = 1 };
        var account = new AccountState(250, 0);

        var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(60));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(0, dispenser.CallsAfterFault);
    }

    [Fact]
    public void Withdraw_fault_on_a_later_pass_returns_the_notes_already_dispensed()
    {
        // 150 sans billets de 50 : 20×5 + 10×5 = 10 billets, donc au moins 3 passages.
        var dispenser = new FaultyDispenser(C(0, 5, 5)) { FailDispenseFromCall = 2 };
        var account = new AccountState(250, 0);

        var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Single(dispenser.Dispensed);
        Assert.Equal(dispenser.Dispensed[0], r.Notes);
        Assert.NotEqual(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(0, dispenser.CallsAfterFault);
    }

    [Fact]
    public void Withdraw_fault_on_the_third_pass_returns_the_two_first_passes()
    {
        var dispenser = new FaultyDispenser(C(0, 5, 5)) { FailDispenseFromCall = 3 };
        var account = new AccountState(250, 0);

        var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(2, dispenser.Dispensed.Count);
        Assert.Equal(dispenser.Dispensed[0] + dispenser.Dispensed[1], r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(0, dispenser.CallsAfterFault);
    }

    [Fact]
    public void Deposit_fault_returns_DispenserFault_with_no_notes()
    {
        var dispenser = new FaultyDispenser(C(0, 0, 0)) { FailLoad = true };
        var account = new AccountState(100, 30);

        var r = new CashMachine(dispenser).Execute(account, new Operation.Deposit(C(1, 1, 1)));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(C(0, 0, 0), dispenser.Stock);
        Assert.Equal(0, dispenser.CallsAfterFault);
    }
}
