using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>La machine est sans mémoire : elle ne dépend que du compte, de l'opération et du module.</summary>
public class StatelessTests
{
    [Fact]
    public void Le_compte_vient_de_l_appel_pas_du_precedent()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(Acc(100, 0), new Operation.Withdraw(50));
        var second = machine.Execute(Acc(200, 10), new Operation.Withdraw(50));

        Assert.Equal(Acc(50, 50), first.Account);
        Assert.Equal(Acc(150, 60), second.Account);
        Assert.Equal(N(3, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Le_meme_appel_donne_le_meme_resultat_avec_le_meme_module()
    {
        var a = Withdraw(250, 0, N(1, 3, 1), Denomination.Ten, 60);
        var b = Withdraw(250, 0, N(1, 3, 1), Denomination.Ten, 60);

        Assert.Equal(a.Result, b.Result);
        Assert.Equal(a.Dispenser.Stock, b.Dispenser.Stock);
    }

    [Fact]
    public void Un_refus_ne_change_pas_les_appels_suivants()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        var refused = machine.Execute(Acc(10, 0), new Operation.Withdraw(50));
        var ok = machine.Execute(Acc(100, 0), new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.InsufficientFunds, refused.Status);
        Assert.Equal(AtmStatus.Ok, ok.Status);
        Assert.Equal(Acc(50, 50), ok.Account);
    }

    [Fact]
    public void La_machine_lit_l_etat_courant_du_module()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var machine = new CashMachine(dispenser);

        var before = machine.Execute(Acc(100, 0), new Operation.Withdraw(50));
        var deposit = machine.Execute(Acc(0, 0), new Operation.Deposit(N(1, 0, 0)));
        var after = machine.Execute(Acc(100, 0), new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.CannotDispense, before.Status);
        Assert.Equal(AtmStatus.Ok, deposit.Status);
        Assert.Equal(AtmStatus.Ok, after.Status);
        Assert.Equal(N(1, 0, 0), after.Notes);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }
}
