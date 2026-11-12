using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Parcourt tout le domaine de la spec (solde, déjà retiré, stock, cassette bloquée, opération)
/// et compare chaque résultat à l'oracle tiré de la spec.
/// Vérifie aussi : stock final, billets réellement sortis/rangés, passages de 4 billets au plus,
/// aucun billet déplacé sur un refus, et jamais de DispenserFault avec le vrai module.
/// </summary>
public class DomainExhaustiveTests
{
    private static readonly int[] Amounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static TheoryData<int> AllAmounts() => new(Amounts);

    public static TheoryData<int, int, int> AllDeposits()
    {
        var data = new TheoryData<int, int, int>();
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var n = 0; n <= 1; n++)
            data.Add(f, t, n);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllAmounts))]
    public void Retrait_sur_tout_le_domaine(int amount) => Sweep(W(amount));

    [Theory]
    [MemberData(nameof(AllDeposits))]
    public void Depot_sur_tout_le_domaine(int f, int t, int n) => Sweep(D(N(f, t, n)));

    private static string Fmt(NoteCounts c) => $"50×{c.Fifties} 20×{c.Twenties} 10×{c.Tens}";

    private static string Fmt(Operation op) => op switch
    {
        Operation.Withdraw w => $"Retrait {w.Amount}",
        Operation.Deposit d => $"Dépôt {Fmt(d.Notes)}",
        _ => op.ToString(),
    };

    private static void Sweep(Operation op)
    {
        // Écarts regroupés par nature : nombre de cas et premier exemple.
        var failures = new Dictionary<string, (int Count, string Example)>();
        var total = 0;
        var wrong = 0;

        foreach (var jam in JamOptions)
        for (var sf = 0; sf <= 5; sf++)
        for (var st = 0; st <= 5; st++)
        for (var sn = 0; sn <= 5; sn++)
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        {
            total++;
            var stock = N(sf, st, sn);
            var account = Acc(balance, withdrawn);
            var expected = Oracle.Compute(account, stock, jam, op);
            var (machine, spy) = Build(stock, jam);
            var where = $"solde={balance} retiré={withdrawn} stock={Fmt(stock)} bloquée={jam?.ToString() ?? "aucune"}";
            var problems = new List<(string Kind, string Detail)>();

            try
            {
                var actual = machine.Execute(account, op);

                if (actual.Status != expected.Status)
                    problems.Add(($"Status {actual.Status} au lieu de {expected.Status}", ""));
                else
                {
                    if (actual.Notes != expected.Notes)
                        problems.Add(("Notes fausses", $"{Fmt(actual.Notes)} au lieu de {Fmt(expected.Notes)}"));
                    if (actual.Fee != expected.Fee)
                        problems.Add(("Fee faux", $"{actual.Fee} au lieu de {expected.Fee}"));
                    if (actual.Account != expected.Account)
                        problems.Add(("Account faux", $"{actual.Account} au lieu de {expected.Account}"));
                    if (spy.Stock != expected.StockAfter)
                        problems.Add(("stock final faux", $"{Fmt(spy.Stock)} au lieu de {Fmt(expected.StockAfter)}"));
                }

                if (spy.DispenseCalls.Any(c => !c.Ok) || spy.LoadCalls.Any(c => !c.Ok))
                    problems.Add(("appel refusé par le vrai module", ""));
                if (spy.DispenseCalls.Any(c => c.Notes.Total > MoneyDispenser.MaxNotesPerPass))
                    problems.Add(("passage de plus de 4 billets", ""));
                if (expected.Status != AtmStatus.Ok && spy.AnyMove)
                    problems.Add(("billets déplacés sur un refus", ""));
                if (op is Operation.Withdraw && spy.Dispensed != actual.Notes)
                    problems.Add(("billets sortis ≠ Notes", $"{Fmt(spy.Dispensed)} / {Fmt(actual.Notes)}"));
                if (op is Operation.Withdraw && spy.LoadCalls.Count > 0)
                    problems.Add(("Load appelé lors d'un retrait", ""));
                if (op is Operation.Deposit && spy.DispenseCalls.Count > 0)
                    problems.Add(("Dispense appelé lors d'un dépôt", ""));
            }
            catch (Exception e)
            {
                problems.Add(($"exception {e.GetType().Name}", e.Message));
            }

            if (problems.Count > 0) wrong++;
            foreach (var (kind, detail) in problems)
            {
                failures[kind] = failures.TryGetValue(kind, out var f)
                    ? (f.Count + 1, f.Example)
                    : (1, $"{where} {detail}".TrimEnd());
            }
        }

        Assert.True(wrong == 0,
            $"{Fmt(op)} : {wrong} cas faux sur {total}.\n" +
            string.Join("\n", failures.Select(f => $"  - {f.Key} : {f.Value.Count} cas, ex. {f.Value.Example}")));
    }
}
