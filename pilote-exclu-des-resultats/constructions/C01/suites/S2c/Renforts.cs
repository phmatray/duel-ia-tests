namespace Atm.Tests;

/// <summary>Tests complémentaires : domaine complet des comptes, pannes en cours de retrait, absence d'effets de bord
/// sur refus, découpage en passages et absence de mémoire de la machine.</summary>
public sealed class Renforts
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    private static readonly Denomination?[] JamStates = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
            for (var t = 0; t <= 5; t++)
                for (var c = 0; c <= 5; c++)
                    yield return new NoteCounts(f, t, c);
    }

    private static IEnumerable<int> AllAmounts() =>
        new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));

    private static IEnumerable<int> AllBalances() => Enumerable.Range(0, 51).Select(i => i * 5);

    private static IEnumerable<int> AllWithdrawn() => Enumerable.Range(0, 21).Select(i => i * 10);

    private static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var mask = 0; mask < 8; mask++)
            yield return new NoteCounts(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
    }

    /// <summary>La combinaison au moins de billets, calculée par force brute à partir de la spécification.</summary>
    private static NoteCounts? Fewest(int amount, NoteCounts stock, Denomination? jammed)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= (jammed == Denomination.Fifty ? 0 : stock.Fifties); f++)
            for (var t = 0; t <= (jammed == Denomination.Twenty ? 0 : stock.Twenties); t++)
                for (var c = 0; c <= (jammed == Denomination.Ten ? 0 : stock.Tens); c++)
                {
                    var n = new NoteCounts(f, t, c);
                    if (n.Value == amount && (best is not { } b || n.Total < b.Total)) best = n;
                }

        return best;
    }

    private static AtmResult Expected(AccountState account, int amount, NoteCounts? combination)
    {
        var fee = amount > 100 ? 5 : 0;
        var refused = (AtmStatus status) => new AtmResult(status, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return refused(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return refused(AtmStatus.DailyLimitExceeded);
        if (amount + fee > account.Balance) return refused(AtmStatus.InsufficientFunds);
        if (combination is not { } notes) return refused(AtmStatus.CannotDispense);

        return new AtmResult(AtmStatus.Ok, notes, fee,
            new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    // ---- Domaine complet des comptes ----

    /// <summary>Tous les soldes, tous les retraits du jour et tous les montants du domaine, pour chaque cassette bloquée
    /// et un éventail de stocks.</summary>
    [Theory]
    [InlineData(5, 5, 5)]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(5, 5, 0)]
    [InlineData(0, 5, 5)]
    [InlineData(5, 0, 5)]
    [InlineData(2, 3, 1)]
    [InlineData(4, 0, 2)]
    public void Withdraw_AllAccounts_MatchesSpecification(int fifties, int twenties, int tens)
    {
        var stock = new NoteCounts(fifties, twenties, tens);
        foreach (var jammed in JamStates)
        foreach (var amount in AllAmounts())
        {
            var combination = Fewest(amount, stock, jammed);
            foreach (var balance in AllBalances())
            foreach (var withdrawn in AllWithdrawn())
            {
                var account = new AccountState(balance, withdrawn);
                var dispenser = new MoneyDispenser(stock, jammed);

                var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

                var expected = Expected(account, amount, combination);
                Assert.Equal(expected, result);
                Assert.Equal(stock - expected.Notes, dispenser.Stock);
            }
        }
    }

    /// <summary>Tout le domaine du stock, des cassettes bloquées et des dépôts, pour plusieurs comptes.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 200)]
    [InlineData(250, 0)]
    [InlineData(250, 200)]
    [InlineData(5, 10)]
    [InlineData(135, 70)]
    public void Deposit_AllStocks_MatchesSpecification(int balance, int withdrawn)
    {
        var account = new AccountState(balance, withdrawn);
        foreach (var jammed in JamStates)
        foreach (var stock in AllStocks())
        foreach (var notes in AllDeposits())
        {
            var dispenser = new MoneyDispenser(stock, jammed);

            var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));

            var after = stock + notes;
            var expected =
                notes.Total == 0 ? AtmStatus.InvalidAmount
                : jammed is { } j && notes.Of(j) > 0 ? AtmStatus.CassetteJammed
                : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
                : AtmStatus.Ok;

            if (expected == AtmStatus.Ok)
            {
                Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(balance + notes.Value, withdrawn)), result);
                Assert.Equal(after, dispenser.Stock);
            }
            else
            {
                Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
                Assert.Equal(stock, dispenser.Stock);
            }
        }
    }

    // ---- Bornes explicites ----

    [Theory]
    [InlineData(100, 0, 100, AtmStatus.Ok)]                    // 100 € : pas de frais, solde tout juste suffisant
    [InlineData(95, 0, 100, AtmStatus.InsufficientFunds)]
    [InlineData(115, 0, 110, AtmStatus.Ok)]                    // 110 € + 5 € de frais = 115
    [InlineData(110, 0, 110, AtmStatus.InsufficientFunds)]
    [InlineData(250, 190, 10, AtmStatus.Ok)]                   // plafond atteint exactement
    [InlineData(250, 200, 10, AtmStatus.DailyLimitExceeded)]
    [InlineData(250, 0, 210, AtmStatus.DailyLimitExceeded)]
    [InlineData(205, 0, 200, AtmStatus.Ok)]                    // frais hors plafond
    [InlineData(200, 0, 200, AtmStatus.InsufficientFunds)]
    [InlineData(0, 0, 10, AtmStatus.InsufficientFunds)]
    [InlineData(10, 0, 10, AtmStatus.Ok)]
    public void Withdraw_Boundaries(int balance, int withdrawn, int amount, AtmStatus status)
    {
        var result = new CashMachine(new MoneyDispenser(Full)).Execute(new(balance, withdrawn), new Operation.Withdraw(amount));

        Assert.Equal(status, result.Status);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(200, 5)]
    public void Withdraw_Fee(int amount, int fee)
    {
        var result = new CashMachine(new MoneyDispenser(Full)).Execute(new(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(new AccountState(250 - amount - fee, amount), result.Account);
        Assert.Equal(amount, result.Notes.Value);
    }

    // ---- Passages ----

    /// <summary>Chaque retrait réussi sort exactement la combinaison, en ⌈n/4⌉ passages non vides d'au plus 4 billets.</summary>
    [Fact]
    public void Withdraw_Passes_AreMinimalAndSumToCombination()
    {
        foreach (var jammed in JamStates)
        foreach (var stock in AllStocks())
        foreach (var amount in Enumerable.Range(1, 20).Select(i => i * 10))
        {
            var dispenser = new FakeDispenser(stock, jammed);

            var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(amount));

            if (result.Status != AtmStatus.Ok)
            {
                Assert.Empty(dispenser.DispenseCalls);
                continue;
            }

            Assert.All(dispenser.DispenseCalls, pass =>
            {
                Assert.InRange(pass.Total, 1, 4);
                Assert.True(pass is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0 });
            });
            Assert.Equal(result.Notes, dispenser.DispenseCalls.Aggregate(NoteCounts.None, (a, b) => a + b));
            Assert.Equal((result.Notes.Total + 3) / 4, dispenser.DispenseCalls.Count);
            Assert.Empty(dispenser.LoadCalls);
            Assert.Equal(stock - result.Notes, dispenser.Stock);
        }
    }

    [Fact]
    public void Withdraw_FourNotes_SinglePass()
    {
        var dispenser = new FakeDispenser(Full);

        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(200));

        Assert.Equal(new NoteCounts(4, 0, 0), result.Notes);
        Assert.Equal([new NoteCounts(4, 0, 0)], dispenser.DispenseCalls);
    }

    [Fact]
    public void Withdraw_FiveNotes_TwoPasses()
    {
        // 90 € avec les 50 bloqués : 20×4 + 10×1 = 5 billets.
        var dispenser = new FakeDispenser(Full, Denomination.Fifty);

        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(90));

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 4, 1), 0, new(160, 90)), result);
        Assert.Equal(2, dispenser.DispenseCalls.Count);
        Assert.Equal(new NoteCounts(5, 1, 4), dispenser.Stock);
    }

    // ---- Pannes ----

    [Fact]
    public void Withdraw_FaultOnThirdPass_ReturnsAllNotesAlreadyDispensed()
    {
        // 150 € avec les 50 bloqués : 20×5 + 10×5 = 10 billets → 3 passages.
        var dispenser = new FakeDispenser(Full, Denomination.Fifty) { FailDispenseAt = 3 };

        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(3, dispenser.DispenseCalls.Count);
        Assert.Equal(dispenser.DispenseCalls[0] + dispenser.DispenseCalls[1], result.Notes);
        Assert.Equal(8, result.Notes.Total);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
        Assert.Equal(Full - result.Notes, dispenser.Stock);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void Withdraw_FaultOnSecondOfTwoPasses_StopsAndKeepsAccount()
    {
        var dispenser = new FakeDispenser(Full, Denomination.Fifty) { FailDispenseAt = 2 };

        var result = new CashMachine(dispenser).Execute(new(250, 40), new Operation.Withdraw(90));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(2, dispenser.DispenseCalls.Count);
        Assert.Equal(dispenser.DispenseCalls[0], result.Notes);
        Assert.Equal(4, result.Notes.Total);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 40), result.Account);
        Assert.Equal(Full - result.Notes, dispenser.Stock);
    }

    [Fact]
    public void Withdraw_FaultOnFirstPass_WithFee_NoFeeCharged()
    {
        var dispenser = new FakeDispenser(Full) { FailDispenseAt = 1 };

        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new(250, 0)), result);
        Assert.Single(dispenser.DispenseCalls);
        Assert.Equal(Full, dispenser.Stock);
    }

    /// <summary>Un module qui annonce plus de billets qu'il n'en a : son refus est une panne, pas un refus de la machine.</summary>
    [Fact]
    public void Withdraw_ModuleRefusesDespiteChecks_IsDispenserFault()
    {
        var dispenser = new ScriptedDispenser(Full) { AcceptDispense = false };

        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new(250, 0)), result);
        Assert.Single(dispenser.DispenseCalls);
    }

    [Fact]
    public void Deposit_LoadFails_LoadsExactlyTheDepositOnce()
    {
        var dispenser = new FakeDispenser(new(1, 2, 3), Denomination.Fifty) { FailLoad = true };

        var result = new CashMachine(dispenser).Execute(new(250, 200), new Operation.Deposit(new(0, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new(250, 200)), result);
        Assert.Equal([new NoteCounts(0, 1, 1)], dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Equal(new NoteCounts(1, 2, 3), dispenser.Stock);
    }

    [Fact]
    public void Deposit_Ok_LoadsExactlyTheDepositOnce()
    {
        var dispenser = new FakeDispenser(new(1, 2, 3));

        var result = new CashMachine(dispenser).Execute(new(0, 200), new Operation.Deposit(new(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(80, 200)), result);
        Assert.Equal([new NoteCounts(1, 1, 1)], dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Equal(new NoteCounts(2, 3, 4), dispenser.Stock);
    }

    // ---- Refus : le module n'est pas sollicité ----

    [Theory]
    [InlineData(250, 0, -10, AtmStatus.InvalidAmount)]
    [InlineData(250, 0, 0, AtmStatus.InvalidAmount)]
    [InlineData(250, 0, 5, AtmStatus.InvalidAmount)]
    [InlineData(250, 150, 60, AtmStatus.DailyLimitExceeded)]
    [InlineData(110, 0, 110, AtmStatus.InsufficientFunds)]
    public void Withdraw_Refused_ModuleUntouched(int balance, int withdrawn, int amount, AtmStatus status)
    {
        // Un module qui accepterait tout : aucun billet ne doit pourtant sortir.
        var dispenser = new ScriptedDispenser(Full);

        var result = new CashMachine(dispenser).Execute(new(balance, withdrawn), new Operation.Withdraw(amount));

        Assert.Equal(new AtmResult(status, NoteCounts.None, 0, new(balance, withdrawn)), result);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void Withdraw_CannotDispense_ModuleUntouched()
    {
        var dispenser = new ScriptedDispenser(new(1, 0, 0), Denomination.Fifty);

        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new(250, 0)), result);
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Theory]
    [InlineData(0, 0, 0, null, AtmStatus.InvalidAmount)]
    [InlineData(1, 0, 0, Denomination.Fifty, AtmStatus.CassetteJammed)]
    [InlineData(0, 0, 1, Denomination.Ten, AtmStatus.CassetteJammed)]
    [InlineData(0, 1, 0, null, AtmStatus.CassetteFull)]
    [InlineData(1, 1, 1, null, AtmStatus.CassetteFull)]
    public void Deposit_Refused_ModuleUntouched(int fifties, int twenties, int tens, Denomination? jammed, AtmStatus status)
    {
        // Un module qui accepterait tout : il ne doit pourtant rien recevoir.
        var dispenser = new ScriptedDispenser(new(0, 5, 0), jammed);

        var result = new CashMachine(dispenser).Execute(new(10, 30), new Operation.Deposit(new(fifties, twenties, tens)));

        Assert.Equal(new AtmResult(status, NoteCounts.None, 0, new(10, 30)), result);
        Assert.Empty(dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Fact]
    public void Deposit_FillsCassetteExactly_IsAccepted()
    {
        var dispenser = new MoneyDispenser(new(4, 4, 4));

        var result = new CashMachine(dispenser).Execute(new(0, 0), new Operation.Deposit(new(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(80, 0)), result);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Deposit_JammedCassetteFull_OtherNotesAccepted()
    {
        var dispenser = new MoneyDispenser(new(5, 0, 0), Denomination.Fifty);

        var result = new CashMachine(dispenser).Execute(new(0, 0), new Operation.Deposit(new(0, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(30, 0)), result);
        Assert.Equal(new NoteCounts(5, 1, 1), dispenser.Stock);
    }

    // ---- Sans mémoire entre deux appels ----

    [Fact]
    public void SameMachine_RepeatedWithdrawals_DependOnlyOnInputs()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new(250, 0), new Operation.Withdraw(100));
        var second = machine.Execute(new(250, 0), new Operation.Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 0), 0, new(150, 100)), first);
        Assert.Equal(first, second);
        Assert.Equal(new NoteCounts(1, 5, 5), dispenser.Stock);

        // Un compte différent sur la même machine : seul le compte transmis compte.
        var third = machine.Execute(new(30, 190), new Operation.Withdraw(10));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 0, 1), 0, new(20, 200)), third);
    }

    [Fact]
    public void SameMachine_UsesCurrentStock()
    {
        var dispenser = new MoneyDispenser(new(1, 0, 1));
        var machine = new CashMachine(dispenser);

        Assert.Equal(new NoteCounts(1, 0, 0), machine.Execute(new(250, 0), new Operation.Withdraw(50)).Notes);

        // Le billet de 50 est parti : 60 € n'est plus faisable.
        var refused = machine.Execute(new(250, 0), new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new(250, 0)), refused);

        // Un dépôt le rend à nouveau possible.
        Assert.Equal(AtmStatus.Ok, machine.Execute(new(0, 0), new Operation.Deposit(new(1, 0, 0))).Status);
        var again = machine.Execute(new(250, 0), new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 1), 0, new(190, 60)), again);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void SameMachine_DepositsFillCassetteThenRefuse()
    {
        var dispenser = new MoneyDispenser(new(0, 3, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, machine.Execute(new(0, 0), new Operation.Deposit(new(0, 1, 0))).Status);
        Assert.Equal(AtmStatus.Ok, machine.Execute(new(0, 0), new Operation.Deposit(new(0, 1, 0))).Status);
        var full = machine.Execute(new(0, 0), new Operation.Deposit(new(0, 1, 0)));

        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, new(0, 0)), full);
        Assert.Equal(new NoteCounts(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void SameMachine_AfterFault_NextCallUnaffected()
    {
        var dispenser = new FakeDispenser(Full) { FailDispenseAt = 1 };
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.DispenserFault, machine.Execute(new(250, 0), new Operation.Withdraw(50)).Status);
        var next = machine.Execute(new(250, 0), new Operation.Withdraw(50));

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 0), 0, new(200, 50)), next);
        Assert.Equal(new NoteCounts(4, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void SameMachine_ReadsJammedCassetteAtEachCall()
    {
        var dispenser = new ScriptedDispenser(Full);
        var machine = new CashMachine(dispenser);

        Assert.Equal(new NoteCounts(1, 0, 0), machine.Execute(new(250, 0), new Operation.Withdraw(50)).Notes);

        dispenser.Jammed = Denomination.Fifty;
        Assert.Equal(new NoteCounts(0, 2, 1), machine.Execute(new(250, 0), new Operation.Withdraw(50)).Notes);
        Assert.Equal(AtmStatus.CassetteJammed, machine.Execute(new(0, 0), new Operation.Deposit(new(1, 0, 0))).Status);

        dispenser.Jammed = Denomination.Twenty;
        Assert.Equal(new NoteCounts(1, 0, 0), machine.Execute(new(250, 0), new Operation.Withdraw(50)).Notes);
        Assert.Equal(new NoteCounts(0, 0, 4), machine.Execute(new(250, 0), new Operation.Withdraw(40)).Notes);

        dispenser.Jammed = null;
        Assert.Equal(new NoteCounts(0, 2, 0), machine.Execute(new(250, 0), new Operation.Withdraw(40)).Notes);
    }

    /// <summary>Module entièrement piloté par le test : stock et cassette bloquée modifiables, appels enregistrés,
    /// et accepte par défaut tout ce qu'on lui demande.</summary>
    private sealed class ScriptedDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
    {
        public NoteCounts Stock { get; set; } = stock;

        public Denomination? Jammed { get; set; } = jammed;

        public bool AcceptDispense { get; init; } = true;

        public List<NoteCounts> DispenseCalls { get; } = [];

        public List<NoteCounts> LoadCalls { get; } = [];

        public bool Dispense(NoteCounts notes)
        {
            DispenseCalls.Add(notes);

            return AcceptDispense;
        }

        public bool Load(NoteCounts notes)
        {
            LoadCalls.Add(notes);

            return true;
        }
    }
}
