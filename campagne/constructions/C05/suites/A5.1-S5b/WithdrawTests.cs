using static Atm.Tests.S;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (m, d) = Real(Full);
        var r = m.Execute(Acc(250, 0), W(amount));
        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Equal(Full, d.Stock);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, aucun billet : le montant reste le premier contrôle.
        var (m, _) = Real(N(0, 0, 0));
        var r = m.Execute(Acc(0, 200), W(amount));
        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 200)]
    [InlineData(100, 100)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_200_est_permis(int withdrawnToday, int amount)
    {
        var (m, _) = Real(Full);
        var r = m.Execute(Acc(250, withdrawnToday), W(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(200, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(110, 100)]
    [InlineData(200, 10)]
    [InlineData(150, 60)]
    public void Depasser_200_est_refuse(int withdrawnToday, int amount)
    {
        var (m, d) = Real(Full);
        var r = m.Execute(Acc(250, withdrawnToday), W(amount));
        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, Acc(250, withdrawnToday)), r);
        Assert.Equal(Full, d.Stock);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : permis, même si 90 + 110 + 5 dépasse 200.
        var (m, _) = Real(Full);
        var r = m.Execute(Acc(250, 90), W(110));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, Acc(135, 200)), r);
    }

    [Fact]
    public void Plafond_passe_avant_le_solde()
    {
        var (m, _) = Real(Full);
        Assert.Equal(AtmStatus.DailyLimitExceeded, m.Execute(Acc(0, 150), W(60)).Status);
    }

    [Fact]
    public void Plafond_passe_avant_la_distribution()
    {
        var (m, _) = Real(N(0, 0, 0));
        Assert.Equal(AtmStatus.DailyLimitExceeded, m.Execute(Acc(250, 200), W(10)).Status);
    }

    // ---- Frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int fee)
    {
        var (m, _) = Real(Full);
        var r = m.Execute(Acc(250, 0), W(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(250 - amount - fee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(10, 100)]
    [InlineData(100, 100)]
    [InlineData(60, 50)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Les_frais_dependent_du_montant_seul_pas_du_cumul_du_jour(int withdrawnToday, int amount)
    {
        // amount <= 100 : pas de frais, même si WithdrawnToday + amount dépasse 100.
        var (m, _) = Real(Full);
        var r = m.Execute(Acc(250, withdrawnToday), W(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250 - amount, withdrawnToday + amount), r.Account);
    }

    [Fact]
    public void Retrait_de_100_avec_solde_100_apres_un_premier_retrait()
    {
        var (m, _) = Real(Full);
        var r = m.Execute(Acc(100, 10), W(100));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(0, 110)), r);
    }

    [Fact]
    public void Sans_frais_un_solde_egal_au_montant_mene_a_la_distribution()
    {
        // Solde suffisant (pas de frais à 100) : c'est le contrôle 4 qui refuse.
        var (m, _) = Real(N(0, 0, 0));
        Assert.Equal(AtmStatus.CannotDispense, m.Execute(Acc(100, 10), W(100)).Status);
    }

    // ---- Contrôle 3 : solde ----

    [Theory]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    public void Solde_exactement_suffisant_frais_compris(int balance, int amount)
    {
        var (m, _) = Real(Full);
        var r = m.Execute(Acc(balance, 0), W(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(200, 200)]
    public void Solde_insuffisant_frais_compris(int balance, int amount)
    {
        var (m, d) = Real(Full);
        var r = m.Execute(Acc(balance, 0), W(amount));
        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, Acc(balance, 0)), r);
        Assert.Equal(Full, d.Stock);
    }

    [Fact]
    public void Solde_passe_avant_la_distribution()
    {
        var (m, _) = Real(N(0, 0, 0));
        Assert.Equal(AtmStatus.InsufficientFunds, m.Execute(Acc(0, 0), W(10)).Status);
    }

    // ---- Contrôle 4 : combinaison au moins de billets ----

    public static TheoryData<NoteCounts, int?, int, NoteCounts> FewestNotesCases => new()
    {
        // stock, cassette bloquée, montant, combinaison attendue
        { N(5, 5, 5), null, 10, N(0, 0, 1) },
        { N(5, 5, 5), null, 30, N(0, 1, 1) },
        { N(5, 5, 5), null, 60, N(1, 0, 1) },
        { N(5, 5, 5), null, 70, N(1, 1, 0) },
        { N(5, 5, 5), null, 90, N(1, 2, 0) },
        { N(5, 5, 5), null, 130, N(2, 1, 1) },
        { N(5, 5, 5), null, 200, N(4, 0, 0) },
        { N(5, 5, 5), null, 190, N(3, 2, 0) },
        // l'approche gloutonne échoue : il faut renoncer à un 50
        { N(1, 3, 0), null, 60, N(0, 3, 0) },
        { N(2, 3, 0), null, 110, N(1, 3, 0) },
        { N(3, 5, 0), null, 160, N(2, 3, 0) },
        { N(1, 4, 0), null, 80, N(0, 4, 0) },
        // le stock limite la combinaison
        { N(1, 4, 1), null, 80, N(1, 1, 1) },
        { N(1, 2, 1), null, 100, N(1, 2, 1) },
        { N(0, 5, 5), null, 90, N(0, 4, 1) },
        { N(0, 0, 5), null, 50, N(0, 0, 5) },
        { N(1, 5, 0), null, 150, N(1, 5, 0) },
        // la cassette bloquée compte pour zéro
        { N(5, 5, 5), 10, 60, N(0, 3, 0) },
        { N(5, 5, 5), 50, 60, N(0, 3, 0) },
        { N(5, 5, 5), 50, 100, N(0, 5, 0) },
        { N(5, 5, 5), 20, 40, N(0, 0, 4) },
        { N(5, 5, 5), 20, 70, N(1, 0, 2) },
        { N(2, 2, 0), 50, 40, N(0, 2, 0) },
    };

    [Theory]
    [MemberData(nameof(FewestNotesCases))]
    public void Sort_la_combinaison_exacte_au_moins_de_billets(NoteCounts stock, int? jammed, int amount, NoteCounts expected)
    {
        var (m, d) = Real(stock, Jam(jammed));
        var r = m.Execute(Acc(250, 0), W(amount));
        var fee = amount > 100 ? 5 : 0;
        Assert.Equal(new AtmResult(AtmStatus.Ok, expected, fee, Acc(250 - amount - fee, amount)), r);
        Assert.Equal(stock - expected, d.Stock);
    }

    public static TheoryData<NoteCounts, int?, int> CannotDispenseCases => new()
    {
        { N(0, 0, 0), null, 10 },
        { N(1, 2, 0), null, 100 },  // 90 € au plus
        { N(5, 5, 0), null, 10 },   // pas de 10
        { N(5, 5, 0), null, 30 },
        { N(1, 0, 0), null, 40 },
        { N(5, 0, 0), null, 120 },
        { N(5, 5, 5), 10, 30 },     // seul un 10 ferait l'appoint
        { N(5, 5, 5), 10, 10 },
        { N(1, 0, 0), 50, 50 },
        { N(5, 5, 0), 20, 60 },
        { N(0, 5, 5), 20, 80 },     // 10×5 au plus = 50
    };

    [Theory]
    [MemberData(nameof(CannotDispenseCases))]
    public void Aucune_combinaison_possible(NoteCounts stock, int? jammed, int amount)
    {
        var (m, d) = Real(stock, Jam(jammed));
        var r = m.Execute(Acc(250, 0), W(amount));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Equal(stock, d.Stock);
    }

    // ---- La sortie : passages ----

    [Theory]
    [InlineData(5, 5, 5, 10)]
    [InlineData(5, 5, 5, 200)]  // 4 billets
    [InlineData(5, 5, 5, 190)]  // 5 billets
    [InlineData(1, 5, 0, 150)]  // 6 billets
    [InlineData(0, 5, 5, 140)]  // 20×5 10×4 : 9 billets
    [InlineData(0, 0, 5, 50)]   // 10×5
    public void Sortie_en_passages_de_1_a_4_billets_dont_la_somme_est_la_combinaison(int f, int t, int d10, int amount)
    {
        var recorder = new RecordingDispenser(new MoneyDispenser(N(f, t, d10)));
        var m = new CashMachine(recorder);

        var r = m.Execute(Acc(250, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.NotEmpty(recorder.Dispensed);
        Assert.All(recorder.Dispensed, pass =>
        {
            Assert.InRange(pass.Total, 1, 4);
            Assert.True(pass.Fifties >= 0 && pass.Twenties >= 0 && pass.Tens >= 0);
        });
        Assert.Equal(r.Notes, recorder.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(amount, r.Notes.Value);
        Assert.Empty(recorder.Loaded);
        Assert.Equal(N(f, t, d10) - r.Notes, recorder.Stock);
    }

    [Fact]
    public void Neuf_billets_sortent_et_le_stock_le_reflete()
    {
        var (m, d) = Real(N(0, 5, 5));
        var r = m.Execute(Acc(250, 0), W(140));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 5, 4), 5, Acc(105, 140)), r);
        Assert.Equal(N(0, 0, 1), d.Stock);
    }
}
