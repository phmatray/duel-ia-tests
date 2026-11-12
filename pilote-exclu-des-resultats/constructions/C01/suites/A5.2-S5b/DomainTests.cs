namespace Atm.Tests;

/// <summary>
/// Compare la machine au modèle de référence (<see cref="Oracle"/>) sur le domaine défini par la spec :
/// exhaustivement sur le stock, la cassette bloquée et l'opération, pour une sélection de comptes,
/// puis sur un échantillon aléatoire (graine fixe) de tout le domaine.
/// </summary>
public class DomainTests
{
    private static readonly Denomination?[] JammedValues = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return new NoteCounts(f, t, d);
    }

    private static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return new NoteCounts(f, t, d);
    }

    private static readonly AccountState[] SelectedAccounts =
    [
        new(250, 0), new(0, 0), new(250, 200), new(205, 0), new(200, 0), new(115, 90),
        new(110, 0), new(105, 100), new(100, 0), new(65, 140), new(5, 0), new(250, 150),
    ];

    private sealed class Mismatches
    {
        private readonly List<string> _samples = [];
        private readonly Dictionary<string, int> _byStatus = [];
        public int Count { get; private set; }
        public int Total { get; private set; }

        public void Check(string scenario, AtmResult expected, NoteCounts expectedStock, AtmResult actual, NoteCounts actualStock)
        {
            Total++;
            if (expected == actual && expectedStock == actualStock) return;
            Count++;
            var key = $"{expected.Status} -> {actual.Status}";
            _byStatus[key] = _byStatus.GetValueOrDefault(key) + 1;
            if (_samples.Count < 15)
                _samples.Add($"{scenario}\n    attendu : {expected}, stock {expectedStock}\n    obtenu  : {actual}, stock {actualStock}");
        }

        public void AssertNone()
        {
            if (Count > 0)
                Assert.Fail($"{Count} écarts sur {Total} cas.\nPar statut (attendu -> obtenu) : "
                    + string.Join(", ", _byStatus.Select(kv => $"{kv.Key} ×{kv.Value}"))
                    + "\nExemples :\n" + string.Join("\n", _samples));
        }
    }

    private static void CheckWithdraw(Mismatches m, AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var (expected, expectedStock) = Oracle.Withdraw(account, stock, jammed, amount);
        var dispenser = new MoneyDispenser(stock, jammed);
        AtmResult actual;
        try
        {
            actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
        }
        catch (Exception e)
        {
            actual = default;
            m.Check($"Retrait {amount}, compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"} -> exception {e.GetType().Name}",
                expected, expectedStock, actual, dispenser.Stock);
            return;
        }

        m.Check($"Retrait {amount}, compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}",
            expected, expectedStock, actual, dispenser.Stock);
    }

    private static void CheckDeposit(Mismatches m, AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        var (expected, expectedStock) = Oracle.Deposit(account, stock, jammed, notes);
        var dispenser = new MoneyDispenser(stock, jammed);
        AtmResult actual;
        try
        {
            actual = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));
        }
        catch (Exception e)
        {
            actual = default;
            m.Check($"Dépôt {notes}, compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"} -> exception {e.GetType().Name}",
                expected, expectedStock, actual, dispenser.Stock);
            return;
        }

        m.Check($"Dépôt {notes}, compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}",
            expected, expectedStock, actual, dispenser.Stock);
    }

    [Fact]
    public void Retrait_conforme_au_modele_sur_tout_stock_toute_cassette_tout_montant()
    {
        var m = new Mismatches();
        foreach (var account in SelectedAccounts)
        foreach (var stock in AllStocks())
        foreach (var jammed in JammedValues)
        foreach (var amount in Amounts)
            CheckWithdraw(m, account, stock, jammed, amount);
        m.AssertNone();
    }

    [Fact]
    public void Depot_conforme_au_modele_sur_tout_stock_toute_cassette_tout_depot()
    {
        var m = new Mismatches();
        foreach (var account in SelectedAccounts)
        foreach (var stock in AllStocks())
        foreach (var jammed in JammedValues)
        foreach (var notes in AllDeposits())
            CheckDeposit(m, account, stock, jammed, notes);
        m.AssertNone();
    }

    [Fact]
    public void Operations_conformes_au_modele_sur_un_echantillon_de_tout_le_domaine()
    {
        var random = new Random(20261106);
        var stocks = AllStocks().ToArray();
        var deposits = AllDeposits().ToArray();
        var m = new Mismatches();

        for (var i = 0; i < 100_000; i++)
        {
            var account = new AccountState(random.Next(0, 51) * 5, random.Next(0, 21) * 10);
            var stock = stocks[random.Next(stocks.Length)];
            var jammed = JammedValues[random.Next(JammedValues.Length)];

            if (random.Next(4) == 0)
                CheckDeposit(m, account, stock, jammed, deposits[random.Next(deposits.Length)]);
            else
                CheckWithdraw(m, account, stock, jammed, Amounts[random.Next(Amounts.Length)]);
        }

        m.AssertNone();
    }

    [Fact]
    public void Le_modele_de_reference_reproduit_les_exemples_de_la_spec()
    {
        // Garde-fou : l'oracle lui-même est validé sur les exemples de la spec.
        foreach (object?[] r in ExamplesTests.Withdrawals)
        {
            var (result, _) = Oracle.Withdraw(new AccountState((int)r[0]!, (int)r[1]!), (NoteCounts)r[2]!, (Denomination?)r[3], (int)r[4]!);
            Assert.Equal(new AtmResult((AtmStatus)r[5]!, (NoteCounts)r[6]!, (int)r[7]!, new AccountState((int)r[8]!, (int)r[9]!)), result);
        }

        foreach (object?[] r in ExamplesTests.Deposits)
        {
            var (result, stockAfter) = Oracle.Deposit(new AccountState((int)r[0]!, (int)r[1]!), (NoteCounts)r[2]!, (Denomination?)r[3], (NoteCounts)r[4]!);
            Assert.Equal((AtmStatus)r[5]!, result.Status);
            Assert.Equal((NoteCounts)r[6]!, stockAfter);
            Assert.Equal(new AccountState((int)r[7]!, (int)r[8]!), result.Account);
        }
    }

    [Fact]
    public void La_combinaison_minimale_est_unique_sur_tout_le_domaine()
    {
        // « Avec ces trois coupures, elle est toujours unique. » Vérifié sur le modèle.
        foreach (var stock in AllStocks())
        foreach (var amount in Amounts.Where(a => a > 0 && a % 10 == 0))
        {
            var combos = new List<NoteCounts>();
            for (var f = 0; f <= stock.Fifties; f++)
            for (var t = 0; t <= stock.Twenties; t++)
            for (var d = 0; d <= stock.Tens; d++)
                if (f * 50 + t * 20 + d * 10 == amount) combos.Add(new NoteCounts(f, t, d));
            if (combos.Count == 0) continue;
            var min = combos.Min(c => c.Total);
            Assert.Single(combos, c => c.Total == min);
        }
    }
}
