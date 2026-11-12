using static Atm.Tests.TestSupport;

namespace Atm.Tests;

/// <summary>Règles transverses : refus sans effet, machine sans mémoire.</summary>
public class MachineBehaviourTests
{
    public static TheoryData<int, int, int, int, int, Denomination?, Operation, AtmStatus> Refusals => new()
    {
        { 250, 0,   5, 5, 5, null,                new Operation.Withdraw(0),             AtmStatus.InvalidAmount },
        { 250, 150, 5, 5, 5, null,                new Operation.Withdraw(60),            AtmStatus.DailyLimitExceeded },
        { 105, 0,   5, 5, 5, null,                new Operation.Withdraw(110),           AtmStatus.InsufficientFunds },
        { 250, 0,   5, 5, 5, Denomination.Ten,    new Operation.Withdraw(30),            AtmStatus.CannotDispense },
        { 250, 0,   3, 2, 1, Denomination.Fifty,  new Operation.Withdraw(200),           AtmStatus.CannotDispense },
        { 100, 20,  1, 1, 1, null,                new Operation.Deposit(N(0, 0, 0)),     AtmStatus.InvalidAmount },
        { 100, 20,  1, 1, 1, Denomination.Twenty, new Operation.Deposit(N(1, 1, 1)),     AtmStatus.CassetteJammed },
        { 100, 20,  1, 1, 5, null,                new Operation.Deposit(N(1, 1, 1)),     AtmStatus.CassetteFull },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Un_refus_ne_change_rien(
        int balance, int withdrawn, int f, int t, int n, Denomination? jammed, Operation op, AtmStatus expected) =>
        AssertRefused(expected, Run(balance, withdrawn, N(f, t, n), jammed, op), balance, withdrawn, N(f, t, n));

    [Fact]
    public void Machine_sans_memoire_apres_un_refus()
    {
        var dispenser = new MoneyDispenser(FullStock);
        var machine = new CashMachine(dispenser);

        machine.Execute(new AccountState(0, 0), new Operation.Withdraw(100));      // refusé
        machine.Execute(new AccountState(250, 200), new Operation.Withdraw(10));   // refusé
        var result = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, new AccountState(135, 110)), result);
        Assert.Equal(N(3, 5, 4), dispenser.Stock);
    }

    [Fact]
    public void Machine_sans_memoire_apres_un_succes()
    {
        // Le compte passé en entrée fait foi, pas celui rendu au coup précédent.
        var dispenser = new MoneyDispenser(FullStock);
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(3, 0, 0), 5, new AccountState(95, 150)), first);
        // Même compte en entrée : le premier retrait n'est pas pris en compte (ni solde, ni plafond).
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, new AccountState(150, 100)), second);
        Assert.Equal(N(0, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Machine_lit_le_stock_courant_du_module()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var machine = new CashMachine(dispenser);

        var empty = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.CannotDispense, empty.Status);

        var deposit = machine.Execute(new AccountState(0, 0), new Operation.Deposit(N(1, 0, 0)));
        Assert.Equal(AtmStatus.Ok, deposit.Status);

        var withdraw = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, new AccountState(200, 50)), withdraw);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }
}
