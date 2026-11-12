using System.Collections.Concurrent;

namespace Atm.Tests;

/// <summary>
/// Tests d'intégration : <see cref="CashMachine"/> travaille avec le vrai <see cref="MoneyDispenser"/>, sans doublure.
/// Chaque opération est vérifiée sur deux plans : le résultat rendu, et le stock réellement présent dans le module ensuite.
/// </summary>
public class IntegrationTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static AccountState A(int balance, int withdrawn) => new(balance, withdrawn);

    private static Operation W(int amount) => new Operation.Withdraw(amount);

    private static Operation D(int fifties, int twenties, int tens) => new Operation.Deposit(N(fifties, twenties, tens));

    private static AtmResult Ok(NoteCounts notes, int fee, AccountState account) => new(AtmStatus.Ok, notes, fee, account);

    private static AtmResult Refused(AtmStatus status, AccountState account) => new(status, NoteCounts.None, 0, account);

    /// <summary>Exécute une opération sur un module existant et vérifie le résultat puis le stock du module.</summary>
    private static AccountState Step(
        ICashMachine machine, MoneyDispenser dispenser, AccountState account, Operation operation,
        AtmResult expected, NoteCounts expectedStock)
    {
        var actual = machine.Execute(account, operation);

        Assert.Equal(expected, actual);
        Assert.Equal(expectedStock, dispenser.Stock);

        return actual.Account;
    }

    // ---- Les exemples de la spécification, résultat et stock ----------------------------------------

    public static TheoryData<AccountState, NoteCounts, Denomination?, Operation, AtmResult, NoteCounts> SpecExamples => new()
    {
        { A(100, 0), N(1, 3, 0), null, W(60), Ok(N(0, 3, 0), 0, A(40, 60)), N(1, 0, 0) },
        { A(100, 0), N(1, 1, 1), null, W(80), Ok(N(1, 1, 1), 0, A(20, 80)), N(0, 0, 0) },
        { A(105, 0), N(5, 5, 5), null, W(100), Ok(N(2, 0, 0), 0, A(5, 100)), N(3, 5, 5) },
        { A(115, 0), N(5, 5, 5), null, W(110), Ok(N(2, 0, 1), 5, A(0, 110)), N(3, 5, 4) },
        { A(110, 0), N(5, 5, 5), null, W(110), Refused(AtmStatus.InsufficientFunds, A(110, 0)), N(5, 5, 5) },
        { A(250, 150), N(5, 5, 5), null, W(60), Refused(AtmStatus.DailyLimitExceeded, A(250, 150)), N(5, 5, 5) },
        { A(250, 0), N(1, 0, 0), Denomination.Fifty, W(50), Refused(AtmStatus.CannotDispense, A(250, 0)), N(1, 0, 0) },
        { A(250, 0), N(2, 2, 0), Denomination.Fifty, W(40), Ok(N(0, 2, 0), 0, A(210, 40)), N(2, 0, 0) },
        { A(0, 200), N(0, 0, 0), null, W(5), Refused(AtmStatus.InvalidAmount, A(0, 200)), N(0, 0, 0) },
        { A(10, 0), N(0, 5, 0), null, D(0, 1, 0), Refused(AtmStatus.CassetteFull, A(10, 0)), N(0, 5, 0) },
        { A(10, 30), N(0, 1, 0), Denomination.Twenty, D(0, 1, 0), Refused(AtmStatus.CassetteJammed, A(10, 30)), N(0, 1, 0) },
        { A(10, 30), N(1, 1, 0), null, D(1, 0, 1), Ok(NoteCounts.None, 0, A(70, 30)), N(2, 1, 1) },
    };

    [Theory]
    [MemberData(nameof(SpecExamples))]
    public void SpecExample_ResultAndStock(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation,
        AtmResult expected, NoteCounts expectedStock)
    {
        var dispenser = new MoneyDispenser(stock, jammed);

        Step(new CashMachine(dispenser), dispenser, account, operation, expected, expectedStock);
        Assert.Equal(jammed, dispenser.Jammed);
    }

    // ---- Retraits en plusieurs passages, avec la vraie limite de 4 billets du module ---------------

    public static TheoryData<NoteCounts, Denomination?, AccountState, int, AtmResult, NoteCounts> MultiPassWithdrawals => new()
    {
        // 5 billets : 2 passages.
        { N(0, 5, 0), null, A(100, 0), 100, Ok(N(0, 5, 0), 0, A(0, 100)), N(0, 0, 0) },
        { N(0, 0, 5), null, A(50, 0), 50, Ok(N(0, 0, 5), 0, A(0, 50)), N(0, 0, 0) },
        // 10 billets : 3 passages, la machine est vidée.
        { N(0, 5, 5), null, A(250, 0), 150, Ok(N(0, 5, 5), 5, A(95, 150)), N(0, 0, 0) },
        // La cassette bloquée garde ses billets pendant que les autres se vident.
        { N(5, 5, 5), Denomination.Fifty, A(250, 0), 150, Ok(N(0, 5, 5), 5, A(95, 150)), N(5, 0, 0) },
        { N(3, 5, 5), Denomination.Fifty, A(155, 50), 140, Ok(N(0, 5, 4), 5, A(10, 190)), N(3, 0, 1) },
        // Les trois coupures dans le même retrait, 6 billets.
        { N(2, 2, 2), null, A(250, 0), 160, Ok(N(2, 2, 2), 5, A(85, 160)), N(0, 0, 0) },
        // Le plus gros retrait du domaine : 200 € en billets de 50, puis en 20 et 10 si les 50 manquent.
        { N(4, 5, 5), null, A(205, 0), 200, Ok(N(4, 0, 0), 5, A(0, 200)), N(0, 5, 5) },
        { N(1, 5, 5), null, A(250, 0), 200, Ok(N(1, 5, 5), 5, A(45, 200)), N(0, 0, 0) },
    };

    [Theory]
    [MemberData(nameof(MultiPassWithdrawals))]
    public void Withdraw_SplitIntoPasses_RealModuleAcceptsThem(
        NoteCounts stock, Denomination? jammed, AccountState account, int amount, AtmResult expected, NoteCounts expectedStock)
    {
        var dispenser = new MoneyDispenser(stock, jammed);

        Step(new CashMachine(dispenser), dispenser, account, W(amount), expected, expectedStock);
    }

    [Fact]
    public void Withdraw_TooManyNotesForStock_CannotDispense_StockUntouched()
    {
        // 200 € sans billet de 50 utilisable : 20×5 + 10×5 = 150 au mieux.
        var dispenser = new MoneyDispenser(N(5, 5, 5), Denomination.Fifty);

        Step(new CashMachine(dispenser), dispenser, A(250, 0), W(200),
            Refused(AtmStatus.CannotDispense, A(250, 0)), N(5, 5, 5));
    }

    // ---- La cassette bloquée change la combinaison ------------------------------------------------

    public static TheoryData<Denomination?, int, NoteCounts, NoteCounts> JamChangesCombination => new()
    {
        { null, 60, N(1, 0, 1), N(4, 5, 4) },
        { Denomination.Ten, 60, N(0, 3, 0), N(5, 2, 5) },
        { Denomination.Twenty, 60, N(1, 0, 1), N(4, 5, 4) },
        { Denomination.Fifty, 60, N(0, 3, 0), N(5, 2, 5) },
        { null, 90, N(1, 2, 0), N(4, 3, 5) },
        { Denomination.Twenty, 90, N(1, 0, 4), N(4, 5, 1) },
        { Denomination.Fifty, 90, N(0, 4, 1), N(5, 1, 4) },
        { Denomination.Ten, 90, N(1, 2, 0), N(4, 3, 5) },
        { Denomination.Fifty, 100, N(0, 5, 0), N(5, 0, 5) },
        { Denomination.Twenty, 100, N(2, 0, 0), N(3, 5, 5) },
    };

    [Theory]
    [MemberData(nameof(JamChangesCombination))]
    public void Withdraw_AvoidsJammedCassette_AndLeavesItFull(Denomination? jammed, int amount, NoteCounts notes, NoteCounts expectedStock)
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5), jammed);
        var fee = amount > 100 ? 5 : 0;

        Step(new CashMachine(dispenser), dispenser, A(250, 0), W(amount),
            Ok(notes, fee, A(250 - amount - fee, amount)), expectedStock);
    }

    // ---- Scénarios : plusieurs opérations sur le même module ---------------------------------------

    [Fact]
    public void Scenario_FullDay_LimitThenDepositThenNextDay()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);
        var account = A(250, 0);

        account = Step(machine, dispenser, account, W(110), Ok(N(2, 0, 1), 5, A(135, 110)), N(3, 5, 4));
        account = Step(machine, dispenser, account, W(100), Refused(AtmStatus.DailyLimitExceeded, A(135, 110)), N(3, 5, 4));
        account = Step(machine, dispenser, account, W(90), Ok(N(1, 2, 0), 0, A(45, 200)), N(2, 3, 4));
        account = Step(machine, dispenser, account, W(10), Refused(AtmStatus.DailyLimitExceeded, A(45, 200)), N(2, 3, 4));
        account = Step(machine, dispenser, account, D(1, 1, 1), Ok(NoteCounts.None, 0, A(125, 200)), N(3, 4, 5));
        account = Step(machine, dispenser, account, D(0, 0, 1), Refused(AtmStatus.CassetteFull, A(125, 200)), N(3, 4, 5));

        // Le lendemain : le compteur du jour repart à zéro, la machine n'a rien retenu.
        account = account with { WithdrawnToday = 0 };
        account = Step(machine, dispenser, account, W(120), Ok(N(2, 1, 0), 5, A(0, 120)), N(1, 3, 5));
        Step(machine, dispenser, account, W(10), Refused(AtmStatus.InsufficientFunds, A(0, 120)), N(1, 3, 5));
    }

    [Fact]
    public void Scenario_EmptyThenRefill()
    {
        var dispenser = new MoneyDispenser(N(1, 1, 1));
        var machine = new CashMachine(dispenser);
        var account = A(250, 0);

        account = Step(machine, dispenser, account, W(80), Ok(N(1, 1, 1), 0, A(170, 80)), N(0, 0, 0));
        account = Step(machine, dispenser, account, W(10), Refused(AtmStatus.CannotDispense, A(170, 80)), N(0, 0, 0));
        account = Step(machine, dispenser, account, D(0, 0, 1), Ok(NoteCounts.None, 0, A(180, 80)), N(0, 0, 1));
        account = Step(machine, dispenser, account, W(20), Refused(AtmStatus.CannotDispense, A(180, 80)), N(0, 0, 1));
        account = Step(machine, dispenser, account, W(10), Ok(N(0, 0, 1), 0, A(170, 90)), N(0, 0, 0));
        account = Step(machine, dispenser, account, D(1, 1, 0), Ok(NoteCounts.None, 0, A(240, 90)), N(1, 1, 0));
        account = Step(machine, dispenser, account, W(70), Ok(N(1, 1, 0), 0, A(170, 160)), N(0, 0, 0));
        Step(machine, dispenser, account, W(40), Refused(AtmStatus.CannotDispense, A(170, 160)), N(0, 0, 0));
    }

    [Fact]
    public void Scenario_DepositedNotesCanBeWithdrawnAgain()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var machine = new CashMachine(dispenser);
        var account = A(0, 0);

        account = Step(machine, dispenser, account, D(1, 1, 1), Ok(NoteCounts.None, 0, A(80, 0)), N(1, 1, 1));
        account = Step(machine, dispenser, account, D(1, 1, 1), Ok(NoteCounts.None, 0, A(160, 0)), N(2, 2, 2));
        account = Step(machine, dispenser, account, W(160), Refused(AtmStatus.InsufficientFunds, A(160, 0)), N(2, 2, 2));
        account = Step(machine, dispenser, account, W(150), Ok(N(2, 2, 1), 5, A(5, 150)), N(0, 0, 1));
        Step(machine, dispenser, account, W(10), Refused(AtmStatus.InsufficientFunds, A(5, 150)), N(0, 0, 1));
    }

    [Fact]
    public void Scenario_CombinationFollowsStockDepletion()
    {
        var dispenser = new MoneyDispenser(N(2, 3, 2));
        var machine = new CashMachine(dispenser);
        var account = A(250, 0);

        account = Step(machine, dispenser, account, W(60), Ok(N(1, 0, 1), 0, A(190, 60)), N(1, 3, 1));
        account = Step(machine, dispenser, account, W(60), Ok(N(1, 0, 1), 0, A(130, 120)), N(0, 3, 0));
        account = Step(machine, dispenser, account, W(60), Ok(N(0, 3, 0), 0, A(70, 180)), N(0, 0, 0));
        Step(machine, dispenser, account, W(10), Refused(AtmStatus.CannotDispense, A(70, 180)), N(0, 0, 0));
    }

    [Fact]
    public void Scenario_JammedCassette_StaysUntouchedThroughoutTheSession()
    {
        var dispenser = new MoneyDispenser(N(2, 5, 3), Denomination.Twenty);
        var machine = new CashMachine(dispenser);
        var account = A(100, 0);

        // Bloquée passe avant pleine.
        account = Step(machine, dispenser, account, D(0, 1, 0), Refused(AtmStatus.CassetteJammed, A(100, 0)), N(2, 5, 3));
        account = Step(machine, dispenser, account, D(1, 1, 1), Refused(AtmStatus.CassetteJammed, A(100, 0)), N(2, 5, 3));
        account = Step(machine, dispenser, account, D(1, 0, 1), Ok(NoteCounts.None, 0, A(160, 0)), N(3, 5, 4));
        account = Step(machine, dispenser, account, W(40), Ok(N(0, 0, 4), 0, A(120, 40)), N(3, 5, 0));
        account = Step(machine, dispenser, account, W(20), Refused(AtmStatus.CannotDispense, A(120, 40)), N(3, 5, 0));
        account = Step(machine, dispenser, account, W(100), Ok(N(2, 0, 0), 0, A(20, 140)), N(1, 5, 0));
        account = Step(machine, dispenser, account, D(0, 0, 1), Ok(NoteCounts.None, 0, A(30, 140)), N(1, 5, 1));
        Step(machine, dispenser, account, W(30), Refused(AtmStatus.CannotDispense, A(30, 140)), N(1, 5, 1));

        Assert.Equal(Denomination.Twenty, dispenser.Jammed);
    }

    [Fact]
    public void Scenario_FillCassettesToCapacity()
    {
        var dispenser = new MoneyDispenser(N(3, 4, 3));
        var machine = new CashMachine(dispenser);
        var account = A(0, 0);

        account = Step(machine, dispenser, account, D(1, 1, 1), Ok(NoteCounts.None, 0, A(80, 0)), N(4, 5, 4));
        // Les 20 sont pleins : tout le dépôt est refusé, même les billets qui auraient eu de la place.
        account = Step(machine, dispenser, account, D(1, 1, 1), Refused(AtmStatus.CassetteFull, A(80, 0)), N(4, 5, 4));
        account = Step(machine, dispenser, account, D(1, 0, 1), Ok(NoteCounts.None, 0, A(140, 0)), N(5, 5, 5));
        account = Step(machine, dispenser, account, D(0, 0, 1), Refused(AtmStatus.CassetteFull, A(140, 0)), N(5, 5, 5));
        account = Step(machine, dispenser, account, W(20), Ok(N(0, 1, 0), 0, A(120, 20)), N(5, 4, 5));
        Step(machine, dispenser, account, D(0, 1, 0), Ok(NoteCounts.None, 0, A(140, 20)), N(5, 5, 5));
    }

    // ---- La machine est sans mémoire ---------------------------------------------------------------

    [Fact]
    public void TwoMachinesSharingOneModule_SeeEachOthersMoves()
    {
        var dispenser = new MoneyDispenser(N(1, 2, 0));
        var first = new CashMachine(dispenser);
        var second = new CashMachine(dispenser);

        Step(first, dispenser, A(250, 0), W(50), Ok(N(1, 0, 0), 0, A(200, 50)), N(0, 2, 0));
        Step(second, dispenser, A(250, 0), W(50), Refused(AtmStatus.CannotDispense, A(250, 0)), N(0, 2, 0));
        Step(second, dispenser, A(0, 0), D(0, 0, 1), Ok(NoteCounts.None, 0, A(10, 0)), N(0, 2, 1));
        Step(first, dispenser, A(250, 0), W(50), Ok(N(0, 2, 1), 0, A(200, 50)), N(0, 0, 0));
    }

    [Fact]
    public void Machine_KeepsNothingBetweenCalls_OnlyAccountOperationAndModuleMatter()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        // Un refus sur un compte puis, aussitôt, le même retrait sur un autre compte.
        Step(machine, dispenser, A(250, 200), W(50), Refused(AtmStatus.DailyLimitExceeded, A(250, 200)), N(5, 5, 5));
        Step(machine, dispenser, A(250, 0), W(50), Ok(N(1, 0, 0), 0, A(200, 50)), N(4, 5, 5));
        // Le compte rendu n'est pas réutilisé implicitement : on repasse un compte « neuf ».
        Step(machine, dispenser, A(50, 0), W(50), Ok(N(1, 0, 0), 0, A(0, 50)), N(3, 5, 5));

        // Une machine neuve sur un module dans le même état donne le même résultat.
        var twin = new MoneyDispenser(N(3, 5, 5));
        Assert.Equal(machine.Execute(A(250, 0), W(130)), new CashMachine(twin).Execute(A(250, 0), W(130)));
        Assert.Equal(dispenser.Stock, twin.Stock);
    }

    // ---- Tout le domaine, contre la spécification ---------------------------------------------------

    private static readonly Denomination?[] AllJams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] DomainAmounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly Operation[] DomainOperations =
    [
        .. DomainAmounts.Select(W),
        .. from f in new[] { 0, 1 } from t in new[] { 0, 1 } from c in new[] { 0, 1 } select D(f, t, c),
    ];

    private static readonly NoteCounts[] DomainStocks =
        [.. from f in Enumerable.Range(0, 6) from t in Enumerable.Range(0, 6) from c in Enumerable.Range(0, 6) select N(f, t, c)];

    private static readonly AccountState[] DomainAccounts =
        [.. from b in Enumerable.Range(0, 51) from w in Enumerable.Range(0, 21) select A(b * 5, w * 10)];

    /// <summary>
    /// Toutes les combinaisons exactes de <paramref name="amount"/> avec les billets disponibles, la cassette bloquée comptant pour zéro.
    /// </summary>
    private static List<NoteCounts> Combinations(int amount, NoteCounts stock, Denomination? jammed)
    {
        int Available(Denomination d) => jammed == d ? 0 : stock.Of(d);

        var combinations = new List<NoteCounts>();
        for (var f = 0; f <= Available(Denomination.Fifty); f++)
        for (var t = 0; t <= Available(Denomination.Twenty); t++)
        for (var c = 0; c <= Available(Denomination.Ten); c++)
        {
            if (f * 50 + t * 20 + c * 10 == amount)
            {
                combinations.Add(N(f, t, c));
            }
        }

        return combinations;
    }

    /// <summary>
    /// Vérifie une opération contre la spécification : le résultat, et le stock du module avant/après.
    /// Rend la liste des écarts, vide si tout est conforme.
    /// </summary>
    private static List<string> Violations(
        AccountState account, NoteCounts stockBefore, Denomination? jammed, Operation operation,
        AtmResult actual, NoteCounts stockAfter, IReadOnlyList<NoteCounts>? combinations = null)
    {
        var errors = new List<string>();
        void Expect(bool condition, string message)
        {
            if (!condition) errors.Add(message);
        }

        Expect(actual.Status != AtmStatus.DispenserFault, "le vrai module ne tombe jamais en panne dans le domaine");
        Expect(stockAfter is { Fifties: >= 0 and <= 5, Twenties: >= 0 and <= 5, Tens: >= 0 and <= 5 }, "stock hors des cassettes");

        AtmStatus expectedStatus;
        switch (operation)
        {
            case Operation.Withdraw(var amount):
            {
                var fee = amount > 100 ? 5 : 0;
                expectedStatus =
                    amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
                    : account.WithdrawnToday + amount > 200 ? AtmStatus.DailyLimitExceeded
                    : amount + fee > account.Balance ? AtmStatus.InsufficientFunds
                    : (combinations ?? Combinations(amount, stockBefore, jammed)).Count == 0 ? AtmStatus.CannotDispense
                    : AtmStatus.Ok;

                if (expectedStatus == AtmStatus.Ok && actual.Status == AtmStatus.Ok)
                {
                    var all = combinations ?? Combinations(amount, stockBefore, jammed);
                    var fewest = all.Min(n => n.Total);
                    var best = all.Where(n => n.Total == fewest).ToList();

                    Expect(best.Count == 1, $"la combinaison minimale devrait être unique : {string.Join(", ", best)}");
                    Expect(actual.Notes == best[0], $"billets {actual.Notes}, attendu {best[0]}");
                    Expect(actual.Notes.Value == amount, "les billets ne font pas le montant");
                    Expect(jammed is not { } j || actual.Notes.Of(j) == 0, "billets sortis de la cassette bloquée");
                    Expect(actual.Fee == fee, $"frais {actual.Fee}, attendu {fee}");
                    Expect(actual.Account == A(account.Balance - amount - fee, account.WithdrawnToday + amount), "compte après retrait");
                    Expect(stockAfter == stockBefore - actual.Notes, "stock ≠ stock initial − billets rendus");
                }

                break;
            }

            case Operation.Deposit(var notes):
            {
                var after = stockBefore + notes;
                expectedStatus =
                    notes.Total == 0 ? AtmStatus.InvalidAmount
                    : jammed is { } j && notes.Of(j) > 0 ? AtmStatus.CassetteJammed
                    : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
                    : AtmStatus.Ok;

                if (expectedStatus == AtmStatus.Ok && actual.Status == AtmStatus.Ok)
                {
                    Expect(actual.Notes == NoteCounts.None, "un dépôt ne rend pas de billets");
                    Expect(actual.Fee == 0, "un dépôt ne prend pas de frais");
                    Expect(actual.Account == account with { Balance = account.Balance + notes.Value }, "compte après dépôt");
                    Expect(stockAfter == after, "stock ≠ stock initial + dépôt");
                }

                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        Expect(actual.Status == expectedStatus, $"statut {actual.Status}, attendu {expectedStatus}");

        if (expectedStatus != AtmStatus.Ok)
        {
            Expect(actual.Notes == NoteCounts.None && actual.Fee == 0 && actual.Account == account,
                "un refus ne rend aucun billet, ne prend aucun frais et laisse le compte identique");
            Expect(stockAfter == stockBefore, "un refus ne déplace aucun billet");
        }

        return errors;
    }

    [Fact]
    public void WholeDomain_EveryOperation_MatchesSpec_AndModuleStock()
    {
        var failures = new ConcurrentQueue<string>();
        var executed = 0L;

        Parallel.ForEach(
            from stock in DomainStocks from jammed in AllJams select (stock, jammed),
            () => 0L,
            (item, state, count) =>
            {
                var (stock, jammed) = item;
                var combinations = DomainAmounts.ToDictionary(a => a, a => Combinations(a, stock, jammed));

                foreach (var account in DomainAccounts)
                foreach (var operation in DomainOperations)
                {
                    var dispenser = new MoneyDispenser(stock, jammed);
                    var actual = new CashMachine(dispenser).Execute(account, operation);
                    count++;

                    var errors = Violations(account, stock, jammed, operation, actual, dispenser.Stock,
                        operation is Operation.Withdraw(var amount) ? combinations[amount] : null);

                    if (errors.Count > 0)
                    {
                        failures.Enqueue($"{account} {stock} bloquée={jammed} {operation} → {actual} ; stock {dispenser.Stock} : {string.Join(" ; ", errors)}");
                        if (failures.Count > 20) state.Stop();
                    }
                }

                return count;
            },
            count => Interlocked.Add(ref executed, count));

        Assert.Empty(failures.Take(20));
        Assert.Equal((long)DomainStocks.Length * AllJams.Length * DomainAccounts.Length * DomainOperations.Length, executed);
    }

    // ---- Longues séquences sur un même module ------------------------------------------------------

    public static TheoryData<int> Seeds => new(Enumerable.Range(0, 200));

    /// <summary>
    /// Une suite d'opérations tirées au hasard (graine fixe) sur un seul module, en enchaînant le compte rendu.
    /// Le stock attendu est suivi à part, à partir des seuls résultats, et comparé au module après chaque opération.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void RandomSession_ChainedOperations_MatchSpec_AndStockIsConserved(int seed)
    {
        var random = new Random(seed);
        var jammed = AllJams[random.Next(AllJams.Length)];
        var initial = N(random.Next(6), random.Next(6), random.Next(6));
        var dispenser = new MoneyDispenser(initial, jammed);
        var machine = new CashMachine(dispenser);
        var account = A(random.Next(51) * 5, random.Next(21) * 10);

        var expectedStock = initial;
        var dispensedTotal = NoteCounts.None;
        var depositedTotal = NoteCounts.None;
        var log = new List<string>();

        for (var step = 0; step < 60; step++)
        {
            // Un nouveau jour, de temps en temps, pour que le plafond ne bloque pas toute la séquence.
            if (random.Next(8) == 0)
            {
                account = account with { WithdrawnToday = 0 };
            }

            var operation = DomainOperations[random.Next(DomainOperations.Length)];

            // Un dépôt qui ferait sortir le solde du domaine (250 au plus) est remplacé par un retrait.
            if (operation is Operation.Deposit(var deposit) && account.Balance + deposit.Value > 250)
            {
                operation = W(DomainAmounts[random.Next(DomainAmounts.Length)]);
            }

            var before = dispenser.Stock;
            var result = machine.Execute(account, operation);
            log.Add($"{step}: {account} {before} {operation} → {result}");

            var errors = Violations(account, before, jammed, operation, result, dispenser.Stock);
            Assert.True(errors.Count == 0,
                $"graine {seed}, bloquée={jammed}\n{string.Join("\n", log)}\nstock après {dispenser.Stock} : {string.Join(" ; ", errors)}");

            if (result.Status == AtmStatus.Ok)
            {
                switch (operation)
                {
                    case Operation.Withdraw:
                        expectedStock -= result.Notes;
                        dispensedTotal += result.Notes;
                        break;
                    case Operation.Deposit(var notes):
                        expectedStock += notes;
                        depositedTotal += notes;
                        break;
                }
            }

            Assert.Equal(expectedStock, dispenser.Stock);
            Assert.Equal(jammed, dispenser.Jammed);
            account = result.Account;
        }

        // Conservation : rien ne se crée ni ne se perd dans le module.
        Assert.Equal(initial + depositedTotal - dispensedTotal, dispenser.Stock);
        if (jammed is { } j)
        {
            Assert.Equal(initial.Of(j), dispenser.Stock.Of(j));
        }
    }
}
