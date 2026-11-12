using System.Text;

namespace Atm.Tests;

/// <summary>
/// Parcourt tout le domaine de la spec et compare chaque résultat à l'oracle écrit depuis la spec
/// (statut, billets, frais, compte et stock du module après l'opération).
/// </summary>
public class ExhaustiveDomainTests
{
    private const int MaxReported = 5;

    public static TheoryData<int> WithdrawAmounts => new(Domain.Amounts);

    [Theory]
    [MemberData(nameof(WithdrawAmounts))]
    public void Withdraw_matches_spec_on_whole_domain(int amount)
    {
        var failures = new StringBuilder();
        var count = 0;

        foreach (var jammed in Domain.Jams)
        foreach (var stock in Domain.Stocks)
        foreach (var balance in Domain.Balances)
        foreach (var withdrawn in Domain.WithdrawnToday)
        {
            var account = new AccountState(balance, withdrawn);
            var dispenser = new MoneyDispenser(stock, jammed);
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
            var expected = Oracle.Withdraw(account, stock, jammed, amount);

            var actualTuple = (actual.Status, actual.Notes, actual.Fee, actual.Account, dispenser.Stock);
            if (actualTuple != expected && ++count <= MaxReported)
            {
                failures.AppendLine($"{account} stock={stock} bloquée={jammed?.ToString() ?? "aucune"} retrait {amount}: attendu {expected}, obtenu {actualTuple}");
            }
        }

        Assert.True(count == 0, $"{count} écart(s) :\n{failures}");
    }

    public static TheoryData<NoteCounts> Deposits => new(Domain.Deposits);

    [Theory]
    [MemberData(nameof(Deposits))]
    public void Deposit_matches_spec_on_whole_domain(NoteCounts notes)
    {
        var failures = new StringBuilder();
        var count = 0;

        foreach (var jammed in Domain.Jams)
        foreach (var stock in Domain.Stocks)
        foreach (var balance in Domain.Balances)
        foreach (var withdrawn in Domain.WithdrawnToday)
        {
            var account = new AccountState(balance, withdrawn);
            var dispenser = new MoneyDispenser(stock, jammed);
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));
            var expected = Oracle.Deposit(account, stock, jammed, notes);

            var actualTuple = (actual.Status, actual.Notes, actual.Fee, actual.Account, dispenser.Stock);
            if (actualTuple != expected && ++count <= MaxReported)
            {
                failures.AppendLine($"{account} stock={stock} bloquée={jammed?.ToString() ?? "aucune"} dépôt {notes}: attendu {expected}, obtenu {actualTuple}");
            }
        }

        Assert.True(count == 0, $"{count} écart(s) :\n{failures}");
    }
}
