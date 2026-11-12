namespace Atm.Tests;

/// <summary>Outils communs aux tests.</summary>
internal static class T
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Acc(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    /// <summary>0 = aucune cassette bloquée, sinon la valeur de la coupure.</summary>
    public static Denomination? Jam(int value) => value == 0 ? null : (Denomination)value;

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(int fifties, int twenties, int tens) => new Operation.Deposit(N(fifties, twenties, tens));

    public static NoteCounts Full => N(5, 5, 5);
}

/// <summary>
/// Enveloppe le vrai module : enregistre chaque appel et peut simuler une panne
/// (rendre <c>false</c> sans rien changer) au n-ième appel.
/// </summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<NoteCounts> DispenseCalls { get; } = [];
    public List<NoteCounts> SuccessfulPasses { get; } = [];
    public List<NoteCounts> LoadCalls { get; } = [];

    /// <summary>Numéro (1-based) de l'appel à Dispense qui tombe en panne.</summary>
    public int? FailDispenseAt { get; init; }

    /// <summary>Numéro (1-based) de l'appel à Load qui tombe en panne.</summary>
    public int? FailLoadAt { get; init; }

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        if (FailDispenseAt == DispenseCalls.Count)
        {
            return false;
        }

        var ok = inner.Dispense(notes);
        if (ok)
        {
            SuccessfulPasses.Add(notes);
        }

        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        if (FailLoadAt == LoadCalls.Count)
        {
            return false;
        }

        return inner.Load(notes);
    }
}

/// <summary>Un distributeur monté sur le vrai module, espionné.</summary>
internal sealed class Rig
{
    public Rig(NoteCounts stock, Denomination? jammed = null, int? failDispenseAt = null, int? failLoadAt = null)
    {
        Real = new MoneyDispenser(stock, jammed);
        Spy = new SpyDispenser(Real) { FailDispenseAt = failDispenseAt, FailLoadAt = failLoadAt };
        Machine = new CashMachine(Spy);
    }

    public MoneyDispenser Real { get; }
    public SpyDispenser Spy { get; }
    public CashMachine Machine { get; }

    public AtmResult Run(AccountState account, Operation operation) => Machine.Execute(account, operation);
}
