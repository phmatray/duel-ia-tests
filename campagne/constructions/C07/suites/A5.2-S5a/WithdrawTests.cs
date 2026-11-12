using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (machine, dispenser) = Real(Full);

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        AssertRefused(result, AtmStatus.InvalidAmount, Acc(250, 0));
        Assert.Equal(Full, dispenser.Stock);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(200)]
    public void Multiple_de_10_positif_est_valide(int amount)
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
    }

    [Fact]
    public void Montant_invalide_passe_avant_le_plafond_le_solde_et_les_billets()
    {
        var (machine, _) = Real(N(0, 0, 0), Denomination.Ten);

        var result = machine.Execute(Acc(0, 200), Withdraw(-10));

        AssertRefused(result, AtmStatus.InvalidAmount, Acc(0, 200));
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_le_plafond_est_permis(int withdrawn, int amount)
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Acc(250, withdrawn), Withdraw(amount));

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
        var (machine, dispenser) = Real(Full);

        var result = machine.Execute(Acc(250, withdrawn), Withdraw(amount));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, Acc(250, withdrawn));
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        var (machine, _) = Real(Full);

        // 90 + 110 = 200 : pile le plafond, les 5 € de frais n'y entrent pas.
        var result = machine.Execute(Acc(250, 90), Withdraw(110));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Acc(135, 200), result.Account);
    }

    [Fact]
    public void Plafond_passe_avant_le_solde_et_les_billets()
    {
        var (machine, _) = Real(N(0, 0, 0));

        var result = machine.Execute(Acc(0, 0), Withdraw(220));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, Acc(0, 0));
    }

    // ---- Contrôle 3 : solde ----

    [Theory]
    [InlineData(100, 100)] // sans frais, solde tout juste suffisant
    [InlineData(105, 100)]
    [InlineData(115, 110)] // avec frais, solde tout juste suffisant
    [InlineData(205, 200)]
    public void Solde_qui_couvre_montant_et_frais_suffit(int balance, int amount)
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Acc(balance, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
    }

    [Theory]
    [InlineData(95, 100)]
    [InlineData(0, 10)]
    [InlineData(110, 110)] // les frais font déborder
    [InlineData(200, 200)]
    [InlineData(115, 120)]
    public void Montant_plus_frais_au_dela_du_solde_est_refuse(int balance, int amount)
    {
        var (machine, dispenser) = Real(Full);

        var result = machine.Execute(Acc(balance, 0), Withdraw(amount));

        AssertRefused(result, AtmStatus.InsufficientFunds, Acc(balance, 0));
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Solde_passe_avant_les_billets()
    {
        var (machine, _) = Real(N(0, 0, 0));

        var result = machine.Execute(Acc(0, 0), Withdraw(10));

        AssertRefused(result, AtmStatus.InsufficientFunds, Acc(0, 0));
    }

    // ---- Frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int fee)
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(Acc(250 - amount - fee, amount), result.Account);
    }

    // ---- Contrôle 4 : billets disponibles ----

    [Fact]
    public void Stock_vide_ne_peut_rien_distribuer()
    {
        var (machine, dispenser) = Real(N(0, 0, 0));

        var result = machine.Execute(Acc(250, 0), Withdraw(10));

        AssertRefused(result, AtmStatus.CannotDispense, Acc(250, 0));
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Montant_superieur_au_stock_ne_peut_etre_distribue()
    {
        var (machine, dispenser) = Real(N(1, 1, 1));

        var result = machine.Execute(Acc(250, 0), Withdraw(90));

        AssertRefused(result, AtmStatus.CannotDispense, Acc(250, 0));
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Somme_atteignable_en_valeur_mais_pas_exactement_est_refusee()
    {
        // 30 € : un 50 ne convient pas, un seul 20 et aucun 10.
        var (machine, dispenser) = Real(N(1, 1, 0));

        var result = machine.Execute(Acc(250, 0), Withdraw(30));

        AssertRefused(result, AtmStatus.CannotDispense, Acc(250, 0));
        Assert.Equal(N(1, 1, 0), dispenser.Stock);
    }

    [Theory]
    [InlineData(10, 0, 5, 5, 10)]  // 10 € alors que la cassette de 10, pleine, est bloquée
    [InlineData(20, 5, 5, 1, 40)]  // 40 € : il faudrait des 20, bloqués, ou quatre 10
    [InlineData(50, 5, 5, 0, 50)]  // 50 € : il faudrait un 50, bloqué, ou des 10
    public void La_cassette_bloquee_compte_pour_zero(int jam, int fifties, int twenties, int tens, int amount)
    {
        var stock = N(fifties, twenties, tens);
        var (machine, dispenser) = Real(stock, Jam(jam));

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        AssertRefused(result, AtmStatus.CannotDispense, Acc(250, 0));
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void La_cassette_bloquee_est_contournee_avec_les_autres_coupures()
    {
        // 100 € sans les 50 : cinq 20.
        var (machine, dispenser) = Real(N(5, 5, 5), Denomination.Fifty);

        var result = machine.Execute(Acc(250, 0), Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 5, 0), 0, Acc(150, 100)), result);
        Assert.Equal(N(5, 0, 5), dispenser.Stock);
    }

    // ---- La combinaison : le moins de billets ----

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
    public void Stock_plein_donne_le_moins_de_billets(int amount, int fifties, int twenties, int tens)
    {
        var (machine, dispenser) = Real(Full);

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(fifties, twenties, tens), result.Notes);
        Assert.Equal(Full - N(fifties, twenties, tens), dispenser.Stock);
    }

    [Theory]
    [InlineData(60, 1, 5, 5, 1, 0, 1)] // 50 + 10
    [InlineData(60, 1, 5, 0, 0, 3, 0)] // pas de 10 : 20×3
    [InlineData(80, 0, 5, 5, 0, 4, 0)] // sans 50 : 20×4
    [InlineData(90, 0, 5, 5, 0, 4, 1)] // sans 50 : 20×4 + 10
    [InlineData(100, 1, 5, 5, 1, 2, 1)] // un seul 50 : 50 + 20×2 + 10
    [InlineData(70, 5, 0, 5, 1, 0, 2)] // sans 20 : 50 + 10×2
    [InlineData(40, 5, 1, 5, 0, 1, 2)] // un seul 20 : 20 + 10×2
    [InlineData(120, 1, 5, 5, 1, 3, 1)] // un seul 50 : 50 + 20×3 + 10
    [InlineData(150, 2, 5, 5, 2, 2, 1)] // deux 50 : 50×2 + 20×2 + 10
    public void Stock_partiel_donne_le_moins_de_billets_parmi_les_disponibles(
        int amount, int stockFifties, int stockTwenties, int stockTens, int fifties, int twenties, int tens)
    {
        var stock = N(stockFifties, stockTwenties, stockTens);
        var (machine, dispenser) = Real(stock);

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(fifties, twenties, tens), result.Notes);
        Assert.Equal(stock - N(fifties, twenties, tens), dispenser.Stock);
    }

    // ---- La sortie : passages de 4 billets au plus ----

    [Theory]
    [InlineData(180, 5, 5, 5)] // 50×3 20×1 10×1 : 5 billets
    [InlineData(190, 5, 5, 5)] // 50×3 20×2 : 5 billets
    [InlineData(200, 5, 5, 5)] // 50×4
    [InlineData(100, 0, 5, 5)] // 20×5
    [InlineData(90, 0, 5, 5)]  // 20×4 10×1
    [InlineData(150, 2, 5, 5)] // 50×2 20×2 10×1
    [InlineData(120, 1, 5, 5)] // 50×1 20×3 10×1
    [InlineData(200, 2, 5, 5)] // 50×2 20×5 : 7 billets
    [InlineData(40, 0, 0, 5)]  // 10×4
    [InlineData(50, 0, 5, 5)]  // 20×2 10×1
    public void Les_billets_sortent_en_passages_de_4_au_plus_et_au_total_la_combinaison(
        int amount, int fifties, int twenties, int tens)
    {
        var stock = N(fifties, twenties, tens);
        var (machine, spy) = Spied(stock);

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(amount, result.Notes.Value);
        Assert.All(spy.Dispensed, pass => Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(result.Notes, spy.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(stock - result.Notes, spy.Stock);
        Assert.Empty(spy.Loaded);
    }

    [Fact]
    public void Un_refus_ne_touche_pas_au_module()
    {
        var (machine, spy) = Spied(Full);

        machine.Execute(Acc(250, 0), Withdraw(5));
        machine.Execute(Acc(250, 200), Withdraw(10));
        machine.Execute(Acc(0, 0), Withdraw(10));

        Assert.Empty(spy.Dispensed);
        Assert.Empty(spy.Loaded);
        Assert.Equal(Full, spy.Stock);
    }

    [Fact]
    public void Un_refus_cannot_dispense_ne_touche_pas_au_module()
    {
        var (machine, spy) = Spied(N(1, 0, 0), Denomination.Fifty);

        machine.Execute(Acc(250, 0), Withdraw(50));

        Assert.Empty(spy.Dispensed);
        Assert.Empty(spy.Loaded);
    }

    // ---- Le succès ----

    [Fact]
    public void Le_succes_ne_touche_que_ce_qu_il_faut_au_compte()
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Acc(200, 40), Withdraw(70));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 0), 0, Acc(130, 110)), result);
    }
}
