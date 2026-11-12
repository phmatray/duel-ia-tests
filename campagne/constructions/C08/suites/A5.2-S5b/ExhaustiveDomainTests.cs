using static Atm.Tests.Harness;

namespace Atm.Tests;

/// <summary>
/// Parcours systématique du domaine de la spec, comparé à un oracle écrit d'après les règles.
/// Chaque test rapporte les premiers écarts trouvés.
/// </summary>
public class ExhaustiveDomainTests
{
    private const int MaxReported = 15;

    private static void Check(
        List<string> failures, int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount)
    {
        var (status, notes, fee) = SpecOracle.Withdraw(balance, withdrawnToday, stock, jammed, amount);
        var expectedAccount = status == AtmStatus.Ok
            ? new AccountState(balance - amount - fee, withdrawnToday + amount)
            : new AccountState(balance, withdrawnToday);
        var expectedStock = stock - notes;

        var (result, dispenser) = Run(balance, withdrawnToday, stock, jammed, new Operation.Withdraw(amount));

        if (result.Status != status || result.Notes != notes || result.Fee != fee
            || result.Account != expectedAccount || dispenser.Stock != expectedStock)
        {
            failures.Add(
                $"solde={balance} retiré={withdrawnToday} stock {F(stock)} bloquée={jammed?.ToString() ?? "aucune"} retrait {amount} : " +
                $"attendu {status} {F(notes)} frais={fee} {F(expectedAccount)} stock après {F(expectedStock)} ; " +
                $"obtenu {result.Status} {F(result.Notes)} frais={result.Fee} {F(result.Account)} stock après {F(dispenser.Stock)}");
        }
    }

    private static string F(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";

    private static string F(AccountState a) => $"solde {a.Balance} retiré {a.WithdrawnToday}";

    private static void AssertNoFailures(List<string> failures)
    {
        Assert.True(failures.Count == 0,
            $"{failures.Count} écart(s), dont :\n" + string.Join("\n", failures.Take(MaxReported)));
    }

    [Fact]
    public void Withdraw_AllStocksJamsAndAmounts_GenerousAccount()
    {
        var failures = new List<string>();
        foreach (var stock in Domain.Stocks)
        foreach (var jammed in Domain.Jammed)
        foreach (var amount in Domain.Amounts)
        {
            Check(failures, 250, 0, stock, jammed, amount);
        }

        AssertNoFailures(failures);
    }

    [Fact]
    public void Withdraw_AllAccountsAndAmounts_SeveralStocks()
    {
        NoteCounts[] stocks = [FullStock, N(), N(1, 3, 0), N(0, 5, 0), N(2, 2, 5), N(1, 1, 1)];
        var failures = new List<string>();
        foreach (var stock in stocks)
        foreach (var jammed in Domain.Jammed)
        foreach (var balance in Domain.Balances)
        foreach (var withdrawnToday in Domain.WithdrawnToday)
        foreach (var amount in Domain.Amounts)
        {
            Check(failures, balance, withdrawnToday, stock, jammed, amount);
        }

        AssertNoFailures(failures);
    }

    [Fact]
    public void Deposit_AllStocksJamsAndDeposits()
    {
        (int Balance, int WithdrawnToday)[] accounts = [(0, 0), (125, 70), (250, 200)];
        var failures = new List<string>();
        foreach (var (balance, withdrawnToday) in accounts)
        foreach (var stock in Domain.Stocks)
        foreach (var jammed in Domain.Jammed)
        foreach (var deposit in Domain.Deposits)
        {
            var status = SpecOracle.Deposit(stock, jammed, deposit);
            var ok = status == AtmStatus.Ok;
            var expectedAccount = ok
                ? new AccountState(balance + deposit.Value, withdrawnToday)
                : new AccountState(balance, withdrawnToday);
            var expectedStock = ok ? stock + deposit : stock;

            var (result, dispenser) = Run(balance, withdrawnToday, stock, jammed, new Operation.Deposit(deposit));

            if (result.Status != status || result.Notes != NoteCounts.None || result.Fee != 0
                || result.Account != expectedAccount || dispenser.Stock != expectedStock)
            {
                failures.Add(
                    $"solde={balance} retiré={withdrawnToday} stock {F(stock)} bloquée={jammed?.ToString() ?? "aucune"} dépôt {F(deposit)} : " +
                    $"attendu {status} {F(expectedAccount)} stock après {F(expectedStock)} ; " +
                    $"obtenu {result.Status} {F(result.Notes)} frais={result.Fee} {F(result.Account)} stock après {F(dispenser.Stock)}");
            }
        }

        AssertNoFailures(failures);
    }
}
