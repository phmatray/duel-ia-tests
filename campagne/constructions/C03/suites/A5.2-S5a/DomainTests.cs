using System.Text;
using static Atm.Tests.Fixture;

namespace Atm.Tests;

/// <summary>
/// Parcours exhaustifs du domaine, comparés à une lecture directe de la spec.
/// Chaque test rassemble les écarts et en affiche les premiers.
/// </summary>
public class DomainTests
{
    private const int MaxReported = 15;

    /// <summary>Lecture directe de la spec pour un retrait.</summary>
    private static (AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account) ExpectedWithdraw(
        AccountState account, int amount, NoteCounts stock, Denomination? jammed)
    {
        if (amount <= 0 || amount % 10 != 0) return (AtmStatus.InvalidAmount, NoteCounts.None, 0, account);
        if (account.WithdrawnToday + amount > 200) return (AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, account);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return (AtmStatus.InsufficientFunds, NoteCounts.None, 0, account);

        var available = new NoteCounts(
            jammed == Denomination.Fifty ? 0 : stock.Fifties,
            jammed == Denomination.Twenty ? 0 : stock.Twenties,
            jammed == Denomination.Ten ? 0 : stock.Tens);

        // Toutes les combinaisons exactes, puis celle au moins de billets (qui doit être unique).
        var exact = (
            from f in Enumerable.Range(0, available.Fifties + 1)
            from t in Enumerable.Range(0, available.Twenties + 1)
            from n in Enumerable.Range(0, available.Tens + 1)
            let c = new NoteCounts(f, t, n)
            where c.Value == amount
            select c).ToList();
        if (exact.Count == 0) return (AtmStatus.CannotDispense, NoteCounts.None, 0, account);

        var fewest = exact.Min(c => c.Total);
        var best = exact.Where(c => c.Total == fewest).ToList();
        Assert.Single(best); // la spec l'affirme ; on le vérifie au passage

        return (AtmStatus.Ok, best[0], fee, new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    private static void Check(
        List<string> failures, string label, AtmResult result, MoneyDispenser dispenser, NoteCounts stockBefore,
        (AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account) expected)
    {
        var expectedStock = expected.Status == AtmStatus.Ok ? stockBefore - expected.Notes : stockBefore;
        if (result.Status != expected.Status || result.Notes != expected.Notes || result.Fee != expected.Fee
            || result.Account != expected.Account || dispenser.Stock != expectedStock)
        {
            failures.Add($"{label} → obtenu {result.Status} {result.Notes} frais {result.Fee} {result.Account} stock {dispenser.Stock}" +
                         $" ; attendu {expected.Status} {expected.Notes} frais {expected.Fee} {expected.Account} stock {expectedStock}");
        }
    }

    private static void AssertNoFailure(List<string> failures, int cases)
    {
        if (failures.Count == 0) return;
        var message = new StringBuilder($"{failures.Count} écart(s) sur {cases} cas. Premiers :\n");
        foreach (var f in failures.Take(MaxReported)) message.AppendLine(f);
        Assert.Fail(message.ToString());
    }

    /// <summary>
    /// Tous les stocks, toutes les cassettes bloquées, tous les montants, avec un compte qui ne bloque pas
    /// les contrôles 2 et 3 : on vérifie la combinaison, la sortie des billets et l'absence de panne.
    /// </summary>
    [Fact]
    public void Retrait_sur_tous_les_stocks_cassettes_bloquees_et_montants()
    {
        var failures = new List<string>();
        var cases = 0;
        var account = Account(250, 0);
        foreach (var stock in Stocks)
        foreach (var jammed in JammedValues)
        foreach (var amount in Amounts)
        {
            cases++;
            var (result, dispenser) = Withdraw(account, amount, stock, jammed);
            Check(failures, $"retrait {amount}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}",
                result, dispenser, stock, ExpectedWithdraw(account, amount, stock, jammed));
        }

        AssertNoFailure(failures, cases);
    }

    /// <summary>
    /// Tous les soldes, tous les retraits du jour, tous les montants, avec un module plein :
    /// on vérifie l'ordre des contrôles 1 à 3, les frais et la mise à jour du compte.
    /// </summary>
    [Fact]
    public void Retrait_sur_tous_les_comptes_et_montants()
    {
        var failures = new List<string>();
        var cases = 0;
        foreach (var balance in Balances)
        foreach (var withdrawn in WithdrawnValues)
        foreach (var amount in Amounts)
        {
            cases++;
            var account = Account(balance, withdrawn);
            var (result, dispenser) = Withdraw(account, amount, Full);
            Check(failures, $"retrait {amount}, compte {account}", result, dispenser, Full,
                ExpectedWithdraw(account, amount, Full, null));
        }

        AssertNoFailure(failures, cases);
    }

    /// <summary>Le contrôle 4 combiné à un solde limite : solde 0 à 250, stocks variés.</summary>
    [Fact]
    public void Retrait_croise_solde_et_stock()
    {
        var failures = new List<string>();
        var cases = 0;
        foreach (var balance in Balances.Where(b => b % 25 == 0))
        foreach (var stock in Stocks.Where(s => s.Total % 2 == 0))
        foreach (var jammed in JammedValues)
        foreach (var amount in Amounts.Where(a => a % 30 == 0 || a <= 10))
        {
            cases++;
            var account = Account(balance, 100);
            var (result, dispenser) = Withdraw(account, amount, stock, jammed);
            Check(failures, $"retrait {amount}, compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}",
                result, dispenser, stock, ExpectedWithdraw(account, amount, stock, jammed));
        }

        AssertNoFailure(failures, cases);
    }

    /// <summary>Tous les stocks, toutes les cassettes bloquées, tous les dépôts du domaine.</summary>
    [Fact]
    public void Depot_sur_tous_les_stocks_cassettes_bloquees_et_depots()
    {
        var failures = new List<string>();
        var cases = 0;
        var account = Account(125, 70);
        foreach (var stock in Stocks)
        foreach (var jammed in JammedValues)
        foreach (var notes in DepositValues)
        {
            cases++;
            var (result, dispenser) = Deposit(account, notes, stock, jammed);

            var after = stock + notes;
            var expected =
                notes.Total == 0 ? (AtmStatus.InvalidAmount, NoteCounts.None, 0, account)
                : jammed is { } j && notes.Of(j) > 0 ? (AtmStatus.CassetteJammed, NoteCounts.None, 0, account)
                : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? (AtmStatus.CassetteFull, NoteCounts.None, 0, account)
                : (AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value });
            var expectedStock = expected.Item1 == AtmStatus.Ok ? after : stock;

            if (result.Status != expected.Item1 || result.Notes != expected.Item2 || result.Fee != expected.Item3
                || result.Account != expected.Item4 || dispenser.Stock != expectedStock)
            {
                failures.Add($"dépôt {notes}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"} → obtenu {result}" +
                             $" stock {dispenser.Stock} ; attendu {expected} stock {expectedStock}");
            }
        }

        AssertNoFailure(failures, cases);
    }

    /// <summary>
    /// La panne ne fait partie d'aucun résultat attendu : avec le vrai module et des valeurs du domaine,
    /// la machine ne doit jamais rendre DispenserFault.
    /// </summary>
    [Fact]
    public void Jamais_de_panne_dans_le_domaine()
    {
        var failures = new List<string>();
        var cases = 0;
        foreach (var stock in Stocks)
        foreach (var jammed in JammedValues)
        {
            foreach (var amount in Amounts)
            {
                cases++;
                var (result, _) = Withdraw(Account(250, 0), amount, stock, jammed);
                if (result.Status == AtmStatus.DispenserFault)
                    failures.Add($"retrait {amount}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"} → {result}");
            }

            foreach (var notes in DepositValues)
            {
                cases++;
                var (result, _) = Deposit(Account(250, 0), notes, stock, jammed);
                if (result.Status == AtmStatus.DispenserFault)
                    failures.Add($"dépôt {notes}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"} → {result}");
            }
        }

        AssertNoFailure(failures, cases);
    }
}
