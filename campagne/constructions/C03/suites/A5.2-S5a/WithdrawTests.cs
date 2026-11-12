using static Atm.Tests.Fixture;

namespace Atm.Tests;

public class WithdrawTests
{
    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var account = Account(250, 0);
        var (result, dispenser) = Withdraw(account, amount, Full);

        AssertRefused(AtmStatus.InvalidAmount, result, account, dispenser, Full);
    }

    [Fact]
    public void Montant_invalide_passe_avant_le_plafond_le_solde_et_les_billets()
    {
        var account = Account(0, 200);
        var stock = Notes(0, 0, 0);
        var (result, dispenser) = Withdraw(account, -10, stock, Denomination.Ten);

        AssertRefused(AtmStatus.InvalidAmount, result, account, dispenser, stock);
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_le_plafond_est_permis(int withdrawn, int amount)
    {
        var (result, _) = Withdraw(Account(250, withdrawn), amount, Full);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(200, result.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(200, 10)]
    [InlineData(150, 60)]
    public void Depasser_le_plafond_est_refuse(int withdrawn, int amount)
    {
        var account = Account(250, withdrawn);
        var (result, dispenser) = Withdraw(account, amount, Full);

        AssertRefused(AtmStatus.DailyLimitExceeded, result, account, dispenser, Full);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : pile au plafond, les 5 € de frais n'y entrent pas.
        var (result, _) = Withdraw(Account(250, 90), 110, Full);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Account(135, 200), result.Account);
    }

    [Fact]
    public void Plafond_passe_avant_le_solde_et_les_billets()
    {
        var account = Account(0, 200);
        var stock = Notes(0, 0, 0);
        var (result, dispenser) = Withdraw(account, 10, stock);

        AssertRefused(AtmStatus.DailyLimitExceeded, result, account, dispenser, stock);
    }

    // ---- Contrôle 3 : solde ----

    [Theory]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    public void Solde_couvrant_exactement_montant_et_frais_suffit(int balance, int amount)
    {
        var (result, _) = Withdraw(Account(balance, 0), amount, Full);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Account.Balance);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(200, 200)]
    public void Solde_inferieur_a_montant_plus_frais_est_refuse(int balance, int amount)
    {
        var account = Account(balance, 0);
        var (result, dispenser) = Withdraw(account, amount, Full);

        AssertRefused(AtmStatus.InsufficientFunds, result, account, dispenser, Full);
    }

    [Fact]
    public void Solde_passe_avant_les_billets()
    {
        var account = Account(0, 0);
        var stock = Notes(0, 0, 0);
        var (result, dispenser) = Withdraw(account, 50, stock);

        AssertRefused(AtmStatus.InsufficientFunds, result, account, dispenser, stock);
    }

    // ---- Frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var (result, _) = Withdraw(Account(250, 0), amount, Full);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(Account(250 - amount - expectedFee, amount), result.Account);
    }

    // ---- Contrôle 4 : combinaison de billets ----

    [Fact]
    public void Stock_vide_ne_peut_rien_distribuer()
    {
        var account = Account(250, 0);
        var stock = Notes(0, 0, 0);
        var (result, dispenser) = Withdraw(account, 10, stock);

        AssertRefused(AtmStatus.CannotDispense, result, account, dispenser, stock);
    }

    [Fact]
    public void Montant_impossible_a_composer_avec_les_coupures_presentes()
    {
        // Seulement des 20 et des 50 : 30 est impossible.
        var account = Account(250, 0);
        var stock = Notes(5, 5, 0);
        var (result, dispenser) = Withdraw(account, 30, stock);

        AssertRefused(AtmStatus.CannotDispense, result, account, dispenser, stock);
    }

    [Fact]
    public void Montant_superieur_au_stock_disponible()
    {
        var account = Account(250, 0);
        var stock = Notes(1, 1, 1);
        var (result, dispenser) = Withdraw(account, 90, stock);

        AssertRefused(AtmStatus.CannotDispense, result, account, dispenser, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50)]
    [InlineData(Denomination.Twenty, 20)]
    [InlineData(Denomination.Ten, 10)]
    public void Cassette_bloquee_compte_pour_zero(Denomination jammed, int amount)
    {
        var account = Account(250, 0);
        var stock = jammed switch
        {
            Denomination.Fifty => Notes(5, 0, 0),
            Denomination.Twenty => Notes(0, 5, 0),
            _ => Notes(0, 0, 5),
        };
        var (result, dispenser) = Withdraw(account, amount, stock, jammed);

        AssertRefused(AtmStatus.CannotDispense, result, account, dispenser, stock);
    }

    [Fact]
    public void Cassette_bloquee_n_est_jamais_utilisee()
    {
        // Sans blocage : 50×1. Avec les 50 bloqués : 20×2 10×1.
        var (result, dispenser) = Withdraw(Account(250, 0), 50, Full, Denomination.Fifty);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(0, 2, 1), result.Notes);
        Assert.Equal(Notes(5, 3, 4), dispenser.Stock);
    }

    [Theory]
    [InlineData(10, 0, 0, 1)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(30, 0, 1, 1)]
    [InlineData(40, 0, 2, 0)]
    [InlineData(50, 1, 0, 0)]
    [InlineData(60, 1, 0, 1)]
    [InlineData(70, 1, 1, 0)]
    [InlineData(90, 1, 2, 0)]
    [InlineData(100, 2, 0, 0)]
    [InlineData(130, 2, 1, 1)]
    [InlineData(200, 4, 0, 0)]
    public void Stock_plein_donne_la_combinaison_au_moins_de_billets(int amount, int f, int t, int n)
    {
        var (result, dispenser) = Withdraw(Account(250, 0), amount, Full);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(f, t, n), result.Notes);
        Assert.Equal(Full - Notes(f, t, n), dispenser.Stock);
    }

    [Theory]
    // montant, stock (50, 20, 10), combinaison attendue (50, 20, 10)
    [InlineData(60, 0, 5, 5, 0, 3, 0)]   // 20×3 plutôt que 20×2 10×2 ou 10×6
    [InlineData(60, 1, 5, 0, 0, 3, 0)]   // pas de 10 : 50 inutilisable
    [InlineData(100, 1, 5, 5, 1, 2, 1)]  // un seul 50
    [InlineData(70, 0, 1, 5, 0, 1, 5)]   // 6 billets, plus de 4 : plusieurs passages
    [InlineData(100, 0, 5, 0, 0, 5, 0)]  // 5 billets de 20
    public void Stock_contraint_donne_la_combinaison_au_moins_de_billets(
        int amount, int sf, int st, int sn, int f, int t, int n)
    {
        var stock = Notes(sf, st, sn);
        var (result, dispenser) = Withdraw(Account(250, 0), amount, stock);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(f, t, n), result.Notes);
        Assert.Equal(stock - Notes(f, t, n), dispenser.Stock);
    }

    // ---- Sortie en plusieurs passages (4 billets au plus par passage) ----

    [Theory]
    // montant, stock (50, 20, 10), combinaison attendue (50, 20, 10)
    [InlineData(80, 0, 3, 5, 0, 3, 2)]    // 5 billets : 20×3 10×2
    [InlineData(90, 0, 4, 1, 0, 4, 1)]    // 5 billets : 20×4 10×1
    [InlineData(140, 1, 5, 5, 1, 4, 1)]   // 6 billets : 50×1 20×4 10×1
    [InlineData(150, 0, 5, 5, 0, 5, 5)]   // 10 billets : 20×5 10×5
    [InlineData(200, 2, 5, 0, 2, 5, 0)]   // 7 billets : 50×2 20×5
    [InlineData(180, 5, 5, 5, 3, 1, 1)]   // 5 billets, stock plein : 50×3 20×1 10×1
    [InlineData(190, 5, 5, 5, 3, 2, 0)]   // 5 billets, stock plein : 50×3 20×2
    public void Retrait_de_plus_de_4_billets_sort_en_plusieurs_passages(
        int amount, int sf, int st, int sn, int f, int t, int n)
    {
        var stock = Notes(sf, st, sn);
        var (result, dispenser) = Withdraw(Account(250, 0), amount, stock);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(f, t, n), result.Notes);
        Assert.Equal(stock - Notes(f, t, n), dispenser.Stock);
        Assert.Equal(Account(250 - amount - (amount > 100 ? 5 : 0), amount), result.Account);
    }

    // ---- Succès ----

    [Fact]
    public void Succes_met_a_jour_le_compte_et_sort_les_billets_du_module()
    {
        var (result, dispenser) = Withdraw(Account(200, 40), 150, Full);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(3, 0, 0), result.Notes);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Account(45, 190), result.Account);
        Assert.Equal(Notes(2, 5, 5), dispenser.Stock);
    }

    // ---- Machine sans mémoire ----

    [Fact]
    public void Un_refus_puis_un_retrait_sur_la_meme_machine_ne_s_influencent_pas()
    {
        var (machine, dispenser) = Machine(Full);

        var refused = machine.Execute(Account(0, 0), new Operation.Withdraw(50));
        var accepted = machine.Execute(Account(100, 0), new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.InsufficientFunds, refused.Status);
        Assert.Equal(AtmStatus.Ok, accepted.Status);
        Assert.Equal(Account(50, 50), accepted.Account);
        Assert.Equal(Notes(4, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Le_resultat_ne_depend_que_du_compte_de_l_operation_et_du_module()
    {
        var (machine, dispenser) = Machine(Full);

        // Un premier retrait sur un autre compte ne laisse aucune trace, sauf dans le module.
        machine.Execute(Account(250, 150), new Operation.Withdraw(50));
        var stockBefore = dispenser.Stock;
        var second = machine.Execute(Account(100, 0), new Operation.Withdraw(50));

        var (fresh, _) = Withdraw(Account(100, 0), 50, stockBefore);
        Assert.Equal(fresh, second);
    }
}
