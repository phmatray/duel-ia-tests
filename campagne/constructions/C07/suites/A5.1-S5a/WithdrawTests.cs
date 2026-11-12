using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var (atm, dispenser) = RecordingAtm(Full);
        var r = atm.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Empty(dispenser.Dispensed);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, aucun billet : seul le montant compte.
        var (atm, _) = Machine(N(0, 0, 0), Denomination.Ten);
        var r = atm.Execute(Acc(0, 200), Withdraw(amount));

        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    [InlineData(100, 100)]
    public void Atteindre_exactement_le_plafond_est_permis(int withdrawn, int amount)
    {
        var (atm, _) = Machine(Full);
        var r = atm.Execute(Acc(250, withdrawn), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(200, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(200, 10)]
    [InlineData(190, 20)]
    public void Depasser_le_plafond_est_refuse(int withdrawn, int amount)
    {
        var (atm, dispenser) = RecordingAtm(Full);
        var r = atm.Execute(Acc(250, withdrawn), Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, Acc(250, withdrawn)), r);
        Assert.Empty(dispenser.Dispensed);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 200 + 5 de frais : le plafond porte sur 200 seulement.
        var (atm, _) = Machine(Full);
        var r = atm.Execute(Acc(205, 0), Withdraw(200));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(0, 200), r.Account);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond_avec_retrait_anterieur()
    {
        var (atm, _) = Machine(Full);
        var r = atm.Execute(Acc(250, 90), Withdraw(110));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Acc(135, 200), r.Account);
    }

    [Fact]
    public void Plafond_passe_avant_fonds_insuffisants()
    {
        var (atm, _) = Machine(Full);
        var r = atm.Execute(Acc(0, 200), Withdraw(10));

        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    [Fact]
    public void Plafond_passe_avant_impossible_a_servir()
    {
        var (atm, _) = Machine(N(0, 0, 0));
        var r = atm.Execute(Acc(250, 200), Withdraw(10));

        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    // ---- Contrôle 3 : fonds insuffisants, frais compris ----

    [Theory]
    [InlineData(10, 0, 10)]   // pas de frais, solde juste suffisant
    [InlineData(100, 0, 100)] // 100 : pas de frais
    [InlineData(115, 0, 110)] // 110 + 5
    [InlineData(205, 0, 200)] // 200 + 5
    public void Solde_egal_a_montant_plus_frais_suffit(int balance, int withdrawn, int amount)
    {
        var (atm, _) = Machine(Full);
        var r = atm.Execute(Acc(balance, withdrawn), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    [InlineData(95, 100)]
    [InlineData(110, 110)] // les frais font basculer
    [InlineData(114, 110)]
    [InlineData(200, 200)]
    public void Solde_inferieur_a_montant_plus_frais_est_refuse(int balance, int amount)
    {
        var (atm, dispenser) = RecordingAtm(Full);
        var r = atm.Execute(Acc(balance, 0), Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, Acc(balance, 0)), r);
        Assert.Empty(dispenser.Dispensed);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Fonds_insuffisants_passe_avant_impossible_a_servir_stock_vide()
    {
        var (atm, _) = Machine(N(0, 0, 0));
        var r = atm.Execute(Acc(0, 0), Withdraw(50));

        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    [Fact]
    public void Fonds_insuffisants_passe_avant_impossible_a_servir_cassette_bloquee()
    {
        var (atm, _) = Machine(N(1, 0, 0), Denomination.Fifty);
        var r = atm.Execute(Acc(40, 0), Withdraw(50));

        Assert.Equal(AtmStatus.InsufficientFunds, r.Status);
    }

    // ---- Contrôle 4 : impossible à servir ----

    [Theory]
    [InlineData(0, 0, 0, 10)]
    [InlineData(1, 0, 0, 30)]  // 50 seul ne fait pas 30
    [InlineData(0, 5, 0, 10)]  // 20 ne fait pas 10
    [InlineData(0, 5, 0, 110)] // 20×5 = 100, il manque 10
    [InlineData(1, 1, 0, 60)]  // 50+20 = 70, ni 50 ni 20 ne font 60
    [InlineData(0, 0, 5, 60)]  // 10×5 = 50
    public void Aucune_combinaison_exacte(int fifties, int twenties, int tens, int amount)
    {
        var stock = N(fifties, twenties, tens);
        var (atm, dispenser) = RecordingAtm(stock);
        var r = atm.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Acc(250, 0)), r);
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
            Denomination.Fifty => N(5, 0, 0),
            Denomination.Twenty => N(0, 5, 0),
            _ => N(0, 0, 5),
        };
        var (atm, dispenser) = RecordingAtm(stock, jammed);
        var r = atm.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Empty(dispenser.Dispensed);
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void Cassette_10_bloquee_force_une_combinaison_sans_10()
    {
        // 60 = 50+10 (2 billets) d'habitude ; ici 20×3.
        var (atm, dispenser) = Machine(N(5, 5, 5), Denomination.Ten);
        var r = atm.Execute(Acc(250, 0), Withdraw(60));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 3, 0), r.Notes);
        Assert.Equal(N(5, 2, 5), dispenser.Stock);
    }

    [Fact]
    public void Cassette_20_bloquee_force_une_combinaison_sans_20()
    {
        // 40 = 20×2 d'habitude ; ici 10×4.
        var (atm, dispenser) = Machine(N(5, 5, 5), Denomination.Twenty);
        var r = atm.Execute(Acc(250, 0), Withdraw(40));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 0, 4), r.Notes);
        Assert.Equal(N(5, 5, 1), dispenser.Stock);
    }

    // ---- La combinaison : le moins de billets ----

    [Theory]
    [InlineData(5, 5, 5, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 20, 0, 1, 0)]
    [InlineData(5, 5, 5, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 50, 1, 0, 0)]
    [InlineData(5, 5, 5, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 70, 1, 1, 0)]
    [InlineData(5, 5, 5, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 100, 2, 0, 0)]
    [InlineData(5, 5, 5, 130, 2, 1, 1)]
    [InlineData(5, 5, 5, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, 200, 4, 0, 0)]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // 50+10 impossible
    [InlineData(0, 5, 5, 100, 0, 5, 0)]  // sans 50
    [InlineData(0, 2, 5, 90, 0, 2, 5)]   // 20 manquants compensés en 10
    [InlineData(1, 0, 5, 100, 1, 0, 5)]  // un seul 50
    [InlineData(5, 0, 5, 80, 1, 0, 3)]   // sans 20
    [InlineData(5, 5, 0, 60, 0, 3, 0)]   // sans 10 : pas 50+10
    [InlineData(5, 5, 0, 80, 0, 4, 0)]   // sans 10 : 50+… ne tombe pas juste
    public void Choisit_la_combinaison_qui_compte_le_moins_de_billets(
        int sf, int st, int sn, int amount, int ef, int et, int en)
    {
        var stock = N(sf, st, sn);
        var (atm, dispenser) = Machine(stock);
        var r = atm.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(ef, et, en), r.Notes);
        Assert.Equal(amount, r.Notes.Value);
        Assert.Equal(stock - r.Notes, dispenser.Stock);
    }

    // ---- Les frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_au_dela_de_100_strictement(int amount, int fee)
    {
        var (atm, _) = Machine(Full);
        var r = atm.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(fee, r.Fee);
        Assert.Equal(Acc(250 - amount - fee, amount), r.Account);
    }

    // ---- La sortie : passages de 4 billets au plus ----

    [Fact]
    public void Plus_de_4_billets_sortent_en_plusieurs_passages()
    {
        // 150 sans 50 : 20×5 + 10×5 = 10 billets.
        var (atm, dispenser) = RecordingAtm(N(5, 5, 5), Denomination.Fifty);
        var r = atm.Execute(Acc(250, 0), Withdraw(150));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(0, 5, 5), r.Notes);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(95, 150), r.Account);
        Assert.Equal(N(5, 0, 0), dispenser.Stock);

        Assert.True(dispenser.Dispensed.Count >= 3);
        Assert.All(dispenser.Dispensed, pass => Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(r.Notes, dispenser.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
    }

    [Theory]
    [InlineData(5, 5, 5, 200)] // 50×4 : un seul passage possible
    [InlineData(0, 5, 5, 130)] // 20×5 + 10×3
    [InlineData(5, 0, 5, 200)] // 50×4
    [InlineData(0, 0, 5, 50)]  // 10×5 : deux passages
    [InlineData(0, 5, 0, 100)] // 20×5 : deux passages
    public void Chaque_passage_compte_au_plus_4_billets_et_leur_somme_fait_la_combinaison(
        int sf, int st, int sn, int amount)
    {
        var stock = N(sf, st, sn);
        var (atm, dispenser) = RecordingAtm(stock);
        var r = atm.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.NotEmpty(dispenser.Dispensed);
        Assert.All(dispenser.Dispensed, pass => Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(r.Notes, dispenser.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(stock - r.Notes, dispenser.Stock);
        Assert.Empty(dispenser.Loaded);
    }

    // ---- Machine sans mémoire, état porté par le module ----

    [Fact]
    public void Deux_retraits_successifs_epuisent_le_module()
    {
        var (atm, dispenser) = Machine(N(1, 0, 0));

        var first = atm.Execute(Acc(250, 0), Withdraw(50));
        var second = atm.Execute(first.Account, Withdraw(50));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, Acc(200, 50)), second);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Le_resultat_ne_depend_que_du_compte_passe_en_argument()
    {
        var (atm, _) = Machine(Full);

        atm.Execute(Acc(250, 0), Withdraw(200));
        var r = atm.Execute(Acc(250, 0), Withdraw(100));

        // La machine ne retient pas le premier retrait : le plafond vient du compte fourni.
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Acc(150, 100), r.Account);
    }
}
