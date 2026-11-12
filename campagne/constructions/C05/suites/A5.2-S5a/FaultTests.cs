using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// « Si le module rend false malgré ces contrôles, c'est une panne. » Le vrai module ne le fait pas
/// dans le domaine : on simule la panne avec un espion.
/// </summary>
public class FaultTests
{
    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var (machine, spy) = Spied(Full, failOnCall: 1);

        var result = machine.Execute(Account(250, 0), Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, Empty, 0, Account(250, 0)), result);
        Assert.Equal(1, spy.Calls);
        Assert.Equal(Full, spy.Stock);
    }

    [Fact]
    public void Panne_au_deuxieme_passage_rend_les_billets_deja_sortis_et_s_arrete()
    {
        // 150 sans 50 = 20×5 10×5 : 10 billets, au moins 3 passages.
        var (machine, spy) = Spied(Notes(0, 5, 5), failOnCall: 2);

        var result = machine.Execute(Account(250, 0), Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(250, 0), result.Account);
        Assert.Single(spy.Dispensed);
        Assert.Equal(spy.Dispensed[0], result.Notes);
        Assert.Equal(Notes(0, 5, 5) - spy.Dispensed[0], spy.Stock);
        Assert.Equal(2, spy.Calls); // on s'arrête aussitôt : pas de troisième passage
    }

    [Fact]
    public void Panne_au_dernier_passage_rend_tous_les_billets_sauf_le_dernier_passage()
    {
        var (machine, spy) = Spied(Notes(0, 5, 0), failOnCall: 2);

        var result = machine.Execute(Account(250, 0), Withdraw(100));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(250, 0), result.Account);
        Assert.Equal(spy.Dispensed.Aggregate(NoteCounts.None, (s, p) => s + p), result.Notes);
        Assert.InRange(result.Notes.Total, 1, 4);
        Assert.Equal(2, spy.Calls);
    }

    [Fact]
    public void Panne_sur_un_retrait_avec_frais_ne_preleve_pas_les_frais()
    {
        var (machine, _) = Spied(Full, failOnCall: 1);

        var result = machine.Execute(Account(250, 0), Withdraw(200));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, Empty, 0, Account(250, 0)), result);
    }

    [Fact]
    public void Panne_sur_un_depot()
    {
        var (machine, spy) = Spied(Notes(1, 1, 1), failOnCall: 1);

        var result = machine.Execute(Account(10, 30), Deposit(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, Empty, 0, Account(10, 30)), result);
        Assert.Equal(Notes(1, 1, 1), spy.Stock);
        Assert.Equal(1, spy.Calls);
    }

    [Fact]
    public void Dans_le_domaine_le_vrai_module_ne_tombe_jamais_en_panne_cassette_de_10_bloquee()
    {
        // Le vrai module refuse tout passage qui touche la cassette bloquée. Si la machine en propose
        // un, elle produit une panne là où la spec attend un résultat du domaine.
        var (machine, spy) = Spied(Notes(1, 3, 1), Denomination.Ten);

        var result = machine.Execute(Account(250, 0), Withdraw(60));

        Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
        Assert.All(spy.Dispensed, pass => Assert.Equal(0, pass.Tens));
    }
}
