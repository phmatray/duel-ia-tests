using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = Notes(5, 5, 5);

    // ---------- Contrôle 1 : montant invalide ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (machine, dispenser) = Spied(Full);

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Account(250, 0)), result);
        Assert.Empty(dispenser.Dispensed);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, stock vide : tout échouerait, mais le contrôle 1 passe en premier.
        var (machine, _) = Real(Notes(0, 0, 0));

        var result = machine.Execute(Account(0, 200), Withdraw(amount));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    // ---------- Contrôle 2 : plafond du jour ----------

    [Theory]
    [InlineData(0, 200)]
    [InlineData(100, 100)]
    [InlineData(190, 10)]
    [InlineData(150, 50)]
    public void Atteindre_exactement_le_plafond_est_accepte(int withdrawnToday, int amount)
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Account(250, withdrawnToday), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(200, result.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    [InlineData(150, 60)]
    public void Depasser_le_plafond_est_refuse(int withdrawnToday, int amount)
    {
        var (machine, dispenser) = Spied(Full);

        var result = machine.Execute(Account(250, withdrawnToday), Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, Account(250, withdrawnToday)), result);
        Assert.Empty(dispenser.Dispensed);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : le plafond est atteint pile, les 5 € de frais n'y entrent pas.
        var (machine, _) = Real(Full);

        var result = machine.Execute(Account(250, 90), Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 1), 5, Account(135, 200)), result);
    }

    [Fact]
    public void Plafond_passe_avant_solde_insuffisant()
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Account(0, 200), Withdraw(10));

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void Plafond_passe_avant_impossibilite_de_distribuer()
    {
        var (machine, _) = Real(Notes(0, 0, 0));

        var result = machine.Execute(Account(250, 200), Withdraw(10));

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    // ---------- Contrôle 3 : solde insuffisant (frais compris) ----------

    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(115, 110, 5)]
    [InlineData(205, 200, 5)]
    public void Solde_egal_au_montant_plus_frais_est_accepte(int balance, int amount, int fee)
    {
        var (machine, _) = Real(Full);

        var result = machine.Execute(Account(balance, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(0, result.Account.Balance);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(114, 110)]
    [InlineData(200, 200)]
    public void Solde_inferieur_au_montant_plus_frais_est_refuse(int balance, int amount)
    {
        var (machine, dispenser) = Spied(Full);

        var result = machine.Execute(Account(balance, 0), Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, Account(balance, 0)), result);
        Assert.Empty(dispenser.Dispensed);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Solde_insuffisant_passe_avant_impossibilite_de_distribuer_stock_vide()
    {
        var (machine, _) = Real(Notes(0, 0, 0));

        var result = machine.Execute(Account(0, 0), Withdraw(10));

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    [Fact]
    public void Solde_insuffisant_passe_avant_impossibilite_de_distribuer_cassette_bloquee()
    {
        var (machine, _) = Real(Notes(1, 0, 0), Denomination.Fifty);

        var result = machine.Execute(Account(40, 0), Withdraw(50));

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    [Fact]
    public void Solde_insuffisant_a_cause_des_frais_passe_avant_impossibilite_de_distribuer()
    {
        var (machine, _) = Real(Notes(0, 0, 0));

        var result = machine.Execute(Account(110, 0), Withdraw(110));

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    // ---------- Les frais ----------

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
        Assert.Equal(250 - amount - expectedFee, result.Account.Balance);
        Assert.Equal(amount, result.Account.WithdrawnToday);
    }

    // ---------- Contrôle 4 : impossibilité de distribuer ----------

    [Fact]
    public void Stock_vide_ne_peut_rien_distribuer()
    {
        var (machine, dispenser) = Spied(Notes(0, 0, 0));

        var result = machine.Execute(Account(250, 0), Withdraw(10));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Account(250, 0)), result);
        Assert.Empty(dispenser.Dispensed);
    }

    [Theory]
    [InlineData(5, 0, 0, 30)]   // que des 50
    [InlineData(5, 0, 0, 60)]
    [InlineData(0, 5, 0, 10)]   // que des 20
    [InlineData(0, 5, 0, 50)]
    [InlineData(1, 1, 0, 40)]   // 50 + 20 ne font pas 40 avec un seul 20
    [InlineData(1, 0, 4, 100)]  // 50 + 40 au plus
    [InlineData(0, 0, 5, 60)]   // 50 € de 10 au plus
    public void Aucune_combinaison_exacte_avec_le_stock(int fifties, int twenties, int tens, int amount)
    {
        var stock = Notes(fifties, twenties, tens);
        var (machine, dispenser) = Spied(stock);

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Account(250, 0)), result);
        Assert.Empty(dispenser.Dispensed);
        Assert.Equal(stock, dispenser.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50)]
    [InlineData(Denomination.Twenty, 20)]
    [InlineData(Denomination.Ten, 10)]
    public void La_cassette_bloquee_compte_pour_zero(Denomination jammed, int amount)
    {
        var stock = jammed switch
        {
            Denomination.Fifty => Notes(5, 0, 0),
            Denomination.Twenty => Notes(0, 5, 0),
            _ => Notes(0, 0, 5),
        };
        var (machine, dispenser) = Spied(stock, jammed);

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Account(250, 0)), result);
        Assert.Empty(dispenser.Dispensed);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 100, 0, 5, 0)]  // sans les 50 : 20×5
    [InlineData(Denomination.Twenty, 60, 1, 0, 1)]  // sans les 20 : 50 + 10
    [InlineData(Denomination.Ten, 90, 1, 2, 0)]     // sans les 10 : 50 + 20×2
    [InlineData(Denomination.Ten, 70, 1, 1, 0)]
    public void Avec_une_cassette_bloquee_on_utilise_les_autres(
        Denomination jammed, int amount, int fifties, int twenties, int tens)
    {
        var (machine, dispenser) = Spied(Full, jammed);

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(fifties, twenties, tens), result.Notes);
        Assert.Equal(0, result.Notes.Of(jammed));
        Assert.All(dispenser.Dispensed, pass => Assert.Equal(0, pass.Of(jammed)));
    }

    // ---------- La combinaison : le moins de billets ----------

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
    public void Stock_plein_donne_le_minimum_de_billets(int amount, int fifties, int twenties, int tens)
    {
        var (machine, dispenser) = Real(Full);

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(fifties, twenties, tens), result.Notes);
        Assert.Equal(Full - Notes(fifties, twenties, tens), dispenser.Stock);
    }

    [Theory]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // le glouton prendrait 50 et resterait bloqué à 10
    [InlineData(1, 4, 0, 80, 0, 4, 0)]
    [InlineData(3, 5, 0, 160, 2, 3, 0)]  // 50×3 + 10 impossible : 50×2 + 20×3
    [InlineData(1, 5, 0, 110, 1, 3, 0)]  // 50 + 20×3
    [InlineData(5, 0, 5, 80, 1, 0, 3)]   // sans 20 : 50 + 10×3
    [InlineData(0, 5, 5, 150, 0, 5, 5)]  // sans 50 : tout le stock
    [InlineData(1, 1, 5, 120, 1, 1, 5)]
    [InlineData(2, 5, 0, 120, 2, 1, 0)]  // 20×6 serait trop : 50×2 + 20
    public void Le_minimum_de_billets_tient_compte_du_stock(
        int sf, int st, int sn, int amount, int fifties, int twenties, int tens)
    {
        var (machine, _) = Real(Notes(sf, st, sn));

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Notes(fifties, twenties, tens), result.Notes);
        Assert.Equal(amount, result.Notes.Value);
    }

    // ---------- La sortie : passages de 4 billets au plus ----------

    [Theory]
    [InlineData(0, 5, 5, 150)]  // 10 billets
    [InlineData(4, 0, 0, 200)]  // 4 billets, un seul passage possible
    [InlineData(0, 5, 0, 100)]  // 5 billets
    [InlineData(1, 5, 5, 200)]  // 50 + 20×5 + 10×5 = 11 billets
    [InlineData(0, 0, 5, 50)]
    public void Les_billets_sortent_par_passages_de_4_au_plus(int sf, int st, int sn, int amount)
    {
        var stock = Notes(sf, st, sn);
        var (machine, spy) = Spied(stock);

        var result = machine.Execute(Account(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.NotEmpty(spy.Dispensed);
        Assert.All(spy.Dispensed, pass =>
        {
            Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass);
            Assert.True(pass.Fifties >= 0 && pass.Twenties >= 0 && pass.Tens >= 0);
        });
        var total = spy.Dispensed.Aggregate(NoteCounts.None, (sum, pass) => sum + pass);
        Assert.Equal(result.Notes, total);
        Assert.Equal(stock - result.Notes, spy.Stock);
    }

    // ---------- Le succès ----------

    [Fact]
    public void Succes_met_a_jour_solde_et_retrait_du_jour()
    {
        var (machine, dispenser) = Real(Full);

        var result = machine.Execute(Account(200, 40), Withdraw(130));

        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 1, 1), 5, Account(65, 170)), result);
        Assert.Equal(Notes(3, 4, 4), dispenser.Stock);
    }

    [Fact]
    public void Aucun_depot_lors_d_un_retrait()
    {
        var (machine, spy) = Spied(Full);

        machine.Execute(Account(250, 0), Withdraw(80));

        Assert.Empty(spy.Loaded);
    }
}
