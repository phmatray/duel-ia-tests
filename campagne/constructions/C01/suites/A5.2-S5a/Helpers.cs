namespace Atm.Tests;

/// <summary>Outils communs : construction de la machine avec le vrai module, oracle de la spec.</summary>
internal static class Helpers
{
    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static Denomination? J(int jammed) => jammed switch
    {
        0 => null,
        50 => Denomination.Fifty,
        20 => Denomination.Twenty,
        10 => Denomination.Ten,
        _ => throw new ArgumentOutOfRangeException(nameof(jammed)),
    };

    public static (AtmResult Result, MoneyDispenser Dispenser) Run(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var machine = new CashMachine(dispenser);
        var result = machine.Execute(new AccountState(balance, withdrawnToday), operation);
        return (result, dispenser);
    }

    public static readonly NoteCounts Full = new(5, 5, 5);

    /// <summary>Tous les stocks du domaine : 0 à 5 billets par cassette.</summary>
    public static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return new NoteCounts(f, t, d);
    }

    public static readonly Denomination?[] AllJammed = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    /// <summary>Les montants du domaine : -10, 0, 5 et les multiples de 10 de 10 à 220.</summary>
    public static IEnumerable<int> AllAmounts()
    {
        yield return -10;
        yield return 0;
        yield return 5;
        for (var a = 10; a <= 220; a += 10) yield return a;
    }

    /// <summary>Les dépôts du domaine : 0 ou 1 billet par coupure.</summary>
    public static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return new NoteCounts(f, t, d);
    }

    public static int FeeFor(int amount) => amount > 100 ? 5 : 0;

    /// <summary>Billets disponibles selon la spec : le stock, la cassette bloquée comptant pour zéro.</summary>
    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => new(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    /// <summary>Oracle par force brute : la combinaison exacte qui compte le moins de billets.</summary>
    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
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

    /// <summary>Résultat attendu d'un retrait, d'après la spec.</summary>
    public static AtmResult ExpectedWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
        var fee = FeeFor(amount);
        if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds);
        if (FewestNotes(amount, Available(stock, jammed)) is not { } combination) return Refuse(AtmStatus.CannotDispense);
        return new AtmResult(AtmStatus.Ok, combination, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    /// <summary>Résultat attendu d'un dépôt, d'après la spec.</summary>
    public static AtmResult ExpectedDeposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        AtmResult Refuse(AtmStatus s) => new(s, NoteCounts.None, 0, account);

        if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);
        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value });
    }
}
