using static Atm.Tests.TestSupport;

namespace Atm.Tests;

/// <summary>
/// Balayage exhaustif du domaine de la spec, comparé au modèle de référence de <see cref="TestSupport"/>.
/// Un cas par (cassette bloquée, opération) ; chacun parcourt tous les soldes, retraits du jour et stocks.
/// </summary>
public class DomainSweepTests
{
    private const int MaxReported = 10;

    public static TheoryData<Denomination?, int> WithdrawCases()
    {
        var data = new TheoryData<Denomination?, int>();
        foreach (var jammed in JammedValues)
        foreach (var amount in Amounts)
            data.Add(jammed, amount);
        return data;
    }

    public static TheoryData<Denomination?, int, int, int> DepositCases()
    {
        var data = new TheoryData<Denomination?, int, int, int>();
        foreach (var jammed in JammedValues)
        foreach (var notes in DepositValues)
            data.Add(jammed, notes.Fifties, notes.Twenties, notes.Tens);
        return data;
    }

    [Theory]
    [MemberData(nameof(WithdrawCases))]
    public void Retrait_conforme_a_la_spec_sur_tout_le_domaine(Denomination? jammed, int amount) =>
        Sweep((balance, withdrawn, stock) =>
            (ExpectedWithdraw(balance, withdrawn, stock, jammed, amount), Withdraw(balance, withdrawn, stock, jammed, amount)),
            $"Withdraw({amount}), bloquée {jammed?.ToString() ?? "aucune"}");

    [Theory]
    [MemberData(nameof(DepositCases))]
    public void Depot_conforme_a_la_spec_sur_tout_le_domaine(Denomination? jammed, int f, int t, int n) =>
        Sweep((balance, withdrawn, stock) =>
            (ExpectedDeposit(balance, withdrawn, stock, jammed, N(f, t, n)), Deposit(balance, withdrawn, stock, jammed, N(f, t, n))),
            $"Deposit(50×{f} 20×{t} 10×{n}), bloquée {jammed?.ToString() ?? "aucune"}");

    private static void Sweep(
        Func<int, int, NoteCounts, (Expected Expected, (AtmResult Result, MoneyDispenser Dispenser) Actual)> check,
        string label)
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var stock in Stocks)
        foreach (var balance in Balances)
        foreach (var withdrawn in WithdrawnTodayValues)
        {
            var (expected, (result, dispenser)) = check(balance, withdrawn, stock);
            if (expected.Result == result && expected.StockAfter == dispenser.Stock) continue;

            count++;
            if (failures.Count < MaxReported)
            {
                failures.Add(
                    $"  solde {balance}, retiré {withdrawn}, stock {stock}\n" +
                    $"    attendu {expected.Result}, stock après {expected.StockAfter}\n" +
                    $"    obtenu  {result}, stock après {dispenser.Stock}");
            }
        }

        Assert.True(count == 0, $"{label} : {count} cas non conformes, dont :\n{string.Join("\n", failures)}");
    }
}
