namespace Atm.Tests;

/// <summary>Outils communs aux tests.</summary>
internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    /// <summary>0 = aucune cassette bloquée, sinon la valeur de la coupure (50, 20, 10).</summary>
    public static Denomination? Jam(int value) => value == 0 ? null : (Denomination)value;

    public static Operation.Withdraw Withdraw(int amount) => new(amount);

    public static Operation.Deposit Deposit(NoteCounts notes) => new(notes);

    /// <summary>Le vrai module, enveloppé pour observer les appels.</summary>
    public static (CashMachine Machine, SpyDispenser Dispenser) Build(NoteCounts stock, Denomination? jammed = null)
    {
        var spy = new SpyDispenser(new MoneyDispenser(stock, jammed));
        return (new CashMachine(spy), spy);
    }

    public static readonly Denomination?[] AllJams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
            yield return N(f, t, n);
    }

    public static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var n = 0; n <= 1; n++)
            yield return N(f, t, n);
    }

    public static readonly int[] AllAmounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static readonly int[] AllBalances = [.. Enumerable.Range(0, 51).Select(i => i * 5)];

    public static readonly int[] AllWithdrawnToday = [.. Enumerable.Range(0, 21).Select(i => i * 10)];
}

/// <summary>
/// Délègue au vrai <see cref="MoneyDispenser"/> et enregistre chaque appel et sa réponse.
/// Le comportement reste celui du vrai module : on reste dans le domaine de la spec.
/// </summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<(NoteCounts Notes, bool Result)> Dispensed { get; } = [];

    public List<(NoteCounts Notes, bool Result)> Loaded { get; } = [];

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var result = inner.Dispense(notes);
        Dispensed.Add((notes, result));
        return result;
    }

    public bool Load(NoteCounts notes)
    {
        var result = inner.Load(notes);
        Loaded.Add((notes, result));
        return result;
    }

    public NoteCounts TotalDispensed =>
        Dispensed.Where(d => d.Result).Aggregate(NoteCounts.None, (acc, d) => acc + d.Notes);
}

/// <summary>Modèle de référence écrit directement d'après la spec, sur le domaine.</summary>
internal static class Reference
{
    public const int DailyLimit = 200;
    public const int Capacity = 5;

    public static int FeeFor(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => new(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    /// <summary>Recherche exhaustive de la combinaison exacte qui compte le moins de billets.</summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var n = 0; n <= available.Tens; n++)
        {
            var c = new NoteCounts(f, t, n);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total))
            {
                best = c;
            }
        }

        return best;
    }

    public static AtmResult Refused(AtmStatus status, AccountState account) => new(status, NoteCounts.None, 0, account);

    public static AtmResult Withdraw(AccountState account, int amount, NoteCounts stock, Denomination? jammed)
    {
        if (amount <= 0 || amount % 10 != 0)
            return Refused(AtmStatus.InvalidAmount, account);
        if (account.WithdrawnToday + amount > DailyLimit)
            return Refused(AtmStatus.DailyLimitExceeded, account);
        var fee = FeeFor(amount);
        if (amount + fee > account.Balance)
            return Refused(AtmStatus.InsufficientFunds, account);
        if (FewestNotes(amount, Available(stock, jammed)) is not { } notes)
            return Refused(AtmStatus.CannotDispense, account);

        return new AtmResult(AtmStatus.Ok, notes, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    public static AtmResult Deposit(AccountState account, NoteCounts notes, NoteCounts stock, Denomination? jammed)
    {
        if (notes.Total == 0)
            return Refused(AtmStatus.InvalidAmount, account);
        if (jammed is { } j && notes.Of(j) > 0)
            return Refused(AtmStatus.CassetteJammed, account);
        var after = stock + notes;
        if (after.Fifties > Capacity || after.Twenties > Capacity || after.Tens > Capacity)
            return Refused(AtmStatus.CassetteFull, account);

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0,
            new AccountState(account.Balance + notes.Value, account.WithdrawnToday));
    }
}
