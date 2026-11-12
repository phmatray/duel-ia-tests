using static Atm.Tests.TestKit;

namespace Atm.Tests;

public class DepositTests
{
    // ---- Contrôle 1 : dépôt vide ----

    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Twenty)]
    public void Empty_deposit_is_InvalidAmount(Denomination? jammed)
    {
        var (result, dispenser) = Run(100, 0, FullStock, jammed, Deposit(0, 0, 0));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, new AccountState(100, 0)), result);
        Assert.Empty(dispenser.Loaded);
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 1)]
    [InlineData(Denomination.Fifty, 1, 0, 1)]
    public void A_note_for_the_jammed_cassette_is_CassetteJammed(Denomination jammed, int d50, int d20, int d10)
    {
        var (result, dispenser) = Run(100, 0, Notes(1, 1, 1), jammed, Deposit(d50, d20, d10));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
        Assert.Equal(Notes(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void CassetteJammed_is_checked_before_CassetteFull()
    {
        // La cassette de 20 est pleine ET le 10 est bloqué.
        var (result, _) = Run(100, 0, Notes(0, 5, 0), Denomination.Ten, Deposit(0, 1, 1));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
    }

    [Fact]
    public void Notes_for_other_cassettes_are_accepted_while_one_is_jammed()
    {
        var (result, dispenser) = Run(100, 20, Notes(0, 0, 5), Denomination.Ten, Deposit(1, 1, 0));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(170, 20)), result);
        Assert.Equal(Notes(1, 1, 5), dispenser.Stock);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(4, 5, 4, 1, 1, 1)]
    [InlineData(5, 5, 5, 1, 1, 1)]
    public void Going_above_5_notes_in_a_cassette_is_CassetteFull(int s50, int s20, int s10, int d50, int d20, int d10)
    {
        var (result, dispenser) = Run(100, 0, Notes(s50, s20, s10), null, Deposit(d50, d20, d10));

        Assert.Equal(AtmStatus.CassetteFull, result.Status);
        Assert.Equal(Notes(s50, s20, s10), dispenser.Stock);
    }

    [Fact]
    public void Filling_a_cassette_up_to_exactly_5_is_allowed()
    {
        var (result, dispenser) = Run(0, 0, Notes(4, 4, 4), null, Deposit(1, 1, 1));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(FullStock, dispenser.Stock);
    }

    // ---- Le succès ----

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 0, 70)]
    [InlineData(1, 1, 1, 80)]
    public void Ok_loads_the_notes_and_credits_their_value_only(int d50, int d20, int d10, int value)
    {
        var (result, dispenser) = Run(250, 200, Notes(2, 2, 2), null, Deposit(d50, d20, d10));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(250 + value, 200)), result);
        Assert.Equal(Notes(2 + d50, 2 + d20, 2 + d10), dispenser.Stock);
        Assert.Empty(dispenser.Dispensed);
    }

    [Fact]
    public void Deposit_is_not_subject_to_withdrawal_rules()
    {
        // Solde nul, plafond du jour atteint, module vide : le dépôt passe.
        var (result, dispenser) = Run(0, 200, NoteCounts.None, null, Deposit(1, 0, 0));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(50, 200)), result);
        Assert.Equal(Notes(1, 0, 0), dispenser.Stock);
    }
}
