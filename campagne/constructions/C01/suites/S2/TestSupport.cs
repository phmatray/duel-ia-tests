namespace Atm.Tests;

/// <summary>Un module qui enregistre les appels et peut tomber en panne à la demande.</summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser real = new(stock, jammed);

    /// <summary>Le passage (indice à partir de 0) qui échoue, ou -1.</summary>
    public int FailDispenseAt { get; init; } = -1;

    public bool FailLoad { get; init; }

    public List<NoteCounts> Dispensed { get; } = [];

    public List<NoteCounts> Loaded { get; } = [];

    public NoteCounts Stock => real.Stock;

    public Denomination? Jammed => real.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        if (Dispensed.Count == FailDispenseAt)
        {
            Dispensed.Add(default);
            return false;
        }

        Dispensed.Add(notes);
        return real.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        Loaded.Add(notes);
        return !FailLoad && real.Load(notes);
    }
}

/// <summary>Un modèle de référence, écrit indépendamment, directement depuis la spécification.</summary>
internal static class Oracle
{
    public static AtmResult Expected(AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        AtmResult Refuse(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        switch (operation)
        {
            case Operation.Withdraw(var amount):
            {
                if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
                if (account.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
                var fee = amount > 100 ? 5 : 0;
                if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds);

                int Avail(Denomination d) => jammed == d ? 0 : stock.Of(d);
                NoteCounts? best = null;
                for (var f = 0; f <= Avail(Denomination.Fifty); f++)
                for (var t = 0; t <= Avail(Denomination.Twenty); t++)
                for (var c = 0; c <= Avail(Denomination.Ten); c++)
                {
                    var n = new NoteCounts(f, t, c);
                    if (n.Value == amount && (best is null || n.Total < best.Value.Total)) best = n;
                }

                if (best is not { } notes) return Refuse(AtmStatus.CannotDispense);
                return new AtmResult(AtmStatus.Ok, notes, fee,
                    new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
            }

            case Operation.Deposit(var notes):
            {
                if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
                if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
                var after = stock + notes;
                if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);
                return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0,
                    account with { Balance = account.Balance + notes.Value });
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}
