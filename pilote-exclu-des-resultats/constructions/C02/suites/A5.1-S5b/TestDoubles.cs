namespace Atm.Tests;

/// <summary>
/// Enveloppe le vrai <see cref="MoneyDispenser"/> et enregistre chaque appel.
/// Peut simuler une panne : l'appel numéro <c>failOnCall</c> (à partir de 1) rend <c>false</c> sans rien changer.
/// </summary>
public sealed class SpyDispenser(NoteCounts stock, Denomination? jammed = null, int? failOnCall = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    public List<(string Kind, NoteCounts Notes, bool Result)> Calls { get; } = [];

    public IEnumerable<NoteCounts> SuccessfulDispenses =>
        Calls.Where(c => c.Kind == nameof(Dispense) && c.Result).Select(c => c.Notes);

    public IEnumerable<NoteCounts> SuccessfulLoads =>
        Calls.Where(c => c.Kind == nameof(Load) && c.Result).Select(c => c.Notes);

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes) => Record(nameof(Dispense), notes, () => _inner.Dispense(notes));

    public bool Load(NoteCounts notes) => Record(nameof(Load), notes, () => _inner.Load(notes));

    private bool Record(string kind, NoteCounts notes, Func<bool> action)
    {
        var result = Calls.Count + 1 == failOnCall ? false : action();
        Calls.Add((kind, notes, result));
        return result;
    }
}

/// <summary>Modèle de référence écrit directement à partir de spec.md.</summary>
public static class Reference
{
    public static int FeeFor(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => new(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    public static NoteCounts? BestCombination(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var c = 0; c <= available.Tens; c++)
        {
            var candidate = new NoteCounts(f, t, c);
            if (candidate.Value == amount && (best is null || candidate.Total < best.Value.Total))
            {
                best = candidate;
            }
        }

        return best;
    }

    public static (AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account) Withdraw(
        AccountState account, int amount, NoteCounts stock, Denomination? jammed)
    {
        if (amount <= 0 || amount % 10 != 0) return (AtmStatus.InvalidAmount, NoteCounts.None, 0, account);
        if (account.WithdrawnToday + amount > 200) return (AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, account);
        var fee = FeeFor(amount);
        if (amount + fee > account.Balance) return (AtmStatus.InsufficientFunds, NoteCounts.None, 0, account);
        if (BestCombination(amount, Available(stock, jammed)) is not { } notes)
            return (AtmStatus.CannotDispense, NoteCounts.None, 0, account);
        return (AtmStatus.Ok, notes, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    public static (AtmStatus Status, AccountState Account) Deposit(
        AccountState account, NoteCounts notes, NoteCounts stock, Denomination? jammed)
    {
        if (notes.Total == 0) return (AtmStatus.InvalidAmount, account);
        if (jammed is { } j && notes.Of(j) > 0) return (AtmStatus.CassetteJammed, account);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (AtmStatus.CassetteFull, account);
        return (AtmStatus.Ok, account with { Balance = account.Balance + notes.Value });
    }
}

public static class Domain
{
    public static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var c = 0; c <= 5; c++)
            yield return new NoteCounts(f, t, c);
    }

    public static IEnumerable<NoteCounts> Deposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var c = 0; c <= 1; c++)
            yield return new NoteCounts(f, t, c);
    }

    public static IEnumerable<int> Balances() => Enumerable.Range(0, 51).Select(i => i * 5);

    public static IEnumerable<int> WithdrawnTodays() => Enumerable.Range(0, 21).Select(i => i * 10);
}
