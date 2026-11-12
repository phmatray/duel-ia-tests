using Atm.Tests.Support;
using static Atm.Tests.Support.Notes;

namespace Atm.Tests;

public class DepositTests
{
    private static (AtmResult Result, RecordingDispenser Dispenser) Deposit(
        AccountState account, NoteCounts notes, NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new RecordingDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AtmResult r, RecordingDispenser d, AccountState account, NoteCounts stock)
    {
        Assert.Equal(expected, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(stock, d.Stock);
        Assert.DoesNotContain(d.LoadCalls, c => c.Ok);
        Assert.DoesNotContain(d.DispenseCalls, c => c.Ok);
    }

    // ---- Contrôle 1 : dépôt vide ----

    [Fact]
    public void Depot_vide_est_un_montant_invalide()
    {
        var account = new AccountState(100, 50);
        var (r, d) = Deposit(account, N(0, 0, 0), N(1, 1, 1));

        AssertRefused(AtmStatus.InvalidAmount, r, d, account, N(1, 1, 1));
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_passe_avant_la_cassette_bloquee_et_pleine(Denomination jammed)
    {
        var account = new AccountState(0, 0);
        var (r, d) = Deposit(account, N(0, 0, 0), N(5, 5, 5), jammed);

        AssertRefused(AtmStatus.InvalidAmount, r, d, account, N(5, 5, 5));
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Twenty, 1, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Billet_de_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int n)
    {
        var account = new AccountState(30, 20);
        var stock = N(2, 2, 2);
        var (r, d) = Deposit(account, N(f, t, n), stock, jammed);

        AssertRefused(AtmStatus.CassetteJammed, r, d, account, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]  // la cassette bloquée est aussi pleine
    [InlineData(Denomination.Twenty, 0, 1, 1)] // une autre cassette visée est pleine
    [InlineData(Denomination.Ten, 1, 1, 1)]
    public void Cassette_bloquee_passe_avant_cassette_pleine(Denomination jammed, int f, int t, int n)
    {
        var account = new AccountState(30, 20);
        var stock = N(5, 5, 5);
        var (r, d) = Deposit(account, N(f, t, n), stock, jammed);

        AssertRefused(AtmStatus.CassetteJammed, r, d, account, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 0)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void Cassette_bloquee_non_touchee_ne_gene_pas(Denomination jammed, int f, int t, int n)
    {
        var account = new AccountState(30, 20);
        var notes = N(f, t, n);
        var (r, d) = Deposit(account, notes, N(2, 2, 2), jammed);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(30 + notes.Value, 20)), r);
        Assert.Equal(N(2, 2, 2) + notes, d.Stock);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(4, 4, 5, 1, 1, 1)] // une seule cassette déborde : tout est refusé
    [InlineData(5, 0, 0, 1, 1, 1)]
    public void Depasser_5_billets_dans_une_cassette_est_refuse(int sf, int st, int sn, int f, int t, int n)
    {
        var account = new AccountState(250, 200);
        var stock = N(sf, st, sn);
        var (r, d) = Deposit(account, N(f, t, n), stock);

        AssertRefused(AtmStatus.CassetteFull, r, d, account, stock);
    }

    [Fact]
    public void Remplir_une_cassette_jusqu_a_5_est_permis()
    {
        var (r, d) = Deposit(new AccountState(0, 0), N(1, 1, 1), N(4, 4, 4));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(80, 0)), r);
        Assert.Equal(N(5, 5, 5), d.Stock);
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
    public void Succes_credite_la_valeur_des_billets(int f, int t, int n, int value)
    {
        var account = new AccountState(250, 200);
        var notes = N(f, t, n);
        var (r, d) = Deposit(account, notes, N(0, 0, 0));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(250 + value, 200), r.Account);
        Assert.Equal(notes, d.Stock);
        Assert.Empty(d.DispenseCalls);
    }

    [Fact]
    public void Succes_sans_frais_meme_au_dessus_de_100_et_sans_toucher_au_retrait_du_jour()
    {
        var (r, _) = Deposit(new AccountState(5, 70), N(1, 1, 1), N(3, 0, 2));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(85, 70)), r);
    }
}
