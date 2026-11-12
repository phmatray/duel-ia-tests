namespace Atm.Tests;

using static T;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    // --- Contrôle 1 : montant invalide -------------------------------------------------------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (machine, spy) = Spied(Full);

        var result = machine.Execute(Acc(250, 0), W(amount));

        AssertRefused(result, AtmStatus.InvalidAmount, Acc(250, 0));
        Assert.Equal(Full, spy.Stock);
        Assert.Equal(0, spy.Calls);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, stock vide : tout échouerait, mais le contrôle 1 l'emporte.
        var (machine, _) = Real(N(0, 0, 0));

        var result = machine.Execute(Acc(0, 200), W(amount));

        AssertRefused(result, AtmStatus.InvalidAmount, Acc(0, 200));
    }

    [Fact]
    public void Plus_petit_montant_valide_10_est_accepte()
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Acc(10, 0), W(10));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 0, 1), 0, Acc(0, 10)), result);
    }

    // --- Contrôle 2 : plafond du jour --------------------------------------------------------

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_le_plafond_de_200_est_permis(int withdrawnToday, int amount)
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Acc(250, withdrawnToday), W(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(200, result.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    public void Depasser_le_plafond_de_200_est_refuse(int withdrawnToday, int amount)
    {
        var (machine, spy) = Spied(Full);

        var result = machine.Execute(Acc(250, withdrawnToday), W(amount));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, Acc(250, withdrawnToday));
        Assert.Equal(Full, spy.Stock);
        Assert.Equal(0, spy.Calls);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : au plafond, même si 110 + 5 de frais ferait 205.
        var (machine, _) = Real(Full);

        var result = machine.Execute(Acc(115, 90), W(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, Acc(0, 200)), result);
    }

    [Fact]
    public void Plafond_passe_avant_le_solde()
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Acc(0, 200), W(10));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, Acc(0, 200));
    }

    [Fact]
    public void Plafond_passe_avant_la_distribution()
    {
        var (machine, _) = Real(N(0, 0, 0));

        var result = machine.Execute(Acc(250, 150), W(60));

        AssertRefused(result, AtmStatus.DailyLimitExceeded, Acc(250, 150));
    }

    // --- Contrôle 3 : solde et frais ---------------------------------------------------------

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

        var result = machine.Execute(Acc(250, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(250 - amount - expectedFee, result.Account.Balance);
        Assert.Equal(amount, result.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    public void Solde_egal_a_montant_plus_frais_suffit(int balance, int amount)
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Acc(balance, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(0, result.Account.Balance);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(45, 50)]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(200, 200)]
    public void Solde_inferieur_a_montant_plus_frais_est_refuse(int balance, int amount)
    {
        var (machine, spy) = Spied(Full);

        var result = machine.Execute(Acc(balance, 0), W(amount));

        AssertRefused(result, AtmStatus.InsufficientFunds, Acc(balance, 0));
        Assert.Equal(Full, spy.Stock);
        Assert.Equal(0, spy.Calls);
    }

    [Fact]
    public void Solde_passe_avant_la_distribution()
    {
        var (machine, _) = Real(N(0, 0, 0));

        var result = machine.Execute(Acc(0, 0), W(10));

        AssertRefused(result, AtmStatus.InsufficientFunds, Acc(0, 0));
    }

    // --- Contrôle 4 : distribution impossible ------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0, null, 10)]
    [InlineData(0, 5, 0, null, 10)]   // pas de 10
    [InlineData(0, 5, 0, null, 30)]   // 20 seuls ne font pas 30
    [InlineData(1, 0, 0, null, 60)]   // stock total insuffisant
    [InlineData(5, 0, 0, null, 30)]
    [InlineData(1, 1, 1, null, 90)]   // tout le stock fait 80
    [InlineData(0, 0, 5, Denomination.Ten, 10)]       // la cassette bloquée compte pour zéro
    [InlineData(0, 5, 0, Denomination.Twenty, 40)]
    [InlineData(5, 0, 1, Denomination.Fifty, 50)]
    public void Aucune_combinaison_disponible_donne_CannotDispense(
        int fifties, int twenties, int tens, Denomination? jammed, int amount)
    {
        var stock = N(fifties, twenties, tens);
        var (machine, spy) = Spied(stock, jammed);

        var result = machine.Execute(Acc(250, 0), W(amount));

        AssertRefused(result, AtmStatus.CannotDispense, Acc(250, 0));
        Assert.Equal(stock, spy.Stock);
        Assert.Equal(0, spy.Calls);
    }

    // --- Combinaison au moins de billets -----------------------------------------------------

    [Theory]
    [InlineData(5, 5, 5, null, 40, 0, 2, 0)]
    [InlineData(0, 1, 5, null, 40, 0, 1, 2)]
    [InlineData(5, 5, 5, null, 60, 1, 0, 1)]
    [InlineData(1, 5, 0, null, 60, 0, 3, 0)]   // 50 + 10 impossible faute de 10
    [InlineData(5, 5, 5, null, 80, 1, 1, 1)]
    [InlineData(1, 4, 0, null, 80, 0, 4, 0)]   // le glouton 50 + ... échoue
    [InlineData(5, 5, 5, null, 100, 2, 0, 0)]
    [InlineData(5, 5, 5, null, 130, 2, 1, 1)]
    [InlineData(2, 5, 0, null, 130, 1, 4, 0)]
    [InlineData(5, 5, 5, null, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, null, 200, 4, 0, 0)]
    [InlineData(5, 5, 5, Denomination.Fifty, 150, 0, 5, 5)]
    [InlineData(5, 5, 5, Denomination.Twenty, 90, 1, 0, 4)]
    [InlineData(5, 5, 5, Denomination.Ten, 70, 1, 1, 0)]
    public void La_combinaison_choisie_compte_le_moins_de_billets(
        int fifties, int twenties, int tens, Denomination? jammed, int amount, int e50, int e20, int e10)
    {
        var stock = N(fifties, twenties, tens);
        var expected = N(e50, e20, e10);
        var (machine, dispenser) = Real(stock, jammed);

        var result = machine.Execute(Acc(250, 0), W(amount));

        var fee = amount > 100 ? 5 : 0;
        Assert.Equal(new AtmResult(AtmStatus.Ok, expected, fee, Acc(250 - amount - fee, amount)), result);
        Assert.Equal(stock - expected, dispenser.Stock);
    }

    // --- Sortie en passages de 4 billets au plus ---------------------------------------------

    [Theory]
    [InlineData(5, 5, 5, null, 10)]
    [InlineData(5, 5, 5, null, 200)]    // 4 billets : un passage suffit
    [InlineData(5, 5, 5, null, 190)]    // 5 billets : deux passages
    [InlineData(5, 5, 5, Denomination.Fifty, 150)]  // 10 billets : trois passages
    [InlineData(0, 5, 5, null, 140)]    // 20×5 + 10×4 : 9 billets
    public void Les_billets_sortent_par_le_module_en_passages_de_4_au_plus(
        int fifties, int twenties, int tens, Denomination? jammed, int amount)
    {
        var stock = N(fifties, twenties, tens);
        var (machine, spy) = Spied(stock, jammed);

        var result = machine.Execute(Acc(250, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Empty(spy.Loads);
        Assert.NotEmpty(spy.Dispenses);
        Assert.All(spy.Dispenses, d =>
        {
            Assert.True(d.Ok);
            Assert.InRange(d.Notes.Total, 1, 4);
        });
        var total = spy.Dispenses.Aggregate(NoteCounts.None, (acc, d) => acc + d.Notes);
        Assert.Equal(result.Notes, total);
        Assert.Equal(amount, result.Notes.Value);
        Assert.Equal(stock - result.Notes, spy.Stock);
    }
}
