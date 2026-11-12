using static Atm.Tests.T;
using static Atm.Tests.SpecExamplesTests;

namespace Atm.Tests;

public class WithdrawTests
{
    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (r, d) = Run(Acc(250, 0), Full, null, Withdraw(amount));

        AssertRefused(r, AtmStatus.InvalidAmount, Acc(250, 0));
        Assert.Empty(d.Dispensed);
        Assert.Equal(Full, d.Stock);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, stock vide : tout échouerait, mais le montant est contrôlé en premier.
        var (r, _) = Run(Acc(0, 200), N(0, 0, 0), Denomination.Ten, Withdraw(amount));

        AssertRefused(r, AtmStatus.InvalidAmount, Acc(0, 200));
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_le_plafond_est_permis(int withdrawn, int amount)
    {
        var (r, _) = Run(Acc(250, withdrawn), Full, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(withdrawn + amount, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    public void Depasser_le_plafond_est_refuse(int withdrawn, int amount)
    {
        var (r, d) = Run(Acc(250, withdrawn), Full, null, Withdraw(amount));

        AssertRefused(r, AtmStatus.DailyLimitExceeded, Acc(250, withdrawn));
        Assert.Empty(d.Dispensed);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : pile au plafond, les 5 € de frais n'y comptent pas.
        var (r, _) = Run(Acc(250, 90), Full, null, Withdraw(110));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(135, 200), r.Account);
    }

    [Fact]
    public void Plafond_controle_avant_le_solde_et_les_billets()
    {
        var (r, _) = Run(Acc(0, 200), N(0, 0, 0), null, Withdraw(10));

        AssertRefused(r, AtmStatus.DailyLimitExceeded, Acc(0, 200));
    }

    // ---- Contrôle 3 : solde ----

    [Theory]
    [InlineData(100, 100)] // pas de frais : solde exact suffisant
    [InlineData(115, 110)] // frais : solde exact = montant + 5
    [InlineData(205, 200)]
    public void Solde_egal_a_montant_plus_frais_suffit(int balance, int amount)
    {
        var (r, _) = Run(Acc(balance, 0), Full, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Theory]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(200, 200)]
    [InlineData(0, 10)]
    public void Solde_inferieur_a_montant_plus_frais_est_refuse(int balance, int amount)
    {
        var (r, d) = Run(Acc(balance, 0), Full, null, Withdraw(amount));

        AssertRefused(r, AtmStatus.InsufficientFunds, Acc(balance, 0));
        Assert.Empty(d.Dispensed);
    }

    [Fact]
    public void Solde_controle_avant_les_billets()
    {
        var (r, _) = Run(Acc(0, 0), N(0, 0, 0), null, Withdraw(10));

        AssertRefused(r, AtmStatus.InsufficientFunds, Acc(0, 0));
    }

    // ---- Frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var (r, _) = Run(Acc(250, 0), Full, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedFee, r.Fee);
        Assert.Equal(250 - amount - expectedFee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    // ---- Contrôle 4 : billets ----

    [Fact]
    public void Stock_vide_ne_peut_rien_distribuer()
    {
        var (r, _) = Run(Acc(250, 0), N(0, 0, 0), null, Withdraw(10));

        AssertRefused(r, AtmStatus.CannotDispense, Acc(250, 0));
    }

    [Fact]
    public void Aucune_combinaison_exacte()
    {
        // Seulement des 50 et des 20 : 30 est impossible.
        var (r, d) = Run(Acc(250, 0), N(5, 5, 0), null, Withdraw(30));

        AssertRefused(r, AtmStatus.CannotDispense, Acc(250, 0));
        Assert.Empty(d.Dispensed);
        Assert.Equal(N(5, 5, 0), d.Stock);
    }

    [Fact]
    public void Stock_insuffisant_en_valeur()
    {
        var (r, _) = Run(Acc(250, 0), N(1, 1, 1), null, Withdraw(90));

        AssertRefused(r, AtmStatus.CannotDispense, Acc(250, 0));
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
        var (r, d) = Run(Acc(250, 0), stock, jammed, Withdraw(amount));

        AssertRefused(r, AtmStatus.CannotDispense, Acc(250, 0));
        Assert.Empty(d.Dispensed);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 100, 0, 5, 0)]
    [InlineData(Denomination.Twenty, 60, 1, 0, 1)]
    [InlineData(Denomination.Ten, 70, 1, 1, 0)]
    [InlineData(Denomination.Ten, 30, 0, 0, 0)] // 30 impossible sans les 10
    public void La_combinaison_evite_la_cassette_bloquee(Denomination jammed, int amount, int f, int t, int c)
    {
        var (r, d) = Run(Acc(250, 0), Full, jammed, Withdraw(amount));

        if (f + t + c == 0)
        {
            AssertRefused(r, AtmStatus.CannotDispense, Acc(250, 0));
            return;
        }

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, c), r.Notes);
        Assert.Equal(0, r.Notes.Of(jammed));
        Assert.All(d.Dispensed, pass => Assert.Equal(0, pass.Of(jammed)));
    }

