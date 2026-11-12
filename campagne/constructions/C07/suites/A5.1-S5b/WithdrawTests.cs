using Atm.Tests.Support;
using static Atm.Tests.Support.N;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = Notes(5, 5, 5);

    // ---- Contrôle 1 : montant ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var run = Scenario.Withdraw(250, 0, Full, null, amount);

        Assert.Equal(AtmStatus.InvalidAmount, run.Result.Status);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, stock vide : seul le montant doit compter.
        var run = Scenario.Withdraw(0, 200, Notes(0, 0, 0), Denomination.Ten, amount);

        Assert.Equal(AtmStatus.InvalidAmount, run.Result.Status);
    }

    [Fact]
    public void Plus_petit_montant_valide_est_accepte()
    {
        var run = Scenario.Withdraw(10, 0, Full, null, 10);

        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.Equal(Notes(0, 0, 1), run.Result.Notes);
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(190, 20)]
    [InlineData(200, 10)]
    public void Depasser_200_dans_la_journee_est_refuse(int withdrawnToday, int amount)
    {
        var run = Scenario.Withdraw(250, withdrawnToday, Full, null, amount);

        Assert.Equal(AtmStatus.DailyLimitExceeded, run.Result.Status);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_200_est_permis(int withdrawnToday, int amount)
    {
        var run = Scenario.Withdraw(250, withdrawnToday, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.Equal(200, run.Result.Account.WithdrawnToday);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : permis, même si 110 + 5 de frais font 205 au total.
        var run = Scenario.Withdraw(250, 90, Full, null, 110);

        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.Equal(5, run.Result.Fee);
        Assert.Equal(new AccountState(135, 200), run.Result.Account);
    }

    [Fact]
    public void Plafond_passe_avant_le_solde()
    {
        var run = Scenario.Withdraw(0, 150, Full, null, 60);

        Assert.Equal(AtmStatus.DailyLimitExceeded, run.Result.Status);
    }

    [Fact]
    public void Plafond_passe_avant_la_disponibilite_des_billets()
    {
        var run = Scenario.Withdraw(250, 200, Notes(0, 0, 0), null, 10);

        Assert.Equal(AtmStatus.DailyLimitExceeded, run.Result.Status);
    }

    // ---- Contrôle 3 : solde et frais ----

    [Theory]
    [InlineData(100, 0)]
    [InlineData(90, 0)]
    [InlineData(10, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var run = Scenario.Withdraw(250, 0, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.Equal(expectedFee, run.Result.Fee);
        Assert.Equal(250 - amount - expectedFee, run.Result.Account.Balance);
    }

    [Theory]
    [InlineData(55, 60)]
    [InlineData(0, 10)]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(114, 110)]
    [InlineData(200, 200)]
    public void Montant_plus_frais_superieur_au_solde_est_refuse(int balance, int amount)
    {
        var run = Scenario.Withdraw(balance, 0, Full, null, amount);

        Assert.Equal(AtmStatus.InsufficientFunds, run.Result.Status);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(100, 100)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    public void Montant_plus_frais_egal_au_solde_est_permis(int balance, int amount)
    {
        var run = Scenario.Withdraw(balance, 0, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.Equal(0, run.Result.Account.Balance);
    }

    [Fact]
    public void Solde_passe_avant_la_disponibilite_des_billets()
    {
        var run = Scenario.Withdraw(10, 0, Notes(0, 0, 0), null, 20);

        Assert.Equal(AtmStatus.InsufficientFunds, run.Result.Status);
    }

    // ---- Contrôle 4 : billets disponibles ----

    [Fact]
    public void Stock_vide_ne_peut_rien_sortir()
    {
        var run = Scenario.Withdraw(250, 0, Notes(0, 0, 0), null, 10);

        Assert.Equal(AtmStatus.CannotDispense, run.Result.Status);
    }

    [Fact]
    public void Montant_impossible_a_composer_exactement()
    {
        // 30 avec seulement des 20 et des 50.
        var run = Scenario.Withdraw(250, 0, Notes(5, 5, 0), null, 30);

        Assert.Equal(AtmStatus.CannotDispense, run.Result.Status);
    }

    [Fact]
    public void Stock_insuffisant_en_valeur()
    {
        // 50 + 20 + 10 = 80 < 90.
        var run = Scenario.Withdraw(250, 0, Notes(1, 1, 1), null, 90);

        Assert.Equal(AtmStatus.CannotDispense, run.Result.Status);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50)]
    [InlineData(Denomination.Twenty, 20)]
    [InlineData(Denomination.Ten, 10)]
    public void La_cassette_bloquee_compte_pour_zero(Denomination jammed, int amount)
    {
        var stock = jammed switch
        {
            Denomination.Fifty => Notes(5, 0, 0),
            Denomination.Twenty => Notes(0, 5, 0),
            _ => Notes(0, 0, 5),
        };

        var run = Scenario.Withdraw(250, 0, stock, jammed, amount);

        Assert.Equal(AtmStatus.CannotDispense, run.Result.Status);
        Assert.Empty(run.Dispenser.Dispenses);
    }

    [Fact]
    public void Cassette_de_10_bloquee_rend_impossible_un_montant_qui_l_exige()
    {
        // 70 = 50 + 20 sans 10 ; mais 30 exige un 10.
        var run = Scenario.Withdraw(250, 0, Notes(5, 5, 5), Denomination.Ten, 30);

        Assert.Equal(AtmStatus.CannotDispense, run.Result.Status);
    }

    [Fact]
    public void Cassette_bloquee_force_une_autre_combinaison()
    {
        // Sans blocage : 50×2. Avec les 50 bloqués : 20×5 plutôt que 20×4 + 10×2.
        var run = Scenario.Withdraw(250, 0, Notes(5, 5, 5), Denomination.Fifty, 100);

        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.Equal(Notes(0, 5, 0), run.Result.Notes);
    }

    // ---- La combinaison : le moins de billets ----

    [Theory]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]   // 50+10 plutôt que 20×3
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // pas de 10 : 20×3
    [InlineData(5, 5, 5, 40, 0, 2, 0)]   // 20×2 plutôt que 10×4
    [InlineData(5, 5, 5, 90, 1, 2, 0)]   // 50+20+20
    [InlineData(5, 5, 5, 200, 4, 0, 0)]
    [InlineData(5, 5, 5, 180, 3, 1, 1)]
    [InlineData(5, 5, 5, 130, 2, 1, 1)]
    [InlineData(2, 4, 0, 130, 1, 4, 0)]  // le glouton (50×2 puis 30) échoue
    [InlineData(1, 3, 0, 110, 1, 3, 0)]  // le glouton échoue aussi ici
    [InlineData(0, 0, 5, 50, 0, 0, 5)]   // seulement des 10
    [InlineData(1, 0, 5, 50, 1, 0, 0)]
    [InlineData(0, 5, 5, 100, 0, 5, 0)]  // 20×5 plutôt que 20×4+10×2
    [InlineData(5, 1, 5, 80, 1, 1, 1)]
    [InlineData(1, 5, 5, 150, 1, 5, 0)]
    public void La_combinaison_compte_le_moins_de_billets(
        int stock50, int stock20, int stock10, int amount, int exp50, int exp20, int exp10)
    {
        var run = Scenario.Withdraw(250, 0, Notes(stock50, stock20, stock10), null, amount);

        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.Equal(Notes(exp50, exp20, exp10), run.Result.Notes);
    }

    // ---- La sortie ----

    [Theory]
    [InlineData(0, 0, 5, 50)]    // 5 billets : deux passages au moins
    [InlineData(1, 4, 0, 130)]   // 5 billets
    [InlineData(4, 0, 0, 200)]   // 4 billets : un passage suffit
    [InlineData(5, 5, 5, 180)]
    [InlineData(0, 5, 5, 100)]
    [InlineData(1, 5, 5, 150)]   // 6 billets
    public void Les_billets_sortent_par_passages_de_4_au_plus(int stock50, int stock20, int stock10, int amount)
    {
        var stock = Notes(stock50, stock20, stock10);
        var run = Scenario.Withdraw(250, 0, stock, null, amount);

        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.NotEmpty(run.Dispenser.Dispenses);
        Assert.All(run.Dispenser.Dispenses, pass =>
        {
            Assert.True(pass.Accepted);
            Assert.InRange(pass.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass);
        });
        Assert.Equal(run.Result.Notes, run.Dispenser.DispensedTotal);
        Assert.Equal(stock - run.Result.Notes, run.Dispenser.Stock);
        Assert.Empty(run.Dispenser.Loads);
    }

    [Fact]
    public void Succes_decrit_completement_le_resultat()
    {
        var run = Scenario.Withdraw(250, 40, Notes(3, 3, 3), Denomination.Twenty, 160);

        // 160 sans les 20 : 50×3 + 10.
        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(3, 0, 1), 5, new AccountState(85, 200)), run.Result);
        Assert.Equal(Notes(0, 3, 2), run.Dispenser.Stock);
    }
}
