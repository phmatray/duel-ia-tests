using Atm.Tests.Support;
using static Atm.Tests.Support.N;

namespace Atm.Tests;

/// <summary>La machine est sans mémoire : elle ne dépend que du compte, de l'opération et du module.</summary>
public class StatelessTests
{
    [Fact]
    public void Un_retrait_ne_garde_pas_le_compte_precedent()
    {
        var dispenser = new MoneyDispenser(Notes(5, 5, 5));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(150));
        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.Ok, first.Status);
        // Le plafond se juge sur le compte reçu (0 + 50), pas sur 150 + 50 mémorisé.
        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 0, 0), 0, new AccountState(200, 50)), second);
    }

    [Fact]
    public void La_machine_lit_le_stock_du_module_a_chaque_appel()
    {
        var dispenser = new MoneyDispenser(Notes(1, 0, 0));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        var deposit = machine.Execute(new AccountState(250, 0), new Operation.Deposit(Notes(1, 0, 0)));
        var third = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(AtmStatus.CannotDispense, second.Status);
        Assert.Equal(AtmStatus.Ok, deposit.Status);
        Assert.Equal(AtmStatus.Ok, third.Status);
        Assert.Equal(Notes(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Un_refus_n_influence_pas_l_appel_suivant()
    {
        var machine = new CashMachine(new MoneyDispenser(Notes(5, 5, 5)));

        var refused = machine.Execute(new AccountState(0, 200), new Operation.Withdraw(5));
        var next = machine.Execute(new AccountState(100, 0), new Operation.Withdraw(60));

        Assert.Equal(AtmStatus.InvalidAmount, refused.Status);
        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 0, 1), 0, new AccountState(40, 60)), next);
    }

    [Fact]
    public void Deux_machines_identiques_rendent_le_meme_resultat()
    {
        var account = new AccountState(115, 30);
        var operation = new Operation.Withdraw(110);

        var a = new CashMachine(new MoneyDispenser(Notes(2, 3, 1), Denomination.Twenty)).Execute(account, operation);
        var b = new CashMachine(new MoneyDispenser(Notes(2, 3, 1), Denomination.Twenty)).Execute(account, operation);

        Assert.Equal(a, b);
    }
}
