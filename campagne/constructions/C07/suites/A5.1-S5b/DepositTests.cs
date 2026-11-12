using Atm.Tests.Support;
using static Atm.Tests.Support.N;

namespace Atm.Tests;

public class DepositTests
{
    // ---- Contrôle 1 : dépôt vide ----

    [Fact]
    public void Depot_sans_billet_est_invalide()
    {
        var run = Scenario.Deposit(100, 0, Notes(1, 1, 1), null, NoteCounts.None);

        Assert.Equal(AtmStatus.InvalidAmount, run.Result.Status);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_passe_avant_le_blocage_et_la_saturation(Denomination jammed)
    {
        var run = Scenario.Deposit(100, 0, Notes(5, 5, 5), jammed, NoteCounts.None);

        Assert.Equal(AtmStatus.InvalidAmount, run.Result.Status);
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Depot_touchant_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int n)
    {
        var run = Scenario.Deposit(100, 0, Notes(0, 0, 0), jammed, Notes(f, t, n));

        Assert.Equal(AtmStatus.CassetteJammed, run.Result.Status);
    }

    [Fact]
    public void Blocage_passe_avant_la_saturation()
    {
        // Les 50 sont bloqués et les 20 sont pleins.
        var run = Scenario.Deposit(100, 0, Notes(0, 5, 0), Denomination.Fifty, Notes(1, 1, 0));

        Assert.Equal(AtmStatus.CassetteJammed, run.Result.Status);
    }

    [Fact]
    public void Blocage_d_une_autre_cassette_ne_gene_pas_le_depot()
    {
        var run = Scenario.Deposit(100, 0, Notes(0, 0, 0), Denomination.Twenty, Notes(1, 0, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(160, 0)), run.Result);
        Assert.Equal(Notes(1, 0, 1), run.Dispenser.Stock);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)]   // une seule cassette déborde : tout est refusé
    public void Depot_qui_ferait_depasser_5_billets_est_refuse(int s50, int s20, int s10, int f, int t, int n)
    {
        var run = Scenario.Deposit(100, 0, Notes(s50, s20, s10), null, Notes(f, t, n));

        Assert.Equal(AtmStatus.CassetteFull, run.Result.Status);
    }

    [Fact]
    public void Depot_qui_remplit_exactement_les_cassettes_est_accepte()
    {
        var run = Scenario.Deposit(0, 0, Notes(4, 4, 4), null, Notes(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(80, 0)), run.Result);
        Assert.Equal(Notes(5, 5, 5), run.Dispenser.Stock);
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
    public void Depot_credite_la_valeur_des_billets(int f, int t, int n, int value)
    {
        var deposit = Notes(f, t, n);
        var run = Scenario.Deposit(25, 70, Notes(2, 2, 2), null, deposit);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(25 + value, 70)), run.Result);
        Assert.Equal(Notes(2, 2, 2) + deposit, run.Dispenser.Stock);
    }

    [Fact]
    public void Depot_passe_par_le_module_et_ne_sort_aucun_billet()
    {
        var run = Scenario.Deposit(0, 0, Notes(0, 0, 0), null, Notes(1, 1, 1));

        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.NotEmpty(run.Dispenser.Loads);
        Assert.All(run.Dispenser.Loads, l => Assert.True(l.Accepted));
        Assert.Equal(Notes(1, 1, 1), run.Dispenser.Loads.Aggregate(NoteCounts.None, (a, l) => a + l.Notes));
        Assert.Empty(run.Dispenser.Dispenses);
    }

    [Fact]
    public void Depot_ignore_le_plafond_du_jour()
    {
        var run = Scenario.Deposit(250, 200, Notes(0, 0, 0), null, Notes(1, 1, 1));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(330, 200)), run.Result);
    }
}
