using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Balayage du domaine de la spec : chaque cas est comparé au comportement décrit
/// (statut, billets, frais, compte, mouvements de billets, taille des passages).
/// </summary>
public class DomainSweepTests
{
    private const int MaxReported = 15;

    private static readonly int[] AllBalances = [.. Enumerable.Range(0, 51).Select(i => i * 5)];
    private static readonly int[] AllWithdrawn = [.. Enumerable.Range(0, 21).Select(i => i * 10)];
    private static readonly int[] SomeBalances = [0, 5, 10, 45, 50, 55, 100, 105, 110, 115, 200, 205, 250];
    private static readonly int[] SomeWithdrawn = [0, 10, 100, 150, 190, 200];

    private static string? CheckWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var spy = new SpyDispenser(stock, jammed);
        var result = new CashMachine(spy).Execute(account, Withdraw(amount));
        var expected = ExpectedWithdrawStatus(account, stock, jammed, amount);
        var ctx = $"compte={account} stock={stock} bloquée={jammed?.ToString() ?? "aucune"} retrait {amount}";

        if (result.Status != expected) return $"{ctx} : statut {result.Status}, attendu {expected}";
        if (spy.LoadCalls.Count != 0) return $"{ctx} : Load appelé pendant un retrait";

        if (expected != AtmStatus.Ok)
        {
            if (result != new AtmResult(expected, NoteCounts.None, 0, account)) return $"{ctx} : refus non neutre {result}";
            if (spy.DispenseCalls.Count != 0 || spy.Stock != stock) return $"{ctx} : billets déplacés sur un refus";
            return null;
        }

        var notes = FewestNotes(amount, Available(stock, jammed))!.Value;
        var fee = Fee(amount);
        var after = Acc(account.Balance - amount - fee, account.WithdrawnToday + amount);
        if (result != new AtmResult(AtmStatus.Ok, notes, fee, after)) return $"{ctx} : {result}, attendu Ok {notes} frais {fee} {after}";
        if (spy.DispenseCalls.Any(p => p.Total is < 1 or > 4)) return $"{ctx} : passage hors 1..4 billets [{string.Join("; ", spy.DispenseCalls)}]";
        if (spy.Dispensed != notes || spy.Stock != stock - notes) return $"{ctx} : billets sortis {spy.Dispensed}, attendu {notes}";
        return null;
    }

    private static string? CheckDeposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        var spy = new SpyDispenser(stock, jammed);
        var result = new CashMachine(spy).Execute(account, Deposit(notes));
        var expected = ExpectedDepositStatus(stock, jammed, notes);
        var ctx = $"compte={account} stock={stock} bloquée={jammed?.ToString() ?? "aucune"} dépôt {notes}";

        if (result.Status != expected) return $"{ctx} : statut {result.Status}, attendu {expected}";
        if (spy.DispenseCalls.Count != 0) return $"{ctx} : Dispense appelé pendant un dépôt";

        if (expected != AtmStatus.Ok)
        {
            if (result != new AtmResult(expected, NoteCounts.None, 0, account)) return $"{ctx} : refus non neutre {result}";
            if (spy.LoadCalls.Count != 0 || spy.Stock != stock) return $"{ctx} : billets déplacés sur un refus";
            return null;
        }

        var after = account with { Balance = account.Balance + notes.Value };
        if (result != new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, after)) return $"{ctx} : {result}, attendu Ok {after}";
        if (spy.Stock != stock + notes) return $"{ctx} : stock {spy.Stock}, attendu {stock + notes}";
        return null;
    }

    private static void AssertNoFailure(IEnumerable<string?> checks)
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var failure in checks)
        {
            if (failure is null) continue;
            count++;
            if (failures.Count < MaxReported) failures.Add(failure);
        }

        Assert.True(count == 0, $"{count} cas en échec, dont :\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Withdraw_every_account_and_amount_with_full_unjammed_stock()
    {
        AssertNoFailure(
            from b in AllBalances
            from w in AllWithdrawn
            from a in DomainAmounts
            select CheckWithdraw(Acc(b, w), N(5, 5, 5), null, a));
    }

    [Fact]
    public void Withdraw_every_stock_jam_and_amount_with_ample_account()
    {
        AssertNoFailure(
            from s in AllStocks()
            from j in AllJams
            from a in DomainAmounts
            select CheckWithdraw(Acc(250, 0), s, j, a));
    }

    [Fact]
    public void Withdraw_every_stock_jam_and_amount_with_boundary_accounts()
    {
        AssertNoFailure(
            from b in SomeBalances
            from w in SomeWithdrawn
            from s in AllStocks()
            from j in AllJams
            from a in DomainAmounts
            select CheckWithdraw(Acc(b, w), s, j, a));
    }

    [Fact]
    public void Deposit_every_stock_jam_and_deposit()
    {
        AssertNoFailure(
            from b in SomeBalances
            from w in SomeWithdrawn
            from s in AllStocks()
            from j in AllJams
            from d in AllDeposits()
            select CheckDeposit(Acc(b, w), s, j, d));
    }

    [Fact]
    public void Deposit_every_account()
    {
        AssertNoFailure(
            from b in AllBalances
            from w in AllWithdrawn
            from d in AllDeposits()
            select CheckDeposit(Acc(b, w), N(2, 2, 2), Denomination.Ten, d));
    }

    [Fact]
    public void Oracle_combination_is_always_unique_on_the_domain()
    {
        // Garde-fou du test : la spec affirme l'unicité, FewestNotes lève sinon.
        foreach (var s in AllStocks())
        foreach (var a in DomainAmounts.Where(a => a > 0 && a % 10 == 0))
            _ = FewestNotes(a, s);
    }
}
