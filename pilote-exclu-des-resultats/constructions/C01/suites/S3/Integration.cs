namespace Atm.Tests;

/// <summary>
/// Tests d'intégration : <see cref="CashMachine"/> travaille avec le vrai <see cref="MoneyDispenser"/>, sans doublure.
/// Chaque opération est vérifiée sur son résultat et sur le stock du module qui en résulte. Les scénarios enchaînent
/// plusieurs opérations sur le même module : ce que l'une fait au stock, la suivante doit le voir.
/// </summary>
public sealed class Integration
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    private static readonly Denomination?[] JamStates = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] DomainAmounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly NoteCounts[] DomainDeposits =
        Enumerable.Range(0, 8).Select(m => new NoteCounts(m & 1, (m >> 1) & 1, (m >> 2) & 1)).ToArray();

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
            for (var t = 0; t <= 5; t++)
                for (var c = 0; c <= 5; c++)
                    yield return new NoteCounts(f, t, c);
    }

    // ---- Modèle écrit depuis la spécification ----

    /// <summary>Ce que la spécification prévoit : le résultat et le stock du module après l'opération.</summary>
    private static (AtmResult Result, NoteCounts Stock) Expected(AccountState account, Operation operation, NoteCounts stock,
        Denomination? jammed)
    {
        AtmResult Refused(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        switch (operation)
        {
            case Operation.Withdraw(var amount):
            {
                var fee = amount > 100 ? 5 : 0;
                if (amount <= 0 || amount % 10 != 0) return (Refused(AtmStatus.InvalidAmount), stock);
                if (account.WithdrawnToday + amount > 200) return (Refused(AtmStatus.DailyLimitExceeded), stock);
                if (amount + fee > account.Balance) return (Refused(AtmStatus.InsufficientFunds), stock);
                if (FewestNotes(amount, stock, jammed) is not { } notes) return (Refused(AtmStatus.CannotDispense), stock);

                var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);

                return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
            }
            case Operation.Deposit(var notes):
            {
                var after = stock + notes;
                if (notes.Total == 0) return (Refused(AtmStatus.InvalidAmount), stock);
                if (jammed is { } j && notes.Of(j) > 0) return (Refused(AtmStatus.CassetteJammed), stock);
                if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return (Refused(AtmStatus.CassetteFull), stock);

                return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }),
                    after);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    /// <summary>Recherche exhaustive de la combinaison exacte qui compte le moins de billets.</summary>
    private static NoteCounts? FewestNotes(int amount, NoteCounts stock, Denomination? jammed)
    {
        NoteCounts? best = null;
        var ties = 0;
        for (var f = 0; f <= (jammed == Denomination.Fifty ? 0 : stock.Fifties); f++)
            for (var t = 0; t <= (jammed == Denomination.Twenty ? 0 : stock.Twenties); t++)
                for (var c = 0; c <= (jammed == Denomination.Ten ? 0 : stock.Tens); c++)
                {
                    var candidate = new NoteCounts(f, t, c);
                    if (candidate.Value != amount) continue;
                    if (best is not { } current || candidate.Total < current.Total) (best, ties) = (candidate, 1);
                    else if (candidate.Total == current.Total) ties++;
                }

        Assert.True(ties <= 1, "la spécification garantit l'unicité de la combinaison");

        return best;
    }

    /// <summary>
    /// Exécute l'opération sur la machine, vérifie le résultat et le stock du module contre le modèle,
    /// et rend le compte après l'opération pour l'enchaîner.
    /// </summary>
    private static AtmResult Step(CashMachine machine, MoneyDispenser dispenser, AccountState account, Operation operation)
    {
        var before = dispenser.Stock;
        var (expected, expectedStock) = Expected(account, operation, before, dispenser.Jammed);

        var result = machine.Execute(account, operation);

        Assert.Equal(expected, result);
        Assert.Equal(expectedStock, dispenser.Stock);
        // Conservation des billets : ce qui sort du module est exactement ce que le résultat annonce.
        if (operation is Operation.Withdraw) Assert.Equal(before - result.Notes, dispenser.Stock);

        return result;
    }

    private static Operation W(int amount) => new Operation.Withdraw(amount);

    private static Operation D(int fifties, int twenties, int tens) => new Operation.Deposit(new(fifties, twenties, tens));

    // ---- Scénarios enchaînés sur un même module ----

    [Fact]
    public void WithdrawThenDepositThenWithdraw_StockCarriesOver()
    {
        var dispenser = new MoneyDispenser(new(1, 3, 0));
        var machine = new CashMachine(dispenser);

        var r1 = machine.Execute(new(100, 0), W(60));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 3, 0), 0, new(40, 60)), r1);
        Assert.Equal(new NoteCounts(1, 0, 0), dispenser.Stock);

        // Plus de billets de 20 : 70 € n'est plus possible…
        var r2 = machine.Execute(r1.Account, W(30));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new(40, 60)), r2);
        Assert.Equal(new NoteCounts(1, 0, 0), dispenser.Stock);

        // … jusqu'à ce qu'un dépôt apporte les billets manquants.
        var r3 = machine.Execute(r2.Account, D(0, 1, 1));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(70, 60)), r3);
        Assert.Equal(new NoteCounts(1, 1, 1), dispenser.Stock);

        var r4 = machine.Execute(r3.Account, W(30));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 1, 1), 0, new(40, 90)), r4);
        Assert.Equal(new NoteCounts(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void RepeatedWithdrawal_ResultFollowsTheStock()
    {
        // Même compte, même opération : seul le stock du module change, et le résultat avec lui.
        var dispenser = new MoneyDispenser(new(1, 0, 5));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        var first = machine.Execute(account, W(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 0), 0, new(200, 50)), first);
        Assert.Equal(new NoteCounts(0, 0, 5), dispenser.Stock);

        // Cinq billets de 10 : le vrai module refuse plus de 4 billets par passage, il en faut deux.
        var second = machine.Execute(account, W(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 0, 5), 0, new(200, 50)), second);
        Assert.Equal(NoteCounts.None, dispenser.Stock);

        var third = machine.Execute(account, W(50));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, account), third);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void DrainingTheMachine_OverSeveralDays()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);

        // Jour 1 : 200 € en quatre billets de 50, un seul passage.
        var day1 = Step(machine, dispenser, new(250, 0), W(200));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(4, 0, 0), 5, new(45, 200)), day1);
        Assert.Equal(new NoteCounts(1, 5, 5), dispenser.Stock);

        // Le plafond du jour est atteint, même pour 10 €.
        var refused = Step(machine, dispenser, day1.Account, W(10));
        Assert.Equal(AtmStatus.DailyLimitExceeded, refused.Status);
        Assert.Equal(new NoteCounts(1, 5, 5), dispenser.Stock);

        // Jour 2 : il ne reste qu'un billet de 50 ; 200 € = 50 + 20×5 + 10×5, onze billets, trois passages.
        var day2 = Step(machine, dispenser, new(250, 0), W(200));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 5, 5), 5, new(45, 200)), day2);
        Assert.Equal(NoteCounts.None, dispenser.Stock);

        // Jour 3 : la machine est vide.
        var day3 = Step(machine, dispenser, new(250, 0), W(10));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new(250, 0)), day3);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void FullCassette_AcceptsDepositOnceAWithdrawalMadeRoom()
    {
        var dispenser = new MoneyDispenser(new(0, 5, 0));
        var machine = new CashMachine(dispenser);

        var full = machine.Execute(new(10, 0), D(0, 1, 0));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, new(10, 0)), full);
        Assert.Equal(new NoteCounts(0, 5, 0), dispenser.Stock);

        var withdraw = machine.Execute(new(100, 0), W(20));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 1, 0), 0, new(80, 20)), withdraw);
        Assert.Equal(new NoteCounts(0, 4, 0), dispenser.Stock);

        var deposit = machine.Execute(withdraw.Account, D(0, 1, 0));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(100, 20)), deposit);
        Assert.Equal(new NoteCounts(0, 5, 0), dispenser.Stock);

        // Et la cassette est de nouveau pleine.
        var again = machine.Execute(deposit.Account, D(0, 1, 0));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, new(100, 20)), again);
        Assert.Equal(new NoteCounts(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void FillingAnEmptyMachine_ByDeposits_ThenWithdrawingEverything()
    {
        var dispenser = new MoneyDispenser(NoteCounts.None);
        var machine = new CashMachine(dispenser);

        // Cinq clients déposent chacun un billet de chaque coupure.
        for (var i = 1; i <= 5; i++)
        {
            var deposit = Step(machine, dispenser, new(0, 30), D(1, 1, 1));
            Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(80, 30)), deposit);
            Assert.Equal(new NoteCounts(i, i, i), dispenser.Stock);
        }

        Assert.Equal(AtmStatus.CassetteFull, Step(machine, dispenser, new(0, 0), D(0, 0, 1)).Status);

        // Puis la machine est vidée, sur plusieurs jours.
        var account = new AccountState(250, 0);
        var r1 = Step(machine, dispenser, account, W(200));
        Assert.Equal(new NoteCounts(4, 0, 0), r1.Notes);
        var r2 = Step(machine, dispenser, account, W(200));
        Assert.Equal(new NoteCounts(1, 5, 5), r2.Notes);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void JammedCassette_IsNeverTouched_AcrossASequence()
    {
        var dispenser = new MoneyDispenser(new(3, 2, 2), Denomination.Fifty);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        (Operation Operation, AtmStatus Status, NoteCounts StockAfter)[] steps =
        [
            (W(50), AtmStatus.Ok, new(3, 0, 1)), // 20×2 + 10×1, malgré trois billets de 50
            (D(1, 0, 0), AtmStatus.CassetteJammed, new(3, 0, 1)),
            (D(1, 1, 0), AtmStatus.CassetteJammed, new(3, 0, 1)),
            (D(0, 1, 1), AtmStatus.Ok, new(3, 1, 2)),
            (W(50), AtmStatus.CannotDispense, new(3, 1, 2)),
            (W(40), AtmStatus.Ok, new(3, 0, 0)), // 20×1 + 10×2
            (D(0, 1, 1), AtmStatus.Ok, new(3, 1, 1)),
            (W(10), AtmStatus.Ok, new(3, 1, 0)),
            (W(20), AtmStatus.Ok, new(3, 0, 0)),
            (W(10), AtmStatus.CannotDispense, new(3, 0, 0)),
        ];

        foreach (var (operation, status, stockAfter) in steps)
        {
            var result = Step(machine, dispenser, account, operation);
            Assert.Equal(status, result.Status);
            Assert.Equal(stockAfter, dispenser.Stock);
            Assert.Equal(0, result.Notes.Fifties);
            Assert.Equal(Denomination.Fifty, dispenser.Jammed);
            account = result.Account;
        }

        Assert.Equal(new AccountState(190, 120), account);
    }

    [Fact]
    public void Refusals_LeaveTheModuleUntouched_AcrossASequence()
    {
        var stock = new NoteCounts(5, 1, 0);
        var dispenser = new MoneyDispenser(stock, Denomination.Twenty);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(110, 150);

        (Operation Operation, AtmStatus Status)[] refusals =
        [
            (W(-10), AtmStatus.InvalidAmount),
            (W(0), AtmStatus.InvalidAmount),
            (W(5), AtmStatus.InvalidAmount),
            (W(60), AtmStatus.DailyLimitExceeded),
            (W(20), AtmStatus.CannotDispense), // le seul billet de 20 est dans la cassette bloquée
            (W(10), AtmStatus.CannotDispense),
            (D(0, 0, 0), AtmStatus.InvalidAmount),
            (D(0, 1, 0), AtmStatus.CassetteJammed),
            (D(1, 1, 1), AtmStatus.CassetteJammed),
            (D(1, 0, 0), AtmStatus.CassetteFull),
            (D(1, 0, 1), AtmStatus.CassetteFull),
        ];

        foreach (var (operation, status) in refusals)
        {
            var result = machine.Execute(account, operation);
            Assert.Equal(new AtmResult(status, NoteCounts.None, 0, account), result);
            Assert.Equal(stock, dispenser.Stock);
        }

        // Après tous ces refus, la machine fonctionne normalement.
        var ok = machine.Execute(account, W(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 0), 0, new(60, 200)), ok);
        Assert.Equal(new NoteCounts(4, 1, 0), dispenser.Stock);

        var funds = machine.Execute(new(40, 0), W(50));
        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, new(40, 0)), funds);
        Assert.Equal(new NoteCounts(4, 1, 0), dispenser.Stock);
    }

    [Fact]
    public void DailyLimit_AccumulatesAcrossChainedWithdrawals()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        foreach (var amount in new[] { 50, 50, 50 })
        {
            account = Step(machine, dispenser, account, W(amount)).Account;
        }

        Assert.Equal(new AccountState(100, 150), account);
        Assert.Equal(new NoteCounts(2, 5, 5), dispenser.Stock);

        Assert.Equal(AtmStatus.DailyLimitExceeded, Step(machine, dispenser, account, W(60)).Status);

        // Un dépôt ne rend pas de marge sur le plafond du jour.
        account = Step(machine, dispenser, account, D(1, 0, 0)).Account;
        Assert.Equal(new AccountState(150, 150), account);
        Assert.Equal(AtmStatus.DailyLimitExceeded, Step(machine, dispenser, account, W(60)).Status);

        var last = Step(machine, dispenser, account, W(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 0), 0, new(100, 200)), last);
        Assert.Equal(new NoteCounts(2, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Fees_AreChargedOnEachLargeWithdrawal_AndBalanceRunsOut()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);

        var r1 = Step(machine, dispenser, new(230, 0), W(110));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 1), 5, new(115, 110)), r1);

        // Lendemain : il reste 115 €, soit exactement 110 € plus les frais.
        var r2 = Step(machine, dispenser, r1.Account with { WithdrawnToday = 0 }, W(110));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 1), 5, new(0, 110)), r2);
        Assert.Equal(new NoteCounts(1, 5, 3), dispenser.Stock);

        var r3 = Step(machine, dispenser, r2.Account with { WithdrawnToday = 0 }, W(10));
        Assert.Equal(AtmStatus.InsufficientFunds, r3.Status);
        Assert.Equal(new NoteCounts(1, 5, 3), dispenser.Stock);
    }

    [Fact]
    public void TwoMachinesSharingOneModule_SeeEachOthersChanges()
    {
        // La machine est sans mémoire : elle lit le stock du module à chaque appel.
        var dispenser = new MoneyDispenser(new(1, 0, 0));
        var first = new CashMachine(dispenser);
        var second = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, first.Execute(new(250, 0), W(50)).Status);
        Assert.Equal(NoteCounts.None, dispenser.Stock);

        Assert.Equal(AtmStatus.CannotDispense, second.Execute(new(250, 0), W(50)).Status);

        Assert.Equal(AtmStatus.Ok, second.Execute(new(0, 0), D(1, 0, 0)).Status);
        Assert.Equal(new NoteCounts(1, 0, 0), dispenser.Stock);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 0), 0, new(200, 50)), first.Execute(new(250, 0), W(50)));
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void FreshMachineOnTheSameModule_BehavesLikeTheOldOne()
    {
        Operation[] operations = [W(60), D(0, 1, 0), W(140), D(1, 0, 1), W(30), W(200), D(1, 1, 1), W(90)];

        var shared = new MoneyDispenser(new(2, 3, 4), Denomination.Ten);
        var sharedMachine = new CashMachine(shared);
        var other = new MoneyDispenser(new(2, 3, 4), Denomination.Ten);
        var account = new AccountState(250, 0);

        foreach (var operation in operations)
        {
            var fromOld = sharedMachine.Execute(account, operation);
            var fromFresh = new CashMachine(other).Execute(account, operation);

            Assert.Equal(fromOld, fromFresh);
            Assert.Equal(shared.Stock, other.Stock);
            account = fromOld.Account with { WithdrawnToday = 0 };
        }
    }

    // ---- Passages réels de 4 billets au plus ----

    [Theory]
    [InlineData(Denomination.Fifty, 140, 0, 5, 4)] // 9 billets : 3 passages
    [InlineData(Denomination.Fifty, 150, 0, 5, 5)] // 10 billets
    [InlineData(Denomination.Twenty, 100, 2, 0, 0)]
    [InlineData(Denomination.Twenty, 190, 3, 0, 4)] // 7 billets
    [InlineData(Denomination.Ten, 200, 4, 0, 0)]
    [InlineData(Denomination.Ten, 190, 3, 2, 0)]
    [InlineData(null, 190, 3, 2, 0)]
    public void LargeWithdrawals_GoThroughTheRealModule(Denomination? jammed, int amount, int fifties, int twenties, int tens)
    {
        var dispenser = new MoneyDispenser(Full, jammed);

        var result = new CashMachine(dispenser).Execute(new(250, 0), W(amount));

        var notes = new NoteCounts(fifties, twenties, tens);
        Assert.Equal(new AtmResult(AtmStatus.Ok, notes, amount > 100 ? 5 : 0, new(250 - amount - (amount > 100 ? 5 : 0), amount)),
            result);
        Assert.Equal(Full - notes, dispenser.Stock);
    }

    // ---- Balayages du domaine ----

    [Fact]
    public void Withdraw_WholeAccountDomain_AgainstModel()
    {
        // Tous les soldes et tous les montants déjà retirés du domaine, sur des stocks représentatifs.
        (NoteCounts Stock, Denomination? Jammed)[] modules =
        [
            (Full, null), (Full, Denomination.Fifty), (Full, Denomination.Twenty), (Full, Denomination.Ten),
            (NoteCounts.None, null), (new(1, 3, 0), null), (new(5, 5, 0), null), (new(0, 5, 5), Denomination.Twenty),
            (new(2, 1, 1), Denomination.Ten), (new(1, 0, 5), null),
        ];

        foreach (var (stock, jammed) in modules)
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in DomainAmounts)
        {
            var dispenser = new MoneyDispenser(stock, jammed);
            Step(new CashMachine(dispenser), dispenser, new(balance, withdrawn), W(amount));
        }
    }

    [Fact]
    public void Deposit_WholeDomain_AgainstModel()
    {
        foreach (var jammed in JamStates)
        foreach (var stock in AllStocks())
        foreach (var notes in DomainDeposits)
        for (var balance = 0; balance <= 250; balance += 25)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 50)
        {
            var dispenser = new MoneyDispenser(stock, jammed);
            var result = Step(new CashMachine(dispenser), dispenser, new(balance, withdrawn), new Operation.Deposit(notes));
            Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
        }
    }

    [Fact]
    public void EveryPairOfOperations_OnTheSameModule_AgainstModel()
    {
        // Toute opération du domaine suivie de toute autre, sur le même module : la seconde voit le stock laissé par la
        // première. Le compte intermédiaire est ramené dans le domaine (solde ≤ 250) si un dépôt l'en a fait sortir.
        Operation[] operations =
            [.. DomainAmounts.Select(W), .. DomainDeposits.Select(n => (Operation)new Operation.Deposit(n))];

        foreach (var jammed in JamStates)
        foreach (var stock in AllStocks())
        foreach (var first in operations)
        {
            var reference = new MoneyDispenser(stock, jammed);
            var afterFirst = Step(new CashMachine(reference), reference, new(250, 0), first);
            var account = afterFirst.Account with { Balance = Math.Min(afterFirst.Account.Balance, 250) };

            foreach (var second in operations)
            {
                var dispenser = new MoneyDispenser(stock, jammed);
                var machine = new CashMachine(dispenser);
                machine.Execute(new(250, 0), first);
                Step(machine, dispenser, account, second);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RandomSequences_AgainstModel(int seed)
    {
        var random = new Random(seed);

        for (var walk = 0; walk < 500; walk++)
        {
            var initial = new NoteCounts(random.Next(6), random.Next(6), random.Next(6));
            var dispenser = new MoneyDispenser(initial, JamStates[random.Next(JamStates.Length)]);
            var machine = new CashMachine(dispenser);
            var account = new AccountState(5 * random.Next(51), 10 * random.Next(21));
            var fees = 0;
            var invariant = account.Balance - dispenser.Stock.Value;

            for (var step = 0; step < 40; step++)
            {
                if (random.Next(10) == 0) account = account with { WithdrawnToday = 0 }; // un nouveau jour

                // Les dépôts sont choisis pour que le solde reste dans le domaine (le dépôt vide convient toujours).
                var deposits = DomainDeposits.Where(n => account.Balance + n.Value <= 250).ToArray();
                Operation operation = random.Next(2) == 0
                    ? W(DomainAmounts[random.Next(DomainAmounts.Length)])
                    : new Operation.Deposit(deposits[random.Next(deposits.Length)]);

                var result = Step(machine, dispenser, account, operation);
                Assert.NotEqual(AtmStatus.DispenserFault, result.Status);

                account = result.Account;
                fees += result.Fee;

                // Le solde et le stock bougent ensemble, à la différence des frais près.
                Assert.Equal(invariant, account.Balance + fees - dispenser.Stock.Value);
                Assert.InRange(account.Balance, 0, 250);
                Assert.InRange(account.WithdrawnToday, 0, 200);
                if (dispenser.Jammed is { } jammed) Assert.Equal(initial.Of(jammed), dispenser.Stock.Of(jammed));
                Assert.True(dispenser.Stock is { Fifties: >= 0 and <= 5, Twenties: >= 0 and <= 5, Tens: >= 0 and <= 5 });
            }
        }
    }
}
