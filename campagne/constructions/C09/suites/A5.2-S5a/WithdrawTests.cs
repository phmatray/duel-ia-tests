using static Atm.Tests.Machine;

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
        var (r, d) = Withdraw(250, 0, Full, null, amount);
        AssertRefused(AtmStatus.InvalidAmount, r, d, 250, 0, Full);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, stock vide, cassette bloquée : tout échouerait.
        var (r, d) = Withdraw(0, 200, Notes(0, 0, 0), Denomination.Ten, amount);
        AssertRefused(AtmStatus.InvalidAmount, r, d, 0, 200, Notes(0, 0, 0));
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 200)]
    [InlineData(190, 10)]
    [InlineData(150, 50)]
    public void Atteindre_exactement_200_est_permis(int withdrawn, int amount)
    {
        var (r, _) = Withdraw(250, withdrawn, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(withdrawn + amount, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    [InlineData(150, 60)]
    public void Depasser_200_est_refuse(int withdrawn, int amount)
    {
        var (r, d) = Withdraw(250, withdrawn, Full, null, amount);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, d, 250, withdrawn, Full);
    }

    [Theory]
    [InlineData(90, 110)]
    [InlineData(80, 120)]
    [InlineData(50, 150)]
    [InlineData(0, 200)]
    public void Les_frais_ne_comptent_pas_dans_le_plafond_du_jour(int withdrawn, int amount)
    {
        // withdrawn + amount = 200 pile ; avec les frais on dépasserait 200, mais ils ne comptent pas.
        var (r, d) = Withdraw(250, withdrawn, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(new AccountState(250 - amount - 5, 200), r.Account);
        Assert.Equal(Full - r.Notes, d.Stock);
    }

    [Fact]
    public void Plafond_passe_avant_le_solde_et_les_billets()
    {
        var (r, d) = Withdraw(0, 200, Notes(0, 0, 0), Denomination.Fifty, 10);
        AssertRefused(AtmStatus.DailyLimitExceeded, r, d, 0, 200, Notes(0, 0, 0));
    }

    // ---- Contrôle 3 : solde ----

    [Theory]
    [InlineData(100, 100)] // pas de frais
    [InlineData(10, 10)]
    [InlineData(115, 110)] // frais de 5
    [InlineData(195, 190)]
    public void Solde_couvrant_exactement_montant_et_frais_est_suffisant(int balance, int amount)
    {
        var (r, _) = Withdraw(balance, 0, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Theory]
    [InlineData(95, 100)]
    [InlineData(0, 10)]
    [InlineData(110, 110)] // couvre le montant, pas les frais
    [InlineData(190, 190)]
    [InlineData(105, 110)]
    public void Solde_ne_couvrant_pas_montant_et_frais_est_insuffisant(int balance, int amount)
    {
        var (r, d) = Withdraw(balance, 0, Full, null, amount);
        AssertRefused(AtmStatus.InsufficientFunds, r, d, balance, 0, Full);
    }

    [Fact]
    public void Solde_passe_avant_les_billets()
    {
        var (r, d) = Withdraw(0, 0, Notes(0, 0, 0), Denomination.Twenty, 50);
        AssertRefused(AtmStatus.InsufficientFunds, r, d, 0, 0, Notes(0, 0, 0));
    }

    // ---- Les frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(190, 5)]
    public void Frais_de_5_strictement_au_dela_de_100(int amount, int expectedFee)
    {
        var (r, _) = Withdraw(250, 0, Full, null, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedFee, r.Fee);
        Assert.Equal(250 - amount - expectedFee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    // ---- Contrôle 4 : combinaison ----

    [Fact]
    public void Stock_vide_ne_peut_rien_distribuer()
    {
        var (r, d) = Withdraw(250, 0, Notes(0, 0, 0), null, 10);
        AssertRefused(AtmStatus.CannotDispense, r, d, 250, 0, Notes(0, 0, 0));
    }

    [Theory]
    [InlineData(1, 0, 0, 30)]  // un 50 ne fait pas 30
    [InlineData(0, 5, 0, 10)]  // que des 20
    [InlineData(0, 5, 0, 30)]
    [InlineData(5, 0, 0, 120)] // que des 50
    [InlineData(1, 1, 0, 80)]  // 50+20 = 70 au mieux
    [InlineData(0, 0, 5, 60)]  // 5 x 10 = 50 au plus
    public void Aucune_combinaison_exacte(int f, int t, int d10, int amount)
    {
        var stock = Notes(f, t, d10);
        var (r, d) = Withdraw(250, 0, stock, null, amount);
        AssertRefused(AtmStatus.CannotDispense, r, d, 250, 0, stock);
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

        var (r, d) = Withdraw(250, 0, stock, jammed, amount);
        AssertRefused(AtmStatus.CannotDispense, r, d, 250, 0, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 100, 0, 5, 0)]  // 5 x 20
    [InlineData(Denomination.Twenty, 60, 1, 0, 1)]  // 50 + 10
    [InlineData(Denomination.Ten, 70, 1, 1, 0)]     // 50 + 20
    [InlineData(Denomination.Ten, 90, 1, 2, 0)]     // 50 + 20 + 20
    public void La_cassette_bloquee_est_evitee(Denomination jammed, int amount, int f, int t, int d10)
    {
        var (r, d) = Withdraw(250, 0, Full, jammed, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Notes(f, t, d10), r.Notes);
        Assert.Equal(Full - Notes(f, t, d10), d.Stock);
    }

    [Theory]
    [InlineData(5, 5, 5, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 130, 2, 1, 1)]
    [InlineData(5, 5, 5, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, 100, 2, 0, 0)]
    [InlineData(1, 5, 5, 190, 1, 5, 4)]   // 50 + 100 + 40 : 10 billets
    [InlineData(0, 3, 5, 60, 0, 3, 0)]    // 3 x 20 plutôt que 6 x 10
    [InlineData(1, 3, 0, 60, 0, 3, 0)]    // 50 + 10 impossible faute de 10
    [InlineData(5, 1, 2, 60, 1, 0, 1)]    // 50 + 10 plutôt que 20 + 4 x 10
    [InlineData(2, 5, 0, 110, 1, 3, 0)]   // 50 + 3 x 20 car 2 x 50 laisse 10
    [InlineData(5, 0, 5, 110, 2, 0, 1)]
    [InlineData(0, 5, 5, 150, 0, 5, 5)]
    public void Combinaison_au_moins_de_billets(int sf, int st, int s10, int amount, int f, int t, int d10)
    {
        var stock = Notes(sf, st, s10);
        var (r, d) = Withdraw(250, 0, stock, null, amount);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Notes(f, t, d10), r.Notes);
        Assert.Equal(amount, r.Notes.Value);
        Assert.Equal(stock - r.Notes, d.Stock);
    }

    // ---- La sortie ----

    [Theory]
    [InlineData(5, 5, 5, 190)] // 5 billets
    [InlineData(1, 5, 5, 190)] // 10 billets
    [InlineData(0, 5, 5, 150)] // 10 billets
    [InlineData(5, 5, 5, 130)]
    [InlineData(5, 5, 5, 10)]
    public void Les_billets_sortent_par_passages_de_4_au_plus(int sf, int st, int s10, int amount)
    {
        var stock = Notes(sf, st, s10);
        var spy = new SpyDispenser(new MoneyDispenser(stock));
        var r = new CashMachine(spy).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.NotEmpty(spy.DispenseCalls);
        Assert.All(spy.DispenseCalls, pass =>
        {
            Assert.InRange(pass.Total, 1, 4);
            Assert.True(pass.Fifties >= 0 && pass.Twenties >= 0 && pass.Tens >= 0);
        });
        Assert.Equal(r.Notes, spy.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(stock - r.Notes, spy.Stock);
        Assert.Empty(spy.LoadCalls);
    }

    [Fact]
    public void Un_refus_ne_touche_pas_au_module()
    {
        var spy = new SpyDispenser(new MoneyDispenser(Full));
        var machine = new CashMachine(spy);

        foreach (var (account, amount) in new[]
                 {
                     (new AccountState(250, 0), 5),       // InvalidAmount
                     (new AccountState(250, 200), 10),    // DailyLimitExceeded
                     (new AccountState(0, 0), 10),        // InsufficientFunds
                 })
        {
            var r = machine.Execute(account, new Operation.Withdraw(amount));
            Assert.NotEqual(AtmStatus.Ok, r.Status);
        }

        var empty = new SpyDispenser(new MoneyDispenser(Notes(0, 0, 0)));
        Assert.Equal(AtmStatus.CannotDispense,
            new CashMachine(empty).Execute(new AccountState(250, 0), new Operation.Withdraw(10)).Status);

        Assert.Empty(spy.DispenseCalls);
        Assert.Empty(spy.LoadCalls);
        Assert.Empty(empty.DispenseCalls);
        Assert.Empty(empty.LoadCalls);
    }

    // ---- Machine sans mémoire ----

    [Fact]
    public void La_machine_ne_garde_rien_entre_deux_appels()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);

        // Le compte fourni fait foi : le premier retrait n'influence pas le second, seul le module a changé.
        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(190));
        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(Notes(3, 2, 0), first.Notes);
        Assert.Equal(Notes(2, 3, 5), dispenser.Stock);

        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(190));
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(new AccountState(55, 190), second.Account);
        Assert.Equal(Notes(2, 3, 3), second.Notes);
        Assert.Equal(Notes(0, 0, 2), dispenser.Stock);
    }
}
