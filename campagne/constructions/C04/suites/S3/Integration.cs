namespace Atm.Tests;

/// <summary>
/// Tests d'intégration : <see cref="CashMachine"/> travaille avec le vrai <see cref="MoneyDispenser"/>, sans doublure.
/// Chaque opération est suivie d'une vérification du résultat et du stock du module.
/// </summary>
public class Integration
{
    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] DomainAmounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly NoteCounts[] DomainDeposits =
    [
        .. from f in Enumerable.Range(0, 2)
           from t in Enumerable.Range(0, 2)
           from n in Enumerable.Range(0, 2)
           select new NoteCounts(f, t, n),
    ];

    private static readonly NoteCounts[] DomainStocks =
    [
        .. from f in Enumerable.Range(0, 6)
           from t in Enumerable.Range(0, 6)
           from n in Enumerable.Range(0, 6)
           select new NoteCounts(f, t, n),
    ];

    private static NoteCounts Full => new(5, 5, 5);

    // ------------------------------------------------------------------------------------------------------------
    // Une session : un vrai module, une machine, et le compte qui passe d'une opération à la suivante.
    // ------------------------------------------------------------------------------------------------------------

    private sealed class Session(NoteCounts stock, Denomination? jammed, AccountState account)
    {
        public MoneyDispenser Dispenser { get; } = new(stock, jammed);

        public AccountState Account { get; private set; } = account;

        public CashMachine Machine => new(Dispenser);

        /// <summary>Exécute l'opération, vérifie le résultat et le stock, puis enchaîne sur le compte rendu.</summary>
        public void Step(Operation operation, AtmResult expected, NoteCounts expectedStock)
        {
            var jammedBefore = Dispenser.Jammed;
            var result = Machine.Execute(Account, operation);

            Assert.Equal(expected, result);
            Assert.Equal(expectedStock, Dispenser.Stock);
            Assert.Equal(jammedBefore, Dispenser.Jammed);

            Account = result.Account;
        }

        public void Withdraw(int amount, AtmResult expected, NoteCounts expectedStock) =>
            Step(new Operation.Withdraw(amount), expected, expectedStock);

        public void Deposit(NoteCounts notes, AtmResult expected, NoteCounts expectedStock) =>
            Step(new Operation.Deposit(notes), expected, expectedStock);

        /// <summary>Un refus : rien ne bouge, ni le compte ni le stock.</summary>
        public void Refused(Operation operation, AtmStatus status) =>
            Step(operation, new AtmResult(status, NoteCounts.None, 0, Account), Dispenser.Stock);
    }

    private static AtmResult Ok(NoteCounts notes, int fee, int balance, int withdrawnToday) =>
        new(AtmStatus.Ok, notes, fee, new AccountState(balance, withdrawnToday));

    private static AtmResult DepositOk(int balance, int withdrawnToday) =>
        new(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(balance, withdrawnToday));

    // ------------------------------------------------------------------------------------------------------------
    // Les exemples de la spécification, avec le vrai module et le stock vérifié.
    // ------------------------------------------------------------------------------------------------------------

    public static TheoryData<int, int, NoteCounts, Denomination?, Operation, AtmResult, NoteCounts> SpecExamples => new()
    {
        { 100, 0, new(1, 3, 0), null, new Operation.Withdraw(60), Ok(new(0, 3, 0), 0, 40, 60), new(1, 0, 0) },
        { 100, 0, new(1, 1, 1), null, new Operation.Withdraw(80), Ok(new(1, 1, 1), 0, 20, 80), new(0, 0, 0) },
        { 105, 0, new(5, 5, 5), null, new Operation.Withdraw(100), Ok(new(2, 0, 0), 0, 5, 100), new(3, 5, 5) },
        { 115, 0, new(5, 5, 5), null, new Operation.Withdraw(110), Ok(new(2, 0, 1), 5, 0, 110), new(3, 5, 4) },
        {
            110, 0, new(5, 5, 5), null, new Operation.Withdraw(110),
            new(AtmStatus.InsufficientFunds, NoteCounts.None, 0, new(110, 0)), new(5, 5, 5)
        },
        {
            250, 150, new(5, 5, 5), null, new Operation.Withdraw(60),
            new(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, new(250, 150)), new(5, 5, 5)
        },
        {
            250, 0, new(1, 0, 0), Denomination.Fifty, new Operation.Withdraw(50),
            new(AtmStatus.CannotDispense, NoteCounts.None, 0, new(250, 0)), new(1, 0, 0)
        },
        { 250, 0, new(2, 2, 0), Denomination.Fifty, new Operation.Withdraw(40), Ok(new(0, 2, 0), 0, 210, 40), new(2, 0, 0) },
        {
            0, 200, new(0, 0, 0), null, new Operation.Withdraw(5),
            new(AtmStatus.InvalidAmount, NoteCounts.None, 0, new(0, 200)), new(0, 0, 0)
        },
        {
            10, 0, new(0, 5, 0), null, new Operation.Deposit(new(0, 1, 0)),
            new(AtmStatus.CassetteFull, NoteCounts.None, 0, new(10, 0)), new(0, 5, 0)
        },
        {
            10, 30, new(0, 1, 0), Denomination.Twenty, new Operation.Deposit(new(0, 1, 0)),
            new(AtmStatus.CassetteJammed, NoteCounts.None, 0, new(10, 30)), new(0, 1, 0)
        },
        { 10, 30, new(1, 1, 0), null, new Operation.Deposit(new(1, 0, 1)), DepositOk(70, 30), new(2, 1, 1) },
    };

    [Theory]
    [MemberData(nameof(SpecExamples))]
    public void SpecExample_WithRealDispenser(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, Operation operation, AtmResult expected,
        NoteCounts expectedStock)
    {
        var session = new Session(stock, jammed, new(balance, withdrawnToday));

        session.Step(operation, expected, expectedStock);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Retraits qui demandent plusieurs passages : le vrai module refuse tout passage de plus de 4 billets.
    // ------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(4, 0, 0, 200, 4, 0, 0)] // 4 billets : un seul passage plein.
    [InlineData(3, 2, 0, 190, 3, 2, 0)] // 5 billets : deux passages.
    [InlineData(0, 5, 5, 150, 0, 5, 5)] // 10 billets : trois passages.
    [InlineData(1, 5, 5, 200, 1, 5, 5)] // 11 billets : trois passages, la machine est vidée.
    [InlineData(5, 5, 5, 180, 3, 1, 1)] // 50×3 + 20 + 10 : 5 billets de trois coupures.
    public void Withdrawal_OverSeveralPasses_SucceedsWithRealDispenser(
        int fifties, int twenties, int tens, int amount, int outFifties, int outTwenties, int outTens)
    {
        var stock = new NoteCounts(fifties, twenties, tens);
        var notes = new NoteCounts(outFifties, outTwenties, outTens);
        var session = new Session(stock, null, new(250, 0));

        session.Withdraw(amount, Ok(notes, amount > 100 ? 5 : 0, 250 - amount - (amount > 100 ? 5 : 0), amount), stock - notes);
    }

    [Fact]
    public void Withdrawal_OverSeveralPasses_AvoidsJammedCassette()
    {
        // 20 bloquée : 150 = 50×2 + 10×5, soit 7 billets, sans toucher aux 20.
        var session = new Session(new(2, 5, 5), Denomination.Twenty, new(250, 0));

        session.Withdraw(150, Ok(new(2, 0, 5), 5, 95, 150), new(0, 5, 0));
    }

    // ------------------------------------------------------------------------------------------------------------
    // Enchaînements d'opérations sur le même module.
    // ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void SuccessiveWithdrawals_DrainTheMachine_ThenCannotDispense()
    {
        var session = new Session(new(2, 1, 1), null, new(250, 0));

        session.Withdraw(50, Ok(new(1, 0, 0), 0, 200, 50), new(1, 1, 1));
        session.Withdraw(80, Ok(new(1, 1, 1), 0, 120, 130), NoteCounts.None);
        session.Refused(new Operation.Withdraw(10), AtmStatus.CannotDispense);
        Assert.Equal(NoteCounts.None, session.Dispenser.Stock);
    }

    [Fact]
    public void Combination_FollowsTheStockAsItShrinks()
    {
        var session = new Session(new(1, 5, 5), null, new(250, 0));

        session.Withdraw(60, Ok(new(1, 0, 1), 0, 190, 60), new(0, 5, 4));
        session.Withdraw(60, Ok(new(0, 3, 0), 0, 130, 120), new(0, 2, 4));
        session.Withdraw(60, Ok(new(0, 2, 2), 0, 70, 180), new(0, 0, 2));
        session.Withdraw(20, Ok(new(0, 0, 2), 0, 50, 200), NoteCounts.None);
    }

    [Fact]
    public void DepositedNotes_CanBeWithdrawnRightAfter()
    {
        var session = new Session(NoteCounts.None, null, new(0, 0));

        session.Refused(new Operation.Withdraw(10), AtmStatus.InsufficientFunds);
        session.Deposit(new(1, 1, 1), DepositOk(80, 0), new(1, 1, 1));
        session.Withdraw(80, Ok(new(1, 1, 1), 0, 0, 80), NoteCounts.None);
    }

    [Fact]
    public void DepositAfterInsufficientFunds_AllowsTheWithdrawal()
    {
        var session = new Session(new(1, 0, 0), null, new(30, 0));

        session.Refused(new Operation.Withdraw(50), AtmStatus.InsufficientFunds);
        session.Deposit(new(0, 1, 0), DepositOk(50, 0), new(1, 1, 0));
        session.Withdraw(50, Ok(new(1, 0, 0), 0, 0, 50), new(0, 1, 0));
    }

    [Fact]
    public void WithdrawThenDepositSameNotes_RestoresStockAndBalance_ButNotTheDailyCounter()
    {
        var session = new Session(new(2, 2, 2), null, new(100, 0));

        session.Withdraw(80, Ok(new(1, 1, 1), 0, 20, 80), new(1, 1, 1));
        session.Deposit(new(1, 1, 1), DepositOk(100, 80), new(2, 2, 2));
    }

    [Fact]
    public void Withdrawal_FreesRoomForAPreviouslyRefusedDeposit()
    {
        var session = new Session(Full, null, new(100, 0));

        session.Refused(new Operation.Deposit(new(1, 0, 0)), AtmStatus.CassetteFull);
        session.Withdraw(50, Ok(new(1, 0, 0), 0, 50, 50), new(4, 5, 5));
        session.Deposit(new(1, 0, 0), DepositOk(100, 50), Full);
        session.Refused(new Operation.Deposit(new(1, 0, 0)), AtmStatus.CassetteFull);
    }

    [Fact]
    public void Deposit_FillsCassette_ThenNextDepositIsFull()
    {
        var session = new Session(new(3, 4, 0), null, new(0, 0));

        session.Deposit(new(1, 1, 1), DepositOk(80, 0), new(4, 5, 1));
        session.Refused(new Operation.Deposit(new(1, 1, 0)), AtmStatus.CassetteFull);
        session.Refused(new Operation.Deposit(new(0, 1, 1)), AtmStatus.CassetteFull);
        session.Deposit(new(1, 0, 1), DepositOk(140, 0), new(5, 5, 2));
        session.Refused(new Operation.Deposit(new(1, 0, 0)), AtmStatus.CassetteFull);
    }

    [Fact]
    public void DailyLimit_AccumulatesAcrossWithdrawals_AndDepositsDoNotResetIt()
    {
        var session = new Session(Full, null, new(250, 0));

        session.Withdraw(100, Ok(new(2, 0, 0), 0, 150, 100), new(3, 5, 5));
        session.Withdraw(100, Ok(new(2, 0, 0), 0, 50, 200), new(1, 5, 5));
        session.Refused(new Operation.Withdraw(10), AtmStatus.DailyLimitExceeded);
        session.Deposit(new(1, 0, 0), DepositOk(100, 200), new(2, 5, 5));
        session.Refused(new Operation.Withdraw(10), AtmStatus.DailyLimitExceeded);
    }

    [Fact]
    public void Fee_IsChargedOncePerWithdrawal_EvenOverSeveralPasses()
    {
        var session = new Session(new(1, 5, 5), null, new(250, 0));

        session.Withdraw(200, Ok(new(1, 5, 5), 5, 45, 200), NoteCounts.None);
        session.Deposit(new(1, 1, 1), DepositOk(125, 200), new(1, 1, 1));
    }

    [Fact]
    public void JammedCassette_StaysUntouchedThroughoutTheSession()
    {
        var session = new Session(new(3, 3, 3), Denomination.Twenty, new(250, 0));

        session.Refused(new Operation.Withdraw(40), AtmStatus.CannotDispense); // 10×3 au plus, et les 20 sont bloqués.
        session.Withdraw(60, Ok(new(1, 0, 1), 0, 190, 60), new(2, 3, 2));
        session.Refused(new Operation.Deposit(new(0, 1, 0)), AtmStatus.CassetteJammed);
        session.Refused(new Operation.Deposit(new(1, 1, 1)), AtmStatus.CassetteJammed);
        session.Deposit(new(1, 0, 1), DepositOk(250, 60), new(3, 3, 3));
        session.Withdraw(30, Ok(new(0, 0, 3), 0, 220, 90), new(3, 3, 0));
        session.Refused(new Operation.Withdraw(20), AtmStatus.CannotDispense);
        Assert.Equal(3, session.Dispenser.Stock.Twenties);
    }

    [Fact]
    public void RefusalsOfEveryKind_InARow_MoveNothing_ThenTheMachineStillWorks()
    {
        var stock = new NoteCounts(1, 5, 0);
        var session = new Session(stock, Denomination.Twenty, new(110, 150));

        session.Refused(new Operation.Withdraw(5), AtmStatus.InvalidAmount);
        session.Refused(new Operation.Withdraw(60), AtmStatus.DailyLimitExceeded);
        session.Refused(new Operation.Deposit(NoteCounts.None), AtmStatus.InvalidAmount);
        session.Refused(new Operation.Deposit(new(0, 1, 0)), AtmStatus.CassetteJammed);
        session.Refused(new Operation.Withdraw(40), AtmStatus.CannotDispense);
        session.Deposit(new(1, 0, 1), DepositOk(170, 150), new(2, 5, 1));
        session.Withdraw(50, Ok(new(1, 0, 0), 0, 120, 200), new(1, 5, 1));
        session.Refused(new Operation.Withdraw(10), AtmStatus.DailyLimitExceeded);
    }

    [Fact]
    public void TwoMachinesOnTheSameDispenser_SeeEachOthersMoves()
    {
        var dispenser = new MoneyDispenser(new(1, 0, 0));
        var first = new CashMachine(dispenser);
        var second = new CashMachine(dispenser);

        Assert.Equal(Ok(new(1, 0, 0), 0, 200, 50), first.Execute(new(250, 0), new Operation.Withdraw(50)));
        Assert.Equal(NoteCounts.None, dispenser.Stock);

        Assert.Equal(
            new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new(250, 0)),
            second.Execute(new(250, 0), new Operation.Withdraw(50)));
        Assert.Equal(NoteCounts.None, dispenser.Stock);

        Assert.Equal(DepositOk(250, 0), second.Execute(new(200, 0), new Operation.Deposit(new(1, 0, 0))));
        Assert.Equal(new NoteCounts(1, 0, 0), dispenser.Stock);

        Assert.Equal(Ok(new(1, 0, 0), 0, 200, 50), first.Execute(new(250, 0), new Operation.Withdraw(50)));
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Parcours systématiques, comparés à un modèle indépendant.
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Chaque opération du domaine (retraits et dépôts), pour chaque stock et chaque cassette bloquée, sur des comptes
    /// choisis autour des seuils : le résultat et le stock suivent le modèle, et le vrai module ne tombe jamais en panne.
    /// </summary>
    [Fact]
    public void SingleOperation_MatchesModel_OverStockJamAndOperationDomain()
    {
        AccountState[] accounts =
        [
            new(250, 0), new(0, 0), new(5, 0), new(45, 0), new(100, 0), new(105, 0), new(110, 0), new(115, 0),
            new(205, 0), new(250, 140), new(250, 150), new(250, 190), new(250, 200), new(55, 150), new(0, 200),
        ];
        Operation[] operations =
        [
            .. DomainAmounts.Select(Operation (a) => new Operation.Withdraw(a)),
            .. DomainDeposits.Select(Operation (n) => new Operation.Deposit(n)),
        ];

        foreach (var stock in DomainStocks)
        foreach (var jammed in JamOptions)
        foreach (var account in accounts)
        foreach (var operation in operations)
        {
            var dispenser = new MoneyDispenser(stock, jammed);
            var result = new CashMachine(dispenser).Execute(account, operation);
            var (expected, expectedStock) = Model(account, stock, jammed, operation);

            if (result != expected || dispenser.Stock != expectedStock)
            {
                Assert.Fail(
                    $"{account} {stock} bloquée={jammed?.ToString() ?? "aucune"} {operation} : " +
                    $"attendu {expected} et stock {expectedStock}, obtenu {result} et stock {dispenser.Stock}.");
            }
        }
    }

    /// <summary>
    /// Marches aléatoires reproductibles : une suite d'opérations sur le même module, le compte enchaîné d'une opération
    /// à l'autre. Après chaque pas, le résultat et le stock suivent le modèle et les invariants tiennent.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RandomSessions_MatchModelAndKeepInvariants(int seed)
    {
        var random = new Random(seed);

        for (var walk = 0; walk < 500; walk++)
        {
            var stock = DomainStocks[random.Next(DomainStocks.Length)];
            var jammed = JamOptions[random.Next(JamOptions.Length)];
            var account = new AccountState(5 * random.Next(51), 10 * random.Next(21));

            var dispenser = new MoneyDispenser(stock, jammed);
            var machine = new CashMachine(dispenser);
            var feesPaid = 0;
            var ledger = account.Balance - stock.Value;

            for (var step = 0; step < 40; step++)
            {
                var operation = NextOperation(random, account);
                var stockBefore = dispenser.Stock;
                var result = machine.Execute(account, operation);
                var (expected, expectedStock) = Model(account, stockBefore, jammed, operation);

                var context = $"graine {seed}, marche {walk}, pas {step} : {account} {stockBefore} " +
                              $"bloquée={jammed?.ToString() ?? "aucune"} {operation}";
                Assert.True(result == expected, $"{context} : attendu {expected}, obtenu {result}.");
                Assert.True(dispenser.Stock == expectedStock, $"{context} : stock attendu {expectedStock}, obtenu {dispenser.Stock}.");

                // Invariants indépendants du modèle.
                Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
                Assert.Equal(jammed, dispenser.Jammed);
                Assert.InRange(dispenser.Stock.Fifties, 0, MoneyDispenser.CassetteCapacity);
                Assert.InRange(dispenser.Stock.Twenties, 0, MoneyDispenser.CassetteCapacity);
                Assert.InRange(dispenser.Stock.Tens, 0, MoneyDispenser.CassetteCapacity);
                if (jammed is { } cassette)
                {
                    Assert.Equal(stock.Of(cassette), dispenser.Stock.Of(cassette));
                }

                if (operation is Operation.Withdraw && result.Status == AtmStatus.Ok)
                {
                    Assert.Equal(stockBefore - result.Notes, dispenser.Stock);
                    Assert.Equal(account.WithdrawnToday + result.Notes.Value, result.Account.WithdrawnToday);
                }
                else if (operation is Operation.Deposit deposit && result.Status == AtmStatus.Ok)
                {
                    Assert.Equal(stockBefore + deposit.Notes, dispenser.Stock);
                }
                else
                {
                    Assert.Equal(new AtmResult(result.Status, NoteCounts.None, 0, account), result);
                    Assert.Equal(stockBefore, dispenser.Stock);
                }

                // L'argent ne se crée ni ne se perd : seuls les frais séparent le solde de la valeur du stock.
                feesPaid += result.Fee;
                Assert.Equal(ledger, result.Account.Balance - dispenser.Stock.Value + feesPaid);

                account = result.Account;
            }
        }
    }

    /// <summary>Une opération du domaine, choisie pour que le compte rendu reste dans le domaine (solde ≤ 250).</summary>
    private static Operation NextOperation(Random random, AccountState account)
    {
        if (random.Next(2) == 0)
        {
            return new Operation.Withdraw(DomainAmounts[random.Next(DomainAmounts.Length)]);
        }

        var deposits = DomainDeposits.Where(n => account.Balance + n.Value <= 250).ToArray();

        return new Operation.Deposit(deposits[random.Next(deposits.Length)]);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Le modèle : la spécification, écrite indépendamment de CashMachine.
    // ------------------------------------------------------------------------------------------------------------

    private static (AtmResult Result, NoteCounts Stock) Model(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus status) => (new AtmResult(status, NoteCounts.None, 0, account), stock);

        switch (operation)
        {
            case Operation.Withdraw { Amount: var amount }:
            {
                if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
                if (account.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
                var fee = amount > 100 ? 5 : 0;
                if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds);

                int Available(Denomination d) => d == jammed ? 0 : stock.Of(d);

                NoteCounts? best = null;
                var ties = 0;
                for (var f = 0; f <= Available(Denomination.Fifty); f++)
                for (var t = 0; t <= Available(Denomination.Twenty); t++)
                for (var n = 0; n <= Available(Denomination.Ten); n++)
                {
                    var candidate = new NoteCounts(f, t, n);
                    if (candidate.Value != amount) continue;
                    if (best is not { } current || candidate.Total < current.Total)
                    {
                        best = candidate;
                        ties = 0;
                    }
                    else if (candidate.Total == current.Total)
                    {
                        ties++;
                    }
                }

                if (best is not { } notes) return Refuse(AtmStatus.CannotDispense);
                Assert.Equal(0, ties); // La spécification garantit l'unicité.

                var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
                return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
            }

            case Operation.Deposit { Notes: var notes }:
            {
                if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
                if (jammed is { } cassette && notes.Of(cassette) > 0) return Refuse(AtmStatus.CassetteJammed);
                var after = stock + notes;
                if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);

                return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }), after);
            }

            default:
                throw new ArgumentException("Opération inconnue.", nameof(operation));
        }
    }
}
