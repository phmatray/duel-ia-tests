namespace Atm.Tests;

using static Notes;

/// <summary>
/// Tests d'intégration : <see cref="CashMachine"/> travaille avec le vrai <see cref="MoneyDispenser"/>, sans doublure.
/// Chaque opération est vérifiée sur son résultat et sur le stock du module après coup.
/// </summary>
public class IntegrationTests
{
    private static readonly NoteCounts Full = Of(5, 5, 5);

    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    // ───────────────────────── Modèle de référence, écrit d'après la spécification ─────────────────────────

    private static IEnumerable<int> DomainBalances() => Enumerable.Range(0, 51).Select(i => i * 5);

    private static IEnumerable<int> DomainWithdrawnToday() => Enumerable.Range(0, 21).Select(i => i * 10);

    private static IEnumerable<int> DomainAmounts() => new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));

    private static IEnumerable<NoteCounts> DomainStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return Of(f, t, d);
    }

    private static IEnumerable<NoteCounts> DomainDeposits()
    {
        for (var mask = 0; mask < 8; mask++)
            yield return Of(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
    }

    /// <summary>Les combinaisons de moins de billets, par valeur, pour un ensemble de billets disponibles.</summary>
    private static Dictionary<int, NoteCounts> FewestByValue(NoteCounts available)
    {
        var best = new Dictionary<int, NoteCounts>();
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
        {
            var candidate = Of(f, t, d);
            if (!best.TryGetValue(candidate.Value, out var current) || candidate.Total < current.Total)
            {
                best[candidate.Value] = candidate;
            }
        }

        return best;
    }

    private static NoteCounts AvailableIn(NoteCounts stock, Denomination? jammed) => Of(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    /// <summary>Résultat et stock attendus d'un retrait.</summary>
    private static (AtmResult Result, NoteCounts Stock) ExpectedWithdraw(
        AccountState account, int amount, NoteCounts stock, Denomination? jammed)
    {
        var fee = amount > 100 ? 5 : 0;
        AtmStatus? refusal =
            amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
            : account.WithdrawnToday + amount > 200 ? AtmStatus.DailyLimitExceeded
            : amount + fee > account.Balance ? AtmStatus.InsufficientFunds
            : null;

        if (refusal is null)
        {
            if (FewestByValue(AvailableIn(stock, jammed)).TryGetValue(amount, out var notes))
            {
                var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
                return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
            }

            refusal = AtmStatus.CannotDispense;
        }

        return (new AtmResult(refusal.Value, NoteCounts.None, 0, account), stock);
    }

    /// <summary>Résultat et stock attendus d'un dépôt.</summary>
    private static (AtmResult Result, NoteCounts Stock) ExpectedDeposit(
        AccountState account, NoteCounts deposit, NoteCounts stock, Denomination? jammed)
    {
        var after = stock + deposit;
        AtmStatus? refusal =
            deposit.Total == 0 ? AtmStatus.InvalidAmount
            : jammed is { } j && deposit.Of(j) > 0 ? AtmStatus.CassetteJammed
            : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
            : null;

        return refusal is { } status
            ? (new AtmResult(status, NoteCounts.None, 0, account), stock)
            : (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + deposit.Value }), after);
    }

    private static (AtmResult Result, NoteCounts Stock) Expected(
        AccountState account, Operation operation, NoteCounts stock, Denomination? jammed) => operation switch
    {
        Operation.Withdraw w => ExpectedWithdraw(account, w.Amount, stock, jammed),
        Operation.Deposit d => ExpectedDeposit(account, d.Notes, stock, jammed),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    /// <summary>Exécute l'opération sur le vrai module et vérifie résultat, stock, cassette bloquée et conservation des billets.</summary>
    private static AtmResult Step(CashMachine machine, MoneyDispenser dispenser, AccountState account, Operation operation)
    {
        var stockBefore = dispenser.Stock;
        var jammedBefore = dispenser.Jammed;
        var (expected, expectedStock) = Expected(account, operation, stockBefore, jammedBefore);

        var result = machine.Execute(account, operation);

        var context = $"compte {account}, {operation}, stock {stockBefore}, bloquée {jammedBefore?.ToString() ?? "aucune"}";
        Assert.True(expected == result, $"{context} : attendu {expected}, obtenu {result}");
        Assert.True(expectedStock == dispenser.Stock, $"{context} : stock attendu {expectedStock}, obtenu {dispenser.Stock}");
        Assert.Equal(jammedBefore, dispenser.Jammed);

        // Conservation : ce qui sort du module est exactement ce que rend le résultat, ce qui y entre est exactement le dépôt.
        var loaded = operation is Operation.Deposit { Notes: var deposited } && result.Status == AtmStatus.Ok ? deposited : NoteCounts.None;
        Assert.Equal(stockBefore - result.Notes + loaded, dispenser.Stock);

        // La valeur sortie ou entrée correspond au mouvement du solde.
        Assert.Equal(account.Balance - result.Notes.Value - result.Fee + loaded.Value, result.Account.Balance);

        return result;
    }

    private static AtmResult Step(CashMachine machine, MoneyDispenser dispenser, AccountState account, Operation operation,
        AtmResult expected, NoteCounts expectedStock)
    {
        var result = Step(machine, dispenser, account, operation);
        Assert.Equal(expected, result);
        Assert.Equal(expectedStock, dispenser.Stock);

        return result;
    }

    // ───────────────────────── Le domaine complet, opération par opération ─────────────────────────

    public static TheoryData<int> Balances() => new(DomainBalances());

    /// <summary>Tous les retraits du domaine (tous comptes, stocks, cassettes bloquées, montants), un solde par cas.</summary>
    [Theory]
    [MemberData(nameof(Balances))]
    public void Withdraw_WholeDomain_MatchesSpecificationWithRealDispenser(int balance)
    {
        var stocks = DomainStocks().ToArray();
        var amounts = DomainAmounts().ToArray();
        var fewest = stocks
            .SelectMany(stock => JamOptions.Select(jammed => (stock, jammed)))
            .ToDictionary(key => key, key => FewestByValue(AvailableIn(key.stock, key.jammed)));

        foreach (var withdrawnToday in DomainWithdrawnToday())
        foreach (var stock in stocks)
        foreach (var jammed in JamOptions)
        foreach (var amount in amounts)
        {
            var account = new AccountState(balance, withdrawnToday);
            var dispenser = new MoneyDispenser(stock, jammed);

            var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

            var fee = amount > 100 ? 5 : 0;
            AtmResult expected;
            NoteCounts expectedStock = stock;
            if (amount <= 0 || amount % 10 != 0) expected = new(AtmStatus.InvalidAmount, default, 0, account);
            else if (withdrawnToday + amount > 200) expected = new(AtmStatus.DailyLimitExceeded, default, 0, account);
            else if (amount + fee > balance) expected = new(AtmStatus.InsufficientFunds, default, 0, account);
            else if (!fewest[(stock, jammed)].TryGetValue(amount, out var notes)) expected = new(AtmStatus.CannotDispense, default, 0, account);
            else
            {
                expected = new(AtmStatus.Ok, notes, fee, new(balance - amount - fee, withdrawnToday + amount));
                expectedStock = stock - notes;
            }

            if (expected != result || expectedStock != dispenser.Stock)
            {
                Assert.Fail($"Retrait {amount}, compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"} : " +
                            $"attendu {expected} / stock {expectedStock}, obtenu {result} / stock {dispenser.Stock}");
            }
        }
    }

    /// <summary>Tous les dépôts du domaine (tous comptes, stocks, cassettes bloquées, dépôts), un solde par cas.</summary>
    [Theory]
    [MemberData(nameof(Balances))]
    public void Deposit_WholeDomain_MatchesSpecificationWithRealDispenser(int balance)
    {
        var deposits = DomainDeposits().ToArray();

        foreach (var withdrawnToday in DomainWithdrawnToday())
        foreach (var stock in DomainStocks())
        foreach (var jammed in JamOptions)
        foreach (var deposit in deposits)
        {
            var account = new AccountState(balance, withdrawnToday);
            var dispenser = new MoneyDispenser(stock, jammed);

            var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(deposit));

            var (expected, expectedStock) = ExpectedDeposit(account, deposit, stock, jammed);
            if (expected != result || expectedStock != dispenser.Stock)
            {
                Assert.Fail($"Dépôt {deposit}, compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"} : " +
                            $"attendu {expected} / stock {expectedStock}, obtenu {result} / stock {dispenser.Stock}");
            }
        }
    }

    [Fact]
    public void Domain_NeverProducesDispenserFault_WithRealDispenser()
    {
        foreach (var stock in DomainStocks())
        foreach (var jammed in JamOptions)
        {
            foreach (var amount in DomainAmounts())
            {
                var dispenser = new MoneyDispenser(stock, jammed);
                var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(amount));
                Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
            }

            foreach (var deposit in DomainDeposits())
            {
                var dispenser = new MoneyDispenser(stock, jammed);
                var result = new CashMachine(dispenser).Execute(new(0, 0), new Operation.Deposit(deposit));
                Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
            }
        }
    }

    // ───────────────────────── Retraits en plusieurs passages avec le vrai module ─────────────────────────

    [Fact]
    public void Withdraw200_ElevenNotesInSeveralPasses_EmptiesTheModule()
    {
        // 200 avec un seul billet de 50 : 50 + 5×20 + 5×10, soit 11 billets, au moins 3 passages de 4.
        var dispenser = new MoneyDispenser(Of(1, 5, 5));
        var machine = new CashMachine(dispenser);

        Step(machine, dispenser, new(250, 0), new Operation.Withdraw(200),
            new AtmResult(AtmStatus.Ok, Of(1, 5, 5), 5, new(45, 200)), Of(0, 0, 0));
    }

    [Theory]
    [InlineData(40, 0, 0, 4, 0, 0, 4)]     // exactement 4 billets : un seul passage
    [InlineData(50, 0, 0, 5, 0, 0, 5)]     // 5 billets de 10 : deux passages
    [InlineData(100, 0, 5, 0, 0, 5, 0)]    // 5 billets de 20 : deux passages
    [InlineData(150, 0, 5, 5, 0, 5, 5)]    // 10 billets, tout le stock
    [InlineData(180, 3, 1, 1, 3, 1, 1)]    // 5 billets de trois coupures
    [InlineData(200, 4, 5, 5, 4, 0, 0)]    // 4 billets de 50 alors que d'autres combinaisons existent
    public void MultiNoteWithdrawals_LeaveTheRightStock(int amount, int f, int t, int d, int nf, int nt, int nd)
    {
        var stock = Of(f, t, d);
        var notes = Of(nf, nt, nd);
        var dispenser = new MoneyDispenser(stock);
        var fee = amount > 100 ? 5 : 0;

        Step(new CashMachine(dispenser), dispenser, new(250, 0), new Operation.Withdraw(amount),
            new AtmResult(AtmStatus.Ok, notes, fee, new(250 - amount - fee, amount)), stock - notes);
    }

    // ───────────────────────── Enchaînements sur un même module ─────────────────────────

    [Fact]
    public void SuccessiveWithdrawals_DrainTheModule_ThenCannotDispense()
    {
        var dispenser = new MoneyDispenser(Of(1, 2, 1));
        var machine = new CashMachine(dispenser);

        var account = Step(machine, dispenser, new(250, 0), new Operation.Withdraw(50),
            new AtmResult(AtmStatus.Ok, Of(1, 0, 0), 0, new(200, 50)), Of(0, 2, 1)).Account;

        // Le 50 est parti : 50 se fait maintenant avec 20×2 + 10.
        account = Step(machine, dispenser, account, new Operation.Withdraw(50),
            new AtmResult(AtmStatus.Ok, Of(0, 2, 1), 0, new(150, 100)), Of(0, 0, 0)).Account;

        Step(machine, dispenser, account, new Operation.Withdraw(10),
            new AtmResult(AtmStatus.CannotDispense, default, 0, account), Of(0, 0, 0));
    }

    [Fact]
    public void DepositedNotes_CanBeWithdrawnByTheNextOperation()
    {
        var dispenser = new MoneyDispenser(Of(0, 0, 0));
        var machine = new CashMachine(dispenser);

        var account = Step(machine, dispenser, new(0, 0), new Operation.Withdraw(10),
            new AtmResult(AtmStatus.InsufficientFunds, default, 0, new(0, 0)), Of(0, 0, 0)).Account;

        account = Step(machine, dispenser, account, new Operation.Deposit(Of(1, 1, 1)),
            new AtmResult(AtmStatus.Ok, default, 0, new(80, 0)), Of(1, 1, 1)).Account;

        account = Step(machine, dispenser, account, new Operation.Withdraw(70),
            new AtmResult(AtmStatus.Ok, Of(1, 1, 0), 0, new(10, 70)), Of(0, 0, 1)).Account;

        Step(machine, dispenser, account, new Operation.Withdraw(10),
            new AtmResult(AtmStatus.Ok, Of(0, 0, 1), 0, new(0, 80)), Of(0, 0, 0));
    }

    [Fact]
    public void WithdrawalFreesRoom_ForADepositThatWasFull()
    {
        var dispenser = new MoneyDispenser(Of(5, 0, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(200, 0);

        account = Step(machine, dispenser, account, new Operation.Deposit(Of(1, 0, 0)),
            new AtmResult(AtmStatus.CassetteFull, default, 0, account), Of(5, 0, 0)).Account;

        account = Step(machine, dispenser, account, new Operation.Withdraw(50),
            new AtmResult(AtmStatus.Ok, Of(1, 0, 0), 0, new(150, 50)), Of(4, 0, 0)).Account;

        Step(machine, dispenser, account, new Operation.Deposit(Of(1, 0, 0)),
            new AtmResult(AtmStatus.Ok, default, 0, new(200, 50)), Of(5, 0, 0));
    }

    [Fact]
    public void JammedCassette_StaysJammedAcrossOperations()
    {
        var dispenser = new MoneyDispenser(Of(3, 1, 3), Denomination.Fifty);
        var machine = new CashMachine(dispenser);

        // Les billets de 50 sont là mais inutilisables : 50 = 20 + 10×3.
        var account = Step(machine, dispenser, new(250, 0), new Operation.Withdraw(50),
            new AtmResult(AtmStatus.Ok, Of(0, 1, 3), 0, new(200, 50)), Of(3, 0, 0)).Account;

        account = Step(machine, dispenser, account, new Operation.Withdraw(50),
            new AtmResult(AtmStatus.CannotDispense, default, 0, account), Of(3, 0, 0)).Account;

        account = Step(machine, dispenser, account, new Operation.Deposit(Of(1, 1, 0)),
            new AtmResult(AtmStatus.CassetteJammed, default, 0, account), Of(3, 0, 0)).Account;

        account = Step(machine, dispenser, account, new Operation.Deposit(Of(0, 1, 1)),
            new AtmResult(AtmStatus.Ok, default, 0, new(230, 50)), Of(3, 1, 1)).Account;

        Step(machine, dispenser, account, new Operation.Withdraw(30),
            new AtmResult(AtmStatus.Ok, Of(0, 1, 1), 0, new(200, 80)), Of(3, 0, 0));

        Assert.Equal(Denomination.Fifty, dispenser.Jammed);
    }

    [Fact]
    public void RefusalsInTheMiddleOfASequence_MoveNoNotes()
    {
        var dispenser = new MoneyDispenser(Of(2, 3, 1), Denomination.Ten);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(120, 150);
        var stock = dispenser.Stock;

        Operation[] refused =
        [
            new Operation.Withdraw(-10),
            new Operation.Withdraw(5),
            new Operation.Withdraw(60),                     // plafond
            new Operation.Withdraw(10),                     // seule la cassette de 10 ferait l'affaire, elle est bloquée
            new Operation.Deposit(NoteCounts.None),
            new Operation.Deposit(Of(1, 1, 1)),             // contient un billet de 10
        ];
        AtmStatus[] statuses =
        [
            AtmStatus.InvalidAmount, AtmStatus.InvalidAmount, AtmStatus.DailyLimitExceeded,
            AtmStatus.CannotDispense, AtmStatus.InvalidAmount, AtmStatus.CassetteJammed,
        ];

        for (var i = 0; i < refused.Length; i++)
        {
            Step(machine, dispenser, account, refused[i], new AtmResult(statuses[i], default, 0, account), stock);
        }

        // Après ces refus, le module sert normalement.
        Step(machine, dispenser, account, new Operation.Withdraw(50),
            new AtmResult(AtmStatus.Ok, Of(1, 0, 0), 0, new(70, 200)), Of(1, 3, 1));
    }

    [Fact]
    public void FullDay_WithdrawsUpToTheLimitThenIsRefused()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);

        var account = Step(machine, dispenser, new(250, 0), new Operation.Withdraw(110),
            new AtmResult(AtmStatus.Ok, Of(2, 0, 1), 5, new(135, 110)), Of(3, 5, 4)).Account;

        account = Step(machine, dispenser, account, new Operation.Withdraw(90),
            new AtmResult(AtmStatus.Ok, Of(1, 2, 0), 0, new(45, 200)), Of(2, 3, 4)).Account;

        account = Step(machine, dispenser, account, new Operation.Withdraw(10),
            new AtmResult(AtmStatus.DailyLimitExceeded, default, 0, account), Of(2, 3, 4)).Account;

        // Le dépôt reste possible et ne touche pas au plafond.
        Step(machine, dispenser, account, new Operation.Deposit(Of(1, 1, 1)),
            new AtmResult(AtmStatus.Ok, default, 0, new(125, 200)), Of(3, 4, 5));
    }

    // ───────────────────────── La machine est sans mémoire : seul le module porte l'état ─────────────────────────

    [Fact]
    public void TwoMachinesSharingAModule_SeeEachOthersEffects()
    {
        var dispenser = new MoneyDispenser(Of(1, 0, 0));
        var first = new CashMachine(dispenser);
        var second = new CashMachine(dispenser);

        Step(first, dispenser, new(100, 0), new Operation.Withdraw(50),
            new AtmResult(AtmStatus.Ok, Of(1, 0, 0), 0, new(50, 50)), Of(0, 0, 0));

        Step(second, dispenser, new(100, 0), new Operation.Withdraw(50),
            new AtmResult(AtmStatus.CannotDispense, default, 0, new(100, 0)), Of(0, 0, 0));

        Step(second, dispenser, new(0, 0), new Operation.Deposit(Of(1, 0, 0)),
            new AtmResult(AtmStatus.Ok, default, 0, new(50, 0)), Of(1, 0, 0));

        Step(first, dispenser, new(100, 0), new Operation.Withdraw(50),
            new AtmResult(AtmStatus.Ok, Of(1, 0, 0), 0, new(50, 50)), Of(0, 0, 0));
    }

    [Fact]
    public void ModuleChangedDirectly_IsSeenByTheMachine()
    {
        var dispenser = new MoneyDispenser(Of(0, 0, 0));
        var machine = new CashMachine(dispenser);

        Step(machine, dispenser, new(100, 0), new Operation.Withdraw(20),
            new AtmResult(AtmStatus.CannotDispense, default, 0, new(100, 0)), Of(0, 0, 0));

        Assert.True(dispenser.Load(Of(0, 1, 0)));

        Step(machine, dispenser, new(100, 0), new Operation.Withdraw(20),
            new AtmResult(AtmStatus.Ok, Of(0, 1, 0), 0, new(80, 20)), Of(0, 0, 0));

        Assert.True(dispenser.Load(Of(0, 0, 5)));
        Assert.True(dispenser.Dispense(Of(0, 0, 4)));

        Step(machine, dispenser, new(100, 0), new Operation.Withdraw(20),
            new AtmResult(AtmStatus.CannotDispense, default, 0, new(100, 0)), Of(0, 0, 1));
    }

    [Fact]
    public void MachineHistory_DoesNotMatter_OnlyTheModuleState()
    {
        // La même machine refait le même retrait après qu'on a remis le module dans son état initial : même résultat.
        var dispenser = new MoneyDispenser(Of(1, 2, 3));
        var machine = new CashMachine(dispenser);

        for (var i = 0; i < 3; i++)
        {
            Step(machine, dispenser, new(250, 0), new Operation.Withdraw(90),
                new AtmResult(AtmStatus.Ok, Of(1, 2, 0), 0, new(160, 90)), Of(0, 0, 3));

            Assert.True(dispenser.Load(Of(1, 2, 0)));
        }
    }

    // ───────────────────────── Marches aléatoires reproductibles ─────────────────────────

    public static TheoryData<int> Seeds() => new(Enumerable.Range(1, 40));

    /// <summary>
    /// Enchaîne des opérations du domaine sur un même module et une même machine. Le compte est reporté d'une opération à l'autre
    /// tant qu'il reste dans le domaine ; sinon on en tire un nouveau. Le stock, lui, n'est jamais réinitialisé.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void RandomSequences_MatchTheModel(int seed)
    {
        var random = new Random(seed);
        var balances = DomainBalances().ToArray();
        var withdrawn = DomainWithdrawnToday().ToArray();
        var amounts = DomainAmounts().ToArray();
        var deposits = DomainDeposits().ToArray();

        var dispenser = new MoneyDispenser(
            Of(random.Next(6), random.Next(6), random.Next(6)), JamOptions[random.Next(JamOptions.Length)]);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(balances[random.Next(balances.Length)], withdrawn[random.Next(withdrawn.Length)]);
        var seen = new HashSet<AtmStatus>();

        for (var step = 0; step < 300; step++)
        {
            Operation operation = random.Next(2) == 0
                ? new Operation.Withdraw(amounts[random.Next(amounts.Length)])
                : new Operation.Deposit(deposits[random.Next(deposits.Length)]);

            var result = Step(machine, dispenser, account, operation);
            seen.Add(result.Status);
            Assert.NotEqual(AtmStatus.DispenserFault, result.Status);

            var next = result.Account;
            var inDomain = next.Balance is >= 0 and <= 250 && next.Balance % 5 == 0
                && next.WithdrawnToday is >= 0 and <= 200 && next.WithdrawnToday % 10 == 0;
            account = inDomain && random.Next(10) != 0
                ? next
                : new AccountState(balances[random.Next(balances.Length)], withdrawn[random.Next(withdrawn.Length)]);
        }

        Assert.Contains(AtmStatus.Ok, seen);
    }
}
