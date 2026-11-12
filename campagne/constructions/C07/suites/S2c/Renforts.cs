namespace Atm.Tests;

/// <summary>
/// Tests de renfort : protocole avec le module (appels, pannes à chaque passage), absence de mémoire,
/// et couverture élargie du domaine (tous les comptes croisés avec des stocks et cassettes bloquées variés).
/// </summary>
public class Renforts
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static readonly NoteCounts Full = new(5, 5, 5);

    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static IEnumerable<AccountState> AllAccounts()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
            yield return new AccountState(balance, withdrawn);
    }

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return new NoteCounts(f, t, d);
    }

    private static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return new NoteCounts(f, t, d);
    }

    private static NoteCounts Sum(IEnumerable<NoteCounts> passes) => passes.Aggregate(NoteCounts.None, (a, b) => a + b);

    // ---------------------------------------------------------------- modèle de référence (d'après la spec)

    private static AtmResult ExpectedWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        AtmResult Refused(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return Refused(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return Refused(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return Refused(AtmStatus.InsufficientFunds);

        NoteCounts? best = null;
        for (var f = 0; f <= (jammed == Denomination.Fifty ? 0 : stock.Fifties); f++)
        for (var t = 0; t <= (jammed == Denomination.Twenty ? 0 : stock.Twenties); t++)
        for (var d = 0; d <= (jammed == Denomination.Ten ? 0 : stock.Tens); d++)
        {
            var c = new NoteCounts(f, t, d);
            if (c.Value == amount && (best is not { } b || c.Total < b.Total)) best = c;
        }

        if (best is not { } notes) return Refused(AtmStatus.CannotDispense);

        return new AtmResult(AtmStatus.Ok, notes, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    // ---------------------------------------------------------------- domaine élargi

    [Fact]
    public void Withdraw_EveryAccount_WithVariedStocksAndJams_MatchesReference()
    {
        NoteCounts[] stocks = [Full, N(0, 0, 0), N(1, 1, 1), N(2, 3, 1), N(0, 5, 5), N(5, 0, 2), N(3, 2, 0)];

        foreach (var stock in stocks)
        foreach (var jammed in JamOptions)
        foreach (var account in AllAccounts())
        foreach (var amount in Amounts)
        {
            var recorder = new RecordingDispenser(new MoneyDispenser(stock, jammed));
            var result = new CashMachine(recorder).Execute(account, new Operation.Withdraw(amount));
            var expected = ExpectedWithdraw(account, stock, jammed, amount);

            var context = $"compte={account} stock={stock} bloquée={jammed} retrait={amount}";
            Assert.True(expected == result, $"{context} : attendu {expected}, obtenu {result}");
            Assert.True(stock - expected.Notes == recorder.Stock, $"{context} : stock {recorder.Stock}");
            Assert.Empty(recorder.LoadCalls);
            if (expected.Status != AtmStatus.Ok)
            {
                Assert.True(recorder.DispenseCalls.Count == 0, $"{context} : un refus ne doit pas solliciter le module");
            }
            else
            {
                Assert.All(recorder.DispenseCalls, pass => Assert.InRange(pass.Total, 1, 4));
                Assert.Equal(expected.Notes, Sum(recorder.DispenseCalls));
            }
        }
    }

    [Fact]
    public void Withdraw_AnyRefusal_NeverCallsDispense()
    {
        foreach (var account in new[] { new AccountState(250, 0), new AccountState(105, 100), new AccountState(0, 0), new AccountState(110, 0) })
        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var amount in Amounts)
        {
            var recorder = new RecordingDispenser(new MoneyDispenser(stock, jammed));
            var result = new CashMachine(recorder).Execute(account, new Operation.Withdraw(amount));

            if (result.Status != AtmStatus.Ok)
            {
                Assert.True(recorder.DispenseCalls.Count == 0,
                    $"compte={account} stock={stock} bloquée={jammed} retrait={amount} : {result.Status} avec appel à Dispense");
            }
        }
    }

    [Fact]
    public void Deposit_EveryAccount_MatchesReference()
    {
        NoteCounts[] stocks = [N(0, 0, 0), N(4, 4, 4), N(5, 0, 3), N(2, 5, 4), Full];

        foreach (var stock in stocks)
        foreach (var jammed in JamOptions)
        foreach (var account in AllAccounts())
        foreach (var notes in AllDeposits())
        {
            var recorder = new RecordingDispenser(new MoneyDispenser(stock, jammed));
            var result = new CashMachine(recorder).Execute(account, new Operation.Deposit(notes));

            AtmStatus status;
            if (notes.Total == 0) status = AtmStatus.InvalidAmount;
            else if (jammed is { } j && notes.Of(j) > 0) status = AtmStatus.CassetteJammed;
            else if (stock.Fifties + notes.Fifties > 5 || stock.Twenties + notes.Twenties > 5 || stock.Tens + notes.Tens > 5)
                status = AtmStatus.CassetteFull;
            else status = AtmStatus.Ok;

            var expected = status == AtmStatus.Ok
                ? new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value })
                : new AtmResult(status, NoteCounts.None, 0, account);
            var expectedStock = status == AtmStatus.Ok ? stock + notes : stock;

            var context = $"compte={account} stock={stock} bloquée={jammed} dépôt={notes}";
            Assert.True(expected == result, $"{context} : attendu {expected}, obtenu {result}");
            Assert.True(expectedStock == recorder.Stock, $"{context} : stock {recorder.Stock}");
            Assert.Empty(recorder.DispenseCalls);
            if (status != AtmStatus.Ok)
            {
                Assert.True(recorder.LoadCalls.Count == 0, $"{context} : un refus ne doit pas solliciter le module");
            }
            else
            {
                Assert.Equal(notes, Sum(recorder.LoadCalls));
            }
        }
    }

    // ---------------------------------------------------------------- pannes du module

    [Fact]
    public void Withdraw_DispenserFaultOnThirdPass_ReportsAllNotesAlreadyOut()
    {
        // 150 avec 20×5 10×5 : 10 billets, donc au moins 3 passages.
        var dispenser = new FaultyDispenser(N(0, 5, 5), failOnDispenseCall: 3);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, dispenser.Dispensed, 0, new AccountState(250, 0)), result);
        Assert.Equal(3, dispenser.DispenseCalls);
        Assert.InRange(result.Notes.Total, 2, 8); // deux passages réussis, au plus 4 billets chacun
        Assert.Equal(N(0, 5, 5) - result.Notes, dispenser.Stock);
        Assert.Equal(0, dispenser.LoadCalls);
    }

    [Fact]
    public void Withdraw_DispenserFaultOnFirstOfSeveralPasses_StopsImmediately()
    {
        var dispenser = new FaultyDispenser(N(0, 5, 5), failOnDispenseCall: 1);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(250, 0)), result);
        Assert.Equal(1, dispenser.DispenseCalls);
        Assert.Equal(0, dispenser.LoadCalls);
    }

    [Fact]
    public void Withdraw_DispenserFaultMidway_DoesNotTryToPutNotesBack()
    {
        var dispenser = new FaultyDispenser(N(0, 5, 5), failOnDispenseCall: 2);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, dispenser.LoadCalls);
        Assert.Equal(2, dispenser.DispenseCalls);
    }

    [Fact]
    public void Withdraw_WorkingFake_SucceedsWithFee()
    {
        // Le faux module ne tombe en panne qu'au-delà des passages nécessaires.
        var dispenser = new FaultyDispenser(N(0, 5, 5), failOnDispenseCall: 100);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 40), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 5, 5), 5, new AccountState(95, 190)), result);
        Assert.Equal(N(0, 5, 5), dispenser.Dispensed);
        Assert.InRange(dispenser.DispenseCalls, 3, 10);
    }

    [Fact]
    public void Withdraw_FaultyFake_RespectsJammedCassette()
    {
        // La cassette bloquée vient du module, même un faux.
        var dispenser = new FaultyDispenser(N(1, 0, 3), Denomination.Fifty, failOnDispenseCall: 100);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new AccountState(250, 0)), result);
        Assert.Equal(0, dispenser.DispenseCalls);
    }

    [Fact]
    public void Deposit_DispenserFault_DoesNotDispense()
    {
        var dispenser = new FaultyDispenser(N(1, 1, 1));
        var result = new CashMachine(dispenser).Execute(new AccountState(10, 70), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(10, 70)), result);
        Assert.Equal(0, dispenser.DispenseCalls);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }

    // ---------------------------------------------------------------- sans mémoire : le module est relu à chaque appel

    [Fact]
    public void SameMachine_SecondWithdrawal_SeesStockLeftByFirst()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 1));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 1), 0, new AccountState(190, 60)), first);

        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new AccountState(250, 0)), second);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void SameMachine_DepositThenWithdraw_UsesDepositedNotes()
    {
        var dispenser = new MoneyDispenser(NoteCounts.None);
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(new AccountState(250, 0), new Operation.Withdraw(70)).Status);

        var deposit = machine.Execute(new AccountState(0, 0), new Operation.Deposit(N(1, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(70, 0)), deposit);

        var withdraw = machine.Execute(new AccountState(70, 0), new Operation.Withdraw(70));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 0), 0, new AccountState(0, 70)), withdraw);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void SameMachine_DepositsFillCassetteUntilFull()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 3));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(0, 0);

        for (var i = 0; i < 2; i++)
        {
            var ok = machine.Execute(account, new Operation.Deposit(N(0, 0, 1)));
            Assert.Equal(AtmStatus.Ok, ok.Status);
            account = ok.Account;
        }

        Assert.Equal(new AccountState(20, 0), account);
        Assert.Equal(N(0, 0, 5), dispenser.Stock);

        var full = machine.Execute(account, new Operation.Deposit(N(0, 0, 1)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, account), full);
    }

    [Fact]
    public void SameMachine_StockChangedOutside_IsSeen()
    {
        var dispenser = new MoneyDispenser(N(0, 2, 0));
        var machine = new CashMachine(dispenser);

        Assert.True(dispenser.Dispense(N(0, 1, 0)));

        var result = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(40));
        Assert.Equal(AtmStatus.CannotDispense, result.Status);

        Assert.True(dispenser.Load(N(0, 0, 2)));
        result = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(40));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 1, 2), 0, new AccountState(210, 40)), result);
    }

    [Fact]
    public void SameMachine_RefusalThenSuccess_NoLeftoverState()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.InsufficientFunds, machine.Execute(new AccountState(10, 0), new Operation.Withdraw(110)).Status);
        Assert.Equal(AtmStatus.DailyLimitExceeded, machine.Execute(new AccountState(250, 190), new Operation.Withdraw(20)).Status);

        var result = machine.Execute(new AccountState(50, 0), new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, new AccountState(0, 50)), result);
        Assert.Equal(N(4, 5, 5), dispenser.Stock);
    }

    // ---------------------------------------------------------------- cas ciblés supplémentaires

    [Theory]
    [InlineData(200, 0, 250, 4, 0, 0, 5)]  // 200 en 50×4 : un seul passage complet
    [InlineData(190, 0, 250, 3, 2, 0, 5)]  // 50×3 20×2
    [InlineData(130, 0, 135, 2, 1, 1, 5)]  // frais exactement couverts
    [InlineData(10, 190, 10, 0, 0, 1, 0)]   // pile au plafond
    [InlineData(100, 100, 100, 2, 0, 0, 0)] // 100 : pas de frais, pile au solde
    public void Withdraw_Boundaries(int amount, int withdrawn, int balance, int fifties, int twenties, int tens, int fee)
    {
        var dispenser = new MoneyDispenser(Full);
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(fifties, twenties, tens), fee,
            new AccountState(balance - amount - fee, withdrawn + amount)), result);
        Assert.Equal(Full - N(fifties, twenties, tens), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_FeeOnlyAppliedOnSuccess_NotOnCannotDispense()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new AccountState(250, 0)), result);
    }

    [Fact]
    public void Withdraw_TenJammed_AmountNeedingTens_CannotDispense()
    {
        // 30 : sans billet de 10, ni 50 ni 20 ne font 30.
        var dispenser = new MoneyDispenser(Full, Denomination.Ten);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(30));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new AccountState(250, 0)), result);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Withdraw_NotGreedyOnFifties()
    {
        // 110 avec 50×2 20×3 sans 10 : 50×2 laisse 10 impossible ; 50×1 20×3 est la seule.
        var result = new CashMachine(new MoneyDispenser(N(2, 3, 0))).Execute(new AccountState(250, 0), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 3, 0), 5, new AccountState(135, 110)), result);
    }

    [Fact]
    public void Deposit_ThreeNotes_CreditsEighty_AndKeepsWithdrawnToday()
    {
        var dispenser = new MoneyDispenser(N(4, 4, 4));
        var result = new CashMachine(dispenser).Execute(new AccountState(170, 200), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(250, 200)), result);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Deposit_ContainingJammedDenomination_RefusedEvenWithOtherNotes(Denomination jammed)
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(N(0, 0, 0), jammed));
        var result = new CashMachine(dispenser).Execute(new AccountState(0, 0), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, new AccountState(0, 0)), result);
        Assert.Empty(dispenser.LoadCalls);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Deposit_FullCassetteOfEachDenomination_IsCassetteFull(Denomination denomination)
    {
        var stock = denomination switch
        {
            Denomination.Fifty => N(5, 0, 0),
            Denomination.Twenty => N(0, 5, 0),
            _ => N(0, 0, 5),
        };
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock));
        var result = new CashMachine(dispenser).Execute(new AccountState(0, 0), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, new AccountState(0, 0)), result);
        Assert.Empty(dispenser.LoadCalls);
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void Deposit_JammedCassetteFull_ButNotTouched_IsAccepted()
    {
        // La cassette bloquée est pleine, mais le dépôt n'y touche pas.
        var dispenser = new MoneyDispenser(N(5, 4, 0), Denomination.Fifty);
        var result = new CashMachine(dispenser).Execute(new AccountState(0, 0), new Operation.Deposit(N(0, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(30, 0)), result);
        Assert.Equal(N(5, 5, 1), dispenser.Stock);
    }
}
