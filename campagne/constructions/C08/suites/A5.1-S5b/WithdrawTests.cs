using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le retrait : contrôles, ordre des contrôles, frais, combinaison, sortie, succès.</summary>
public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    // ---- Contrôle 1 : montant invalide -------------------------------------------------------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_pas_multiple_de_10_est_invalide(int amount)
    {
        var (r, d) = Withdraw(250, 0, Full, null, amount);
        AssertRefused(AtmStatus.InvalidAmount, r, d, Acc(250, 0), Full);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_plafond_solde_et_billets(int amount)
    {
        // Plafond atteint, solde nul, aucun billet : seul le montant compte.
        var (r, d) = Withdraw(0, 200, N(0, 0, 0), null, amount);
        AssertRefused(AtmStatus.InvalidAmount, r, d, Acc(0, 200), N(0, 0, 0));
    }

    // ---- Contrôle 2 : plafond du jour --------------------------------------------------------

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    [InlineData(100, 100)]
    public void Atteindre_exactement_200_est_permis(int withdrawnToday, int amount)
    {
        var (r, _) = Withdraw(250, withdrawnToday, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(withdrawnToday + amount, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    public void Depasser_200_est_refuse(int withdrawnToday, int amount)
    {
        var (r, d) = Withdraw(250, withdrawnToday, Full, null, amount);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, d, Acc(250, withdrawnToday), Full);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 200 + 5 de frais : le plafond porte sur 200 seulement.
        var (r, _) = Withdraw(250, 0, Full, null, 200);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(45, 200), r.Account);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond_apres_retraits_precedents()
    {
        var (r, _) = Withdraw(250, 90, Full, null, 110);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(135, 200), r.Account);
    }

    [Fact]
    public void Plafond_passe_avant_solde_et_billets()
    {
        var (r, d) = Withdraw(0, 200, N(0, 0, 0), null, 10);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, d, Acc(0, 200), N(0, 0, 0));
    }

    // ---- Contrôle 3 : solde insuffisant ------------------------------------------------------

    [Theory]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    public void Solde_exactement_suffisant_frais_compris_est_permis(int balance, int amount)
    {
        var (r, _) = Withdraw(balance, 0, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Theory]
    [InlineData(45, 50)]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(200, 200)]
    [InlineData(0, 10)]
    public void Solde_trop_faible_frais_compris_est_refuse(int balance, int amount)
    {
        var (r, d) = Withdraw(balance, 0, Full, null, amount);
        AssertRefused(AtmStatus.InsufficientFunds, r, d, Acc(balance, 0), Full);
    }

    [Fact]
    public void Solde_passe_avant_billets()
    {
        var (r, d) = Withdraw(0, 0, N(0, 0, 0), null, 10);
        AssertRefused(AtmStatus.InsufficientFunds, r, d, Acc(0, 0), N(0, 0, 0));
    }

    // ---- Frais -------------------------------------------------------------------------------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var (r, _) = Withdraw(250, 0, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedFee, r.Fee);
        Assert.Equal(250 - amount - expectedFee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    // ---- Contrôle 4 : combinaison de billets -------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0, 10)]
    [InlineData(1, 0, 0, 30)] // 50 trop gros, rien d'autre
    [InlineData(0, 5, 0, 30)] // 20 seuls ne font jamais 30
    [InlineData(1, 1, 0, 30)]
    [InlineData(0, 2, 2, 90)] // tout le stock fait 60
    [InlineData(1, 0, 4, 100)] // 50 + 40 = 90 au plus
    public void Aucune_combinaison_exacte(int f, int t, int ten, int amount)
    {
        var stock = N(f, t, ten);
        var (r, d) = Withdraw(250, 0, stock, null, amount);
        AssertRefused(AtmStatus.CannotDispense, r, d, Acc(250, 0), stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 5, 0, 0, 50)]
    [InlineData(Denomination.Twenty, 0, 5, 0, 20)]
    [InlineData(Denomination.Ten, 0, 0, 5, 10)]
    [InlineData(Denomination.Ten, 5, 5, 5, 10)]
    [InlineData(Denomination.Ten, 5, 5, 5, 30)]
    [InlineData(Denomination.Twenty, 1, 5, 0, 70)]
    public void La_cassette_bloquee_compte_pour_zero(Denomination jammed, int f, int t, int ten, int amount)
    {
        var stock = N(f, t, ten);
        var (r, d) = Withdraw(250, 0, stock, jammed, amount);
        AssertRefused(AtmStatus.CannotDispense, r, d, Acc(250, 0), stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 100, 0, 5, 0)] // 20×5 plutôt que 50×2
    [InlineData(Denomination.Twenty, 60, 1, 0, 1)] // 50 + 10
    [InlineData(Denomination.Ten, 70, 1, 1, 0)] // 50 + 20
    [InlineData(Denomination.Fifty, 30, 0, 1, 1)]
    public void La_combinaison_evite_la_cassette_bloquee(
        Denomination jammed, int amount, int ef, int et, int eten)
    {
        var (r, d) = Withdraw(250, 0, Full, jammed, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(ef, et, eten), r.Notes);
        Assert.Equal(Full - N(ef, et, eten), d.Stock);
    }

    [Fact]
    public void Insuffisance_de_billets_passe_apres_les_autres_controles()
    {
        // Montant valide, sous le plafond, solde suffisant, mais aucun billet.
        var (r, d) = Withdraw(250, 0, N(0, 0, 0), null, 10);
        AssertRefused(AtmStatus.CannotDispense, r, d, Acc(250, 0), N(0, 0, 0));
    }

    [Theory]
    // stock (50, 20, 10), montant, combinaison attendue (50, 20, 10)
    [InlineData(5, 5, 5, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 20, 0, 1, 0)]
    [InlineData(5, 5, 5, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 50, 1, 0, 0)]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 70, 1, 1, 0)]
    [InlineData(5, 5, 5, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 100, 2, 0, 0)]
    [InlineData(5, 5, 5, 150, 3, 0, 0)]
    [InlineData(5, 5, 5, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, 200, 4, 0, 0)]
    // Le glouton échoue ou n'est pas minimal :
    [InlineData(1, 3, 0, 60, 0, 3, 0)] // pas de 10 : 20×3
    [InlineData(1, 4, 0, 80, 0, 4, 0)] // 50 + 30 impossible : 20×4
    [InlineData(1, 5, 0, 100, 0, 5, 0)]
    [InlineData(3, 5, 0, 160, 2, 3, 0)] // 50×3 + 10 impossible
    [InlineData(1, 3, 1, 60, 1, 0, 1)] // 50 + 10 (2 billets) plutôt que 20×3
    [InlineData(0, 2, 5, 80, 0, 2, 4)] // 20 manquants : complète en 10
    [InlineData(1, 0, 5, 100, 1, 0, 5)]
    [InlineData(0, 5, 5, 150, 0, 5, 5)]
    [InlineData(2, 1, 3, 150, 2, 1, 3)]
    public void La_combinaison_compte_le_moins_de_billets(
        int f, int t, int ten, int amount, int ef, int et, int eten)
    {
        var stock = N(f, t, ten);
        var expected = N(ef, et, eten);
        var (r, d) = Withdraw(250, 0, stock, null, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expected, r.Notes);
        Assert.Equal(amount, r.Notes.Value);
        Assert.Equal(stock - expected, d.Stock);
    }

    // ---- Sortie en plusieurs passages --------------------------------------------------------

    [Theory]
    // Plus de 4 billets : il faut plusieurs passages de 4 au plus.
    [InlineData(0, 5, 0, 100, 0, 5, 0)] // 5 billets
    [InlineData(0, 1, 4, 60, 0, 1, 4)] // 5 billets mêlant 20 et 10
    [InlineData(0, 4, 1, 90, 0, 4, 1)] // 5 billets mêlant 20 et 10
    [InlineData(3, 0, 2, 170, 3, 0, 2)] // 5 billets mêlant 50 et 10
    [InlineData(0, 5, 1, 110, 0, 5, 1)] // 6 billets
    [InlineData(1, 5, 3, 180, 1, 5, 3)] // 9 billets
    [InlineData(0, 5, 5, 150, 0, 5, 5)] // 10 billets
    [InlineData(4, 0, 0, 200, 4, 0, 0)] // 4 billets : un passage suffit
    public void Les_gros_retraits_sortent_en_plusieurs_passages(
        int f, int t, int ten, int amount, int ef, int et, int eten)
    {
        var stock = N(f, t, ten);
        var expected = N(ef, et, eten);
        var (r, d) = Withdraw(250, 0, stock, null, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expected, r.Notes);
        Assert.Equal(stock - expected, d.Stock);
        Assert.Equal(Acc(250 - amount - (amount > 100 ? 5 : 0), amount), r.Account);
    }

    [Fact]
    public void Chaque_passage_compte_au_plus_4_billets_et_la_somme_fait_la_combinaison()
    {
        var spy = new SpyDispenser(new MoneyDispenser(N(0, 5, 5)));
        var r = new CashMachine(spy).Execute(Acc(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 5, 5), r.Notes);
        Assert.All(spy.Dispensed, pass => Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(r.Notes, spy.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Empty(spy.Loaded);
        Assert.Equal(N(0, 0, 0), spy.Stock);
    }

    [Fact]
    public void Un_refus_ne_touche_pas_au_module()
    {
        var spy = new SpyDispenser(new MoneyDispenser(N(5, 5, 5)));
        var r = new CashMachine(spy).Execute(Acc(250, 150), new Operation.Withdraw(60));

        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
        Assert.Empty(spy.Dispensed);
        Assert.Empty(spy.Loaded);
    }

    // ---- Succès ------------------------------------------------------------------------------

    [Fact]
    public void Succes_le_compte_et_le_stock_suivent_le_retrait()
    {
        var (r, d) = Withdraw(200, 40, N(2, 3, 4), null, 120);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 1, 0), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(75, 160), r.Account);
        Assert.Equal(N(0, 2, 4), d.Stock);
    }

    [Fact]
    public void Le_resultat_n_est_jamais_une_panne_dans_le_domaine()
    {
        // 10 billets en plusieurs passages, cassette de 50 bloquée.
        var (r, _) = Withdraw(250, 0, N(5, 5, 5), Denomination.Fifty, 150);

        Assert.NotEqual(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 5, 5), r.Notes);
        Assert.Equal(Acc(95, 150), r.Account);
    }
}
