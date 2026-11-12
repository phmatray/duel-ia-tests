using static Atm.Tests.Support;

namespace Atm.Tests;

/// <summary>Le retrait : chaque contrôle, leur ordre, les frais, la combinaison et la sortie.</summary>
public class WithdrawTests
{
    // ---------- Contrôle 1 : montant invalide ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount) =>
        AssertRefused(AtmStatus.InvalidAmount, new AccountState(250, 0), Full, null, Withdraw(amount));

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount) =>
        // Plafond atteint, solde nul, aucun billet : seul le premier contrôle compte.
        AssertRefused(AtmStatus.InvalidAmount, new AccountState(0, 200), N(0, 0, 0), Denomination.Ten, Withdraw(amount));

    // ---------- Contrôle 2 : plafond du jour ----------

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(190, 20)]
    [InlineData(200, 10)]
    public void Depasser_200_euros_retires_dans_la_journee_est_refuse(int withdrawnToday, int amount) =>
        AssertRefused(AtmStatus.DailyLimitExceeded, new AccountState(250, withdrawnToday), Full, null, Withdraw(amount));

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_200_euros_est_permis(int withdrawnToday, int amount)
    {
        var (machine, _) = NewAtm(Full);

        var result = machine.Execute(new AccountState(250, withdrawnToday), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(200, result.Account.WithdrawnToday);
        Assert.Equal(amount, result.Notes.Value);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        var (machine, _) = NewAtm(Full);

        var result = machine.Execute(new AccountState(205, 0), Withdraw(200));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(4, 0, 0), 5, new AccountState(0, 200)), result);
    }

    [Fact]
    public void Plafond_passe_avant_solde_insuffisant() =>
        AssertRefused(AtmStatus.DailyLimitExceeded, new AccountState(0, 150), Full, null, Withdraw(60));

    [Fact]
    public void Plafond_passe_avant_billets_impossibles() =>
        AssertRefused(AtmStatus.DailyLimitExceeded, new AccountState(250, 200), N(0, 0, 0), null, Withdraw(10));

    // ---------- Contrôle 3 : solde insuffisant ----------

    [Theory]
    [InlineData(0, 10)]
    [InlineData(95, 100)]
    [InlineData(110, 110)]   // 110 + 5 de frais > 110
    [InlineData(105, 110)]
    [InlineData(200, 200)]   // 200 + 5 de frais > 200
    [InlineData(150, 150)]
    public void Montant_plus_frais_au_dela_du_solde_est_refuse(int balance, int amount) =>
        AssertRefused(AtmStatus.InsufficientFunds, new AccountState(balance, 0), Full, null, Withdraw(amount));

    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 5)]
    [InlineData(205, 200, 5)]
    public void Montant_plus_frais_egal_au_solde_est_permis(int balance, int amount, int fee)
    {
        var (machine, _) = NewAtm(Full);

        var result = machine.Execute(new AccountState(balance, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(0, result.Account.Balance);
    }

    [Fact]
    public void Solde_insuffisant_passe_avant_billets_impossibles() =>
        AssertRefused(AtmStatus.InsufficientFunds, new AccountState(50, 0), N(0, 0, 0), null, Withdraw(60));

    // ---------- Les frais ----------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_euros_strictement_au_dela_de_100_euros(int amount, int expectedFee)
    {
        var (machine, _) = NewAtm(Full);

        var result = machine.Execute(new AccountState(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(new AccountState(250 - amount - expectedFee, amount), result.Account);
    }

    // ---------- Contrôle 4 : billets impossibles ----------

    [Theory]
    [InlineData(0, 0, 0, 10)]
    [InlineData(0, 5, 0, 10)]    // aucun billet de 10
    [InlineData(0, 5, 0, 30)]    // 20 + 10 impossible sans billet de 10
    [InlineData(5, 0, 0, 20)]
    [InlineData(1, 0, 0, 60)]
    [InlineData(1, 1, 1, 90)]    // stock total de 80
    [InlineData(2, 0, 1, 40)]
    [InlineData(1, 2, 0, 80)]    // 50 + 20 + 10 impossible, 20 × 4 aussi
    public void Aucune_combinaison_exacte_disponible(int fifties, int twenties, int tens, int amount) =>
        AssertRefused(AtmStatus.CannotDispense, new AccountState(250, 0), N(fifties, twenties, tens), null, Withdraw(amount));

    [Theory]
    [InlineData(Denomination.Fifty, 5, 0, 0, 50)]
    [InlineData(Denomination.Twenty, 0, 5, 0, 20)]
    [InlineData(Denomination.Ten, 0, 0, 5, 10)]
    [InlineData(Denomination.Ten, 5, 5, 5, 30)]     // 20 + 10 : la cassette de 10 compte pour zéro
    [InlineData(Denomination.Twenty, 1, 5, 0, 60)]  // 20 × 3 : la cassette de 20 compte pour zéro
    [InlineData(Denomination.Fifty, 5, 1, 0, 50)]   // 50 interdit, 20 seul insuffisant
    public void La_cassette_bloquee_compte_pour_zero(Denomination jammed, int fifties, int twenties, int tens, int amount) =>
        AssertRefused(AtmStatus.CannotDispense, new AccountState(250, 0), N(fifties, twenties, tens), jammed, Withdraw(amount));

    // ---------- La combinaison : le moins de billets possible ----------

    [Theory]
    [InlineData(5, 5, 5, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 20, 0, 1, 0)]
    [InlineData(5, 5, 5, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 70, 1, 1, 0)]
    [InlineData(5, 5, 5, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 140, 2, 2, 0)]
    [InlineData(5, 5, 5, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, 200, 4, 0, 0)]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // le glouton (50 puis 10) échoue
    [InlineData(1, 4, 0, 80, 0, 4, 0)]   // le glouton (50 + 20 puis 10) échoue
    [InlineData(1, 5, 0, 100, 0, 5, 0)]  // le glouton échoue, cinq billets de 20
    [InlineData(1, 5, 0, 130, 1, 4, 0)]
    [InlineData(3, 5, 0, 160, 2, 3, 0)]  // 50×3 + 10 impossible
    [InlineData(1, 0, 5, 60, 1, 0, 1)]
    [InlineData(0, 1, 5, 50, 0, 1, 3)]
    [InlineData(0, 0, 5, 50, 0, 0, 5)]
    [InlineData(1, 2, 0, 70, 1, 1, 0)]
    [InlineData(1, 3, 1, 60, 1, 0, 1)]   // 50 + 10 bat 20 × 3
    [InlineData(0, 1, 4, 40, 0, 1, 2)]
    public void La_combinaison_compte_le_moins_de_billets(
        int fifties, int twenties, int tens, int amount, int expected50, int expected20, int expected10)
    {
        var stock = N(fifties, twenties, tens);
        var (machine, dispenser) = NewAtm(stock);

        var result = machine.Execute(new AccountState(250, 0), Withdraw(amount));

        var expected = N(expected50, expected20, expected10);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 5, 5, 5, 60, 0, 3, 0)]
    [InlineData(Denomination.Fifty, 5, 5, 5, 100, 0, 5, 0)]
    [InlineData(Denomination.Twenty, 5, 5, 5, 90, 1, 0, 4)]
    [InlineData(Denomination.Twenty, 5, 5, 5, 40, 0, 0, 4)]
    [InlineData(Denomination.Ten, 5, 5, 5, 60, 0, 3, 0)]
    [InlineData(Denomination.Ten, 5, 5, 5, 90, 1, 2, 0)]
    public void La_combinaison_evite_la_cassette_bloquee(
        Denomination jammed, int fifties, int twenties, int tens, int amount, int expected50, int expected20, int expected10)
    {
        var stock = N(fifties, twenties, tens);
        var (machine, dispenser) = NewAtm(stock, jammed);

        var result = machine.Execute(new AccountState(250, 0), Withdraw(amount));

        var expected = N(expected50, expected20, expected10);
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(0, result.Notes.Of(jammed));
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    // ---------- La sortie : plusieurs passages de 4 billets au plus ----------

    [Theory]
    [InlineData(0, 5, 0, 100, 0, 5, 0)]   // 5 billets
    [InlineData(0, 0, 5, 50, 0, 0, 5)]    // 5 billets
    [InlineData(3, 5, 0, 190, 3, 2, 0)]   // 5 billets
    [InlineData(1, 5, 5, 200, 1, 5, 5)]   // 11 billets : trois passages
    [InlineData(0, 5, 5, 150, 0, 5, 5)]   // 10 billets
    public void Un_retrait_de_plus_de_4_billets_sort_en_plusieurs_passages(
        int fifties, int twenties, int tens, int amount, int expected50, int expected20, int expected10)
    {
        var stock = N(fifties, twenties, tens);
        var (machine, dispenser) = NewAtm(stock);

        var result = machine.Execute(new AccountState(250, 0), Withdraw(amount));

        var expected = N(expected50, expected20, expected10);
        var fee = amount > 100 ? 5 : 0;
        Assert.Equal(new AtmResult(AtmStatus.Ok, expected, fee, new AccountState(250 - amount - fee, amount)), result);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    // ---------- Le succès ----------

    [Fact]
    public void Un_retrait_reussi_debite_le_compte_et_vide_les_cassettes()
    {
        var (machine, dispenser) = NewAtm(N(2, 3, 4));

        var result = machine.Execute(new AccountState(250, 40), Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 2, 1), 5, new AccountState(95, 190)), result);
        Assert.Equal(N(0, 1, 3), dispenser.Stock);
    }

    [Fact]
    public void Un_retrait_ne_touche_pas_aux_billets_d_une_cassette_bloquee()
    {
        var (machine, dispenser) = NewAtm(N(5, 5, 5), Denomination.Fifty);

        var result = machine.Execute(new AccountState(250, 0), Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 5, 5), 5, new AccountState(95, 150)), result);
        Assert.Equal(N(5, 0, 0), dispenser.Stock);
    }
}
