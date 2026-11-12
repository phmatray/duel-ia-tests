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

        if (FewestNotes(amount, Available()) is not { } combination)
        {
            return Refuse(AtmStatus.CannotDispense, account);
        }

        var dispensed = NoteCounts.None;
        foreach (var pass in Passes(combination))
        {
            if (!_dispenser.Dispense(pass))
            {
                return new AtmResult(AtmStatus.DispenserFault, dispensed, 0, account);
            }

            dispensed += pass;
        }

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
        return new AtmResult(AtmStatus.Ok, combination, fee, after);
    }

    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        if (notes.Total <= 0 || notes.Fifties < 0 || notes.Twenties < 0 || notes.Tens < 0)
        {
            return Refuse(AtmStatus.InvalidAmount, account);
        }

        if (_dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)
        {
            return Refuse(AtmStatus.CassetteJammed, account);
        }

        var after = _dispenser.Stock + notes;
        if (after.Fifties > CassetteCapacity || after.Twenties > CassetteCapacity || after.Tens > CassetteCapacity)
        {
            return Refuse(AtmStatus.CassetteFull, account);
        }

        if (!_dispenser.Load(notes))
        {
            return new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account);
        }

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value });
    }

    private static int FeeFor(int amount) => amount > FeeThreshold ? WithdrawalFee : 0;

    private static AtmResult Refuse(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);

    /// <summary>Le stock utilisable : la cassette bloquée compte pour zéro.</summary>
    private NoteCounts Available()
    {
        var stock = _dispenser.Stock;
        var jammed = _dispenser.Jammed;
        int Usable(Denomination d) => jammed == d ? 0 : Math.Max(0, stock.Of(d));
        return new NoteCounts(Usable(Denomination.Fifty), Usable(Denomination.Twenty), Usable(Denomination.Ten));
    }

    /// <summary>La combinaison qui fait exactement <paramref name="amount"/> avec le moins de billets, ou null.</summary>
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
                if (best is not { } current || candidate.Total < current.Total)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    /// <summary>Découpe la combinaison en passages d'au plus 4 billets.</summary>
    private static IEnumerable<NoteCounts> Passes(NoteCounts notes)
    {
        var remaining = notes;
        while (remaining.Total > 0)
        {
            var room = MaxNotesPerPass;
            var fifties = Math.Min(room, remaining.Fifties);
            room -= fifties;
            var twenties = Math.Min(room, remaining.Twenties);
            room -= twenties;
            var tens = Math.Min(room, remaining.Tens);
            var pass = new NoteCounts(fifties, twenties, tens);
            remaining -= pass;
            yield return pass;
        }
    }
}
