using static Atm.Tests.N;

namespace Atm.Tests;

/// <summary>
/// Balaye le domaine de la spec et compare la machine à un oracle écrit directement d'après la spec.
/// Les tests ciblés des autres classes disent quelle règle casse ; ceux-ci attrapent ce qu'ils auraient raté.
/// </summary>
public class ExhaustiveDomainTests
{
    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
            yield return Notes(f, t, n);
    }

    private static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    private static (AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account) WithdrawOracle(
        AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var refused = (AtmStatus s) => (s, NoteCounts.None, 0, account);
        var fee = amount > 100 ? 5 : 0;

        if (amount <= 0 || amount % 10 != 0) return refused(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return refused(AtmStatus.DailyLimitExceeded);
        if (amount + fee > account.Balance) return refused(AtmStatus.InsufficientFunds);

        var avail = Available(stock, jammed);
        NoteCounts? best = null;
        for (var f = 0; f <= avail.Fifties; f++)
        for (var t = 0; t <= avail.Twenties; t++)
        for (var n = 0; n <= avail.Tens; n++)
        {
            var c = Notes(f, t, n);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total)) best = c;
        }

        if (best is null) return refused(AtmStatus.CannotDispense);

        return (AtmStatus.Ok, best.Value, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    private static (AtmStatus Status, NoteCounts Stock, AccountState Account) DepositOracle(
        AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts deposit)
    {
        if (deposit.Total == 0) return (AtmStatus.InvalidAmount, stock, account);
        if (jammed is { } j && deposit.Of(j) > 0) return (AtmStatus.CassetteJammed, stock, account);
        var after = stock + deposit;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (AtmStatus.CassetteFull, stock, account);
        return (AtmStatus.Ok, after, account with { Balance = account.Balance + deposit.Value });
    }

    [Fact]
    public void Withdraw_MatchesSpecOracle_OverDomain()
    {
        int[] balances = [0, 5, 50, 55, 95, 100, 105, 110, 115, 155, 200, 205, 250];
        int[] withdrawn = [0, 10, 90, 100, 150, 190, 200];
        var failures = new List<string>();

        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var amount in Amounts)
        foreach (var balance in balances)
        foreach (var wt in withdrawn)
        {
            var account = Account(balance, wt);
            var expected = WithdrawOracle(account, stock, jammed, amount);
            var spy = SpyDispenser.Real(stock, jammed);
            var actual = new CashMachine(spy).Execute(account, Withdraw(amount));

            var problems = new List<string>();
            if (actual.Status != expected.Status) problems.Add($"statut {actual.Status} au lieu de {expected.Status}");
            if (actual.Notes != expected.Notes) problems.Add($"billets {actual.Notes} au lieu de {expected.Notes}");
            if (actual.Fee != expected.Fee) problems.Add($"frais {actual.Fee} au lieu de {expected.Fee}");
            if (actual.Account != expected.Account) problems.Add($"compte {actual.Account} au lieu de {expected.Account}");
            if (spy.Stock != stock - expected.Notes) problems.Add($"stock final {spy.Stock} au lieu de {stock - expected.Notes}");
            if (spy.Dispenses.Any(d => !d.Result)) problems.Add("un passage refusé par le module");
            if (spy.Dispenses.Any(d => d.Notes.Total > 4)) problems.Add("un passage de plus de 4 billets");
            if (spy.Loads.Count > 0) problems.Add("Load appelé pendant un retrait");

            if (problems.Count > 0)
            {
                failures.Add($"solde {balance}, retiré {wt}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, retrait {amount} : "
                             + string.Join("; ", problems));
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} cas en désaccord avec la spec, dont :\n" + string.Join("\n", failures.Take(25)));
    }

    [Fact]
    public void Deposit_MatchesSpecOracle_OverDomain()
    {
        int[] balances = [0, 5, 100, 250];
        int[] withdrawn = [0, 30, 200];
        var failures = new List<string>();

        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var deposit in from f in new[] { 0, 1 } from t in new[] { 0, 1 } from n in new[] { 0, 1 } select Notes(f, t, n))
        foreach (var balance in balances)
        foreach (var wt in withdrawn)
        {
            var account = Account(balance, wt);
            var expected = DepositOracle(account, stock, jammed, deposit);
            var spy = SpyDispenser.Real(stock, jammed);
            var actual = new CashMachine(spy).Execute(account, new Operation.Deposit(deposit));

            var problems = new List<string>();
            if (actual.Status != expected.Status) problems.Add($"statut {actual.Status} au lieu de {expected.Status}");
            if (actual.Notes != NoteCounts.None) problems.Add($"billets {actual.Notes} au lieu de rien");
            if (actual.Fee != 0) problems.Add($"frais {actual.Fee} au lieu de 0");
            if (actual.Account != expected.Account) problems.Add($"compte {actual.Account} au lieu de {expected.Account}");
            if (spy.Stock != expected.Stock) problems.Add($"stock final {spy.Stock} au lieu de {expected.Stock}");
            if (spy.Loads.Any(l => !l.Result)) problems.Add("un dépôt refusé par le module");
            if (spy.Dispenses.Count > 0) problems.Add("Dispense appelé pendant un dépôt");

            if (problems.Count > 0)
            {
                failures.Add($"solde {balance}, retiré {wt}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, dépôt {deposit} : "
                             + string.Join("; ", problems));
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} cas en désaccord avec la spec, dont :\n" + string.Join("\n", failures.Take(25)));
    }
}
