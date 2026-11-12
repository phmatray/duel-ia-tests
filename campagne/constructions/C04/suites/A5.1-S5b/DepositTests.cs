using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    private static void AssertRefused(AtmStatus status, AccountState account, NoteCounts stock, AtmResult r, SpyDispenser spy)
    {
        Assert.Equal(new AtmResult(status, NoteCounts.None, 0, account), r);
        Assert.Equal(stock, spy.Stock);
        Assert.DoesNotContain(spy.Loaded, l => l.Ok);
        Assert.DoesNotContain(spy.Dispensed, d => d.Ok);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    public void Depot_reussi(int f, int t, int n)
    {
        var notes = N(f, t, n);
        var (r, spy) = Run(Acc(25, 70), N(2, 2, 2), null, D(notes));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(25 + notes.Value, 70)), r);
        Assert.Equal(N(2, 2, 2) + notes, spy.Stock);
        Assert.DoesNotContain(spy.Dispensed, d => d.Ok);
    }

    [Fact]
    public void Depot_jusqu_a_remplir_exactement_les_cassettes()
    {
        var (r, spy) = Run(Acc(0, 0), N(4, 4, 4), null, D(N(1, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(80, 0)), r);
        Assert.Equal(N(5, 5, 5), spy.Stock);
    }

    [Fact]
    public void Depot_ne_touche_pas_au_retire_du_jour()
    {
        var (r, _) = Run(Acc(100, 200), N(0, 0, 0), null, D(N(1, 0, 0)));
        Assert.Equal(Acc(150, 200), r.Account);
    }

    [Fact]
    public void Depot_reussi_avec_une_cassette_bloquee_non_concernee()
    {
        var (r, spy) = Run(Acc(10, 0), N(0, 3, 1), Denomination.Fifty, D(N(0, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(40, 0)), r);
        Assert.Equal(N(0, 4, 2), spy.Stock);
    }

    // --- Contrôle 1 ---

    [Fact]
    public void Depot_vide_est_invalide()
    {
        var (r, spy) = Run(Acc(10, 0), N(1, 1, 1), null, D(N(0, 0, 0)));
        AssertRefused(AtmStatus.InvalidAmount, Acc(10, 0), N(1, 1, 1), r, spy);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_est_invalide_meme_cassette_bloquee_et_pleine(Denomination jammed)
    {
        var (r, spy) = Run(Acc(10, 0), N(5, 5, 5), jammed, D(N(0, 0, 0)));
        AssertRefused(AtmStatus.InvalidAmount, Acc(10, 0), N(5, 5, 5), r, spy);
    }

    // --- Contrôle 2 ---

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 1, 0)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Depot_touchant_la_cassette_bloquee(Denomination jammed, int f, int t, int n)
    {
        var (r, spy) = Run(Acc(10, 30), N(1, 1, 1), jammed, D(N(f, t, n)));
        AssertRefused(AtmStatus.CassetteJammed, Acc(10, 30), N(1, 1, 1), r, spy);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Cassette_bloquee_verifiee_avant_cassette_pleine(Denomination jammed)
    {
        var (r, spy) = Run(Acc(10, 0), N(5, 5, 5), jammed, D(N(1, 1, 1)));
        AssertRefused(AtmStatus.CassetteJammed, Acc(10, 0), N(5, 5, 5), r, spy);
    }

    // --- Contrôle 3 ---

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // une seule cassette déborde : tout ou rien
    [InlineData(0, 4, 5, 0, 1, 1)]
    public void Depot_faisant_deborder_une_cassette(int sf, int st, int sn, int f, int t, int n)
    {
        var (r, spy) = Run(Acc(10, 0), N(sf, st, sn), null, D(N(f, t, n)));
        AssertRefused(AtmStatus.CassetteFull, Acc(10, 0), N(sf, st, sn), r, spy);
    }

    [Fact]
    public void Cassette_pleine_mais_bloquee_et_non_concernee_par_le_depot()
    {
        // La cassette de 50 est pleine ET bloquée, mais le dépôt n'a pas de 50.
        var (r, spy) = Run(Acc(10, 0), N(5, 0, 0), Denomination.Fifty, D(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(30, 0)), r);
        Assert.Equal(N(5, 1, 0), spy.Stock);
    }
}
