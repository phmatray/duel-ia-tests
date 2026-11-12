namespace Atm;

/// <summary>
/// Le distributeur. Sans mémoire : chaque appel ne dépend que du compte, de l'opération et du module de billets.
/// </summary>
public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    public const int DailyLimit = 200;
    public const int FeeThreshold = 100;
    public const int HighAmountFee = 5;
    public const int CassetteCapacity = 5;
    public const int MaxNotesPerPass = 4;

    /// <summary>La plus petite coupure : un montant retiré doit en être un multiple.</summary>
    private const int SmallestNote = (int)Denomination.Ten;

    private readonly IMoneyDispenser _dispenser = dispenser ?? throw new ArgumentNullException(nameof(dispenser));

    public AtmResult Execute(AccountState account, Operation operation) => operation switch
    {
        Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
        Operation.Deposit deposit => Deposit(account, deposit.Notes),
        null => throw new ArgumentNullException(nameof(operation)),
        _ => throw new ArgumentException($"Opération inconnue : {operation.GetType().Name}.", nameof(operation)),
    };

    // ---- Retrait ----

    /// <summary>Les contrôles se font dans l'ordre de la spécification ; le premier qui échoue donne le statut.</summary>
    private AtmResult Withdraw(AccountState account, int amount)
    {
        if (amount <= 0 || amount % SmallestNote != 0)
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

        if (NotePlanner.FewestNotes(amount, AvailableNotes()) is not { } notes)
        {
            return Refuse(AtmStatus.CannotDispense, account);
        }

        if (!TryDispense(notes, out var dispensed))
        {
            return Fault(account, dispensed);
        }

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);

        return new AtmResult(AtmStatus.Ok, notes, fee, after);
    }

    /// <summary>Les frais ne s'appliquent qu'au-delà du seuil, et ne comptent pas dans le plafond du jour.</summary>
    private static int FeeFor(int amount) => amount > FeeThreshold ? HighAmountFee : 0;

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
    /// Sort les billets en plusieurs passages et s'arrête au premier que le module refuse.
    /// <paramref name="dispensed"/> contient les billets effectivement sortis.
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

    // ---- Dépôt ----

    /// <summary>Les contrôles se font dans l'ordre de la spécification ; le premier qui échoue donne le statut.</summary>
    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        if (notes is not { Total: > 0, Fifties: >= 0, Twenties: >= 0, Tens: >= 0 })
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

        var after = account with { Balance = account.Balance + notes.Value };

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, after);
    }

    private static bool ExceedsCapacity(NoteCounts stock) =>
        stock.Fifties > CassetteCapacity || stock.Twenties > CassetteCapacity || stock.Tens > CassetteCapacity;

    // ---- Résultats sans effet sur le compte ----

    /// <summary>Un refus : aucun billet, aucun frais, compte identique.</summary>
    private static AtmResult Refuse(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);

    /// <summary>Une panne du module : on rend les billets déjà sortis, sans frais, compte identique.</summary>
    private static AtmResult Fault(AccountState account, NoteCounts alreadyDispensed) =>
        new(AtmStatus.DispenserFault, alreadyDispensed, 0, account);
}
