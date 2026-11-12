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
        var (m, d) = Machine(Full);
        AssertRefused(m.Withdraw(250, 0, amount), AtmStatus.InvalidAmount, new(250, 0), d, Full);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, aucun billet : seul le montant invalide doit être signalé.
        var (m, d) = Machine(N(0, 0, 0));
        AssertRefused(m.Withdraw(0, 200, amount), AtmStatus.InvalidAmount, new(0, 200), d, N(0, 0, 0));
    }

    // ---------- Contrôle 2 : plafond du jour ----------

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    [InlineData(100, 100)]
    public void Atteindre_exactement_200_est_permis(int withdrawnToday, int amount)
    {
        var (m, _) = Machine(Full);
        var r = m.Withdraw(250, withdrawnToday, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(withdrawnToday + amount, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(200, 10)]
    [InlineData(150, 60)]
    [InlineData(190, 20)]
    public void Depasser_200_est_refuse(int withdrawnToday, int amount)
    {
        var (m, d) = Machine(Full);
        AssertRefused(m.Withdraw(250, withdrawnToday, amount), AtmStatus.DailyLimitExceeded, new(250, withdrawnToday), d, Full);
    }

    [Fact]
    public void Plafond_passe_avant_solde_insuffisant()
    {
        var (m, d) = Machine(Full);
        AssertRefused(m.Withdraw(0, 200, 10), AtmStatus.DailyLimitExceeded, new(0, 200), d, Full);
    }

    [Fact]
    public void Plafond_passe_avant_billets_indisponibles()
    {
        var (m, d) = Machine(N(0, 0, 0));
        AssertRefused(m.Withdraw(250, 100, 110), AtmStatus.DailyLimitExceeded, new(250, 100), d, N(0, 0, 0));
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : permis, même si 110 + 5 de frais dépasserait 200 côté plafond.
        var (m, _) = Machine(Full);
        var r = m.Withdraw(250, 90, 110);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(135, 200), r.Account);
    }

    [Fact]
    public void Retrait_de_200_avec_frais_respecte_le_plafond()
    {
        var (m, _) = Machine(Full);
        var r = m.Withdraw(205, 0, 200);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(4, 0, 0), 5, new AccountState(0, 200)), r);
    }

    // ---------- Contrôle 3 : solde et frais ----------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var (m, _) = Machine(Full);
        var r = m.Withdraw(250, 0, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedFee, r.Fee);
        Assert.Equal(250 - amount - expectedFee, r.Account.Balance);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    [InlineData(110, 115)]
    [InlineData(200, 205)]
    public void Solde_exactement_egal_au_montant_plus_frais_suffit(int amount, int balance)
    {
        var (m, _) = Machine(Full);
        var r = m.Withdraw(balance, 0, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(10, 0)]
    [InlineData(100, 95)]
    [InlineData(110, 110)]
    [InlineData(150, 150)]
    [InlineData(200, 200)]
    public void Solde_inferieur_au_montant_plus_frais_est_insuffisant(int amount, int balance)
    {
        var (m, d) = Machine(Full);
        AssertRefused(m.Withdraw(balance, 0, amount), AtmStatus.InsufficientFunds, new(balance, 0), d, Full);
    }

    [Fact]
    public void Solde_insuffisant_passe_avant_billets_indisponibles()
    {
        var (m, d) = Machine(N(0, 0, 0));
        AssertRefused(m.Withdraw(0, 0, 10), AtmStatus.InsufficientFunds, new(0, 0), d, N(0, 0, 0));
    }

    [Fact]
    public void Solde_insuffisant_a_cause_des_frais_passe_avant_billets_indisponibles()
    {
        var (m, d) = Machine(N(0, 0, 0), Denomination.Ten);
        AssertRefused(m.Withdraw(110, 0, 110), AtmStatus.InsufficientFunds, new(110, 0), d, N(0, 0, 0));
    }

    // ---------- Contrôle 4 : combinaison de billets ----------

    [Fact]
    public void Stock_vide_ne_peut_rien_distribuer()
    {
        var (m, d) = Machine(N(0, 0, 0));
        AssertRefused(m.Withdraw(250, 0, 10), AtmStatus.CannotDispense, new(250, 0), d, N(0, 0, 0));
    }

    [Theory]
    [InlineData(1, 0, 0, 30)]   // un seul 50 : pas de 30
    [InlineData(0, 5, 0, 10)]   // que des 20 : pas de 10
    [InlineData(0, 5, 0, 110)]  // que des 20 : pas de 110 (impair en dizaines)
    [InlineData(1, 0, 1, 40)]   // 50 + 10 : ni 40
    [InlineData(0, 0, 4, 50)]   // 10×4 : pas de 50
    public void Combinaison_exacte_impossible(int f, int t, int n, int amount)
    {
        var (m, d) = Machine(N(f, t, n));
        AssertRefused(m.Withdraw(250, 0, amount), AtmStatus.CannotDispense, new(250, 0), d, N(f, t, n));
    }

    [Fact]
    public void Stock_total_insuffisant()
    {
        // 50+20+10 = 80 disponibles, on demande 90.
        var (m, d) = Machine(N(1, 1, 1));
        AssertRefused(m.Withdraw(250, 0, 90), AtmStatus.CannotDispense, new(250, 0), d, N(1, 1, 1));
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50)]
    [InlineData(Denomination.Twenty, 20)]
    [InlineData(Denomination.Ten, 10)]
    public void La_cassette_bloquee_compte_pour_zero(Denomination jammed, int amount)
    {
        // Seule la cassette bloquée pourrait servir le montant.
        var stock = jammed switch
        {
            Denomination.Fifty => N(5, 0, 0),
            Denomination.Twenty => N(0, 5, 0),
            _ => N(0, 0, 5),
        };
        var (m, d) = Machine(stock, jammed);
        AssertRefused(m.Withdraw(250, 0, amount), AtmStatus.CannotDispense, new(250, 0), d, stock);
    }

    [Fact]
    public void Cassette_de_10_bloquee_force_une_autre_combinaison()
    {
        // 60 = 50+10 normalement, mais les 10 sont bloqués : 20×3.
        var (m, d) = Machine(Full, Denomination.Ten);
        var r = m.Withdraw(250, 0, 60);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 3, 0), r.Notes);
        Assert.Equal(N(5, 2, 5), d.Stock);
    }

    [Fact]
    public void Cassette_de_20_bloquee_force_une_autre_combinaison()
    {
        // 40 = 20×2 normalement, mais les 20 sont bloqués : 10×4.
        var (m, d) = Machine(Full, Denomination.Twenty);
        var r = m.Withdraw(250, 0, 40);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 0, 4), r.Notes);
        Assert.Equal(N(5, 5, 1), d.Stock);
    }

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
    [InlineData(110, 2, 0, 1)]
    [InlineData(120, 2, 1, 0)]
    [InlineData(130, 2, 1, 1)]
    [InlineData(140, 2, 2, 0)]
    [InlineData(150, 3, 0, 0)]
    [InlineData(160, 3, 0, 1)]
    [InlineData(170, 3, 1, 0)]
    [InlineData(180, 3, 1, 1)]
    [InlineData(190, 3, 2, 0)]
    [InlineData(200, 4, 0, 0)]
    public void Stock_plein_donne_la_combinaison_au_moins_de_billets(int amount, int f, int t, int n)
    {
        var (m, d) = Machine(Full);
        var r = m.Withdraw(250, 0, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, n), r.Notes);
        Assert.Equal(Full - N(f, t, n), d.Stock);
    }

    [Theory]
    [InlineData(1, 4, 0, 80, 0, 4, 0)]   // glouton : 50+20+? échoue ; réponse 20×4
    [InlineData(1, 3, 0, 110, 1, 3, 0)]  // 50 + 20×3
    [InlineData(2, 3, 0, 160, 2, 3, 0)]  // 50×2 + 20×3
    [InlineData(0, 5, 5, 150, 0, 5, 5)]  // tout le stock sans 50
    [InlineData(0, 1, 5, 60, 0, 1, 4)]   // 20 + 10×4 (5 billets)
    [InlineData(0, 0, 5, 50, 0, 0, 5)]   // 10×5
    [InlineData(1, 0, 5, 100, 1, 0, 5)]  // 50 + 10×5
    [InlineData(5, 0, 5, 60, 1, 0, 1)]   // 50+10 plutôt que 10×6 (impossible de toute façon)
    [InlineData(1, 5, 5, 100, 1, 2, 1)]  // 50+20+20+10 (4) plutôt que 20×5 (5)
    public void Combinaison_au_moins_de_billets_avec_stock_partiel(int sf, int st, int sn, int amount, int f, int t, int n)
    {
        var (m, d) = Machine(N(sf, st, sn));
        var r = m.Withdraw(250, 0, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, n), r.Notes);
        Assert.Equal(N(sf, st, sn) - N(f, t, n), d.Stock);
    }

    // ---------- Sortie en plusieurs passages ----------

    [Theory]
    [InlineData(190, 3, 2, 0)] // 5 billets
    [InlineData(180, 3, 1, 1)] // 5 billets
    public void Plus_de_4_billets_sortent_en_plusieurs_passages(int amount, int f, int t, int n)
    {
        var dispenser = new RecordingDispenser(Full);
        var r = new CashMachine(dispenser).Withdraw(250, 0, amount);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(f, t, n), 5, new AccountState(245 - amount, amount)), r);
        Assert.Equal(Full - N(f, t, n), dispenser.Stock);
        Assert.True(dispenser.DispenseCalls.Count >= 2);
        Assert.All(dispenser.DispenseCalls, p => Assert.InRange(p.Total, 1, 4));
        Assert.Equal(N(f, t, n), dispenser.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
    }

    [Fact]
    public void Dix_billets_sortent_en_au_moins_trois_passages()
    {
        var dispenser = new RecordingDispenser(N(0, 5, 5));
        var r = new CashMachine(dispenser).Withdraw(155, 0, 150);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 5, 5), 5, new AccountState(0, 150)), r);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
        Assert.True(dispenser.DispenseCalls.Count >= 3);
        Assert.All(dispenser.DispenseCalls, p => Assert.InRange(p.Total, 1, 4));
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void Retrait_reussi_ne_touche_pas_la_cassette_bloquee()
    {
        var dispenser = new RecordingDispenser(Full, Denomination.Fifty);
        var r = new CashMachine(dispenser).Withdraw(250, 0, 150);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 5, 5), r.Notes); // 150 sans 50 : 20×5 + 10×5
        Assert.All(dispenser.DispenseCalls, p => Assert.Equal(0, p.Fifties));
        Assert.Equal(N(5, 0, 0), dispenser.Stock);
    }

    // ---------- Succès : mise à jour du compte ----------

    [Fact]
    public void Succes_debite_montant_plus_frais_et_augmente_le_retire_du_jour()
    {
        var (m, _) = Machine(Full);
        var r = m.Withdraw(200, 40, 120);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 1, 0), 5, new AccountState(75, 160)), r);
    }

    [Fact]
    public void Succes_sans_frais_ne_prend_que_le_montant()
    {
        var (m, _) = Machine(Full);
        var r = m.Withdraw(35, 10, 30);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 1, 1), 0, new AccountState(5, 40)), r);
    }

    // ---------- Sans mémoire ----------

    [Fact]
    public void La_machine_ne_garde_pas_le_compte_entre_deux_appels()
    {
        var (m, d) = Machine(Full);
        var first = m.Withdraw(100, 0, 50);
        var second = m.Withdraw(100, 0, 50);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, new AccountState(50, 50)), first);
        Assert.Equal(first, second);
        Assert.Equal(N(3, 5, 5), d.Stock);
    }

    [Fact]
    public void Un_refus_n_influence_pas_l_appel_suivant()
    {
        var (m, _) = Machine(Full);
        Assert.Equal(AtmStatus.DailyLimitExceeded, m.Withdraw(250, 200, 10).Status);
        Assert.Equal(AtmStatus.Ok, m.Withdraw(250, 0, 10).Status);
    }

    [Fact]
    public void La_machine_suit_le_stock_reel_du_module()
    {
        var (m, d) = Machine(N(1, 0, 0));
        Assert.Equal(AtmStatus.Ok, m.Withdraw(250, 0, 50).Status);
        AssertRefused(m.Withdraw(250, 0, 50), AtmStatus.CannotDispense, new(250, 0), d, N(0, 0, 0));
    }
}
