namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    private static (AtmResult Result, RecordingDispenser Dispenser) Run(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount)
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawnToday), new Operation.Withdraw(amount));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmResult result, RecordingDispenser dispenser, AtmStatus status,
        AccountState account, NoteCounts stock)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
    }

    // ---- Règle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-5)]
    [InlineData(15)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_InvalidAmount(int amount)
    {
        var (result, dispenser) = Run(250, 0, Full, null, amount);
        AssertRefused(result, dispenser, AtmStatus.InvalidAmount, new(250, 0), Full);
    }

    [Fact]
    public void InvalidAmount_passe_avant_le_plafond_le_solde_et_les_billets()
    {
        // Plafond atteint, solde nul, aucun billet : c'est quand même le montant qui est refusé en premier.
        var (result, dispenser) = Run(0, 200, NoteCounts.None, null, -10);
        AssertRefused(result, dispenser, AtmStatus.InvalidAmount, new(0, 200), NoteCounts.None);
    }

    // ---- Règle 2 : plafond du jour ----

    [Theory]
    [InlineData(150, 60)]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(200, 10)]
    [InlineData(110, 100)]
    public void Depasser_200_dans_la_journee_est_DailyLimitExceeded(int withdrawnToday, int amount)
    {
        var (result, dispenser) = Run(250, withdrawnToday, Full, null, amount);
        AssertRefused(result, dispenser, AtmStatus.DailyLimitExceeded, new(250, withdrawnToday), Full);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(100, 100)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_200_est_permis(int withdrawnToday, int amount)
    {
        var (result, _) = Run(250, withdrawnToday, Full, null, amount);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(withdrawnToday + amount, result.Account.WithdrawnToday);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : permis, alors que 90 + 110 + 5 dépasserait 200.
        var (result, _) = Run(250, 90, Full, null, 110);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(200, result.Account.WithdrawnToday);
        Assert.Equal(135, result.Account.Balance);
    }

    [Fact]
    public void DailyLimitExceeded_passe_avant_le_solde_et_les_billets()
    {
        var (result, dispenser) = Run(0, 150, NoteCounts.None, null, 60);
        AssertRefused(result, dispenser, AtmStatus.DailyLimitExceeded, new(0, 150), NoteCounts.None);
    }

    // ---- Règle 3 : solde ----

    [Theory]
    [InlineData(50, 60)]
    [InlineData(0, 10)]
    [InlineData(110, 110)] // 110 + 5 de frais > 110
    [InlineData(200, 200)] // 200 + 5 de frais > 200
    [InlineData(100, 110)]
    public void Montant_plus_frais_au_dela_du_solde_est_InsufficientFunds(int balance, int amount)
    {
        var (result, dispenser) = Run(balance, 0, Full, null, amount);
        AssertRefused(result, dispenser, AtmStatus.InsufficientFunds, new(balance, 0), Full);
    }

    [Theory]
    [InlineData(60, 60, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 5)]
    [InlineData(205, 200, 5)]
    public void Montant_plus_frais_egal_au_solde_est_permis(int balance, int amount, int fee)
    {
        var (result, _) = Run(balance, 0, Full, null, amount);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(0, result.Account.Balance);
    }

    [Fact]
    public void InsufficientFunds_passe_avant_les_billets()
    {
        var (result, dispenser) = Run(50, 0, NoteCounts.None, null, 60);
        AssertRefused(result, dispenser, AtmStatus.InsufficientFunds, new(50, 0), NoteCounts.None);
    }

    // ---- Frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var (result, _) = Run(250, 0, Full, null, amount);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(250 - amount - expectedFee, result.Account.Balance);
        Assert.Equal(amount, result.Account.WithdrawnToday);
    }

    // ---- Règle 4 : combinaison impossible ----

    [Theory]
    [InlineData(0, 0, 0, null, 10)]
    [InlineData(1, 0, 0, Denomination.Fifty, 50)]  // seule cassette utile bloquée
    [InlineData(0, 5, 0, null, 30)]                 // 30 impossible avec des 20
    [InlineData(5, 0, 0, null, 60)]                 // pas de 10
    [InlineData(1, 1, 1, null, 90)]                 // au plus 80
    [InlineData(5, 5, 5, Denomination.Ten, 10)]     // 10 € uniquement en 10, bloquée
    [InlineData(5, 5, 5, Denomination.Ten, 30)]
    public void Aucune_combinaison_exacte_est_CannotDispense(int f, int t, int d, Denomination? jammed, int amount)
    {
        var stock = new NoteCounts(f, t, d);
        var (result, dispenser) = Run(250, 0, stock, jammed, amount);
        AssertRefused(result, dispenser, AtmStatus.CannotDispense, new(250, 0), stock);
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Fact]
    public void La_cassette_bloquee_compte_pour_zero()
    {
        // Sans blocage : 50×1 10×1. Avec le 50 bloqué : 20×3.
        var stock = new NoteCounts(5, 5, 5);
        var (result, dispenser) = Run(250, 0, stock, Denomination.Fifty, 60);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(0, 3, 0), result.Notes);
        Assert.Equal(new NoteCounts(5, 2, 5), dispenser.Stock);
    }

    // ---- Combinaison : le moins de billets ----

    [Theory]
    [InlineData(5, 5, 5, null, 60, 1, 0, 1)]
    [InlineData(1, 3, 0, null, 60, 0, 3, 0)]   // 50 inutilisable sans 10 : 20×3
    [InlineData(1, 4, 0, null, 80, 0, 4, 0)]
    [InlineData(5, 5, 5, null, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, null, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, null, 200, 4, 0, 0)]
    [InlineData(5, 5, 5, null, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, Denomination.Twenty, 40, 0, 0, 4)]
    [InlineData(5, 5, 5, Denomination.Twenty, 90, 1, 0, 4)]
    [InlineData(5, 5, 5, Denomination.Fifty, 90, 0, 4, 1)]   // 5 billets
    [InlineData(5, 5, 5, Denomination.Fifty, 100, 0, 5, 0)]  // 5 billets d'une coupure
    [InlineData(1, 5, 5, null, 140, 1, 4, 1)]                // 6 billets
    [InlineData(0, 5, 5, null, 150, 0, 5, 5)]                // 10 billets
    [InlineData(5, 0, 5, null, 200, 4, 0, 0)]
    [InlineData(2, 5, 0, null, 160, 2, 3, 0)]
    public void La_combinaison_retenue_compte_le_moins_de_billets(
        int f, int t, int d, Denomination? jammed, int amount, int ef, int et, int ed)
    {
        var stock = new NoteCounts(f, t, d);
        var expected = new NoteCounts(ef, et, ed);
        var (result, dispenser) = Run(250, 0, stock, jammed, amount);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    // ---- Sortie : passages de 4 billets au plus ----

    [Theory]
    [InlineData(5, 5, 5, Denomination.Fifty, 90)]   // 20×4 10×1
    [InlineData(5, 5, 5, Denomination.Fifty, 100)]  // 20×5
    [InlineData(1, 5, 5, null, 140)]                // 50×1 20×4 10×1
    [InlineData(0, 5, 5, null, 150)]                // 20×5 10×5
    [InlineData(5, 5, 5, Denomination.Twenty, 90)]  // 50×1 10×4
    [InlineData(0, 3, 2, null, 80)]                 // 20×3 10×2
    [InlineData(5, 5, 5, null, 200)]                // 50×4
    public void Les_billets_sortent_en_passages_de_4_billets_au_plus(
        int f, int t, int d, Denomination? jammed, int amount)
    {
        var stock = new NoteCounts(f, t, d);
        var expected = Domain.FewestNotes(amount, stock, jammed)!.Value;
        var (result, dispenser) = Run(250, 0, stock, jammed, amount);

        Assert.All(dispenser.DispenseCalls, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(expected, dispenser.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(stock - expected, dispenser.Stock);
        Assert.Empty(dispenser.LoadCalls);
    }

    // ---- Succès ----

    [Fact]
    public void Succes_met_a_jour_le_compte_et_le_stock()
    {
        var (result, dispenser) = Run(180, 40, Full, null, 70);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(1, 1, 0), result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(110, 110), result.Account);
        Assert.Equal(new NoteCounts(4, 4, 5), dispenser.Stock);
    }

    // ---- Sans mémoire ----

    [Fact]
    public void La_machine_ne_depend_que_du_compte_de_l_operation_et_du_module()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(5, 5, 5));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(100));
        Assert.Equal(AtmStatus.Ok, first.Status);

        // Un deuxième appel avec un compte « neuf » ne garde rien du premier retrait (plafond, solde).
        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(100));
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(new AccountState(150, 100), second.Account);

        // Le résultat est le même qu'avec une machine neuve sur un module dans le même état.
        var fresh = new CashMachine(new MoneyDispenser(dispenser.Stock)).Execute(new AccountState(250, 0), new Operation.Withdraw(100));
        var third = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(100));
        Assert.Equal(fresh, third);
    }
}
