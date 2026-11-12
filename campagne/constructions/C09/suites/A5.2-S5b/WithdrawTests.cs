using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(-5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (r, _) = Run(Acc(250, 0), Full, null, Withdraw(amount));

        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(190)]
    public void Multiple_de_10_positif_est_accepte(int amount)
    {
        var (r, _) = Run(Acc(250, 0), Full, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
    }

    [Fact]
    public void Montant_invalide_passe_avant_le_plafond_les_fonds_et_les_billets()
    {
        // Plafond atteint, solde nul, aucun billet : c'est quand même le montant qui est refusé.
        var (r, _) = Run(Acc(0, 200), N(0, 0, 0), Denomination.Fifty, Withdraw(-10));

        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    [InlineData(100, 100)]
    [InlineData(0, 200)]   // avec frais : voir aussi Les_frais_ne_comptent_pas_dans_le_plafond
    public void Atteindre_exactement_200_est_permis(int withdrawnToday, int amount)
    {
        var (r, _) = Run(Acc(250, withdrawnToday), Full, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(200, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(200, 10)]
    [InlineData(150, 60)]
    public void Depasser_200_est_refuse(int withdrawnToday, int amount)
    {
        var (r, _) = Run(Acc(250, withdrawnToday), Full, null, Withdraw(amount));

        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    [Theory]
    [InlineData(90, 110)]
    [InlineData(50, 150)]
    [InlineData(10, 190)]
    [InlineData(0, 200)]
    public void Les_frais_ne_comptent_pas_dans_le_plafond(int withdrawnToday, int amount)
    {
        // déjà retiré + montant = 200 : pile au plafond, les 5 € de frais n'y entrent pas.
        var (r, _) = Run(Acc(250, withdrawnToday), Full, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(250 - amount - 5, 200), r.Account);
    }

    [Fact]
    public void Plafond_passe_avant_les_fonds_et_les_billets()
    {
        var (r, _) = Run(Acc(0, 200), N(0, 0, 0), null, Withdraw(10));

        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    // ---- Contrôle 3 : fonds insuffisants et frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(190, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var (r, _) = Run(Acc(250, 0), Full, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedFee, r.Fee);
        Assert.Equal(250 - amount - expectedFee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(10, 10)]
    [InlineData(115, 110)]
    [InlineData(195, 190)]
    public void Solde_egal_a_montant_plus_frais_suffit(int balance, int amount)
    {
        var (r, _) = Run(Acc(balance, 0), Full, null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Theory]
    [InlineData(95, 100)]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    [InlineData(110, 110)]
    [InlineData(190, 190)]
    public void Solde_inferieur_a_montant_plus_frais_est_refuse(int balance, int amount)
    {
        var (r, _) = Run(Acc(balance, 0), Full, null, Withdraw(amount));

        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    [Fact]
    public void Fonds_passent_avant_les_billets()
    {
        var (r, _) = Run(Acc(0, 0), N(0, 0, 0), null, Withdraw(10));

        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    // ---- Contrôle 4 : combinaison impossible ----

    [Theory]
    [InlineData(0, 0, 0, 10)]   // machine vide
    [InlineData(5, 5, 0, 10)]   // pas de 10
    [InlineData(5, 0, 0, 30)]   // que des 50
    [InlineData(0, 5, 0, 50)]   // que des 20, montant impair en dizaines
    [InlineData(1, 1, 1, 90)]   // pas assez de billets
    [InlineData(2, 0, 0, 150)]  // stock insuffisant
    public void Aucune_combinaison_exacte(int fifties, int twenties, int tens, int amount)
    {
        var (r, _) = Run(Acc(250, 0), N(fifties, twenties, tens), null, Withdraw(amount));

        Assert.Equal(AtmStatus.CannotDispense, r.Status);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 5, 5, 50, 0, 2, 1)]
    [InlineData(Denomination.Twenty, 5, 0, 5, 40, 0, 0, 4)]
    [InlineData(Denomination.Ten, 5, 5, 0, 60, 0, 3, 0)]
    public void La_cassette_bloquee_compte_pour_zero(Denomination jammed, int f, int t, int n, int amount,
        int ef, int et, int en)
    {
        // La cassette bloquée est pleine (5 billets) et serait la plus intéressante : elle est ignorée.
        var stock = jammed switch
        {
            Denomination.Fifty => N(5, t, n),
            Denomination.Twenty => N(f, 5, n),
            _ => N(f, t, 5),
        };

        var (r, d) = Run(Acc(250, 0), stock, jammed, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(ef, et, en), r.Notes);
        Assert.Equal(0, r.Notes.Of(jammed));
        Assert.Equal(stock.Of(jammed), d.Stock.Of(jammed));
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50)]
    [InlineData(Denomination.Twenty, 20)]
    [InlineData(Denomination.Ten, 10)]
    public void Billets_seulement_dans_la_cassette_bloquee_donne_CannotDispense(Denomination jammed, int amount)
    {
        var stock = jammed switch
        {
            Denomination.Fifty => N(5, 0, 0),
            Denomination.Twenty => N(0, 5, 0),
            _ => N(0, 0, 5),
        };

        var (r, _) = Run(Acc(250, 0), stock, jammed, Withdraw(amount));

        Assert.Equal(AtmStatus.CannotDispense, r.Status);
    }

    // ---- La combinaison : le moins de billets ----

    [Theory]
    [InlineData(5, 5, 5, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 20, 0, 1, 0)]
    [InlineData(5, 5, 5, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, 160, 3, 0, 1)]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // le glouton (50 d'abord) échoue
    [InlineData(1, 4, 0, 80, 0, 4, 0)]   // idem
    [InlineData(3, 4, 0, 160, 2, 3, 0)]  // 50×3 laisserait 10, impossible
    [InlineData(0, 5, 5, 50, 0, 2, 1)]   // pas de 50
    [InlineData(0, 0, 5, 50, 0, 0, 5)]   // que des 10
    [InlineData(1, 0, 5, 60, 1, 0, 1)]   // 50+10 plutôt que 10×5+...
    [InlineData(5, 1, 5, 70, 1, 1, 0)]
    [InlineData(5, 0, 5, 70, 1, 0, 2)]   // pas de 20
    [InlineData(2, 5, 5, 190, 2, 4, 1)]
    [InlineData(1, 5, 5, 190, 1, 5, 4)]  // 10 billets, plusieurs passages
    [InlineData(0, 5, 5, 150, 0, 5, 5)]
    public void Choisit_la_combinaison_qui_compte_le_moins_de_billets(int f, int t, int n, int amount,
        int ef, int et, int en)
    {
        var (r, d) = Run(Acc(250, 0), N(f, t, n), null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(ef, et, en), r.Notes);
        Assert.Equal(N(f, t, n) - N(ef, et, en), d.Stock);
    }

    // ---- La sortie : passages de 4 billets au plus ----

    [Theory]
    [InlineData(5, 5, 5, 160)]  // 4 billets : un passage suffit
    [InlineData(1, 5, 5, 190)]  // 10 billets
    [InlineData(0, 5, 5, 150)]  // 10 billets
    [InlineData(0, 0, 5, 50)]   // 5 billets
    [InlineData(3, 1, 1, 180)]  // 5 billets, trois coupures
    public void Les_billets_sortent_par_passages_de_4_au_plus(int f, int t, int n, int amount)
    {
        var (r, d) = Run(Acc(250, 0), N(f, t, n), null, Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.NotEmpty(d.DispenseCalls);
        Assert.All(d.DispenseCalls, c =>
        {
            Assert.True(c.Ok, $"passage refusé par le module : {c.Notes}");
            Assert.InRange(c.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass);
        });
        Assert.Equal(r.Notes, d.Dispensed);
        Assert.Empty(d.LoadCalls);
    }

    [Fact]
    public void Un_retrait_de_10_billets_demande_au_moins_3_passages()
    {
        var (r, d) = Run(Acc(250, 0), N(0, 5, 5), null, Withdraw(150));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 5, 5), r.Notes);
        Assert.True(d.DispenseCalls.Count >= 3);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    // ---- Le succès ----

    [Fact]
    public void Succes_met_a_jour_compte_et_stock()
    {
        var (r, d) = Run(Acc(250, 40), N(5, 5, 5), Denomination.Ten, Withdraw(130));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 4, 0), r.Notes); // 50×2 20×1 10×1 est exclu : les 10 sont bloqués
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(115, 170), r.Account);
        Assert.Equal(N(4, 1, 5), d.Stock);
    }
}
