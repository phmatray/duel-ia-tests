namespace Atm;

/// <summary>
/// Le distributeur : applique un retrait ou un dépôt à un compte. Sans mémoire entre deux appels,
/// il ne dépend que du compte, de l'opération et du module de billets.
/// </summary>
public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    private const int DailyLimit = 200;
    private const int FeeThreshold = 100;
    private const int WithdrawalFee = 5;
    private const int CassetteCapacity = 5;
    private const int MaxNotesPerPass = 4;

    private static readonly Denomination[] Denominations = [Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public AtmResult Execute(AccountState account, Operation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation switch
        {
            Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
            Operation.Deposit deposit => Deposit(account, deposit.Notes),
            _ => throw new ArgumentException($"Opération inconnue : {operation.GetType().Name}.", nameof(operation)),
        };
    }

    // Retrait

    private AtmResult Withdraw(AccountState account, int amount)
    {
        if (amount <= 0 || amount % 10 != 0)
        {
            return Refuse(AtmStatus.InvalidAmount, account);
        }

        if (account.WithdrawnToday + amount > DailyLimit)
        {
            return Refuse(AtmStatus.DailyLimitExceeded, account);
        }

        var fee = FeeFor(amount);
        if (amount + fee > account.Balance)
        {
            return Refuse(AtmStatus.InsufficientFunds, account);
        }

        if (FewestNotes(amount, AvailableNotes()) is not { } notes)
        {
            return Refuse(AtmStatus.CannotDispense, account);
        }

        if (!TryDispense(notes, out var dispensed))
        {
            return new AtmResult(AtmStatus.DispenserFault, dispensed, 0, account);
        }

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);

        return new AtmResult(AtmStatus.Ok, notes, fee, after);
    }

    private static int FeeFor(int amount) => amount > FeeThreshold ? WithdrawalFee : 0;

    /// <summary>Les billets disponibles : le stock, sans la cassette bloquée, qui compte pour zéro.</summary>
    private NoteCounts AvailableNotes()
    {
        var stock = dispenser.Stock;
        var jammed = dispenser.Jammed;

        int Available(Denomination denomination) =>
            denomination == jammed ? 0 : Math.Max(0, stock.Of(denomination));

        return new NoteCounts(Available(Denomination.Fifty), Available(Denomination.Twenty), Available(Denomination.Ten));
    }

    /// <summary>
    /// Parmi les combinaisons de billets disponibles qui font exactement <paramref name="amount"/>,
    /// celle qui compte le moins de billets (toujours unique avec ces coupures), ou <c>null</c> s'il n'y en a aucune.
    /// </summary>
    private static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        var exactCombinations =
            from fifties in Enumerable.Range(0, available.Fifties + 1)
            from twenties in Enumerable.Range(0, available.Twenties + 1)
            let rest = amount - fifties * 50 - twenties * 20
            where rest >= 0 && rest % 10 == 0 && rest / 10 <= available.Tens
            select new NoteCounts(fifties, twenties, rest / 10);

        return exactCombinations
            .OrderBy(combination => combination.Total)
            .Cast<NoteCounts?>()
            .FirstOrDefault();
    }

    /// <summary>
    /// Sort <paramref name="notes"/> passage par passage et s'arrête au premier refus du module.
    /// <paramref name="dispensed"/> contient les billets effectivement sortis.
    /// </summary>
    private bool TryDispense(NoteCounts notes, out NoteCounts dispensed)
    {
        dispensed = NoteCounts.None;
        foreach (var pass in SplitIntoPasses(notes))
        {
            if (!dispenser.Dispense(pass))
            {
                return false;
            }

            dispensed += pass;
        }

        return true;
    }

    /// <summary>Découpe <paramref name="notes"/> en passages d'au plus <see cref="MaxNotesPerPass"/> billets.</summary>
    private static IEnumerable<NoteCounts> SplitIntoPasses(NoteCounts notes)
    {
        var remaining = notes;
        while (remaining.Total > 0)
        {
            var pass = TakeUpTo(MaxNotesPerPass, remaining);
            remaining -= pass;

            yield return pass;
        }
    }

    /// <summary>Prend au plus <paramref name="count"/> billets de <paramref name="notes"/>, les plus grosses coupures d'abord.</summary>
    private static NoteCounts TakeUpTo(int count, NoteCounts notes)
    {
        var fifties = Math.Min(count, notes.Fifties);
        var twenties = Math.Min(count - fifties, notes.Twenties);
        var tens = Math.Min(count - fifties - twenties, notes.Tens);

        return new NoteCounts(fifties, twenties, tens);
    }

    // Dépôt

    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        if (notes.Fifties < 0 || notes.Twenties < 0 || notes.Tens < 0 || notes.Total == 0)
        {
            return Refuse(AtmStatus.InvalidAmount, account);
        }

        if (dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)
        {
            return Refuse(AtmStatus.CassetteJammed, account);
        }

        if (WouldOverfillACassette(notes))
        {
            return Refuse(AtmStatus.CassetteFull, account);
        }

        if (!dispenser.Load(notes))
        {
            return Refuse(AtmStatus.DispenserFault, account);
        }

        var after = account with { Balance = account.Balance + notes.Value };

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, after);
    }

    private bool WouldOverfillACassette(NoteCounts deposit)
    {
        var after = dispenser.Stock + deposit;

        return Denominations.Any(denomination => after.Of(denomination) > CassetteCapacity);
    }

    // Commun

    /// <summary>Un refus : aucun billet, aucun frais, compte inchangé.</summary>
    private static AtmResult Refuse(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);
}
