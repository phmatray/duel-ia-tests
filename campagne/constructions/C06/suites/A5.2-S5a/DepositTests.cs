using static Atm.Tests.T;

namespace Atm.Tests;

public class DepositTests
{
    // ---- Contrôle 1 : dépôt vide ----

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    public void Empty_deposit_is_InvalidAmount(int jam)
    {
        // Même avec une cassette bloquée et des cassettes pleines, le contrôle 1 passe d'abord.
        var result = Deposit(Acc(10, 30), N(5, 5, 5), Jam(jam), NoteCounts.None);

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, Acc(10, 30)), result);
    }

    // ---- Contrôle 2 : cassette bloquée ----

    [Theory]
    [InlineData(50, 1, 0, 0)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(10, 1, 1, 1)]
    public void Note_of_jammed_cassette_is_CassetteJammed(int jam, int d50, int d20, int d10)
    {
        var result = Deposit(Acc(10, 30), N(0, 0, 0), Jam(jam), N(d50, d20, d10));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(20)]
    [InlineData(10)]
    public void CassetteJammed_is_checked_before_CassetteFull(int jam)
    {
        var result = Deposit(Acc(10, 30), N(5, 5, 5), Jam(jam), N(1, 1, 1));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
    }

    [Theory]
    [InlineData(50, 0, 1, 1)]
    [InlineData(20, 1, 0, 1)]
    [InlineData(10, 1, 1, 0)]
    public void Deposit_avoiding_jammed_cassette_is_accepted(int jam, int d50, int d20, int d10)
    {
        var (machine, dispenser) = Machine(N(1, 1, 1), Jam(jam));

        var result = machine.Execute(Acc(10, 30), new Operation.Deposit(N(d50, d20, d10)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(1, 1, 1) + N(d50, d20, d10), dispenser.Stock);
    }

    // ---- Contrôle 3 : cassette pleine ----

    [Theory]
    [InlineData(5, 0, 0, 1, 0, 0)]
    [InlineData(0, 5, 0, 0, 1, 0)]
    [InlineData(0, 0, 5, 0, 0, 1)]
    [InlineData(5, 0, 0, 1, 1, 1)]
    [InlineData(0, 0, 5, 1, 1, 1)]
    [InlineData(0, 4, 5, 0, 1, 1)]
    public void Exceeding_5_notes_in_a_cassette_is_CassetteFull(int s50, int s20, int s10, int d50, int d20, int d10)
    {
        var (machine, dispenser) = Machine(N(s50, s20, s10));

        var result = machine.Execute(Acc(10, 30), new Operation.Deposit(N(d50, d20, d10)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, Acc(10, 30)), result);
        Assert.Equal(N(s50, s20, s10), dispenser.Stock);
    }

    [Theory]
    [InlineData(4, 4, 4, 1, 1, 1)]
    [InlineData(4, 0, 0, 1, 0, 0)]
    [InlineData(0, 4, 0, 0, 1, 0)]
    [InlineData(0, 0, 4, 0, 0, 1)]
    [InlineData(5, 5, 4, 0, 0, 1)]
    public void Filling_a_cassette_up_to_5_is_accepted(int s50, int s20, int s10, int d50, int d20, int d10)
    {
        var (machine, dispenser) = Machine(N(s50, s20, s10));

        var result = machine.Execute(Acc(0, 0), new Operation.Deposit(N(d50, d20, d10)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(s50 + d50, s20 + d20, s10 + d10), dispenser.Stock);
    }

    // ---- Succès ----

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 1, 80)]
    [InlineData(1, 0, 1, 60)]
    public void Success_credits_value_and_loads_notes(int d50, int d20, int d10, int value)
    {
        var spy = new SpyDispenser(N(2, 2, 2));
        var machine = new CashMachine(spy);

        var result = machine.Execute(Acc(170, 120), new Operation.Deposit(N(d50, d20, d10)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, Acc(170 + value, 120)), result);
        Assert.Equal(N(2 + d50, 2 + d20, 2 + d10), spy.Stock);
        Assert.Empty(spy.DispenseCalls);
        Assert.Equal(N(d50, d20, d10), spy.LoadCalls.Aggregate(NoteCounts.None, (a, n) => a + n));
    }

    // ---- Refus : rien ne change ----

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0, 0)]  // InvalidAmount
    [InlineData(0, 0, 0, 20, 0, 1, 0)] // CassetteJammed
    [InlineData(5, 5, 5, 0, 1, 1, 1)]  // CassetteFull
    [InlineData(0, 0, 5, 0, 0, 0, 1)]  // CassetteFull (cassette de 10)
    public void Refusal_changes_nothing(int s50, int s20, int s10, int jam, int d50, int d20, int d10)
    {
        var spy = new SpyDispenser(N(s50, s20, s10), Jam(jam));
        var machine = new CashMachine(spy);

        var result = machine.Execute(Acc(10, 30), new Operation.Deposit(N(d50, d20, d10)));

        Assert.NotEqual(AtmStatus.Ok, result.Status);
        Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(10, 30), result.Account);
        Assert.Equal(N(s50, s20, s10), spy.Stock);
        Assert.Empty(spy.DispenseCalls);
    }
}
