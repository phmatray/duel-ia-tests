namespace Atm;

public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    public const int DailyLimit = 200;
    public const int FeeThreshold = 100;
    public const int WithdrawalFee = 5;
    public const int MaxNotesPerPass = 4;
    public const int CassetteCapacity = 5;

    private static readonly Denomination[] Denominations = [Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public AtmResult Execute(AccountState account, Operation operation) => operation switch
    {
        Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
        Operation.Deposit deposit => Deposit(account, deposit.Notes),
        _ => throw new ArgumentException($"Opération inconnue : {operation}", nameof(operation)),
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
            if (!dispenser.Dispense(pass))
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

        if (dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)
        {
            return Refuse(AtmStatus.CassetteJammed, account);
        }

        var after = dispenser.Stock + notes;
        if (Denominations.Any(d => after.Of(d) > CassetteCapacity))
        {
            return Refuse(AtmStatus.CassetteFull, account);
        }

        if (!dispenser.Load(notes))
        {
            return Refuse(AtmStatus.DispenserFault, account);
        }

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value });
    }

    private static AtmResult Refuse(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);

    /// <summary>Le stock utilisable : la cassette bloquée compte pour zéro.</summary>
    private NoteCounts Available()
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

    /// <summary>La combinaison exacte qui compte le moins de billets, ou <c>null</c>.</summary>
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

    /// <summary>Découpe une combinaison en passages d'au plus <see cref="MaxNotesPerPass"/> billets.</summary>
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
