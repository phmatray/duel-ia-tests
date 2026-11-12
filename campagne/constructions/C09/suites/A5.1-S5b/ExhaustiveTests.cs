namespace Atm.Tests;

/// <summary>
/// Comparaison avec un oracle écrit d'après la spec, sur tout le domaine (ou de larges tranches).
/// Chaque test liste les premiers écarts trouvés.
/// </summary>
public class ExhaustiveTests
{
    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<int> Balances => Enumerable.Range(0, 51).Select(i => i * 5);

    private static IEnumerable<int> WithdrawnValues => Enumerable.Range(0, 21).Select(i => i * 10);

    private static IEnumerable<int> Amounts => new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));

    private static IEnumerable<NoteCounts> Stocks =>
        from f in Enumerable.Range(0, 6)
        from t in Enumerable.Range(0, 6)
        from n in Enumerable.Range(0, 6)
        select new NoteCounts(f, t, n);

    private static IEnumerable<NoteCounts> Deposits =>
        from f in Enumerable.Range(0, 2)
        from t in Enumerable.Range(0, 2)
        from n in Enumerable.Range(0, 2)
        select new NoteCounts(f, t, n);

    // ---------- Oracle ----------

    private static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    private static NoteCounts? Fewest(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var n = 0; n <= available.Tens; n++)
        {
            var c = new NoteCounts(f, t, n);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total))
            {
                best = c;
            }
        }

        return best;
    }

    private static (AtmResult Result, NoteCounts StockAfter) ExpectedWithdraw(AccountState a, NoteCounts stock, Denomination? jammed, int amount)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus s) => (new AtmResult(s, NoteCounts.None, 0, a), stock);

        if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
        if (a.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > a.Balance) return Refuse(AtmStatus.InsufficientFunds);
        if (Fewest(amount, Available(stock, jammed)) is not { } combo) return Refuse(AtmStatus.CannotDispense);

        return (new AtmResult(AtmStatus.Ok, combo, fee, new AccountState(a.Balance - amount - fee, a.WithdrawnToday + amount)), stock - combo);
    }

    private static (AtmResult Result, NoteCounts StockAfter) ExpectedDeposit(AccountState a, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus s) => (new AtmResult(s, NoteCounts.None, 0, a), stock);

        if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, a with { Balance = a.Balance + notes.Value }), after);
    }

    // ---------- Vérification ----------

    private static void Check(List<string> failures, string label, (AtmResult Result, NoteCounts StockAfter) expected, AtmResult actual, NoteCounts actualStock)
    {
        if (failures.Count >= 25) return;
        if (expected.Result != actual || expected.StockAfter != actualStock)
        {
            failures.Add($"{label}\n    attendu : {expected.Result}, stock {expected.StockAfter}\n    obtenu  : {actual}, stock {actualStock}");
        }
    }

    private static void AssertNoFailures(List<string> failures) =>
        Assert.True(failures.Count == 0, $"{failures.Count}{(failures.Count >= 25 ? "+" : "")} écart(s) :\n" + string.Join("\n", failures));

    private static void CheckWithdraw(List<string> failures, AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var expected = ExpectedWithdraw(account, stock, jammed, amount);
        var dispenser = new MoneyDispenser(stock, jammed);
        AtmResult actual;
        try
        {
            actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
        }
        catch (Exception e)
        {
            if (failures.Count < 25) failures.Add($"Retrait {amount}, {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"} : exception {e.GetType().Name} {e.Message}");
            return;
        }

        Check(failures, $"Retrait {amount}, {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}", expected, actual, dispenser.Stock);
    }

    [Fact]
    public void Retrait_controles_du_compte_sur_tout_le_domaine_des_comptes()
    {
        var failures = new List<string>();
        foreach (var stock in new[] { new NoteCounts(5, 5, 5), new NoteCounts(0, 0, 0) })
        foreach (var balance in Balances)
        foreach (var withdrawn in WithdrawnValues)
        foreach (var amount in Amounts)
        {
            CheckWithdraw(failures, new AccountState(balance, withdrawn), stock, null, amount);
        }

        AssertNoFailures(failures);
    }

    [Fact]
    public void Retrait_billets_sur_tout_le_domaine_du_module()
    {
        var accounts = new AccountState[] { new(250, 0), new(0, 0), new(105, 0), new(110, 100), new(250, 150) };
        var failures = new List<string>();
        foreach (var account in accounts)
        foreach (var stock in Stocks)
        foreach (var jammed in JamOptions)
        foreach (var amount in Amounts)
        {
            CheckWithdraw(failures, account, stock, jammed, amount);
        }

        AssertNoFailures(failures);
    }

    [Fact]
    public void Depot_sur_tout_le_domaine_du_module()
    {
        var accounts = new AccountState[] { new(0, 0), new(125, 70), new(250, 200) };
        var failures = new List<string>();
        foreach (var account in accounts)
        foreach (var stock in Stocks)
        foreach (var jammed in JamOptions)
        foreach (var notes in Deposits)
        {
            var expected = ExpectedDeposit(account, stock, jammed, notes);
            var dispenser = new MoneyDispenser(stock, jammed);
            var label = $"Dépôt {notes}, {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}";
            try
            {
                var actual = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));
                Check(failures, label, expected, actual, dispenser.Stock);
            }
            catch (Exception e)
            {
                if (failures.Count < 25) failures.Add($"{label} : exception {e.GetType().Name} {e.Message}");
            }
        }

        AssertNoFailures(failures);
    }
}
