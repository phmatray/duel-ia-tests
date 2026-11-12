namespace Atm;

/// <summary>
/// Le distributeur : exécute un retrait ou un dépôt sur un compte, sans mémoire entre deux appels.
/// Toute entrée ou sortie de billets passe par le module <see cref="IMoneyDispenser"/>.
/// </summary>
public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    private const int DailyLimit = 200;
    private const int FeeThreshold = 100;
    private const int WithdrawalFee = 5;
    private const int CassetteCapacity = 5;

    private readonly IMoneyDispenser _dispenser = dispenser ?? throw new ArgumentNullException(nameof(dispenser));

    public AtmResult Execute(AccountState account, Operation operation) => operation switch
    {
        Operation.Withdraw withdraw => Withdraw(account, withdraw.Amount),
        Operation.Deposit deposit => Deposit(account, deposit.Notes),
        null => throw new ArgumentNullException(nameof(operation)),
        _ => throw new ArgumentException($"Opération inconnue : {operation.GetType().Name}.", nameof(operation)),
    };

    // Retrait

    private AtmResult Withdraw(AccountState account, int amount)
    {
        if (amount <= 0 || amount % 10 != 0)
        {
            return Unchanged(AtmStatus.InvalidAmount, account);
        }

        if (account.WithdrawnToday + amount > DailyLimit)
        {
            return Unchanged(AtmStatus.DailyLimitExceeded, account);
        }

        var fee = FeeFor(amount);
        if (amount + fee > account.Balance)
        {
            return Unchanged(AtmStatus.InsufficientFunds, account);
        }

        var available = Banknotes.Available(_dispenser.Stock, _dispenser.Jammed);
        if (Banknotes.FewestNotes(amount, available) is not { } combination)
        {
            return Unchanged(AtmStatus.CannotDispense, account);
        }

        if (!TryDispense(combination, out var dispensedBeforeFault))
        {
            return Unchanged(AtmStatus.DispenserFault, account, dispensedBeforeFault);
        }

        var debited = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);

        return new AtmResult(AtmStatus.Ok, combination, fee, debited);
    }

    private static int FeeFor(int amount) => amount > FeeThreshold ? WithdrawalFee : 0;

    /// <summary>
    /// Sort les billets passage par passage. En cas de panne, s'arrête aussitôt et
    /// rend dans <paramref name="dispensed"/> les billets déjà sortis.
    /// </summary>
    private bool TryDispense(NoteCounts notes, out NoteCounts dispensed)
    {
        dispensed = NoteCounts.None;
        foreach (var pass in Banknotes.Passes(notes))
        {
            if (!_dispenser.Dispense(pass))
            {
                return false;
            }

            dispensed += pass;
        }

        return true;
    }

    // Dépôt

    private AtmResult Deposit(AccountState account, NoteCounts notes)
    {
        if (notes is not { Fifties: >= 0, Twenties: >= 0, Tens: >= 0, Total: not 0 })
        {
            return Unchanged(AtmStatus.InvalidAmount, account);
        }

        if (_dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)
        {
            return Unchanged(AtmStatus.CassetteJammed, account);
        }

        if (AnyCassetteOverflows(_dispenser.Stock + notes))
        {
            return Unchanged(AtmStatus.CassetteFull, account);
        }

        if (!_dispenser.Load(notes))
        {
            return Unchanged(AtmStatus.DispenserFault, account);
        }

        var credited = account with { Balance = account.Balance + notes.Value };

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, credited);
    }

    private static bool AnyCassetteOverflows(NoteCounts stock) =>
        stock.Fifties > CassetteCapacity || stock.Twenties > CassetteCapacity || stock.Tens > CassetteCapacity;

    // Résultats

    /// <summary>Refus ou panne : pas de frais, compte identique.</summary>
    private static AtmResult Unchanged(AtmStatus status, AccountState account, NoteCounts notesOut = default) =>
        new(status, notesOut, 0, account);
}
