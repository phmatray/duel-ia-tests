namespace Atm;

/// <summary>
/// Distributeur de billets sans mémoire : chaque appel ne dépend que du compte, de l'opération et du module de billets.
/// </summary>
public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    public const int DailyLimit = 200;
    public const int FeeThreshold = 100;
    public const int WithdrawalFee = 5;
    public const int CassetteCapacity = 5;
    public const int MaxNotesPerPass = 4;

    private const int SmallestNote = (int)Denomination.Ten;

    public AtmResult Execute(AccountState account, Operation operation) => operation switch
    {
        Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
        Operation.Deposit deposit => Deposit(account, deposit.Notes),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    // ---- Retrait ----

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

        if (FewestNotesFor(amount, AvailableNotes()) is not { } notes)
        {
            return Refused(AtmStatus.CannotDispense, account);
        }

        var dispensed = NoteCounts.None;
        foreach (var pass in SplitIntoPasses(notes))
        {
            if (!dispenser.Dispense(pass))
            {
                return Fault(account, dispensed);
            }

            dispensed += pass;
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
        var stock = dispenser.Stock;

        return dispenser.Jammed switch
        {
            Denomination.Fifty => stock with { Fifties = 0 },
            Denomination.Twenty => stock with { Twenties = 0 },
            Denomination.Ten => stock with { Tens = 0 },
            _ => stock,
        };
    }

    /// <summary>
    /// La combinaison qui fait exactement <paramref name="amount"/> avec le moins de billets, ou <c>null</c> s'il n'y en a pas.
    /// <paramref name="amount"/> est un multiple positif de 10.
    /// </summary>
    private static NoteCounts? FewestNotesFor(int amount, NoteCounts available)
    {
        const int fifty = (int)Denomination.Fifty;
        const int twenty = (int)Denomination.Twenty;
        const int ten = (int)Denomination.Ten;

        NoteCounts? best = null;
        for (var fifties = 0; fifties <= available.Fifties && fifties * fifty <= amount; fifties++)
        {
            for (var twenties = 0; twenties <= available.Twenties && fifties * fifty + twenties * twenty <= amount; twenties++)
            {
                var tens = (amount - fifties * fifty - twenties * twenty) / ten;
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

    /// <summary>Découpe les billets en passages d'au plus <see cref="MaxNotesPerPass"/> billets, les plus grosses coupures d'abord.</summary>
    private static IEnumerable<NoteCounts> SplitIntoPasses(NoteCounts notes)
    {
        var remaining = notes;
        while (remaining.Total > 0)
        {
            var room = MaxNotesPerPass;
            var fifties = Take(remaining.Fifties, ref room);
            var twenties = Take(remaining.Twenties, ref room);
            var tens = Take(remaining.Tens, ref room);

            var pass = new NoteCounts(fifties, twenties, tens);
            remaining -= pass;

            yield return pass;
        }
    }

    /// <summary>Prend autant de billets que possible parmi <paramref name="count"/> dans la place restante du passage.</summary>
    private static int Take(int count, ref int room)
    {
        var taken = Math.Min(count, room);
        room -= taken;

        return taken;
    }

    // ---- Dépôt ----

    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        if (notes.Total <= 0)
        {
            return Refused(AtmStatus.InvalidAmount, account);
        }

        if (dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)
        {
            return Refused(AtmStatus.CassetteJammed, account);
        }

        if (ExceedsCapacity(dispenser.Stock + notes))
        {
            return Refused(AtmStatus.CassetteFull, account);
        }

        if (!dispenser.Load(notes))
        {
            return Fault(account, NoteCounts.None);
        }

        var after = account with { Balance = account.Balance + notes.Value };

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, after);
    }

    private static bool ExceedsCapacity(NoteCounts stock) =>
        stock.Fifties > CassetteCapacity || stock.Twenties > CassetteCapacity || stock.Tens > CassetteCapacity;

    // ---- Résultats d'échec ----

    /// <summary>Refus : aucun billet, aucun frais, compte inchangé.</summary>
    private static AtmResult Refused(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);

    /// <summary>Panne du module : on rend les billets déjà sortis, sans frais, compte inchangé.</summary>
    private static AtmResult Fault(AccountState account, NoteCounts alreadyDispensed) =>
        new(AtmStatus.DispenserFault, alreadyDispensed, 0, account);
}
