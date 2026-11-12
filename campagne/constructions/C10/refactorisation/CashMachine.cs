namespace Atm;

/// <summary>
/// Distributeur de billets sans mémoire : chaque opération ne dépend que du compte reçu, de l'opération
/// et du module de billets. Toute entrée ou sortie de billets passe par <see cref="IMoneyDispenser"/>.
/// </summary>
public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    public const int DailyLimit = 200;
    public const int FeeThreshold = 100;
    public const int WithdrawalFee = 5;
    public const int MaxNotesPerPass = 4;
    public const int CassetteCapacity = 5;

    private const int SmallestNote = (int)Denomination.Ten;

    private static readonly Denomination[] Denominations = [Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public AtmResult Execute(AccountState account, Operation operation) => operation switch
    {
        Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
        Operation.Deposit deposit => Deposit(account, deposit.Notes),
        _ => throw new ArgumentException($"Opération inconnue : {operation}", nameof(operation)),
    };

    // ---------- Retrait ----------

    /// <summary>Contrôles dans l'ordre de la spécification ; le premier qui échoue donne le statut.</summary>
    private AtmResult Withdraw(AccountState account, int amount)
    {
        if (!IsValidAmount(amount))
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
            return Fault(account, dispensed);
        }

        var debited = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);

        return new AtmResult(AtmStatus.Ok, notes, fee, debited);
    }

    private static bool IsValidAmount(int amount) => amount > 0 && amount % SmallestNote == 0;

    /// <summary>Les frais ne s'appliquent qu'au-delà du seuil, et ne comptent pas dans le plafond du jour.</summary>
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
    /// Parmi les combinaisons de <paramref name="available"/> qui font exactement <paramref name="amount"/>,
    /// celle qui compte le moins de billets, ou <c>null</c> s'il n'y en a aucune.
    /// </summary>
    private static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var fifties = 0; fifties <= available.Fifties && fifties * 50 <= amount; fifties++)
        {
            for (var twenties = 0; twenties <= available.Twenties && fifties * 50 + twenties * 20 <= amount; twenties++)
            {
                var rest = amount - fifties * 50 - twenties * 20;
                var tens = rest / 10;
                if (rest % 10 != 0 || tens > available.Tens)
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

    /// <summary>
    /// Sort <paramref name="notes"/> en autant de passages que nécessaire et s'arrête au premier refus du module.
    /// <paramref name="dispensed"/> contient les billets effectivement sortis.
    /// </summary>
    private bool TryDispense(NoteCounts notes, out NoteCounts dispensed)
    {
        dispensed = NoteCounts.None;
        foreach (var pass in Passes(notes))
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
    private static IEnumerable<NoteCounts> Passes(NoteCounts notes)
    {
        var remaining = notes;
        while (remaining.Total > 0)
        {
            var pass = TakeLargestFirst(remaining, MaxNotesPerPass);
            remaining -= pass;

            yield return pass;
        }
    }

    /// <summary>Prend jusqu'à <paramref name="count"/> billets, en commençant par les plus grosses coupures.</summary>
    private static NoteCounts TakeLargestFirst(NoteCounts notes, int count)
    {
        var fifties = Math.Min(count, notes.Fifties);
        var twenties = Math.Min(count - fifties, notes.Twenties);
        var tens = Math.Min(count - fifties - twenties, notes.Tens);

        return new NoteCounts(fifties, twenties, tens);
    }

    // ---------- Dépôt ----------

    /// <summary>Contrôles dans l'ordre de la spécification ; le premier qui échoue donne le statut.</summary>
    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        if (notes.Total <= 0)
        {
            return Refuse(AtmStatus.InvalidAmount, account);
        }

        if (TouchesJammedCassette(notes))
        {
            return Refuse(AtmStatus.CassetteJammed, account);
        }

        if (WouldOverfill(notes))
        {
            return Refuse(AtmStatus.CassetteFull, account);
        }

        if (!dispenser.Load(notes))
        {
            return Fault(account, NoteCounts.None);
        }

        var credited = account with { Balance = account.Balance + notes.Value };

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, credited);
    }

    private bool TouchesJammedCassette(NoteCounts notes) =>
        dispenser.Jammed is { } jammed && notes.Of(jammed) > 0;

    private bool WouldOverfill(NoteCounts notes)
    {
        var after = dispenser.Stock + notes;

        return Denominations.Any(denomination => after.Of(denomination) > CassetteCapacity);
    }

    // ---------- Résultats sans effet sur le compte ----------

    /// <summary>Refus : aucun billet, aucun frais, compte identique.</summary>
    private static AtmResult Refuse(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);

    /// <summary>Panne du module : on rend les billets déjà sortis, sans frais, compte identique.</summary>
    private static AtmResult Fault(AccountState account, NoteCounts dispensed) =>
        new(AtmStatus.DispenserFault, dispensed, 0, account);
}
