using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var r = Withdraw(Acc(250, 0), amount, Full);
        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    [Fact]
    public void Montant_invalide_passe_avant_le_plafond_les_fonds_et_les_billets()
    {
        // Tous les autres contrôles échoueraient aussi.
        var r = Withdraw(Acc(0, 200), -10, N(), Denomination.Fifty);
        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_200_est_permis(int withdrawn, int amount)
    {
        var r = Withdraw(Acc(250, withdrawn), amount, Full);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(200, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    public void Depasser_200_est_refuse(int withdrawn, int amount)
    {
        var r = Withdraw(Acc(250, withdrawn), amount, Full);
        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : permis, même si 90 + 110 + 5 dépasse 200.
        var r = Withdraw(Acc(250, 90), 110, Full);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(135, 200), r.Account);
    }

    [Fact]
    public void Plafond_passe_avant_les_fonds_et_les_billets()
    {
        var r = Withdraw(Acc(0, 200), 10, N());
        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    // ---- Contrôle 3 : fonds ----

    [Theory]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    [InlineData(115, 110)]
    [InlineData(205, 200)]
    public void Solde_egal_au_montant_plus_frais_suffit(int balance, int amount)
    {
        var r = Withdraw(Acc(balance, 0), amount, Full);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(200, 200)]
    public void Solde_inferieur_au_montant_plus_frais_est_refuse(int balance, int amount)
    {
        var r = Withdraw(Acc(balance, 0), amount, Full);
        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    [Fact]
    public void Fonds_passent_avant_les_billets()
    {
        var r = Withdraw(Acc(0, 0), 10, N());
        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    // ---- Frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_au_dessus_de_100_seulement(int amount, int fee)
    {
        var r = Withdraw(Acc(250, 0), amount, Full);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(250 - amount - fee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    // ---- Contrôle 4 : billets ----

    [Fact]
    public void Stock_vide_ne_peut_rien_distribuer()
    {
        var r = Withdraw(Acc(250, 0), 10, N());
        Assert.Equal(AtmStatus.CannotDispense, r.Status);
    }

    [Theory]
    [InlineData(30, 0, 5, 0)]   // que des 20 : 30 impossible
    [InlineData(10, 5, 5, 0)]   // pas de 10
    [InlineData(70, 1, 0, 1)]   // 50 + 10 ne font pas 70
    [InlineData(200, 1, 5, 0)]  // 50 + 20×5 = 150 au plus
    [InlineData(180, 0, 5, 5)]  // 20×5 + 10×5 = 150 au plus
    public void Aucune_combinaison_exacte(int amount, int fifties, int twenties, int tens)
    {
        var r = Withdraw(Acc(250, 0), amount, N(fifties, twenties, tens));
        Assert.Equal(AtmStatus.CannotDispense, r.Status);
    }

    [Theory]
    [InlineData(50, 50, 1, 0, 0)]
    [InlineData(20, 20, 0, 1, 0)]
    [InlineData(10, 10, 0, 0, 1)]
    [InlineData(60, 50, 1, 0, 1)]   // 50 + 10 serait possible sans la panne
    public void La_cassette_bloquee_compte_pour_zero(int amount, int jammed, int fifties, int twenties, int tens)
    {
        var r = Withdraw(Acc(250, 0), amount, N(fifties, twenties, tens), Jam(jammed));
        Assert.Equal(AtmStatus.CannotDispense, r.Status);
    }

    [Theory]
    [InlineData(40, 20, 5, 5, 5, 0, 0, 4)]   // 20 bloquée : 10×4 au lieu de 20×2
    [InlineData(100, 50, 5, 5, 0, 0, 5, 0)]  // 50 bloquée : 20×5 au lieu de 50×2
    [InlineData(60, 10, 1, 3, 5, 0, 3, 0)]   // 10 bloquée : 20×3 au lieu de 50 + 10
    [InlineData(70, 20, 1, 5, 5, 1, 0, 2)]   // 20 bloquée : 50 + 10×2 au lieu de 50 + 20
    public void La_combinaison_evite_la_cassette_bloquee(int amount, int jammed, int sf, int st, int sd, int ef, int et, int ed)
    {
        var (machine, spy) = Machine(N(sf, st, sd), Jam(jammed));
        var r = machine.Execute(Acc(250, 0), new Operation.Withdraw(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(ef, et, ed), r.Notes);
        Assert.Equal(N(sf, st, sd) - N(ef, et, ed), spy.Stock);
    }

    [Theory]
    [InlineData(60, 5, 5, 5, 1, 0, 1)]   // 50+10 plutôt que 20×3
    [InlineData(60, 1, 3, 0, 0, 3, 0)]   // pas de 10 : 20×3 (glouton échouerait)
    [InlineData(80, 1, 4, 0, 0, 4, 0)]   // 50 + 30 impossible : 20×4
    [InlineData(110, 1, 3, 0, 1, 3, 0)]  // 50 + 20×3
    [InlineData(130, 2, 4, 0, 1, 4, 0)]  // 50×2 + 30 impossible : 50 + 20×4
    [InlineData(40, 5, 5, 5, 0, 2, 0)]   // 20×2 plutôt que 20 + 10×2
    [InlineData(30, 5, 5, 5, 0, 1, 1)]
    [InlineData(30, 5, 0, 5, 0, 0, 3)]
    [InlineData(200, 5, 5, 5, 4, 0, 0)]
    [InlineData(190, 5, 5, 5, 3, 2, 0)]
    [InlineData(150, 0, 5, 5, 0, 5, 5)]  // 10 billets : tout le stock utile
    [InlineData(90, 1, 0, 5, 1, 0, 4)]
    public void La_combinaison_compte_le_moins_de_billets(int amount, int sf, int st, int sd, int ef, int et, int ed)
    {
        var (machine, spy) = Machine(N(sf, st, sd));
        var r = machine.Execute(Acc(250, 0), new Operation.Withdraw(amount));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(ef, et, ed), r.Notes);
        Assert.Equal(N(sf, st, sd) - N(ef, et, ed), spy.Stock);
    }

    // ---- Sortie ----

    [Theory]
    [InlineData(150, 0, 5, 5)]  // 10 billets
    [InlineData(130, 2, 4, 0)]  // 5 billets
    [InlineData(80, 0, 5, 0)]   // 4 billets
    [InlineData(120, 0, 5, 5)]  // 20×5 + 10×2 = 7 billets
    [InlineData(10, 0, 0, 1)]
    public void Les_billets_sortent_par_le_module_en_passages_de_4_au_plus(int amount, int fifties, int twenties, int tens)
    {
        var (machine, spy) = Machine(N(fifties, twenties, tens));
        var r = machine.Execute(Acc(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.NotEmpty(spy.Dispenses);
        Assert.All(spy.Dispenses, d =>
        {
            Assert.True(d.Result, $"passage refusé par le module : {d.Notes}");
            Assert.InRange(d.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass);
        });
        Assert.Equal(r.Notes, spy.Dispensed);
        Assert.Equal(amount, r.Notes.Value);
        Assert.Empty(spy.Loads);
    }

    // ---- Refus ----

    public static TheoryData<int, int, int, int, int, int, int, AtmStatus> Refus => new()
    {
        { 250, 0, -10, 5, 5, 5, 0, AtmStatus.InvalidAmount },
        { 250, 0, 0, 5, 5, 5, 0, AtmStatus.InvalidAmount },
        { 250, 0, 5, 5, 5, 5, 0, AtmStatus.InvalidAmount },
        { 250, 150, 60, 5, 5, 5, 0, AtmStatus.DailyLimitExceeded },
        { 250, 0, 220, 5, 5, 5, 0, AtmStatus.DailyLimitExceeded },
        { 110, 0, 110, 5, 5, 5, 0, AtmStatus.InsufficientFunds },
        { 250, 0, 50, 1, 0, 0, 50, AtmStatus.CannotDispense },
        { 250, 0, 30, 0, 5, 0, 0, AtmStatus.CannotDispense },
        { 250, 0, 150, 1, 5, 0, 20, AtmStatus.CannotDispense },
    };

    [Theory]
    [MemberData(nameof(Refus))]
    public void Un_refus_ne_change_rien(int balance, int withdrawn, int amount, int f, int t, int d, int jammed, AtmStatus status)
    {
        var (machine, spy) = Machine(N(f, t, d), Jam(jammed));
        var r = machine.Execute(Acc(balance, withdrawn), new Operation.Withdraw(amount));

        Assert.Equal(new AtmResult(status, N(), 0, Acc(balance, withdrawn)), r);
        Assert.Equal(N(f, t, d), spy.Stock);
        Assert.Equal(N(), spy.Dispensed);
        Assert.Empty(spy.Loads);
    }
}
