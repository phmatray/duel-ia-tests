using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>« La machine est sans mémoire entre deux appels : elle ne dépend que du compte, de l'opération et du module. »</summary>
public class StatelessTests
{
    [Fact]
    public void Le_second_appel_part_du_compte_fourni_et_non_du_resultat_precedent()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(Acc(250, 0), Withdraw(100));
        var second = machine.Execute(Acc(250, 0), Withdraw(100));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(Acc(150, 100), second.Account); // pas de plafond cumulé en mémoire
        Assert.Equal(N(1, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Un_refus_precedent_n_influence_pas_l_appel_suivant()
    {
        var machine = new CashMachine(new MoneyDispenser(N(5, 5, 5)));

        var refused = machine.Execute(Acc(0, 0), Withdraw(50));
        var ok = machine.Execute(Acc(50, 0), Withdraw(50));

        Assert.Equal(AtmStatus.InsufficientFunds, refused.Status);
        Assert.Equal(AtmStatus.Ok, ok.Status);
        Assert.Equal(Acc(0, 50), ok.Account);
    }

    [Fact]
    public void La_machine_lit_le_stock_courant_du_module()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(Acc(250, 0), Withdraw(50)).Status);

        Assert.True(dispenser.Load(N(1, 0, 0)));

        var r = machine.Execute(Acc(250, 0), Withdraw(50));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 0, 0), r.Notes);
    }

    [Fact]
    public void Un_depot_alimente_les_retraits_suivants()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var machine = new CashMachine(dispenser);

        var deposit = machine.Execute(Acc(0, 0), Deposit(N(1, 1, 1)));
        var withdraw = machine.Execute(deposit.Account, Withdraw(80));

        Assert.Equal(AtmStatus.Ok, deposit.Status);
        Assert.Equal(AtmStatus.Ok, withdraw.Status);
        Assert.Equal(N(1, 1, 1), withdraw.Notes);
        Assert.Equal(Acc(0, 80), withdraw.Account);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Meme_entree_meme_resultat()
    {
        var a = Run(Acc(200, 20), N(3, 2, 4), Denomination.Twenty, Withdraw(170));
        var b = Run(Acc(200, 20), N(3, 2, 4), Denomination.Twenty, Withdraw(170));

        Assert.Equal(a.Result, b.Result);
        Assert.Equal(a.Dispenser.Stock, b.Dispenser.Stock);
    }
}
