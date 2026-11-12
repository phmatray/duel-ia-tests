using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    // ---------- Refus : aucun effet ----------

    private static void AssertRefused(Rig rig, AtmResult result, AtmStatus expected, AccountState account, NoteCounts stockBefore)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stockBefore, rig.Real.Stock);
        Assert.Empty(rig.Spy.SuccessfulPasses);
    }

    private static void AssertRefusal(int balance, int withdrawn, NoteCounts stock, int jammed, int amount, AtmStatus expected)
    {
        var rig = new Rig(stock, Jam(jammed));
        var account = Acc(balance, withdrawn);
        var result = rig.Run(account, Withdraw(amount));
        AssertRefused(rig, result, expected, account, stock);
    }

    // ---------- 1. InvalidAmount ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_InvalidAmount(int amount) =>
        AssertRefusal(250, 0, Full, 0, amount, AtmStatus.InvalidAmount);

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void InvalidAmount_passe_avant_le_plafond_les_fonds_et_les_billets(int amount) =>
        // Plafond atteint, solde nul, stock vide : seul le contrôle 1 doit parler.
        AssertRefusal(0, 200, N(0, 0, 0), 0, amount, AtmStatus.InvalidAmount);

    // ---------- 2. DailyLimitExceeded ----------

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(150, 60)]
    [InlineData(190, 20)]
    [InlineData(200, 10)]
    public void Depasser_200_dans_la_journee_est_DailyLimitExceeded(int withdrawn, int amount) =>
        AssertRefusal(250, withdrawn, Full, 0, amount, AtmStatus.DailyLimitExceeded);

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_200_est_permis(int withdrawn, int amount)
    {
        var rig = new Rig(Full);
        var result = rig.Run(Acc(250, withdrawn), Withdraw(amount));
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(withdrawn + amount, result.Account.WithdrawnToday);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : permis, même si 110 + 5 de frais ferait 205.
        var rig = new Rig(Full);
        var result = rig.Run(Acc(250, 90), Withdraw(110));
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(5, result.Fee);
        Assert.Equal(200, result.Account.WithdrawnToday);
        Assert.Equal(135, result.Account.Balance);
    }

    [Fact]
    public void DailyLimit_passe_avant_les_fonds_et_les_billets() =>
        AssertRefusal(0, 200, N(0, 0, 0), 0, 10, AtmStatus.DailyLimitExceeded);

    // ---------- 3. InsufficientFunds ----------

    [Theory]
    [InlineData(0, 10)]
    [InlineData(90, 100)]
    [InlineData(100, 110)] // 110 + 5
    [InlineData(110, 110)] // 110 + 5 > 110
    [InlineData(105, 110)]
    [InlineData(200, 200)] // 200 + 5
    public void Montant_plus_frais_au_dela_du_solde_est_InsufficientFunds(int balance, int amount) =>
        AssertRefusal(balance, 0, Full, 0, amount, AtmStatus.InsufficientFunds);

    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(100, 100, 0)] // frais nuls à 100
    [InlineData(115, 110, 5)] // pile montant + frais
    [InlineData(205, 200, 5)]
    public void Solde_egal_a_montant_plus_frais_est_permis(int balance, int amount, int fee)
    {
        var rig = new Rig(Full);
        var result = rig.Run(Acc(balance, 0), Withdraw(amount));
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(0, result.Account.Balance);
    }

    [Fact]
    public void InsufficientFunds_passe_avant_CannotDispense() =>
        AssertRefusal(0, 0, N(0, 0, 0), 0, 10, AtmStatus.InsufficientFunds);

    [Fact]
    public void InsufficientFunds_a_cause_des_seuls_frais_passe_avant_CannotDispense() =>
        // 110 seraient couverts, pas 115 ; et 110 est de toute façon indistribuable.
        AssertRefusal(110, 0, N(0, 0, 0), 0, 110, AtmStatus.InsufficientFunds);

    // ---------- 4. CannotDispense ----------

    [Theory]
    [InlineData(0, 0, 0, 0, 10)]   // stock vide
    [InlineData(5, 5, 0, 0, 30)]   // pas de 10 : 30 impossible
    [InlineData(5, 0, 0, 0, 20)]   // que des 50
    [InlineData(1, 1, 1, 0, 90)]   // au-delà du stock (80)
    [InlineData(0, 5, 5, 0, 10 * 5 + 20 * 5 + 10)] // 160 > 150 en stock
    [InlineData(1, 0, 0, 50, 50)]  // seul billet utile dans la cassette bloquée
    [InlineData(5, 5, 1, 10, 10)]  // les 10 bloqués
    [InlineData(5, 0, 5, 10, 60)]  // 10 bloqués : 50 + 10 impossible
    [InlineData(0, 5, 5, 20, 80)]  // 20 bloqués : 10×5 seulement, 80 impossible
    [InlineData(5, 5, 0, 50, 110)] // 50 bloqués, pas de 10 : 110 impossible
    public void Aucune_combinaison_exacte_disponible_est_CannotDispense(int f, int t, int d, int jammed, int amount) =>
        AssertRefusal(250, 0, N(f, t, d), jammed, amount, AtmStatus.CannotDispense);

    // ---------- Frais ----------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_strictement_au_dessus_de_100(int amount, int fee)
    {
        var rig = new Rig(Full);
        var result = rig.Run(Acc(250, 0), Withdraw(amount));
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(250 - amount - fee, result.Account.Balance);
        Assert.Equal(amount, result.Account.WithdrawnToday);
    }

    // ---------- Combinaison : le moins de billets ----------

    [Theory]
    // stock (50, 20, 10), bloquée, montant, billets attendus (50, 20, 10)
    [InlineData(5, 5, 5, 0, 10, 0, 0, 1)]
    [InlineData(5, 5, 5, 0, 20, 0, 1, 0)]
    [InlineData(5, 5, 5, 0, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 0, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 0, 60, 1, 0, 1)]
    [InlineData(5, 5, 5, 0, 80, 1, 1, 1)]
    [InlineData(5, 5, 5, 0, 90, 1, 2, 0)]
    [InlineData(5, 5, 5, 0, 100, 2, 0, 0)]
    [InlineData(5, 5, 5, 0, 190, 3, 2, 0)]
    [InlineData(5, 5, 5, 0, 200, 4, 0, 0)]
    // Le glouton échoue : il faut éviter le 50.
    [InlineData(1, 3, 0, 0, 60, 0, 3, 0)]
    [InlineData(1, 4, 0, 0, 80, 0, 4, 0)]
    [InlineData(5, 5, 0, 0, 130, 1, 4, 0)]
    [InlineData(3, 5, 0, 0, 160, 2, 3, 0)]
    // Stock limité : il faut descendre aux petites coupures.
    [InlineData(0, 1, 5, 0, 60, 0, 1, 4)]
    [InlineData(0, 0, 5, 0, 50, 0, 0, 5)]
    [InlineData(1, 0, 5, 0, 100, 1, 0, 5)]
    [InlineData(0, 5, 5, 0, 150, 0, 5, 5)]
    // Cassette bloquée : elle compte pour zéro.
    [InlineData(2, 2, 0, 50, 40, 0, 2, 0)]
    [InlineData(5, 5, 5, 50, 100, 0, 5, 0)]
    [InlineData(5, 5, 5, 50, 150, 0, 5, 5)]
    [InlineData(5, 5, 5, 20, 80, 1, 0, 3)]
    [InlineData(5, 5, 5, 20, 100, 2, 0, 0)]
    [InlineData(5, 5, 5, 10, 60, 0, 3, 0)]
    [InlineData(5, 5, 5, 10, 80, 0, 4, 0)]
    [InlineData(5, 5, 5, 10, 90, 1, 2, 0)]
    public void Rend_la_combinaison_au_plus_petit_nombre_de_billets(
        int f, int t, int d, int jammed, int amount, int ef, int et, int ed)
    {
        var stock = N(f, t, d);
        var expected = N(ef, et, ed);
        var rig = new Rig(stock, Jam(jammed));
        var result = rig.Run(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(expected, result.Notes);
        Assert.Equal(stock - expected, rig.Real.Stock);
    }

    // ---------- Sortie en plusieurs passages ----------

    [Theory]
    [InlineData(0, 5, 0, 0, 100)]   // 5 billets
    [InlineData(5, 5, 0, 0, 130)]   // 5 billets
    [InlineData(0, 5, 5, 0, 150)]   // 10 billets
    [InlineData(5, 5, 5, 50, 150)]  // 10 billets, 50 bloqués
    [InlineData(1, 0, 5, 0, 100)]   // 6 billets
    public void Plus_de_4_billets_sortent_en_plusieurs_passages(int f, int t, int d, int jammed, int amount)
    {
        var stock = N(f, t, d);
        var rig = new Rig(stock, Jam(jammed));
        var result = rig.Run(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(amount, result.Notes.Value);
        Assert.True(rig.Spy.SuccessfulPasses.Count >= 2);
        Assert.All(rig.Spy.SuccessfulPasses, p => Assert.InRange(p.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.Equal(result.Notes, rig.Spy.SuccessfulPasses.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(stock - result.Notes, rig.Real.Stock);
    }

    [Fact]
    public void Les_billets_sortent_par_le_module()
    {
        var rig = new Rig(Full);
        var result = rig.Run(Acc(250, 0), Withdraw(80));

        Assert.Equal(N(1, 1, 1), result.Notes);
        Assert.Equal(N(1, 1, 1), rig.Spy.SuccessfulPasses.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(N(4, 4, 4), rig.Real.Stock);
        Assert.Empty(rig.Spy.LoadCalls);
    }

    // ---------- Succès ----------

    [Theory]
    [InlineData(250, 0, 50)]
    [InlineData(100, 40, 60)]
    [InlineData(245, 70, 130)]
    [InlineData(55, 150, 50)]
    public void Succes_debite_le_solde_et_credite_le_retire(int balance, int withdrawn, int amount)
    {
        var fee = amount > 100 ? 5 : 0;
        var rig = new Rig(Full);
        var result = rig.Run(Acc(balance, withdrawn), Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(amount, result.Notes.Value);
        Assert.Equal(Acc(balance - amount - fee, withdrawn + amount), result.Account);
    }

    // ---------- Contrôle exhaustif sur le domaine du module ----------

    /// <summary>
    /// Pour tout stock, toute cassette bloquée et tout montant permis par le plafond,
    /// avec un solde suffisant : statut Ok + combinaison minimale, ou CannotDispense.
    /// </summary>
    [Fact]
    public void Sur_tout_le_domaine_du_stock_la_combinaison_est_minimale_ou_CannotDispense()
    {
        var failures = new List<string>();
        foreach (var jammed in new[] { 0, 50, 20, 10 })
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
        for (var amount = 10; amount <= 200; amount += 10)
        {
            var stock = N(f, t, d);
            var expected = Oracle.Best(stock, Jam(jammed), amount);
            var rig = new Rig(stock, Jam(jammed));
            var result = rig.Run(Acc(250, 0), Withdraw(amount));

            var expectedStatus = expected is null ? AtmStatus.CannotDispense : AtmStatus.Ok;
            var expectedNotes = expected ?? NoteCounts.None;
            if (result.Status != expectedStatus || result.Notes != expectedNotes
                || rig.Real.Stock != stock - expectedNotes)
            {
                failures.Add($"stock {stock}, bloquée {jammed}, retrait {amount}: attendu {expectedStatus} {expectedNotes}, obtenu {result.Status} {result.Notes}, stock après {rig.Real.Stock}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} cas faux, dont :\n" + string.Join("\n", failures.Take(20)));
    }
}

/// <summary>Référence tirée de la spec : la combinaison exacte au moins de billets, ou null.</summary>
internal static class Oracle
{
    public static NoteCounts? Best(NoteCounts stock, Denomination? jammed, int amount)
    {
        var maxF = jammed == Denomination.Fifty ? 0 : stock.Fifties;
        var maxT = jammed == Denomination.Twenty ? 0 : stock.Twenties;
        var maxD = jammed == Denomination.Ten ? 0 : stock.Tens;
        NoteCounts? best = null;
        for (var f = 0; f <= maxF; f++)
        for (var t = 0; t <= maxT; t++)
        for (var d = 0; d <= maxD; d++)
        {
            var c = new NoteCounts(f, t, d);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total))
            {
                best = c;
            }
        }

        return best;
    }
}
