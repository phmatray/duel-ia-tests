using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    // ---- Contrôle 1 : montant invalide -------------------------------------------------------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (machine, spy) = Spied(Full);

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        AssertRefused(result, AtmStatus.InvalidAmount, Account(250, 0));
        Assert.Equal(0, spy.Calls);
        Assert.Equal(Full, spy.Stock);
    }

    [Fact]
    public void Montant_invalide_passe_avant_tous_les_autres_controles()
    {
        // Plafond atteint, solde nul, stock vide : le montant invalide l'emporte.
        var (machine, _) = Real(Empty, Denomination.Ten);

        var result = machine.Execute(Account(0, 200), Withdraw(-10));

        AssertRefused(result, AtmStatus.InvalidAmount, Account(0, 200));
    }

    // ---- Contrôle 2 : plafond du jour --------------------------------------------------------

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    [InlineData(100, 100)]
    public void Atteindre_exactement_le_plafond_est_permis(int withdrawnToday, int amount)
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Account(250, withdrawnToday), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(withdrawnToday + amount, result.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    public void Depasser_le_plafond_est_refuse(int withdrawnToday, int amount)
    {
        var (machine, spy) = Spied(Full);

        var result = machine.Execute(Account(250, withdrawnToday), Withdraw(amount));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, Account(250, withdrawnToday));
        Assert.Equal(0, spy.Calls);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : pile le plafond, alors que 90 + 110 + 5 le dépasserait.
        var (machine, _) = Real(Full);

        var result = machine.Execute(Account(250, 90), Withdraw(110));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(Account(135, 200), result.Account);
    }

    [Fact]
    public void Plafond_passe_avant_le_solde()
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Account(0, 200), Withdraw(10));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, Account(0, 200));
    }

    [Fact]
    public void Plafond_passe_avant_la_distribution()
    {
        var (machine, _) = Real(Empty);

        var result = machine.Execute(Account(250, 200), Withdraw(10));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, Account(250, 200));
    }

    // ---- Contrôle 3 : solde ------------------------------------------------------------------

    [Theory]
    [InlineData(55, 60)]   // sans frais : il manque 5
    [InlineData(100, 110)] // avec frais
    [InlineData(110, 110)] // avec frais : le montant passe, pas les frais
    [InlineData(200, 200)]
    [InlineData(0, 10)]
    public void Montant_plus_frais_au_dela_du_solde_est_refuse(int balance, int amount)
    {
        var (machine, spy) = Spied(Full);

        var result = machine.Execute(Account(balance, 0), Withdraw(amount));

        AssertRefused(result, AtmStatus.InsufficientFunds, Account(balance, 0));
        Assert.Equal(0, spy.Calls);
    }

    [Theory]
    [InlineData(60, 60, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 0)]
    [InlineData(205, 200, 0)]
    public void Montant_plus_frais_egal_au_solde_est_permis(int balance, int amount, int expectedBalance)
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Account(balance, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedBalance, result.Account.Balance);
    }

    [Fact]
    public void Solde_passe_avant_la_distribution()
    {
        var (machine, _) = Real(Empty);

        var result = machine.Execute(Account(0, 0), Withdraw(10));

        AssertRefused(result, AtmStatus.InsufficientFunds, Account(0, 0));
    }

    // ---- Frais -------------------------------------------------------------------------------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(Account(250 - amount - expectedFee, amount), result.Account);
    }

    // ---- Contrôle 4 : distribution impossible ------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0, 10)]   // stock vide
    [InlineData(1, 0, 0, 10)]   // rien d'assez petit
    [InlineData(0, 1, 0, 10)]
    [InlineData(0, 2, 0, 30)]   // 20 ne fait pas 30
    [InlineData(1, 0, 0, 60)]   // 50 ne fait pas 60
    [InlineData(1, 1, 0, 80)]   // 50 + 20 = 70 au mieux
    [InlineData(0, 0, 5, 60)]   // 50 € de 10 au plus
    [InlineData(1, 1, 1, 90)]   // 80 € en tout
    public void Aucune_combinaison_exacte_donne_CannotDispense(int fifties, int twenties, int tens, int amount)
    {
        var stock = Notes(fifties, twenties, tens);
        var (machine, spy) = Spied(stock);

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        AssertRefused(result, AtmStatus.CannotDispense, Account(250, 0));
        Assert.Empty(spy.Dispensed);
        Assert.Equal(stock, spy.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 5, 0, 0, 50)]
    [InlineData(Denomination.Twenty, 0, 5, 0, 20)]
    [InlineData(Denomination.Ten, 0, 0, 5, 10)]
    [InlineData(Denomination.Ten, 5, 5, 5, 10)]
    [InlineData(Denomination.Ten, 1, 1, 1, 80)]
    [InlineData(Denomination.Twenty, 1, 5, 0, 70)]
    public void La_cassette_bloquee_compte_pour_zero(Denomination jammed, int fifties, int twenties, int tens, int amount)
    {
        var stock = Notes(fifties, twenties, tens);
        var (machine, dispenser) = Real(stock, jammed);

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        AssertRefused(result, AtmStatus.CannotDispense, Account(250, 0));
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void Cassette_de_10_bloquee_sur_un_retrait_en_plusieurs_passages_ne_sort_rien()
    {
        // Avec les 10, 190 = 50×3 20×1 10×2 (6 billets, 2 passages). Sans eux, 190 est impossible.
        var stock = Notes(3, 1, 5);
        var (machine, dispenser) = Real(stock, Denomination.Ten);

        var result = machine.Execute(Account(250, 0), Withdraw(190));

        AssertRefused(result, AtmStatus.CannotDispense, Account(250, 0));
        Assert.Equal(stock, dispenser.Stock);
    }

    // ---- La combinaison : le moins de billets parmi les disponibles --------------------------

    [Theory]
    // stock (50, 20, 10), bloquée, montant -> billets attendus (50, 20, 10)
    [InlineData(5, 5, 5, null, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, null, 20, 0, 1, 0)]
    [InlineData(5, 5, 5, null, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, null, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, null, 50, 1, 0, 0)]
    [InlineData(5, 5, 5, null, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, null, 70, 1, 1, 0)]
    [InlineData(5, 5, 5, null, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, null, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, null, 200, 4, 0, 0)]
    [InlineData(1, 3, 1, null, 60, 1, 0, 1)]        // 50+10 plutôt que 20×3
    [InlineData(1, 3, 0, null, 60, 0, 3, 0)]        // sans 10, le 50 ne sert pas
    [InlineData(1, 4, 3, null, 80, 1, 1, 1)]        // 3 billets plutôt que 20×4
    [InlineData(0, 0, 4, null, 40, 0, 0, 4)]        // que des 10
    [InlineData(0, 5, 0, null, 100, 0, 5, 0)]       // 5 billets, il faut 2 passages
    [InlineData(1, 0, 5, null, 100, 1, 0, 5)]       // 6 billets
    [InlineData(5, 5, 5, Denomination.Fifty, 100, 0, 5, 0)]
    [InlineData(5, 5, 5, Denomination.Fifty, 150, 0, 5, 5)]
    [InlineData(5, 5, 5, Denomination.Twenty, 90, 1, 0, 4)]
    [InlineData(5, 5, 5, Denomination.Twenty, 40, 0, 0, 4)]
    [InlineData(1, 3, 1, Denomination.Ten, 60, 0, 3, 0)] // le 10 bloqué écarte 50+10
    [InlineData(5, 5, 5, Denomination.Ten, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, Denomination.Ten, 60, 0, 3, 0)]
    [InlineData(5, 5, 5, Denomination.Ten, 30, 0, 0, 0)] // impossible : voir assertion
    public void La_combinaison_compte_le_moins_de_billets_disponibles(
        int fifties, int twenties, int tens, Denomination? jammed, int amount, int eFifties, int eTwenties, int eTens)
    {
        var stock = Notes(fifties, twenties, tens);
        var expected = Notes(eFifties, eTwenties, eTens);
        var (machine, dispenser) = Real(stock, jammed);

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        if (expected == NoteCounts.None)
        {
            AssertRefused(result, AtmStatus.CannotDispense, Account(250, 0));
            Assert.Equal(stock, dispenser.Stock);
            return;
        }

        var fee = amount > 100 ? 5 : 0;
        Assert.Equal(new AtmResult(AtmStatus.Ok, expected, fee, Account(250 - amount - fee, amount)), result);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    // ---- La sortie : passages de 4 billets au plus -------------------------------------------

    [Theory]
    [InlineData(0, 5, 0, 100)]
    [InlineData(1, 0, 5, 100)]
    [InlineData(4, 5, 5, 200)]
    [InlineData(0, 5, 5, 150)]
    [InlineData(1, 5, 0, 150)]
    public void Les_billets_sortent_par_passages_de_4_au_plus_et_le_total_fait_la_combinaison(
        int fifties, int twenties, int tens, int amount)
    {
        var stock = Notes(fifties, twenties, tens);
        var (machine, spy) = Spied(stock);

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.NotEmpty(spy.Dispensed);
        Assert.All(spy.Dispensed, pass => Assert.InRange(pass.Total, 1, 4));
        var total = spy.Dispensed.Aggregate(NoteCounts.None, (sum, pass) => sum + pass);
        Assert.Equal(result.Notes, total);
        Assert.Equal(amount, total.Value);
        Assert.Equal(stock - total, spy.Stock);
        Assert.Empty(spy.Loaded);
    }

    [Fact]
    public void Un_retrait_reussi_ne_touche_que_le_solde_et_le_retire_du_jour()
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Account(235, 30), Withdraw(170));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(3, 1, 0), 5, Account(60, 200)), result);
    }

    // ---- Sans mémoire ------------------------------------------------------------------------

    [Fact]
    public void La_machine_ne_depend_que_du_compte_de_l_operation_et_du_module()
    {
        var (machine, dispenser) = Real(Notes(1, 0, 0));

        var first = machine.Execute(Account(250, 0), Withdraw(50));
        var second = machine.Execute(Account(250, 0), Withdraw(50));

        Assert.Equal(AtmStatus.Ok, first.Status);
        // Même compte, même opération : seul le module a changé (cassette de 50 vide).
        AssertRefused(second, AtmStatus.CannotDispense, Account(250, 0));
        Assert.Equal(Empty, dispenser.Stock);
    }

    [Fact]
    public void Deux_machines_sur_des_modules_identiques_rendent_le_meme_resultat()
    {
        var (a, _) = Real(Notes(2, 3, 1), Denomination.Fifty);
        var (b, _) = Real(Notes(2, 3, 1), Denomination.Fifty);
        a.Execute(Account(5, 0), Withdraw(10)); // un refus (solde insuffisant) sur a ne doit rien laisser derrière lui

        Assert.Equal(b.Execute(Account(250, 0), Withdraw(70)), a.Execute(Account(250, 0), Withdraw(70)));
    }
}
