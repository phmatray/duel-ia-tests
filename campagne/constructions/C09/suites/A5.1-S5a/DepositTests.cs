using static Atm.Tests.T;
using static Atm.Tests.SpecExamplesTests;

namespace Atm.Tests;

public class DepositTests
{
    [Fact]
    public void Depot_vide_est_un_montant_invalide()
    {
        var (r, d) = Run(Acc(100, 50), N(1, 1, 1), null, Deposit(NoteCounts.None));

        AssertRefused(r, AtmStatus.InvalidAmount, Acc(100, 50));
        Assert.Equal(N(1, 1, 1), d.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_est_invalide_avant_tout_autre_controle(Denomination jammed)
    {
        var (r, _) = Run(Acc(100, 50), Full, jammed, Deposit(NoteCounts.None));

        AssertRefused(r, AtmStatus.InvalidAmount, Acc(100, 50));
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 1)]
    [InlineData(Denomination.Fifty, 1, 0, 1)]
    public void Un_billet_de_la_cassette_bloquee_refuse_tout_le_depot(Denomination jammed, int f, int t, int c)
    {
        var stock = N(1, 1, 1);
        var (r, d) = Run(Acc(100, 50), stock, jammed, Deposit(N(f, t, c)));

        AssertRefused(r, AtmStatus.CassetteJammed, Acc(100, 50));
        Assert.Equal(stock, d.Stock);
    }

    [Fact]
    public void Cassette_bloquee_controlee_avant_cassette_pleine()
    {
        // La cassette des 20 est à la fois bloquée et pleine.
        var (r, d) = Run(Acc(100, 0), N(0, 5, 0), Denomination.Twenty, Deposit(N(0, 1, 0)));

        AssertRefused(r, AtmStatus.CassetteJammed, Acc(100, 0));
        Assert.Equal(N(0, 5, 0), d.Stock);
    }

    [Fact]
    public void Cassette_bloquee_controlee_avant_cassette_pleine_sur_une_autre_coupure()
    {
        // Les 50 sont pleins, les 10 bloqués ; le dépôt touche les deux.
        var (r, _) = Run(Acc(100, 0), N(5, 0, 0), Denomination.Ten, Deposit(N(1, 0, 1)));

        AssertRefused(r, AtmStatus.CassetteJammed, Acc(100, 0));
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // une seule cassette déborde : tout est refusé
    [InlineData(4, 4, 5, 1, 1, 1)]
    public void Depasser_5_billets_dans_une_cassette_est_refuse(int sf, int st, int sc, int f, int t, int c)
    {
        var stock = N(sf, st, sc);
        var (r, d) = Run(Acc(100, 20), stock, null, Deposit(N(f, t, c)));

        AssertRefused(r, AtmStatus.CassetteFull, Acc(100, 20));
        Assert.Equal(stock, d.Stock);
    }

    [Fact]
    public void Cassette_pleine_alors_qu_une_autre_cassette_est_bloquee()
    {
        var (r, d) = Run(Acc(100, 20), N(5, 0, 0), Denomination.Twenty, Deposit(N(1, 0, 1)));

        AssertRefused(r, AtmStatus.CassetteFull, Acc(100, 20));
        Assert.Equal(N(5, 0, 0), d.Stock);
    }

    [Theory]
    [InlineData(4, 4, 4, 1, 1, 1)] // remplir jusqu'à 5 exactement est permis
    [InlineData(0, 0, 0, 1, 0, 0)]
    [InlineData(0, 0, 0, 0, 1, 0)]
    [InlineData(0, 0, 0, 0, 0, 1)]
    [InlineData(2, 5, 3, 1, 0, 1)] // la cassette pleine n'est pas touchée
    public void Depot_reussi(int sf, int st, int sc, int f, int t, int c)
    {
        var stock = N(sf, st, sc);
        var notes = N(f, t, c);
        var (r, d) = Run(Acc(100, 120), stock, null, Deposit(notes));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(100 + notes.Value, 120), r.Account);
        Assert.Equal(stock + notes, d.Stock);
        Assert.Empty(d.Dispensed);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void Depot_reussi_quand_une_autre_cassette_est_bloquee(Denomination jammed, int f, int t, int c)
    {
        var notes = N(f, t, c);
        var (r, d) = Run(Acc(0, 0), N(2, 2, 2), jammed, Deposit(notes));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Acc(notes.Value, 0), r.Account);
        Assert.Equal(N(2, 2, 2) + notes, d.Stock);
    }

    [Fact]
    public void Depot_au_solde_maximal_et_plafond_atteint_ne_touche_pas_au_retire()
    {
        var (r, _) = Run(Acc(250, 200), N(0, 0, 0), null, Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Acc(330, 200), r.Account);
    }
}