    // ---- La combinaison : le moins de billets ----

    [Theory]
    [InlineData(10, 0, 0, 1)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(30, 0, 1, 1)]
    [InlineData(40, 0, 2, 0)]
    [InlineData(50, 1, 0, 0)]
    [InlineData(60, 1, 0, 1)]
    [InlineData(70, 1, 1, 0)]
    [InlineData(80, 1, 1, 1)]
    [InlineData(90, 1, 2, 0)]
    [InlineData(100, 2, 0, 0)]
    [InlineData(130, 2, 1, 1)]
    [InlineData(190, 3, 2, 0)]
    [InlineData(200, 4, 0, 0)]
    public void Avec_un_stock_plein_la_combinaison_compte_le_moins_de_billets(int amount, int f, int t, int c)
    {
        var (r, d) = Run(Acc(250, 0), Full, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, c), r.Notes);
        Assert.Equal(Full - N(f, t, c), d.Stock);
    }

    [Theory]
    [InlineData(60, 1, 5, 0, 0, 3, 0)]   // pas de 10 : 20×3 plutôt que 50+10
    [InlineData(80, 5, 5, 0, 0, 4, 0)]   // pas de 10 : 20×4
    [InlineData(100, 1, 5, 5, 1, 2, 1)]  // un seul 50 : 50+20+20+10
    [InlineData(100, 0, 5, 5, 0, 5, 0)]  // aucun 50 : 20×5
    [InlineData(70, 5, 0, 5, 1, 0, 2)]   // pas de 20 : 50+10+10
    [InlineData(40, 5, 1, 5, 0, 1, 2)]   // un seul 20 : 20+10+10
    public void Avec_un_stock_partiel_la_combinaison_compte_le_moins_de_billets(
        int amount, int sf, int st, int sc, int f, int t, int c)
    {
        var (r, d) = Run(Acc(250, 0), N(sf, st, sc), null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, c), r.Notes);
        Assert.Equal(N(sf, st, sc) - N(f, t, c), d.Stock);
    }

    // ---- La sortie : passages de 4 billets au plus ----

    [Theory]
    [InlineData(5, 5, 5, 200)]  // 50×4
    [InlineData(0, 5, 5, 100)]  // 20×5
    [InlineData(0, 5, 5, 110)]  // 20×5 10×1
    [InlineData(0, 4, 5, 90)]   // 20×4 10×1
    [InlineData(1, 5, 5, 120)]  // 50 20×3 10
    [InlineData(3, 2, 1, 200)]  // 50×3 20×2 10
    [InlineData(1, 5, 5, 150)]  // 50 20×5
    [InlineData(0, 5, 5, 120)]  // 20×5 10×2
    public void Les_billets_sortent_en_passages_de_4_billets_au_plus(int sf, int st, int sc, int amount)
    {
        var stock = N(sf, st, sc);
        var expected = Oracle.FewestNotes(amount, stock)!.Value;
        var (r, d) = Run(Acc(250, 0), stock, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expected, r.Notes);
        Assert.All(d.Dispensed, pass =>
        {
            Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass);
        });
        Assert.All(d.DispenseResults, Assert.True);
        Assert.Equal(expected, d.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(stock - expected, d.Stock);
    }

    [Fact]
    public void Un_retrait_reussi_fait_sortir_les_billets_par_le_module_et_rien_d_autre()
    {
        var (r, d) = Run(Acc(250, 0), Full, null, Withdraw(180));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(3, 1, 1), r.Notes);
        Assert.Equal(r.Notes, d.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Empty(d.Loaded);
        Assert.Equal(N(2, 4, 4), d.Stock);
        Assert.Equal(Acc(65, 180), r.Account);
    }
}
