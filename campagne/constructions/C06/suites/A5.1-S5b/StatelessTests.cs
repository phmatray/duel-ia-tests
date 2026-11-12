using static Atm.Tests.Support;

namespace Atm.Tests;

/// <summary>La machine est sans mémoire : seuls le compte, l'opération et le module comptent.</summary>
public class StatelessTests
{
    [Fact]
    public void Deux_retraits_successifs_dependent_seulement_du_compte_passe_en_argument()
    {
        var (atm, dispenser) = Machine(Full);

        var first = atm.Execute(Account(250, 0), Withdraw(100));
        Assert.Equal(AtmStatus.Ok, first.Status);

        // On repasse le compte d'origine : la machine ne doit pas se souvenir du premier retrait.
        var second = atm.Execute(Account(250, 0), Withdraw(100));

        var (fresh, _) = Machine(Notes(3, 5, 5));
        var reference = fresh.Execute(Account(250, 0), Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 0), 0, Account(150, 100)), second);
        Assert.Equal(reference, second);
    }

    [Fact]
    public void Un_refus_puis_un_retrait_valide_sur_la_meme_machine()
    {
        var (atm, dispenser) = Machine(Notes(1, 1, 1));

        var refused = atm.Execute(Account(250, 190), Withdraw(80));
        Assert.Equal(AtmStatus.DailyLimitExceeded, refused.Status);

        var ok = atm.Execute(Account(250, 0), Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 1, 1), 0, Account(170, 80)), ok);
        Assert.Equal(Notes(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Le_module_reflete_les_operations_successives()
    {
        var (atm, dispenser) = Machine(Notes(1, 0, 0));

        var deposit = atm.Execute(Account(50, 0), Deposit(Notes(0, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(80, 0)), deposit);
        Assert.Equal(Notes(1, 1, 1), dispenser.Stock);

        var withdraw = atm.Execute(deposit.Account, Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 1, 1), 0, Account(0, 80)), withdraw);
        Assert.Equal(Notes(0, 0, 0), dispenser.Stock);
    }
}
