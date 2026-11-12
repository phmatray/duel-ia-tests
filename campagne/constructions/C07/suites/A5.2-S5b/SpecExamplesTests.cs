using static Atm.Tests.Support;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Retrait_60_sans_billets_de_10_sort_trois_billets_de_20()
    {
        var (machine, dispenser) = NewAtm(N(1, 3, 0));

        var result = machine.Execute(new AccountState(100, 0), Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, new AccountState(40, 60)), result);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_80_sort_un_billet_de_chaque_coupure()
    {
        var (machine, dispenser) = NewAtm(N(1, 1, 1));

        var result = machine.Execute(new AccountState(100, 0), Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, new AccountState(20, 80)), result);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_100_sans_frais_sort_deux_billets_de_50()
    {
        var (machine, dispenser) = NewAtm(Full);

        var result = machine.Execute(new AccountState(105, 0), Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, new AccountState(5, 100)), result);
        Assert.Equal(N(3, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Retrait_110_preleve_5_euros_de_frais()
    {
        var (machine, dispenser) = NewAtm(Full);

        var result = machine.Execute(new AccountState(115, 0), Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, new AccountState(0, 110)), result);
        Assert.Equal(N(3, 5, 4), dispenser.Stock);
    }

    [Fact]
    public void Retrait_110_avec_solde_110_est_refuse_a_cause_des_frais() =>
        AssertRefused(AtmStatus.InsufficientFunds, new AccountState(110, 0), Full, null, Withdraw(110));

    [Fact]
    public void Retrait_60_apres_150_deja_retires_depasse_le_plafond() =>
        AssertRefused(AtmStatus.DailyLimitExceeded, new AccountState(250, 150), Full, null, Withdraw(60));

    [Fact]
    public void Retrait_50_avec_la_cassette_de_50_bloquee_est_impossible() =>
        AssertRefused(AtmStatus.CannotDispense, new AccountState(250, 0), N(1, 0, 0), Denomination.Fifty, Withdraw(50));

    [Fact]
    public void Retrait_40_avec_la_cassette_de_50_bloquee_sort_deux_billets_de_20()
    {
        var (machine, dispenser) = NewAtm(N(2, 2, 0), Denomination.Fifty);

        var result = machine.Execute(new AccountState(250, 0), Withdraw(40));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, new AccountState(210, 40)), result);
        Assert.Equal(N(2, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Retrait_5_est_un_montant_invalide() =>
        AssertRefused(AtmStatus.InvalidAmount, new AccountState(0, 200), N(0, 0, 0), null, Withdraw(5));

    [Fact]
    public void Depot_dans_une_cassette_pleine_est_refuse() =>
        AssertRefused(AtmStatus.CassetteFull, new AccountState(10, 0), N(0, 5, 0), null, Deposit(N(0, 1, 0)));

    [Fact]
    public void Depot_dans_la_cassette_bloquee_est_refuse() =>
        AssertRefused(AtmStatus.CassetteJammed, new AccountState(10, 30), N(0, 1, 0), Denomination.Twenty, Deposit(N(0, 1, 0)));

    [Fact]
    public void Depot_de_60_credite_le_compte_et_remplit_les_cassettes()
    {
        var (machine, dispenser) = NewAtm(N(1, 1, 0));

        var result = machine.Execute(new AccountState(10, 30), Deposit(N(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(70, 30)), result);
        Assert.Equal(N(2, 1, 1), dispenser.Stock);
    }
}
