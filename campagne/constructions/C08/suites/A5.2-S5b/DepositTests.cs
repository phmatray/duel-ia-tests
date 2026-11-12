using static Atm.Tests.Harness;

namespace Atm.Tests;

public class DepositTests
{
    // ---- Contrôle 1 : dépôt vide ----

    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void EmptyDeposit_InvalidAmount(Denomination? jammed)
    {
        AssertRefused(AtmStatus.InvalidAmount, Deposit(100, 50, FullStock, N(), jammed), 100, 50, FullStock);
    }

    [Fact]
    public void EmptyDeposit_InvalidAmount_EvenWithEmptyStock()
    {
        AssertRefused(AtmStatus.InvalidAmount, Deposit(0, 0, N(), N()), 0, 0, N());
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Fifty, 1, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 1, 0)]
    [InlineData(Denomination.Ten, 0, 1, 1)]
    public void DepositTouchingJammedCassette_CassetteJammed(Denomination jammed, int f, int t, int te)
    {
        var stock = N(1, 1, 1);
        AssertRefused(AtmStatus.CassetteJammed, Deposit(100, 0, stock, N(f, t, te), jammed), 100, 0, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void CassetteJammed_ComesBeforeCassetteFull(Denomination jammed)
    {
        // Toutes les cassettes sont pleines, et le dépôt touche la bloquée.
        AssertRefused(AtmStatus.CassetteJammed, Deposit(100, 0, FullStock, N(1, 1, 1), jammed), 100, 0, FullStock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 1, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 0)]
    public void DepositAvoidingJammedCassette_IsAccepted(Denomination jammed, int f, int t, int te)
    {
        var stock = N(2, 2, 2);
        var deposit = N(f, t, te);
        AssertDepositOk(Deposit(100, 40, stock, deposit, jammed), 100, 40, stock, deposit);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(4, 5, 4, 1, 1, 1)]   // une seule cassette déborde : tout est refusé
    [InlineData(0, 0, 5, 1, 1, 1)]
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void DepositOverflowingACassette_CassetteFull(int sf, int st, int ste, int f, int t, int te)
    {
        var stock = N(sf, st, ste);
        AssertRefused(AtmStatus.CassetteFull, Deposit(100, 0, stock, N(f, t, te)), 100, 0, stock);
    }

    [Fact]
    public void CassetteFull_WithAnotherCassetteJammed_ButNotTouched()
    {
        var stock = N(5, 0, 3);
        AssertRefused(AtmStatus.CassetteFull, Deposit(100, 0, stock, N(1, 0, 1), Denomination.Twenty), 100, 0, stock);
    }

    // ---- Succès ----

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    public void ValidDeposit_Ok(int f, int t, int te)
    {
        var stock = N(4, 4, 4);   // remplir jusqu'à 5 est permis
        var deposit = N(f, t, te);
        var run = Deposit(250, 200, stock, deposit);
        AssertDepositOk(run, 250, 200, stock, deposit);
    }

    [Fact]
    public void ValidDeposit_IntoEmptyMachine_Ok()
    {
        var run = Deposit(0, 0, N(), N(1, 1, 1));
        AssertDepositOk(run, 0, 0, N(), N(1, 1, 1));
        Assert.Equal(new AccountState(80, 0), run.Result.Account);
    }

    [Fact]
    public void ValidDeposit_DoesNotTouchWithdrawnToday()
    {
        var run = Deposit(30, 120, N(1, 1, 1), N(0, 1, 0));
        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.Equal(new AccountState(50, 120), run.Result.Account);
    }
}
