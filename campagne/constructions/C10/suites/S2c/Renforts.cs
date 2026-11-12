namespace Atm.Tests;

/// <summary>
/// Tests de renfort : modèle de référence parcouru sur tout le domaine, cas limites
/// et pannes du module que la suite d'origine ne couvre pas.
/// </summary>
public class Renforts
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static AccountState A(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<int> Balances() => Enumerable.Range(0, 51).Select(i => i * 5);

    private static IEnumerable<int> WithdrawnTodays() => Enumerable.Range(0, 21).Select(i => i * 10);

    private static IEnumerable<int> Amounts() => new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
        {
            yield return N(f, t, d);
        }
    }

    private static IEnumerable<NoteCounts> Deposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
        {
            yield return N(f, t, d);
        }
    }

    // ---------- Modèle de référence, écrit directement d'après la spécification ----------

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int, NoteCounts, Denomination?), NoteCounts?> BestCombination = new();

    /// <summary>Énumère toutes les combinaisons possibles et garde la plus courte qui fait exactement le montant.</summary>
    private static NoteCounts? Fewest((int Amount, NoteCounts Stock, Denomination? Jammed) key)
    {
        var (amount, stock, jammed) = key;
        NoteCounts? best = null;
        foreach (var c in Stocks())
        {
            var fits = c.Fifties <= stock.Fifties && c.Twenties <= stock.Twenties && c.Tens <= stock.Tens;
            var avoidsJam = jammed is not { } j || c.Of(j) == 0;
            if (c.Value == amount && fits && avoidsJam && (best is not { } b || c.Total < b.Total))
            {
                best = c;
            }
        }

        return best;
    }

    private static (AtmResult Result, NoteCounts StockAfter) Expected(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        AtmResult Refused(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        switch (operation)
        {
            case Operation.Withdraw { Amount: var amount }:
            {
                if (amount <= 0 || amount % 10 != 0) return (Refused(AtmStatus.InvalidAmount), stock);
                if (account.WithdrawnToday + amount > 200) return (Refused(AtmStatus.DailyLimitExceeded), stock);
                var fee = amount > 100 ? 5 : 0;
                if (amount + fee > account.Balance) return (Refused(AtmStatus.InsufficientFunds), stock);

                if (BestCombination.GetOrAdd((amount, stock, jammed), Fewest) is not { } notes) return (Refused(AtmStatus.CannotDispense), stock);

                var after = A(account.Balance - amount - fee, account.WithdrawnToday + amount);
                return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
            }

            case Operation.Deposit { Notes: var notes }:
            {
                if (notes.Total == 0) return (Refused(AtmStatus.InvalidAmount), stock);
                if (jammed is { } j && notes.Of(j) > 0) return (Refused(AtmStatus.CassetteJammed), stock);
                var after = stock + notes;
                if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (Refused(AtmStatus.CassetteFull), stock);

                return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }), after);
            }

            default:
                throw new InvalidOperationException();
        }
    }

    /// <summary>Exécute et vérifie résultat, stock final et passages contre le modèle de référence.</summary>
    private static void Check(AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var recorder = new RecordingDispenser(new MoneyDispenser(stock, jammed));
        var result = new CashMachine(recorder).Execute(account, operation);
        var (expected, stockAfter) = Expected(account, stock, jammed, operation);

        if (result != expected || recorder.Stock != stockAfter)
        {
            Assert.Fail($"{account} stock={stock} bloquée={jammed?.ToString() ?? "aucune"} {operation} : " +
                        $"attendu {expected} / stock {stockAfter}, obtenu {result} / stock {recorder.Stock}");
        }

        if (expected.Status != AtmStatus.Ok)
        {
            // Sur un refus, le module n'est pas sollicité pour déplacer des billets.
            Assert.Empty(recorder.Dispensed);
            Assert.Empty(recorder.Loaded);
            return;
        }

        if (operation is Operation.Withdraw)
        {
            Assert.Empty(recorder.Loaded);
            Assert.All(recorder.Dispensed, pass => Assert.InRange(pass.Total, 1, 4));
            Assert.Equal(expected.Notes, recorder.Dispensed.Aggregate(NoteCounts.None, (a, b) => a + b));
        }
        else
        {
            Assert.Empty(recorder.Dispensed);
            Assert.Equal(expected.Account.Balance - account.Balance, recorder.Loaded.Aggregate(NoteCounts.None, (a, b) => a + b).Value);
        }
    }

    // ---------- Parcours du domaine ----------

    /// <summary>Tous les comptes et tous les montants du domaine, sur tous les stocks et cassettes bloquées.</summary>
    [Fact]
    public void Withdraw_WholeDomain_MatchesReference()
    {
        var operations = Amounts().Select(a => (Operation)new Operation.Withdraw(a)).ToArray();

        Parallel.ForEach(Stocks(), stock =>
        {
            foreach (var jammed in Jams)
            foreach (var balance in Balances())
            foreach (var withdrawn in WithdrawnTodays())
            foreach (var operation in operations)
            {
                Check(A(balance, withdrawn), stock, jammed, operation);
            }
        });
    }

    /// <summary>Tous les dépôts du domaine, sur tous les stocks, cassettes bloquées et comptes.</summary>
    [Fact]
    public void Deposit_WholeDomain_MatchesReference()
    {
        var operations = Deposits().Select(d => (Operation)new Operation.Deposit(d)).ToArray();

        Parallel.ForEach(Stocks(), stock =>
        {
            foreach (var jammed in Jams)
            foreach (var balance in Balances())
            foreach (var withdrawn in WithdrawnTodays())
            foreach (var operation in operations)
            {
                Check(A(balance, withdrawn), stock, jammed, operation);
            }
        });
    }

    // ---------- Cas limites explicites (lisibles en cas d'échec) ----------

    [Fact]
    public void Deposit_FullFifties_IsRefused()
    {
        var (result, dispenser) = Run(A(10, 0), N(5, 0, 0), null, new Operation.Deposit(N(1, 0, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, A(10, 0)), result);
        Assert.Equal(N(5, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_OnlyOneCassetteWouldOverflow_RefusesWholeDeposit()
    {
        var (result, dispenser) = Run(A(10, 0), N(0, 0, 5), null, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.CassetteFull, result.Status);
        Assert.Equal(N(0, 0, 5), dispenser.Stock);
    }

    [Fact]
    public void Deposit_FillsCassetteToExactlyFive_IsAccepted()
    {
        var (result, dispenser) = Run(A(0, 0), N(4, 4, 4), null, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(80, 0)), result);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    public void Deposit_AnyJammedCassette_IsRefused(Denomination jammed, int f, int t, int d)
    {
        var (result, dispenser) = Run(A(10, 0), N(0, 0, 0), jammed, new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, A(10, 0)), result);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);

        var (single, _) = Run(A(10, 0), N(0, 0, 0), jammed, new Operation.Deposit(N(f, t, d)));
        Assert.Equal(AtmStatus.CassetteJammed, single.Status);
    }

    [Fact]
    public void Deposit_JammedCassetteFull_ButNoteForOtherCassette_IsAccepted()
    {
        var (result, dispenser) = Run(A(0, 0), N(5, 0, 0), Denomination.Fifty, new Operation.Deposit(N(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(20, 0)), result);
        Assert.Equal(N(5, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_100_WithBalance100_NoFeeSoAllowed()
    {
        var (result, _) = Run(A(100, 0), N(5, 5, 5), null, new Operation.Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, A(0, 100)), result);
    }

    [Fact]
    public void Withdraw_AmountEqualsBalance_Below100_IsAllowed()
    {
        var (result, _) = Run(A(30, 0), N(0, 1, 1), null, new Operation.Withdraw(30));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 1, 1), 0, A(0, 30)), result);
    }

    [Fact]
    public void Withdraw_OneStepOverBalance_IsRefused() =>
        AssertRefused(AtmStatus.InsufficientFunds, A(120, 0), N(5, 5, 5), null, new Operation.Withdraw(120));

    [Fact]
    public void Withdraw_DailyLimit_OneStepOver_IsRefused() =>
        AssertRefused(AtmStatus.DailyLimitExceeded, A(250, 10), N(5, 5, 5), null, new Operation.Withdraw(200));

    [Fact]
    public void Withdraw_220_IsDailyLimitNotInvalid() =>
        AssertRefused(AtmStatus.DailyLimitExceeded, A(250, 0), N(5, 5, 5), null, new Operation.Withdraw(220));

    [Fact]
    public void Withdraw_InvalidAmount_CheckedBeforeFunds() =>
        AssertRefused(AtmStatus.InvalidAmount, A(0, 0), N(5, 5, 5), null, new Operation.Withdraw(0));

    [Fact]
    public void Withdraw_DailyLimit_CheckedBeforeDispensability() =>
        AssertRefused(AtmStatus.DailyLimitExceeded, A(250, 200), N(0, 0, 0), null, new Operation.Withdraw(10));

    [Fact]
    public void Withdraw_FewestNotes_PrefersFiftyOverTwentiesAndTens()
    {
        // 90 = 50+20+20 (3 billets) plutôt que 20×4+10 (5) ou 10×… : la combinaison la plus courte.
        var (result, dispenser) = Run(A(250, 0), N(5, 5, 5), null, new Operation.Withdraw(90));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 2, 0), 0, A(160, 90)), result);
        Assert.Equal(N(4, 3, 5), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_FewestNotes_IsNotGreedy()
    {
        // Glouton : 50 puis 10 impossibles (pas de 10) → doit trouver 20×3.
        var (result, _) = Run(A(250, 0), N(5, 5, 0), null, new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, A(190, 60)), result);
    }

    [Fact]
    public void Withdraw_JammedCassette_StockedButIgnored()
    {
        var (result, dispenser) = Run(A(250, 0), N(5, 5, 5), Denomination.Fifty, new Operation.Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 5, 0), 0, A(150, 100)), result);
        Assert.Equal(N(5, 0, 5), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_200_WithJammedFifties_NeedsMoreThanAvailable() =>
        AssertRefused(AtmStatus.CannotDispense, A(250, 0), N(5, 5, 5), Denomination.Fifty, new Operation.Withdraw(200));

    // ---------- Pannes du module ----------

    [Fact]
    public void Withdraw_DispenserFailsOnThirdPass_ReturnsAllNotesAlreadyOut()
    {
        var faulty = new FaultyDispenser(N(0, 5, 5), failDispenseOnCall: 3);
        var account = A(250, 0);

        var result = new CashMachine(faulty).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(2, faulty.Dispensed.Count);
        var alreadyOut = faulty.Dispensed[0] + faulty.Dispensed[1];
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, alreadyOut, 0, account), result);
        Assert.Equal(8, result.Notes.Total);
        Assert.Equal(3, faulty.DispenseCalls);
    }

    [Fact]
    public void Withdraw_DispenserFault_StopsImmediately()
    {
        var faulty = new FaultyDispenser(N(0, 5, 5), failDispenseOnCall: 1);
        var account = A(250, 0);

        var result = new CashMachine(faulty).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal(1, faulty.DispenseCalls);
        Assert.Equal(0, faulty.LoadCalls);
    }

    [Fact]
    public void Deposit_DispenserFails_DoesNotDispenseAndKeepsWithdrawnToday()
    {
        var faulty = new FaultyDispenser(N(1, 1, 1), failLoad: true);
        var account = A(45, 130);

        var result = new CashMachine(faulty).Execute(account, new Operation.Deposit(N(1, 0, 0)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal(1, faulty.LoadCalls);
        Assert.Equal(0, faulty.DispenseCalls);
    }

    // ---------- Absence de mémoire ----------

    [Fact]
    public void SameMachine_SuccessiveOperations_DependOnlyOnCurrentModuleState()
    {
        var dispenser = new MoneyDispenser(N(1, 1, 1));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(A(250, 0), new Operation.Withdraw(80));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, A(170, 80)), first);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);

        // Le stock est vide : le même retrait est maintenant impossible.
        var second = machine.Execute(A(250, 0), new Operation.Withdraw(80));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, A(250, 0)), second);

        // Un dépôt réalimente le module, et le retrait suivant en tient compte.
        var deposit = machine.Execute(A(0, 0), new Operation.Deposit(N(1, 0, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(60, 0)), deposit);

        var third = machine.Execute(A(250, 0), new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 1), 0, A(190, 60)), third);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void SameMachine_RefusalDoesNotAffectNextOperation()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        machine.Execute(A(0, 0), new Operation.Withdraw(110));
        machine.Execute(A(250, 200), new Operation.Withdraw(10));
        machine.Execute(A(10, 0), new Operation.Deposit(N(1, 0, 0)));

        var result = machine.Execute(A(115, 0), new Operation.Withdraw(110));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, A(0, 110)), result);
        Assert.Equal(N(3, 5, 4), dispenser.Stock);
    }

    // ---------- Outils ----------

    private static (AtmResult Result, MoneyDispenser Dispenser) Run(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, operation);

        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var (result, dispenser) = Run(account, stock, jammed, operation);

        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
        Assert.Equal(stock, dispenser.Stock);
    }

    private sealed class RecordingDispenser(IMoneyDispenser inner) : IMoneyDispenser
    {
        public List<NoteCounts> Dispensed { get; } = [];

        public List<NoteCounts> Loaded { get; } = [];

        public NoteCounts Stock => inner.Stock;

        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            Dispensed.Add(notes);
            return inner.Dispense(notes);
        }

        public bool Load(NoteCounts notes)
        {
            Loaded.Add(notes);
            return inner.Load(notes);
        }
    }

    private sealed class FaultyDispenser(NoteCounts stock, int failDispenseOnCall = 0, bool failLoad = false) : IMoneyDispenser
    {
        public int DispenseCalls { get; private set; }

        public int LoadCalls { get; private set; }

        public List<NoteCounts> Dispensed { get; } = [];

        public NoteCounts Stock { get; private set; } = stock;

        public Denomination? Jammed => null;

        public bool Dispense(NoteCounts notes)
        {
            if (++DispenseCalls == failDispenseOnCall)
            {
                return false;
            }

            Dispensed.Add(notes);
            Stock -= notes;
            return true;
        }

        public bool Load(NoteCounts notes)
        {
            LoadCalls++;
            if (failLoad)
            {
                return false;
            }

            Stock += notes;
            return true;
        }
    }
}
