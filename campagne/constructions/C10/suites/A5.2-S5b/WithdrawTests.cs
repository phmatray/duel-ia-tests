using Atm.Tests.Support;
using static Atm.Tests.Support.Notes;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    private static (AtmResult Result, RecordingDispenser Dispenser) Withdraw(
        int balance, int withdrawn, int amount, NoteCounts? stock = null, Denomination? jammed = null)
    {
        var dispenser = new RecordingDispenser(stock ?? Full, jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult r, RecordingDispenser d, AccountState account, NoteCounts stock)
    {
        Assert.Equal(expected, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(stock, d.Stock);
        Assert.DoesNotContain(d.DispenseCalls, c => c.Ok);
        Assert.DoesNotContain(d.LoadCalls, c => c.Ok);
    }

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-5)]
    [InlineData(15)]
    [InlineData(105)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (r, d) = Withdraw(250, 0, amount);

        AssertRefused(AtmStatus.InvalidAmount, r, d, new AccountState(250, 0), Full);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(200)]
    public void Multiple_de_10_positif_est_accepte(int amount)
    {
        var (r, _) = Withdraw(250, 0, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
    }

    [Fact]
    public void Montant_invalide_passe_avant_le_plafond_le_solde_et_les_billets()
    {
        var (r, d) = Withdraw(0, 200, -10, N(0, 0, 0), Denomination.Ten);

        AssertRefused(AtmStatus.InvalidAmount, r, d, new AccountState(0, 200), N(0, 0, 0));
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    public void Depasser_200_dans_la_journee_est_refuse(int withdrawn, int amount)
    {
        var (r, d) = Withdraw(250, withdrawn, amount);

        AssertRefused(AtmStatus.DailyLimitExceeded, r, d, new AccountState(250, withdrawn), Full);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(100, 100)]
    [InlineData(190, 10)]
    [InlineData(150, 50)]
    public void Atteindre_exactement_200_est_permis(int withdrawn, int amount)
    {
        var (r, _) = Withdraw(250, withdrawn, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(200, r.Account.WithdrawnToday);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : pile au plafond, les 5 € de frais n'y entrent pas.
        var (r, _) = Withdraw(250, 90, 110);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(135, 200), r.Account);
    }

    [Fact]
    public void Plafond_passe_avant_le_solde_et_les_billets()
    {
        var (r, d) = Withdraw(0, 150, 60, N(0, 0, 0));

        AssertRefused(AtmStatus.DailyLimitExceeded, r, d, new AccountState(0, 150), N(0, 0, 0));
    }

    // ---- Contrôle 3 : solde ----

    [Theory]
    [InlineData(50, 60)]
    [InlineData(0, 10)]
    [InlineData(95, 100)]
    [InlineData(110, 110)] // 110 + 5 de frais > 110
    [InlineData(114, 110)]
    [InlineData(200, 200)] // 200 + 5 de frais > 200
    public void Montant_plus_frais_superieur_au_solde_est_refuse(int balance, int amount)
    {
        var (r, d) = Withdraw(balance, 0, amount);

        AssertRefused(AtmStatus.InsufficientFunds, r, d, new AccountState(balance, 0), Full);
    }

    [Theory]
    [InlineData(60, 60, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 0)]
    [InlineData(205, 200, 0)]
    public void Solde_exactement_suffisant_est_permis(int balance, int amount, int expectedBalance)
    {
        var (r, _) = Withdraw(balance, 0, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedBalance, r.Account.Balance);
    }

    [Fact]
    public void Solde_passe_avant_les_billets()
    {
        var (r, d) = Withdraw(0, 0, 10, N(0, 0, 0));

        AssertRefused(AtmStatus.InsufficientFunds, r, d, new AccountState(0, 0), N(0, 0, 0));
    }

    // ---- Les frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int fee)
    {
        var (r, _) = Withdraw(250, 0, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(250 - amount - fee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    // ---- Contrôle 4 : combinaison de billets ----

    [Theory]
    [InlineData(10, 0, 0, 0)]       // stock vide
    [InlineData(30, 0, 1, 0)]       // 20 seul ne fait pas 30
    [InlineData(10, 5, 5, 0)]       // pas de 10
    [InlineData(30, 5, 0, 2)]       // 10×2 = 20 < 30, 50 trop gros
    [InlineData(200, 1, 2, 3)]      // valeur totale 120 < 200
    [InlineData(70, 0, 3, 0)]       // 20×3 = 60, impossible de faire 70
    public void Aucune_combinaison_exacte_donne_CannotDispense(int amount, int f, int t, int n)
    {
        var stock = N(f, t, n);
        var (r, d) = Withdraw(250, 0, amount, stock);

        AssertRefused(AtmStatus.CannotDispense, r, d, new AccountState(250, 0), stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 40, 0, 5, 0)]
    [InlineData(Denomination.Ten, 10, 5, 5, 5)]
    [InlineData(Denomination.Ten, 30, 0, 1, 5)]
    [InlineData(Denomination.Twenty, 60, 1, 5, 0)]
    public void La_cassette_bloquee_compte_pour_zero(Denomination jammed, int amount, int f, int t, int n)
    {
        var stock = N(f, t, n);
        var (r, d) = Withdraw(250, 0, amount, stock, jammed);

        AssertRefused(AtmStatus.CannotDispense, r, d, new AccountState(250, 0), stock);
    }

    [Theory]
    [InlineData(null, 60, 1, 3, 0, 0, 3, 0)]   // pas de « 50 d'abord » : 50 + 10 impossible
    [InlineData(null, 60, 1, 3, 1, 1, 0, 1)]   // 50 + 10 (2 billets) plutôt que 20×3
    [InlineData(null, 80, 1, 4, 0, 0, 4, 0)]   // 20×4 (50 + 30 impossible)
    [InlineData(null, 80, 1, 4, 3, 1, 1, 1)]   // 50+20+10 plutôt que 20×4 ou 50+10×3
    [InlineData(null, 80, 1, 0, 3, 1, 0, 3)]   // 50 + 10×3
    [InlineData(null, 100, 5, 5, 5, 2, 0, 0)]
    [InlineData(null, 100, 1, 5, 5, 1, 2, 1)]
    [InlineData(null, 100, 0, 5, 5, 0, 5, 0)]
    [InlineData(null, 110, 1, 3, 0, 1, 3, 0)]
    [InlineData(null, 90, 0, 4, 5, 0, 4, 1)]
    [InlineData(null, 40, 0, 0, 5, 0, 0, 4)]
    [InlineData(null, 200, 4, 5, 5, 4, 0, 0)]
    [InlineData(null, 200, 2, 5, 0, 2, 5, 0)]
    [InlineData(null, 190, 5, 5, 5, 3, 2, 0)]
    [InlineData(null, 170, 3, 1, 0, 3, 1, 0)]
    [InlineData(Denomination.Fifty, 100, 5, 5, 5, 0, 5, 0)]
    [InlineData(Denomination.Twenty, 70, 1, 5, 5, 1, 0, 2)]
    [InlineData(Denomination.Ten, 70, 1, 5, 5, 1, 1, 0)]
    [InlineData(Denomination.Ten, 60, 1, 5, 5, 0, 3, 0)]
    public void La_combinaison_compte_le_moins_de_billets(
        Denomination? jammed, int amount, int f, int t, int n, int ef, int et, int en)
    {
        var stock = N(f, t, n);
        var (r, d) = Withdraw(250, 0, amount, stock, jammed);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(ef, et, en), r.Notes);
        Assert.Equal(stock - N(ef, et, en), d.Stock);
    }

    // ---- La sortie ----

    [Theory]
    [InlineData(190, 5, 5, 5)]   // 50×3 20×2 : 5 billets
    [InlineData(200, 2, 5, 0)]   // 50×2 20×5 : 7 billets
    [InlineData(180, 3, 1, 1)]   // 50×3 20×1 10×1 : 5 billets
    [InlineData(150, 0, 5, 5)]   // 20×5 10×5 : 10 billets
    [InlineData(40, 0, 0, 5)]    // 10×4 : 4 billets, un passage suffit
    public void Les_billets_sortent_par_passages_de_4_au_plus(int amount, int f, int t, int n)
    {
        var stock = N(f, t, n);
        var (r, d) = Withdraw(250, 0, amount, stock);

        Assert.All(d.DispenseCalls, c => Assert.InRange(c.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.All(d.DispenseCalls, c => Assert.True(c.Ok, $"passage refusé par le module : {Show(c.Notes)}"));

        var sum = d.DispenseCalls.Aggregate(NoteCounts.None, (acc, c) => acc + c.Notes);
        Assert.Equal(r.Notes, sum);
        Assert.Equal(stock - r.Notes, d.Stock);
        if (r.Status == AtmStatus.Ok)
        {
            Assert.Equal(amount, r.Notes.Value);
        }
    }

    [Fact]
    public void Dix_billets_sortent_et_le_retrait_reussit()
    {
        var (r, d) = Withdraw(250, 0, 150, N(0, 5, 5));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 5, 5), r.Notes);
        Assert.Equal(N(0, 0, 0), d.Stock);
        Assert.True(d.DispenseCalls.Count >= 3);
    }

    [Fact]
    public void Le_retrait_ne_charge_jamais_de_billets()
    {
        var (_, d) = Withdraw(250, 0, 190);

        Assert.Empty(d.LoadCalls);
    }

    // ---- Le succès ----

    [Fact]
    public void Succes_met_a_jour_solde_et_retrait_du_jour()
    {
        var (r, d) = Withdraw(200, 40, 130, N(2, 2, 2));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(2, 1, 1), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(65, 170), r.Account);
        Assert.Equal(N(0, 1, 1), d.Stock);
    }
}
