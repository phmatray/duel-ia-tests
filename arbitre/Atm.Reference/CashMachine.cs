namespace Atm;

public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    private const int DailyLimit = 200;
    private const int FeeThreshold = 100;
    private const int FeeAmount = 5;

    public AtmResult Execute(AccountState account, Operation operation) => operation switch
    {
        Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
        Operation.Deposit deposit => Deposit(account, deposit.Notes),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private AtmResult Withdraw(AccountState account, int amount)
    {
        if (amount <= 0 || amount % 10 != 0)
        {
            return Refused(AtmStatus.InvalidAmount, account);
        }

        if (account.WithdrawnToday + amount > DailyLimit)
        {
            return Refused(AtmStatus.DailyLimitExceeded, account);
        }

        var fee = amount > FeeThreshold ? FeeAmount : 0;
        if (amount + fee > account.Balance)
        {
            return Refused(AtmStatus.InsufficientFunds, account);
        }

        if (FewestNotes(amount) is not { } plan)
        {
            return Refused(AtmStatus.CannotDispense, account);
        }

        var delivered = NoteCounts.None;
        while (delivered != plan)
        {
            var pass = NextPass(plan - delivered);
            if (!dispenser.Dispense(pass))
            {
                return new AtmResult(AtmStatus.DispenserFault, delivered, 0, account);
            }

            delivered += pass;
        }

        return new AtmResult(
            AtmStatus.Ok,
            plan,
            fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        if (notes.Total == 0)
        {
            return Refused(AtmStatus.InvalidAmount, account);
        }

        if (dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)
        {
            return Refused(AtmStatus.CassetteJammed, account);
        }

        var after = dispenser.Stock + notes;
        if (after.Fifties > MoneyDispenser.CassetteCapacity
            || after.Twenties > MoneyDispenser.CassetteCapacity
            || after.Tens > MoneyDispenser.CassetteCapacity)
        {
            return Refused(AtmStatus.CassetteFull, account);
        }

        return dispenser.Load(notes)
            ? new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value })
            : Refused(AtmStatus.DispenserFault, account);
    }

    private NoteCounts? FewestNotes(int amount)
    {
        var usable = Usable();
        NoteCounts? best = null;
        for (var fifties = 0; fifties <= usable.Fifties; fifties++)
        {
            for (var twenties = 0; twenties <= usable.Twenties; twenties++)
            {
                var tens = (amount - fifties * 50 - twenties * 20) / 10;
                if (tens < 0 || tens > usable.Tens || fifties * 50 + twenties * 20 + tens * 10 != amount)
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

    private NoteCounts Usable()
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

    private static NoteCounts NextPass(NoteCounts left)
    {
        var fifties = Math.Min(left.Fifties, MoneyDispenser.MaxNotesPerPass);
        var twenties = Math.Min(left.Twenties, MoneyDispenser.MaxNotesPerPass - fifties);
        var tens = Math.Min(left.Tens, MoneyDispenser.MaxNotesPerPass - fifties - twenties);

        return new NoteCounts(fifties, twenties, tens);
    }

    private static AtmResult Refused(AtmStatus status, AccountState account) =>
        new(status, NoteCounts.None, 0, account);
}
