using static Atm.Tests.Notes;

namespace Atm.Tests;

/// <summary>« La machine est sans mémoire entre deux appels. »</summary>
public class StatelessTests
{
    [Fact]
    public void Le_compte_vient_de_l_appel_pas_de_l_appel_precedent()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(100));
        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(100));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(new AccountState(150, 100), second.Account);
    }

    [Fact]
    public void Le_plafond_du_jour_vient_du_compte_passe_en_parametre()
    {
        var machine = new CashMachine(new MoneyDispenser(N(5, 5, 5)));

        Assert.Equal(AtmStatus.Ok, machine.Execute(new AccountState(250, 0), new Operation.Withdraw(200)).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(new AccountState(250, 0), new Operation.Withdraw(10)).Status);
    }

    [Fact]
    public void Le_stock_vient_du_module_a_chaque_appel()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50)).Status);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50)).Status);

        Assert.Equal(AtmStatus.Ok, machine.Execute(new AccountState(0, 0), new Operation.Deposit(N(1, 0, 0))).Status);
        var again = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.Ok, again.Status);
        Assert.Equal(N(1, 0, 0), again.Notes);
    }

    [Fact]
    public void Un_refus_ne_laisse_pas_de_trace()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        machine.Execute(new AccountState(250, 0), new Operation.Withdraw(5));
        machine.Execute(new AccountState(250, 0), new Operation.Deposit(N(1, 1, 1)));
        var result = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(80));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(1, 1, 1), result.Notes);
        Assert.Equal(new AccountState(170, 80), result.Account);
    }
}
