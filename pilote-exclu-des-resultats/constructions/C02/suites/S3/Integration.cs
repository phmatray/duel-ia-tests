namespace Atm.Tests;

/// <summary>
/// Tests d'intégration : <see cref="CashMachine"/> travaille avec le vrai <see cref="MoneyDispenser"/>, sans doublure.
/// Après chaque opération, on vérifie le résultat rendu et le stock réellement présent dans le module.
/// </summary>
public class Integration
{
    private static readonly NoteCounts Full = new(5, 5, 5);
    private static readonly NoteCounts Empty = NoteCounts.None;

    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    // ======================================================================================================
    //  Outillage
    // ======================================================================================================

    /// <summary>Une machine et son vrai module, qui gardent leur état d'une opération à l'autre.</summary>
    private sealed class Guichet(NoteCounts stock, Denomination? jammed = null)
    {
        public MoneyDispenser Dispenser { get; } = new(stock, jammed);

        public CashMachine Machine => new(Dispenser);

        /// <summary>Exécute l'opération puis vérifie le résultat et le stock du module ; rend le compte après.</summary>
        public AccountState Step(AccountState account, Operation operation, AtmResult expected, NoteCounts expectedStock)
        {
            var result = Machine.Execute(account, operation);

            Assert.Equal(expected, result);
            Assert.Equal(expectedStock, Dispenser.Stock);
            Assert.Equal(jammed, Dispenser.Jammed);

            return result.Account;
        }

        public AccountState Ok(AccountState account, Operation operation, NoteCounts notes, int fee, AccountState after,
            NoteCounts expectedStock) =>
            Step(account, operation, new AtmResult(AtmStatus.Ok, notes, fee, after), expectedStock);

        /// <summary>Un refus : rien ne sort, aucun frais, compte et stock identiques.</summary>
        public AccountState Refused(AccountState account, Operation operation, AtmStatus status)
        {
            var before = Dispenser.Stock;
            return Step(account, operation, new AtmResult(status, Empty, 0, account), before);
        }
    }

    private static Operation W(int amount) => new Operation.Withdraw(amount);

    private static Operation D(int fifties, int twenties, int tens) => new Operation.Deposit(new NoteCounts(fifties, twenties, tens));

    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static AccountState A(int balance, int withdrawnToday) => new(balance, withdrawnToday);

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

    private static IEnumerable<Operation> AllOperations() =>
        Amounts.Select(W).Concat(AllDeposits().Select(n => (Operation)new Operation.Deposit(n)));

    private static bool InDomain(AccountState account) =>
        account.Balance is >= 0 and <= 250 && account.Balance % 5 == 0
        && account.WithdrawnToday is >= 0 and <= 200 && account.WithdrawnToday % 10 == 0;

