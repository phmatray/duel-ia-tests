using static Atm.Tests.Outils;

namespace Atm.Tests;

/// <summary>
/// Balayage du domaine : chaque résultat est comparé à la spec, réécrite ici sous forme directe.
/// </summary>
public class DomaineTests
{
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<int> Amounts() => new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));

    /// <summary>Tous les comptes et tous les montants, stock plein sans cassette bloquée (toute somme jusqu'à 200 est alors distribuable).</summary>
    [Fact]
    public void Retrait_tous_comptes_tous_montants()
    {
        var erreurs = new List<string>();

        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in Amounts())
        {
            var account = Compte(balance, withdrawn);
            var (r, d) = Executer(account, Plein, null, Retrait(amount));
            var (status, fee) = Attendu(account, amount);

            var notesOk = status == AtmStatus.Ok ? r.Notes.Value == amount && d.Stock == Plein - r.Notes : r.Notes == NoteCounts.None && d.Stock == Plein;
            var accountOk = status == AtmStatus.Ok
                ? r.Account == Compte(balance - amount - fee, withdrawn + amount)
                : r.Account == account;

            if (r.Status != status || r.Fee != fee || !notesOk || !accountOk)
            {
                erreurs.Add($"compte {account}, retrait {amount} : attendu {status} frais {fee}, obtenu {r.Status} frais {r.Fee} {r.Account}");
            }
        }

        Assert.True(erreurs.Count == 0, $"{erreurs.Count} écarts, dont :{Environment.NewLine}{string.Join(Environment.NewLine, erreurs.Take(15))}");
    }

    /// <summary>Tous les stocks, toutes les cassettes bloquées, tous les dépôts, quelques comptes.</summary>
    [Fact]
    public void Depot_tous_stocks_toutes_cassettes_tous_depots()
    {
        var erreurs = new List<string>();
        AccountState[] accounts = [Compte(0, 0), Compte(125, 70), Compte(250, 200)];

        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d10 = 0; d10 <= 5; d10++)
        foreach (var jammed in Jams)
        for (var df = 0; df <= 1; df++)
        for (var dt = 0; dt <= 1; dt++)
        for (var dd = 0; dd <= 1; dd++)
        foreach (var account in accounts)
        {
            var stock = N(f, t, d10);
            var notes = N(df, dt, dd);
            var (r, d) = Executer(account, stock, jammed, Depot(notes));

            var after = stock + notes;
            var status =
                notes.Total == 0 ? AtmStatus.InvalidAmount
                : jammed is { } j && notes.Of(j) > 0 ? AtmStatus.CassetteJammed
                : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
                : AtmStatus.Ok;

            var expectedAccount = status == AtmStatus.Ok ? account with { Balance = account.Balance + notes.Value } : account;
            var expectedStock = status == AtmStatus.Ok ? after : stock;

            if (r.Status != status || r.Notes != NoteCounts.None || r.Fee != 0 || r.Account != expectedAccount || d.Stock != expectedStock)
            {
                erreurs.Add($"stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, dépôt {notes}, compte {account} : attendu {status}, obtenu {r.Status}");
            }
        }

        Assert.True(erreurs.Count == 0, $"{erreurs.Count} écarts, dont :{Environment.NewLine}{string.Join(Environment.NewLine, erreurs.Take(15))}");
    }

    private static (AtmStatus Status, int Fee) Attendu(AccountState account, int amount)
    {
        if (amount <= 0 || amount % 10 != 0) return (AtmStatus.InvalidAmount, 0);
        if (account.WithdrawnToday + amount > 200) return (AtmStatus.DailyLimitExceeded, 0);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return (AtmStatus.InsufficientFunds, 0);

        return (AtmStatus.Ok, fee);
    }
}
