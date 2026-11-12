using System.Collections.Concurrent;
using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Parcours exhaustif du domaine de la spec, comparé à un oracle écrit d'après la spec
/// (ordre des contrôles, frais, combinaison minimale, passages de 4 billets au plus, refus sans effet).
/// </summary>
public class DomainTests
{
    private const int MaxReported = 15;

    private static IEnumerable<AccountState> AllAccounts()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        {
            yield return Acc(balance, withdrawn);
        }
    }

    private static void Report(ConcurrentQueue<string> failures, long count, string what)
    {
        if (count == 0)
        {
            return;
        }

        Assert.Fail($"{count} situations de {what} (stock, cassette bloquée, opération) en écart avec la spec. Premières :\n" + string.Join("\n", failures.Take(MaxReported)));
    }

    /// <summary>Note un écart ; un seul message par situation (stock, cassette, opération), le premier compte fautif.</summary>
    private static void Fail(ConcurrentQueue<string> failures, ref long count, string message)
    {
        if (Interlocked.Increment(ref count) <= MaxReported)
        {
            failures.Enqueue(message);
        }
    }

    private static void Fail(ConcurrentQueue<string> failures, ref long count, ref bool situationReported, string message)
    {
        if (!situationReported)
        {
            situationReported = true;
            Fail(failures, ref count, message);
        }
    }

    [Fact]
    public void Retrait_sur_tout_le_domaine()
    {
        var accounts = AllAccounts().ToArray();
        var cases =
            from stock in AllStocks()
            from jammed in AllJams
            from amount in AllAmounts
            select (stock, jammed, amount);

        var failures = new ConcurrentQueue<string>();
        long count = 0;

        Parallel.ForEach(cases, c =>
        {
            var (stock, jammed, amount) = c;
            var combination = amount > 0 && amount % 10 == 0 ? Fewest(amount, WithoutJammed(stock, jammed)) : null;
            var fee = amount > 100 ? 5 : 0;

            var reported = false;
            foreach (var account in accounts)
            {
                var expectedStatus =
                    amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
                    : account.WithdrawnToday + amount > 200 ? AtmStatus.DailyLimitExceeded
                    : amount + fee > account.Balance ? AtmStatus.InsufficientFunds
                    : combination is null ? AtmStatus.CannotDispense
                    : AtmStatus.Ok;

                var expected = expectedStatus == AtmStatus.Ok
                    ? new AtmResult(AtmStatus.Ok, combination!.Value, fee,
                        Acc(account.Balance - amount - fee, account.WithdrawnToday + amount))
                    : new AtmResult(expectedStatus, NoteCounts.None, 0, account);
                var expectedStock = expectedStatus == AtmStatus.Ok ? stock - combination!.Value : stock;

                var spy = new SpyDispenser(stock, jammed);
                var label = $"compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, retrait {amount}";

                AtmResult actual;
                try
                {
                    actual = new CashMachine(spy).Execute(account, W(amount));
                }
                catch (Exception e)
                {
                    Fail(failures, ref count, ref reported, $"{label} : exception {e.GetType().Name}");
                    continue;
                }

                if (actual != expected)
                {
                    Fail(failures, ref count, ref reported, $"{label} : attendu {expected}, obtenu {actual}");
                }
                else if (spy.Stock != expectedStock)
                {
                    Fail(failures, ref count, ref reported, $"{label} : stock attendu {expectedStock}, obtenu {spy.Stock}");
                }
                else if (spy.Loads.Count > 0)
                {
                    Fail(failures, ref count, ref reported, $"{label} : un retrait ne doit rien charger");
                }
                else if (spy.Dispenses.Any(p => !p.Result || p.Notes.Total > 4))
                {
                    Fail(failures, ref count, ref reported, $"{label} : passage invalide {spy.Dispenses.First(p => !p.Result || p.Notes.Total > 4)}");
                }
            }
        });

        Report(failures, count, "retrait");
    }

    [Fact]
    public void Depot_sur_tout_le_domaine()
    {
        var accounts = AllAccounts().ToArray();
        var cases =
            from stock in AllStocks()
            from jammed in AllJams
            from notes in AllDeposits()
            select (stock, jammed, notes);

        var failures = new ConcurrentQueue<string>();
        long count = 0;

        Parallel.ForEach(cases, c =>
        {
            var (stock, jammed, notes) = c;
            var after = stock + notes;

            var expectedStatus =
                notes.Total == 0 ? AtmStatus.InvalidAmount
                : jammed is { } j && notes.Of(j) > 0 ? AtmStatus.CassetteJammed
                : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
                : AtmStatus.Ok;
            var expectedStock = expectedStatus == AtmStatus.Ok ? after : stock;

            var reported = false;
            foreach (var account in accounts)
            {
                var expected = expectedStatus == AtmStatus.Ok
                    ? new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value })
                    : new AtmResult(expectedStatus, NoteCounts.None, 0, account);

                var spy = new SpyDispenser(stock, jammed);
                var label = $"compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, dépôt {notes}";

                AtmResult actual;
                try
                {
                    actual = new CashMachine(spy).Execute(account, new Operation.Deposit(notes));
                }
                catch (Exception e)
                {
                    Fail(failures, ref count, ref reported, $"{label} : exception {e.GetType().Name}");
                    continue;
                }

                if (actual != expected)
                {
                    Fail(failures, ref count, ref reported, $"{label} : attendu {expected}, obtenu {actual}");
                }
                else if (spy.Stock != expectedStock)
                {
                    Fail(failures, ref count, ref reported, $"{label} : stock attendu {expectedStock}, obtenu {spy.Stock}");
                }
                else if (spy.Dispenses.Count > 0)
                {
                    Fail(failures, ref count, ref reported, $"{label} : un dépôt ne doit rien sortir");
                }
            }
        });

        Report(failures, count, "dépôt");
    }
}
