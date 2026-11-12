using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>La machine est sans mémoire : elle ne dépend que du compte, de l'opération et du module.</summary>
public class StatelessTests
{
    [Fact]
    public void Le_compte_rendu_ne_sert_pas_de_memoire_pour_l_appel_suivant()
    {
        var (machine, _) = Real(N(5, 5, 5));

        machine.Execute(Acc(250, 0), Withdraw(100));
        // Le même compte de départ, pas celui rendu par l'appel précédent.
        var second = machine.Execute(Acc(250, 0), Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(150, 100)), second);
    }

    [Fact]
    public void Apres_des_refus_la_machine_se_comporte_comme_une_neuve()
    {
        var (used, _) = Real(N(5, 5, 5));
        used.Execute(Acc(0, 200), Withdraw(5));            // InvalidAmount
        used.Execute(Acc(10, 0), Deposit(N(1, 0, 0)));     // CassetteFull
        used.Execute(Acc(250, 190), Withdraw(20));         // DailyLimitExceeded
        var fromUsed = used.Execute(Acc(250, 0), Withdraw(170));

        var (fresh, _) = Real(N(5, 5, 5));
        var fromFresh = fresh.Execute(Acc(250, 0), Withdraw(170));

        Assert.Equal(fromFresh, fromUsed);
        Assert.Equal(AtmStatus.Ok, fromUsed.Status);
    }

    [Fact]
    public void Le_resultat_suit_l_etat_courant_du_module()
    {
        var (machine, dispenser) = Real(N(1, 0, 0));

        var first = machine.Execute(Acc(250, 0), Withdraw(50));
        var second = machine.Execute(Acc(250, 0), Withdraw(50));
        machine.Execute(Acc(250, 0), Deposit(N(1, 0, 0)));
        var third = machine.Execute(Acc(250, 0), Withdraw(50));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(AtmStatus.CannotDispense, second.Status);
        Assert.Equal(AtmStatus.Ok, third.Status);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }
}
