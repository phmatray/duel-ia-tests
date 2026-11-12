using static Atm.Tests.Notes;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    private static (AtmResult Result, SpyDispenser Dispenser) Withdraw(
        int balance, int withdrawn, int amount, NoteCounts? stock = null, Denomination? jammed = null)
    {
        var dispenser = new SpyDispenser(stock ?? Full, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult result, SpyDispenser dispenser,
        int balance, int withdrawn, NoteCounts stock)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(balance, withdrawn), result.Account);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.SuccessfulPasses);
        Assert.Empty(dispenser.LoadCalls);
    }

    // --- Contrôle 1 : montant invalide ---

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_InvalidAmount(int amount)
    {
        var (result, dispenser) = Withdraw(250, 0, amount);
        AssertRefused(AtmStatus.InvalidAmount, result, dispenser, 250, 0, Full);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(200)]
    public void Montant_multiple_de_10_positif_est_accepte(int amount)
    {
        var (result, _) = Withdraw(250, 0, amount);
        Assert.Equal(AtmStatus.Ok, result.Status);
    }

    // --- Contrôle 2 : plafond du jour ---

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(190, 20)]
    [InlineData(200, 10)]
    public void Depasser_200_dans_la_journee_est_DailyLimitExceeded(int withdrawn, int amount)
    {
        var (result, dispenser) = Withdraw(250, withdrawn, amount);
        AssertRefused(AtmStatus.DailyLimitExceeded, result, dispenser, 250, withdrawn, Full);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_200_est_autorise(int withdrawn, int amount)
    {
        var (result, _) = Withdraw(250, withdrawn, amount);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(200, result.Account.WithdrawnToday);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond_du_jour()
    {
        // 90 + 110 = 200 : pile au plafond ; avec les frais on serait à 205.
        var (result, _) = Withdraw(250, 90, 110);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(135, 200), result.Account);
    }

    // --- Contrôle 3 : solde insuffisant ---

    [Theory]
    [InlineData(50, 60)]
    [InlineData(0, 10)]
    [InlineData(100, 110)] // 110 + 5 > 100
    [InlineData(110, 110)] // 110 + 5 > 110 : les frais comptent
    [InlineData(200, 200)] // 200 + 5 > 200
    public void Montant_plus_frais_superieur_au_solde_est_InsufficientFunds(int balance, int amount)
    {
        var (result, dispenser) = Withdraw(balance, 0, amount);
        AssertRefused(AtmStatus.InsufficientFunds, result, dispenser, balance, 0, Full);
    }

    [Theory]
    [InlineData(60, 60, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 0)]
    [InlineData(205, 200, 0)]
    public void Montant_plus_frais_egal_au_solde_est_autorise(int balance, int amount, int balanceAfter)
    {
        var (result, _) = Withdraw(balance, 0, amount);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(balanceAfter, result.Account.Balance);
    }

    // --- Contrôle 4 : combinaison impossible ---

    [Theory]
    [InlineData(0, 0, 0, null, 10)]
    [InlineData(5, 5, 0, null, 30)]   // pas de 10 : 30 est impossible
    [InlineData(5, 0, 0, null, 60)]
    [InlineData(1, 1, 1, null, 90)]   // 80 au maximum
    [InlineData(5, 5, 5, Denomination.Ten, 10)]
    [InlineData(1, 0, 5, Denomination.Fifty, 60)] // 10×5 = 50 seulement
    [InlineData(0, 5, 0, Denomination.Twenty, 20)]
    public void Aucune_combinaison_exacte_est_CannotDispense(int f, int t, int d, Denomination? jammed, int amount)
    {
        var stock = N(f, t, d);
        var (result, dispenser) = Withdraw(250, 0, amount, stock, jammed);
        AssertRefused(AtmStatus.CannotDispense, result, dispenser, 250, 0, stock);
    }

    // --- Ordre des contrôles ---

    [Fact]
    public void Ordre_InvalidAmount_avant_DailyLimitExceeded()
    {
        var (result, _) = Withdraw(0, 200, 5, N(0, 0, 0));
        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void Ordre_InvalidAmount_avant_tout_le_reste_meme_montant_negatif()
    {
        var (result, _) = Withdraw(0, 200, -10, N(0, 0, 0), Denomination.Fifty);
        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void Ordre_DailyLimitExceeded_avant_InsufficientFunds()
    {
        var (result, _) = Withdraw(0, 150, 60, N(0, 0, 0));
        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void Ordre_DailyLimitExceeded_avant_CannotDispense()
    {
        var (result, _) = Withdraw(250, 150, 60, N(0, 0, 0));
        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void Ordre_InsufficientFunds_avant_CannotDispense()
    {
        var (result, _) = Withdraw(10, 0, 30, N(0, 0, 0));
        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    // --- Frais ---

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int fee)
    {
        var (result, _) = Withdraw(250, 0, amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(250 - amount - fee, result.Account.Balance);
        Assert.Equal(amount, result.Account.WithdrawnToday);
    }

    // --- Billets disponibles : la cassette bloquée compte pour zéro ---

    [Theory]
    [InlineData(Denomination.Fifty, 100, 0, 5, 0)]   // 20×5
    [InlineData(Denomination.Twenty, 70, 1, 0, 2)]   // 50 + 10×2
    [InlineData(Denomination.Ten, 70, 1, 1, 0)]
    [InlineData(Denomination.Fifty, 50, 0, 2, 1)]
    [InlineData(Denomination.Ten, 90, 1, 2, 0)]
    public void La_cassette_bloquee_n_est_jamais_utilisee(Denomination jammed, int amount, int f, int t, int d)
    {
        var (result, dispenser) = Withdraw(250, 0, amount, Full, jammed);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(f, t, d), result.Notes);
        Assert.Equal(0, result.Notes.Of(jammed));
        Assert.Equal(Full - N(f, t, d), dispenser.Stock);
        Assert.All(dispenser.DispenseCalls, pass => Assert.Equal(0, pass.Of(jammed)));
    }

    // --- Combinaison au moins de billets ---

    [Theory]
    // stock f, t, d ; montant ; billets attendus f, t, d
    [InlineData(5, 5, 5, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 130, 2, 1, 1)]
    [InlineData(5, 5, 5, 200, 4, 0, 0)]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // le glouton (50 d'abord) échoue
    [InlineData(1, 4, 0, 80, 0, 4, 0)]   // idem
    [InlineData(1, 5, 0, 110, 1, 3, 0)]
    [InlineData(3, 5, 0, 160, 2, 3, 0)]  // 50×3 + 10 impossible
    [InlineData(0, 5, 5, 100, 0, 5, 0)]  // plutôt que 20×4 + 10×2
    [InlineData(1, 0, 5, 100, 1, 0, 5)]
    [InlineData(5, 0, 5, 100, 2, 0, 0)]  // plutôt que 50 + 10×5
    [InlineData(5, 1, 5, 120, 2, 1, 0)]
    [InlineData(0, 2, 5, 70, 0, 2, 3)]
    public void La_combinaison_est_celle_qui_compte_le_moins_de_billets(
        int sf, int st, int sd, int amount, int f, int t, int d)
    {
        var stock = N(sf, st, sd);
        var (result, dispenser) = Withdraw(250, 0, amount, stock);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(f, t, d), result.Notes);
        Assert.Equal(amount, result.Notes.Value);
        Assert.Equal(stock - N(f, t, d), dispenser.Stock);
    }

    // --- La sortie : passages de 4 billets au plus ---

    [Theory]
    [InlineData(5, 5, 5, 200)]  // 50×4 : un seul passage possible
    [InlineData(1, 5, 0, 150)]  // 50 + 20×5 : 6 billets
    [InlineData(0, 5, 5, 150)]  // 20×5 + 10×5 : 10 billets
    [InlineData(3, 5, 5, 200)]  // 50×3 + 20×2 + 10 : 6 billets
    [InlineData(0, 0, 5, 50)]   // 10×5
    public void Les_billets_sortent_en_passages_de_4_au_plus(int sf, int st, int sd, int amount)
    {
        var stock = N(sf, st, sd);
        var (result, dispenser) = Withdraw(250, 0, amount, stock);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(amount, result.Notes.Value);
        Assert.All(dispenser.DispenseCalls, pass =>
        {
            Assert.InRange(pass.Total, 1, 4);
            Assert.True(pass.Fifties >= 0 && pass.Twenties >= 0 && pass.Tens >= 0);
        });
        var sum = dispenser.SuccessfulPasses.Aggregate(NoteCounts.None, (a, b) => a + b);
        Assert.Equal(result.Notes, sum);
        Assert.Equal(dispenser.DispenseCalls.Count, dispenser.SuccessfulPasses.Count);
        Assert.Equal(stock - result.Notes, dispenser.Stock);
    }

    [Fact]
    public void Un_retrait_de_plus_de_4_billets_reussit_avec_le_vrai_module()
    {
        var dispenser = new MoneyDispenser(N(0, 5, 5));
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(0, 5, 5), result.Notes);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(95, 150), result.Account);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    // --- Le succès ---

    [Fact]
    public void Succes_met_a_jour_compte_et_stock_sans_toucher_Load()
    {
        var (result, dispenser) = Withdraw(200, 20, 130);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(2, 1, 1), result.Notes);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(65, 150), result.Account);
        Assert.Equal(N(3, 4, 4), dispenser.Stock);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void Retrait_qui_vide_tout_le_stock_disponible()
    {
        var stock = N(1, 1, 1);
        var (result, dispenser) = Withdraw(80, 0, 80, stock);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(0, 80), result.Account);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }
}
