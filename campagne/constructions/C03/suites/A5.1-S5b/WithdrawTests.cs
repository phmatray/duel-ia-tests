using static Atm.Tests.TestSupport;

namespace Atm.Tests;

public class WithdrawTests
{
    private static void AssertOk(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount,
        NoteCounts expectedNotes, int expectedFee)
    {
        var (r, d) = Withdraw(balance, withdrawnToday, stock, jammed, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedNotes, r.Notes);
        Assert.Equal(expectedFee, r.Fee);
        Assert.Equal(new AccountState(balance - amount - expectedFee, withdrawnToday + amount), r.Account);
        Assert.Equal(stock - expectedNotes, d.Stock);
    }

    // ---- Contrôle 1 : montant invalide ------------------------------------------------------------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount) =>
        AssertRefused(AtmStatus.InvalidAmount, Withdraw(250, 0, FullStock, null, amount), 250, 0, FullStock);

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_est_controle_avant_tout_le_reste(int amount) =>
        // Plafond atteint, solde nul, aucun billet : c'est quand même InvalidAmount.
        AssertRefused(AtmStatus.InvalidAmount, Withdraw(0, 200, N(0, 0, 0), Denomination.Ten, amount), 0, 200, N(0, 0, 0));

    // ---- Contrôle 2 : plafond du jour ------------------------------------------------------------

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(190, 20)]
    [InlineData(200, 10)]
    public void Depasser_200_dans_la_journee_est_refuse(int withdrawnToday, int amount) =>
        AssertRefused(AtmStatus.DailyLimitExceeded, Withdraw(250, withdrawnToday, FullStock, null, amount), 250, withdrawnToday, FullStock);

    [Theory]
    [InlineData(0, 200, 4, 0, 0, 5)]
    [InlineData(100, 100, 2, 0, 0, 0)]
    [InlineData(190, 10, 0, 0, 1, 0)]
    [InlineData(90, 110, 2, 0, 1, 5)]
    public void Atteindre_exactement_200_est_permis(int withdrawnToday, int amount, int f, int t, int n, int fee) =>
        AssertOk(250, withdrawnToday, FullStock, null, amount, N(f, t, n), fee);

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond() =>
        // 200 + 5 € de frais : le plafond ne voit que 200.
        AssertOk(250, 0, FullStock, null, 200, N(4, 0, 0), 5);

    [Fact]
    public void Plafond_est_controle_avant_les_fonds_et_les_billets() =>
        AssertRefused(AtmStatus.DailyLimitExceeded, Withdraw(0, 200, N(0, 0, 0), null, 10), 0, 200, N(0, 0, 0));

    // ---- Contrôle 3 : fonds insuffisants ---------------------------------------------------------

    [Theory]
    [InlineData(0, 10)]
    [InlineData(90, 100)]
    [InlineData(95, 100)]
    [InlineData(105, 110)]
    [InlineData(110, 110)]   // 110 + 5 de frais > 110
    [InlineData(200, 200)]   // 200 + 5 de frais > 200
    [InlineData(150, 150)]
    public void Montant_plus_frais_superieur_au_solde_est_refuse(int balance, int amount) =>
        AssertRefused(AtmStatus.InsufficientFunds, Withdraw(balance, 0, FullStock, null, amount), balance, 0, FullStock);

    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 5)]
    [InlineData(205, 200, 5)]
    [InlineData(155, 150, 5)]
    public void Montant_plus_frais_egal_au_solde_est_permis(int balance, int amount, int fee)
    {
        var (r, _) = Withdraw(balance, 0, FullStock, null, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(0, r.Account.Balance);
    }

    [Fact]
    public void Fonds_sont_controles_avant_les_billets() =>
        AssertRefused(AtmStatus.InsufficientFunds, Withdraw(0, 0, N(0, 0, 0), null, 10), 0, 0, N(0, 0, 0));

    // ---- Frais -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_au_dela_de_100_strictement(int amount, int expectedFee)
    {
        var (r, _) = Withdraw(250, 0, FullStock, null, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedFee, r.Fee);
        Assert.Equal(250 - amount - expectedFee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    // ---- Contrôle 4 : impossible à servir --------------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0, null, 10)]
    [InlineData(1, 0, 0, null, 30)]     // 50 ne fait pas 30
    [InlineData(1, 1, 0, null, 30)]     // 50 et 20 ne font pas 30
    [InlineData(1, 1, 1, null, 90)]     // stock de 80 seulement
    [InlineData(0, 5, 0, null, 50)]     // 20 seuls : pas de multiple de 20 qui fasse 50
    [InlineData(5, 0, 0, null, 60)]
    [InlineData(5, 5, 5, Denomination.Ten, 30)]   // 10 bloquée : 50/20 ne font pas 30
    [InlineData(5, 0, 5, Denomination.Ten, 10)]
    [InlineData(5, 0, 0, Denomination.Fifty, 50)]
    [InlineData(0, 5, 0, Denomination.Twenty, 20)]
    [InlineData(5, 5, 5, Denomination.Fifty, 160)]  // 20×5 + 10×5 = 150 seulement
    public void Aucune_combinaison_exacte_disponible(int f, int t, int n, Denomination? jammed, int amount) =>
        AssertRefused(AtmStatus.CannotDispense, Withdraw(250, 0, N(f, t, n), jammed, amount), 250, 0, N(f, t, n));

    // ---- Combinaison : le moins de billets ---------------------------------------------------------

    [Theory]
    // stock (50,20,10), bloquée, montant, billets attendus (50,20,10), frais
    [InlineData(1, 3, 0, null, 60, 0, 3, 0, 0)]          // pas de glouton : 50 puis bloqué
    [InlineData(5, 5, 5, null, 60, 1, 0, 1, 0)]
    [InlineData(2, 3, 0, null, 110, 1, 3, 0, 5)]         // pas de glouton : 50×2 puis bloqué
    [InlineData(5, 5, 5, null, 80, 1, 1, 1, 0)]
    [InlineData(0, 5, 5, null, 80, 0, 4, 0, 0)]
    [InlineData(5, 5, 0, null, 80, 0, 4, 0, 0)]
    [InlineData(5, 5, 5, null, 30, 0, 1, 1, 0)]
    [InlineData(5, 5, 5, null, 40, 0, 2, 0, 0)]
    [InlineData(5, 5, 5, null, 70, 1, 1, 0, 0)]
    [InlineData(5, 5, 5, null, 90, 1, 2, 0, 0)]
    [InlineData(5, 2, 1, null, 130, 2, 1, 1, 5)]
    [InlineData(2, 5, 0, null, 160, 2, 3, 0, 5)]
    [InlineData(5, 5, 5, null, 200, 4, 0, 0, 5)]
    [InlineData(5, 5, 5, Denomination.Fifty, 100, 0, 5, 0, 0)]
    [InlineData(5, 5, 5, Denomination.Twenty, 60, 1, 0, 1, 0)]
    [InlineData(5, 5, 5, Denomination.Twenty, 40, 0, 0, 4, 0)]
    [InlineData(5, 5, 5, Denomination.Ten, 60, 0, 3, 0, 0)]
    [InlineData(2, 2, 0, Denomination.Fifty, 40, 0, 2, 0, 0)]
    public void Combinaison_avec_le_moins_de_billets(
        int sf, int st, int sn, Denomination? jammed, int amount, int f, int t, int n, int fee) =>
        AssertOk(250, 0, N(sf, st, sn), jammed, amount, N(f, t, n), fee);

    [Fact]
    public void Cassette_bloquee_compte_pour_zero_meme_si_pleine() =>
        AssertOk(250, 0, N(5, 5, 5), Denomination.Fifty, 50, N(0, 2, 1), 0);

    // ---- Sortie en plusieurs passages de 4 billets au plus ---------------------------------------

    [Theory]
    [InlineData(5, 5, 5, null, 180, 3, 1, 1)]                 // 5 billets
    [InlineData(5, 5, 5, null, 190, 3, 2, 0)]                 // 5 billets
    [InlineData(5, 0, 5, null, 90, 1, 0, 4)]                  // 5 billets
    [InlineData(0, 5, 5, null, 150, 0, 5, 5)]                 // 10 billets
    [InlineData(5, 5, 5, Denomination.Fifty, 150, 0, 5, 5)]   // 10 billets, cassette bloquée
    [InlineData(1, 5, 5, null, 200, 1, 5, 5)]                 // 11 billets
    [InlineData(0, 0, 5, null, 50, 0, 0, 5)]                  // 5 billets d'une seule coupure
    [InlineData(5, 5, 5, Denomination.Fifty, 90, 0, 4, 1)]    // 5 billets, 20 et 10
    [InlineData(2, 2, 1, null, 150, 2, 2, 1)]                 // 5 billets, les trois coupures
    public void Plus_de_4_billets_sortent_en_plusieurs_passages(
        int sf, int st, int sn, Denomination? jammed, int amount, int f, int t, int n) =>
        AssertOk(250, 0, N(sf, st, sn), jammed, amount, N(f, t, n), FeeFor(amount));

    // ---- Succès : compte -------------------------------------------------------------------------

    [Fact]
    public void Succes_debite_le_solde_et_augmente_le_retire_du_jour() =>
        AssertOk(250, 40, FullStock, null, 120, N(2, 1, 0), 5);
}
