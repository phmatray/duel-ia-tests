using System.Text;
using Atm.Tests.Support;
using static Atm.Tests.Support.Notes;

namespace Atm.Tests;

/// <summary>
/// Parcourt tout le domaine de la spec et compare la machine au modèle de référence (<see cref="Oracle"/>) :
/// statut, billets, frais, compte, stock final, et passages de 4 billets au plus.
/// </summary>
public class DomainTests
{
    private const int MaxReported = 15;

    public static TheoryData<int> Amounts() => new(DomainAmounts);

    public static TheoryData<int> Balances() => new(DomainBalances);

    [Theory]
    [MemberData(nameof(Amounts))]
    public void Retrait_sur_tout_le_domaine(int amount)
    {
        var failures = new StringBuilder();
        var count = 0;

        foreach (var balance in DomainBalances)
        foreach (var withdrawn in DomainWithdrawnToday)
        foreach (var stock in AllStocks())
        foreach (var jammed in JamCases)
        {
            var account = new AccountState(balance, withdrawn);
            var expected = Oracle.Withdraw(account, amount, stock, jammed);
            var dispenser = new RecordingDispenser(stock, jammed);
            var problem = Check(dispenser, account, new Operation.Withdraw(amount), expected);

            if (problem is null && dispenser.DispenseCalls.Any(c => c.Notes.Total > MoneyDispenser.MaxNotesPerPass))
            {
                problem = "un passage de plus de 4 billets";
            }

            if (problem is not null && ++count <= MaxReported)
            {
                failures.AppendLine($"solde {balance}, retiré {withdrawn}, stock {Show(stock)}, bloquée {Show(jammed)}, retrait {amount} : {problem}");
            }
        }

        Assert.True(count == 0, $"{count} cas faux, dont :\n{failures}");
    }

    [Theory]
    [MemberData(nameof(Balances))]
    public void Depot_sur_tout_le_domaine(int balance)
    {
        var failures = new StringBuilder();
        var count = 0;

        foreach (var withdrawn in DomainWithdrawnToday)
        foreach (var stock in AllStocks())
        foreach (var jammed in JamCases)
        foreach (var notes in AllDeposits())
        {
            var account = new AccountState(balance, withdrawn);
            var expected = Oracle.Deposit(account, notes, stock, jammed);
            var dispenser = new RecordingDispenser(stock, jammed);
            var problem = Check(dispenser, account, new Operation.Deposit(notes), expected);

            if (problem is not null && ++count <= MaxReported)
            {
                failures.AppendLine($"solde {balance}, retiré {withdrawn}, stock {Show(stock)}, bloquée {Show(jammed)}, dépôt {Show(notes)} : {problem}");
            }
        }

        Assert.True(count == 0, $"{count} cas faux, dont :\n{failures}");
    }

    private static string? Check(RecordingDispenser dispenser, AccountState account, Operation op, Oracle.Expected expected)
    {
        AtmResult actual;
        try
        {
            actual = new CashMachine(dispenser).Execute(account, op);
        }
        catch (Exception e)
        {
            return $"exception {e.GetType().Name} : {e.Message}";
        }

        var want = new AtmResult(expected.Status, expected.Notes, expected.Fee, expected.Account);
        if (actual != want)
        {
            return $"attendu {Describe(want)}, obtenu {Describe(actual)}";
        }

        if (dispenser.Stock != expected.StockAfter)
        {
            return $"stock final attendu {Show(expected.StockAfter)}, obtenu {Show(dispenser.Stock)}";
        }

        return null;
    }

    private static string Describe(AtmResult r) =>
        $"{r.Status} [{Show(r.Notes)}] frais {r.Fee} solde {r.Account.Balance} retiré {r.Account.WithdrawnToday}";
}
