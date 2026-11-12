namespace Atm;

public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    public const int DailyLimit = 200;
    public const int FeeThreshold = 100;
    public const int WithdrawalFee = 5;
    public const int CassetteCapacity = 5;
    public const int MaxNotesPerPass = 4;

    private readonly IMoneyDispenser _dispenser = dispenser ?? throw new ArgumentNullException(nameof(dispenser));

    public AtmResult Execute(AccountState account, Operation operation) => operation switch
    {
        Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
        Operation.Deposit deposit => Deposit(account, deposit.Notes),
        null => throw new ArgumentNullException(nameof(operation)),
        _ => throw new ArgumentException($"Opération inconnue : {operation.GetType().Name}.", nameof(operation)),
    };

    // ---- Retrait -------------------------------------------------------------------------------

    private AtmResult Withdraw(AccountState account, int amount)
    {
        // Les contrôles se font dans l'ordre de la spécification : le premier qui échoue l'emporte.
        if (amount <= 0 || amount % 10 != 0)
        {
            return Refused(AtmStatus.InvalidAmount, account);
        }

        if (account.WithdrawnToday + amount > DailyLimit)
        {
            return Refused(AtmStatus.DailyLimitExceeded, account);
        }

        var fee = FeeFor(amount);
        if (amount + fee > account.Balance)
        {
            return Refused(AtmStatus.InsufficientFunds, account);
        }

        if (NotePlanner.FewestNotes(amount, AvailableNotes()) is not { } notes)
        {
            return Refused(AtmStatus.CannotDispense, account);
        }

        if (!TryDispense(notes, out var dispensed))
        {
            return Fault(account, dispensed);
        }

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
        return new AtmResult(AtmStatus.Ok, notes, fee, after);
    }

    private static int FeeFor(int amount) => amount > FeeThreshold ? WithdrawalFee : 0;

    /// <summary>Le stock utilisable : la cassette bloquée compte pour zéro.</summary>
    private NoteCounts AvailableNotes()
    {
        var stock = _dispenser.Stock;
        var jammed = _dispenser.Jammed;

        int Usable(Denomination denomination) => denomination == jammed ? 0 : Math.Max(0, stock.Of(denomination));

        return new NoteCounts(Usable(Denomination.Fifty), Usable(Denomination.Twenty), Usable(Denomination.Ten));
    }

    /// <summary>
    /// Sort <paramref name="notes"/> en passages successifs. S'arrête au premier passage refusé ;
    /// <paramref name="dispensed"/> contient alors les billets déjà sortis.
    /// </summary>
    private bool TryDispense(NoteCounts notes, out NoteCounts dispensed)
    {
        dispensed = NoteCounts.None;
        foreach (var pass in NotePlanner.SplitIntoPasses(notes, MaxNotesPerPass))
        {
            if (!_dispenser.Dispense(pass))
            {
                return false;
            }

            dispensed += pass;
        }

        return true;
    }

    // ---- Dépôt ---------------------------------------------------------------------------------

    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        // Les contrôles se font dans l'ordre de la spécification : le premier qui échoue l'emporte.
        if (IsEmptyOrNegative(notes))
        {
            return Refused(AtmStatus.InvalidAmount, account);
        }

        if (_dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)
        {
            return Refused(AtmStatus.CassetteJammed, account);
        }

        if (ExceedsCapacity(_dispenser.Stock + notes))
        {
            return Refused(AtmStatus.CassetteFull, account);
        }

        if (!_dispenser.Load(notes))
        {
            return Fault(account, NoteCounts.None);
        }

        var after = account with { Balance = account.Balance + notes.Value };
        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, after);
    }

    private static bool IsEmptyOrNegative(NoteCounts notes) =>
        notes.Total <= 0 || notes.Fifties < 0 || notes.Twenties < 0 || notes.Tens < 0;

    private static bool ExceedsCapacity(NoteCounts stock) =>
        stock.Fifties > CassetteCapacity || stock.Twenties > CassetteCapacity || stock.Tens > CassetteCapacity;

    // ---- Résultats -----------------------------------------------------------------------------

    /// <summary>Un refus : rien ne sort, aucun frais, compte inchangé.</summary>
    private static AtmResult Refused(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);

    /// <summary>Une panne du module : aucun frais, compte inchangé, seuls les billets déjà sortis sont rendus.</summary>
    private static AtmResult Fault(AccountState account, NoteCounts dispensed) =>
        new(AtmStatus.DispenserFault, dispensed, 0, account);
}
