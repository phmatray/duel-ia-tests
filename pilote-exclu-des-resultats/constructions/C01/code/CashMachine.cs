namespace Atm;

public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    private const int DailyLimit = 200;
    private const int FeeThreshold = 100;
    private const int WithdrawalFee = 5;
    private const int MaxNotesPerPass = 4;
    private const int CassetteCapacity = 5;

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

        var fee = amount > FeeThreshold ? WithdrawalFee : 0;
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
        if (notes.Total <= 0)
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
            return Refuse(AtmStatus.DispenserFault, account);
        }

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value });
    }

    /// <summary>Le stock utilisable : la cassette bloquée compte pour zéro.</summary>
    private NoteCounts Available()
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

    /// <summary>La combinaison qui fait exactement <paramref name="amount"/> avec le moins de billets, ou <c>null</c>.</summary>
    private static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var fifties = 0; fifties <= available.Fifties && fifties * 50 <= amount; fifties++)
        {
            for (var twenties = 0; twenties <= available.Twenties && fifties * 50 + twenties * 20 <= amount; twenties++)
            {
                var rest = amount - fifties * 50 - twenties * 20;
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

    /// <summary>Découpe la combinaison en passages d'au plus quatre billets.</summary>
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

    private static AtmResult Refuse(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);
}
