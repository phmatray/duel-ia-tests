using Atm.Tests.Support;
using static Atm.Tests.Support.Notes;

namespace Atm.Tests;

/// <summary>« La machine est sans mémoire entre deux appels : elle ne dépend que du compte, de l'opération et du module. »</summary>
public class StatelessnessTests
{
    [Fact]
    public void Une_suite_d_operations_donne_les_memes_resultats_qu_une_machine_neuve_a_chaque_fois()
    {
        var dispenser = new MoneyDispenser(N(3, 3, 3), Denomination.Ten);
        var machine = new CashMachine(dispenser);

        (AccountState Account, Operation Op)[] steps =
        [
            (new AccountState(250, 0), new Operation.Withdraw(150)),
            (new AccountState(250, 0), new Operation.Withdraw(150)),
            (new AccountState(20, 190), new Operation.Withdraw(20)),
            (new AccountState(100, 0), new Operation.Deposit(N(1, 1, 0))),
            (new AccountState(100, 0), new Operation.Deposit(N(0, 0, 1))),
            (new AccountState(250, 0), new Operation.Withdraw(70)),
            (new AccountState(0, 0), new Operation.Deposit(N(0, 0, 0))),
            (new AccountState(250, 0), new Operation.Withdraw(40)),
            (new AccountState(250, 100), new Operation.Withdraw(100)),
        ];

        foreach (var (account, op) in steps)
        {
            var twin = new MoneyDispenser(dispenser.Stock, dispenser.Jammed);
            var expected = new CashMachine(twin).Execute(account, op);

            var actual = machine.Execute(account, op);

            Assert.Equal(expected, actual);
            Assert.Equal(twin.Stock, dispenser.Stock);
        }
    }

    [Fact]
    public void La_machine_lit_le_stock_du_module_a_chaque_appel()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(new AccountState(100, 0), new Operation.Withdraw(50)).Status);

        Assert.True(dispenser.Load(N(1, 0, 0)));

        var r = machine.Execute(new AccountState(100, 0), new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, new AccountState(50, 50)), r);
    }

    [Fact]
    public void Le_meme_appel_repete_sur_un_module_inchange_donne_le_meme_resultat()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);
        var op = new Operation.Withdraw(60);
        var account = new AccountState(250, 150);

        var first = machine.Execute(account, op);
        var second = machine.Execute(account, op);

        Assert.Equal(AtmStatus.DailyLimitExceeded, first.Status);
        Assert.Equal(first, second);
    }
}
