using System.Text;
using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Compare la machine au modèle de référence (Oracle, écrit d'après la spec) sur de larges pans du domaine.
/// Chaque test agrège ses écarts pour donner un diagnostic lisible.
/// </summary>
public class DomainSweepTests
{
    private static readonly AccountState[] Accounts =
    [
        Acc(0, 0), Acc(5, 0), Acc(50, 0), Acc(95, 0), Acc(100, 0), Acc(105, 0), Acc(110, 0), Acc(115, 0),
        Acc(150, 30), Acc(200, 0), Acc(205, 0), Acc(250, 0), Acc(250, 100), Acc(250, 150), Acc(250, 190), Acc(250, 200),
    ];

    private static string? Compare(string label, Oracle.Expected e, AtmResult r, SpyDispenser spy, bool isWithdraw)
    {
        var problems = new List<string>();
        if (r.Status != e.Status) problems.Add($"statut {r.Status} au lieu de {e.Status}");
        if (r.Notes != e.Notes) problems.Add($"billets {r.Notes} au lieu de {e.Notes}");
        if (r.Fee != e.Fee) problems.Add($"frais {r.Fee} au lieu de {e.Fee}");
        if (r.Account != e.Account) problems.Add($"compte {r.Account} au lieu de {e.Account}");
        if (spy.Stock != e.StockAfter) problems.Add($"stock {spy.Stock} au lieu de {e.StockAfter}");
        if (spy.Dispensed.Any(d => !d.Ok) || spy.Loaded.Any(l => !l.Ok))
            problems.Add("appel refusé par le vrai module (DispenserFault hors cas attendu)");
        if (spy.Dispensed.Any(d => d.Notes.Total > 4)) problems.Add("passage de plus de 4 billets");
        if (isWithdraw && spy.Loaded.Count > 0) problems.Add("Load appelé pendant un retrait");
        if (!isWithdraw && spy.Dispensed.Count > 0) problems.Add("Dispense appelé pendant un dépôt");
        return problems.Count == 0 ? null : $"{label} : {string.Join(", ", problems)}";
    }

    private static void Report(List<string> failures, int total)
    {
        if (failures.Count == 0) return;
        var sb = new StringBuilder($"{failures.Count} écart(s) sur {total} cas. Premiers :\n");
        foreach (var f in failures.Take(25)) sb.AppendLine(f);
        Assert.Fail(sb.ToString());
    }

    public static TheoryData<int> Amounts() => new(DomainAmounts);

    [Theory]
    [MemberData(nameof(Amounts))]
    public void Retrait_conforme_a_la_spec_sur_le_domaine(int amount)
    {
        var failures = new List<string>();
        var total = 0;
        foreach (var account in Accounts)
            foreach (var stock in AllStocks())
                foreach (var jammed in AllJams)
                {
                    total++;
                    var expected = Oracle.Withdraw(account, stock, jammed, amount);
                    var (r, spy) = Run(account, stock, jammed, W(amount));
                    if (Compare($"{account} stock={stock} bloquée={jammed?.ToString() ?? "aucune"} retrait {amount}",
                            expected, r, spy, isWithdraw: true) is { } f)
                        failures.Add(f);
                }
        Report(failures, total);
    }

    [Fact]
    public void Retrait_conforme_a_la_spec_pour_tous_les_comptes_du_domaine()
    {
        // Tous les comptes du domaine, sur quelques stocks représentatifs.
        NoteCounts[] stocks = [N(5, 5, 5), N(0, 0, 0), N(1, 3, 0), N(2, 0, 5)];
        var failures = new List<string>();
        var total = 0;
        for (var balance = 0; balance <= 250; balance += 5)
            for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
                foreach (var stock in stocks)
                    foreach (var amount in DomainAmounts)
                    {
                        total++;
                        var account = Acc(balance, withdrawn);
                        var expected = Oracle.Withdraw(account, stock, null, amount);
                        var (r, spy) = Run(account, stock, null, W(amount));
                        if (Compare($"{account} stock={stock} retrait {amount}", expected, r, spy, isWithdraw: true) is { } f)
                            failures.Add(f);
                    }
        Report(failures, total);
    }

    [Fact]
    public void Depot_conforme_a_la_spec_sur_tout_le_domaine()
    {
        var failures = new List<string>();
        var total = 0;
        AccountState[] accounts = [Acc(0, 0), Acc(10, 30), Acc(250, 200), Acc(165, 90)];
        foreach (var account in accounts)
            foreach (var stock in AllStocks())
                foreach (var jammed in AllJams)
                    foreach (var notes in AllDeposits())
                    {
                        total++;
                        var expected = Oracle.Deposit(account, stock, jammed, notes);
                        var (r, spy) = Run(account, stock, jammed, D(notes));
                        if (Compare($"{account} stock={stock} bloquée={jammed?.ToString() ?? "aucune"} dépôt {notes}",
                                expected, r, spy, isWithdraw: false) is { } f)
                            failures.Add(f);
                    }
        Report(failures, total);
    }
}