    /// <summary>
    /// Modèle de la spécification, écrit indépendamment de <see cref="CashMachine"/> : le résultat attendu et le stock
    /// du module après l'opération.
    /// </summary>
    private static (AtmResult Result, NoteCounts Stock) Expected(AccountState account, NoteCounts stock, Denomination? jam,
        Operation operation)
    {
        (AtmResult, NoteCounts) Refuse(AtmStatus status) => (new AtmResult(status, Empty, 0, account), stock);

        switch (operation)
        {
            case Operation.Withdraw { Amount: var amount }:
            {
                if (amount <= 0 || amount % 10 != 0) return Refuse(AtmStatus.InvalidAmount);
                if (account.WithdrawnToday + amount > 200) return Refuse(AtmStatus.DailyLimitExceeded);
                var fee = amount > 100 ? 5 : 0;
                if (amount + fee > account.Balance) return Refuse(AtmStatus.InsufficientFunds);
                if (FewestNotes(amount, stock, jam) is not { } notes) return Refuse(AtmStatus.CannotDispense);

                var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
                return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
            }

            case Operation.Deposit { Notes: var notes }:
            {
                if (notes.Total == 0) return Refuse(AtmStatus.InvalidAmount);
                if (jam is { } j && notes.Of(j) > 0) return Refuse(AtmStatus.CassetteJammed);
                var after = stock + notes;
                if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refuse(AtmStatus.CassetteFull);

                var account2 = account with { Balance = account.Balance + notes.Value };
                return (new AtmResult(AtmStatus.Ok, Empty, 0, account2), after);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    /// <summary>La combinaison exacte qui compte le moins de billets parmi les billets disponibles (unique d'après la spec).</summary>
    private static NoteCounts? FewestNotes(int amount, NoteCounts stock, Denomination? jam)
    {
        var available = jam switch
        {
            Denomination.Fifty => stock with { Fifties = 0 },
            Denomination.Twenty => stock with { Twenties = 0 },
            Denomination.Ten => stock with { Tens = 0 },
            _ => stock,
        };

        var candidates = new List<NoteCounts>();
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var c = 0; c <= available.Tens; c++)
        {
            if (f * 50 + t * 20 + c * 10 == amount)
            {
                candidates.Add(new NoteCounts(f, t, c));
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var min = candidates.Min(n => n.Total);
        var best = candidates.Where(n => n.Total == min).ToList();
        Assert.Single(best); // la spec affirme l'unicité : on le vérifie au passage

        return best[0];
    }

    private static string Describe(AccountState account, NoteCounts stock, Denomination? jam, Operation operation) =>
        $"compte {account}, stock {stock}, bloquée {jam?.ToString() ?? "aucune"}, opération {operation}";

    /// <summary>Exécute une opération sur le vrai module et la compare au modèle ; rend le compte après.</summary>
    private static AccountState CheckAgainstModel(MoneyDispenser dispenser, AccountState account, Operation operation,
        string history = "")
    {
        var stock = dispenser.Stock;
        var (expected, expectedStock) = Expected(account, stock, dispenser.Jammed, operation);

        var result = new CashMachine(dispenser).Execute(account, operation);

        if (result != expected || dispenser.Stock != expectedStock)
        {
            Assert.Fail($"{history}{Describe(account, stock, dispenser.Jammed, operation)}\n"
                        + $"  attendu : {expected}, stock {expectedStock}\n"
                        + $"  obtenu  : {result}, stock {dispenser.Stock}");
        }

        return result.Account;
    }

    // ======================================================================================================
    //  Scénarios : plusieurs opérations à la suite sur le même module
    // ======================================================================================================

    [Fact]
    public void Scenario_DayOfOneCustomer_WithdrawalsDailyLimitThenDeposits()
    {
        var atm = new Guichet(Full);
        var account = A(250, 0);

        account = atm.Ok(account, W(80), N(1, 1, 1), 0, A(170, 80), N(4, 4, 4));
        account = atm.Refused(account, W(130), AtmStatus.DailyLimitExceeded);         // 80 + 130 > 200
        account = atm.Ok(account, W(120), N(2, 1, 0), 5, A(45, 200), N(2, 3, 4));     // frais, plafond atteint pile
        account = atm.Refused(account, W(10), AtmStatus.DailyLimitExceeded);
        account = atm.Ok(account, D(1, 1, 1), Empty, 0, A(125, 200), N(3, 4, 5));     // le dépôt ne touche pas au plafond
        account = atm.Refused(account, D(0, 0, 1), AtmStatus.CassetteFull);
        account = atm.Ok(account, D(1, 1, 0), Empty, 0, A(195, 200), N(4, 5, 5));
        atm.Refused(account, D(0, 1, 0), AtmStatus.CassetteFull);
    }

    [Fact]
    public void Scenario_SeveralCustomers_DrainTheModule_ThenADepositRefillsIt()
    {
        var atm = new Guichet(N(1, 2, 3));

        // Premier client : 100 = 50 + 20×2 + 10, la combinaison la plus courte.
        atm.Ok(A(250, 0), W(100), N(1, 2, 1), 0, A(150, 100), N(0, 0, 2));

        // Second client : le stock laissé par le premier ne permet plus que 10 ou 20.
        var account = A(100, 0);
        account = atm.Refused(account, W(30), AtmStatus.CannotDispense);
        account = atm.Ok(account, W(20), N(0, 0, 2), 0, A(80, 20), Empty);
        account = atm.Refused(account, W(10), AtmStatus.CannotDispense);
        account = atm.Ok(account, D(1, 1, 1), Empty, 0, A(160, 20), N(1, 1, 1));
        account = atm.Ok(account, W(80), N(1, 1, 1), 0, A(80, 100), Empty);
        atm.Refused(account, W(50), AtmStatus.CannotDispense);
    }

    [Fact]
    public void Scenario_JammedTwenties_StayUntouchedForTheWholeSession()
    {
        var atm = new Guichet(N(3, 3, 3), Denomination.Twenty);
        var account = A(100, 0);

        account = atm.Refused(account, D(0, 1, 0), AtmStatus.CassetteJammed);
        account = atm.Refused(account, D(1, 1, 1), AtmStatus.CassetteJammed);
        account = atm.Ok(account, D(1, 0, 1), Empty, 0, A(160, 0), N(4, 3, 4));
        account = atm.Ok(account, W(60), N(1, 0, 1), 0, A(100, 60), N(3, 3, 3));
        account = atm.Refused(account, W(40), AtmStatus.CannotDispense);              // 20×2 dans la cassette bloquée, 10×3 seulement
        account = atm.Ok(account, W(30), N(0, 0, 3), 0, A(70, 90), N(3, 3, 0));
        account = atm.Refused(account, W(20), AtmStatus.CannotDispense);
        account = atm.Ok(account, W(50), N(1, 0, 0), 0, A(20, 140), N(2, 3, 0));
        atm.Refused(account, W(20), AtmStatus.CannotDispense);                         // fonds suffisants, mais 20 bloqués

        Assert.Equal(3, atm.Dispenser.Stock.Twenties);
    }

    [Theory]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Scenario_JammedCassette_NeverChanges_WhateverTheOperations(Denomination jam)
    {
        var dispenser = new MoneyDispenser(N(2, 2, 2), jam);
        var jammedCount = dispenser.Stock.Of(jam);
        var account = A(100, 0);

        foreach (var operation in AllOperations())
        {
            account = CheckAgainstModel(dispenser, account, operation);
            Assert.Equal(jammedCount, dispenser.Stock.Of(jam));
            if (!InDomain(account))
            {
                account = A(100, 0);
            }
        }
    }

    [Fact]
    public void Scenario_WithdrawalNeedingSeveralPasses_ThenRefillCassetteByCassette()
    {
        // Dix billets : le vrai module refuse plus de 4 billets par passage, la machine doit découper.
        var atm = new Guichet(N(0, 5, 5));
        var account = atm.Ok(A(250, 0), W(150), N(0, 5, 5), 5, A(95, 150), Empty);

        for (var i = 1; i <= 5; i++)
        {
            account = atm.Ok(account, D(0, 1, 1), Empty, 0, A(95 + 30 * i, 150), N(0, i, i));
        }

        account = atm.Refused(account, D(0, 1, 0), AtmStatus.CassetteFull);
        account = atm.Refused(account, D(0, 0, 1), AtmStatus.CassetteFull);
        atm.Ok(account, D(1, 0, 0), Empty, 0, A(295, 150), N(1, 5, 5));
    }

    [Fact]
    public void Scenario_LargestWithdrawal_ElevenNotesInThreePasses_EmptiesTheModule()
    {
        var atm = new Guichet(N(1, 5, 5));
        var account = atm.Ok(A(205, 0), W(200), N(1, 5, 5), 5, A(0, 200), Empty);

        atm.Refused(account, W(10), AtmStatus.DailyLimitExceeded);
        atm.Refused(A(250, 0), W(10), AtmStatus.CannotDispense);
    }

    [Fact]
    public void Scenario_FullCassette_FreedByAWithdrawal_AcceptsTheDepositAgain()
    {
        var atm = new Guichet(N(5, 0, 0));
        var account = A(60, 0);

        account = atm.Refused(account, D(1, 0, 0), AtmStatus.CassetteFull);
        account = atm.Ok(account, W(50), N(1, 0, 0), 0, A(10, 50), N(4, 0, 0));
        account = atm.Ok(account, D(1, 0, 0), Empty, 0, A(60, 50), N(5, 0, 0));
        atm.Refused(account, D(1, 0, 0), AtmStatus.CassetteFull);
    }

    [Fact]
    public void Scenario_EmptyCassette_FilledByADeposit_MakesTheAmountDispensable()
    {
        var atm = new Guichet(Empty);
        var account = A(100, 0);

        account = atm.Refused(account, W(70), AtmStatus.CannotDispense);
        account = atm.Ok(account, D(1, 1, 0), Empty, 0, A(170, 0), N(1, 1, 0));
        account = atm.Ok(account, W(70), N(1, 1, 0), 0, A(100, 70), Empty);
        atm.Refused(account, W(70), AtmStatus.CannotDispense);
    }

    [Fact]
    public void Scenario_RefusalsInTheMiddle_DoNotDisturbTheFollowingOperations()
    {
        var atm = new Guichet(N(2, 2, 2));
        var account = A(200, 100);

        account = atm.Refused(account, W(-10), AtmStatus.InvalidAmount);
        account = atm.Refused(account, W(0), AtmStatus.InvalidAmount);
        account = atm.Refused(account, W(5), AtmStatus.InvalidAmount);
        account = atm.Refused(account, W(110), AtmStatus.DailyLimitExceeded);
        account = atm.Refused(account, D(0, 0, 0), AtmStatus.InvalidAmount);
        account = atm.Ok(account, W(90), N(1, 2, 0), 0, A(110, 190), N(1, 0, 2));
        account = atm.Refused(account, W(20), AtmStatus.DailyLimitExceeded);
        account = atm.Ok(account, W(10), N(0, 0, 1), 0, A(100, 200), N(1, 0, 1));
        atm.Ok(account, D(1, 1, 1), Empty, 0, A(180, 200), N(2, 1, 2));
    }

    [Fact]
    public void Scenario_FeeThreshold_AcrossSuccessiveWithdrawals()
    {
        var atm = new Guichet(Full);
        var account = A(215, 0);

        account = atm.Ok(account, W(100), N(2, 0, 0), 0, A(115, 100), N(3, 5, 5));    // 100 : pas de frais
        account = atm.Refused(account, W(110), AtmStatus.DailyLimitExceeded);          // 100 + 110 > 200
        account = atm.Ok(account, W(100), N(2, 0, 0), 0, A(15, 200), N(1, 5, 5));
        Assert.Equal(A(15, 200), account);

        // Un autre client, sur le stock restant : 110 = 50 + 20×3, frais de 5.
        atm.Ok(A(115, 0), W(110), N(1, 3, 0), 5, A(0, 110), N(0, 2, 5));
    }

    // ======================================================================================================
    //  Plusieurs machines, un seul module
    // ======================================================================================================

    [Fact]
    public void TwoMachines_SharingOneModule_SeeEachOthersMovements()
    {
        var dispenser = new MoneyDispenser(N(1, 1, 0));
        var first = new CashMachine(dispenser);
        var second = new CashMachine(dispenser);

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, A(50, 50)),
            first.Execute(A(100, 0), W(50)));
        Assert.Equal(N(0, 1, 0), dispenser.Stock);

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, Empty, 0, A(100, 0)),
            second.Execute(A(100, 0), W(50)));
        Assert.Equal(N(0, 1, 0), dispenser.Stock);

        Assert.Equal(new AtmResult(AtmStatus.Ok, Empty, 0, A(100, 50)),
            second.Execute(A(50, 50), D(1, 0, 0)));
        Assert.Equal(N(1, 1, 0), dispenser.Stock);

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 0), 0, A(30, 120)),
            first.Execute(A(100, 50), W(70)));
        Assert.Equal(Empty, dispenser.Stock);
    }

    [Fact]
    public void Result_DependsOnlyOnTheModuleState_NotOnItsHistory()
    {
        // Le stock (2,2,5) atteint après une série d'opérations, ou fourni tel quel au module.
        static MoneyDispenser Travelled()
        {
            var atm = new Guichet(Full);
            atm.Ok(A(250, 0), W(200), N(4, 0, 0), 5, A(45, 200), N(1, 5, 5));
            atm.Ok(A(250, 0), W(40), N(0, 2, 0), 0, A(210, 40), N(1, 3, 5));
            atm.Ok(A(250, 0), W(40), N(0, 2, 0), 0, A(210, 40), N(1, 1, 5));
            atm.Ok(A(0, 0), D(1, 1, 0), Empty, 0, A(70, 0), N(2, 2, 5));
            return atm.Dispenser;
        }

        foreach (var operation in AllOperations())
        {
            var travelled = Travelled();
            var fresh = new MoneyDispenser(N(2, 2, 5));

            Assert.Equal(new CashMachine(fresh).Execute(A(150, 0), operation),
                new CashMachine(travelled).Execute(A(150, 0), operation));
            Assert.Equal(fresh.Stock, travelled.Stock);
        }
    }

    // ======================================================================================================
    //  Propriétés sur tout le domaine, avec le vrai module
    // ======================================================================================================

    public static IEnumerable<object?[]> JamCases() => Jams.Select(j => new object?[] { j });

    /// <summary>Retrait : tout le domaine (compte × stock × cassette bloquée × montant) comparé au modèle.</summary>
    [Theory]
    [MemberData(nameof(JamCases))]
    public void Withdraw_WholeDomain_MatchesTheSpecification(Denomination? jam)
    {
        foreach (var stock in AllStocks())
        foreach (var amount in Amounts)
        {
            // Le modèle ne dépend du compte que pour les contrôles : on calcule la combinaison une fois.
            var combination = amount > 0 && amount % 10 == 0 ? FewestNotes(amount, stock, jam) : null;
            var fee = amount > 100 ? 5 : 0;

            for (var balance = 0; balance <= 250; balance += 5)
            for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
            {
                var account = A(balance, withdrawn);
                var dispenser = new MoneyDispenser(stock, jam);
                var result = new CashMachine(dispenser).Execute(account, W(amount));

                AtmStatus status =
                    amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
                    : withdrawn + amount > 200 ? AtmStatus.DailyLimitExceeded
                    : amount + fee > balance ? AtmStatus.InsufficientFunds
                    : combination is null ? AtmStatus.CannotDispense
                    : AtmStatus.Ok;

                var (expected, expectedStock) = status == AtmStatus.Ok
                    ? (new AtmResult(AtmStatus.Ok, combination!.Value, fee, A(balance - amount - fee, withdrawn + amount)),
                        stock - combination.Value)
                    : (new AtmResult(status, Empty, 0, account), stock);

                if (result != expected || dispenser.Stock != expectedStock)
                {
                    Assert.Fail($"{Describe(account, stock, jam, W(amount))}\n"
                                + $"  attendu : {expected}, stock {expectedStock}\n"
                                + $"  obtenu  : {result}, stock {dispenser.Stock}");
                }
            }
        }
    }

    /// <summary>Dépôt : tout le domaine (stock × cassette bloquée × dépôt), sur des comptes aux bornes.</summary>
    [Fact]
    public void Deposit_WholeDomain_MatchesTheSpecification()
    {
        AccountState[] accounts = [A(0, 0), A(5, 200), A(170, 110), A(250, 0)];

        foreach (var jam in Jams)
        foreach (var stock in AllStocks())
        foreach (var notes in AllDeposits())
        foreach (var account in accounts)
        {
            CheckAgainstModel(new MoneyDispenser(stock, jam), account, new Operation.Deposit(notes));
        }
    }

    [Fact]
    public void RealModule_NeverFaults_OnTheWholeDomain()
    {
        var operations = AllOperations().ToList();

        foreach (var jam in Jams)
        foreach (var stock in AllStocks())
        foreach (var operation in operations)
        {
            var dispenser = new MoneyDispenser(stock, jam);
            var result = new CashMachine(dispenser).Execute(A(250, 0), operation);

            Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
        }
    }

    [Fact]
    public void Withdraw_TheModuleLosesExactlyTheNotesHandedOut()
    {
        foreach (var jam in Jams)
        foreach (var stock in AllStocks())
        for (var amount = 10; amount <= 200; amount += 10)
        {
            var dispenser = new MoneyDispenser(stock, jam);
            var result = new CashMachine(dispenser).Execute(A(250, 0), W(amount));

            Assert.Equal(stock - result.Notes, dispenser.Stock);
            Assert.Equal(result.Status == AtmStatus.Ok ? amount : 0, result.Notes.Value);
            Assert.Equal(result.Status == AtmStatus.Ok ? 250 - amount - result.Fee : 250, result.Account.Balance);
        }
    }

    [Fact]
    public void Deposit_TheModuleGainsExactlyWhatTheAccountIsCredited()
    {
        foreach (var jam in Jams)
        foreach (var stock in AllStocks())
        foreach (var notes in AllDeposits())
        {
            var dispenser = new MoneyDispenser(stock, jam);
            var result = new CashMachine(dispenser).Execute(A(100, 40), new Operation.Deposit(notes));

            var gained = dispenser.Stock - stock;
            Assert.Equal(result.Status == AtmStatus.Ok ? notes : Empty, gained);
            Assert.Equal(100 + gained.Value, result.Account.Balance);
            Assert.Equal(40, result.Account.WithdrawnToday);
        }
    }

    [Fact]
    public void WithdrawThenDepositTheSameNotes_RestoresTheModule()
    {
        foreach (var jam in Jams)
        foreach (var stock in AllStocks())
        for (var amount = 10; amount <= 200; amount += 10)
        {
            var dispenser = new MoneyDispenser(stock, jam);
            var machine = new CashMachine(dispenser);
            var withdrawal = machine.Execute(A(250, 0), W(amount));
            var notes = withdrawal.Notes;

            // Un dépôt du domaine compte au plus un billet par coupure.
            if (withdrawal.Status != AtmStatus.Ok || notes.Fifties > 1 || notes.Twenties > 1 || notes.Tens > 1)
            {
                continue;
            }

            var deposit = machine.Execute(withdrawal.Account, new Operation.Deposit(notes));

            // Les billets viennent de cassettes non bloquées et y retrouvent leur place.
            Assert.Equal(new AtmResult(AtmStatus.Ok, Empty, 0, A(250 - withdrawal.Fee, amount)), deposit);
            Assert.Equal(stock, dispenser.Stock);
        }
    }

    [Fact]
    public void DepositThenWithdrawItsValue_SucceedsWhenTheDepositWasAccepted()
    {
        foreach (var jam in Jams)
        foreach (var stock in AllStocks())
        foreach (var notes in AllDeposits())
        {
            var dispenser = new MoneyDispenser(stock, jam);
            var machine = new CashMachine(dispenser);
            var deposit = machine.Execute(A(0, 0), new Operation.Deposit(notes));
            if (deposit.Status != AtmStatus.Ok)
            {
                continue;
            }

            // Les billets déposés sont disponibles : le montant est forcément distribuable.
            var withdrawal = machine.Execute(deposit.Account, W(notes.Value));

            Assert.Equal(AtmStatus.Ok, withdrawal.Status);
            Assert.Equal(notes.Value, withdrawal.Notes.Value);
            Assert.True(withdrawal.Notes.Total <= notes.Total);
            Assert.Equal(A(0, notes.Value), withdrawal.Account);
            Assert.Equal(stock + notes - withdrawal.Notes, dispenser.Stock);
        }
    }

    /// <summary>Toutes les paires d'opérations, sur le même module, depuis tous les stocks et toutes les pannes de cassette.</summary>
    [Theory]
    [MemberData(nameof(JamCases))]
    public void EveryPairOfOperations_OnTheSameModule_MatchesTheSpecification(Denomination? jam)
    {
        var operations = AllOperations().ToList();
        var start = A(90, 100); // 90 + 2 dépôts de 80 = 250 : le compte reste dans le domaine

        foreach (var stock in AllStocks())
        foreach (var first in operations)
        foreach (var second in operations)
        {
            var dispenser = new MoneyDispenser(stock, jam);
            var account = CheckAgainstModel(dispenser, start, first);
            CheckAgainstModel(dispenser, account, second, $"après {first} : ");
        }
    }

    /// <summary>Longues séquences pseudo-aléatoires (reproductibles) : plusieurs clients se succèdent sur le même module.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RandomSessions_OnTheSameModule_MatchTheSpecification(int seed)
    {
        var random = new Random(seed);
        var operations = AllOperations().ToList();

        for (var session = 0; session < 500; session++)
        {
            var jam = Jams[random.Next(Jams.Length)];
            var dispenser = new MoneyDispenser(N(random.Next(6), random.Next(6), random.Next(6)), jam);
            var account = RandomAccount(random);
            var history = new List<string>();

            for (var step = 0; step < 40; step++)
            {
                // Un nouveau client se présente de temps en temps, ou quand le compte sort du domaine.
                if (!InDomain(account) || random.Next(5) == 0)
                {
                    account = RandomAccount(random);
                }

                var operation = operations[random.Next(operations.Count)];
                var context = $"graine {seed}, session {session}, étape {step}\n  historique : "
                              + string.Join(" ; ", history) + "\n  ";
                var before = account;
                account = CheckAgainstModel(dispenser, account, operation, context);
                history.Add($"{before} {operation} -> stock {dispenser.Stock}");

                Assert.InRange(dispenser.Stock.Fifties, 0, 5);
                Assert.InRange(dispenser.Stock.Twenties, 0, 5);
                Assert.InRange(dispenser.Stock.Tens, 0, 5);
            }
        }
    }

    private static AccountState RandomAccount(Random random) => A(random.Next(51) * 5, random.Next(21) * 10);
}
