namespace Atm;

/// <summary>
/// Le distributeur. Il applique les règles de retrait et de dépôt, et passe par le module de billets
/// pour tout mouvement de billets. Il ne garde aucun état entre deux appels.
/// </summary>
public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    private const int DailyLimit = 200;
    private const int FeeThreshold = 100;
    private const int WithdrawalFee = 5;
    private const int MaxNotesPerPass = 4;
    private const int CassetteCapacity = 5;

    private const int Fifty = (int)Denomination.Fifty;
    private const int Twenty = (int)Denomination.Twenty;
    private const int Ten = (int)Denomination.Ten;

    private readonly IMoneyDispenser _dispenser = dispenser ?? throw new ArgumentNullException(nameof(dispenser));

    public AtmResult Execute(AccountState account, Operation operation) => operation switch
    {
        Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
        Operation.Deposit deposit => Deposit(account, deposit.Notes),
        null => throw new ArgumentNullException(nameof(operation)),
        _ => throw new ArgumentException($"Opération inconnue : {operation.GetType().Name}.", nameof(operation)),
    };

    // ---- Retrait ----

    private AtmResult Withdraw(AccountState account, int amount)
    {
        var fee = FeeFor(amount);

        if (!IsValidAmount(amount))
        {
            return Refused(AtmStatus.InvalidAmount, account);
        }

        if (account.WithdrawnToday + amount > DailyLimit)
        {
            return Refused(AtmStatus.DailyLimitExceeded, account);
        }

        if (amount + fee > account.Balance)
        {
            return Refused(AtmStatus.InsufficientFunds, account);
        }

        if (FewestNotes(amount, AvailableNotes()) is not { } combination)
        {
            return Refused(AtmStatus.CannotDispense, account);
        }

        if (!TryDispense(combination, out var dispensed))
        {
            return Fault(account, dispensed);
        }

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);

        return new AtmResult(AtmStatus.Ok, combination, fee, after);
    }

    private static bool IsValidAmount(int amount) => amount > 0 && amount % Ten == 0;

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
    /// La combinaison qui fait exactement <paramref name="amount"/> avec le moins de billets, ou <c>null</c>.
    /// <paramref name="amount"/> doit être un multiple de 10.
    /// </summary>
    private static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;

        for (var fifties = 0; fifties <= available.Fifties && fifties * Fifty <= amount; fifties++)
        {
            for (var twenties = 0; twenties <= available.Twenties && fifties * Fifty + twenties * Twenty <= amount; twenties++)
            {
                var tens = (amount - fifties * Fifty - twenties * Twenty) / Ten;
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

    /// <summary>
    /// Sort la combinaison passage par passage. S'arrête au premier passage refusé par le module ;
    /// <paramref name="dispensed"/> contient alors les billets déjà sortis.
    /// </summary>
    private bool TryDispense(NoteCounts combination, out NoteCounts dispensed)
    {
        dispensed = NoteCounts.None;

        foreach (var pass in Passes(combination))
        {
            if (!_dispenser.Dispense(pass))
            {
                return false;
            }

            dispensed += pass;
        }

        return true;
    }

    /// <summary>Découpe la combinaison en passages d'au plus quatre billets, en commençant par les plus grosses coupures.</summary>
    private static IEnumerable<NoteCounts> Passes(NoteCounts combination)
    {
        var remaining = combination;
        while (remaining.Total > 0)
        {
            var room = MaxNotesPerPass;
            var fifties = Math.Min(remaining.Fifties, room);
            room -= fifties;
            var twenties = Math.Min(remaining.Twenties, room);
            room -= twenties;
            var tens = Math.Min(remaining.Tens, room);

            var pass = new NoteCounts(fifties, twenties, tens);
            remaining -= pass;

            yield return pass;
        }
    }

    // ---- Dépôt ----

    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        if (notes.Total <= 0)
        {
            return Refused(AtmStatus.InvalidAmount, account);
        }

        if (TouchesJammedCassette(notes))
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

    private bool TouchesJammedCassette(NoteCounts notes) =>
        _dispenser.Jammed is { } jammed && notes.Of(jammed) > 0;

    private static bool ExceedsCapacity(NoteCounts stock) =>
        stock.Fifties > CassetteCapacity || stock.Twenties > CassetteCapacity || stock.Tens > CassetteCapacity;

    // ---- Résultats sans effet sur le compte ----

    /// <summary>Un refus : rien n'a bougé.</summary>
    private static AtmResult Refused(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);

    /// <summary>Une panne du module : le compte reste intact, mais des billets ont pu sortir.</summary>
    private static AtmResult Fault(AccountState account, NoteCounts dispensed) =>
        new(AtmStatus.DispenserFault, dispensed, 0, account);
}
