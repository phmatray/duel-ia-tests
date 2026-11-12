namespace Atm.Tests;

/// <summary>
/// Balayage du domaine contre un oracle écrit d'après la spec.
/// Chaque test rassemble tous les cas en échec pour donner une vue d'ensemble.
/// </summary>
public class DomainSweepTests
{
    private const int MaxReported = 25;

    private static (AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account) ExpectedWithdraw(
        AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        if (amount <= 0 || amount % 10 != 0) return (AtmStatus.InvalidAmount, NoteCounts.None, 0, account);
        if (account.WithdrawnToday + amount > 200) return (AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, account);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return (AtmStatus.InsufficientFunds, NoteCounts.None, 0, account);
        if (Domain.FewestNotes(amount, stock, jammed) is not { } combination)
            return (AtmStatus.CannotDispense, NoteCounts.None, 0, account);
        return (AtmStatus.Ok, combination, fee, new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    private static string? CheckWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
        var actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
        var expected = ExpectedWithdraw(account, stock, jammed, amount);
        var expectedStock = expected.Status == AtmStatus.Ok ? stock - expected.Notes : stock;

        var problems = new List<string>();
        if (actual.Status != expected.Status) problems.Add($"statut {actual.Status} au lieu de {expected.Status}");
        if (actual.Notes != expected.Notes) problems.Add($"billets {Domain.Show(actual.Notes)} au lieu de {Domain.Show(expected.Notes)}");
        if (actual.Fee != expected.Fee) problems.Add($"frais {actual.Fee} au lieu de {expected.Fee}");
        if (actual.Account != expected.Account) problems.Add($"compte {actual.Account} au lieu de {expected.Account}");
        if (dispenser.Stock != expectedStock) problems.Add($"stock {Domain.Show(dispenser.Stock)} au lieu de {Domain.Show(expectedStock)}");
        if (dispenser.DispenseCalls.Any(p => p.Total > 4)) problems.Add("passage de plus de 4 billets");
        if (dispenser.LoadCalls.Count > 0) problems.Add("Load appelé pendant un retrait");

        return problems.Count == 0
            ? null
            : $"solde {account.Balance}, retiré {account.WithdrawnToday}, stock {Domain.Show(stock)}, bloquée {Domain.Show(jammed)}, retrait {amount} : {string.Join(" ; ", problems)}";
    }

    private static void Report(List<string> failures, int total)
    {
        if (failures.Count == 0) return;
        Assert.Fail($"{failures.Count} cas sur {total} en écart avec la spec. Premiers cas :\n"
            + string.Join("\n", failures.Take(MaxReported)));
    }

    /// <summary>
    /// Tous les stocks, cassettes bloquées et montants du domaine, avec un compte qui ne bloque jamais
    /// (solde 250, rien retiré) : exerce la combinaison, la cassette bloquée et la sortie en passages.
    /// </summary>
    [Fact]
    public void Retrait_sur_tous_les_stocks_cassettes_et_montants()
    {
        var failures = new List<string>();
        var total = 0;
        foreach (var stock in Domain.Stocks())
        foreach (var jammed in Domain.JammedValues)
        foreach (var amount in Domain.Amounts)
        {
            total++;
            if (CheckWithdraw(new AccountState(250, 0), stock, jammed, amount) is { } failure) failures.Add(failure);
        }
        Report(failures, total);
    }

    /// <summary>Tous les comptes et montants du domaine, sur un stock plein : exerce l'ordre des contrôles, le plafond, le solde et les frais.</summary>
    [Fact]
    public void Retrait_sur_tous_les_comptes_et_montants()
    {
        var failures = new List<string>();
        var total = 0;
        foreach (var jammed in Domain.JammedValues)
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in Domain.Amounts)
        {
            total++;
            if (CheckWithdraw(new AccountState(balance, withdrawn), new NoteCounts(5, 5, 5), jammed, amount) is { } failure)
                failures.Add(failure);
        }
        Report(failures, total);
    }

    /// <summary>Tous les stocks, cassettes bloquées et dépôts du domaine.</summary>
    [Fact]
    public void Depot_sur_tout_le_domaine()
    {
        var failures = new List<string>();
        var total = 0;
        var account = new AccountState(125, 70);
        foreach (var stock in Domain.Stocks())
        foreach (var jammed in Domain.JammedValues)
        foreach (var deposit in Domain.Deposits())
        {
            total++;
            var after = stock + deposit;
            var expected =
                deposit.Total == 0 ? AtmStatus.InvalidAmount
                : jammed is { } j && deposit.Of(j) > 0 ? AtmStatus.CassetteJammed
                : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
                : AtmStatus.Ok;
            var expectedAccount = expected == AtmStatus.Ok ? account with { Balance = account.Balance + deposit.Value } : account;
            var expectedStock = expected == AtmStatus.Ok ? after : stock;

            var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Deposit(deposit));

            if (actual != new AtmResult(expected, NoteCounts.None, 0, expectedAccount)
                || dispenser.Stock != expectedStock
                || dispenser.DispenseCalls.Count > 0)
            {
                failures.Add($"stock {Domain.Show(stock)}, bloquée {Domain.Show(jammed)}, dépôt {Domain.Show(deposit)} : "
                    + $"obtenu {actual} / stock {Domain.Show(dispenser.Stock)}, attendu {expected} / stock {Domain.Show(expectedStock)}");
            }
        }
        Report(failures, total);
    }
}
