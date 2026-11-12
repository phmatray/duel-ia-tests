namespace Atm.Tests;

public class Renforts
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static AtmResult Run(AccountState account, NoteCounts stock, Denomination? jammed, Operation operation,
        out MoneyDispenser dispenser)
    {
        dispenser = new MoneyDispenser(stock, jammed);
        return new CashMachine(dispenser).Execute(account, operation);
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

    // ---- Dépôt : remplir une cassette jusqu'à exactement 5 billets est permis ----

    [Theory]
    [InlineData(4, 0, 0, 1, 0, 0)]
    [InlineData(0, 4, 0, 0, 1, 0)]
    [InlineData(0, 0, 4, 0, 0, 1)]
    [InlineData(4, 4, 4, 1, 1, 1)]
    public void Deposit_FillingCassetteToExactlyFive_IsAccepted(int s50, int s20, int s10, int n50, int n20, int n10)
    {
        var stock = new NoteCounts(s50, s20, s10);
        var notes = new NoteCounts(n50, n20, n10);
        var result = Run(new AccountState(0, 40), stock, null, new Operation.Deposit(notes), out var d);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(notes.Value, 40)), result);
        Assert.Equal(stock + notes, d.Stock);
    }

    [Fact]
    public void Deposit_OnlyTheOverflowingCassetteMatters()
    {
        // 50 et 10 pleins mais non touchés : le dépôt d'un 20 passe.
        var stock = new NoteCounts(5, 4, 5);
        var result = Run(new AccountState(5, 0), stock, null, new Operation.Deposit(new NoteCounts(0, 1, 0)), out var d);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(25, 0)), result);
        Assert.Equal(new NoteCounts(5, 5, 5), d.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    public void Deposit_ContainingJammedNote_AmongOthers_IsRefused(Denomination jam, int n50, int n20, int n10)
    {
        var notes = new NoteCounts(1, 1, 1);
        var stock = new NoteCounts(n50, n20, n10);
        var account = new AccountState(100, 100);
        var result = Run(account, stock, jam, new Operation.Deposit(notes), out var d);

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, account), result);
        Assert.Equal(stock, d.Stock);
    }

    [Fact]
    public void Deposit_Empty_CheckedBeforeEverythingElse_EvenWithFullStock()
    {
        var account = new AccountState(250, 200);
        var result = Run(account, Full, Denomination.Fifty, new Operation.Deposit(NoteCounts.None), out var d);

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, account), result);
        Assert.Equal(Full, d.Stock);
    }

    // ---- Dépôt : oracle sur tout le domaine (stock × cassette bloquée × dépôt) ----

    [Fact]
    public void Deposit_AllDomain_MatchesSpecification()
    {
        var account = new AccountState(35, 70);
        foreach (var jam in Jams)
        foreach (var stock in AllStocks())
        foreach (var notes in AllDeposits())
        {
            var result = Run(account, stock, jam, new Operation.Deposit(notes), out var d);
            var after = stock + notes;

            AtmStatus? refusal =
                notes.Total == 0 ? AtmStatus.InvalidAmount
                : jam is { } j && notes.Of(j) > 0 ? AtmStatus.CassetteJammed
                : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
                : null;

            if (refusal is { } status)
            {
                Assert.Equal(new AtmResult(status, NoteCounts.None, 0, account), result);
                Assert.Equal(stock, d.Stock);
            }
            else
            {
                Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0,
                    new AccountState(account.Balance + notes.Value, account.WithdrawnToday)), result);
                Assert.Equal(after, d.Stock);
            }
        }
    }

    // ---- Retrait : oracle des contrôles sur tout le domaine (solde × déjà retiré × montant) ----

    public static readonly int[] Amounts =
        [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    [Fact]
    public void Withdraw_AllAccountsAndAmounts_ChecksInSpecifiedOrder()
    {
        foreach (var amount in Amounts)
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        {
            var account = new AccountState(balance, withdrawn);
            var result = Run(account, Full, null, new Operation.Withdraw(amount), out var d);
            var fee = amount > 100 ? 5 : 0;

            AtmStatus? refusal =
                amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
                : withdrawn + amount > 200 ? AtmStatus.DailyLimitExceeded
                : amount + fee > balance ? AtmStatus.InsufficientFunds
                : null; // avec un stock plein, tout montant de 10 à 200 est distribuable

            if (refusal is { } status)
            {
                Assert.Equal(new AtmResult(status, NoteCounts.None, 0, account), result);
                Assert.Equal(Full, d.Stock);
            }
            else
            {
                Assert.Equal(AtmStatus.Ok, result.Status);
                Assert.Equal(amount, result.Notes.Value);
                Assert.Equal(fee, result.Fee);
                Assert.Equal(new AccountState(balance - amount - fee, withdrawn + amount), result.Account);
                Assert.Equal(Full - result.Notes, d.Stock);
            }
        }
    }

    // ---- Retrait : limites des frais et du plafond ----

    [Theory]
    [InlineData(100, 100, 0, 0)]
    [InlineData(110, 115, 5, 0)]
    [InlineData(200, 205, 5, 0)]
    public void Withdraw_FeeBoundaries(int amount, int balance, int fee, int expectedBalance)
    {
        var result = Run(new AccountState(balance, 0), Full, null, new Operation.Withdraw(amount), out _);

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(new AccountState(expectedBalance, amount), result.Account);
    }

    [Fact]
    public void Withdraw_FeeOnlyMakesFundsInsufficient_ByFiveEuros()
    {
        var account = new AccountState(114, 0);
        var result = Run(account, Full, null, new Operation.Withdraw(110), out var d);

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, account), result);
        Assert.Equal(Full, d.Stock);
    }

    [Fact]
    public void Withdraw_DailyLimit_OneStepOver_IsRefused()
    {
        var account = new AccountState(250, 200);
        var result = Run(account, Full, null, new Operation.Withdraw(10), out var d);

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, account), result);
        Assert.Equal(Full, d.Stock);
    }

    [Fact]
    public void Withdraw_InsufficientFunds_CheckedBeforeCannotDispense_WithJammedStock()
    {
        var account = new AccountState(40, 0);
        var stock = new NoteCounts(1, 0, 0);
        var result = Run(account, stock, Denomination.Fifty, new Operation.Withdraw(50), out var d);

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, account), result);
        Assert.Equal(stock, d.Stock);
    }

    // ---- Retrait : passages ----

    [Fact]
    public void Withdraw_EveryPassHasAtMostFourNotes_AndSumsToCombination()
    {
        foreach (var stock in AllStocks())
        for (var amount = 10; amount <= 200; amount += 10)
        {
            var fake = new SpyDispenser(stock);
            var result = new CashMachine(fake).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));

            if (result.Status != AtmStatus.Ok)
            {
                Assert.Empty(fake.Passes);
                continue;
            }

            Assert.All(fake.Passes, p => Assert.InRange(p.Total, 1, 4));
            Assert.Equal(result.Notes, fake.Passes.Aggregate(NoteCounts.None, (a, b) => a + b));
            Assert.Equal((result.Notes.Total + 3) / 4, fake.Passes.Count);
        }
    }

    [Fact]
    public void Withdraw_FaultOnThirdPass_ReturnsNotesOfFirstTwoPasses()
    {
        var fake = new SpyDispenser(new NoteCounts(1, 5, 5)) { FailDispenseOnPass = 3 };
        var account = new AccountState(250, 0);
        var result = new CashMachine(fake).Execute(account, new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(3, fake.Passes.Count);
        Assert.Equal(fake.Passes[0] + fake.Passes[1], result.Notes);
        Assert.Equal(8, result.Notes.Total);
    }

    [Fact]
    public void Deposit_Fault_CallsLoadOnceWithTheNotes_AndKeepsNotesEmpty()
    {
        var fake = new SpyDispenser(new NoteCounts(1, 1, 1)) { FailLoad = true };
        var account = new AccountState(0, 0);
        var notes = new NoteCounts(1, 0, 1);
        var result = new CashMachine(fake).Execute(account, new Operation.Deposit(notes));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal([notes], fake.Loads);
    }

    [Fact]
    public void Refusals_NeverCallTheDispenser()
    {
        var fake = new SpyDispenser(new NoteCounts(0, 0, 5));
        var machine = new CashMachine(fake);

        machine.Execute(new AccountState(250, 0), new Operation.Withdraw(5));
        machine.Execute(new AccountState(250, 200), new Operation.Withdraw(10));
        machine.Execute(new AccountState(0, 0), new Operation.Withdraw(10));
        machine.Execute(new AccountState(250, 0), new Operation.Withdraw(60));
        machine.Execute(new AccountState(250, 0), new Operation.Deposit(NoteCounts.None));
        machine.Execute(new AccountState(250, 0), new Operation.Deposit(new NoteCounts(0, 0, 1)));

        Assert.Empty(fake.Passes);
        Assert.Empty(fake.Loads);
    }

    private sealed class SpyDispenser(NoteCounts stock) : IMoneyDispenser
    {
        public NoteCounts Stock { get; private set; } = stock;
        public Denomination? Jammed => null;
        public List<NoteCounts> Passes { get; } = [];
        public List<NoteCounts> Loads { get; } = [];
        public int FailDispenseOnPass { get; init; }
        public bool FailLoad { get; init; }

        public bool Dispense(NoteCounts notes)
        {
            Passes.Add(notes);
            if (Passes.Count == FailDispenseOnPass || notes.Total is < 1 or > 4)
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
