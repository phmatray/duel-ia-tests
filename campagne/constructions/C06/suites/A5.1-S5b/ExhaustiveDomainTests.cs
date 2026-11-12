using System.Text;

namespace Atm.Tests;

/// <summary>
/// Parcourt tout le domaine de la spec et compare la machine au modèle de référence <see cref="SpecModel"/> :
/// statut, billets, frais, compte, et stock du module après l'opération.
/// </summary>
public class ExhaustiveDomainTests
{
    private const int MaxReported = 15;

    public static TheoryData<int> Amounts => new(Domain.Amounts);

    [Theory]
    [MemberData(nameof(Amounts))]
    public void Retrait_conforme_a_la_spec_sur_tout_le_domaine(int amount)
    {
        var failures = new List<string>();
        var count = 0;

        foreach (var jammed in Domain.JammedCassettes)
        foreach (var stock in Domain.Stocks)
        foreach (var withdrawnToday in Domain.WithdrawnTodays)
        foreach (var balance in Domain.Balances)
        {
            var account = new AccountState(balance, withdrawnToday);
            var (expected, expectedStock) = SpecModel.Withdraw(account, stock, jammed, amount);

            var dispenser = new MoneyDispenser(stock, jammed);
            AtmResult actual;
            try
            {
                actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
            }
            catch (Exception e)
            {
                Record(failures, ref count, account, stock, jammed, $"Retrait {amount}", expected.ToString(), $"exception {e.GetType().Name}: {e.Message}");
                continue;
            }

            if (actual != expected || dispenser.Stock != expectedStock)
            {
                Record(failures, ref count, account, stock, jammed, $"Retrait {amount}",
                    $"{expected} / stock {expectedStock}", $"{actual} / stock {dispenser.Stock}");
            }
        }

        Assert.True(count == 0, Report(count, failures));
    }

    public static TheoryData<NoteCounts> Deposits => new(Domain.Deposits);

    [Theory]
    [MemberData(nameof(Deposits))]
    public void Depot_conforme_a_la_spec_sur_tout_le_domaine(NoteCounts deposit)
    {
        var failures = new List<string>();
        var count = 0;

        foreach (var jammed in Domain.JammedCassettes)
        foreach (var stock in Domain.Stocks)
        foreach (var withdrawnToday in Domain.WithdrawnTodays)
        foreach (var balance in Domain.Balances)
        {
            var account = new AccountState(balance, withdrawnToday);
            var (expected, expectedStock) = SpecModel.Deposit(account, stock, jammed, deposit);

            var dispenser = new MoneyDispenser(stock, jammed);
            AtmResult actual;
            try
            {
                actual = new CashMachine(dispenser).Execute(account, new Operation.Deposit(deposit));
            }
            catch (Exception e)
            {
                Record(failures, ref count, account, stock, jammed, $"Dépôt {deposit}", expected.ToString(), $"exception {e.GetType().Name}: {e.Message}");
                continue;
            }

            if (actual != expected || dispenser.Stock != expectedStock)
            {
                Record(failures, ref count, account, stock, jammed, $"Dépôt {deposit}",
                    $"{expected} / stock {expectedStock}", $"{actual} / stock {dispenser.Stock}");
            }
        }

        Assert.True(count == 0, Report(count, failures));
    }

    [Fact]
    public void Le_modele_de_reference_trouve_une_combinaison_minimale_unique()
    {
        // Garde-fou sur le modèle : la spec affirme que la combinaison minimale est toujours unique.
        foreach (var stock in Domain.Stocks)
        foreach (var amount in Enumerable.Range(1, 20).Select(i => i * 10))
        {
            var minimal = (
                from f in Enumerable.Range(0, stock.Fifties + 1)
                from t in Enumerable.Range(0, stock.Twenties + 1)
                from c in Enumerable.Range(0, stock.Tens + 1)
                where 50 * f + 20 * t + 10 * c == amount
                group new NoteCounts(f, t, c) by f + t + c into g
                orderby g.Key
                select g.ToList()).FirstOrDefault();

            if (minimal is null)
            {
                Assert.Null(SpecModel.FewestNotes(amount, stock));
            }
            else
            {
                Assert.Single(minimal);
                Assert.Equal(minimal[0], SpecModel.FewestNotes(amount, stock));
            }
        }
    }

    private static void Record(List<string> failures, ref int count, AccountState account, NoteCounts stock, Denomination? jammed,
        string operation, string expected, string actual)
    {
        count++;
        if (failures.Count < MaxReported)
        {
            failures.Add($"  {account}, stock 50×{stock.Fifties} 20×{stock.Twenties} 10×{stock.Tens}, bloquée {jammed?.ToString() ?? "aucune"}, {operation}\n" +
                         $"    attendu : {expected}\n    obtenu  : {actual}");
        }
    }

    private static string Report(int count, List<string> failures)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{count} cas non conformes à la spec. Premiers cas :");
        foreach (var f in failures)
        {
            sb.AppendLine(f);
        }

        return sb.ToString();
    }
}
