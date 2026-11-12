using static Atm.Tests.Support;

namespace Atm.Tests;

public class DepositTests
{
    // ---------- Contrôle 1 : dépôt vide ----------

    [Fact]
    public void Depot_vide_est_invalide()
    {
        var (m, d) = Machine(N(1, 1, 1));
        AssertRefused(m.Deposit(100, 50, NoteCounts.None), AtmStatus.InvalidAmount, new(100, 50), d, N(1, 1, 1));
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_passe_avant_cassette_bloquee(Denomination jammed)
    {
        var (m, d) = Machine(Full, jammed);
        AssertRefused(m.Deposit(100, 0, NoteCounts.None), AtmStatus.InvalidAmount, new(100, 0), d, Full);
    }

    // ---------- Contrôle 2 : cassette bloquée ----------

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 1, 0)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Depot_contenant_un_billet_de_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int n)
    {
        var (m, d) = Machine(N(1, 1, 1), jammed);
        AssertRefused(m.Deposit(100, 0, N(f, t, n)), AtmStatus.CassetteJammed, new(100, 0), d, N(1, 1, 1));
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 1, 1, 1)]
    public void Cassette_bloquee_passe_avant_cassette_pleine(Denomination jammed, int f, int t, int n)
    {
        var (m, d) = Machine(Full, jammed);
        AssertRefused(m.Deposit(100, 0, N(f, t, n)), AtmStatus.CassetteJammed, new(100, 0), d, Full);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_une_autre_cassette_pleine()
    {
        // Le 50 vise une cassette pleine, le 10 la cassette bloquée : CassetteJammed l'emporte.
        var (m, d) = Machine(N(5, 0, 0), Denomination.Ten);
        AssertRefused(m.Deposit(100, 0, N(1, 0, 1)), AtmStatus.CassetteJammed, new(100, 0), d, N(5, 0, 0));
    }

    [Fact]
    public void Depot_hors_cassette_bloquee_est_accepte()
    {
        var (m, d) = Machine(N(0, 0, 0), Denomination.Twenty);
        var r = m.Deposit(0, 0, N(1, 0, 1));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(60, 0)), r);
        Assert.Equal(N(1, 0, 1), d.Stock);
    }

    // ---------- Contrôle 3 : cassette pleine ----------

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(4, 4, 5, 1, 1, 1)] // seule la cassette de 10 déborde
    [InlineData(5, 0, 0, 1, 1, 1)] // seule la cassette de 50 déborde
    public void Depot_qui_ferait_depasser_5_billets_est_refuse(int sf, int st, int sn, int f, int t, int n)
    {
        var (m, d) = Machine(N(sf, st, sn));
        AssertRefused(m.Deposit(100, 0, N(f, t, n)), AtmStatus.CassetteFull, new(100, 0), d, N(sf, st, sn));
    }

    [Fact]
    public void Remplir_une_cassette_jusqu_a_5_est_permis()
    {
        var (m, d) = Machine(N(4, 4, 4));
        var r = m.Deposit(0, 0, N(1, 1, 1));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(80, 0)), r);
        Assert.Equal(Full, d.Stock);
    }

    // ---------- Succès ----------

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Depot_reussi_credite_la_valeur_et_range_les_billets(int f, int t, int n, int value)
    {
        var (m, d) = Machine(N(2, 2, 2));
        var r = m.Deposit(35, 120, N(f, t, n));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(35 + value, 120)), r);
        Assert.Equal(N(2 + f, 2 + t, 2 + n), d.Stock);
    }

    [Fact]
    public void Depot_ne_change_pas_le_retire_du_jour_meme_au_plafond()
    {
        var (m, _) = Machine(N(0, 0, 0));
        var r = m.Deposit(0, 200, N(1, 1, 1));
        Assert.Equal(new AccountState(80, 200), r.Account);
    }

    [Fact]
    public void Depot_passe_par_le_module_en_une_fois_sans_distribuer()
    {
        var dispenser = new RecordingDispenser(N(1, 1, 1));
        var r = new CashMachine(dispenser).Deposit(10, 0, N(1, 1, 1));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Equal(N(1, 1, 1), dispenser.LoadCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(N(2, 2, 2), dispenser.Stock);
    }

    [Fact]
    public void Les_billets_deposes_peuvent_ensuite_etre_retires()
    {
        var (m, d) = Machine(N(0, 0, 0));
        Assert.Equal(AtmStatus.Ok, m.Deposit(0, 0, N(1, 0, 0)).Status);
        var r = m.Withdraw(50, 0, 50);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, new AccountState(0, 50)), r);
        Assert.Equal(N(0, 0, 0), d.Stock);
    }
}
