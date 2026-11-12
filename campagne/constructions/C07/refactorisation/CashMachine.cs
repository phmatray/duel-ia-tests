namespace Atm;

public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    public const int DailyLimit = 200;
    public const int FeeThreshold = 100;
    public const int WithdrawalFee = 5;
    public const int MaxNotesPerPass = 4;
    public const int CassetteCapacity = 5;

    private const int SmallestNote = 10;

    private readonly IMoneyDispenser _dispenser = dispenser ?? throw new ArgumentNullException(nameof(dispenser));

    public AtmResult Execute(AccountState account, Operation operation) => operation switch
    {
        Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
        Operation.Deposit deposit => Deposit(account, deposit.Notes),
        _ => throw new ArgumentException("Opération inconnue.", nameof(operation)),
    };

    // ---------------------------------------------------------------- Retrait

    private AtmResult Withdraw(AccountState account, int amount)
    {
        if (amount <= 0 || amount % SmallestNote != 0)
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

        if (FewestNotes(amount, AvailableNotes()) is not { } notes)
        {
            return Refused(AtmStatus.CannotDispense, account);
        }

        if (!TryDispenseInPasses(notes, out var dispensed))
        {
            return new AtmResult(AtmStatus.DispenserFault, dispensed, 0, account);
        }

        var after = new AccountState(
            Balance: account.Balance - amount - fee,
            WithdrawnToday: account.WithdrawnToday + amount);

        return new AtmResult(AtmStatus.Ok, notes, fee, after);
    }

    private static int FeeFor(int amount) => amount > FeeThreshold ? WithdrawalFee : 0;

    /// <summary>Le stock utilisable : la cassette bloquée compte pour zéro.</summary>
    private NoteCounts AvailableNotes()
    {
        var stock = _dispenser.Stock;

        return _dispenser.Jammed switch
        {
            Denomination.Fifty => stock with { Fifties = 0 },
            Denomination.Twenty => stock with { Twenties = 0 },
            Denomination.Ten => stock with { Tens = 0 },
            _ => stock,
        };
    }

    /// <summary>
    /// La combinaison qui fait exactement <paramref name="amount"/> avec le moins de billets,
    /// ou <c>null</c> s'il n'y en a aucune. <paramref name="amount"/> est un multiple de 10.
    /// </summary>
    private static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;

        for (var fifties = Math.Min(available.Fifties, amount / 50); fifties >= 0; fifties--)
        {
            var afterFifties = amount - fifties * 50;

            for (var twenties = Math.Min(available.Twenties, afterFifties / 20); twenties >= 0; twenties--)
            {
                var tens = (afterFifties - twenties * 20) / 10;
                if (tens > available.Tens)
                {
                    continue;
                }

                var candidate = new NoteCounts(fifties, twenties, tens);
                if (best is null || candidate.Total < best.Value.Total)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Sort <paramref name="notes"/> passage par passage et s'arrête au premier refus du module.
    /// <paramref name="dispensed"/> reçoit les billets effectivement sortis.
    /// </summary>
    private bool TryDispenseInPasses(NoteCounts notes, out NoteCounts dispensed)
    {
        dispensed = NoteCounts.None;

        foreach (var pass in SplitIntoPasses(notes))
        {
            if (!_dispenser.Dispense(pass))
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
            var pass = TakeAtMost(MaxNotesPerPass, remaining);
            remaining -= pass;

            yield return pass;
        }
    }

    /// <summary>Prend au plus <paramref name="count"/> billets dans <paramref name="notes"/>, les plus grosses coupures d'abord.</summary>
    private static NoteCounts TakeAtMost(int count, NoteCounts notes)
    {
        var fifties = Math.Min(notes.Fifties, count);
        var twenties = Math.Min(notes.Twenties, count - fifties);
        var tens = Math.Min(notes.Tens, count - fifties - twenties);

        return new NoteCounts(fifties, twenties, tens);
    }

    // ------------------------------------------------------------------ Dépôt

    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        if (notes.Total <= 0)
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
            return Refused(AtmStatus.DispenserFault, account);
        }

        var after = account with { Balance = account.Balance + notes.Value };

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, after);
    }

    private static bool ExceedsCapacity(NoteCounts stock) =>
        stock.Fifties > CassetteCapacity
        || stock.Twenties > CassetteCapacity
        || stock.Tens > CassetteCapacity;

    // ---------------------------------------------------------------- Commun

    /// <summary>Un refus : aucun billet, aucuns frais, compte inchangé.</summary>
    private static AtmResult Refused(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);
}
