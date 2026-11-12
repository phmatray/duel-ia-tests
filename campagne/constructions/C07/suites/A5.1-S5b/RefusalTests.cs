using Atm.Tests.Support;
using static Atm.Tests.Support.N;

namespace Atm.Tests;

/// <summary>Sur un refus, rien ne change : Notes vide, Fee à 0, compte identique, aucun billet déplacé.</summary>
public class RefusalTests
{
    public static TheoryData<string, int, int, NoteCounts, Denomination?, Operation, AtmStatus> Refusals => new()
    {
        { "montant négatif", 250, 0, Notes(5, 5, 5), null, new Operation.Withdraw(-10), AtmStatus.InvalidAmount },
        { "montant nul", 250, 0, Notes(5, 5, 5), null, new Operation.Withdraw(0), AtmStatus.InvalidAmount },
        { "montant 5", 250, 0, Notes(5, 5, 5), null, new Operation.Withdraw(5), AtmStatus.InvalidAmount },
        { "plafond", 250, 150, Notes(5, 5, 5), null, new Operation.Withdraw(60), AtmStatus.DailyLimitExceeded },
        { "plafond, gros retrait", 250, 0, Notes(5, 5, 5), null, new Operation.Withdraw(220), AtmStatus.DailyLimitExceeded },
        { "solde, frais", 110, 0, Notes(5, 5, 5), null, new Operation.Withdraw(110), AtmStatus.InsufficientFunds },
        { "solde", 50, 0, Notes(5, 5, 5), null, new Operation.Withdraw(60), AtmStatus.InsufficientFunds },
        { "billets, bloquée", 250, 0, Notes(1, 0, 0), Denomination.Fifty, new Operation.Withdraw(50), AtmStatus.CannotDispense },
        { "billets, combinaison", 250, 0, Notes(5, 5, 0), null, new Operation.Withdraw(30), AtmStatus.CannotDispense },
        { "billets, stock", 250, 0, Notes(1, 1, 1), null, new Operation.Withdraw(90), AtmStatus.CannotDispense },
        { "dépôt vide", 10, 30, Notes(1, 1, 1), null, new Operation.Deposit(NoteCounts.None), AtmStatus.InvalidAmount },
        { "dépôt bloqué", 10, 30, Notes(0, 1, 0), Denomination.Twenty, new Operation.Deposit(Notes(1, 1, 0)), AtmStatus.CassetteJammed },
        { "dépôt plein", 10, 30, Notes(0, 5, 0), null, new Operation.Deposit(Notes(1, 1, 1)), AtmStatus.CassetteFull },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Un_refus_ne_change_rien(
        string label, int balance, int withdrawn, NoteCounts stock, Denomination? jammed, Operation operation, AtmStatus expected)
    {
        _ = label;
        var run = Scenario.Execute(balance, withdrawn, stock, jammed, operation);

        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, new AccountState(balance, withdrawn)), run.Result);
        Assert.Equal(stock, run.Dispenser.Stock);
        Assert.DoesNotContain(run.Dispenser.Dispenses, d => d.Accepted);
        Assert.DoesNotContain(run.Dispenser.Loads, l => l.Accepted);
    }
}
