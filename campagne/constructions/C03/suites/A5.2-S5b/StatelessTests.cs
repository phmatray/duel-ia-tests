using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>« La machine est sans mémoire entre deux appels. »</summary>
public class StatelessTests
{
    [Fact]
    public void Daily_total_comes_only_from_the_account_passed_in()
    {
        var (machine, _) = Build(N(5, 5, 5));

        var first = machine.Execute(Acc(250, 0), Withdraw(100));
        var second = machine.Execute(Acc(250, 0), Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(150, 100)), first);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(150, 100)), second);
    }

    [Fact]
    public void Result_depends_on_the_current_stock_of_the_module()
    {
        var (machine, dispenser) = Build(N(1, 0, 0));

        var first = machine.Execute(Acc(250, 0), Withdraw(50));
        var second = machine.Execute(Acc(250, 0), Withdraw(50));
        var deposit = machine.Execute(Acc(250, 0), Deposit(N(1, 0, 0)));
        var third = machine.Execute(Acc(250, 0), Withdraw(50));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(AtmStatus.CannotDispense, second.Status);
        Assert.Equal(AtmStatus.Ok, deposit.Status);
        Assert.Equal(AtmStatus.Ok, third.Status);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void A_refusal_leaves_no_trace_on_the_next_call()
    {
        var (machine, _) = Build(N(5, 5, 5));

        machine.Execute(Acc(0, 0), Withdraw(100));            // InsufficientFunds
        machine.Execute(Acc(250, 200), Withdraw(10));         // DailyLimitExceeded
        machine.Execute(Acc(250, 0), Deposit(N(1, 0, 0)));    // CassetteFull
        var result = machine.Execute(Acc(250, 0), Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, Acc(170, 80)), result);
    }
}
