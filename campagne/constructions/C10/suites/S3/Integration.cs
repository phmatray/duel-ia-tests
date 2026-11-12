namespace Atm.Tests;

/// <summary>
/// Tests d'intégration : <see cref="CashMachine"/> travaille avec le vrai <see cref="MoneyDispenser"/>, sans doublure.
/// Chaque opération vérifie le résultat complet et le stock du module après coup.
/// Les attendus viennent d'un modèle écrit directement depuis la spécification, indépendant du code de la machine.
/// </summary>
public class IntegrationTests
{
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static AccountState A(int balance, int withdrawnToday) => new(balance, withdrawnToday);

    // ====================================================================================================
    // Exemples de la spécification, un par un, avec le stock du module vérifié à chaque fois
    // ====================================================================================================

    public static TheoryData<string, AccountState, NoteCounts, Denomination?, Operation, AtmResult, NoteCounts> SpecExamples => new()
    {
        { "Retrait 60, 20×3", A(100, 0), N(1, 3, 0), null, new Operation.Withdraw(60),
            new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, A(40, 60)), N(1, 0, 0) },
        { "Retrait 80, un de chaque", A(100, 0), N(1, 1, 1), null, new Operation.Withdraw(80),
            new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, A(20, 80)), N(0, 0, 0) },
        { "Retrait 100 sans frais", A(105, 0), N(5, 5, 5), null, new Operation.Withdraw(100),
            new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, A(5, 100)), N(3, 5, 5) },
        { "Retrait 110 avec frais", A(115, 0), N(5, 5, 5), null, new Operation.Withdraw(110),
            new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, A(0, 110)), N(3, 5, 4) },
        { "Retrait 110, frais rendent le solde insuffisant", A(110, 0), N(5, 5, 5), null, new Operation.Withdraw(110),
            new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, A(110, 0)), N(5, 5, 5) },
        { "Retrait 60 au-delà du plafond", A(250, 150), N(5, 5, 5), null, new Operation.Withdraw(60),
            new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, A(250, 150)), N(5, 5, 5) },
        { "Retrait 50, seule la cassette de 50 bloquée", A(250, 0), N(1, 0, 0), Denomination.Fifty, new Operation.Withdraw(50),
            new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, A(250, 0)), N(1, 0, 0) },
        { "Retrait 40 en évitant la cassette bloquée", A(250, 0), N(2, 2, 0), Denomination.Fifty, new Operation.Withdraw(40),
            new AtmResult(AtmStatus.Ok, N(0, 2, 0), 0, A(210, 40)), N(2, 0, 0) },
        { "Retrait 5 invalide", A(0, 200), N(0, 0, 0), null, new Operation.Withdraw(5),
            new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, A(0, 200)), N(0, 0, 0) },
        { "Dépôt dans une cassette pleine", A(10, 0), N(0, 5, 0), null, new Operation.Deposit(N(0, 1, 0)),
            new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, A(10, 0)), N(0, 5, 0) },
        { "Dépôt dans une cassette bloquée", A(10, 30), N(0, 1, 0), Denomination.Twenty, new Operation.Deposit(N(0, 1, 0)),
            new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, A(10, 30)), N(0, 1, 0) },
        { "Dépôt accepté", A(10, 30), N(1, 1, 0), null, new Operation.Deposit(N(1, 0, 1)),
            new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(70, 30)), N(2, 1, 1) },
    };

    [Theory]
    [MemberData(nameof(SpecExamples))]
    public void SpecExample(
        string label, AccountState account, NoteCounts stock, Denomination? jammed, Operation operation,
        AtmResult expected, NoteCounts expectedStock)
    {
        _ = label;
        var dispenser = new MoneyDispenser(stock, jammed);

        var result = new CashMachine(dispenser).Execute(account, operation);

        Assert.Equal(expected, result);
        Assert.Equal(expectedStock, dispenser.Stock);
        Assert.Equal(jammed, dispenser.Jammed);
    }

    // ====================================================================================================
    // Parcours exhaustif du domaine
    // ====================================================================================================

    /// <summary>
    /// Tout le domaine du retrait : chaque solde, chaque retrait du jour, chaque stock, chaque cassette bloquée
    /// et chaque montant. Le résultat et le stock doivent correspondre au modèle de la spécification.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllJams))]
    public void Withdraw_WholeDomain_MatchesSpecification(Denomination? jammed)
    {
        var failures = new List<string>();

        foreach (var stock in AllStocks())
        foreach (var amount in Amounts())
        {
            // La combinaison ne dépend que du stock, de la cassette bloquée et du montant.
            var combination = FewestNotesBySpec(amount, stock, jammed);

            foreach (var account in AllAccounts())
            {
                var (expected, expectedStock) = ExpectedWithdraw(account, amount, stock, combination);
                var dispenser = new MoneyDispenser(stock, jammed);

                var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

                if (result != expected || dispenser.Stock != expectedStock || dispenser.Jammed != jammed)
                {
                    failures.Add(
                        $"{account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, retrait {amount} : " +
                        $"attendu {expected} / stock {expectedStock}, obtenu {result} / stock {dispenser.Stock}");
                }

                if (failures.Count >= 20)
                {
                    Assert.Fail(string.Join(Environment.NewLine, failures));
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Tout le domaine du dépôt : chaque compte, chaque stock, chaque cassette bloquée et chaque dépôt
    /// (0 ou 1 billet par coupure).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllJams))]
    public void Deposit_WholeDomain_MatchesSpecification(Denomination? jammed)
    {
        var failures = new List<string>();

        foreach (var stock in AllStocks())
        foreach (var notes in AllDeposits())
        foreach (var account in AllAccounts())
        {
            var (expected, expectedStock) = ExpectedDeposit(account, notes, stock, jammed);
            var dispenser = new MoneyDispenser(stock, jammed);

            var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));

            if (result != expected || dispenser.Stock != expectedStock || dispenser.Jammed != jammed)
            {
                failures.Add(
                    $"{account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, dépôt {notes} : " +
                    $"attendu {expected} / stock {expectedStock}, obtenu {result} / stock {dispenser.Stock}");
            }

            if (failures.Count >= 20)
            {
                Assert.Fail(string.Join(Environment.NewLine, failures));
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// La spécification affirme que la combinaison la plus courte est toujours unique avec ces trois coupures.
    /// Le modèle s'appuie sur cette affirmation : on la vérifie sur tout le domaine.
    /// </summary>
    [Fact]
    public void FewestNotesCombination_IsAlwaysUnique()
    {
        foreach (var jammed in Jams)
        foreach (var stock in AllStocks())
        foreach (var amount in Amounts().Where(a => a > 0 && a % 10 == 0))
        {
            var exact = Combinations(stock, jammed).Where(c => c.Value == amount).ToList();
            if (exact.Count == 0)
            {
                continue;
            }

            var fewest = exact.Min(c => c.Total);
            Assert.Single(exact, c => c.Total == fewest);
        }
    }

    // ====================================================================================================
    // Scénarios : plusieurs opérations sur la même machine et le même module
    // ====================================================================================================

    [Fact]
    public void Scenario_WithdrawalsDrainCassettes_ThenNextWithdrawalAdapts()
    {
        var dispenser = new MoneyDispenser(N(1, 3, 1));
        var machine = new CashMachine(dispenser);

        // 60 : 50+10 (2 billets) plutôt que 20×3.
        var first = machine.Execute(A(250, 0), new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 1), 0, A(190, 60)), first);
        Assert.Equal(N(0, 3, 0), dispenser.Stock);

        // Plus de 50 ni de 10 : 60 sort maintenant en 20×3.
        var second = machine.Execute(first.Account, new Operation.Withdraw(60));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 0), 0, A(130, 120)), second);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);

        // Module vide : refus, rien ne bouge.
        var third = machine.Execute(second.Account, new Operation.Withdraw(10));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, A(130, 120)), third);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Scenario_DepositRefillsModule_ThenWithdrawalUsesDepositedNotes()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var machine = new CashMachine(dispenser);

        var refused = machine.Execute(A(100, 0), new Operation.Withdraw(80));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, A(100, 0)), refused);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);

        var deposit = machine.Execute(refused.Account, new Operation.Deposit(N(1, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(180, 0)), deposit);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);

        var withdraw = machine.Execute(deposit.Account, new Operation.Withdraw(80));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 1), 0, A(100, 80)), withdraw);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Scenario_DepositsFillCassette_ThenCassetteFull_ThenWithdrawalMakesRoom()
    {
        var dispenser = new MoneyDispenser(N(0, 3, 0));
        var machine = new CashMachine(dispenser);
        var account = A(0, 0);

        for (var expected = 4; expected <= 5; expected++)
        {
            var ok = machine.Execute(account, new Operation.Deposit(N(0, 1, 0)));
            Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + 20 }), ok);
            Assert.Equal(N(0, expected, 0), dispenser.Stock);
            account = ok.Account;
        }

        // Le dépôt d'un 20 ferait 6 billets ; les autres billets du même dépôt ne passent pas non plus.
        var full = machine.Execute(account, new Operation.Deposit(N(1, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, account), full);
        Assert.Equal(N(0, 5, 0), dispenser.Stock);

        var withdraw = machine.Execute(account, new Operation.Withdraw(20));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 1, 0), 0, A(20, 20)), withdraw);
        Assert.Equal(N(0, 4, 0), dispenser.Stock);

        var again = machine.Execute(withdraw.Account, new Operation.Deposit(N(1, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(100, 20)), again);
        Assert.Equal(N(1, 5, 1), dispenser.Stock);
    }

    [Fact]
    public void Scenario_JammedCassette_StaysUntouchedAcrossOperations()
    {
        var dispenser = new MoneyDispenser(N(5, 2, 3), Denomination.Fifty);
        var machine = new CashMachine(dispenser);

        // 100 sans les 50 : au plus 20×2 + 10×3 = 70 → refus.
        var refused = machine.Execute(A(250, 0), new Operation.Withdraw(100));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, A(250, 0)), refused);
        Assert.Equal(N(5, 2, 3), dispenser.Stock);

        // 70 : 20×2 + 10×3, les 50 restent en place.
        var ok = machine.Execute(A(250, 0), new Operation.Withdraw(70));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 2, 3), 0, A(180, 70)), ok);
        Assert.Equal(N(5, 0, 0), dispenser.Stock);

        // Dépôt contenant un 50 : refusé, même si les autres cassettes ont de la place.
        var jammed = machine.Execute(ok.Account, new Operation.Deposit(N(1, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteJammed, NoteCounts.None, 0, A(180, 70)), jammed);
        Assert.Equal(N(5, 0, 0), dispenser.Stock);

        // Dépôt sans 50 : accepté.
        var deposit = machine.Execute(ok.Account, new Operation.Deposit(N(0, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(210, 70)), deposit);
        Assert.Equal(N(5, 1, 1), dispenser.Stock);
        Assert.Equal(Denomination.Fifty, dispenser.Jammed);
    }

    [Fact]
    public void Scenario_FullModule_LargestWithdrawalLeavesFeeInAccountNotInNotes()
    {
        var dispenser = new MoneyDispenser(N(0, 5, 5));
        var machine = new CashMachine(dispenser);

        // 150 sans 50 : 20×5 + 10×5, 10 billets, donc au moins trois passages dans le vrai module.
        var result = machine.Execute(A(155, 0), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 5, 5), 5, A(0, 150)), result);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Scenario_JammedFifty_StockWorthEnoughButNotDispensable()
    {
        // Le stock vaut 200 €, mais le 50 est bloqué : au plus 20×5 + 10×5 = 150 € disponibles.
        var dispenser = new MoneyDispenser(N(1, 5, 5), Denomination.Fifty);
        var machine = new CashMachine(dispenser);

        var refused = machine.Execute(A(250, 0), new Operation.Withdraw(200));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, A(250, 0)), refused);
        Assert.Equal(N(1, 5, 5), dispenser.Stock);

        var ok = machine.Execute(A(250, 0), new Operation.Withdraw(150));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 5, 5), 5, A(95, 150)), ok);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);

        // Il ne reste que le 50 bloqué : plus rien ne sort.
        var empty = machine.Execute(ok.Account, new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, A(95, 150)), empty);
        Assert.Equal(N(1, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Scenario_DailyLimitReachedOverSeveralWithdrawals()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(A(250, 0), new Operation.Withdraw(110));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 1), 5, A(135, 110)), first);
        Assert.Equal(N(3, 5, 4), dispenser.Stock);

        var second = machine.Execute(first.Account, new Operation.Withdraw(90));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 2, 0), 0, A(45, 200)), second);
        Assert.Equal(N(2, 3, 4), dispenser.Stock);

        var third = machine.Execute(second.Account, new Operation.Withdraw(10));
        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, A(45, 200)), third);
        Assert.Equal(N(2, 3, 4), dispenser.Stock);

        // Le dépôt reste possible et ne touche pas au plafond du jour.
        var deposit = machine.Execute(third.Account, new Operation.Deposit(N(1, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, A(125, 200)), deposit);
        Assert.Equal(N(3, 4, 5), dispenser.Stock);
    }

    // ====================================================================================================
    // Séquences pseudo-aléatoires : le module garde son état d'une opération à l'autre
    // ====================================================================================================

    /// <summary>
    /// Enchaîne des opérations du domaine sur une même machine et un même module, et vérifie après chacune
    /// le résultat et le stock contre le modèle. Vérifie aussi que la machine est sans mémoire : une machine
    /// neuve, branchée sur un module neuf dans le même état, donne le même résultat.
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
    public void RandomSequences_MatchSpecification_StepByStep(int seed)
    {
        var random = new Random(seed);
        var allStocks = AllStocks().ToArray();
        var amounts = Amounts().ToArray();
        var deposits = AllDeposits().ToArray();

        for (var run = 0; run < 200; run++)
        {
            var jammed = Jams[random.Next(Jams.Length)];
            var dispenser = new MoneyDispenser(allStocks[random.Next(allStocks.Length)], jammed);
            var machine = new CashMachine(dispenser);
            var account = A(5 * random.Next(0, 51), 10 * random.Next(0, 21));
            var trace = new List<string>();

            for (var step = 0; step < 30; step++)
            {
                var stock = dispenser.Stock;
                Operation operation = random.Next(2) == 0
                    ? new Operation.Withdraw(amounts[random.Next(amounts.Length)])
                    : new Operation.Deposit(deposits[random.Next(deposits.Length)]);

                var (expected, expectedStock) = operation switch
                {
                    Operation.Withdraw w => ExpectedWithdraw(account, w.Amount, stock, FewestNotesBySpec(w.Amount, stock, jammed)),
                    Operation.Deposit d => ExpectedDeposit(account, d.Notes, stock, jammed),
                    _ => throw new InvalidOperationException(),
                };

                var fresh = new MoneyDispenser(stock, jammed);
                var freshResult = new CashMachine(fresh).Execute(account, operation);

                var result = machine.Execute(account, operation);
                trace.Add($"{account}, stock {stock}, {operation} → {result}, stock {dispenser.Stock}");

                var context = $"graine {seed}, essai {run}, bloquée {jammed?.ToString() ?? "aucune"}{Environment.NewLine}" +
                              string.Join(Environment.NewLine, trace);
                Assert.True(expected == result, $"attendu {expected}{Environment.NewLine}{context}");
                Assert.True(expectedStock == dispenser.Stock, $"stock attendu {expectedStock}{Environment.NewLine}{context}");
                Assert.True(jammed == dispenser.Jammed, context);
                Assert.True(freshResult == result, $"machine neuve : {freshResult}{Environment.NewLine}{context}");
                Assert.True(fresh.Stock == dispenser.Stock, $"module neuf : {fresh.Stock}{Environment.NewLine}{context}");

                // Le compte suit les résultats, sauf si un dépôt le fait sortir du domaine (solde > 250) :
                // on garde alors l'ancien compte, le stock restant celui du module.
                account = result.Account.Balance <= 250 ? result.Account : account;
            }
        }
    }

    /// <summary>
    /// Conservation : sur toute une séquence, ce qui est entré moins ce qui est sorti égale l'écart de stock,
    /// et l'écart de solde s'explique exactement par les dépôts, les retraits et les frais.
    /// </summary>
    [Fact]
    public void RandomSequences_ConserveNotesAndMoney()
    {
        var random = new Random(42);
        var allStocks = AllStocks().ToArray();
        var amounts = Amounts().ToArray();
        var deposits = AllDeposits().ToArray();

        for (var run = 0; run < 500; run++)
        {
            var jammed = Jams[random.Next(Jams.Length)];
            var initialStock = allStocks[random.Next(allStocks.Length)];
            var dispenser = new MoneyDispenser(initialStock, jammed);
            var machine = new CashMachine(dispenser);
            var initialAccount = A(250, 0);
            var account = initialAccount;

            var paidOut = NoteCounts.None;
            var paidIn = NoteCounts.None;
            var fees = 0;
            var withdrawn = 0;

            for (var step = 0; step < 20; step++)
            {
                Operation operation = random.Next(2) == 0
                    ? new Operation.Withdraw(amounts[random.Next(amounts.Length)])
                    : new Operation.Deposit(deposits[random.Next(deposits.Length)]);

                var result = machine.Execute(account, operation);
                Assert.NotEqual(AtmStatus.DispenserFault, result.Status);

                if (result.Status == AtmStatus.Ok)
                {
                    if (operation is Operation.Deposit d)
                    {
                        paidIn += d.Notes;
                    }
                    else
                    {
                        paidOut += result.Notes;
                        fees += result.Fee;
                        withdrawn += result.Notes.Value;
                    }
                }

                account = result.Account;

                // Un dépôt peut faire sortir le solde du domaine : on arrête la séquence là.
                if (account.Balance > 250)
                {
                    break;
                }
            }

            Assert.Equal(initialStock + paidIn - paidOut, dispenser.Stock);
            Assert.Equal(initialAccount.Balance + paidIn.Value - paidOut.Value - fees, account.Balance);
            Assert.Equal(initialAccount.WithdrawnToday + withdrawn, account.WithdrawnToday);
        }
    }

    // ====================================================================================================
    // Modèle de la spécification
    // ====================================================================================================

    private static (AtmResult Result, NoteCounts Stock) ExpectedWithdraw(
        AccountState account, int amount, NoteCounts stock, NoteCounts? combination)
    {
        AtmResult Refusal(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0)
        {
            return (Refusal(AtmStatus.InvalidAmount), stock);
        }

        if (account.WithdrawnToday + amount > 200)
        {
            return (Refusal(AtmStatus.DailyLimitExceeded), stock);
        }

        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance)
        {
            return (Refusal(AtmStatus.InsufficientFunds), stock);
        }

        if (combination is not { } notes)
        {
            return (Refusal(AtmStatus.CannotDispense), stock);
        }

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);

        return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
    }

    private static (AtmResult Result, NoteCounts Stock) ExpectedDeposit(
        AccountState account, NoteCounts notes, NoteCounts stock, Denomination? jammed)
    {
        AtmResult Refusal(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (notes.Total == 0)
        {
            return (Refusal(AtmStatus.InvalidAmount), stock);
        }

        if (jammed is { } j && notes.Of(j) > 0)
        {
            return (Refusal(AtmStatus.CassetteJammed), stock);
        }

        if (stock.Fifties + notes.Fifties > 5 || stock.Twenties + notes.Twenties > 5 || stock.Tens + notes.Tens > 5)
        {
            return (Refusal(AtmStatus.CassetteFull), stock);
        }

        var after = new AccountState(account.Balance + notes.Value, account.WithdrawnToday);

        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, after), stock + notes);
    }

    /// <summary>Énumère naïvement toutes les combinaisons tirées des billets disponibles et garde la plus courte.</summary>
    private static NoteCounts? FewestNotesBySpec(int amount, NoteCounts stock, Denomination? jammed)
    {
        NoteCounts? best = null;
        foreach (var candidate in Combinations(stock, jammed))
        {
            if (candidate.Value == amount && (best is not { } current || candidate.Total < current.Total))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static IEnumerable<NoteCounts> Combinations(NoteCounts stock, Denomination? jammed)
    {
        var available = N(
            jammed == Denomination.Fifty ? 0 : stock.Fifties,
            jammed == Denomination.Twenty ? 0 : stock.Twenties,
            jammed == Denomination.Ten ? 0 : stock.Tens);

        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            yield return N(f, t, d);
        }
    }

    // ====================================================================================================
    // Domaine
    // ====================================================================================================

    public static TheoryData<Denomination?> AllJams() => new(Jams);

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
        {
            yield return N(f, t, d);
        }
    }

    private static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
        {
            yield return N(f, t, d);
        }
    }

    private static IEnumerable<int> Amounts()
    {
        yield return -10;
        yield return 0;
        yield return 5;
        for (var amount = 10; amount <= 220; amount += 10)
        {
            yield return amount;
        }
    }

    private static IEnumerable<AccountState> AllAccounts()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        {
            yield return A(balance, withdrawn);
        }
    }
}
