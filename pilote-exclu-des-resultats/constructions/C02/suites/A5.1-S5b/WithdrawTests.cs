namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static (AtmResult Result, SpyDispenser Dispenser) Withdraw(
        int balance, int withdrawnToday, NoteCounts stock, int amount, Denomination? jammed = null)
    {
        var dispenser = new SpyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawnToday), new Operation.Withdraw(amount));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult result, SpyDispenser dispenser,
        AccountState account, NoteCounts stock)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.SuccessfulDispenses);
        Assert.Empty(dispenser.SuccessfulLoads);
    }

    // ---------- Exemples de la spec ----------

    [Fact]
    public void Exemple_retrait_60_avec_trois_billets_de_20()
    {
        var (r, d) = Withdraw(100, 0, N(1, 3, 0), 60);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 3, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(40, 60), r.Account);
        Assert.Equal(N(1, 0, 0), d.Stock);
    }

    [Fact]
    public void Exemple_retrait_80_avec_une_coupure_de_chaque()
    {
        var (r, d) = Withdraw(100, 0, N(1, 1, 1), 80);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 1, 1), r.Notes);
        Assert.Equal(new AccountState(20, 80), r.Account);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }

    [Fact]
    public void Exemple_retrait_100_sans_frais()
    {
        var (r, _) = Withdraw(105, 0, Full, 100);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 0), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(5, 100), r.Account);
    }

    [Fact]
    public void Exemple_retrait_110_avec_frais()
    {
        var (r, _) = Withdraw(115, 0, Full, 110);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 0, 1), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(0, 110), r.Account);
    }

    [Fact]
    public void Exemple_retrait_110_solde_insuffisant_a_cause_des_frais()
    {
        var (r, d) = Withdraw(110, 0, Full, 110);
        AssertRefused(AtmStatus.InsufficientFunds, r, d, new AccountState(110, 0), Full);
    }

    [Fact]
    public void Exemple_plafond_du_jour_depasse()
    {
        var (r, d) = Withdraw(250, 150, Full, 60);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, d, new AccountState(250, 150), Full);
    }

    [Fact]
    public void Exemple_cassette_bloquee_impossible_a_servir()
    {
        var (r, d) = Withdraw(250, 0, N(1, 0, 0), 50, Denomination.Fifty);
        AssertRefused(AtmStatus.CannotDispense, r, d, new AccountState(250, 0), N(1, 0, 0));
    }

    [Fact]
    public void Exemple_cassette_bloquee_contournee()
    {
        var (r, d) = Withdraw(250, 0, N(2, 2, 0), 40, Denomination.Fifty);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 2, 0), r.Notes);
        Assert.Equal(new AccountState(210, 40), r.Account);
        Assert.Equal(N(2, 0, 0), d.Stock);
    }

    [Fact]
    public void Exemple_montant_invalide_passe_avant_tous_les_autres_controles()
    {
        var (r, d) = Withdraw(0, 200, N(0, 0, 0), 5);
        AssertRefused(AtmStatus.InvalidAmount, r, d, new AccountState(0, 200), N(0, 0, 0));
    }

    // ---------- Contrôle 1 : montant ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (r, d) = Withdraw(250, 0, Full, amount);
        AssertRefused(AtmStatus.InvalidAmount, r, d, new AccountState(250, 0), Full);
    }

    [Fact]
    public void Plus_petit_montant_valide_est_accepte()
    {
        var (r, _) = Withdraw(250, 0, Full, 10);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 0, 1), r.Notes);
    }

    // ---------- Contrôle 2 : plafond du jour ----------

    [Theory]
    [InlineData(0, 200)]
    [InlineData(190, 10)]
    [InlineData(100, 100)]
    [InlineData(90, 110)]
    public void Atteindre_exactement_200_est_autorise(int withdrawnToday, int amount)
    {
        var (r, _) = Withdraw(250, withdrawnToday, Full, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(withdrawnToday + amount, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(200, 10)]
    [InlineData(150, 60)]
    public void Depasser_200_est_refuse(int withdrawnToday, int amount)
    {
        var (r, d) = Withdraw(250, withdrawnToday, Full, amount);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, d, new AccountState(250, withdrawnToday), Full);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : pile au plafond, les 5 € de frais n'y entrent pas.
        var (r, _) = Withdraw(250, 90, Full, 110);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(135, 200), r.Account);
    }

    [Fact]
    public void Plafond_controle_avant_le_solde()
    {
        var (r, d) = Withdraw(0, 200, Full, 10);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, d, new AccountState(0, 200), Full);
    }

    [Fact]
    public void Plafond_controle_avant_la_disponibilite_des_billets()
    {
        var (r, d) = Withdraw(250, 200, N(0, 0, 0), 10);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, d, new AccountState(250, 200), N(0, 0, 0));
    }

    // ---------- Contrôle 3 : solde et frais ----------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_euros_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var (r, _) = Withdraw(250, 0, Full, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedFee, r.Fee);
        Assert.Equal(250 - amount - expectedFee, r.Account.Balance);
    }

    [Fact]
    public void Retirer_tout_le_solde_sans_frais_est_autorise()
    {
        var (r, _) = Withdraw(100, 0, Full, 100);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(0, 100), r.Account);
    }

    [Theory]
    [InlineData(95, 100)]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    [InlineData(114, 110)]
    [InlineData(200, 200)]
    public void Montant_plus_frais_superieur_au_solde_est_refuse(int balance, int amount)
    {
        var (r, d) = Withdraw(balance, 0, Full, amount);
        AssertRefused(AtmStatus.InsufficientFunds, r, d, new AccountState(balance, 0), Full);
    }

    [Fact]
    public void Montant_plus_frais_egal_au_solde_est_autorise()
    {
        var (r, _) = Withdraw(205, 0, Full, 200);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(0, 200), r.Account);
    }

    [Fact]
    public void Solde_controle_avant_la_disponibilite_des_billets()
    {
        var (r, d) = Withdraw(0, 0, N(0, 0, 0), 50);
        AssertRefused(AtmStatus.InsufficientFunds, r, d, new AccountState(0, 0), N(0, 0, 0));
    }

    // ---------- Contrôle 4 : billets disponibles ----------

    [Fact]
    public void Stock_vide_ne_peut_rien_servir()
    {
        var (r, d) = Withdraw(250, 0, N(0, 0, 0), 10);
        AssertRefused(AtmStatus.CannotDispense, r, d, new AccountState(250, 0), N(0, 0, 0));
    }

    [Fact]
    public void Aucune_combinaison_exacte()
    {
        // Que des billets de 20 : 30 est impossible.
        var (r, d) = Withdraw(250, 0, N(0, 5, 0), 30);
        AssertRefused(AtmStatus.CannotDispense, r, d, new AccountState(250, 0), N(0, 5, 0));
    }

    [Fact]
    public void Stock_total_insuffisant()
    {
        var (r, d) = Withdraw(250, 0, N(1, 1, 1), 90);
        AssertRefused(AtmStatus.CannotDispense, r, d, new AccountState(250, 0), N(1, 1, 1));
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50)]
    [InlineData(Denomination.Twenty, 20)]
    [InlineData(Denomination.Ten, 10)]
    public void La_cassette_bloquee_compte_pour_zero(Denomination jammed, int amount)
    {
        var stock = new NoteCounts(
            jammed == Denomination.Fifty ? 5 : 0,
            jammed == Denomination.Twenty ? 5 : 0,
            jammed == Denomination.Ten ? 5 : 0);
        var (r, d) = Withdraw(250, 0, stock, amount, jammed);
        AssertRefused(AtmStatus.CannotDispense, r, d, new AccountState(250, 0), stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 100, 0, 5, 0)]
    [InlineData(Denomination.Twenty, 60, 1, 0, 1)]
    [InlineData(Denomination.Ten, 70, 1, 1, 0)]
    [InlineData(Denomination.Ten, 60, 0, 3, 0)]
    public void Le_retrait_contourne_la_cassette_bloquee(Denomination jammed, int amount, int f, int t, int c)
    {
        var (r, d) = Withdraw(250, 0, Full, amount, jammed);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, c), r.Notes);
        Assert.Equal(0, r.Notes.Of(jammed));
        Assert.Equal(5, d.Stock.Of(jammed));
    }

    // ---------- La combinaison : le moins de billets ----------

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
    [InlineData(140, 2, 2, 0)]
    [InlineData(190, 3, 2, 0)]
    [InlineData(200, 4, 0, 0)]
    public void Avec_stock_plein_la_combinaison_minimale(int amount, int f, int t, int c)
    {
        var (r, d) = Withdraw(250, 0, Full, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, c), r.Notes);
        Assert.Equal(Full - N(f, t, c), d.Stock);
    }

    [Theory]
    // Le glouton (prendre d'abord les 50) échouerait ou ne serait pas minimal.
    [InlineData(1, 3, 0, 60, 0, 3, 0)]
    [InlineData(1, 4, 0, 80, 0, 4, 0)]
    [InlineData(2, 3, 0, 110, 1, 3, 0)]
    [InlineData(3, 5, 0, 160, 2, 3, 0)]
    // Quand une coupure manque, on se rabat sur les plus petites.
    [InlineData(0, 5, 5, 90, 0, 4, 1)]
    [InlineData(0, 0, 5, 50, 0, 0, 5)]
    [InlineData(1, 0, 5, 100, 1, 0, 5)]
    [InlineData(2, 1, 0, 120, 2, 1, 0)]
    public void Combinaison_minimale_avec_stock_partiel(int sf, int st, int sc, int amount, int f, int t, int c)
    {
        var stock = N(sf, st, sc);
        var (r, d) = Withdraw(250, 0, stock, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, c), r.Notes);
        Assert.Equal(stock - N(f, t, c), d.Stock);
    }

    // ---------- La sortie : passages de 4 billets au plus ----------

    [Theory]
    [InlineData(5, 5, 5, 200, 4, 0, 0)]  // 4 billets : un passage suffit
    [InlineData(5, 5, 5, 180, 3, 1, 1)]  // 5 billets : au moins deux passages
    [InlineData(0, 0, 5, 50, 0, 0, 5)]   // 5 billets de 10
    [InlineData(0, 5, 5, 140, 0, 5, 4)]  // 9 billets : au moins trois passages
    public void Les_billets_sortent_en_passages_de_4_au_plus(int sf, int st, int sc, int amount, int f, int t, int c)
    {
        var (r, d) = Withdraw(250, 0, N(sf, st, sc), amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, c), r.Notes);

        var passes = d.Calls.Where(c => c.Kind == "Dispense").ToList();
        Assert.NotEmpty(passes);
        Assert.All(passes, p =>
        {
            Assert.True(p.Result);
            Assert.InRange(p.Notes.Total, 1, 4);
        });
        Assert.Equal(r.Notes, passes.Aggregate(NoteCounts.None, (acc, p) => acc + p.Notes));
        Assert.Empty(d.SuccessfulLoads);
    }

    [Fact]
    public void Retrait_de_5_billets_exige_au_moins_deux_passages()
    {
        var (r, d) = Withdraw(250, 0, Full, 180);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.True(d.SuccessfulDispenses.Count() >= 2);
        Assert.Equal(N(2, 4, 4), d.Stock);
    }

    // ---------- Pannes du module ----------

    [Fact]
    public void Panne_au_premier_passage()
    {
        var dispenser = new SpyDispenser(Full, failOnCall: 1);
        var account = new AccountState(250, 0);
        var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(Full, dispenser.Stock);
        Assert.Single(dispenser.Calls);
    }

    [Fact]
    public void Panne_au_second_passage_rend_les_billets_deja_sortis()
    {
        var dispenser = new SpyDispenser(Full, failOnCall: 2);
        var account = new AccountState(250, 0);
        var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(180));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);

        // On s'arrête aussitôt : aucun appel après la panne.
        Assert.Equal(2, dispenser.Calls.Count);
        Assert.False(dispenser.Calls[1].Result);

        var first = dispenser.Calls[0];
        Assert.True(first.Result);
        Assert.Equal(first.Notes, r.Notes);
        Assert.True(r.Notes.Total > 0);
        Assert.Equal(Full - r.Notes, dispenser.Stock);
    }

    [Fact]
    public void Panne_au_second_passage_sans_frais_rend_les_billets_deja_sortis()
    {
        // 50 € avec seulement des billets de 10 : 5 billets, donc au moins deux passages.
        var stock = N(0, 0, 5);
        var dispenser = new SpyDispenser(stock, failOnCall: 2);
        var account = new AccountState(250, 0);
        var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(2, dispenser.Calls.Count);
        Assert.Equal(dispenser.Calls[0].Notes, r.Notes);
        Assert.Equal(stock - r.Notes, dispenser.Stock);
    }

    [Fact]
    public void Panne_sur_retrait_avec_frais_ne_preleve_pas_de_frais()
    {
        var dispenser = new SpyDispenser(Full, failOnCall: 1);
        var account = new AccountState(250, 50);
        var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
    }

    // ---------- Machine sans mémoire ----------

    [Fact]
    public void La_machine_ne_garde_rien_entre_deux_appels()
    {
        var dispenser = new SpyDispenser(Full);
        var machine = new CashMachine(dispenser);

        // Un premier appel, refusé puis réussi, ne doit pas influencer le suivant.
        machine.Execute(new AccountState(0, 0), new Operation.Withdraw(50));
        machine.Execute(new AccountState(250, 0), new Operation.Withdraw(200));

        var account = new AccountState(100, 0);
        var second = machine.Execute(account, new Operation.Withdraw(60));
        var fresh = new CashMachine(new SpyDispenser(N(1, 5, 5))).Execute(account, new Operation.Withdraw(60));

        Assert.Equal(fresh, second);
        Assert.Equal(new AccountState(40, 60), second.Account);
    }

    [Fact]
    public void Meme_compte_et_meme_operation_donnent_le_meme_resultat()
    {
        var machine = new CashMachine(new SpyDispenser(Full));
        var account = new AccountState(250, 0);

        var a = machine.Execute(account, new Operation.Withdraw(70));
        var b = machine.Execute(account, new Operation.Withdraw(70));

        Assert.Equal(a, b);
    }
}
