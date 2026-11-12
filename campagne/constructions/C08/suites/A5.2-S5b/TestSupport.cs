namespace Atm.Tests;

/// <summary>Outils communs : construction d'une machine avec le vrai module de billets.</summary>
internal static class Harness
{
    public static NoteCounts N(int fifties = 0, int twenties = 0, int tens = 0) => new(fifties, twenties, tens);

    public static readonly NoteCounts FullStock = N(5, 5, 5);

    public static (AtmResult Result, MoneyDispenser Dispenser) Run(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var machine = new CashMachine(dispenser);
        var result = machine.Execute(new AccountState(balance, withdrawnToday), operation);
        return (result, dispenser);
    }

    public static (AtmResult Result, MoneyDispenser Dispenser) Withdraw(
        int balance, int withdrawnToday, NoteCounts stock, int amount, Denomination? jammed = null) =>
        Run(balance, withdrawnToday, stock, jammed, new Operation.Withdraw(amount));

    public static (AtmResult Result, MoneyDispenser Dispenser) Deposit(
        int balance, int withdrawnToday, NoteCounts stock, NoteCounts notes, Denomination? jammed = null) =>
        Run(balance, withdrawnToday, stock, jammed, new Operation.Deposit(notes));

    /// <summary>Vérifie un refus : rien ne change, ni le compte, ni les billets.</summary>
    public static void AssertRefused(
        AtmStatus expected, (AtmResult Result, MoneyDispenser Dispenser) run,
        int balance, int withdrawnToday, NoteCounts stock)
    {
        Assert.Equal(expected, run.Result.Status);
        Assert.Equal(NoteCounts.None, run.Result.Notes);
        Assert.Equal(0, run.Result.Fee);
        Assert.Equal(new AccountState(balance, withdrawnToday), run.Result.Account);
        Assert.Equal(stock, run.Dispenser.Stock);
    }

    /// <summary>Vérifie un retrait réussi selon la spec.</summary>
    public static void AssertWithdrawOk(
        (AtmResult Result, MoneyDispenser Dispenser) run,
        int balance, int withdrawnToday, NoteCounts stock, int amount, NoteCounts expectedNotes, int expectedFee)
    {
        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.Equal(expectedNotes, run.Result.Notes);
        Assert.Equal(expectedFee, run.Result.Fee);
        Assert.Equal(new AccountState(balance - amount - expectedFee, withdrawnToday + amount), run.Result.Account);
        Assert.Equal(stock - expectedNotes, run.Dispenser.Stock);
    }

    /// <summary>Vérifie un dépôt réussi selon la spec.</summary>
    public static void AssertDepositOk(
        (AtmResult Result, MoneyDispenser Dispenser) run,
        int balance, int withdrawnToday, NoteCounts stock, NoteCounts deposit)
    {
        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.Equal(NoteCounts.None, run.Result.Notes);
        Assert.Equal(0, run.Result.Fee);
        Assert.Equal(new AccountState(balance + deposit.Value, withdrawnToday), run.Result.Account);
        Assert.Equal(stock + deposit, run.Dispenser.Stock);
    }
}

/// <summary>
/// Oracle écrit d'après la spec, utilisé par les tests exhaustifs sur le domaine.
/// </summary>
internal static class SpecOracle
{
    public const int DailyLimit = 200;
    public const int Capacity = 5;

    public static int Fee(int amount) => amount > 100 ? 5 : 0;

    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    public static NoteCounts? FewestNotes(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        {
            for (var t = 0; t <= available.Twenties; t++)
            {
                var rest = amount - 50 * f - 20 * t;
                if (rest < 0 || rest % 10 != 0 || rest / 10 > available.Tens)
                {
                    continue;
                }

                var candidate = new NoteCounts(f, t, rest / 10);
                if (best is null || candidate.Total < best.Value.Total)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    public static (AtmStatus Status, NoteCounts Notes, int Fee) Withdraw(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount)
    {
        if (amount <= 0 || amount % 10 != 0)
        {
            return (AtmStatus.InvalidAmount, NoteCounts.None, 0);
        }

        if (withdrawnToday + amount > DailyLimit)
        {
            return (AtmStatus.DailyLimitExceeded, NoteCounts.None, 0);
        }

        var fee = Fee(amount);
        if (amount + fee > balance)
        {
            return (AtmStatus.InsufficientFunds, NoteCounts.None, 0);
        }

        return FewestNotes(amount, Available(stock, jammed)) is { } notes
            ? (AtmStatus.Ok, notes, fee)
            : (AtmStatus.CannotDispense, NoteCounts.None, 0);
    }

    public static AtmStatus Deposit(NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        if (notes.Total == 0)
        {
            return AtmStatus.InvalidAmount;
        }

        if (jammed is { } j && notes.Of(j) > 0)
        {
            return AtmStatus.CassetteJammed;
        }

        var after = stock + notes;
        if (after.Fifties > Capacity || after.Twenties > Capacity || after.Tens > Capacity)
        {
            return AtmStatus.CassetteFull;
        }

        return AtmStatus.Ok;
    }
}

/// <summary>Les valeurs du domaine, telles que listées par la spec.</summary>
internal static class Domain
{
    public static IEnumerable<int> Balances => Enumerable.Range(0, 51).Select(i => i * 5);

    public static IEnumerable<int> WithdrawnToday => Enumerable.Range(0, 21).Select(i => i * 10);

    public static IEnumerable<int> Amounts =>
        new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));

    public static IEnumerable<Denomination?> Jammed =>
        [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static IEnumerable<NoteCounts> Stocks =>
        from f in Enumerable.Range(0, 6)
        from t in Enumerable.Range(0, 6)
        from te in Enumerable.Range(0, 6)
        select new NoteCounts(f, t, te);

    public static IEnumerable<NoteCounts> Deposits =>
        from f in Enumerable.Range(0, 2)
        from t in Enumerable.Range(0, 2)
        from te in Enumerable.Range(0, 2)
        select new NoteCounts(f, t, te);
}
