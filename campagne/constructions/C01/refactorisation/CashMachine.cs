namespace Atm;

/// <summary>
/// Le distributeur de billets. Il applique les règles de retrait et de dépôt de la spécification
/// et fait passer tout mouvement de billets par le module <see cref="IMoneyDispenser"/>.
/// Il ne garde aucun état entre deux appels.
/// </summary>
public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    public const int DailyLimit = 200;
    public const int FeeThreshold = 100;
    public const int WithdrawalFee = 5;
    public const int MaxNotesPerPass = 4;
    public const int CassetteCapacity = 5;

    private const int SmallestNote = (int)Denomination.Ten;

    public AtmResult Execute(AccountState account, Operation operation) => operation switch
    {
        Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
        Operation.Deposit deposit => Deposit(account, deposit.Notes),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    // ---- Retrait ----------------------------------------------------------------------------------

    /// <summary>Contrôles dans l'ordre de la spécification, puis sortie des billets par passages.</summary>
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

        return Success(after, notes, fee);
    }

    /// <summary>Un montant retirable est strictement positif et multiple de la plus petite coupure.</summary>
    private static bool IsValidAmount(int amount) => amount > 0 && amount % SmallestNote == 0;

    /// <summary>Les frais ne s'appliquent qu'au-delà du seuil, et ne comptent pas dans le plafond du jour.</summary>
    private static int FeeFor(int amount) => amount > FeeThreshold ? WithdrawalFee : 0;

    // ---- Dépôt ------------------------------------------------------------------------------------

    /// <summary>Contrôles dans l'ordre de la spécification, puis rangement des billets en un seul appel.</summary>
    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        if (!IsValidDeposit(notes))
        {
            return Refuse(AtmStatus.InvalidAmount, account);
        }

        if (TouchesJammedCassette(notes))
        {
            return Refuse(AtmStatus.CassetteJammed, account);
        }

        if (ExceedsCapacity(dispenser.Stock + notes))
        {
            return Refuse(AtmStatus.CassetteFull, account);
        }

        if (!dispenser.Load(notes))
        {
            return Fault(account, NoteCounts.None);
        }

        return Success(account with { Balance = account.Balance + notes.Value }, NoteCounts.None, fee: 0);
    }

    /// <summary>Un dépôt valide contient au moins un billet et aucun compte négatif.</summary>
    private static bool IsValidDeposit(NoteCounts notes) =>
        notes is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0, Total: > 0 };

    private bool TouchesJammedCassette(NoteCounts notes) =>
        dispenser.Jammed is { } jammed && notes.Of(jammed) > 0;

    private static bool ExceedsCapacity(NoteCounts stock) =>
        stock.Fifties > CassetteCapacity || stock.Twenties > CassetteCapacity || stock.Tens > CassetteCapacity;

    // ---- Résultats --------------------------------------------------------------------------------

    private static AtmResult Success(AccountState after, NoteCounts notes, int fee) =>
        new(AtmStatus.Ok, notes, fee, after);

    /// <summary>Un refus : rien ne sort, aucun frais, compte inchangé.</summary>
    private static AtmResult Refuse(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);

    /// <summary>Une panne du module : on rend les billets déjà sortis, sans frais, compte inchangé.</summary>
    private static AtmResult Fault(AccountState account, NoteCounts alreadyDispensed) =>
        new(AtmStatus.DispenserFault, alreadyDispensed, 0, account);

    // ---- Calcul des billets -----------------------------------------------------------------------

    /// <summary>Le stock utilisable : la cassette bloquée compte pour zéro, un compte négatif aussi.</summary>
    private NoteCounts AvailableNotes()
    {
        var stock = dispenser.Stock;
        var jammed = dispenser.Jammed;

        int Usable(Denomination denomination) =>
            denomination == jammed ? 0 : Math.Max(0, stock.Of(denomination));

        return new NoteCounts(
            Usable(Denomination.Fifty),
            Usable(Denomination.Twenty),
            Usable(Denomination.Ten));
    }

    /// <summary>
    /// La combinaison qui fait exactement <paramref name="amount"/> avec le moins de billets, ou <c>null</c>.
    /// <paramref name="amount"/> doit être un multiple de 10 : le reste après les 50 et les 20 l'est donc aussi.
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

    /// <summary>Découpe les billets en passages d'au plus <see cref="MaxNotesPerPass"/>, les grosses coupures d'abord.</summary>
    private static IEnumerable<NoteCounts> SplitIntoPasses(NoteCounts notes)
    {
        var remaining = notes;
        while (remaining.Total > 0)
        {
            var room = MaxNotesPerPass;
            int Take(int count)
            {
                var taken = Math.Min(room, count);
                room -= taken;
                return taken;
            }

            var pass = new NoteCounts(Take(remaining.Fifties), Take(remaining.Twenties), Take(remaining.Tens));
            remaining -= pass;

            yield return pass;
        }
    }
}
