using static Atm.Tests.N;

namespace Atm.Tests;

/// <summary>
/// Balayage du domaine contre un oracle tiré directement de la spec :
/// tous les stocks, toutes les cassettes bloquées, tous les montants (ou dépôts), pour plusieurs comptes.
/// </summary>
public class DomainSweepTests
{
    public static TheoryData<int, int> Accounts => new()
    {
        { 250, 0 }, { 0, 0 }, { 100, 0 }, { 105, 0 }, { 115, 90 }, { 155, 50 }, { 205, 0 }, { 60, 150 }, { 250, 200 },
    };

    [Theory]
    [MemberData(nameof(Accounts))]
    public void Withdraw_matches_the_spec_over_the_domain(int balance, int withdrawn)
    {
        var account = new AccountState(balance, withdrawn);
        var failures = new List<string>();

        foreach (var stock in AllStocks())
        foreach (var jam in AllJams)
        foreach (var amount in AllAmounts)
        {
            var spy = new SpyDispenser(new MoneyDispenser(stock, jam));
            var r = new CashMachine(spy).Execute(account, new Operation.Withdraw(amount));
            var expectedStatus = Oracle.WithdrawStatus(account, amount, stock, jam);

            string? problem = null;
            if (r.Status != expectedStatus)
            {
                problem = $"statut {r.Status}, attendu {expectedStatus}";
            }
            else if (expectedStatus == AtmStatus.Ok)
            {
                var notes = Oracle.Combination(amount, Oracle.Available(stock, jam))!.Value;
                var fee = Oracle.Fee(amount);
                var expectedAccount = new AccountState(balance - amount - fee, withdrawn + amount);
                if (r.Notes != notes) problem = $"billets {r.Notes}, attendu {notes}";
                else if (r.Fee != fee) problem = $"frais {r.Fee}, attendu {fee}";
                else if (r.Account != expectedAccount) problem = $"compte {r.Account}, attendu {expectedAccount}";
                else if (spy.Stock != stock - notes) problem = $"stock {spy.Stock}, attendu {stock - notes}";
                else if (spy.DispenseCalls.Any(c => !c.Result || c.Notes.Total > 4))
                    problem = "passage refusé ou de plus de 4 billets";
            }
            else
            {
                if (r.Notes != NoteCounts.None || r.Fee != 0 || r.Account != account || spy.Stock != stock)
                    problem = $"refus avec effet : {r}, stock {spy.Stock}";
            }

            if (problem is not null)
                failures.Add($"stock {stock}, bloquée {jam?.ToString() ?? "aucune"}, retrait {amount} : {problem}");
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} cas faux, dont :\n" + string.Join("\n", failures.Take(20)));
    }

    [Theory]
    [MemberData(nameof(Accounts))]
    public void Deposit_matches_the_spec_over_the_domain(int balance, int withdrawn)
    {
        var account = new AccountState(balance, withdrawn);
        var failures = new List<string>();

        foreach (var stock in AllStocks())
        foreach (var jam in AllJams)
        foreach (var notes in AllDeposits())
        {
            var dispenser = new MoneyDispenser(stock, jam);
            var r = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));
            var expectedStatus = Oracle.DepositStatus(notes, stock, jam);

            var expectedAccount = expectedStatus == AtmStatus.Ok
                ? account with { Balance = balance + notes.Value }
                : account;
            var expectedStock = expectedStatus == AtmStatus.Ok ? stock + notes : stock;

            string? problem = null;
            if (r.Status != expectedStatus) problem = $"statut {r.Status}, attendu {expectedStatus}";
            else if (r.Notes != NoteCounts.None) problem = $"billets {r.Notes}, attendu aucun";
            else if (r.Fee != 0) problem = $"frais {r.Fee}, attendu 0";
            else if (r.Account != expectedAccount) problem = $"compte {r.Account}, attendu {expectedAccount}";
            else if (dispenser.Stock != expectedStock) problem = $"stock {dispenser.Stock}, attendu {expectedStock}";

            if (problem is not null)
                failures.Add($"stock {stock}, bloquée {jam?.ToString() ?? "aucune"}, dépôt {notes} : {problem}");
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} cas faux, dont :\n" + string.Join("\n", failures.Take(20)));
    }
}
