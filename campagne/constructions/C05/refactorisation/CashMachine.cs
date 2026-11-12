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

    // ---------- Retrait ----------

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

        var dispensed = NoteCounts.None;
        foreach (var pass in SplitIntoPasses(notes))
        {
            if (!_dispenser.Dispense(pass))
            {
                return Fault(account, dispensed);
            }

            dispensed += pass;
        }

        var debited = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);

        return new AtmResult(AtmStatus.Ok, notes, fee, debited);
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
    /// La combinaison exacte qui compte le moins de billets, ou <c>null</c> s'il n'y en a aucune.
    /// <paramref name="amount"/> est un multiple de 10 strictement positif.
    /// </summary>
    private static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;

        for (var fifties = MaxUsable(available.Fifties, amount, 50); fifties >= 0; fifties--)
        {
            var afterFifties = amount - fifties * 50;

            for (var twenties = MaxUsable(available.Twenties, afterFifties, 20); twenties >= 0; twenties--)
            {
                var tens = (afterFifties - twenties * 20) / 10;
                if (tens > available.Tens)
                {
                    continue;
                }

                var candidate = new NoteCounts(fifties, twenties, tens);
                if (best is not { } current || candidate.Total < current.Total)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    /// <summary>Le plus grand nombre de billets de cette valeur qu'on peut prendre sans dépasser <paramref name="remaining"/>.</summary>
    private static int MaxUsable(int available, int remaining, int noteValue) =>
        Math.Max(0, Math.Min(available, remaining / noteValue));

    /// <summary>Découpe une combinaison en passages non vides d'au plus <see cref="MaxNotesPerPass"/> billets.</summary>
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
            yield return pass;
            remaining -= pass;
        }

        static int Take(int wanted, ref int room)
        {
            var taken = Math.Min(wanted, room);
            room -= taken;
            return taken;
        }
    }

    // ---------- Dépôt ----------

    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        if (HasNegativeCount(notes) || notes.Total == 0)
        {
            return Refuse(AtmStatus.InvalidAmount, account);
        }

        if (_dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)
        {
            return Refuse(AtmStatus.CassetteJammed, account);
        }

        if (ExceedsCapacity(_dispenser.Stock + notes))
        {
            return Refuse(AtmStatus.CassetteFull, account);
        }

        if (!_dispenser.Load(notes))
        {
            return Fault(account, NoteCounts.None);
        }

        var credited = account with { Balance = account.Balance + notes.Value };

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, credited);
    }

    private static bool HasNegativeCount(NoteCounts notes) =>
        notes.Fifties < 0 || notes.Twenties < 0 || notes.Tens < 0;

    private static bool ExceedsCapacity(NoteCounts stock) =>
        stock.Fifties > CassetteCapacity || stock.Twenties > CassetteCapacity || stock.Tens > CassetteCapacity;

    // ---------- Résultats sans effet sur le compte ----------

    /// <summary>Refus : rien ne sort, aucun frais, compte inchangé.</summary>
    private static AtmResult Refuse(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);

    /// <summary>Panne du module : seuls les billets déjà sortis sont rendus, compte inchangé.</summary>
    private static AtmResult Fault(AccountState account, NoteCounts alreadyDispensed) =>
        new(AtmStatus.DispenserFault, alreadyDispensed, 0, account);
}
