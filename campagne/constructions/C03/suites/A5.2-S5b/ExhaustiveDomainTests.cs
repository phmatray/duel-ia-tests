using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Parcours de tout le domaine de la spec, comparé au modèle de référence (<see cref="Reference"/>).
/// Vérifie à chaque fois le résultat complet, le stock du module après l'opération,
/// et qu'aucun billet ne bouge sur un refus.
/// </summary>
public class ExhaustiveDomainTests
{
    private const int MaxReported = 20;

    private static void Check(List<string> failures, string label, AtmResult expected, AtmResult actual,
        NoteCounts expectedStock, SpyDispenser dispenser)
    {
        if (failures.Count >= MaxReported) return;
        if (expected != actual)
            failures.Add($"{label} : attendu {expected}, obtenu {actual}");
        else if (expectedStock != dispenser.Stock)
            failures.Add($"{label} : stock attendu {expectedStock}, obtenu {dispenser.Stock}");
        else if (expected.Status != AtmStatus.Ok && (dispenser.Dispensed.Count > 0 || dispenser.Loaded.Count > 0))
            failures.Add($"{label} : refus mais le module a été sollicité");
        else if (dispenser.Dispensed.Any(d => !d.Result) || dispenser.Loaded.Any(l => !l.Result))
            failures.Add($"{label} : le module a refusé un appel");
    }

    private static void AssertNoFailures(List<string> failures) =>
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

    /// <summary>Tout le domaine des billets (stock × cassette bloquée × montant), compte confortable.</summary>
    [Fact]
    public void Withdraw_over_every_stock_jam_and_amount()
    {
        var failures = new List<string>();
        var account = Acc(250, 0);
        foreach (var stock in AllStocks())
        foreach (var jam in AllJams)
        foreach (var amount in AllAmounts)
        {
            var (machine, dispenser) = Build(stock, jam);
            var expected = Reference.Withdraw(account, amount, stock, jam);
            var actual = machine.Execute(account, Withdraw(amount));
            Check(failures, $"stock {stock}, bloquée {jam?.ToString() ?? "aucune"}, retrait {amount}",
                expected, actual, stock - expected.Notes, dispenser);
        }

        AssertNoFailures(failures);
    }

    /// <summary>Tout le domaine du compte (solde × retiré × montant), quelques stocks typiques.</summary>
    [Fact]
    public void Withdraw_over_every_account_and_amount()
    {
        NoteCounts[] stocks = [N(5, 5, 5), N(0, 0, 0), N(1, 3, 0), N(0, 5, 5)];
        var failures = new List<string>();
        foreach (var stock in stocks)
        foreach (var jam in AllJams)
        foreach (var balance in AllBalances)
        foreach (var withdrawn in AllWithdrawnToday)
        foreach (var amount in AllAmounts)
        {
            var account = Acc(balance, withdrawn);
            var (machine, dispenser) = Build(stock, jam);
            var expected = Reference.Withdraw(account, amount, stock, jam);
            var actual = machine.Execute(account, Withdraw(amount));
            Check(failures, $"compte {account}, stock {stock}, bloquée {jam?.ToString() ?? "aucune"}, retrait {amount}",
                expected, actual, stock - expected.Notes, dispenser);
        }

        AssertNoFailures(failures);
    }

    [Fact]
    public void Deposit_over_every_stock_jam_and_deposit()
    {
        AccountState[] accounts = [Acc(0, 0), Acc(125, 70), Acc(250, 200)];
        var failures = new List<string>();
        foreach (var account in accounts)
        foreach (var stock in AllStocks())
        foreach (var jam in AllJams)
        foreach (var notes in AllDeposits())
        {
            var (machine, dispenser) = Build(stock, jam);
            var expected = Reference.Deposit(account, notes, stock, jam);
            var actual = machine.Execute(account, Deposit(notes));
            var expectedStock = expected.Status == AtmStatus.Ok ? stock + notes : stock;
            Check(failures, $"compte {account}, stock {stock}, bloquée {jam?.ToString() ?? "aucune"}, dépôt {notes}",
                expected, actual, expectedStock, dispenser);
        }

        AssertNoFailures(failures);
    }

    /// <summary>La spec affirme l'unicité de la combinaison minimale : on vérifie que le modèle de référence est bien fondé.</summary>
    [Fact]
    public void Fewest_notes_combination_is_unique_on_the_domain()
    {
        foreach (var available in AllStocks())
        foreach (var amount in AllAmounts.Where(a => a > 0 && a % 10 == 0))
        {
            var exact = new List<NoteCounts>();
            for (var f = 0; f <= available.Fifties; f++)
            for (var t = 0; t <= available.Twenties; t++)
            for (var n = 0; n <= available.Tens; n++)
                if (N(f, t, n).Value == amount) exact.Add(N(f, t, n));
            if (exact.Count == 0) continue;
            var min = exact.Min(c => c.Total);
            Assert.Single(exact, c => c.Total == min);
        }
    }
}
