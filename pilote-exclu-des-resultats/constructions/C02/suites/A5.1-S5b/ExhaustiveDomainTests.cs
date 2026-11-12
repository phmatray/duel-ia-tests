namespace Atm.Tests;

/// <summary>
/// Balaye le domaine défini par la spec et compare chaque résultat au modèle de référence.
/// Chaque test rapporte les premiers écarts trouvés.
/// </summary>
public class ExhaustiveDomainTests
{
    private const int MaxReported = 15;

    private static void Report(List<string> failures)
    {
        if (failures.Count > 0)
        {
            Assert.Fail($"{failures.Count} écart(s) avec la spec, dont :\n" + string.Join("\n", failures.Take(MaxReported)));
        }
    }

    private static string? CheckWithdraw(AccountState account, int amount, NoteCounts stock, Denomination? jammed)
    {
        var dispenser = new SpyDispenser(stock, jammed);
        var actual = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
        var expected = Reference.Withdraw(account, amount, stock, jammed);
        var ctx = $"compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, retrait {amount}";

        if (actual.Status != expected.Status || actual.Notes != expected.Notes || actual.Fee != expected.Fee || actual.Account != expected.Account)
        {
            return $"{ctx} : attendu {expected}, obtenu {actual}";
        }

        var passes = dispenser.Calls.Where(c => c.Kind == "Dispense").ToList();
        if (dispenser.Calls.Any(c => c.Kind == "Load"))
            return $"{ctx} : Load appelé pendant un retrait";
        if (passes.Any(p => !p.Result))
            return $"{ctx} : le module a refusé un passage {passes.First(p => !p.Result).Notes}";
        if (passes.Any(p => p.Notes.Total > 4))
            return $"{ctx} : passage de plus de 4 billets";
        var dispensed = passes.Aggregate(NoteCounts.None, (a, p) => a + p.Notes);
        if (dispensed != expected.Notes)
            return $"{ctx} : billets sortis {dispensed} ≠ résultat {expected.Notes}";
        if (dispenser.Stock != stock - expected.Notes)
            return $"{ctx} : stock final {dispenser.Stock}";
        return null;
    }

    [Fact]
    public void Retrait_sur_tous_les_stocks_cassettes_bloquees_et_montants()
    {
        var failures = new List<string>();
        foreach (var account in new[] { new AccountState(250, 0), new AccountState(250, 100), new AccountState(80, 0) })
        foreach (var stock in Domain.Stocks())
        foreach (var jammed in Domain.Jams)
        foreach (var amount in Domain.Amounts)
        {
            if (CheckWithdraw(account, amount, stock, jammed) is { } f) failures.Add(f);
        }

        Report(failures);
    }

    [Fact]
    public void Retrait_sur_tous_les_soldes_retraits_du_jour_et_montants()
    {
        var failures = new List<string>();
        foreach (var stock in new[] { new NoteCounts(5, 5, 5), new NoteCounts(1, 3, 0), new NoteCounts(0, 0, 0) })
        foreach (var jammed in new Denomination?[] { null, Denomination.Fifty })
        foreach (var balance in Domain.Balances())
        foreach (var withdrawn in Domain.WithdrawnTodays())
        foreach (var amount in Domain.Amounts)
        {
            if (CheckWithdraw(new AccountState(balance, withdrawn), amount, stock, jammed) is { } f) failures.Add(f);
        }

        Report(failures);
    }

    [Fact]
    public void Depot_sur_tout_le_domaine()
    {
        var failures = new List<string>();
        foreach (var account in new[] { new AccountState(0, 0), new AccountState(135, 70), new AccountState(250, 200) })
        foreach (var stock in Domain.Stocks())
        foreach (var jammed in Domain.Jams)
        foreach (var notes in Domain.Deposits())
        {
            var dispenser = new SpyDispenser(stock, jammed);
            var actual = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));
            var expected = Reference.Deposit(account, notes, stock, jammed);
            var ctx = $"compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, dépôt {notes}";
            var expectedStock = expected.Status == AtmStatus.Ok ? stock + notes : stock;

            if (actual.Status != expected.Status || actual.Account != expected.Account
                || actual.Notes != NoteCounts.None || actual.Fee != 0)
                failures.Add($"{ctx} : attendu {expected}, obtenu {actual}");
            else if (dispenser.Stock != expectedStock)
                failures.Add($"{ctx} : stock final {dispenser.Stock}, attendu {expectedStock}");
            else if (dispenser.Calls.Any(c => c.Kind == "Dispense"))
                failures.Add($"{ctx} : Dispense appelé pendant un dépôt");
        }

        Report(failures);
    }

    [Fact]
    public void Panne_a_chaque_passage_possible_d_un_retrait()
    {
        var failures = new List<string>();
        var account = new AccountState(250, 0);
        foreach (var stock in Domain.Stocks())
        foreach (var jammed in Domain.Jams)
        foreach (var amount in Domain.Amounts)
        {
            var expected = Reference.Withdraw(account, amount, stock, jammed);
            if (expected.Status != AtmStatus.Ok) continue;

            // Nombre de passages réellement utilisés par la machine sans panne.
            var probe = new SpyDispenser(stock, jammed);
            new CashMachine(probe).Execute(account, new Operation.Withdraw(amount));
            var passCount = probe.Calls.Count;

            for (var k = 1; k <= passCount; k++)
            {
                var dispenser = new SpyDispenser(stock, jammed, failOnCall: k);
                var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
                var already = dispenser.SuccessfulDispenses.Aggregate(NoteCounts.None, (a, n) => a + n);
                var ctx = $"stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, retrait {amount}, panne au passage {k}";

                if (r.Status != AtmStatus.DispenserFault || r.Account != account)
                    failures.Add($"{ctx} : obtenu {r}");
                else if (dispenser.Calls.Count != k)
                    failures.Add($"{ctx} : {dispenser.Calls.Count} appels, la machine ne s'est pas arrêtée");
                else if (r.Notes != already)
                    failures.Add($"{ctx} : Notes {r.Notes} ≠ billets déjà sortis {already}");
                else if (r.Fee != 0)
                    failures.Add($"{ctx} : Fee {r.Fee}, attendu 0");
            }
        }

        Report(failures);
    }

    [Fact]
    public void Panne_au_depot_sur_tout_le_domaine()
    {
        var failures = new List<string>();
        var account = new AccountState(100, 40);
        foreach (var stock in Domain.Stocks())
        foreach (var jammed in Domain.Jams)
        foreach (var notes in Domain.Deposits())
        {
            if (Reference.Deposit(account, notes, stock, jammed).Status != AtmStatus.Ok) continue;

            var dispenser = new SpyDispenser(stock, jammed, failOnCall: 1);
            var r = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));
            if (r.Status != AtmStatus.DispenserFault || r.Notes != NoteCounts.None || r.Fee != 0 || r.Account != account
                || dispenser.Calls.Count != 1 || dispenser.Stock != stock)
                failures.Add($"stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, dépôt {notes} : obtenu {r}, {dispenser.Calls.Count} appel(s)");
        }

        Report(failures);
    }
}
