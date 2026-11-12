using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    private static void AssertRefused(AtmStatus status, AccountState account, NoteCounts stock, AtmResult r, SpyDispenser spy)
    {
        Assert.Equal(new AtmResult(status, NoteCounts.None, 0, account), r);
        Assert.Equal(stock, spy.Stock);
        Assert.Equal(NoteCounts.None, spy.TotalDispensed);
        Assert.DoesNotContain(spy.Loaded, l => l.Ok);
    }

    // --- Contrôle 1 : montant ---

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (r, spy) = Run(Acc(250, 0), Full, null, W(amount));
        AssertRefused(AtmStatus.InvalidAmount, Acc(250, 0), Full, r, spy);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_prime_sur_tous_les_autres_refus(int amount)
    {
        // Plafond atteint, solde nul, stock vide, cassette bloquée : tout échouerait.
        var (r, spy) = Run(Acc(0, 200), N(0, 0, 0), Denomination.Ten, W(amount));
        AssertRefused(AtmStatus.InvalidAmount, Acc(0, 200), N(0, 0, 0), r, spy);
    }

    // --- Contrôle 2 : plafond du jour ---

    [Theory]
    [InlineData(0, 200)]
    [InlineData(100, 100)]
    [InlineData(190, 10)]
    [InlineData(150, 50)]
    public void Atteindre_exactement_200_est_autorise(int withdrawn, int amount)
    {
        var (r, _) = Run(Acc(250, withdrawn), Full, null, W(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(200, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(200, 10)]
    [InlineData(150, 60)]
    [InlineData(100, 110)]
    public void Depasser_200_est_refuse(int withdrawn, int amount)
    {
        var (r, spy) = Run(Acc(250, withdrawn), Full, null, W(amount));
        AssertRefused(AtmStatus.DailyLimitExceeded, Acc(250, withdrawn), Full, r, spy);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 200 + 5 de frais : le plafond porte sur 200 seulement.
        var (r, _) = Run(Acc(250, 0), Full, null, W(200));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Acc(45, 200), r.Account);
        Assert.Equal(5, r.Fee);
    }

    [Fact]
    public void Plafond_verifie_avant_le_solde()
    {
        var (r, spy) = Run(Acc(0, 200), Full, null, W(10));
        AssertRefused(AtmStatus.DailyLimitExceeded, Acc(0, 200), Full, r, spy);
    }

    [Fact]
    public void Plafond_verifie_avant_les_billets()
    {
        var (r, spy) = Run(Acc(250, 150), N(0, 0, 0), null, W(60));
        AssertRefused(AtmStatus.DailyLimitExceeded, Acc(250, 150), N(0, 0, 0), r, spy);
    }

    // --- Contrôle 3 : solde + frais ---

    [Theory]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    [InlineData(105, 100)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    public void Solde_suffisant_frais_compris(int balance, int amount)
    {
        var (r, _) = Run(Acc(balance, 0), Full, null, W(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(balance - amount - Oracle.Fee(amount), r.Account.Balance);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(114, 110)]
    [InlineData(200, 200)]
    public void Solde_insuffisant(int balance, int amount)
    {
        var (r, spy) = Run(Acc(balance, 0), Full, null, W(amount));
        AssertRefused(AtmStatus.InsufficientFunds, Acc(balance, 0), Full, r, spy);
    }

    [Fact]
    public void Solde_verifie_avant_les_billets()
    {
        var (r, spy) = Run(Acc(30, 0), N(0, 0, 0), Denomination.Fifty, W(50));
        AssertRefused(AtmStatus.InsufficientFunds, Acc(30, 0), N(0, 0, 0), r, spy);
    }

    // --- Frais ---

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_au_dela_de_100_strictement(int amount, int fee)
    {
        var (r, _) = Run(Acc(250, 0), Full, null, W(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(Acc(250 - amount - fee, amount), r.Account);
    }

    // --- Contrôle 4 : billets ---

    [Fact]
    public void Stock_vide_ne_peut_rien_sortir()
    {
        var (r, spy) = Run(Acc(250, 0), N(0, 0, 0), null, W(10));
        AssertRefused(AtmStatus.CannotDispense, Acc(250, 0), N(0, 0, 0), r, spy);
    }

    [Fact]
    public void Aucune_combinaison_exacte()
    {
        // Que des 50 et des 20 : 30 est impossible.
        var (r, spy) = Run(Acc(250, 0), N(5, 5, 0), null, W(30));
        AssertRefused(AtmStatus.CannotDispense, Acc(250, 0), N(5, 5, 0), r, spy);
    }

    [Fact]
    public void Stock_insuffisant_en_valeur()
    {
        var (r, spy) = Run(Acc(250, 0), N(1, 1, 1), null, W(90));
        AssertRefused(AtmStatus.CannotDispense, Acc(250, 0), N(1, 1, 1), r, spy);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50)]
    [InlineData(Denomination.Twenty, 20)]
    [InlineData(Denomination.Ten, 10)]
    public void La_cassette_bloquee_compte_pour_zero(Denomination jammed, int amount)
    {
        var stock = jammed switch
        {
            Denomination.Fifty => N(5, 0, 0),
            Denomination.Twenty => N(0, 5, 0),
            _ => N(0, 0, 5),
        };
        var (r, spy) = Run(Acc(250, 0), stock, jammed, W(amount));
        AssertRefused(AtmStatus.CannotDispense, Acc(250, 0), stock, r, spy);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50, 0, 2, 1)]
    [InlineData(Denomination.Twenty, 40, 0, 0, 4)]
    [InlineData(Denomination.Ten, 60, 0, 3, 0)] // 50+10 impossible : 20×3
    public void La_cassette_bloquee_est_contournee(Denomination jammed, int amount, int f, int t, int n)
    {
        var expected = N(f, t, n);
        var (r, spy) = Run(Acc(250, 0), Full, jammed, W(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expected, r.Notes);
        Assert.Equal(0, r.Notes.Of(jammed));
        Assert.Equal(Full - expected, spy.Stock);
    }

    // --- Combinaison au moins de billets ---

    [Theory]
    [InlineData(5, 5, 5, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 70, 1, 1, 0)]
    [InlineData(5, 5, 5, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 140, 2, 2, 0)]
    [InlineData(5, 5, 5, 200, 4, 0, 0)]
    [InlineData(1, 5, 5, 60, 1, 0, 1)]
    [InlineData(1, 5, 0, 60, 0, 3, 0)]   // pas de 10 : 50+10 impossible
    [InlineData(1, 5, 5, 110, 1, 3, 0)]  // un seul 50
    [InlineData(0, 5, 5, 100, 0, 5, 0)]
    [InlineData(0, 0, 5, 50, 0, 0, 5)]
    [InlineData(3, 0, 5, 180, 3, 0, 3)]
    [InlineData(2, 4, 0, 80, 0, 4, 0)]   // 50+? impossible sans 10 → 20×4
    public void Choisit_la_combinaison_au_moins_de_billets(
        int sf, int st, int sn, int amount, int f, int t, int n)
    {
        var (r, spy) = Run(Acc(250, 0), N(sf, st, sn), null, W(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, n), r.Notes);
        Assert.Equal(N(sf, st, sn) - N(f, t, n), spy.Stock);
    }

    // --- Sortie ---

    [Theory]
    [InlineData(200)] // 50×4 : un passage plein
    [InlineData(190)] // 50×3 20×2 : 5 billets
    [InlineData(180)] // 50×3 10×3 : 6 billets
    [InlineData(100)]
    public void Sortie_en_passages_de_4_billets_au_plus(int amount)
    {
        var (r, spy) = Run(Acc(250, 0), Full, null, W(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.NotEmpty(spy.Dispensed);
        Assert.All(spy.Dispensed, d =>
        {
            Assert.True(d.Ok);
            Assert.InRange(d.Notes.Total, 1, 4);
        });
        Assert.Equal(r.Notes, spy.TotalDispensed);
        Assert.Empty(spy.Loaded);
    }

    [Theory]
    [InlineData(0, 1, 4, 60, 0, 1, 4)]  // 5 billets
    [InlineData(0, 5, 5, 90, 0, 4, 1)]  // 5 billets
    [InlineData(5, 5, 5, 180, 3, 0, 3)] // 6 billets
    [InlineData(1, 4, 5, 180, 1, 3, 3)] // 7 billets
    [InlineData(0, 5, 5, 200, 0, 5, 5)] // 10 billets : au moins 3 passages
    public void Retrait_de_plus_de_4_billets_avec_des_10(int sf, int st, int sn, int amount, int f, int t, int n)
    {
        var (r, spy) = Run(Acc(250, 0), N(sf, st, sn), null, W(amount));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(f, t, n), Oracle.Fee(amount), Acc(250 - amount - Oracle.Fee(amount), amount)), r);
        Assert.All(spy.Dispensed, d => Assert.InRange(d.Notes.Total, 1, 4));
        Assert.True(spy.Dispensed.Count >= (N(f, t, n).Total + 3) / 4);
        Assert.Equal(N(sf, st, sn) - N(f, t, n), spy.Stock);
    }

    [Fact]
    public void Retrait_de_10_billets_de_10_sur_plusieurs_passages()
    {
        // 0×50, 0×20 : 50 € = 10×5, donc au moins deux passages.
        var (r, spy) = Run(Acc(250, 0), N(0, 0, 5), null, W(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 0, 5), 0, Acc(200, 50)), r);
        Assert.True(spy.Dispensed.Count >= 2);
        Assert.All(spy.Dispensed, d => Assert.InRange(d.Notes.Total, 1, 4));
        Assert.Equal(N(0, 0, 0), spy.Stock);
    }

    [Fact]
    public void Retrait_reussi_ne_change_pas_le_solde_au_dela_de_montant_plus_frais()
    {
        var (r, _) = Run(Acc(250, 40), Full, null, W(150));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(3, 0, 0), 5, Acc(95, 190)), r);
    }
}
