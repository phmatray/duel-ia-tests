using static Atm.Tests.Outils;

namespace Atm.Tests;

public class DepotTests
{
    public static TheoryData<int, int, int> DepotsNonVides => new()
    {
        { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 }, { 1, 1, 0 }, { 1, 0, 1 }, { 0, 1, 1 }, { 1, 1, 1 },
    };

    // ---------- Contrôle 1 : dépôt vide ----------

    [Fact]
    public void Depot_vide_est_invalide()
    {
        var (r, d) = Executer(Compte(100, 50), N(1, 1, 1), null, Depot(NoteCounts.None));

        AssertRefus(AtmStatus.InvalidAmount, r, Compte(100, 50), N(1, 1, 1), d);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_passe_avant_cassette_bloquee_ou_pleine(Denomination jammed)
    {
        var (r, d) = Executer(Compte(100, 50), Plein, jammed, Depot(NoteCounts.None));

        AssertRefus(AtmStatus.InvalidAmount, r, Compte(100, 50), Plein, d);
    }

    // ---------- Contrôle 2 : cassette bloquée ----------

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 1, 0)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Un_billet_de_la_cassette_bloquee_refuse_tout_le_depot(Denomination jammed, int f, int t, int d10)
    {
        var stock = N(2, 2, 2);
        var (r, d) = Executer(Compte(100, 50), stock, jammed, Depot(N(f, t, d10)));

        AssertRefus(AtmStatus.CassetteJammed, r, Compte(100, 50), stock, d);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Cassette_bloquee_passe_avant_cassette_pleine(Denomination jammed)
    {
        var (r, d) = Executer(Compte(100, 50), Plein, jammed, Depot(N(1, 1, 1)));

        AssertRefus(AtmStatus.CassetteJammed, r, Compte(100, 50), Plein, d);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void Une_autre_cassette_bloquee_n_empeche_pas_le_depot(Denomination jammed, int f, int t, int d10)
    {
        var notes = N(f, t, d10);
        var (r, d) = Executer(Compte(100, 50), N(2, 2, 2), jammed, Depot(notes));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Compte(100 + notes.Value, 50), r.Account);
        Assert.Equal(N(2, 2, 2) + notes, d.Stock);
    }

    // ---------- Contrôle 3 : cassette pleine ----------

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(4, 4, 5, 1, 1, 1)]   // une seule cassette déborde : tout est refusé
    [InlineData(0, 0, 5, 1, 0, 1)]
    public void Depasser_5_billets_dans_une_cassette_refuse_tout_le_depot(int sf, int st, int sd, int f, int t, int d10)
    {
        var stock = N(sf, st, sd);
        var (r, d) = Executer(Compte(100, 50), stock, null, Depot(N(f, t, d10)));

        AssertRefus(AtmStatus.CassetteFull, r, Compte(100, 50), stock, d);
    }

    [Fact]
    public void Remplir_une_cassette_jusqu_a_5_est_permis()
    {
        var (r, d) = Executer(Compte(0, 0), N(4, 4, 4), null, Depot(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Compte(80, 0), r.Account);
        Assert.Equal(Plein, d.Stock);
    }

    [Fact]
    public void Une_cassette_pleine_non_concernee_n_empeche_pas_le_depot()
    {
        var (r, d) = Executer(Compte(0, 0), N(5, 5, 0), null, Depot(N(0, 0, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Compte(10, 0), r.Account);
        Assert.Equal(N(5, 5, 1), d.Stock);
    }

    // ---------- Le succès ----------

    [Theory]
    [MemberData(nameof(DepotsNonVides))]
    public void Depot_reussi_credite_la_valeur_et_range_les_billets(int f, int t, int d10)
    {
        var notes = N(f, t, d10);
        var (r, d) = Executer(Compte(150, 120), N(1, 2, 3), null, Depot(notes));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(150 + notes.Value, 120), r.Account);
        Assert.Equal(N(1, 2, 3) + notes, d.Stock);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public void Depot_ne_change_pas_le_retire_du_jour(int withdrawn)
    {
        var (r, _) = Executer(Compte(0, withdrawn), N(0, 0, 0), null, Depot(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(withdrawn, r.Account.WithdrawnToday);
        Assert.Equal(80, r.Account.Balance);
    }

    [Fact]
    public void Depot_passe_par_le_module_et_ne_sort_aucun_billet()
    {
        var espion = new ModuleEspion(N(1, 1, 1));
        var r = new CashMachine(espion).Execute(Compte(10, 0), Depot(N(1, 0, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(1, 0, 1), espion.Chargements.Aggregate(NoteCounts.None, (a, c) => a + c));
        Assert.Empty(espion.Passages);
        Assert.Equal(N(2, 1, 2), espion.Stock);
    }

    [Fact]
    public void Un_depot_refuse_ne_charge_rien_dans_le_module()
    {
        var espion = new ModuleEspion(N(0, 5, 0), Denomination.Fifty);
        var machine = new CashMachine(espion);

        Assert.Equal(AtmStatus.InvalidAmount, machine.Execute(Compte(10, 0), Depot(NoteCounts.None)).Status);
        Assert.Equal(AtmStatus.CassetteJammed, machine.Execute(Compte(10, 0), Depot(N(1, 0, 0))).Status);
        Assert.Equal(AtmStatus.CassetteFull, machine.Execute(Compte(10, 0), Depot(N(0, 1, 0))).Status);

        Assert.Empty(espion.Chargements);
        Assert.Empty(espion.Passages);
        Assert.Equal(N(0, 5, 0), espion.Stock);
    }
}
