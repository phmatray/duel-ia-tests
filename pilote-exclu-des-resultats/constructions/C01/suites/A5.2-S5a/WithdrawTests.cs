using static Atm.Tests.Fixture;

namespace Atm.Tests;

public class WithdrawTests
{
    private static void AssertRefused(AtmStatus expected, AccountState account, AtmResult result, SpyDispenser spy,
        NoteCounts stockBefore)
    {
        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
        Assert.Empty(spy.DispenseCalls);
        Assert.Empty(spy.LoadCalls);
        Assert.Equal(stockBefore, spy.Stock);
    }

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (machine, spy) = Machine(Full);
        var account = new AccountState(250, 0);

        var result = machine.Execute(account, new Operation.Withdraw(amount));

        AssertRefused(AtmStatus.InvalidAmount, account, result, spy, Full);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, stock vide : seul InvalidAmount doit sortir.
        var (machine, spy) = Machine(Notes(0, 0, 0), Denomination.Ten);
        var account = new AccountState(0, 200);

        var result = machine.Execute(account, new Operation.Withdraw(amount));

        AssertRefused(AtmStatus.InvalidAmount, account, result, spy, Notes(0, 0, 0));
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(190, 20)]
    [InlineData(200, 10)]
    public void Depasser_200_dans_la_journee_est_refuse(int withdrawnToday, int amount)
    {
        var (machine, spy) = Machine(Full);
        var account = new AccountState(250, withdrawnToday);

        var result = machine.Execute(account, new Operation.Withdraw(amount));

        AssertRefused(AtmStatus.DailyLimitExceeded, account, result, spy, Full);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(140, 60)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_200_est_permis(int withdrawnToday, int amount)
    {
        var (machine, _) = Machine(Full);

        var result = machine.Execute(new(250, withdrawnToday), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(withdrawnToday + amount, result.Account.WithdrawnToday);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        var (machine, _) = Machine(Full);

        // 90 + 110 = 200 : permis, même si 110 + 5 de frais ferait 205.
        var result = machine.Execute(new(250, 90), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 1), 5, new(135, 200)), result);
    }

    [Fact]
    public void Plafond_passe_avant_solde_insuffisant()
    {
        var (machine, spy) = Machine(Notes(0, 0, 0));
        var account = new AccountState(0, 150);

        var result = machine.Execute(account, new Operation.Withdraw(60));

        AssertRefused(AtmStatus.DailyLimitExceeded, account, result, spy, Notes(0, 0, 0));
    }

    // ---- Contrôle 3 : solde insuffisant ----

    [Theory]
    [InlineData(0, 10)]
    [InlineData(55, 60)]
    [InlineData(100, 110)] // 110 + 5
    [InlineData(110, 110)] // 110 + 5
    [InlineData(200, 200)] // 200 + 5
    public void Montant_plus_frais_superieur_au_solde_est_refuse(int balance, int amount)
    {
        var (machine, spy) = Machine(Full);
        var account = new AccountState(balance, 0);

        var result = machine.Execute(account, new Operation.Withdraw(amount));

        AssertRefused(AtmStatus.InsufficientFunds, account, result, spy, Full);
    }

    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 5)]
    [InlineData(205, 200, 5)]
    public void Montant_plus_frais_egal_au_solde_est_permis(int balance, int amount, int fee)
    {
        var (machine, _) = Machine(Full);

        var result = machine.Execute(new(balance, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(0, result.Account.Balance);
    }

    [Fact]
    public void Solde_insuffisant_passe_avant_billets_indisponibles()
    {
        var (machine, spy) = Machine(Notes(0, 0, 0), Denomination.Fifty);
        var account = new AccountState(5, 0);

        var result = machine.Execute(account, new Operation.Withdraw(50));

        AssertRefused(AtmStatus.InsufficientFunds, account, result, spy, Notes(0, 0, 0));
    }

    // ---- Les frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var (machine, _) = Machine(Full);

        var result = machine.Execute(new(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expectedFee, result.Fee);
        Assert.Equal(250 - amount - expectedFee, result.Account.Balance);
        Assert.Equal(amount, result.Account.WithdrawnToday);
    }

    // ---- Contrôle 4 : combinaison impossible ----

    [Theory]
    [InlineData(0, 0, 0, 0, 10)]
    [InlineData(5, 5, 0, 0, 10)]   // pas de 10
    [InlineData(1, 0, 0, 0, 30)]   // 50 trop gros
    [InlineData(0, 1, 0, 0, 30)]   // 20 seul
    [InlineData(0, 0, 5, 10, 10)]  // 10 bloqué
    [InlineData(0, 5, 0, 20, 20)]  // 20 bloqué
    [InlineData(1, 1, 1, 0, 90)]   // total 80
    [InlineData(0, 3, 0, 0, 70)]   // que des 20
    [InlineData(2, 0, 0, 0, 200)]  // pas assez de 50
    public void Aucune_combinaison_exacte_donne_CannotDispense(int f, int t, int te, int jammed, int amount)
    {
        var stock = Notes(f, t, te);
        var (machine, spy) = Machine(stock, Jam(jammed));
        var account = new AccountState(250, 0);

        var result = machine.Execute(account, new Operation.Withdraw(amount));

        AssertRefused(AtmStatus.CannotDispense, account, result, spy, stock);
    }

    // ---- La combinaison : le moins de billets ----

    [Theory]
    [InlineData(5, 5, 5, 0, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 0, 20, 0, 1, 0)]
    [InlineData(5, 5, 5, 0, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 0, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 0, 50, 1, 0, 0)]
    [InlineData(5, 5, 5, 0, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 0, 70, 1, 1, 0)]
    [InlineData(5, 5, 5, 0, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, 0, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 0, 200, 4, 0, 0)]
    [InlineData(5, 5, 5, 0, 190, 3, 2, 0)]
    [InlineData(0, 5, 5, 0, 50, 0, 2, 1)]    // sans 50
    [InlineData(5, 0, 5, 0, 40, 0, 0, 4)]    // sans 20
    [InlineData(5, 5, 5, 50, 100, 0, 5, 0)]  // 50 bloqué
    [InlineData(5, 5, 5, 20, 80, 1, 0, 3)]   // 20 bloqué
    [InlineData(5, 5, 5, 10, 60, 0, 3, 0)]   // 10 bloqué : 50+10 impossible
    [InlineData(1, 3, 0, 0, 60, 0, 3, 0)]    // 50 en stock mais inutilisable
    [InlineData(1, 5, 5, 0, 150, 1, 5, 0)]
    [InlineData(2, 5, 5, 0, 150, 2, 2, 1)]
    public void Rend_la_combinaison_avec_le_moins_de_billets(int f, int t, int te, int jammed, int amount,
        int ef, int et, int ete)
    {
        var stock = Notes(f, t, te);
        var expected = Notes(ef, et, ete);
        var (machine, spy) = Machine(stock, Jam(jammed));

        var result = machine.Execute(new(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(expected, spy.SuccessfullyDispensed);
        Assert.Equal(stock - expected, spy.Stock);
    }

    // ---- La sortie ----

    [Theory]
    [InlineData(5, 5, 5, 0, 200)]  // 50×4
    [InlineData(0, 0, 5, 0, 50)]   // 10×5
    [InlineData(0, 5, 5, 0, 110)]  // 20×5 10×1
    [InlineData(0, 5, 5, 50, 110)] // 20×5 10×1
    [InlineData(2, 5, 5, 0, 150)]  // 50×2 20×2 10×1
    [InlineData(1, 5, 5, 0, 150)]  // 50×1 20×5
    [InlineData(0, 5, 0, 0, 100)]  // 20×5
    [InlineData(5, 5, 5, 50, 90)]  // 20×4 10×1
    [InlineData(0, 1, 4, 0, 60)]   // 20×1 10×4
    public void Les_billets_sortent_par_passages_de_4_au_plus(int f, int t, int te, int jammed, int amount)
    {
        var stock = Notes(f, t, te);
        var (machine, spy) = Machine(stock, Jam(jammed));

        var result = machine.Execute(new(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(amount, result.Notes.Value);
        Assert.All(spy.DispenseCalls, call =>
        {
            Assert.InRange(call.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass);
            Assert.True(call.Result);
        });
        Assert.Equal(result.Notes, spy.SuccessfullyDispensed);
        Assert.Equal(stock - result.Notes, spy.Stock);
        Assert.Empty(spy.LoadCalls);
    }

    [Fact]
    public void Un_retrait_ne_charge_jamais_de_billets()
    {
        var (machine, spy) = Machine(Full);

        machine.Execute(new(250, 0), new Operation.Withdraw(80));

        Assert.Empty(spy.LoadCalls);
    }

    // ---- Sans mémoire ----

    [Fact]
    public void La_machine_ne_garde_rien_entre_deux_appels()
    {
        var (machine, spy) = Machine(Full);

        var first = machine.Execute(new(250, 0), new Operation.Withdraw(60));
        // Même compte d'entrée : la machine ne doit pas se souvenir du premier retrait.
        var second = machine.Execute(new(250, 0), new Operation.Withdraw(60));

        Assert.Equal(first, second);
        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 0, 1), 0, new(190, 60)), second);
        Assert.Equal(Notes(3, 5, 3), spy.Stock);
    }

    [Fact]
    public void La_machine_lit_le_stock_courant_du_module()
    {
        var (machine, spy) = Machine(Notes(1, 0, 0));

        Assert.Equal(AtmStatus.Ok, machine.Execute(new(250, 0), new Operation.Withdraw(50)).Status);
        var second = machine.Execute(new(250, 0), new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.CannotDispense, second.Status);
        Assert.Equal(Notes(0, 0, 0), spy.Stock);
    }
}
