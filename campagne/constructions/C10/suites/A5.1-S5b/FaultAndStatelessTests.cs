using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Pannes : le module rend false malgré les contrôles. Le vrai module ne le fait jamais
/// dans le domaine ; on simule donc la panne en enveloppant le vrai module.
/// </summary>
public class DispenserFaultTests
{
    [Fact]
    public void Panne_au_premier_passage_du_retrait()
    {
        var stock = N(5, 5, 5);
        var rig = new Rig(stock, failDispenseAt: 1);
        var account = Acc(250, 0);

        var result = rig.Run(account, Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Single(rig.Spy.DispenseCalls); // on s'arrête aussitôt
        Assert.Equal(stock, rig.Real.Stock);
    }

    [Theory]
    [InlineData(0, 5, 0, 0, 100, 2)]  // 5 billets : 2 passages, panne au 2e
    [InlineData(5, 5, 0, 0, 130, 2)]  // frais de 5 non prélevés
    [InlineData(0, 5, 5, 0, 150, 2)]  // 10 billets : 3 passages, panne au 2e
    [InlineData(0, 5, 5, 0, 150, 3)]  // panne au 3e
    [InlineData(5, 5, 5, 50, 150, 3)]
    public void Panne_en_cours_de_retrait_rend_les_billets_deja_sortis(int f, int t, int d, int jammed, int amount, int failAt)
    {
        var stock = N(f, t, d);
        var rig = new Rig(stock, Jam(jammed), failDispenseAt: failAt);
        var account = Acc(250, 0);

        var result = rig.Run(account, Withdraw(amount));

        var alreadyOut = rig.Spy.SuccessfulPasses.Aggregate(NoteCounts.None, (a, b) => a + b);
        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(failAt - 1, rig.Spy.SuccessfulPasses.Count);
        Assert.Equal(failAt, rig.Spy.DispenseCalls.Count); // on s'arrête aussitôt
        Assert.Equal(alreadyOut, result.Notes);
        Assert.True(result.Notes.Total > 0);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock - alreadyOut, rig.Real.Stock);
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(0, 1, 0)]
    public void Panne_au_depot(int df, int dt, int dd)
    {
        var stock = N(1, 1, 0);
        var rig = new Rig(stock, failLoadAt: 1);
        var account = Acc(10, 30);

        var result = rig.Run(account, Deposit(df, dt, dd));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Single(rig.Spy.LoadCalls);
        Assert.Equal(stock, rig.Real.Stock);
    }
}

/// <summary>La machine est sans mémoire : elle ne dépend que du compte, de l'opération et du module.</summary>
public class StatelessTests
{
    [Fact]
    public void Deux_machines_sur_des_modules_identiques_donnent_le_meme_resultat()
    {
        var used = new Rig(N(5, 5, 5));
        used.Run(Acc(250, 0), Withdraw(200));     // fait vivre la machine
        used.Run(Acc(0, 0), Withdraw(10));        // un refus
        used.Run(Acc(10, 0), Deposit(1, 1, 1));   // un dépôt

        var fresh = new Rig(used.Real.Stock);
        var op = Withdraw(110);
        var account = Acc(200, 50);

        var expected = fresh.Run(account, op);
        Assert.Equal(expected, used.Run(account, op));
        Assert.Equal(fresh.Real.Stock, used.Real.Stock);
    }

    [Fact]
    public void Le_compte_passe_en_entree_fait_foi_pas_celui_de_l_appel_precedent()
    {
        var rig = new Rig(N(5, 5, 5));
        var first = rig.Run(Acc(250, 0), Withdraw(150));
        Assert.Equal(AtmStatus.Ok, first.Status);

        // On repasse un compte « neuf » : la machine ne doit pas se souvenir des 150 retirés.
        var second = rig.Run(Acc(250, 0), Withdraw(100));
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(Acc(150, 100), second.Account);
    }

    [Fact]
    public void Le_stock_courant_du_module_fait_foi()
    {
        var rig = new Rig(N(1, 0, 0));
        Assert.Equal(AtmStatus.Ok, rig.Run(Acc(250, 0), Withdraw(50)).Status);
        Assert.Equal(AtmStatus.CannotDispense, rig.Run(Acc(250, 0), Withdraw(50)).Status);
        Assert.Equal(AtmStatus.Ok, rig.Run(Acc(250, 0), Deposit(1, 0, 0)).Status);
        Assert.Equal(AtmStatus.Ok, rig.Run(Acc(250, 0), Withdraw(50)).Status);
    }
}
