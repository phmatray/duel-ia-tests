using static Atm.Tests.TestKit;

namespace Atm.Tests;

/// <summary>
/// « Si le module rend false malgré ces contrôles, c'est une panne. » Le vrai module ne le fait jamais
/// dans le domaine : on le simule avec un module défaillant pour vérifier la règle de panne.
/// </summary>
public class DispenserFaultTests
{
    [Fact]
    public void Withdrawal_failing_on_first_pass_is_DispenserFault_with_no_notes()
    {
        var module = new FaultyDispenser(N(5, 5, 5), null, failOnDispenseCall: 1);

        var result = new CashMachine(module).Execute(Acc(250, 0), new Operation.Withdraw(110));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(250, 0), result.Account);
        Assert.Equal(1, module.DispenseCalls); // on s'arrête aussitôt
    }

    [Fact]
    public void Withdrawal_failing_on_a_later_pass_returns_the_notes_already_out_and_stops()
    {
        // 200 avec 50×2 20×5 : 7 billets, donc au moins deux passages.
        var module = new FaultyDispenser(N(2, 5, 0), null, failOnDispenseCall: 2);

        var result = new CashMachine(module).Execute(Acc(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(250, 0), result.Account);
        Assert.Equal(2, module.DispenseCalls);
        // Les billets annoncés sont exactement ceux sortis du module lors du premier passage.
        Assert.Equal(N(2, 5, 0) - module.Stock, result.Notes);
        Assert.InRange(result.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass);
    }

    [Fact]
    public void Deposit_refused_by_the_module_is_DispenserFault_with_no_notes()
    {
        var module = new FaultyDispenser(N(1, 1, 1), null, failLoad: true);

        var result = new CashMachine(module).Execute(Acc(100, 30), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(100, 30), result.Account);
        Assert.Equal(1, module.LoadCalls);
        Assert.Equal(N(1, 1, 1), module.Stock);
    }
}
