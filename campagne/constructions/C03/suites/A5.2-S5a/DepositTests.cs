using static Atm.Tests.Fixture;

namespace Atm.Tests;

public class DepositTests
{
    // ---- Contrôle 1 : dépôt vide ----

    [Fact]
    public void Depot_sans_billet_est_invalide()
    {
        var account = Account(100, 30);
        var (result, dispenser) = Deposit(account, NoteCounts.None, Notes(1, 1, 1));

        AssertRefused(AtmStatus.InvalidAmount, result, account, dispenser, Notes(1, 1, 1));
    }

    [Fact]
    public void Depot_vide_passe_avant_cassette_bloquee_et_pleine()
    {
        var account = Account(100, 30);
        var (result, dispenser) = Deposit(account, NoteCounts.None, Full, Denomination.Twenty);

        AssertRefused(AtmStatus.InvalidAmount, result, account, dispenser, Full);
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Billet_de_la_cassette_bloquee_est_refuse(Denomination jammed)
    {
        var account = Account(100, 30);
        var stock = Notes(1, 1, 1);
        var (result, dispenser) = Deposit(account, Notes(1, 1, 1), stock, jammed);

        AssertRefused(AtmStatus.CassetteJammed, result, account, dispenser, stock);
    }

    [Fact]
    public void Cassette_bloquee_passe_avant_cassette_pleine()
    {
        // Les 20 sont bloqués et les 10 sont pleins : c'est le blocage qui l'emporte.
        var account = Account(100, 30);
        var stock = Notes(0, 0, 5);
        var (result, dispenser) = Deposit(account, Notes(0, 1, 1), stock, Denomination.Twenty);

        AssertRefused(AtmStatus.CassetteJammed, result, account, dispenser, stock);
    }

    [Fact]
    public void Cassette_bloquee_pleine_et_depot_dans_cette_cassette_donne_cassette_bloquee()
    {
        var account = Account(100, 30);
        var stock = Notes(0, 5, 0);
        var (result, dispenser) = Deposit(account, Notes(0, 1, 0), stock, Denomination.Twenty);

        AssertRefused(AtmStatus.CassetteJammed, result, account, dispenser, stock);
    }

    [Fact]
    public void Cassette_bloquee_n_empeche_pas_de_deposer_dans_les_autres()
    {
        var (result, dispenser) = Deposit(Account(100, 30), Notes(1, 0, 1), Notes(1, 1, 1), Denomination.Twenty);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Account(160, 30), result.Account);
        Assert.Equal(Notes(2, 1, 2), dispenser.Stock);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(4, 5, 4, 1, 1, 1)] // une seule cassette déborde : tout est refusé
    public void Depot_faisant_deborder_une_cassette_est_refuse_en_entier(
        int sf, int st, int sn, int df, int dt, int dn)
    {
        var account = Account(100, 30);
        var stock = Notes(sf, st, sn);
        var (result, dispenser) = Deposit(account, Notes(df, dt, dn), stock);

        AssertRefused(AtmStatus.CassetteFull, result, account, dispenser, stock);
    }

    [Fact]
    public void Remplir_une_cassette_jusqu_a_5_est_permis()
    {
        var (result, dispenser) = Deposit(Account(0, 0), Notes(1, 1, 1), Notes(4, 4, 4));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Account(80, 0), result.Account);
        Assert.Equal(Full, dispenser.Stock);
    }

    // ---- Succès ----

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(0, 1, 1, 30)]
    [InlineData(1, 0, 1, 60)]
    [InlineData(1, 1, 1, 80)]
    public void Depot_credite_la_valeur_des_billets_sans_toucher_au_retrait_du_jour(int f, int t, int n, int value)
    {
        var deposit = Notes(f, t, n);
        var stock = Notes(2, 2, 2);
        var (result, dispenser) = Deposit(Account(250, 200), deposit, stock);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account(250 + value, 200), result.Account);
        Assert.Equal(stock + deposit, dispenser.Stock);
    }

    [Fact]
    public void Depot_dans_un_module_vide()
    {
        var (result, dispenser) = Deposit(Account(0, 0), Notes(0, 1, 0), Notes(0, 0, 0));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(Account(20, 0), result.Account);
        Assert.Equal(Notes(0, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Un_depot_puis_un_retrait_utilisent_le_meme_module()
    {
        var (machine, dispenser) = Machine(Notes(0, 0, 0));

        var deposit = machine.Execute(Account(0, 0), new Operation.Deposit(Notes(1, 0, 0)));
        var withdraw = machine.Execute(deposit.Account, new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.Ok, deposit.Status);
        Assert.Equal(AtmStatus.Ok, withdraw.Status);
        Assert.Equal(Notes(1, 0, 0), withdraw.Notes);
        Assert.Equal(Account(0, 50), withdraw.Account);
        Assert.Equal(Notes(0, 0, 0), dispenser.Stock);
    }
}
