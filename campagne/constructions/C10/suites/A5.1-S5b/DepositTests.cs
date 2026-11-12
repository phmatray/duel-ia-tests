using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    private static void AssertRefusal(int balance, int withdrawn, NoteCounts stock, int jammed, NoteCounts deposit, AtmStatus expected)
    {
        var rig = new Rig(stock, Jam(jammed));
        var account = Acc(balance, withdrawn);
        var result = rig.Run(account, new Operation.Deposit(deposit));

        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, rig.Real.Stock);
        Assert.Empty(rig.Spy.SuccessfulPasses);
    }

    // ---------- 1. InvalidAmount ----------

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(5, 5, 5, 0)]  // cassettes pleines
    [InlineData(1, 1, 1, 50)] // cassette bloquée
    [InlineData(5, 5, 5, 20)]
    public void Depot_vide_est_InvalidAmount(int f, int t, int d, int jammed) =>
        AssertRefusal(100, 30, N(f, t, d), jammed, NoteCounts.None, AtmStatus.InvalidAmount);

    // ---------- 2. CassetteJammed ----------

    [Theory]
    [InlineData(0, 0, 0, 50, 1, 0, 0)]
    [InlineData(0, 0, 0, 20, 0, 1, 0)]
    [InlineData(0, 0, 0, 10, 0, 0, 1)]
    [InlineData(0, 0, 0, 20, 1, 1, 1)] // un seul billet fautif suffit
    [InlineData(2, 2, 2, 10, 1, 0, 1)]
    [InlineData(0, 1, 0, 20, 0, 1, 0)] // exemple de la spec
    public void Depot_touchant_la_cassette_bloquee_est_CassetteJammed(int f, int t, int d, int jammed, int df, int dt, int dd) =>
        AssertRefusal(10, 30, N(f, t, d), jammed, N(df, dt, dd), AtmStatus.CassetteJammed);

    [Theory]
    [InlineData(0, 5, 0, 20, 0, 1, 0)] // bloquée ET pleine
    [InlineData(5, 0, 0, 20, 1, 1, 0)] // une autre cassette pleine, la bloquée touchée
    [InlineData(5, 5, 5, 10, 1, 1, 1)]
    public void CassetteJammed_passe_avant_CassetteFull(int f, int t, int d, int jammed, int df, int dt, int dd) =>
        AssertRefusal(10, 30, N(f, t, d), jammed, N(df, dt, dd), AtmStatus.CassetteJammed);

    // ---------- 3. CassetteFull ----------

    [Theory]
    [InlineData(0, 5, 0, 0, 0, 1, 0)] // exemple de la spec
    [InlineData(5, 0, 0, 0, 1, 0, 0)]
    [InlineData(0, 0, 5, 0, 0, 0, 1)]
    [InlineData(4, 4, 5, 0, 1, 1, 1)] // les deux autres auraient tenu
    [InlineData(5, 5, 5, 0, 1, 1, 1)]
    [InlineData(5, 0, 0, 20, 1, 0, 1)] // une autre cassette est bloquée mais non touchée
    public void Depot_faisant_deborder_une_cassette_est_CassetteFull(int f, int t, int d, int jammed, int df, int dt, int dd) =>
        AssertRefusal(10, 0, N(f, t, d), jammed, N(df, dt, dd), AtmStatus.CassetteFull);

    // ---------- Succès ----------

    [Theory]
    // stock, bloquée, dépôt, solde
    [InlineData(1, 1, 0, 0, 1, 0, 1, 10, 30)] // exemple de la spec
    [InlineData(0, 0, 0, 0, 1, 1, 1, 0, 0)]
    [InlineData(4, 4, 4, 0, 1, 1, 1, 250, 200)]
    [InlineData(0, 0, 0, 0, 0, 0, 1, 5, 0)]
    [InlineData(0, 0, 0, 0, 0, 1, 0, 0, 200)]
    [InlineData(0, 0, 0, 0, 1, 0, 0, 125, 90)]
    [InlineData(3, 2, 4, 50, 0, 1, 1, 40, 10)] // une autre cassette bloquée
    public void Succes_range_les_billets_et_credite_le_solde(
        int f, int t, int d, int jammed, int df, int dt, int dd, int balance, int withdrawn)
    {
        var deposit = N(df, dt, dd);
        var stock = N(f, t, d);
        var rig = new Rig(stock, Jam(jammed));
        var result = rig.Run(Acc(balance, withdrawn), new Operation.Deposit(deposit));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(balance + deposit.Value, withdrawn), result.Account);
        Assert.Equal(stock + deposit, rig.Real.Stock);
        Assert.Empty(rig.Spy.SuccessfulPasses);
    }

    // ---------- Contrôle exhaustif sur le domaine ----------

    [Fact]
    public void Sur_tout_le_domaine_du_stock_et_des_depots_le_statut_suit_l_ordre_des_controles()
    {
        var failures = new List<string>();
        foreach (var jammed in new[] { 0, 50, 20, 10 })
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
        for (var df = 0; df <= 1; df++)
        for (var dt = 0; dt <= 1; dt++)
        for (var dd = 0; dd <= 1; dd++)
        {
            var stock = N(f, t, d);
            var deposit = N(df, dt, dd);
            var jam = Jam(jammed);
            var after = stock + deposit;

            AtmStatus expected;
            if (deposit.Total == 0) expected = AtmStatus.InvalidAmount;
            else if (jam is { } j && deposit.Of(j) > 0) expected = AtmStatus.CassetteJammed;
            else if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) expected = AtmStatus.CassetteFull;
            else expected = AtmStatus.Ok;

            var account = Acc(100, 50);
            var rig = new Rig(stock, jam);
            var result = rig.Run(account, new Operation.Deposit(deposit));

            var ok = expected == AtmStatus.Ok;
            var expectedAccount = ok ? Acc(100 + deposit.Value, 50) : account;
            var expectedStock = ok ? after : stock;
            if (result.Status != expected || result.Notes != NoteCounts.None || result.Fee != 0
                || result.Account != expectedAccount || rig.Real.Stock != expectedStock)
            {
                failures.Add($"stock {stock}, bloquée {jammed}, dépôt {deposit}: attendu {expected}/{expectedAccount}/{expectedStock}, obtenu {result}, stock après {rig.Real.Stock}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} cas faux, dont :\n" + string.Join("\n", failures.Take(20)));
    }
}
