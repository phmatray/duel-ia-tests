namespace Atm.Tests;

using static N;

public class DepositTests
{
    // ---- Contrôle 1 : dépôt vide ----

    [Fact]
    public void Depot_vide_est_invalide()
    {
        var (r, d) = Run.Deposit(100, 30, C(1, 1, 1), null, NoteCounts.None);
        AssertAtm.Refused(AtmStatus.InvalidAmount, new(100, 30), C(1, 1, 1), r, d);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_est_invalide_avant_tout_autre_controle(Denomination jammed)
    {
        var (r, d) = Run.Deposit(0, 0, Full, jammed, NoteCounts.None);
        AssertAtm.Refused(AtmStatus.InvalidAmount, new(0, 0), Full, r, d);
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Twenty, 1, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Un_billet_de_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int n)
    {
        var stock = C(1, 1, 1);
        var (r, d) = Run.Deposit(50, 0, stock, jammed, C(f, t, n));
        AssertAtm.Refused(AtmStatus.CassetteJammed, new(50, 0), stock, r, d);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Cassette_bloquee_verifiee_avant_cassette_pleine(Denomination jammed)
    {
        // Toutes les cassettes pleines, dépôt d'un billet de chaque.
        var (r, d) = Run.Deposit(50, 0, Full, jammed, C(1, 1, 1));
        AssertAtm.Refused(AtmStatus.CassetteJammed, new(50, 0), Full, r, d);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1, 50 + 30)]
    [InlineData(Denomination.Twenty, 1, 0, 1, 50 + 60)]
    [InlineData(Denomination.Ten, 1, 1, 0, 50 + 70)]
    public void Une_cassette_bloquee_n_empeche_pas_de_deposer_dans_les_autres(Denomination jammed, int f, int t, int n, int balanceAfter)
    {
        var (r, d) = Run.Deposit(50, 20, C(1, 1, 1), jammed, C(f, t, n));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(balanceAfter, 20), r.Account);
        Assert.Equal(C(1, 1, 1) + C(f, t, n), d.Stock);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // une seule cassette pleine suffit, rien n'est rangé
    [InlineData(4, 4, 5, 1, 1, 1)]
    public void Depasser_5_billets_dans_une_cassette_est_refuse(int sf, int st, int sn, int f, int t, int n)
    {
        var stock = C(sf, st, sn);
        var (r, d) = Run.Deposit(50, 0, stock, null, C(f, t, n));
        AssertAtm.Refused(AtmStatus.CassetteFull, new(50, 0), stock, r, d);
    }

    [Fact]
    public void Remplir_exactement_jusqu_a_5_est_permis()
    {
        var (r, d) = Run.Deposit(0, 0, C(4, 4, 4), null, C(1, 1, 1));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Full, d.Stock);
        Assert.Equal(new AccountState(80, 0), r.Account);
    }

    [Fact]
    public void Une_cassette_pleine_non_concernee_ne_gene_pas()
    {
        var (r, d) = Run.Deposit(0, 0, C(5, 5, 0), null, C(0, 0, 1));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(5, 5, 1), d.Stock);
    }

    // ---- Le succès ----

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 1, 1, 80)]
    public void Le_succes_credite_la_valeur_et_range_les_billets(int f, int t, int n, int value)
    {
        var (r, d) = Run.Deposit(150, 70, C(2, 2, 2), null, C(f, t, n));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(150 + value, 70), r.Account);
        Assert.Equal(C(2, 2, 2) + C(f, t, n), d.Stock);
    }

    [Fact]
    public void Le_depot_ne_touche_pas_au_plafond_meme_atteint()
    {
        var (r, _) = Run.Deposit(0, 200, C(0, 0, 0), null, C(1, 1, 1));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(80, 200), r.Account);
    }

    [Fact]
    public void Le_depot_passe_par_le_module()
    {
        var dispenser = new FaultyDispenser(C(1, 1, 1));
        var r = new CashMachine(dispenser).Execute(new(10, 0), new Operation.Deposit(C(1, 1, 1)));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(C(1, 1, 1), dispenser.LoadCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Equal(C(2, 2, 2), dispenser.Stock);
    }

    // ---- Refus : aucun billet rangé ----

    public static TheoryData<NoteCounts, Denomination?, NoteCounts, AtmStatus> Refusals => new()
    {
        { C(1, 1, 1), null, NoteCounts.None, AtmStatus.InvalidAmount },
        { C(1, 1, 1), Denomination.Twenty, C(0, 1, 0), AtmStatus.CassetteJammed },
        { C(0, 5, 0), null, C(1, 1, 0), AtmStatus.CassetteFull },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Un_refus_ne_charge_rien(NoteCounts stock, Denomination? jammed, NoteCounts notes, AtmStatus status)
    {
        var dispenser = new FaultyDispenser(stock, jammed);
        var r = new CashMachine(dispenser).Execute(new(40, 10), new Operation.Deposit(notes));
        AssertAtm.Refused(status, new(40, 10), stock, r, dispenser);
        Assert.Empty(dispenser.SuccessfulPasses);
    }
}
