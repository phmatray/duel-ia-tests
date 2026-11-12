using static Atm.Tests.TestKit;

namespace Atm.Tests;

/// <summary>
/// Balayage exhaustif du domaine, comparé à un oracle écrit depuis la spec.
/// Chaque test rassemble tous les écarts pour donner un diagnostic complet.
/// </summary>
public class DomainSweepTests
{
    private static string? CheckWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, Withdraw(amount));

        var status = Oracle.WithdrawStatus(account, stock, jammed, amount);
        string Where() => $"compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, retrait {amount}";

        if (result.Status != status)
            return $"{Where()} : statut {result.Status}, attendu {status}";

        if (status != AtmStatus.Ok)
        {
            if (result.Notes != NoteCounts.None || result.Fee != 0 || result.Account != account || dispenser.Stock != stock)
                return $"{Where()} : le refus a changé quelque chose ({result}, stock {dispenser.Stock})";
            return null;
        }

        var notes = Oracle.Fewest(amount, Oracle.Available(stock, jammed))!.Value;
        var fee = Oracle.Fee(amount);
        var expectedAccount = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
        if (result.Notes != notes || result.Fee != fee || result.Account != expectedAccount || dispenser.Stock != stock - notes)
            return $"{Where()} : obtenu {result} stock {dispenser.Stock}, attendu billets {notes}, frais {fee}, compte {expectedAccount}";

        return null;
    }

    private static void AssertNoFailures(List<string> failures) =>
        Assert.True(failures.Count == 0,
            $"{failures.Count} écart(s) :{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(30))}");

    [Fact]
    public void Withdraw_over_every_stock_jam_and_amount()
    {
        var failures = new List<string>();
        foreach (var stock in AllStocks())
        foreach (var jammed in AllJams)
        foreach (var amount in AllAmounts())
            if (CheckWithdraw(Account(250, 0), stock, jammed, amount) is { } f)
                failures.Add(f);

        AssertNoFailures(failures);
    }

    [Fact]
    public void Withdraw_over_every_balance_withdrawn_and_amount()
    {
        var failures = new List<string>();
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in AllAmounts())
        foreach (var stock in new[] { N(5, 5, 5), N(0, 0, 0), N(1, 2, 1) })
            if (CheckWithdraw(Account(balance, withdrawn), stock, null, amount) is { } f)
                failures.Add(f);

        AssertNoFailures(failures);
    }

    [Fact]
    public void Deposit_over_every_stock_jam_and_deposit()
    {
        var failures = new List<string>();
        var account = Account(120, 90);
        foreach (var stock in AllStocks())
        foreach (var jammed in AllJams)
        foreach (var notes in AllDeposits())
        {
            var dispenser = new MoneyDispenser(stock, jammed);
            var result = new CashMachine(dispenser).Execute(account, Deposit(notes));
            var status = Oracle.DepositStatus(stock, jammed, notes);
            var where = $"stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, dépôt {notes}";

            var (expectedAccount, expectedStock) = status == AtmStatus.Ok
                ? (account with { Balance = account.Balance + notes.Value }, stock + notes)
                : (account, stock);

            if (result.Status != status)
                failures.Add($"{where} : statut {result.Status}, attendu {status}");
            else if (result.Notes != NoteCounts.None || result.Fee != 0 || result.Account != expectedAccount
                     || dispenser.Stock != expectedStock)
                failures.Add($"{where} : obtenu {result} stock {dispenser.Stock}, attendu compte {expectedAccount} stock {expectedStock}");
        }

        AssertNoFailures(failures);
    }
}
