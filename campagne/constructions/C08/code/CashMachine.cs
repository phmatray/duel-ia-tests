namespace Atm;

public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    private const int DailyLimit = 200;
    private const int FeeThreshold = 100;
    private const int WithdrawalFee = 5;
    private const int MaxNotesPerPass = 4;
    private const int CassetteCapacity = 5;

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
        foreach (var pass in SplitIntoPasses(combination))
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
        if (after.Fifties > CassetteCapacity || after.Twenties > CassetteCapacity || after.Tens > CassetteCapacity)
        {
            return Refuse(AtmStatus.CassetteFull, account);
        }

        if (!dispenser.Load(notes))
        {
            return new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account);
        }

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value });
    }

    /// <summary>Le stock, la cassette bloquée comptant pour zéro.</summary>
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

    /// <summary>Découpe la combinaison en passages de 4 billets au plus.</summary>
    private static IEnumerable<NoteCounts> SplitIntoPasses(NoteCounts notes)
    {
        var remaining = notes;
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
