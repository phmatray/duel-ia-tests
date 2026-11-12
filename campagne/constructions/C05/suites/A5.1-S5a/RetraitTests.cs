using static Atm.Tests.Outils;

namespace Atm.Tests;

public class RetraitTests
{
    // ---------- Contrôle 1 : montant invalide ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (r, d) = Executer(Compte(250, 0), Plein, null, Retrait(amount));

        AssertRefus(AtmStatus.InvalidAmount, r, Compte(250, 0), Plein, d);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, stock vide : tout échouerait, mais le contrôle 1 gagne.
        var (r, d) = Executer(Compte(0, 200), N(0, 0, 0), Denomination.Ten, Retrait(amount));

        AssertRefus(AtmStatus.InvalidAmount, r, Compte(0, 200), N(0, 0, 0), d);
    }

    // ---------- Contrôle 2 : plafond du jour ----------

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    [InlineData(100, 100)]
    public void Atteindre_exactement_200_est_permis(int withdrawn, int amount)
    {
        var (r, _) = Executer(Compte(250, withdrawn), Plein, null, Retrait(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(withdrawn + amount, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(160, 50)]
    [InlineData(200, 10)]
    [InlineData(150, 60)]
    public void Depasser_200_est_refuse(int withdrawn, int amount)
    {
        var (r, d) = Executer(Compte(250, withdrawn), Plein, null, Retrait(amount));

        AssertRefus(AtmStatus.DailyLimitExceeded, r, Compte(250, withdrawn), Plein, d);
    }

    [Fact]
    public void Plafond_passe_avant_fonds_insuffisants_et_distribution_impossible()
    {
        var (r, d) = Executer(Compte(0, 200), N(0, 0, 0), null, Retrait(10));

        AssertRefus(AtmStatus.DailyLimitExceeded, r, Compte(0, 200), N(0, 0, 0), d);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : permis, même si 110 + 5 de frais ferait 205.
        var (r, _) = Executer(Compte(115, 90), Plein, null, Retrait(110));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Compte(0, 200), r.Account);
    }

    // ---------- Les frais ----------

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void Pas_de_frais_jusqu_a_100_compris(int amount)
    {
        var (r, _) = Executer(Compte(250, 0), Plein, null, Retrait(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(250 - amount, amount), r.Account);
    }

    [Theory]
    [InlineData(110)]
    [InlineData(150)]
    [InlineData(200)]
    public void Frais_de_5_au_dessus_de_100(int amount)
    {
        var (r, _) = Executer(Compte(250, 0), Plein, null, Retrait(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Compte(250 - amount - 5, amount), r.Account);
    }

    [Theory]
    [InlineData(50, 60)]
    [InlineData(100, 100)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Les_frais_dependent_du_montant_seul_pas_du_cumul_du_jour(int withdrawn, int amount)
    {
        // Cumul > 100 mais montant <= 100 : pas de frais.
        var (r, _) = Executer(Compte(250, withdrawn), Plein, null, Retrait(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(250 - amount, withdrawn + amount), r.Account);
    }

    [Fact]
    public void Solde_egal_au_montant_suffit_quand_le_cumul_depasse_100_sans_frais()
    {
        // Montant 60 <= 100 : aucun frais, donc 60 de solde suffisent.
        var (r, _) = Executer(Compte(60, 50), Plein, null, Retrait(60));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(0, 110), r.Account);
    }

    // ---------- Contrôle 3 : fonds insuffisants ----------

    [Theory]
    [InlineData(60, 60)]
    [InlineData(100, 100)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    public void Solde_egal_a_montant_plus_frais_suffit(int balance, int amount)
    {
        var (r, _) = Executer(Compte(balance, 0), Plein, null, Retrait(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Theory]
    [InlineData(55, 60)]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(105, 110)]
    [InlineData(200, 200)]
    [InlineData(0, 10)]
    public void Solde_inferieur_a_montant_plus_frais_est_refuse(int balance, int amount)
    {
        var (r, d) = Executer(Compte(balance, 0), Plein, null, Retrait(amount));

        AssertRefus(AtmStatus.InsufficientFunds, r, Compte(balance, 0), Plein, d);
    }

    [Fact]
    public void Fonds_insuffisants_passe_avant_distribution_impossible()
    {
        var (r, d) = Executer(Compte(0, 0), N(0, 0, 0), null, Retrait(10));

        AssertRefus(AtmStatus.InsufficientFunds, r, Compte(0, 0), N(0, 0, 0), d);
    }

    // ---------- Contrôle 4 : distribution impossible ----------

    [Theory]
    [InlineData(0, 0, 0, null, 10)]
    [InlineData(5, 5, 0, null, 10)]          // pas de billet de 10
    [InlineData(1, 0, 0, null, 30)]
    [InlineData(5, 0, 5, Denomination.Ten, 60)]   // il faudrait un 10, il est bloqué
    [InlineData(0, 5, 5, Denomination.Twenty, 70)] // 10×5 = 50 seulement sans les 20
    [InlineData(1, 1, 1, null, 90)]          // 80 au total dans la machine
    [InlineData(0, 5, 0, null, 110)]         // 100 au total
    public void Aucune_combinaison_disponible_est_refusee(int f, int t, int d10, Denomination? jammed, int amount)
    {
        var stock = N(f, t, d10);
        var (r, d) = Executer(Compte(250, 0), stock, jammed, Retrait(amount));

        AssertRefus(AtmStatus.CannotDispense, r, Compte(250, 0), stock, d);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void La_cassette_bloquee_compte_pour_zero(Denomination jammed)
    {
        // Seule la cassette bloquée contient des billets.
        var stock = N(jammed == Denomination.Fifty ? 5 : 0, jammed == Denomination.Twenty ? 5 : 0, jammed == Denomination.Ten ? 5 : 0);
        var (r, d) = Executer(Compte(250, 0), stock, jammed, Retrait((int)jammed));

        AssertRefus(AtmStatus.CannotDispense, r, Compte(250, 0), stock, d);
    }

    // ---------- La combinaison ----------

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
    [InlineData(160, 3, 0, 1)]
    [InlineData(190, 3, 2, 0)]
    [InlineData(200, 4, 0, 0)]
    public void Stock_plein_le_moins_de_billets(int amount, int f, int t, int d10)
    {
        var (r, d) = Executer(Compte(250, 0), Plein, null, Retrait(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, d10), r.Notes);
        Assert.Equal(Plein - N(f, t, d10), d.Stock);
    }

    [Theory]
    [InlineData(1, 3, 0, null, 60, 0, 3, 0)]              // pas de 10 : 20×3
    [InlineData(1, 3, 1, null, 60, 1, 0, 1)]              // avec un 10 : 50+10
    [InlineData(1, 3, 1, Denomination.Ten, 60, 0, 3, 0)]  // 10 bloqué : 20×3
    [InlineData(5, 5, 5, Denomination.Fifty, 100, 0, 5, 0)]
    [InlineData(5, 5, 5, Denomination.Twenty, 70, 1, 0, 2)]
    [InlineData(5, 0, 5, null, 80, 1, 0, 3)]
    [InlineData(0, 5, 5, null, 150, 0, 5, 5)]
    [InlineData(1, 5, 5, null, 200, 1, 5, 5)]
    [InlineData(2, 5, 0, Denomination.Ten, 140, 2, 2, 0)] // 50×2 20×2 plutôt que 20×7 (impossible) ou 50×1 + 90 (impair)
    public void Le_moins_de_billets_parmi_les_billets_disponibles(
        int f, int t, int d10, Denomination? jammed, int amount, int ef, int et, int ed)
    {
        var stock = N(f, t, d10);
        var (r, d) = Executer(Compte(250, 0), stock, jammed, Retrait(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(ef, et, ed), r.Notes);
        Assert.Equal(amount, r.Notes.Value);
        Assert.Equal(stock - r.Notes, d.Stock);
    }

    /// <summary>
    /// Sur tout le domaine du stock et de la cassette bloquée, et pour tous les montants valides,
    /// la machine sert la combinaison qui compte le moins de billets, calculée ici par force brute,
    /// ou refuse avec CannotDispense s'il n'y en a aucune.
    /// </summary>
    [Fact]
    public void Combinaison_optimale_sur_tout_le_domaine_du_stock()
    {
        Denomination?[] jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];
        var erreurs = new List<string>();

        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d10 = 0; d10 <= 5; d10++)
        foreach (var jammed in jams)
        for (var amount = 10; amount <= 200; amount += 10)
        {
            var stock = N(f, t, d10);
            var attendu = MeilleureCombinaison(stock, jammed, amount);
            var (r, d) = Executer(Compte(250, 0), stock, jammed, Retrait(amount));

            var ok = attendu is { } c
                ? r.Status == AtmStatus.Ok && r.Notes == c && d.Stock == stock - c
                : r.Status == AtmStatus.CannotDispense && r.Notes == NoteCounts.None && d.Stock == stock;

            if (!ok)
            {
                erreurs.Add($"stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, retrait {amount} : attendu {attendu?.ToString() ?? "CannotDispense"}, obtenu {r.Status} {r.Notes}");
            }
        }

        Assert.True(erreurs.Count == 0, string.Join(Environment.NewLine, erreurs.Take(20)));
    }

    private static NoteCounts? MeilleureCombinaison(NoteCounts stock, Denomination? jammed, int amount)
    {
        var maxF = jammed == Denomination.Fifty ? 0 : stock.Fifties;
        var maxT = jammed == Denomination.Twenty ? 0 : stock.Twenties;
        var maxD = jammed == Denomination.Ten ? 0 : stock.Tens;

        NoteCounts? best = null;
        for (var f = 0; f <= maxF; f++)
        for (var t = 0; t <= maxT; t++)
        for (var d = 0; d <= maxD; d++)
        {
            var c = N(f, t, d);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total))
            {
                best = c;
            }
        }

        return best;
    }

    // ---------- La sortie ----------

    [Theory]
    [InlineData(150, 1, 5, 0)]   // 6 billets
    [InlineData(200, 1, 5, 5)]   // 11 billets
    [InlineData(150, 0, 5, 5)]   // 10 billets
    [InlineData(80, 1, 1, 1)]    // 3 billets
    [InlineData(200, 5, 5, 5)]   // 4 billets
    public void Les_billets_sortent_par_passages_de_4_au_plus_et_font_la_combinaison(int amount, int f, int t, int d10)
    {
        var stock = N(f, t, d10);
        var espion = new ModuleEspion(stock);
        var r = new CashMachine(espion).Execute(Compte(250, 0), Retrait(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.NotEmpty(espion.Passages);
        Assert.All(espion.Passages, p =>
        {
            Assert.InRange(p.Total, 1, MoneyDispenser.MaxNotesPerPass);
            Assert.True(p.Fifties >= 0 && p.Twenties >= 0 && p.Tens >= 0, $"passage négatif : {p}");
        });
        Assert.Equal(r.Notes, espion.Passages.Aggregate(NoteCounts.None, (a, p) => a + p));
        Assert.Equal(stock - r.Notes, espion.Stock);
        Assert.Empty(espion.Chargements);
    }

    [Fact]
    public void Un_retrait_de_plus_de_4_billets_reussit_avec_le_vrai_module()
    {
        var (r, d) = Executer(Compte(250, 0), N(1, 5, 5), null, Retrait(200));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 5, 5), r.Notes);
        Assert.Equal(N(0, 0, 0), d.Stock);
        Assert.Equal(Compte(45, 200), r.Account);
    }

    [Fact]
    public void Un_refus_ne_sollicite_pas_la_sortie_de_billets()
    {
        var espion = new ModuleEspion(Plein);
        var machine = new CashMachine(espion);

        machine.Execute(Compte(250, 0), Retrait(5));
        machine.Execute(Compte(250, 200), Retrait(10));
        machine.Execute(Compte(0, 0), Retrait(10));

        var vide = new ModuleEspion(N(0, 0, 0));
        new CashMachine(vide).Execute(Compte(250, 0), Retrait(10));

        Assert.Empty(espion.Passages);
        Assert.Empty(espion.Chargements);
        Assert.Equal(Plein, espion.Stock);
        Assert.Empty(vide.Passages);
    }

    // ---------- Sans mémoire ----------

    [Fact]
    public void La_machine_ne_cumule_pas_elle_meme_les_retraits()
    {
        var dispenser = new MoneyDispenser(Plein);
        var machine = new CashMachine(dispenser);

        var premier = machine.Execute(Compte(250, 0), Retrait(200));
        var second = machine.Execute(Compte(250, 0), Retrait(10));

        Assert.Equal(AtmStatus.Ok, premier.Status);
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(Compte(240, 10), second.Account);
    }

    [Fact]
    public void La_machine_suit_l_etat_courant_du_module()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        var premier = machine.Execute(Compte(250, 0), Retrait(50));
        var second = machine.Execute(Compte(250, 0), Retrait(50));

        Assert.Equal(AtmStatus.Ok, premier.Status);
        Assert.Equal(AtmStatus.CannotDispense, second.Status);
    }
}
