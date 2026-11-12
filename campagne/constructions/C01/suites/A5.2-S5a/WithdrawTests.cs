using static Atm.Tests.Helpers;

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
        var (r, d) = Run(250, 0, Full, null, new Operation.Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, new AccountState(250, 0)), r);
        Assert.Equal(Full, d.Stock);
    }

    [Fact]
    public void Montant_invalide_passe_avant_le_plafond_le_solde_et_les_billets()
    {
        // Tout le reste échouerait aussi : plafond atteint, solde nul, aucun billet.
        var (r, _) = Run(0, 200, N(0, 0, 0), Denomination.Ten, new Operation.Withdraw(-10));

        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    // ---------- Contrôle 2 : plafond du jour ----------

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    [InlineData(100, 100)]
    public void Atteindre_exactement_200_est_permis(int withdrawn, int amount)
    {
        var (r, _) = Run(250, withdrawn, Full, null, new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(250 - amount - FeeFor(amount), withdrawn + amount), r.Account);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(100, 110)]
    public void Depasser_200_dans_la_journee_est_refuse(int withdrawn, int amount)
    {
        var (r, d) = Run(250, withdrawn, Full, null, new Operation.Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, new AccountState(250, withdrawn)), r);
        Assert.Equal(Full, d.Stock);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : permis, même si 110 + 5 € de frais ferait 205.
        var (r, _) = Run(250, 90, Full, null, new Operation.Withdraw(110));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(135, 200), r.Account);
    }

    [Fact]
    public void Plafond_passe_avant_le_solde_et_les_billets()
    {
        var (r, _) = Run(0, 200, N(0, 0, 0), null, new Operation.Withdraw(10));

        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    // ---------- Contrôle 3 : solde et frais ----------

    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(110, 115, 5)]
    [InlineData(200, 205, 5)]
    public void Solde_exactement_suffisant_avec_les_frais_est_accepte(int amount, int balance, int fee)
    {
        var (r, _) = Run(balance, 0, Full, null, new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(new AccountState(0, amount), r.Account);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(100, 95)]
    [InlineData(110, 110)]
    [InlineData(200, 200)]
    [InlineData(150, 0)]
    public void Solde_insuffisant_avec_les_frais_est_refuse(int amount, int balance)
    {
        var (r, d) = Run(balance, 0, Full, null, new Operation.Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, new AccountState(balance, 0)), r);
        Assert.Equal(Full, d.Stock);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_euros_strictement_au_dessus_de_100(int amount, int fee)
    {
        var (r, _) = Run(250, 0, Full, null, new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(250 - amount - fee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    [Fact]
    public void Solde_passe_avant_les_billets()
    {
        var (r, _) = Run(0, 0, N(0, 0, 0), null, new Operation.Withdraw(10));

        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    // ---------- Contrôle 4 : combinaison de billets ----------

    [Theory]
    [InlineData(0, 0, 0, 0, 10)]
    [InlineData(1, 0, 0, 0, 30)]
    [InlineData(0, 5, 5, 0, 160)]
    [InlineData(5, 5, 5, 10, 10)]
    [InlineData(5, 0, 5, 10, 60)]
    [InlineData(0, 5, 0, 20, 20)]
    [InlineData(0, 0, 5, 0, 60)]
    [InlineData(1, 0, 0, 0, 40)]
    public void Aucune_combinaison_disponible_donne_CannotDispense(int f, int t, int d, int jammed, int amount)
    {
        var stock = N(f, t, d);
        var (r, disp) = Run(250, 0, stock, J(jammed), new Operation.Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new AccountState(250, 0)), r);
        Assert.Equal(stock, disp.Stock);
    }

    [Theory]
    // stock (50, 20, 10), bloquée, montant -> combinaison attendue
    [InlineData(5, 5, 5, 0, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 0, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 0, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 0, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 0, 200, 4, 0, 0)]
    [InlineData(5, 5, 5, 0, 180, 3, 1, 1)]
    [InlineData(5, 5, 5, 0, 190, 3, 2, 0)]
    [InlineData(0, 5, 5, 0, 60, 0, 3, 0)]
    [InlineData(1, 5, 5, 0, 130, 1, 4, 0)]
    [InlineData(1, 1, 5, 0, 100, 1, 1, 3)]
    [InlineData(5, 0, 5, 0, 80, 1, 0, 3)]
    [InlineData(5, 5, 0, 0, 60, 0, 3, 0)]  // pas de 10 : 20×3
    [InlineData(5, 5, 5, 50, 100, 0, 5, 0)] // 50 bloqués
    [InlineData(5, 5, 5, 20, 90, 1, 0, 4)]  // 20 bloqués
    [InlineData(5, 5, 5, 10, 70, 1, 1, 0)]  // 10 bloqués
    [InlineData(5, 5, 5, 10, 60, 0, 3, 0)]  // 10 bloqués : 50+10 exclu
    [InlineData(0, 5, 5, 0, 150, 0, 5, 5)]
    public void La_combinaison_est_celle_qui_compte_le_moins_de_billets(
        int f, int t, int d, int jammed, int amount, int ef, int et, int ed)
    {
        var stock = N(f, t, d);
        var expected = N(ef, et, ed);
        var (r, disp) = Run(250, 0, stock, J(jammed), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expected, r.Notes);
        Assert.Equal(FeeFor(amount), r.Fee);
        Assert.Equal(new AccountState(250 - amount - FeeFor(amount), amount), r.Account);
        Assert.Equal(stock - expected, disp.Stock);
    }

    [Fact]
    public void La_cassette_bloquee_compte_pour_zero_meme_si_elle_est_pleine()
    {
        var (r, d) = Run(250, 0, N(5, 0, 5), Denomination.Fifty, new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 0, 5), r.Notes);
        Assert.Equal(N(5, 0, 0), d.Stock);
    }

    // ---------- La sortie : plusieurs passages de 4 billets au plus ----------

    [Theory]
    [InlineData(5, 5, 5, 0, 180)] // 50×3 20×1 10×1 : 5 billets
    [InlineData(5, 5, 5, 0, 190)] // 50×3 20×2 : 5 billets
    [InlineData(1, 5, 5, 0, 130)] // 50×1 20×4 : 5 billets
    [InlineData(0, 5, 5, 0, 120)] // 20×5 10×2 : 7 billets
    [InlineData(0, 5, 5, 0, 150)] // 20×5 10×5 : 10 billets
    [InlineData(1, 5, 5, 0, 150)] // 50×1 20×5 : 6 billets
    [InlineData(5, 5, 5, 50, 150)] // 20×5 10×5 avec 50 bloqués
    [InlineData(5, 0, 5, 20, 200)] // 50×4 : 4 billets, un seul passage
    public void Plus_de_4_billets_sortent_en_plusieurs_passages(int f, int t, int d, int jammed, int amount)
    {
        var stock = N(f, t, d);
        var expected = FewestNotes(amount, Available(stock, J(jammed)))!.Value;
        var spy = new SpyDispenser(new MoneyDispenser(stock, J(jammed)));
        var r = new CashMachine(spy).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expected, r.Notes);
        Assert.Equal(new AccountState(250 - amount - FeeFor(amount), amount), r.Account);
        Assert.All(spy.Dispensed, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(expected, spy.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(stock - expected, spy.Stock);
        Assert.Empty(spy.Loaded);
    }

    [Fact]
    public void Retrait_accepte_ne_charge_jamais_de_billets()
    {
        var spy = new SpyDispenser(new MoneyDispenser(Full));
        new CashMachine(spy).Execute(new AccountState(250, 0), new Operation.Withdraw(80));

        Assert.Empty(spy.Loaded);
    }

    // ---------- Refus : rien ne bouge ----------

    [Theory]
    [InlineData(250, 0, 5)]     // InvalidAmount
    [InlineData(250, 150, 60)]  // DailyLimitExceeded
    [InlineData(50, 0, 60)]     // InsufficientFunds
    public void Un_refus_ne_touche_pas_au_module(int balance, int withdrawn, int amount)
    {
        var spy = new SpyDispenser(new MoneyDispenser(Full));
        var r = new CashMachine(spy).Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));

        Assert.NotEqual(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(balance, withdrawn), r.Account);
        Assert.Empty(spy.Dispensed);
        Assert.Empty(spy.Loaded);
        Assert.Equal(Full, spy.Stock);
    }

    [Fact]
    public void CannotDispense_ne_touche_pas_au_module()
    {
        var spy = new SpyDispenser(new MoneyDispenser(N(1, 0, 0)));
        var r = new CashMachine(spy).Execute(new AccountState(250, 0), new Operation.Withdraw(30));

        Assert.Equal(AtmStatus.CannotDispense, r.Status);
        Assert.Empty(spy.Dispensed);
        Assert.Empty(spy.Loaded);
    }

    // ---------- Sans mémoire ----------

    [Fact]
    public void La_machine_ne_garde_rien_entre_deux_appels()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        // Second appel avec un compte « neuf » : le plafond ne doit pas tenir compte du premier retrait.
        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(new AccountState(45, 200), second.Account);
    }

    [Fact]
    public void La_machine_lit_le_stock_courant_du_module()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50)).Status);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50)).Status);
    }
}
