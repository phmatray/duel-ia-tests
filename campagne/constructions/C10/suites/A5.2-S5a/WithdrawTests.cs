namespace Atm.Tests;

public class WithdrawTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static readonly NoteCounts Full = new(5, 5, 5);

    private static (AtmResult Result, SpyDispenser Module) Withdraw(
        int balance, int withdrawn, NoteCounts stock, int amount, Denomination? jammed = null)
    {
        var module = new SpyDispenser(stock, jammed);
        var result = new CashMachine(module).Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));
        return (result, module);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult result, AccountState account, SpyDispenser module, NoteCounts stock)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, module.Stock);
        Assert.DoesNotContain(module.DispenseCalls, c => c.Result);
    }

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (r, m) = Withdraw(250, 0, Full, amount);
        AssertRefused(AtmStatus.InvalidAmount, r, new AccountState(250, 0), m, Full);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_prime_sur_tous_les_autres_refus(int amount)
    {
        // Plafond atteint, solde nul, stock vide : c'est quand même InvalidAmount.
        var (r, m) = Withdraw(0, 200, N(0, 0, 0), amount);
        AssertRefused(AtmStatus.InvalidAmount, r, new AccountState(0, 200), m, N(0, 0, 0));
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(190, 20)]
    public void Depasser_200_dans_la_journee_est_refuse(int withdrawn, int amount)
    {
        var (r, m) = Withdraw(250, withdrawn, Full, amount);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, new AccountState(250, withdrawn), m, Full);
    }

    [Theory]
    [InlineData(150, 50)]
    [InlineData(0, 200)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_200_est_permis(int withdrawn, int amount)
    {
        var (r, _) = Withdraw(250, withdrawn, Full, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(200, r.Account.WithdrawnToday);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : permis, même si 90 + 110 + 5 > 200.
        var (r, _) = Withdraw(250, 90, Full, 110);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(135, 200), r.Account);
    }

    [Fact]
    public void Plafond_prime_sur_solde_insuffisant_et_billets_indisponibles()
    {
        var (r, m) = Withdraw(0, 150, N(0, 0, 0), 60);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, new AccountState(0, 150), m, N(0, 0, 0));
    }

    // ---- Contrôle 3 : solde insuffisant ----

    [Theory]
    [InlineData(50, 60)]
    [InlineData(0, 10)]
    [InlineData(105, 110)] // montant > solde
    [InlineData(110, 110)] // montant = solde mais frais en plus
    [InlineData(200, 200)]
    [InlineData(95, 100)]
    public void Montant_plus_frais_au_dela_du_solde_est_refuse(int balance, int amount)
    {
        var (r, m) = Withdraw(balance, 0, Full, amount);
        AssertRefused(AtmStatus.InsufficientFunds, r, new AccountState(balance, 0), m, Full);
    }

    [Theory]
    [InlineData(100, 100, 0)] // pas de frais à 100
    [InlineData(115, 110, 0)] // montant + frais = solde
    [InlineData(205, 200, 0)]
    [InlineData(10, 10, 0)]
    public void Montant_plus_frais_egal_au_solde_est_permis(int balance, int amount, int expectedBalance)
    {
        var (r, _) = Withdraw(balance, 0, Full, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedBalance, r.Account.Balance);
    }

    [Fact]
    public void Solde_insuffisant_prime_sur_billets_indisponibles()
    {
        var (r, m) = Withdraw(10, 0, N(0, 0, 0), 20);
        AssertRefused(AtmStatus.InsufficientFunds, r, new AccountState(10, 0), m, N(0, 0, 0));
    }

    // ---- Les frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_euros_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var (r, _) = Withdraw(250, 0, Full, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedFee, r.Fee);
        Assert.Equal(250 - amount - expectedFee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    // ---- Contrôle 4 : impossible de servir ----

    [Fact]
    public void Stock_vide_ne_peut_rien_servir()
    {
        var (r, m) = Withdraw(250, 0, N(0, 0, 0), 10);
        AssertRefused(AtmStatus.CannotDispense, r, new AccountState(250, 0), m, N(0, 0, 0));
    }

    [Theory]
    [InlineData(1, 5, 0, 30)]  // 30 impossible avec des 50 et des 20
    [InlineData(1, 5, 0, 10)]
    [InlineData(0, 0, 5, 60)]  // stock insuffisant : 50 € au plus
    [InlineData(1, 0, 0, 100)]
    [InlineData(2, 0, 0, 60)]
    [InlineData(0, 5, 0, 110)]
    public void Aucune_combinaison_exacte_est_refusee(int f, int t, int d, int amount)
    {
        var (r, m) = Withdraw(250, 0, N(f, t, d), amount);
        AssertRefused(AtmStatus.CannotDispense, r, new AccountState(250, 0), m, N(f, t, d));
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
        var (r, m) = Withdraw(250, 0, stock, amount, jammed);
        AssertRefused(AtmStatus.CannotDispense, r, new AccountState(250, 0), m, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 100, 0, 5, 0)]
    [InlineData(Denomination.Twenty, 60, 1, 0, 1)]
    [InlineData(Denomination.Ten, 70, 1, 1, 0)]
    [InlineData(Denomination.Fifty, 50, 0, 2, 1)]
    public void Avec_une_cassette_bloquee_les_autres_servent(Denomination jammed, int amount, int f, int t, int d)
    {
        var (r, m) = Withdraw(250, 0, Full, amount, jammed);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, d), r.Notes);
        Assert.Equal(0, r.Notes.Of(jammed));
        Assert.Equal(Full - N(f, t, d), m.Stock);
    }

    // ---- La combinaison : le moins de billets ----

    [Theory]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]   // 50+10 plutôt que 20×3
    [InlineData(5, 5, 5, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 40, 0, 2, 0)]   // 20×2 plutôt que 10×4
    [InlineData(5, 5, 5, 200, 4, 0, 0)]
    [InlineData(5, 5, 5, 130, 2, 1, 1)]
    [InlineData(5, 5, 0, 60, 0, 3, 0)]   // sans 10 : 20×3
    [InlineData(5, 5, 0, 110, 1, 3, 0)]  // sans 10 : 50 + 20×3
    [InlineData(1, 5, 5, 150, 1, 5, 0)]  // un seul 50 disponible
    [InlineData(0, 2, 5, 70, 0, 2, 3)]   // peu de 20
    [InlineData(1, 5, 5, 200, 1, 5, 5)]  // 11 billets
    public void Rend_la_combinaison_qui_compte_le_moins_de_billets(
        int sf, int st, int sd, int amount, int f, int t, int d)
    {
        var (r, m) = Withdraw(250, 0, N(sf, st, sd), amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(f, t, d), r.Notes);
        Assert.Equal(N(sf, st, sd) - N(f, t, d), m.Stock);
    }

    // ---- La sortie : passages d'au plus 4 billets ----

    [Theory]
    [InlineData(0, 4, 1, 90)]   // 20×4 + 10×1 : 5 billets
    [InlineData(5, 5, 5, 150)]  // 50×3 : 3 billets
    [InlineData(2, 5, 5, 150)]  // 50×2 + 20×2 + 10×1 : 5 billets
    [InlineData(1, 5, 5, 140)]  // 50×1 + 20×4 + 10×1 : 6 billets
    [InlineData(0, 5, 5, 150)]  // 20×5 + 10×5 : 10 billets
    [InlineData(1, 5, 5, 200)]  // 11 billets
    [InlineData(0, 5, 3, 130)]  // 20×5 + 10×3 : 8 billets
    public void Les_billets_sortent_en_passages_de_4_au_plus(int sf, int st, int sd, int amount)
    {
        var stock = N(sf, st, sd);
        var (r, m) = Withdraw(250, 0, stock, amount);

        var expected = Oracle.Fewest(amount, stock)!.Value;
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expected, r.Notes);
        Assert.All(m.DispenseCalls, c =>
        {
            Assert.True(c.Result, $"le module a refusé le passage {c.Notes}");
            Assert.InRange(c.Notes.Total, 1, 4);
        });
        Assert.Equal(expected, m.DispenseCalls.Aggregate(NoteCounts.None, (s, c) => s + c.Notes));
        Assert.Equal(stock - expected, m.Stock);
    }

    // ---- Le succès ----

    [Fact]
    public void Succes_met_a_jour_le_compte_et_le_stock()
    {
        var (r, m) = Withdraw(200, 40, N(3, 2, 1), 120);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 1, 0), 5, new AccountState(75, 160)), r);
        Assert.Equal(N(1, 1, 1), m.Stock);
        Assert.Equal(N(2, 1, 0), m.DispenseCalls.Where(c => c.Result).Aggregate(NoteCounts.None, (s, c) => s + c.Notes));
    }

    [Fact]
    public void La_machine_est_sans_memoire_entre_deux_appels()
    {
        // Même compte, même opération, même état du module : même résultat, quelle que soit l'histoire.
        var module = new MoneyDispenser(Full);
        var machine = new CashMachine(module);
        machine.Execute(new AccountState(250, 0), new Operation.Withdraw(5));          // refus
        machine.Execute(new AccountState(250, 0), new Operation.Withdraw(60));         // succès : stock 4,5,4
        var reused = machine.Execute(new AccountState(100, 0), new Operation.Withdraw(70));

        var fresh = new CashMachine(new MoneyDispenser(N(4, 5, 4))).Execute(new AccountState(100, 0), new Operation.Withdraw(70));

        Assert.Equal(fresh, reused);
    }
}
