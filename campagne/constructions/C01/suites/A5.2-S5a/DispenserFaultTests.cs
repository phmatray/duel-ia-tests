using static Atm.Tests.Helpers;

namespace Atm.Tests;

/// <summary>
/// « Si le module rend false malgré ces contrôles, c'est une panne. » Le vrai module ne le fait jamais
/// dans le domaine : on simule la panne avec un module de test.
/// </summary>
public class DispenserFaultTests
{
    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var faulty = new FaultyDispenser(Full, null, failOnCall: 1);
        var r = new CashMachine(faulty).Execute(new AccountState(250, 0), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(250, 0)), r);
        Assert.Equal(1, faulty.Calls);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Panne_en_cours_de_retrait_rend_les_billets_deja_sortis_et_s_arrete(int failOnCall)
    {
        // 150 avec 20×5 10×5 : 10 billets, donc au moins 3 passages.
        var faulty = new FaultyDispenser(N(0, 5, 5), null, failOnCall);
        var r = new CashMachine(faulty).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        var alreadyOut = faulty.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b);
        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(failOnCall - 1, faulty.Dispensed.Count);
        Assert.True(alreadyOut.Total > 0);
        Assert.Equal(alreadyOut, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(250, 0), r.Account);
        Assert.Equal(failOnCall, faulty.Calls); // on s'arrête aussitôt
    }

    [Fact]
    public void Panne_lors_d_un_depot()
    {
        var faulty = new FaultyDispenser(N(1, 1, 1), null, failOnCall: 1);
        var r = new CashMachine(faulty).Execute(new AccountState(100, 50), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(100, 50)), r);
        Assert.Equal(1, faulty.Calls);
        Assert.Equal(N(1, 1, 1), faulty.Stock);
    }

    [Fact]
    public void Le_vrai_module_ne_tombe_jamais_en_panne_dans_le_domaine()
    {
        var failures = new List<string>();
        foreach (var stock in AllStocks())
        foreach (var jammed in AllJammed)
        {
            foreach (var amount in AllAmounts())
            {
                var (r, _) = Run(250, 0, stock, jammed, new Operation.Withdraw(amount));
                if (r.Status == AtmStatus.DispenserFault)
                    failures.Add($"Retrait {amount}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}");
            }
            foreach (var notes in AllDeposits())
            {
                var (r, _) = Run(0, 0, stock, jammed, new Operation.Deposit(notes));
                if (r.Status == AtmStatus.DispenserFault)
                    failures.Add($"Dépôt {notes}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} cas en DispenserFault, par exemple :\n" + string.Join("\n", failures.Take(15)));
    }
}
