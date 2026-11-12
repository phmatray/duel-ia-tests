namespace Atm.Tests;

/// <summary>
/// Tests de renfort : modèle de référence indépendant comparé à la machine sur tout le domaine,
/// pannes à chaque passage, interactions avec le module, et contrat de <see cref="MoneyDispenser"/>.
/// </summary>
public class Renforts
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = BuildAmounts();

    private static int[] BuildAmounts()
    {
        var amounts = new List<int> { -10, 0, 5 };
        for (var a = 10; a <= 220; a += 10)
        {
            amounts.Add(a);
        }

        return [.. amounts];
    }

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var c = 0; c <= 5; c++)
        {
            yield return new NoteCounts(f, t, c);
        }
    }

    private static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var c = 0; c <= 1; c++)
        {
            yield return new NoteCounts(f, t, c);
        }
    }

    private static IEnumerable<AccountState> AllAccounts()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        {
            yield return new AccountState(balance, withdrawn);
        }
    }

    // ---- Modèle de référence, écrit directement d'après la spécification ----

    private static NoteCounts? FewestNotes(int amount, NoteCounts stock, Denomination? jam)
    {
        var available = new NoteCounts(
            jam == Denomination.Fifty ? 0 : stock.Fifties,
            jam == Denomination.Twenty ? 0 : stock.Twenties,
            jam == Denomination.Ten ? 0 : stock.Tens);

        NoteCounts? best = null;
        var ties = 0;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var c = 0; c <= available.Tens; c++)
        {
            var n = new NoteCounts(f, t, c);
            if (n.Value != amount)
            {
                continue;
            }

            if (best is null || n.Total < best.Value.Total)
            {
                best = n;
                ties = 0;
            }
            else if (n.Total == best.Value.Total)
            {
                ties++;
            }
        }

        Assert.Equal(0, ties); // la spécification garantit l'unicité
        return best;
    }

    private static (AtmResult Result, NoteCounts StockAfter) Expected(
        AccountState account, Operation operation, NoteCounts stock, Denomination? jam)
    {
        (AtmResult, NoteCounts) Refused(AtmStatus status) => (new AtmResult(status, NoteCounts.None, 0, account), stock);

        switch (operation)
        {
            case Operation.Withdraw { Amount: var amount }:
            {
                if (amount <= 0 || amount % 10 != 0) return Refused(AtmStatus.InvalidAmount);
                if (account.WithdrawnToday + amount > 200) return Refused(AtmStatus.DailyLimitExceeded);
                var fee = amount > 100 ? 5 : 0;
                if (amount + fee > account.Balance) return Refused(AtmStatus.InsufficientFunds);
                if (FewestNotes(amount, stock, jam) is not { } notes) return Refused(AtmStatus.CannotDispense);
                var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
                return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
            }
            case Operation.Deposit { Notes: var notes }:
            {
                if (notes.Total == 0) return Refused(AtmStatus.InvalidAmount);
                if (jam is { } j && notes.Of(j) > 0) return Refused(AtmStatus.CassetteJammed);
                var s = stock + notes;
                if (s.Fifties > 5 || s.Twenties > 5 || s.Tens > 5) return Refused(AtmStatus.CassetteFull);
                var after = new AccountState(account.Balance + notes.Value, account.WithdrawnToday);
                return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, after), s);
            }
            default:
                throw new InvalidOperationException();
        }
    }

    private static void Check(AccountState account, Operation operation, NoteCounts stock, Denomination? jam)
    {
        var dispenser = new MoneyDispenser(stock, jam);
        var actual = new CashMachine(dispenser).Execute(account, operation);
        var (expected, stockAfter) = Expected(account, operation, stock, jam);

        if (expected != actual || stockAfter != dispenser.Stock)
        {
            Assert.Fail($"{operation} compte={account} stock={stock} bloquée={jam?.ToString() ?? "aucune"} : " +
                        $"attendu {expected} / stock {stockAfter}, obtenu {actual} / stock {dispenser.Stock}");
        }
    }

    // ---- Comparaisons exhaustives avec le modèle ----

    /// <summary>Tous les comptes × tous les montants, sur des stocks représentatifs (ordre des contrôles, bornes, frais).</summary>
    [Fact]
    public void Withdraw_AllAccountsAndAmounts_MatchModel()
    {
        (NoteCounts Stock, Denomination? Jam)[] stocks =
        [
            (Full, null),
            (NoteCounts.None, null),
            (Full, Denomination.Fifty),
            (Full, Denomination.Twenty),
            (Full, Denomination.Ten),
            (new NoteCounts(1, 3, 0), null),
            (new NoteCounts(0, 5, 5), null),
            (new NoteCounts(1, 5, 5), Denomination.Twenty),
            (new NoteCounts(4, 0, 0), null),
        ];

        foreach (var account in AllAccounts())
        foreach (var amount in Amounts)
        foreach (var (stock, jam) in stocks)
        {
            Check(account, new Operation.Withdraw(amount), stock, jam);
        }
    }

    /// <summary>Tous les stocks × toutes les cassettes bloquées × tous les montants, pour plusieurs comptes.</summary>
    [Fact]
    public void Withdraw_AllStocksJamsAndAmounts_MatchModel()
    {
        AccountState[] accounts =
        [
            new(250, 0), new(0, 0), new(250, 200), new(105, 100), new(115, 90), new(60, 140), new(205, 0),
        ];

        foreach (var account in accounts)
        foreach (var jam in Jams)
        foreach (var stock in AllStocks())
        foreach (var amount in Amounts)
        {
            Check(account, new Operation.Withdraw(amount), stock, jam);
        }
    }

    /// <summary>Tout le domaine du dépôt : stocks × cassettes bloquées × dépôts, pour plusieurs comptes.</summary>
    [Fact]
    public void Deposit_WholeDomain_MatchesModel()
    {
        AccountState[] accounts = [new(0, 0), new(10, 30), new(250, 200), new(165, 70)];

        foreach (var account in accounts)
        foreach (var jam in Jams)
        foreach (var stock in AllStocks())
        foreach (var notes in AllDeposits())
        {
            Check(account, new Operation.Deposit(notes), stock, jam);
        }
    }

    // ---- Ordre des contrôles, cas ciblés lisibles ----

    [Fact]
    public void Withdraw_InvalidAmount_CheckedBeforeEverythingElse()
    {
        var result = new CashMachine(new MoneyDispenser(NoteCounts.None, Denomination.Ten))
            .Execute(new AccountState(0, 200), new Operation.Withdraw(15));

        Assert.Equal(AtmStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void Withdraw_DailyLimit_CheckedBeforeCannotDispense()
    {
        var account = new AccountState(250, 150);
        var result = new CashMachine(new MoneyDispenser(NoteCounts.None)).Execute(account, new Operation.Withdraw(60));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, account), result);
    }

    [Fact]
    public void Withdraw_DailyLimit_BoundaryUsesSumWithAlreadyWithdrawn()
    {
        var machine = new CashMachine(new MoneyDispenser(Full));

        Assert.Equal(AtmStatus.Ok, machine.Execute(new AccountState(250, 190), new Operation.Withdraw(10)).Status);
        Assert.Equal(AtmStatus.DailyLimitExceeded,
            new CashMachine(new MoneyDispenser(Full)).Execute(new AccountState(250, 200), new Operation.Withdraw(10)).Status);
    }

    [Fact]
    public void Withdraw_Fee_AppliesOnlyStrictlyAbove100()
    {
        var at100 = new CashMachine(new MoneyDispenser(Full)).Execute(new AccountState(100, 0), new Operation.Withdraw(100));
        var at110 = new CashMachine(new MoneyDispenser(Full)).Execute(new AccountState(250, 0), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(2, 0, 0), 0, new AccountState(0, 100)), at100);
        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(2, 0, 1), 5, new AccountState(135, 110)), at110);
    }

    [Fact]
    public void Withdraw_FeeIncludedInFundsCheck_BoundaryOneStepBelow()
    {
        // 120 + 5 = 125 : 120 de solde ne suffit pas, 125 suffit tout juste.
        var refused = new CashMachine(new MoneyDispenser(Full)).Execute(new AccountState(120, 0), new Operation.Withdraw(120));
        var accepted = new CashMachine(new MoneyDispenser(Full)).Execute(new AccountState(125, 0), new Operation.Withdraw(120));

        Assert.Equal(AtmStatus.InsufficientFunds, refused.Status);
        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(2, 1, 0), 5, new AccountState(0, 120)), accepted);
    }

    // ---- Interactions avec le module ----

    [Theory]
    [InlineData(-10)]
    [InlineData(5)]
    [InlineData(210)]  // plafond
    [InlineData(120)]  // fonds (solde 100)
    public void Withdraw_Refused_NeverCallsDispense(int amount)
    {
        var fake = new ScriptedDispenser(Full);
        var result = new CashMachine(fake).Execute(new AccountState(100, 0), new Operation.Withdraw(amount));

        Assert.NotEqual(AtmStatus.Ok, result.Status);
        Assert.Empty(fake.Passes);
        Assert.Empty(fake.Loads);
    }

    [Fact]
    public void Withdraw_CannotDispense_NeverCallsDispense()
    {
        var fake = new ScriptedDispenser(new NoteCounts(1, 0, 0));
        var result = new CashMachine(fake).Execute(new AccountState(250, 0), new Operation.Withdraw(30));

        Assert.Equal(AtmStatus.CannotDispense, result.Status);
        Assert.Empty(fake.Passes);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 0, 2, 1)]
    [InlineData(Denomination.Twenty, 1, 0, 0)]
    [InlineData(Denomination.Ten, 1, 0, 0)]
    public void Withdraw_ReadsJammedCassetteFromModule(Denomination jam, int f, int t, int c)
    {
        // 50 : 50×1 normalement ; avec les 50 bloqués, 20×2 + 10×1.
        var fake = new ScriptedDispenser(Full) { JammedCassette = jam };
        var result = new CashMachine(fake).Execute(new AccountState(250, 0), new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(f, t, c), result.Notes);
        Assert.All(fake.Passes, p => Assert.Equal(0, p.Of(jam)));
    }

    [Fact]
    public void Withdraw_Success_PassesAreValidAndSumToNotes()
    {
        foreach (var jam in Jams)
        foreach (var stock in AllStocks())
        for (var amount = 10; amount <= 200; amount += 10)
        {
            var fake = new ScriptedDispenser(stock) { JammedCassette = jam };
            var result = new CashMachine(fake).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));
            if (result.Status != AtmStatus.Ok)
            {
                Assert.Empty(fake.Passes);
                continue;
            }

            Assert.All(fake.Passes, p =>
            {
                Assert.InRange(p.Total, 1, 4);
                Assert.True(p.Fifties >= 0 && p.Twenties >= 0 && p.Tens >= 0);
                if (jam is { } j) Assert.Equal(0, p.Of(j));
            });
            Assert.Equal(result.Notes, fake.Passes.Aggregate(NoteCounts.None, (a, b) => a + b));
            Assert.Equal(amount, result.Notes.Value);
            Assert.Empty(fake.Loads);
        }
    }

    [Fact]
    public void Withdraw_SingleNote_SingleNonEmptyPass()
    {
        // 1 billet → exactement un passage.
        var fake = new ScriptedDispenser(Full);
        new CashMachine(fake).Execute(new AccountState(250, 0), new Operation.Withdraw(10));

        Assert.Equal([new NoteCounts(0, 0, 1)], fake.Passes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Withdraw_FaultOnEachPass_ReturnsSumOfPreviousPasses(int failingPass)
    {
        // 200 avec 50×1 20×5 10×5 : 11 billets, donc 3 passages.
        var fake = new ScriptedDispenser(new NoteCounts(1, 5, 5)) { FailDispenseOnPass = failingPass };
        var account = new AccountState(250, 0);
        var result = new CashMachine(fake).Execute(account, new Operation.Withdraw(200));

        var already = fake.Passes.Take(failingPass - 1).Aggregate(NoteCounts.None, (a, b) => a + b);

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, already, 0, account), result);
        Assert.Equal(failingPass, fake.Passes.Count); // arrêt aussitôt
        Assert.Equal(new NoteCounts(1, 5, 5) - already, fake.Stock);
    }

    [Fact]
    public void Withdraw_FaultWithFee_FeeIsZero()
    {
        var fake = new ScriptedDispenser(Full) { FailDispenseOnPass = 1 };
        var account = new AccountState(250, 50);
        var result = new CashMachine(fake).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
    }

    [Fact]
    public void Deposit_Success_CallsLoadOnceWithExactNotes()
    {
        var fake = new ScriptedDispenser(new NoteCounts(1, 2, 3));
        var notes = new NoteCounts(1, 1, 0);
        var result = new CashMachine(fake).Execute(new AccountState(20, 40), new Operation.Deposit(notes));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(90, 40)), result);
        Assert.Equal([notes], fake.Loads);
        Assert.Empty(fake.Passes);
    }

    [Theory]
    [InlineData(0, 0, 0, null, 0, 0, 0)]                 // vide
    [InlineData(0, 0, 0, Denomination.Ten, 0, 0, 1)]     // bloquée
    [InlineData(0, 0, 0, Denomination.Fifty, 1, 1, 1)]   // bloquée
    [InlineData(0, 5, 0, null, 0, 1, 0)]                 // pleine
    [InlineData(5, 0, 0, null, 1, 0, 0)]                 // pleine
    public void Deposit_Refused_NeverCallsLoad(int s50, int s20, int s10, Denomination? jam, int n50, int n20, int n10)
    {
        var fake = new ScriptedDispenser(new NoteCounts(s50, s20, s10)) { JammedCassette = jam };
        var account = new AccountState(10, 30);
        var result = new CashMachine(fake).Execute(account, new Operation.Deposit(new NoteCounts(n50, n20, n10)));

        Assert.NotEqual(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Empty(fake.Loads);
        Assert.Empty(fake.Passes);
    }

    [Fact]
    public void Deposit_ReadsStockFromModule_ForCassetteFull()
    {
        var fake = new ScriptedDispenser(new NoteCounts(0, 0, 5));
        var result = new CashMachine(fake).Execute(new AccountState(0, 0), new Operation.Deposit(new NoteCounts(0, 0, 1)));

        Assert.Equal(AtmStatus.CassetteFull, result.Status);
    }

    [Fact]
    public void Deposit_Fault_WithFullValidDeposit_NothingChanges()
    {
        var fake = new ScriptedDispenser(new NoteCounts(2, 2, 2)) { JammedCassette = Denomination.Ten, FailLoad = true };
        var account = new AccountState(250, 200);
        var result = new CashMachine(fake).Execute(account, new Operation.Deposit(new NoteCounts(1, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Single(fake.Loads);
        Assert.Equal(new NoteCounts(2, 2, 2), fake.Stock);
    }

    // ---- Absence de mémoire ----

    [Fact]
    public void Machine_DependsOnlyOnCurrentModuleState()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(1, 0, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Withdraw(50)).Status);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(account, new Operation.Withdraw(50)).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Deposit(new NoteCounts(1, 0, 0))).Status);
        Assert.Equal(
            new AtmResult(AtmStatus.Ok, new NoteCounts(1, 0, 0), 0, new AccountState(200, 50)),
            machine.Execute(account, new Operation.Withdraw(50)));
    }

    [Fact]
    public void Machine_AfterFault_NextCallWorksNormally()
    {
        var fake = new ScriptedDispenser(Full) { FailDispenseOnPass = 1 };
        var machine = new CashMachine(fake);
        var account = new AccountState(250, 0);

        Assert.Equal(AtmStatus.DispenserFault, machine.Execute(account, new Operation.Withdraw(50)).Status);
        Assert.Equal(
            new AtmResult(AtmStatus.Ok, new NoteCounts(1, 0, 0), 0, new AccountState(200, 50)),
            machine.Execute(account, new Operation.Withdraw(50)));
    }

    // ---- Contrat du vrai module ----

    [Theory]
    [InlineData(0, 0, 0)] // vide
    [InlineData(4, 1, 0)] // plus de 4 billets
    [InlineData(0, 3, 2)] // plus de 4 billets
    [InlineData(0, 0, 1)] // cassette bloquée
    [InlineData(3, 0, 0)] // plus que le stock
    public void MoneyDispenser_Dispense_RefusesWithoutChange(int f, int t, int c)
    {
        var stock = new NoteCounts(2, 5, 5);
        var dispenser = new MoneyDispenser(stock, Denomination.Ten);

        Assert.False(dispenser.Dispense(new NoteCounts(f, t, c)));
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void MoneyDispenser_Dispense_FourNotesAccepted()
    {
        var dispenser = new MoneyDispenser(Full);

        Assert.True(dispenser.Dispense(new NoteCounts(2, 1, 1)));
        Assert.Equal(new NoteCounts(3, 4, 4), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0)] // vide
    [InlineData(0, 1, 0)] // cassette bloquée
    [InlineData(1, 0, 1)] // dépasse 5 dans la cassette des 10
    public void MoneyDispenser_Load_RefusesWithoutChange(int f, int t, int c)
    {
        var stock = new NoteCounts(0, 0, 5);
        var dispenser = new MoneyDispenser(stock, Denomination.Twenty);

        Assert.False(dispenser.Load(new NoteCounts(f, t, c)));
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void MoneyDispenser_Load_UpToCapacityAccepted()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(4, 4, 0));

        Assert.True(dispenser.Load(new NoteCounts(1, 1, 5)));
        Assert.Equal(Full, dispenser.Stock);
        Assert.Null(dispenser.Jammed);
    }

    // ---- Faux module paramétrable ----

    private sealed class ScriptedDispenser(NoteCounts stock) : IMoneyDispenser
    {
        public NoteCounts Stock { get; private set; } = stock;
        public Denomination? Jammed => JammedCassette;
        public Denomination? JammedCassette { get; init; }
        public int FailDispenseOnPass { get; init; }
        public bool FailLoad { get; init; }
        public List<NoteCounts> Passes { get; } = [];
        public List<NoteCounts> Loads { get; } = [];

        public bool Dispense(NoteCounts notes)
        {
            Passes.Add(notes);
            if (Passes.Count == FailDispenseOnPass)
            {
                return false;
            }

            Stock -= notes;
            return true;
        }

        public bool Load(NoteCounts notes)
        {
            Loads.Add(notes);
            if (FailLoad)
            {
                return false;
            }

            Stock += notes;
            return true;
        }
    }
}
