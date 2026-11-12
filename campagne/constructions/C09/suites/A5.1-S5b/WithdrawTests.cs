namespace Atm.Tests;

using static N;

public class WithdrawTests
{
    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (r, d) = Run.Withdraw(250, 0, Full, null, amount);
        AssertAtm.Refused(AtmStatus.InvalidAmount, new(250, 0), Full, r, d);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, aucun billet : seul le montant compte.
        var (r, d) = Run.Withdraw(0, 200, C(0, 0, 0), Denomination.Ten, amount);
        AssertAtm.Refused(AtmStatus.InvalidAmount, new(0, 200), C(0, 0, 0), r, d);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(220)]
    public void Les_multiples_de_10_positifs_ne_sont_pas_invalides(int amount)
    {
        var (r, _) = Run.Withdraw(0, 0, C(0, 0, 0), null, amount);
        Assert.NotEqual(AtmStatus.InvalidAmount, r.Status);
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(190, 20)]
    [InlineData(200, 10)]
    public void Depasser_200_par_jour_est_refuse(int withdrawnToday, int amount)
    {
        var (r, d) = Run.Withdraw(250, withdrawnToday, Full, null, amount);
        AssertAtm.Refused(AtmStatus.DailyLimitExceeded, new(250, withdrawnToday), Full, r, d);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_200_est_permis(int withdrawnToday, int amount)
    {
        var (r, _) = Run.Withdraw(250, withdrawnToday, Full, null, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(200, r.Account.WithdrawnToday);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : permis, même si 110 + 5 de frais dépasse 200 - 90.
        var (r, _) = Run.Withdraw(250, 90, Full, null, 110);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(135, 200), r.Account);
    }

    [Fact]
    public void Plafond_verifie_avant_le_solde_et_les_billets()
    {
        var (r, d) = Run.Withdraw(0, 200, C(0, 0, 0), Denomination.Fifty, 10);
        AssertAtm.Refused(AtmStatus.DailyLimitExceeded, new(0, 200), C(0, 0, 0), r, d);
    }

    // ---- Contrôle 3 : solde et frais ----

    [Theory]
    [InlineData(50, 60)]
    [InlineData(0, 10)]
    [InlineData(95, 100)]
    public void Solde_insuffisant_est_refuse(int balance, int amount)
    {
        var (r, d) = Run.Withdraw(balance, 0, Full, null, amount);
        AssertAtm.Refused(AtmStatus.InsufficientFunds, new(balance, 0), Full, r, d);
    }

    [Theory]
    [InlineData(110, 110, AtmStatus.InsufficientFunds)] // 110 + 5 > 110
    [InlineData(150, 150, AtmStatus.InsufficientFunds)] // 150 + 5 > 150
    [InlineData(200, 200, AtmStatus.InsufficientFunds)] // 200 + 5 > 200
    [InlineData(105, 100, AtmStatus.Ok)]                // pas de frais à 100
    [InlineData(100, 100, AtmStatus.Ok)]
    [InlineData(115, 110, AtmStatus.Ok)]                // 110 + 5 = 115
    [InlineData(205, 200, AtmStatus.Ok)]                // 200 + 5 = 205
    public void Les_frais_comptent_dans_le_solde(int balance, int amount, AtmStatus expected)
    {
        var (r, _) = Run.Withdraw(balance, 0, Full, null, amount);
        Assert.Equal(expected, r.Status);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(10, 10)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    public void Vider_exactement_le_solde_est_permis(int balance, int amount)
    {
        var (r, _) = Run.Withdraw(balance, 0, Full, null, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Fact]
    public void Solde_verifie_avant_les_billets()
    {
        var (r, d) = Run.Withdraw(0, 0, C(0, 0, 0), null, 50);
        AssertAtm.Refused(AtmStatus.InsufficientFunds, new(0, 0), C(0, 0, 0), r, d);
    }

    // ---- Les frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int fee)
    {
        var (r, _) = Run.Withdraw(250, 0, Full, null, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(250 - amount - fee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    // ---- Contrôle 4 : combinaison de billets ----

    [Fact]
    public void Aucun_billet_rend_cannot_dispense()
    {
        var (r, d) = Run.Withdraw(250, 0, C(0, 0, 0), null, 10);
        AssertAtm.Refused(AtmStatus.CannotDispense, new(250, 0), C(0, 0, 0), r, d);
    }

    [Theory]
    [InlineData(0, 5, 0, 30)]  // que des 20 : 30 impossible
    [InlineData(1, 0, 0, 60)]  // pas de 10
    [InlineData(1, 1, 1, 90)]  // au plus 80 disponibles
    [InlineData(0, 3, 0, 70)]  // 20 seulement par paires
    [InlineData(2, 0, 0, 150)] // stock 100 < 150
    public void Aucune_combinaison_exacte_rend_cannot_dispense(int f, int t, int n, int amount)
    {
        var stock = C(f, t, n);
        var (r, d) = Run.Withdraw(250, 0, stock, null, amount);
        AssertAtm.Refused(AtmStatus.CannotDispense, new(250, 0), stock, r, d);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50)]
    [InlineData(Denomination.Twenty, 20)]
    [InlineData(Denomination.Twenty, 40)]
    [InlineData(Denomination.Ten, 10)]
    [InlineData(Denomination.Ten, 30)]
    public void La_cassette_bloquee_compte_pour_zero(Denomination jammed, int amount)
    {
        // Seule la cassette bloquée a des billets.
        var stock = jammed switch
        {
            Denomination.Fifty => C(5, 0, 0),
            Denomination.Twenty => C(0, 5, 0),
            _ => C(0, 0, 5),
        };
        var (r, d) = Run.Withdraw(250, 0, stock, jammed, amount);
        AssertAtm.Refused(AtmStatus.CannotDispense, new(250, 0), stock, r, d);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50, 0, 2, 1)]  // 20×2 10×1
    [InlineData(Denomination.Fifty, 100, 0, 5, 0)] // 20×5
    [InlineData(Denomination.Twenty, 60, 1, 0, 1)] // 50 10
    [InlineData(Denomination.Twenty, 80, 1, 0, 3)] // 50 10×3
    [InlineData(Denomination.Ten, 60, 0, 3, 0)]    // 20×3 (pas 50+10)
    [InlineData(Denomination.Ten, 90, 1, 2, 0)]    // 50 20×2
    public void Avec_une_cassette_bloquee_on_contourne_par_les_autres(Denomination jammed, int amount, int f, int t, int n)
    {
        var (r, d) = Run.Withdraw(250, 0, Full, jammed, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(f, t, n), r.Notes);
        Assert.Equal(Full - C(f, t, n), d.Stock);
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
    public void Stock_plein_le_moins_de_billets(int amount, int f, int t, int n)
    {
        var (r, d) = Run.Withdraw(250, 0, Full, null, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(f, t, n), r.Notes);
        Assert.Equal(Full - C(f, t, n), d.Stock);
    }

    [Theory]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // pas de 10 : 20×3 plutôt qu'échouer sur 50+10
    [InlineData(1, 4, 0, 80, 0, 4, 0)]   // 20×4 (50+? impossible sans 10)
    [InlineData(1, 4, 0, 130, 1, 4, 0)]  // 50 + 20×4
    [InlineData(2, 5, 0, 160, 2, 3, 0)]  // 50×2 20×3 (pas de 10)
    [InlineData(0, 5, 5, 110, 0, 5, 1)]  // 20×5 10×1
    [InlineData(1, 1, 5, 100, 1, 1, 3)]  // 50 20 10×3
    [InlineData(5, 0, 5, 90, 1, 0, 4)]   // 50 10×4
    [InlineData(1, 5, 5, 140, 1, 4, 1)]  // 50 20×4 10 (6 billets plutôt que 50 20×3 10×3)
    [InlineData(0, 0, 5, 50, 0, 0, 5)]   // 10×5
    [InlineData(3, 5, 5, 200, 3, 2, 1)]  // 50×3 20×2 10 (4 de 50 indisponibles)
    public void Stock_partiel_le_moins_de_billets_disponibles(int sf, int st, int sn, int amount, int f, int t, int n)
    {
        var (r, d) = Run.Withdraw(250, 0, C(sf, st, sn), null, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(f, t, n), r.Notes);
        Assert.Equal(C(sf, st, sn) - C(f, t, n), d.Stock);
    }

    // ---- La sortie : plusieurs passages ----

    [Theory]
    [InlineData(0, 5, 0, 100)]  // 5 billets
    [InlineData(0, 5, 5, 150)]  // 10 billets
    [InlineData(1, 5, 5, 200)]  // 11 billets
    [InlineData(0, 0, 5, 50)]   // 5 billets de 10
    public void Plus_de_4_billets_sortent_en_plusieurs_passages(int f, int t, int n, int amount)
    {
        var stock = C(f, t, n);
        var (r, d) = Run.Withdraw(250, 0, stock, null, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(amount, r.Notes.Value);
        Assert.Equal(stock - r.Notes, d.Stock);
    }

    [Theory]
    [InlineData(0, 5, 5, 150)]
    [InlineData(1, 5, 5, 200)]
    [InlineData(5, 5, 5, 80)]
    public void Chaque_passage_compte_au_plus_4_billets_et_la_somme_fait_la_combinaison(int f, int t, int n, int amount)
    {
        var dispenser = new FaultyDispenser(C(f, t, n));
        var r = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.All(dispenser.DispenseCalls, p => Assert.InRange(p.Total, 1, 4));
        Assert.Equal(r.Notes, dispenser.Dispensed);
        Assert.Empty(dispenser.LoadCalls);
    }

    // ---- Le succès ----

    [Fact]
    public void Le_succes_met_a_jour_le_compte_et_le_module()
    {
        var (r, d) = Run.Withdraw(200, 40, C(2, 3, 4), null, 130);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(2, 1, 1), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(65, 170), r.Account);
        Assert.Equal(C(0, 2, 3), d.Stock);
    }

    // ---- Refus : aucun billet sorti ----

    public static TheoryData<int, int, NoteCounts, Denomination?, int, AtmStatus> Refusals => new()
    {
        { 250, 0, Full, null, -10, AtmStatus.InvalidAmount },
        { 250, 0, Full, null, 0, AtmStatus.InvalidAmount },
        { 250, 0, Full, null, 5, AtmStatus.InvalidAmount },
        { 250, 150, Full, null, 60, AtmStatus.DailyLimitExceeded },
        { 50, 0, Full, null, 60, AtmStatus.InsufficientFunds },
        { 250, 0, C(0, 5, 0), null, 30, AtmStatus.CannotDispense },
        { 250, 0, C(5, 0, 0), Denomination.Fifty, 50, AtmStatus.CannotDispense },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Un_refus_ne_touche_pas_au_module(int balance, int withdrawn, NoteCounts stock, Denomination? jammed, int amount, AtmStatus status)
    {
        var dispenser = new FaultyDispenser(stock, jammed);
        var r = new CashMachine(dispenser).Execute(new(balance, withdrawn), new Operation.Withdraw(amount));
        AssertAtm.Refused(status, new(balance, withdrawn), stock, r, dispenser);
        Assert.Empty(dispenser.SuccessfulPasses);
    }
}
