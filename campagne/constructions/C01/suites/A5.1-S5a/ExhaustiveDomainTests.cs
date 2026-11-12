using System.Text;
using static Atm.Tests.TestSupport;

namespace Atm.Tests;

/// <summary>
/// Compare la machine au modèle de référence (TestSupport) sur tout le domaine de la spec :
/// soldes × déjà retiré × stocks × cassette bloquée × montants (ou dépôts).
/// </summary>
public class ExhaustiveDomainTests
{
    private const int MaxReported = 15;

    public static TheoryData<int> Jams => new(0, 50, 20, 10);

    [Theory]
    [MemberData(nameof(Jams))]
    public void Every_withdrawal_in_the_domain_matches_the_spec(int jam)
    {
        var jammed = Jam(jam);
        var failures = new Failures();

        foreach (var stock in AllStocks())
        foreach (var amount in Amounts)
        foreach (var balance in Balances)
        foreach (var withdrawn in WithdrawnTodayValues)
        {
            var account = new AccountState(balance, withdrawn);
            var actual = Withdraw(stock, jammed, account, amount, out var stockAfter);
            var (expected, expectedStock) = ExpectedWithdraw(stock, jammed, account, amount);

            if (actual != expected || stockAfter != expectedStock)
            {
                failures.Add($"{Describe(account, stock, jammed)}, retrait {amount} : " +
                             $"attendu {Describe(expected, expectedStock)} ; obtenu {Describe(actual, stockAfter)}");
            }
        }

        failures.AssertNone();
    }

    [Theory]
    [MemberData(nameof(Jams))]
    public void Every_deposit_in_the_domain_matches_the_spec(int jam)
    {
        var jammed = Jam(jam);
        var failures = new Failures();

        foreach (var stock in AllStocks())
        foreach (var notes in AllDeposits())
        foreach (var balance in Balances)
        foreach (var withdrawn in new[] { 0, 90, 200 })
        {
            var account = new AccountState(balance, withdrawn);
            var actual = Deposit(stock, jammed, account, notes, out var stockAfter);
            var (expected, expectedStock) = ExpectedDeposit(stock, jammed, account, notes);

            if (actual != expected || stockAfter != expectedStock)
            {
                failures.Add($"{Describe(account, stock, jammed)}, dépôt {Describe(notes)} : " +
                             $"attendu {Describe(expected, expectedStock)} ; obtenu {Describe(actual, stockAfter)}");
            }
        }

        failures.AssertNone();
    }

    private static string Describe(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";

    private static string Describe(AccountState account, NoteCounts stock, Denomination? jammed) =>
        $"solde {account.Balance}, retiré {account.WithdrawnToday}, stock {Describe(stock)}, bloquée {(jammed is { } j ? ((int)j).ToString() : "aucune")}";

    private static string Describe(AtmResult r, NoteCounts stock) =>
        $"{r.Status}, billets {Describe(r.Notes)}, frais {r.Fee}, solde {r.Account.Balance}, retiré {r.Account.WithdrawnToday}, stock {Describe(stock)}";

    private sealed class Failures
    {
        private readonly StringBuilder _samples = new();
        private int _count;

        public void Add(string message)
        {
            if (_count++ < MaxReported) _samples.AppendLine(message);
        }

        public void AssertNone() =>
            Assert.True(_count == 0, $"{_count} cas non conformes, dont :\n{_samples}");
    }
}
