namespace Atm.Tests;

/// <summary>Raccourcis de construction, notation de la spec : 50×a 20×b 10×c.</summary>
internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Denomination? Jam(int value) => value switch
    {
        0 => null,
        50 => Denomination.Fifty,
        20 => Denomination.Twenty,
        10 => Denomination.Ten,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static (CashMachine Machine, MoneyDispenser Dispenser) Machine(NoteCounts stock, Denomination? jammed = null)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        return (new CashMachine(dispenser), dispenser);
    }

    public static AtmResult Withdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount) =>
        Machine(stock, jammed).Machine.Execute(account, new Operation.Withdraw(amount));

    public static AtmResult Deposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes) =>
        Machine(stock, jammed).Machine.Execute(account, new Operation.Deposit(notes));
}

/// <summary>
/// Enveloppe le vrai module : enregistre chaque appel et peut simuler une panne
/// (rendre <c>false</c> sans rien changer) à partir du n-ième appel.
/// </summary>
internal sealed class SpyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _real = new(stock, jammed);

    public List<NoteCounts> DispenseCalls { get; } = [];
    public List<NoteCounts> LoadCalls { get; } = [];
    public NoteCounts SuccessfullyDispensed { get; private set; }

    /// <summary>Numéro (à partir de 1) du premier appel à Dispense qui tombe en panne ; null = jamais.</summary>
    public int? FailDispenseFromCall { get; init; }

    public bool FailLoad { get; init; }

    public NoteCounts Stock => _real.Stock;
    public Denomination? Jammed => _real.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (FailDispenseFromCall is { } n && DispenseCalls.Count >= n)
        {
            return false;
        }

        var ok = _real.Dispense(notes);
        if (ok)
        {
            SuccessfullyDispensed += notes;
        }

        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        return !FailLoad && _real.Load(notes);
    }
}

/// <summary>Le résultat attendu, calculé directement depuis la spec (indépendamment du code testé).</summary>
internal static class Oracle
{
    public static (AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account, NoteCounts StockAfter) Withdraw(
        AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var refused = (AtmStatus s) => (s, NoteCounts.None, 0, account, stock);

        if (amount <= 0 || amount % 10 != 0) return refused(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return refused(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return refused(AtmStatus.InsufficientFunds);

        var available = new NoteCounts(
            jammed == Denomination.Fifty ? 0 : stock.Fifties,
            jammed == Denomination.Twenty ? 0 : stock.Twenties,
            jammed == Denomination.Ten ? 0 : stock.Tens);

        NoteCounts? best = null;
        for (var a = 0; a <= available.Fifties; a++)
        for (var b = 0; b <= available.Twenties; b++)
        for (var c = 0; c <= available.Tens; c++)
        {
            var candidate = new NoteCounts(a, b, c);
            if (candidate.Value == amount && (best is null || candidate.Total < best.Value.Total))
            {
                best = candidate;
            }
        }

        if (best is not { } notes) return refused(AtmStatus.CannotDispense);

        return (AtmStatus.Ok, notes, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount),
            stock - notes);
    }

    public static (AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account, NoteCounts StockAfter) Deposit(
        AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        var refused = (AtmStatus s) => (s, NoteCounts.None, 0, account, stock);

        if (notes.Total == 0) return refused(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return refused(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return refused(AtmStatus.CassetteFull);

        return (AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }, after);
    }
}

/// <summary>Les valeurs du domaine de la spec.</summary>
internal static class Domain
{
    public static IEnumerable<int> Balances => Enumerable.Range(0, 51).Select(i => i * 5);
    public static IEnumerable<int> WithdrawnToday => Enumerable.Range(0, 21).Select(i => i * 10);
    public static IEnumerable<int> Amounts => new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));
    public static IEnumerable<Denomination?> Jams => [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static IEnumerable<NoteCounts> Stocks =>
        from a in Enumerable.Range(0, 6)
        from b in Enumerable.Range(0, 6)
        from c in Enumerable.Range(0, 6)
        select new NoteCounts(a, b, c);

    public static IEnumerable<NoteCounts> Deposits =>
        from a in Enumerable.Range(0, 2)
        from b in Enumerable.Range(0, 2)
        from c in Enumerable.Range(0, 2)
        select new NoteCounts(a, b, c);
}
