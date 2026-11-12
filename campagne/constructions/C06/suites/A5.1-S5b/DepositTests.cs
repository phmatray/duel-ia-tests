using static Atm.Tests.Support;

namespace Atm.Tests;

public class DepositTests
{
    // ---------- Contrôle 1 : dépôt vide ----------

    [Fact]
    public void Depot_sans_billet_est_invalide()
    {
        var (atm, dispenser) = Machine(Notes(1, 1, 1));

        var result = atm.Execute(Account(100, 50), Deposit(NoteCounts.None));

        AssertRefused(AtmStatus.InvalidAmount, result, Account(100, 50), dispenser, Notes(1, 1, 1));
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Depot_vide_passe_avant_cassette_bloquee_et_pleine(Denomination jammed)
    {
        var (atm, dispenser) = Machine(Full, jammed);

        var result = atm.Execute(Account(100, 50), Deposit(NoteCounts.None));

        AssertRefused(AtmStatus.InvalidAmount, result, Account(100, 50), dispenser, Full);
    }

    // ---------- Contrôle 2 : cassette bloquée ----------

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 1, 0)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Depot_avec_un_billet_de_la_cassette_bloquee_est_refuse(Denomination jammed, int f, int t, int c)
    {
        var stock = Notes(1, 1, 1);
        var (atm, dispenser) = Machine(stock, jammed);

        var result = atm.Execute(Account(100, 50), Deposit(Notes(f, t, c)));

        AssertRefused(AtmStatus.CassetteJammed, result, Account(100, 50), dispenser, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 1)]
    public void Cassette_bloquee_passe_avant_cassette_pleine(Denomination jammed, int f, int t, int c)
    {
        var (atm, dispenser) = Machine(Full, jammed);

        var result = atm.Execute(Account(100, 50), Deposit(Notes(f, t, c)));

        AssertRefused(AtmStatus.CassetteJammed, result, Account(100, 50), dispenser, Full);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void Depot_sans_billet_de_la_cassette_bloquee_est_accepte(Denomination jammed, int f, int t, int c)
    {
        var stock = Notes(1, 1, 1);
        var deposit = Notes(f, t, c);
        var (atm, dispenser) = Machine(stock, jammed);

        var result = atm.Execute(Account(100, 50), Deposit(deposit));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(100 + deposit.Value, 50)), result);
        Assert.Equal(stock + deposit, dispenser.Stock);
    }

    // ---------- Contrôle 3 : cassette pleine ----------

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)] // une seule cassette pleine suffit : tout ou rien
    [InlineData(4, 4, 5, 1, 1, 1)]
    public void Depot_qui_fait_depasser_5_billets_est_refuse(int sf, int st, int sc, int f, int t, int c)
    {
        var stock = Notes(sf, st, sc);
        var (atm, dispenser) = Machine(stock);

        var result = atm.Execute(Account(100, 50), Deposit(Notes(f, t, c)));

        AssertRefused(AtmStatus.CassetteFull, result, Account(100, 50), dispenser, stock);
    }

    [Theory]
    [InlineData(4, 4, 4, 1, 1, 1)]
    [InlineData(5, 5, 4, 0, 0, 1)]
    [InlineData(5, 4, 5, 0, 1, 0)]
    [InlineData(4, 5, 5, 1, 0, 0)]
    public void Atteindre_exactement_5_billets_est_permis(int sf, int st, int sc, int f, int t, int c)
    {
        var stock = Notes(sf, st, sc);
        var deposit = Notes(f, t, c);
        var (atm, dispenser) = Machine(stock);

        var result = atm.Execute(Account(100, 50), Deposit(deposit));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(100 + deposit.Value, 50)), result);
        Assert.Equal(stock + deposit, dispenser.Stock);
    }

    // ---------- Le succès ----------

    public static TheoryData<NoteCounts> AllNonEmptyDeposits => new(
        Domain.Deposits.Where(d => d.Total > 0));

    [Theory]
    [MemberData(nameof(AllNonEmptyDeposits))]
    public void Depot_reussi_credite_la_valeur_et_range_les_billets(NoteCounts deposit)
    {
        var stock = Notes(2, 0, 3);
        var (atm, dispenser) = Machine(stock);

        var result = atm.Execute(Account(35, 120), Deposit(deposit));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(35 + deposit.Value, result.Account.Balance);
        Assert.Equal(120, result.Account.WithdrawnToday);
        Assert.Equal(stock + deposit, dispenser.Stock);
    }

    [Fact]
    public void Depot_de_80_au_solde_maximum()
    {
        var (atm, dispenser) = Machine(Notes(0, 0, 0));

        var result = atm.Execute(Account(250, 200), Deposit(Notes(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Account(330, 200)), result);
        Assert.Equal(Notes(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Depot_ne_reduit_pas_le_retrait_du_jour()
    {
        var (atm, _) = Machine(Notes(0, 0, 0));

        var result = atm.Execute(Account(0, 200), Deposit(Notes(1, 0, 0)));

        Assert.Equal(Account(50, 200), result.Account);
    }
}
