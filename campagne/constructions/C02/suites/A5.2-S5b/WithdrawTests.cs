using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    // ---------- Contrôle 1 : montant invalide ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (m, spy) = Build(Full);
        var r = m.Execute(Acc(250, 0), W(amount));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.False(spy.AnyMove);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, aucun billet : tout le reste échouerait aussi.
        var (m, _) = Build(N(0, 0, 0), Denomination.Ten);
        var r = m.Execute(Acc(0, 200), W(amount));

        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    // ---------- Contrôle 2 : plafond du jour ----------

    [Theory]
    [InlineData(0, 200)]
    [InlineData(100, 100)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_200_est_permis(int already, int amount)
    {
        var (m, _) = Build(Full);
        var r = m.Execute(Acc(250, already), W(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(already + amount, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(100, 110)]
    public void Depasser_200_est_refuse(int already, int amount)
    {
        var (m, spy) = Build(Full);
        var r = m.Execute(Acc(250, already), W(amount));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, Acc(250, already)), r);
        Assert.False(spy.AnyMove);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : permis, même si 90 + 110 + 5 = 205.
        var (m, _) = Build(Full);
        var r = m.Execute(Acc(250, 90), W(110));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(135, 200), r.Account);
    }

    [Fact]
    public void Le_plafond_passe_avant_le_solde()
    {
        var (m, _) = Build(Full);
        var r = m.Execute(Acc(0, 200), W(10));

        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    [Fact]
    public void Le_plafond_passe_avant_la_distribution()
    {
        var (m, _) = Build(N(0, 0, 0));
        var r = m.Execute(Acc(250, 150), W(60));

        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    // ---------- Frais ----------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int fee)
    {
        var (m, _) = Build(Full);
        var r = m.Execute(Acc(250, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(250 - amount - fee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    // ---------- Contrôle 3 : solde ----------

    [Theory]
    [InlineData(100, 100)] // sans frais, solde juste suffisant
    [InlineData(115, 110)] // avec frais, solde juste suffisant
    [InlineData(205, 200)]
    public void Solde_egal_a_montant_plus_frais_est_suffisant(int balance, int amount)
    {
        var (m, _) = Build(Full);
        var r = m.Execute(Acc(balance, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Theory]
    [InlineData(95, 100)]
    [InlineData(110, 110)] // couvre le montant, pas les frais
    [InlineData(200, 200)]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    public void Solde_inferieur_a_montant_plus_frais_est_refuse(int balance, int amount)
    {
        var (m, spy) = Build(Full);
        var r = m.Execute(Acc(balance, 0), W(amount));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, Acc(balance, 0)), r);
        Assert.False(spy.AnyMove);
    }

    [Fact]
    public void Le_solde_passe_avant_la_distribution()
    {
        var (m, _) = Build(N(0, 0, 0));
        var r = m.Execute(Acc(50, 0), W(60));

        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    // ---------- Contrôle 4 : distribution ----------

    [Theory]
    [InlineData(0, 0, 0, null, 10)]
    [InlineData(1, 0, 0, null, 60)]   // 50 seul ne fait pas 60
    [InlineData(5, 5, 0, null, 30)]   // 30 sans 10 : impossible
    [InlineData(0, 5, 0, null, 50)]   // 50 avec seulement des 20 : impossible
    [InlineData(0, 2, 0, null, 60)]   // pas assez de 20
    [InlineData(5, 5, 5, Denomination.Ten, 30)] // 30 sans 10 : impossible
    [InlineData(5, 5, 0, Denomination.Twenty, 60)] // seuls les 50 restent : 60 impossible
    public void Aucune_combinaison_exacte_donne_CannotDispense(int f, int t, int n, Denomination? jam, int amount)
    {
        var stock = N(f, t, n);
        var (m, spy) = Build(stock, jam);
        var r = m.Execute(Acc(250, 0), W(amount));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Equal(stock, spy.Stock);
        Assert.False(spy.AnyMove);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50)]
    [InlineData(Denomination.Twenty, 20)]
    [InlineData(Denomination.Ten, 10)]
    public void La_cassette_bloquee_compte_pour_zero(Denomination jam, int amount)
    {
        // Seule la cassette bloquée pourrait servir.
        var stock = jam switch
        {
            Denomination.Fifty => N(5, 0, 0),
            Denomination.Twenty => N(0, 5, 0),
            _ => N(0, 0, 5),
        };
        var (m, spy) = Build(stock, jam);
        var r = m.Execute(Acc(250, 0), W(amount));

        Assert.Equal(AtmStatus.CannotDispense, r.Status);
        Assert.Equal(stock, spy.Stock);
    }

    [Theory]
    // stock (50,20,10), cassette bloquée, montant -> combinaison attendue
    [InlineData(5, 5, 5, null, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, null, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, null, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, null, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, null, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, null, 200, 4, 0, 0)]
    [InlineData(1, 3, 0, null, 60, 0, 3, 0)]      // le glouton échoue (50 puis reste 10)
    [InlineData(1, 4, 0, null, 80, 0, 4, 0)]      // le glouton échoue (50 puis reste 30)
    [InlineData(2, 3, 0, null, 110, 1, 3, 0)]     // le glouton échoue (50+50 puis reste 10)
    [InlineData(3, 5, 0, null, 130, 1, 4, 0)]     // 50×2 + reste 30 impossible
    [InlineData(5, 5, 5, Denomination.Fifty, 100, 0, 5, 0)]
    [InlineData(5, 5, 5, Denomination.Fifty, 110, 0, 5, 1)]
    [InlineData(5, 5, 5, Denomination.Twenty, 80, 1, 0, 3)]
    [InlineData(5, 5, 5, Denomination.Ten, 70, 1, 1, 0)]
    [InlineData(1, 0, 5, null, 50, 1, 0, 0)]      // 50×1 plutôt que 10×5
    [InlineData(0, 5, 5, null, 60, 0, 3, 0)]      // 20×3 plutôt que 20×2 + 10×2
    [InlineData(5, 0, 5, Denomination.Fifty, 50, 0, 0, 5)]
    public void La_combinaison_est_celle_qui_compte_le_moins_de_billets(
        int f, int t, int n, Denomination? jam, int amount, int ef, int et, int en)
    {
        var stock = N(f, t, n);
        var (m, spy) = Build(stock, jam);
        var r = m.Execute(Acc(250, 0), W(amount));

        var expected = N(ef, et, en);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expected, r.Notes);
        Assert.Equal(stock - expected, spy.Stock);
        Assert.Equal(expected, spy.Dispensed);
    }

    // ---------- Sortie des billets ----------

    [Theory]
    [InlineData(190, 5)]  // 50×3 + 20×2
    [InlineData(180, 5)]  // 50×3 + 20×1 + 10×1
    [InlineData(200, 4)]
    [InlineData(80, 3)]
    public void Les_billets_sortent_par_passages_de_4_au_plus(int amount, int expectedNotes)
    {
        var (m, spy) = Build(Full);
        var r = m.Execute(Acc(250, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedNotes, r.Notes.Total);
        Assert.NotEmpty(spy.DispenseCalls);
        Assert.All(spy.DispenseCalls, c =>
        {
            Assert.True(c.Ok);
            Assert.InRange(c.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass);
        });
        Assert.Equal(r.Notes, spy.Dispensed);
        Assert.Empty(spy.LoadCalls);
    }

    [Fact]
    public void Retrait_de_9_billets_avec_cassette_de_50_bloquee()
    {
        // 140 sans 50, au plus 5 billets par cassette : seule 20×5 + 10×4 convient, 9 billets, 3 passages au moins.
        var (m, spy) = Build(Full, Denomination.Fifty);
        var r = m.Execute(Acc(250, 0), W(140));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 5, 4), r.Notes);
        Assert.Equal(N(5, 0, 1), spy.Stock);
        Assert.All(spy.DispenseCalls, c => Assert.InRange(c.Notes.Total, 1, 4));
        Assert.True(spy.DispenseCalls.Count >= 3);
    }

    [Fact]
    public void Retrait_reussi_vide_la_cassette_jusqu_a_zero()
    {
        var (m, spy) = Build(N(0, 0, 5));
        var r = m.Execute(Acc(250, 0), W(50));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 0, 5), 0, Acc(200, 50)), r);
        Assert.Equal(N(0, 0, 0), spy.Stock);
    }
}
