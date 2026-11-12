using static Atm.Tests.N;

namespace Atm.Tests;

/// <summary>La machine est sans mémoire : elle ne dépend que du compte, de l'opération et du module.</summary>
public class StatelessTests
{
    [Fact]
    public void Reused_machine_behaves_like_a_fresh_one_on_the_same_module_state()
    {
        var shared = new MoneyDispenser(C(2, 2, 2));
        var machine = new CashMachine(shared);

        // Un premier appel modifie le module ; on rejoue ensuite avec un compte sans rapport.
        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(80));
        Assert.Equal(AtmStatus.Ok, first.Status);

        var account = new AccountState(100, 0);
        var operation = new Operation.Withdraw(70);
        var reused = machine.Execute(account, operation);

        var fresh = new CashMachine(new MoneyDispenser(shared.Stock + reused.Notes)).Execute(account, operation);

        Assert.Equal(fresh, reused);
        Assert.Equal(new AccountState(30, 70), reused.Account);
    }

    [Fact]
    public void Previous_refusal_does_not_influence_the_next_call()
    {
        var dispenser = new MoneyDispenser(C(5, 5, 5));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.DailyLimitExceeded,
            machine.Execute(new AccountState(250, 200), new Operation.Withdraw(10)).Status);

        var r = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(10));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(240, 10), r.Account);
    }

    [Fact]
    public void Same_inputs_give_the_same_result()
    {
        var account = new AccountState(200, 40);
        var operation = new Operation.Withdraw(130);
        var a = new CashMachine(new MoneyDispenser(C(3, 3, 3))).Execute(account, operation);
        var b = new CashMachine(new MoneyDispenser(C(3, 3, 3))).Execute(account, operation);
        Assert.Equal(a, b);
    }
}
