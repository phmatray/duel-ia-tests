using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    // ---- Contrôle 1 : dépôt vide ----

    [Fact]
    public void Depot_vide_est_invalide()
    {
        var (atm, dispenser) = RecordingAtm(N(1, 1, 1));
        var r = atm.Execute(Acc(10, 30), Deposit(NoteCounts.None));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Acc(10, 30)), r);
        Assert.Empty(dispenser.Loaded);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_est_invalide_meme_cassette_bloquee_et_pleine(Denomination jammed)
    {
        var (atm, _) = Machine(N(5, 5, 5), jammed);
        var r = atm.Execute(Acc(10, 30), Deposit(NoteCounts.None));

        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 1, 0)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Billet_de_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int n)
    {
        var stock = N(2, 2, 2);
        var (atm, dispenser) = RecordingAtm(stock, jammed);
        var r = atm.Execute(Acc(10, 30), Deposit(N(f, t, n)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, Acc(10, 30)), r);
        Assert.Empty(dispenser.Loaded);
        Assert.Equal(stock, dispenser.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Cassette_bloquee_passe_avant_cassette_pleine(Denomination jammed)
    {
        // Toutes les cassettes sont pleines et le dépôt touche la bloquée.
        var (atm, _) = Machine(N(5, 5, 5), jammed);
        var r = atm.Execute(Acc(10, 30), Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Fact]
    public void Cassette_bloquee_meme_si_la_cassette_bloquee_n_est_pas_pleine_mais_une_autre_l_est()
    {
        var (atm, _) = Machine(N(5, 0, 0), Denomination.Ten);
        var r = atm.Execute(Acc(10, 30), Deposit(N(1, 0, 1)));

        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1, 0, 30, 30)]
    [InlineData(Denomination.Twenty, 1, 0, 1, 0, 60, 0)]
    [InlineData(Denomination.Ten, 1, 1, 0, 70, 70, 0)]
    public void Depot_hors_cassette_bloquee_est_accepte(
        Denomination jammed, int f, int t, int n, int balanceBefore, int expectedGain, int withdrawn)
    {
        var stock = N(2, 2, 2);
        var (atm, dispenser) = Machine(stock, jammed);
        var r = atm.Execute(Acc(balanceBefore, withdrawn), Deposit(N(f, t, n)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(balanceBefore + expectedGain, withdrawn)), r);
        Assert.Equal(stock + N(f, t, n), dispenser.Stock);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 4, 4, 1, 1, 1)] // une seule cassette déborde : tout est refusé
    [InlineData(4, 4, 5, 1, 1, 1)]
    [InlineData(0, 5, 0, 1, 1, 0)]
    public void Depasser_5_billets_dans_une_cassette_est_refuse(int sf, int st, int sn, int f, int t, int n)
    {
        var stock = N(sf, st, sn);
        var (atm, dispenser) = RecordingAtm(stock);
        var r = atm.Execute(Acc(10, 30), Deposit(N(f, t, n)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, Acc(10, 30)), r);
        Assert.Empty(dispenser.Loaded);
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void Remplir_une_cassette_jusqu_a_5_est_permis()
    {
        var (atm, dispenser) = Machine(N(4, 4, 4));
        var r = atm.Execute(Acc(0, 0), Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(80, 0)), r);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Une_cassette_pleine_non_touchee_ne_gene_pas()
    {
        var (atm, dispenser) = Machine(N(5, 5, 0));
        var r = atm.Execute(Acc(0, 0), Deposit(N(0, 0, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(5, 5, 1), dispenser.Stock);
    }

    // ---- Succès ----

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Depot_reussi_credite_le_solde_et_range_les_billets(int f, int t, int n, int value)
    {
        var stock = N(1, 2, 3);
        var (atm, dispenser) = RecordingAtm(stock);
        var r = atm.Execute(Acc(170, 120), Deposit(N(f, t, n)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(170 + value, 120), r.Account);
        Assert.Equal(stock + N(f, t, n), dispenser.Stock);
        Assert.Empty(dispenser.Dispensed);
        Assert.Equal(N(f, t, n), dispenser.Loaded.Aggregate(NoteCounts.None, (a, b) => a + b));
    }

    [Fact]
    public void Depot_dans_un_module_vide()
    {
        var (atm, dispenser) = Machine(N(0, 0, 0));
        var r = atm.Execute(Acc(0, 200), Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(80, 200)), r);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Depot_puis_retrait_utilise_les_billets_deposes()
    {
        var (atm, dispenser) = Machine(N(0, 0, 0));

        var deposit = atm.Execute(Acc(0, 0), Deposit(N(1, 0, 0)));
        var withdraw = atm.Execute(deposit.Account, Withdraw(50));

        Assert.Equal(AtmStatus.Ok, withdraw.Status);
        Assert.Equal(N(1, 0, 0), withdraw.Notes);
        Assert.Equal(Acc(0, 50), withdraw.Account);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }
}
