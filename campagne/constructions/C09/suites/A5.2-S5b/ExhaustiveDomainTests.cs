using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Parcourt tout le domaine de la spec (solde, déjà retiré, stock, cassette bloquée, opération)
/// et compare chaque résultat à un oracle écrit d'après la spec.
/// </summary>
public class ExhaustiveDomainTests
{
    private const int MaxReported = 15;

    private static IEnumerable<AccountState> AccountDomain()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
            yield return Acc(balance, withdrawn);
    }

    public static TheoryData<string, int> WithdrawCases()
    {
        var data = new TheoryData<string, int>();
        foreach (var jammed in JammedValues)
        foreach (var amount in AmountDomain)
            data.Add(jammed?.ToString() ?? "aucune", amount);
        return data;
    }

    public static TheoryData<string, int, int, int> DepositCases()
    {
        var data = new TheoryData<string, int, int, int>();
        foreach (var jammed in JammedValues)
        foreach (var notes in DepositDomain())
            data.Add(jammed?.ToString() ?? "aucune", notes.Fifties, notes.Twenties, notes.Tens);
        return data;
    }

    private static Denomination? ParseJammed(string jammed) =>
        jammed == "aucune" ? null : Enum.Parse<Denomination>(jammed);

    [Theory]
    [MemberData(nameof(WithdrawCases))]
    public void Retrait_conforme_sur_tout_le_domaine(string jammedName, int amount)
    {
        var jammed = ParseJammed(jammedName);
        var failures = new List<string>();
        var failureCount = 0;

        foreach (var stock in StockDomain())
        foreach (var account in AccountDomain())
        {
            var (r, d) = Run(account, stock, jammed, Withdraw(amount));
            var problems = CheckWithdraw(account, amount, stock, jammed, r, d);
            if (problems.Count > 0)
            {
                failureCount++;
                if (failures.Count < MaxReported)
                {
                    failures.Add($"compte {account}, stock {stock}, bloquée {jammedName}, retrait {amount} → {r} : "
                                 + string.Join(" ; ", problems));
                }
            }
        }

        Assert.True(failureCount == 0,
            $"{failureCount} cas non conformes, dont :\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DepositCases))]
    public void Depot_conforme_sur_tout_le_domaine(string jammedName, int f, int t, int n)
    {
        var jammed = ParseJammed(jammedName);
        var deposit = N(f, t, n);
        var failures = new List<string>();
        var failureCount = 0;

        foreach (var stock in StockDomain())
        foreach (var account in AccountDomain())
        {
            var (r, d) = Run(account, stock, jammed, Deposit(deposit));
            var problems = CheckDeposit(account, deposit, stock, jammed, r, d);
            if (problems.Count > 0)
            {
                failureCount++;
                if (failures.Count < MaxReported)
                {
                    failures.Add($"compte {account}, stock {stock}, bloquée {jammedName}, dépôt {deposit} → {r} : "
                                 + string.Join(" ; ", problems));
                }
            }
        }

        Assert.True(failureCount == 0,
            $"{failureCount} cas non conformes, dont :\n" + string.Join("\n", failures));
    }

    private static List<string> CheckWithdraw(AccountState account, int amount, NoteCounts stock, Denomination? jammed,
        AtmResult r, SpyDispenser d)
    {
        var problems = new List<string>();
        var expected = Oracle.WithdrawStatus(account, amount, stock, jammed);

        if (r.Status != expected)
        {
            problems.Add($"statut attendu {expected}");
            return problems;
        }

        if (expected != AtmStatus.Ok)
        {
            CheckUnchanged(account, stock, r, d, problems);
            return problems;
        }

        var notes = Oracle.FewestNotes(amount, Oracle.Available(stock, jammed))!.Value;
        var fee = Oracle.Fee(amount);
        if (r.Notes != notes) problems.Add($"billets attendus {notes}");
        if (r.Fee != fee) problems.Add($"frais attendus {fee}");
        var account2 = Acc(account.Balance - amount - fee, account.WithdrawnToday + amount);
        if (r.Account != account2) problems.Add($"compte attendu {account2}");
        if (d.Stock != stock - notes) problems.Add($"stock attendu {stock - notes}, obtenu {d.Stock}");
        if (d.DispenseCalls.Any(c => !c.Ok)) problems.Add("un passage a été refusé par le module");
        if (d.DispenseCalls.Any(c => c.Notes.Total > MoneyDispenser.MaxNotesPerPass))
            problems.Add("passage de plus de 4 billets");
        if (d.Dispensed != r.Notes) problems.Add($"billets réellement sortis {d.Dispensed}");
        if (d.LoadCalls.Count > 0) problems.Add("appel à Load pendant un retrait");
        return problems;
    }

    private static List<string> CheckDeposit(AccountState account, NoteCounts deposit, NoteCounts stock,
        Denomination? jammed, AtmResult r, SpyDispenser d)
    {
        var problems = new List<string>();
        var expected = Oracle.DepositStatus(deposit, stock, jammed);

        if (r.Status != expected)
        {
            problems.Add($"statut attendu {expected}");
            return problems;
        }

        if (expected != AtmStatus.Ok)
        {
            CheckUnchanged(account, stock, r, d, problems);
            return problems;
        }

        if (r.Notes != NoteCounts.None) problems.Add("Notes devrait être vide");
        if (r.Fee != 0) problems.Add("Fee devrait être 0");
        var account2 = account with { Balance = account.Balance + deposit.Value };
        if (r.Account != account2) problems.Add($"compte attendu {account2}");
        if (d.Stock != stock + deposit) problems.Add($"stock attendu {stock + deposit}, obtenu {d.Stock}");
        if (d.DispenseCalls.Any(c => c.Ok)) problems.Add("billets sortis pendant un dépôt");
        return problems;
    }

    private static void CheckUnchanged(AccountState account, NoteCounts stock, AtmResult r, SpyDispenser d,
        List<string> problems)
    {
        if (r.Notes != NoteCounts.None) problems.Add("refus : Notes devrait être vide");
        if (r.Fee != 0) problems.Add("refus : Fee devrait être 0");
        if (r.Account != account) problems.Add("refus : le compte a changé");
        if (d.Stock != stock) problems.Add($"refus : le stock a changé ({d.Stock})");
    }
}
