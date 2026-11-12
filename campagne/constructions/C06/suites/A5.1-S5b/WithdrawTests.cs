using static Atm.Tests.Support;

namespace Atm.Tests;

public class WithdrawTests
{
    // ---------- Contrôle 1 : montant invalide ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (atm, dispenser) = Machine(Full);

        var result = atm.Execute(Account(250, 0), Withdraw(amount));

        AssertRefused(AtmStatus.InvalidAmount, result, Account(250, 0), dispenser, Full);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_plafond_fonds_et_billets(int amount)
    {
        // Plafond atteint, solde nul, aucun billet : tous les autres contrôles échoueraient aussi.
        var (atm, dispenser) = Machine(Notes(0, 0, 0));

        var result = atm.Execute(Account(0, 200), Withdraw(amount));

        AssertRefused(AtmStatus.InvalidAmount, result, Account(0, 200), dispenser, Notes(0, 0, 0));
    }

    // ---------- Contrôle 2 : plafond du jour ----------

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(190, 20)]
    [InlineData(200, 10)]
    [InlineData(150, 60)]
    public void Depasser_200_dans_la_journee_est_refuse(int withdrawnToday, int amount)
    {
        var (atm, dispenser) = Machine(Full);

        var result = atm.Execute(Account(250, withdrawnToday), Withdraw(amount));

        AssertRefused(AtmStatus.DailyLimitExceeded, result, Account(250, withdrawnToday), dispenser, Full);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(190, 10)]
    [InlineData(100, 100)]
    [InlineData(50, 150)]
    public void Atteindre_exactement_200_est_permis(int withdrawnToday, int amount)
    {
        var (atm, _) = Machine(Full);

        var result = atm.Execute(Account(250, withdrawnToday), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(200, result.Account.WithdrawnToday);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 200 + 5 de frais : le plafond porte sur le montant seul.
        var (atm, _) = Machine(Full);

        var result = atm.Execute(Account(205, 0), Withdraw(200));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(4, 0, 0), 5, Account(0, 200)), result);
    }

    [Fact]
    public void Plafond_passe_avant_les_fonds_et_les_billets()
    {
        var (atm, dispenser) = Machine(Notes(0, 0, 0));

        var result = atm.Execute(Account(0, 200), Withdraw(10));

        AssertRefused(AtmStatus.DailyLimitExceeded, result, Account(0, 200), dispenser, Notes(0, 0, 0));
    }

    // ---------- Contrôle 3 : fonds insuffisants ----------

    [Theory]
    [InlineData(50, 60)]
    [InlineData(0, 10)]
    [InlineData(95, 100)]
    public void Montant_sans_frais_superieur_au_solde_est_refuse(int balance, int amount)
    {
        var (atm, dispenser) = Machine(Full);

        var result = atm.Execute(Account(balance, 0), Withdraw(amount));

        AssertRefused(AtmStatus.InsufficientFunds, result, Account(balance, 0), dispenser, Full);
    }

    [Theory]
    [InlineData(110, 110)]
    [InlineData(200, 200)]
    [InlineData(120, 120)]
    public void Montant_plus_frais_superieur_au_solde_est_refuse(int balance, int amount)
    {
        var (atm, dispenser) = Machine(Full);

        var result = atm.Execute(Account(balance, 0), Withdraw(amount));

        AssertRefused(AtmStatus.InsufficientFunds, result, Account(balance, 0), dispenser, Full);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    public void Solde_egal_a_montant_plus_frais_est_suffisant(int balance, int amount)
    {
        var (atm, _) = Machine(Full);

        var result = atm.Execute(Account(balance, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Account.Balance);
    }

    [Fact]
    public void Fonds_passent_avant_les_billets()
    {
        var (atm, dispenser) = Machine(Notes(0, 0, 0));

        var result = atm.Execute(Account(0, 0), Withdraw(10));

        AssertRefused(AtmStatus.InsufficientFunds, result, Account(0, 0), dispenser, Notes(0, 0, 0));
    }

    // ---------- Les frais ----------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var (atm, _) = Machine(Full);

        var result = atm.Execute(Account(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(250 - amount - expectedFee, result.Account.Balance);
        Assert.Equal(amount, result.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(150, 50)]
    [InlineData(100, 10)]
    [InlineData(60, 100)]
    [InlineData(190, 10)]
    public void Les_frais_dependent_du_montant_seul_pas_du_deja_retire(int withdrawnToday, int amount)
    {
        // amount <= 100 : pas de frais, quel que soit ce qui a déjà été retiré aujourd'hui.
        var (atm, _) = Machine(Full);

        var result = atm.Execute(Account(250, withdrawnToday), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(250 - amount, withdrawnToday + amount), result.Account);
    }

    [Fact]
    public void Sans_frais_le_solde_juste_suffit_meme_apres_d_autres_retraits()
    {
        var (atm, dispenser) = Machine(Full);

        var result = atm.Execute(Account(50, 150), Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 0, 0), 0, Account(0, 200)), result);
        Assert.Equal(Notes(4, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Frais_de_5_au_dessus_de_100_meme_apres_d_autres_retraits()
    {
        var (atm, _) = Machine(Full);

        var result = atm.Execute(Account(250, 90), Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 1), 5, Account(135, 200)), result);
    }

    // ---------- Contrôle 4 : combinaison de billets ----------

    [Fact]
    public void Aucun_billet_donne_CannotDispense()
    {
        var (atm, dispenser) = Machine(Notes(0, 0, 0));

        var result = atm.Execute(Account(250, 0), Withdraw(10));

        AssertRefused(AtmStatus.CannotDispense, result, Account(250, 0), dispenser, Notes(0, 0, 0));
    }

    [Theory]
    [InlineData(5, 5, 0, 10)]   // pas de 10
    [InlineData(5, 0, 0, 20)]   // que des 50
    [InlineData(0, 5, 0, 30)]   // que des 20 : 30 impossible
    [InlineData(1, 0, 0, 60)]   // un 50 mais pas de 10
    [InlineData(1, 1, 1, 90)]   // 80 en tout, moins que 90
    [InlineData(0, 0, 5, 60)]   // 50 en tout
    [InlineData(2, 0, 0, 150)]  // 100 en tout
    public void Sans_combinaison_exacte_le_retrait_est_refuse(int f, int t, int c, int amount)
    {
        var stock = Notes(f, t, c);
        var (atm, dispenser) = Machine(stock);

        var result = atm.Execute(Account(250, 0), Withdraw(amount));

        AssertRefused(AtmStatus.CannotDispense, result, Account(250, 0), dispenser, stock);
    }

    [Theory]
    [InlineData(5, 0, 5, Denomination.Ten, 60)]     // 50+10 impossible sans les 10
    [InlineData(5, 5, 0, Denomination.Fifty, 50)]   // sans les 50 ni de 10 : 50 impossible
    [InlineData(0, 5, 0, Denomination.Twenty, 20)]  // il ne reste que la cassette bloquée
    [InlineData(1, 5, 0, Denomination.Fifty, 70)]   // 50+20 impossible, 20 seuls ne font pas 70
    public void La_cassette_bloquee_compte_pour_zero(int f, int t, int c, Denomination jammed, int amount)
    {
        var stock = Notes(f, t, c);
        var (atm, dispenser) = Machine(stock, jammed);

        var result = atm.Execute(Account(250, 0), Withdraw(amount));

        AssertRefused(AtmStatus.CannotDispense, result, Account(250, 0), dispenser, stock);
    }

    [Fact]
    public void Cassette_bloquee_contournee_avec_d_autres_coupures()
    {
        // 50 bloqué : 100 = 20×5 plutôt que 50×2.
        var (atm, dispenser) = Machine(Full, Denomination.Fifty);

        var result = atm.Execute(Account(250, 0), Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 5, 0), 0, Account(150, 100)), result);
        Assert.Equal(Notes(5, 0, 5), dispenser.Stock);
    }

    [Fact]
    public void Cassette_bloquee_20_on_sort_des_10()
    {
        var (atm, dispenser) = Machine(Notes(0, 5, 5), Denomination.Twenty);

        var result = atm.Execute(Account(250, 0), Withdraw(20));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(0, 0, 2), 0, Account(230, 20)), result);
        Assert.Equal(Notes(0, 5, 3), dispenser.Stock);
    }

    [Fact]
    public void Cassette_bloquee_10_retrait_qui_exige_des_10()
    {
        var (atm, dispenser) = Machine(Full, Denomination.Ten);

        var result = atm.Execute(Account(250, 0), Withdraw(10));

        AssertRefused(AtmStatus.CannotDispense, result, Account(250, 0), dispenser, Full);
    }

    // ---------- Le moins de billets ----------

    public static TheoryData<NoteCounts, int, NoteCounts> FewestNotesCases => new()
    {
        // stock complet : solution « gloutonne »
        { Full, 10, Notes(0, 0, 1) },
        { Full, 20, Notes(0, 1, 0) },
        { Full, 30, Notes(0, 1, 1) },
        { Full, 40, Notes(0, 2, 0) },
        { Full, 50, Notes(1, 0, 0) },
        { Full, 60, Notes(1, 0, 1) },
        { Full, 70, Notes(1, 1, 0) },
        { Full, 80, Notes(1, 1, 1) },
        { Full, 90, Notes(1, 2, 0) },
        { Full, 100, Notes(2, 0, 0) },
        { Full, 150, Notes(3, 0, 0) },
        { Full, 190, Notes(3, 2, 0) },
        { Full, 200, Notes(4, 0, 0) },
        // le glouton échoue : prendre un 50 empêche de finir
        { Notes(1, 3, 0), 60, Notes(0, 3, 0) },
        { Notes(1, 4, 0), 80, Notes(0, 4, 0) },
        { Notes(3, 5, 0), 160, Notes(2, 3, 0) },
        { Notes(1, 5, 0), 100, Notes(0, 5, 0) },
        // le glouton par coupure ne minimise pas toujours : 50+10×3 (4) vs 20×4 (4) vs 50+20+10 (3)
        { Notes(1, 4, 3), 80, Notes(1, 1, 1) },
        // moins de 20 qu'il n'en faudrait : on complète avec des 10
        { Notes(0, 1, 5), 60, Notes(0, 1, 4) },
        { Notes(2, 0, 5), 140, Notes(2, 0, 4) },
        // 50 inutilisable seul, compensé par 20 et 10
        { Notes(1, 2, 1), 50, Notes(1, 0, 0) },
        { Notes(0, 2, 1), 50, Notes(0, 2, 1) },
        // seulement des 10
        { Notes(0, 0, 5), 50, Notes(0, 0, 5) },
        // beaucoup de billets
        { Notes(0, 5, 5), 150, Notes(0, 5, 5) },
        { Notes(5, 5, 5), 200, Notes(4, 0, 0) },
    };

    [Theory]
    [MemberData(nameof(FewestNotesCases))]
    public void La_combinaison_compte_le_moins_de_billets(NoteCounts stock, int amount, NoteCounts expected)
    {
        var (atm, dispenser) = Machine(stock);

        var result = atm.Execute(Account(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(amount, result.Notes.Value);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    // ---------- La sortie en plusieurs passages ----------

    public static TheoryData<NoteCounts, Denomination?, int, NoteCounts> ManyNotesCases => new()
    {
        { Full, null, 190, Notes(3, 2, 0) },                       // 5 billets
        { Full, null, 180, Notes(3, 1, 1) },                       // 5 billets
        { Full, Denomination.Fifty, 100, Notes(0, 5, 0) },          // 5 billets d'une même coupure
        { Notes(0, 5, 5), null, 150, Notes(0, 5, 5) },              // 10 billets : 3 passages
        { Notes(0, 0, 5), null, 50, Notes(0, 0, 5) },               // 5 billets de 10
        { Full, Denomination.Fifty, 150, Notes(0, 5, 5) },          // 10 billets, cassette bloquée
        { Notes(5, 0, 5), Denomination.Twenty, 200, Notes(4, 0, 0) },
        { Notes(1, 5, 5), null, 200, Notes(1, 5, 5) },              // 11 billets
    };

    [Theory]
    [MemberData(nameof(ManyNotesCases))]
    public void Plus_de_4_billets_sortent_en_plusieurs_passages(NoteCounts stock, Denomination? jammed, int amount, NoteCounts expected)
    {
        // Le vrai module refuse tout passage de plus de 4 billets : la machine doit découper.
        var (atm, dispenser) = Machine(stock, jammed);

        var result = atm.Execute(Account(250, 0), Withdraw(amount));

        var fee = SpecModel.FeeFor(amount);
        Assert.Equal(new AtmResult(AtmStatus.Ok, expected, fee, Account(250 - amount - fee, amount)), result);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    // ---------- Le succès ----------

    [Fact]
    public void Succes_met_a_jour_compte_et_module()
    {
        var (atm, dispenser) = Machine(Notes(2, 3, 4));

        var result = atm.Execute(Account(200, 40), Withdraw(120));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(2, 1, 0), result.Notes);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Account(75, 160), result.Account);
        Assert.Equal(Notes(0, 2, 4), dispenser.Stock);
    }

    [Fact]
    public void Retrait_ne_modifie_pas_la_cassette_bloquee()
    {
        var (atm, dispenser) = Machine(Notes(3, 3, 3), Denomination.Twenty);

        var result = atm.Execute(Account(250, 0), Withdraw(70));

        Assert.Equal(Notes(1, 0, 2), result.Notes);
        Assert.Equal(Notes(2, 3, 1), dispenser.Stock);
        Assert.Equal(Denomination.Twenty, dispenser.Jammed);
    }
}
