namespace Atm;

/// <summary>
/// Distributeur de billets sans mémoire : le résultat ne dépend que du compte, de l'opération et du module de billets.
/// </summary>
public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    private const int DailyLimit = 200;
    private const int FeeThreshold = 100;
    private const int WithdrawalFee = 5;
    private const int CassetteCapacity = 5;
    private const int MaxNotesPerPass = 4;

    private const int Fifty = (int)Denomination.Fifty;
    private const int Twenty = (int)Denomination.Twenty;
    private const int Ten = (int)Denomination.Ten;

    public AtmResult Execute(AccountState account, Operation operation) => operation switch
    {
        Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
        Operation.Deposit deposit => Deposit(account, deposit.Notes),
        _ => throw new ArgumentException("Opération inconnue.", nameof(operation)),
    };

    // ---- Retrait ----

    /// <summary>Contrôles dans l'ordre de la spécification, puis sortie des billets en plusieurs passages.</summary>
    private AtmResult Withdraw(AccountState account, int amount)
    {
        if (amount <= 0 || amount % Ten != 0)
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

        var dispensed = NoteCounts.None;
        foreach (var pass in SplitIntoPasses(notes))
        {
            if (!dispenser.Dispense(pass))
            {
                return Fault(account, dispensed);
            }

            dispensed += pass;
        }

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);

        return new AtmResult(AtmStatus.Ok, notes, fee, after);
    }

    /// <summary>Les frais ne s'appliquent qu'au-delà du seuil ; ils ne comptent pas dans le plafond du jour.</summary>
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

    /// <summary>La combinaison qui fait exactement <paramref name="amount"/> avec le moins de billets, ou <c>null</c>.</summary>
    private static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        var candidates = ExactCombinations(amount, available).ToList();

        // En cas d'égalité, MinBy garde la première, donc celle qui a le plus de gros billets.
        return candidates.Count == 0 ? null : candidates.MinBy(combination => combination.Total);
    }

    /// <summary>
    /// Toutes les combinaisons de billets disponibles qui font exactement <paramref name="amount"/>,
    /// du plus grand nombre de billets de 50 au plus petit, puis de même pour les billets de 20.
    /// </summary>
    private static IEnumerable<NoteCounts> ExactCombinations(int amount, NoteCounts available)
    {
        for (var fifties = Math.Min(available.Fifties, amount / Fifty); fifties >= 0; fifties--)
        {
            var afterFifties = amount - fifties * Fifty;
            for (var twenties = Math.Min(available.Twenties, afterFifties / Twenty); twenties >= 0; twenties--)
            {
                var rest = afterFifties - twenties * Twenty;
                if (rest % Ten == 0 && rest / Ten <= available.Tens)
                {
                    yield return new NoteCounts(fifties, twenties, rest / Ten);
                }
            }
        }
    }

    /// <summary>Découpe les billets en passages d'au plus <see cref="MaxNotesPerPass"/> billets.</summary>
    private static IEnumerable<NoteCounts> SplitIntoPasses(NoteCounts notes)
    {
        var remaining = notes;
        while (remaining.Total > 0)
        {
            var pass = NextPass(remaining);
            remaining -= pass;

            yield return pass;
        }
    }

    /// <summary>Remplit un passage en prenant d'abord les billets de 50, puis de 20, puis de 10.</summary>
    private static NoteCounts NextPass(NoteCounts remaining)
    {
        var fifties = Math.Min(remaining.Fifties, MaxNotesPerPass);
        var twenties = Math.Min(remaining.Twenties, MaxNotesPerPass - fifties);
        var tens = Math.Min(remaining.Tens, MaxNotesPerPass - fifties - twenties);

        return new NoteCounts(fifties, twenties, tens);
    }

    // ---- Dépôt ----

    /// <summary>Contrôles dans l'ordre de la spécification, puis rangement des billets.</summary>
    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        if (notes.Fifties < 0 || notes.Twenties < 0 || notes.Tens < 0 || notes.Total == 0)
        {
            return Refused(AtmStatus.InvalidAmount, account);
        }

        if (dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)
        {
            return Refused(AtmStatus.CassetteJammed, account);
        }

        if (!FitsInCassettes(dispenser.Stock + notes))
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

    private static bool FitsInCassettes(NoteCounts stock) =>
        stock.Fifties <= CassetteCapacity && stock.Twenties <= CassetteCapacity && stock.Tens <= CassetteCapacity;

    // ---- Résultats d'échec ----

    /// <summary>Refus : rien ne change, aucun billet ne sort, aucun frais.</summary>
    private static AtmResult Refused(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);

    /// <summary>Panne du module : compte inchangé, sans frais, avec les billets déjà sortis.</summary>
    private static AtmResult Fault(AccountState account, NoteCounts alreadyDispensed) =>
        new(AtmStatus.DispenserFault, alreadyDispensed, 0, account);
}
