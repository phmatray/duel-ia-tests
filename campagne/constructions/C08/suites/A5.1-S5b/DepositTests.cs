using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le dépôt : contrôles, ordre des contrôles, succès.</summary>
public class DepositTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(50)]
    [InlineData(20)]
    [InlineData(10)]
    public void Depot_vide_est_invalide(int? jammed)
    {
        var (r, d) = Deposit(100, 30, N(2, 2, 2), Jam(jammed), N(0, 0, 0));
        AssertRefused(AtmStatus.InvalidAmount, r, d, Acc(100, 30), N(2, 2, 2));
    }

    [Fact]
    public void Depot_vide_est_invalide_meme_avec_des_cassettes_pleines()
    {
        var (r, d) = Deposit(100, 30, N(5, 5, 5), Denomination.Ten, N(0, 0, 0));
        AssertRefused(AtmStatus.InvalidAmount, r, d, Acc(100, 30), N(5, 5, 5));
    }

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(50, 1, 1, 1)]
    [InlineData(10, 1, 1, 1)]
    [InlineData(20, 1, 1, 0)]
    public void Un_billet_de_la_cassette_bloquee_est_refuse(int jammed, int f, int t, int ten)
    {
        var stock = N(1, 1, 1);
        var (r, d) = Deposit(100, 30, stock, Jam(jammed), N(f, t, ten));
        AssertRefused(AtmStatus.CassetteJammed, r, d, Acc(100, 30), stock);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(20)]
    [InlineData(10)]
    public void Cassette_bloquee_passe_avant_cassette_pleine(int jammed)
    {
        var stock = N(5, 5, 5);
        var (r, d) = Deposit(100, 30, stock, Jam(jammed), N(1, 1, 1));
        AssertRefused(AtmStatus.CassetteJammed, r, d, Acc(100, 30), stock);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // une seule cassette pleine suffit
    [InlineData(0, 0, 5, 1, 1, 1)]
    [InlineData(4, 4, 5, 1, 1, 1)]
    public void Depasser_5_billets_dans_une_cassette_est_refuse(int f, int t, int ten, int df, int dt, int dten)
    {
        var stock = N(f, t, ten);
        var (r, d) = Deposit(100, 30, stock, null, N(df, dt, dten));
        AssertRefused(AtmStatus.CassetteFull, r, d, Acc(100, 30), stock);
    }

    [Fact]
    public void Cassette_pleine_sur_une_autre_coupure_que_la_cassette_bloquee()
    {
        // La cassette de 20 est bloquée, mais le dépôt n'en contient pas ; celle de 50 est pleine.
        var stock = N(5, 0, 0);
        var (r, d) = Deposit(100, 30, stock, Denomination.Twenty, N(1, 0, 1));
        AssertRefused(AtmStatus.CassetteFull, r, d, Acc(100, 30), stock);
    }

    [Fact]
    public void Cassette_pleine_sans_billet_depose_dans_cette_cassette_est_permis()
    {
        var (r, d) = Deposit(100, 30, N(5, 5, 4), null, N(0, 0, 1));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(5, 5, 5), d.Stock);
        Assert.Equal(Acc(110, 30), r.Account);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    public void Succes_les_billets_rejoignent_le_module_et_le_solde_augmente(int df, int dt, int dten)
    {
        var stock = N(4, 4, 4);
        var notes = N(df, dt, dten);
        var (r, d) = Deposit(250, 200, stock, null, notes);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250 + notes.Value, 200), r.Account);
        Assert.Equal(stock + notes, d.Stock);
    }

    [Theory]
    [InlineData(50, 0, 1, 1)]
    [InlineData(20, 1, 0, 1)]
    [InlineData(10, 1, 1, 0)]
    public void Succes_avec_une_cassette_bloquee_non_concernee(int jammed, int df, int dt, int dten)
    {
        var stock = N(0, 0, 0);
        var notes = N(df, dt, dten);
        var (r, d) = Deposit(0, 0, stock, Jam(jammed), notes);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(Acc(notes.Value, 0), r.Account);
        Assert.Equal(notes, d.Stock);
    }

    [Fact]
    public void Succes_remplir_une_cassette_jusqu_a_5_est_permis()
    {
        var (r, d) = Deposit(0, 0, N(4, 4, 4), null, N(1, 1, 1));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(5, 5, 5), d.Stock);
        Assert.Equal(Acc(80, 0), r.Account);
    }

    [Fact]
    public void Le_depot_passe_par_le_module_et_ne_sort_aucun_billet()
    {
        var spy = new SpyDispenser(new MoneyDispenser(N(1, 1, 1)));
        var r = new CashMachine(spy).Execute(Acc(10, 0), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Empty(spy.Dispensed);
        Assert.Equal(N(1, 1, 1), spy.Loaded.Aggregate(NoteCounts.None, (a, b) => a + b));
        Assert.Equal(N(2, 2, 2), spy.Stock);
    }

    [Fact]
    public void Un_depot_refuse_ne_touche_pas_au_module()
    {
        var spy = new SpyDispenser(new MoneyDispenser(N(5, 0, 0)));
        var r = new CashMachine(spy).Execute(Acc(10, 0), new Operation.Deposit(N(1, 0, 0)));

        Assert.Equal(AtmStatus.CassetteFull, r.Status);
        Assert.Empty(spy.Dispensed);
        Assert.Empty(spy.Loaded);
    }
}
