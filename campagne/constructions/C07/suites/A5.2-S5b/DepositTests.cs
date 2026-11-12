using static Atm.Tests.Support;

namespace Atm.Tests;

/// <summary>Le dépôt : chaque contrôle, leur ordre et le succès.</summary>
public class DepositTests
{
    // ---------- Contrôle 1 : dépôt vide ----------

    [Fact]
    public void Un_depot_sans_billet_est_invalide() =>
        AssertRefused(AtmStatus.InvalidAmount, new AccountState(100, 0), N(1, 1, 1), null, Deposit(NoteCounts.None));

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Un_depot_vide_est_invalide_meme_avec_une_cassette_bloquee(Denomination jammed) =>
        AssertRefused(AtmStatus.InvalidAmount, new AccountState(100, 0), Full, jammed, Deposit(NoteCounts.None));

    [Fact]
    public void Un_depot_vide_est_invalide_meme_avec_des_cassettes_pleines() =>
        AssertRefused(AtmStatus.InvalidAmount, new AccountState(100, 0), Full, null, Deposit(NoteCounts.None));

    // ---------- Contrôle 2 : cassette bloquée ----------

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]   // un seul billet fautif suffit
    [InlineData(Denomination.Twenty, 1, 1, 0)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void Un_depot_touchant_la_cassette_bloquee_est_refuse(Denomination jammed, int fifties, int twenties, int tens) =>
        AssertRefused(AtmStatus.CassetteJammed, new AccountState(100, 0), N(1, 1, 1), jammed, Deposit(N(fifties, twenties, tens)));

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    public void Cassette_bloquee_passe_avant_cassette_pleine(Denomination jammed, int fifties, int twenties, int tens) =>
        // La cassette bloquée est elle-même pleine : c'est le blocage qui l'emporte.
        AssertRefused(AtmStatus.CassetteJammed, new AccountState(100, 0), Full, jammed, Deposit(N(fifties, twenties, tens)));

    [Fact]
    public void Cassette_bloquee_passe_avant_une_autre_cassette_pleine() =>
        AssertRefused(AtmStatus.CassetteJammed, new AccountState(100, 0), N(0, 5, 0), Denomination.Ten, Deposit(N(0, 1, 1)));

    // ---------- Contrôle 3 : cassette pleine ----------

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)]   // une seule cassette pleine suffit : rien n'est rangé
    [InlineData(0, 0, 5, 1, 0, 1)]
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void Un_depot_qui_ferait_depasser_5_billets_est_refuse(
        int fifties, int twenties, int tens, int in50, int in20, int in10) =>
        AssertRefused(AtmStatus.CassetteFull, new AccountState(100, 0), N(fifties, twenties, tens), null, Deposit(N(in50, in20, in10)));

    [Fact]
    public void Cassette_pleine_avec_une_autre_cassette_bloquee_non_touchee() =>
        AssertRefused(AtmStatus.CassetteFull, new AccountState(100, 0), N(5, 0, 0), Denomination.Ten, Deposit(N(1, 1, 0)));

    // ---------- Le succès ----------

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    public void Un_depot_reussi_credite_le_compte_et_range_les_billets(int in50, int in20, int in10)
    {
        var deposit = N(in50, in20, in10);
        var stock = N(2, 3, 4);
        var (machine, dispenser) = NewAtm(stock);

        var result = machine.Execute(new AccountState(150, 70), Deposit(deposit));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(150 + deposit.Value, 70)), result);
        Assert.Equal(stock + deposit, dispenser.Stock);
    }

    [Fact]
    public void Un_depot_peut_remplir_une_cassette_jusqu_a_5_billets()
    {
        var (machine, dispenser) = NewAtm(N(4, 4, 4));

        var result = machine.Execute(new AccountState(0, 200), Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(80, 200)), result);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void Un_depot_reussit_si_la_cassette_bloquee_n_est_pas_touchee(
        Denomination jammed, int in50, int in20, int in10)
    {
        var deposit = N(in50, in20, in10);
        var (machine, dispenser) = NewAtm(N(2, 2, 2), jammed);

        var result = machine.Execute(new AccountState(100, 30), Deposit(deposit));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(100 + deposit.Value, 30)), result);
        Assert.Equal(N(2, 2, 2) + deposit, dispenser.Stock);
    }

    [Fact]
    public void Un_depot_ne_regarde_ni_le_plafond_ni_le_solde()
    {
        var (machine, dispenser) = NewAtm(N(0, 0, 0));

        var result = machine.Execute(new AccountState(0, 200), Deposit(N(0, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(10, 200)), result);
        Assert.Equal(N(0, 0, 1), dispenser.Stock);
    }
}
