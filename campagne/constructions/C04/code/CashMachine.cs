namespace Atm;

public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    private const int DailyLimit = 200;
    private const int FeeThreshold = 100;
    private const int WithdrawalFee = 5;
    private const int CassetteCapacity = 5;
    private const int MaxNotesPerPass = 4;

    private static readonly Denomination[] Denominations = [Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public AtmResult Execute(AccountState account, Operation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation switch
        {
            Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
            Operation.Deposit deposit => Deposit(account, deposit.Notes),
            _ => throw new ArgumentException($"Opération inconnue : {operation.GetType().Name}.", nameof(operation)),
        };
    }

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
        if (notes.Fifties < 0 || notes.Twenties < 0 || notes.Tens < 0 || notes.Total == 0)
        {
            return Refuse(AtmStatus.InvalidAmount, account);
        }

        if (dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)
        {
            return Refuse(AtmStatus.CassetteJammed, account);
        }

        var after = dispenser.Stock + notes;
        if (Denominations.Any(denomination => after.Of(denomination) > CassetteCapacity))
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

    /// <summary>Les billets disponibles : le stock, sans la cassette bloquée.</summary>
    private NoteCounts Available()
    {
        var stock = dispenser.Stock;
        var jammed = dispenser.Jammed;

        int Count(Denomination denomination) =>
            denomination == jammed ? 0 : Math.Max(0, stock.Of(denomination));

        return new NoteCounts(Count(Denomination.Fifty), Count(Denomination.Twenty), Count(Denomination.Ten));
    }

    /// <summary>La combinaison qui fait exactement <paramref name="amount"/> avec le moins de billets, ou <c>null</c>.</summary>
    private static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var fifties = Math.Min(available.Fifties, amount / 50); fifties >= 0; fifties--)
        {
            var afterFifties = amount - fifties * 50;
            for (var twenties = Math.Min(available.Twenties, afterFifties / 20); twenties >= 0; twenties--)
            {
                var rest = afterFifties - twenties * 20;
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

    /// <summary>Découpe la combinaison en passages d'au plus <see cref="MaxNotesPerPass"/> billets.</summary>
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
