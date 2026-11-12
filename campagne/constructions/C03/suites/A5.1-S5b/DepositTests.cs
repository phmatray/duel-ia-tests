using static Atm.Tests.TestSupport;

namespace Atm.Tests;

public class DepositTests
{
    private static void AssertOk(int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        var (r, d) = Deposit(balance, withdrawnToday, stock, jammed, notes);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(balance + notes.Value, withdrawnToday), r.Account);
        Assert.Equal(stock + notes, d.Stock);
    }

    // ---- Contrôle 1 : dépôt vide -----------------------------------------------------------------

    [Fact]
    public void Depot_vide_est_invalide() =>
        AssertRefused(AtmStatus.InvalidAmount, Deposit(100, 0, N(1, 1, 1), null, N(0, 0, 0)), 100, 0, N(1, 1, 1));

    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_est_controle_avant_bloquee_et_pleine(Denomination? jammed) =>
        AssertRefused(AtmStatus.InvalidAmount, Deposit(100, 0, FullStock, jammed, N(0, 0, 0)), 100, 0, FullStock);

    // ---- Contrôle 2 : cassette bloquée -----------------------------------------------------------

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Billet_de_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int n) =>
        AssertRefused(AtmStatus.CassetteJammed, Deposit(100, 0, N(1, 1, 1), jammed, N(f, t, n)), 100, 0, N(1, 1, 1));

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Cassette_bloquee_est_controlee_avant_cassette_pleine(Denomination jammed) =>
        // Tout est plein ET le dépôt touche la cassette bloquée : CassetteJammed.
        AssertRefused(AtmStatus.CassetteJammed, Deposit(100, 0, FullStock, jammed, N(1, 1, 1)), 100, 0, FullStock);

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void Billets_hors_cassette_bloquee_sont_acceptes(Denomination jammed, int f, int t, int n) =>
        AssertOk(100, 0, N(2, 2, 2), jammed, N(f, t, n));

    // ---- Contrôle 3 : cassette pleine ------------------------------------------------------------

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)]   // une seule cassette déborde : tout est refusé
    [InlineData(4, 4, 5, 1, 1, 1)]
    public void Depasser_5_billets_dans_une_cassette_est_refuse(int sf, int st, int sn, int f, int t, int n) =>
        AssertRefused(AtmStatus.CassetteFull, Deposit(100, 0, N(sf, st, sn), null, N(f, t, n)), 100, 0, N(sf, st, sn));

    [Fact]
    public void Atteindre_exactement_5_billets_est_permis() =>
        AssertOk(100, 0, N(4, 4, 4), null, N(1, 1, 1));

    // ---- Succès ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    public void Succes_credite_la_valeur_des_billets_et_les_range(int f, int t, int n) =>
        AssertOk(10, 30, N(1, 1, 1), null, N(f, t, n));

    [Fact]
    public void Depot_ne_touche_pas_au_retire_du_jour_meme_au_plafond() =>
        AssertOk(0, 200, N(0, 0, 0), null, N(1, 1, 1));

    [Fact]
    public void Depot_peut_porter_le_solde_au_dela_de_250() =>
        // Le résultat n'est pas borné par le domaine des entrées.
        AssertOk(250, 0, N(0, 0, 0), null, N(1, 1, 1));
}
