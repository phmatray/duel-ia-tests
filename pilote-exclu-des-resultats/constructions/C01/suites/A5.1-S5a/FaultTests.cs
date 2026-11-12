using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le module rend <c>false</c> malgré les contrôles : panne.</summary>
public class FaultTests
{
    public static TheoryData<int, int> WithdrawFaults()
    {
        // (montant, numéro de l'appel qui échoue) pour des retraits en plusieurs passages.
        var data = new TheoryData<int, int>();
        foreach (var amount in new[] { 10, 80, 150, 180, 190, 200 })
        {
            var passes = (FewestNotes(amount, N(5, 5, 5))!.Value.Total + 3) / 4;
            for (var k = 1; k <= passes; k++)
            {
                data.Add(amount, k);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(WithdrawFaults))]
    public void Withdraw_fault_stops_immediately_and_reports_notes_already_out(int amount, int failingCall)
    {
        var account = Acc(250, 0);
        var spy = new SpyDispenser(N(5, 5, 5)) { FailDispenseAtCall = failingCall };

        var result = new CashMachine(spy).Execute(account, Withdraw(amount));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(failingCall, spy.DispenseCalls.Count); // on s'arrête aussitôt
        Assert.Equal(spy.Dispensed, result.Notes);           // billets déjà sortis
        Assert.Equal(N(5, 5, 5) - spy.Dispensed, spy.Stock);
        Assert.Empty(spy.LoadCalls);
    }

    [Fact]
    public void Withdraw_fault_on_first_pass_reports_no_notes()
    {
        var account = Acc(250, 0);
        var spy = new SpyDispenser(N(5, 5, 5)) { FailDispenseAtCall = 1 };

        var result = new CashMachine(spy).Execute(account, Withdraw(190));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Single(spy.DispenseCalls);
        Assert.Equal(N(5, 5, 5), spy.Stock);
    }

    [Fact]
    public void Withdraw_fault_on_second_pass_reports_first_pass_notes()
    {
        // 190 = 50×3 20×2, soit 5 billets : au moins deux passages.
        var account = Acc(250, 0);
        var spy = new SpyDispenser(N(5, 5, 5)) { FailDispenseAtCall = 2 };

        var result = new CashMachine(spy).Execute(account, Withdraw(190));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(2, spy.DispenseCalls.Count);
        Assert.Equal(spy.DispenseCalls[0], result.Notes);
        Assert.InRange(result.Notes.Total, 1, 4);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, 1)]
    public void Deposit_fault_changes_nothing_and_reports_no_notes(int d50, int d20, int d10)
    {
        var account = Acc(100, 30);
        var spy = new SpyDispenser(N(1, 1, 1)) { FailLoad = true };

        var result = new CashMachine(spy).Execute(account, Deposit(N(d50, d20, d10)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Single(spy.LoadCalls);
        Assert.Equal(N(1, 1, 1), spy.Stock);
        Assert.Empty(spy.DispenseCalls);
    }
}
