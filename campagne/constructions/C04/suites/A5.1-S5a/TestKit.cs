namespace Atm.Tests;

/// <summary>Outils communs : construction des scénarios et doublures du module de billets.</summary>
internal static class TestKit
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static AccountState Account(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    public static Operation Withdraw(int amount) => new Operation.Withdraw(amount);

    public static Operation Deposit(NoteCounts notes) => new Operation.Deposit(notes);

    /// <summary>Exécute une opération sur une machine équipée du vrai module.</summary>
    public static (AtmResult Result, MoneyDispenser Dispenser) Run(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, operation);
        return (result, dispenser);
    }

    public static Denomination? ParseJammed(int jammed) => jammed switch
    {
        0 => null,
        50 => Denomination.Fifty,
        20 => Denomination.Twenty,
        10 => Denomination.Ten,
        _ => throw new ArgumentOutOfRangeException(nameof(jammed)),
    };

    /// <summary>Vérifie qu'un refus ne change rien.</summary>
    public static void AssertRefused(
        AtmStatus expected, AtmResult result, AccountState account, NoteCounts stockBefore, IMoneyDispenser dispenser)
    {
        Assert.Equal(expected, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stockBefore, dispenser.Stock);
    }

    /// <summary>Tous les stocks du domaine : 0 à 5 billets par cassette.</summary>
    public static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return N(f, t, d);
    }

    public static readonly Denomination?[] AllJams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    /// <summary>Les montants du domaine : -10, 0, 5 et les multiples de 10 de 10 à 220.</summary>
    public static IEnumerable<int> AllAmounts()
    {
        yield return -10;
        yield return 0;
        yield return 5;
        for (var a = 10; a <= 220; a += 10)
            yield return a;
    }

    /// <summary>Les dépôts du domaine : 0 ou 1 billet par coupure.</summary>
    public static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return N(f, t, d);
    }
}

/// <summary>Délègue au vrai module et enregistre chaque passage accepté ou refusé.</summary>
internal sealed class SpyDispenser(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<(NoteCounts Notes, bool Accepted)> Dispenses { get; } = [];
    public List<(NoteCounts Notes, bool Accepted)> Loads { get; } = [];

    public NoteCounts Stock => inner.Stock;
    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var ok = inner.Dispense(notes);
        Dispenses.Add((notes, ok));
        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        var ok = inner.Load(notes);
        Loads.Add((notes, ok));
        return ok;
    }
}

/// <summary>
/// Module en panne : il accepte les <c>acceptedPasses</c> premiers passages de sortie, puis rend <c>false</c>.
/// <c>Load</c> rend toujours <c>loadResult</c>.
/// </summary>
internal sealed class FaultyDispenser(NoteCounts stock, Denomination? jammed, int acceptedPasses, bool loadResult = false)
    : IMoneyDispenser
{
    public List<NoteCounts> AcceptedPasses { get; } = [];
    public int DispenseCalls { get; private set; }
    public int LoadCalls { get; private set; }

    public NoteCounts Stock { get; private set; } = stock;
    public Denomination? Jammed { get; } = jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls++;
        if (AcceptedPasses.Count >= acceptedPasses)
            return false;
        AcceptedPasses.Add(notes);
        Stock -= notes;
        return true;
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls++;
        if (loadResult)
            Stock += notes;
        return loadResult;
    }
}

/// <summary>Oracle indépendant, écrit directement depuis la spec.</summary>
internal static class Oracle
{
    public static int Fee(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => new(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    /// <summary>Recherche exhaustive de la combinaison exacte qui compte le moins de billets.</summary>
    public static NoteCounts? Fewest(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var c = new NoteCounts(f, t, d);
            if (c.Value == amount && (best is null || c.Total < best.Value.Total))
                best = c;
        }
        return best;
    }

    public static AtmStatus WithdrawStatus(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        if (amount <= 0 || amount % 10 != 0) return AtmStatus.InvalidAmount;
        if (account.WithdrawnToday + amount > 200) return AtmStatus.DailyLimitExceeded;
        if (amount + Fee(amount) > account.Balance) return AtmStatus.InsufficientFunds;
        if (Fewest(amount, Available(stock, jammed)) is null) return AtmStatus.CannotDispense;
        return AtmStatus.Ok;
    }

    public static AtmStatus DepositStatus(NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        if (notes.Total == 0) return AtmStatus.InvalidAmount;
        if (jammed is { } j && notes.Of(j) > 0) return AtmStatus.CassetteJammed;
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return AtmStatus.CassetteFull;
        return AtmStatus.Ok;
    }
}
