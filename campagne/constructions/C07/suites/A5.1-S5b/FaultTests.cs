using Atm.Tests.Support;
using static Atm.Tests.Support.N;

namespace Atm.Tests;

/// <summary>Le module rend false malgré les contrôles : DispenserFault, arrêt immédiat, Fee 0, compte identique.</summary>
public class FaultTests
{
    private static readonly AccountState Account = new(250, 0);

    [Fact]
    public void Retrait_dont_le_premier_passage_echoue()
    {
        var dispenser = new FaultyDispenser(Notes(5, 5, 5)) { FailDispenseAt = 1 };

        var result = new CashMachine(dispenser).Execute(Account, new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Account), result);
        Assert.Single(dispenser.Dispenses);
        Assert.Equal(Notes(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Retrait_dont_le_deuxieme_passage_echoue_rend_les_billets_deja_sortis()
    {
        // 50 en billets de 10 : cinq billets, donc au moins deux passages.
        var dispenser = new FaultyDispenser(Notes(0, 0, 5)) { FailDispenseAt = 2 };

        var result = new CashMachine(dispenser).Execute(Account, new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account, result.Account);
        Assert.Equal(2, dispenser.Dispenses.Count);   // on s'arrête aussitôt
        Assert.True(dispenser.Dispenses[0].Accepted);
        Assert.Equal(dispenser.Dispenses[0].Notes, result.Notes);
        Assert.Equal(Notes(0, 0, 5) - result.Notes, dispenser.Stock);
    }

    [Fact]
    public void Retrait_avec_frais_en_panne_ne_preleve_pas_les_frais()
    {
        // 180 = 50×3 + 20 + 10 : cinq billets, deux passages.
        var dispenser = new FaultyDispenser(Notes(5, 5, 5)) { FailDispenseAt = 2 };

        var result = new CashMachine(dispenser).Execute(Account, new Operation.Withdraw(180));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Account, result.Account);
        Assert.Equal(2, dispenser.Dispenses.Count);
        Assert.Equal(dispenser.Dispenses[0].Notes, result.Notes);
    }

    [Fact]
    public void Depot_que_le_module_refuse()
    {
        var dispenser = new FaultyDispenser(Notes(1, 1, 0)) { FailLoad = true };

        var result = new CashMachine(dispenser).Execute(new AccountState(10, 30), new Operation.Deposit(Notes(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(10, 30)), result);
        Assert.Single(dispenser.Loads);
        Assert.Equal(Notes(1, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Une_panne_n_est_jamais_produite_par_le_vrai_module_dans_le_domaine()
    {
        var failures = new List<string>();
        int[] accounts = [0, 105, 250];
        int[] withdrawn = [0, 100, 200];

        foreach (var stock in Scenario.AllStocks())
        foreach (var jammed in Scenario.JammedValues)
        foreach (var balance in accounts)
        foreach (var today in withdrawn)
        {
            foreach (var amount in Scenario.DomainAmounts)
            {
                var run = Scenario.Withdraw(balance, today, stock, jammed, amount);
                if (run.Result.Status == AtmStatus.DispenserFault)
                    failures.Add($"retrait {amount}, solde {balance}, retiré {today}, stock {stock}, bloquée {jammed}");
            }

            foreach (var deposit in Scenario.AllDeposits())
            {
                var run = Scenario.Deposit(balance, today, stock, jammed, deposit);
                if (run.Result.Status == AtmStatus.DispenserFault)
                    failures.Add($"dépôt {deposit}, solde {balance}, retiré {today}, stock {stock}, bloquée {jammed}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} panne(s), par ex. :\n{string.Join("\n", failures.Take(10))}");
    }
}
