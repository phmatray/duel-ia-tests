namespace Atm;

/// <summary>
/// Le distributeur : applique les règles de retrait et de dépôt de la spécification,
/// et fait passer tout mouvement de billets par le module <see cref="IMoneyDispenser"/>.
/// </summary>
public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    private const int DailyLimit = 200;
    private const int FeeThreshold = 100;
    private const int WithdrawalFee = 5;
    private const int MaxNotesPerPass = 4;
    private const int CassetteCapacity = 5;
    private const int SmallestNote = (int)Denomination.Ten;

    public AtmResult Execute(AccountState account, Operation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation switch
        {
            Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
            Operation.Deposit deposit => Deposit(account, deposit.Notes),
            _ => throw new ArgumentException($"Opération inconnue : {operation.GetType().Name}", nameof(operation)),
        };
    }

    // ----- Retrait -----

    private AtmResult Withdraw(AccountState account, int amount)
    {
        // 1. Montant
        if (amount <= 0 || amount % SmallestNote != 0)
        {
            return Refused(AtmStatus.InvalidAmount, account);
        }

        // 2. Plafond du jour (les frais n'y comptent pas)
        if (account.WithdrawnToday + amount > DailyLimit)
        {
            return Refused(AtmStatus.DailyLimitExceeded, account);
        }

        // 3. Solde, frais compris
        var fee = FeeFor(amount);
        if (amount + fee > account.Balance)
        {
            return Refused(AtmStatus.InsufficientFunds, account);
        }

        // 4. Combinaison de billets disponibles
        if (FewestNotes(amount, AvailableNotes()) is not { } notes)
        {
            return Refused(AtmStatus.CannotDispense, account);
        }

        if (!TryDispense(notes, out var dispensed))
        {
            return Faulted(account, dispensed);
        }

        var debited = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);

        return Succeeded(notes, fee, debited);
    }

    private static int FeeFor(int amount) => amount > FeeThreshold ? WithdrawalFee : 0;

    /// <summary>Le stock, la cassette bloquée comptant pour zéro.</summary>
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

        for (var fifties = Math.Min(available.Fifties, amount / 50); fifties >= 0; fifties--)
        {
            var afterFifties = amount - fifties * 50;

            for (var twenties = Math.Min(available.Twenties, afterFifties / 20); twenties >= 0; twenties--)
            {
                var rest = afterFifties - twenties * 20;
                if (rest % 10 != 0 || rest / 10 > available.Tens)
                {
                    continue;
                }

                var candidate = new NoteCounts(fifties, twenties, rest / 10);
                if (best is null || candidate.Total < best.Value.Total)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Sort <paramref name="notes"/> en passages successifs. S'arrête au premier passage refusé ;
    /// <paramref name="dispensed"/> contient alors les billets déjà sortis.
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

    /// <summary>Découpe les billets en passages de <see cref="MaxNotesPerPass"/> billets au plus, les plus grosses coupures d'abord.</summary>
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

        static int Take(int wanted, ref int room)
        {
            var taken = Math.Min(wanted, room);
            room -= taken;

            return taken;
        }
    }

    // ----- Dépôt -----

    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        // 1. Au moins un billet (et aucun nombre négatif)
        if (notes is not { Fifties: >= 0, Twenties: >= 0, Tens: >= 0, Total: > 0 })
        {
            return Refused(AtmStatus.InvalidAmount, account);
        }

        // 2. Cassette bloquée
        if (dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)
        {
            return Refused(AtmStatus.CassetteJammed, account);
        }

        // 3. Capacité des cassettes
        if (ExceedsCapacity(dispenser.Stock + notes))
        {
            return Refused(AtmStatus.CassetteFull, account);
        }

        if (!dispenser.Load(notes))
        {
            return Faulted(account, dispensed: NoteCounts.None);
        }

        var credited = account with { Balance = account.Balance + notes.Value };

        return Succeeded(NoteCounts.None, fee: 0, credited);
    }

    private static bool ExceedsCapacity(NoteCounts stock) =>
        stock.Fifties > CassetteCapacity || stock.Twenties > CassetteCapacity || stock.Tens > CassetteCapacity;

    // ----- Résultats -----

    /// <summary>Refus : rien ne sort, aucun frais, compte inchangé.</summary>
    private static AtmResult Refused(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);

    /// <summary>Panne du module : seuls les billets déjà sortis sont rendus, aucun frais, compte inchangé.</summary>
    private static AtmResult Faulted(AccountState account, NoteCounts dispensed) =>
        new(AtmStatus.DispenserFault, dispensed, 0, account);

    private static AtmResult Succeeded(NoteCounts notes, int fee, AccountState account) =>
        new(AtmStatus.Ok, notes, fee, account);
}
