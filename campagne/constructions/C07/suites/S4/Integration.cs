namespace Atm.Tests;

// Tests d'intégration : CashMachine travaille avec le vrai MoneyDispenser, sans doublure ni enveloppe.
// Après chaque opération, on vérifie le résultat ET le stock réel du module.
//
// L'oracle (IntegrationSpec) est réécrit d'après spec.md, indépendamment du code de la machine : il ne réutilise
// ni ses constantes ni ses algorithmes (la combinaison est trouvée par énumération brute).

/// <summary>Modèle de référence et domaine, tirés directement de la spécification.</summary>
file static class IntegrationSpec
{
    public static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    public static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var d = 0; d <= 5; d++)
            yield return N(f, t, d);
    }

    public static IEnumerable<NoteCounts> Deposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var d = 0; d <= 1; d++)
            yield return N(f, t, d);
    }

    public static IEnumerable<AccountState> Accounts()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
            yield return new AccountState(balance, withdrawn);
    }

    public static bool InDomain(AccountState account) =>
        account.Balance is >= 0 and <= 250 && account.Balance % 5 == 0
        && account.WithdrawnToday is >= 0 and <= 200 && account.WithdrawnToday % 10 == 0;

    public static bool InDomain(NoteCounts stock) =>
        stock is { Fifties: >= 0 and <= 5, Twenties: >= 0 and <= 5, Tens: >= 0 and <= 5 };

    /// <summary>Le stock sans la cassette bloquée, qui compte pour zéro.</summary>
    public static NoteCounts Available(NoteCounts stock, Denomination? jammed) => N(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    /// <summary>Toutes les combinaisons exactes, puis celle qui compte le moins de billets (unique d'après la spec).</summary>
    public static NoteCounts? Fewest(int amount, NoteCounts available)
    {
        var exact = new List<NoteCounts>();
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var d = 0; d <= available.Tens; d++)
            if (f * 50 + t * 20 + d * 10 == amount)
                exact.Add(N(f, t, d));

        if (exact.Count == 0) return null;

        var min = exact.Min(c => c.Total);
        var best = exact.Where(c => c.Total == min).ToList();
        if (best.Count != 1)
            throw new InvalidOperationException($"Combinaison non unique pour {amount} avec {available}.");

        return best[0];
    }

    public static (AtmResult Result, NoteCounts Stock) Withdraw(
        AccountState account, NoteCounts stock, Denomination? jammed, int amount) =>
        Withdraw(account, stock, amount, amount > 0 && amount % 10 == 0 ? Fewest(amount, Available(stock, jammed)) : null);

    /// <summary>Retrait, avec la combinaison déjà calculée (pour les parcours exhaustifs).</summary>
    public static (AtmResult Result, NoteCounts Stock) Withdraw(
        AccountState account, NoteCounts stock, int amount, NoteCounts? combination)
    {
        (AtmResult, NoteCounts) Refused(AtmStatus status) => (new AtmResult(status, NoteCounts.None, 0, account), stock);

        if (amount <= 0 || amount % 10 != 0) return Refused(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return Refused(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return Refused(AtmStatus.InsufficientFunds);
        if (combination is not { } notes) return Refused(AtmStatus.CannotDispense);

        var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
        return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
    }

    public static (AtmResult Result, NoteCounts Stock) Deposit(
        AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        (AtmResult, NoteCounts) Refused(AtmStatus status) => (new AtmResult(status, NoteCounts.None, 0, account), stock);

        if (notes.Total == 0) return Refused(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return Refused(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refused(AtmStatus.CassetteFull);

        var credited = account with { Balance = account.Balance + notes.Value };
        return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, credited), after);
    }

    public static (AtmResult Result, NoteCounts Stock) Expected(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation) => operation switch
    {
        Operation.Withdraw w => Withdraw(account, stock, jammed, w.Amount),
        Operation.Deposit d => Deposit(account, stock, jammed, d.Notes),
        _ => throw new ArgumentException("Opération inattendue.", nameof(operation)),
    };

    /// <summary>
    /// Second oracle, plus grossier mais indépendant du premier : conservation des billets et de l'argent.
    /// Rend la première règle violée, ou <c>null</c>.
    /// </summary>
    public static string? Invariants(
        AccountState account, NoteCounts before, NoteCounts after, Denomination? jammed, Operation operation, AtmResult result)
    {
        if (result.Status == AtmStatus.DispenserFault) return "le vrai module ne doit jamais provoquer de panne";
        if (!InDomain(after)) return "le stock du module sort de 0..5";

        var ok = result.Status == AtmStatus.Ok;
        switch (operation)
        {
            case Operation.Withdraw w:
                if (before - after != result.Notes) return "les billets rendus ne sont pas ceux sortis du module";
                if (result.Notes.Value != (ok ? w.Amount : 0)) return "la valeur des billets ne fait pas le montant";
                if (jammed is { } j && result.Notes.Of(j) != 0) return "des billets sortent de la cassette bloquée";
                if (result.Fee != (ok && w.Amount > 100 ? 5 : 0)) return "frais incorrects";
                if (account.Balance - result.Account.Balance != result.Notes.Value + result.Fee) return "débit incorrect";
                if (result.Account.WithdrawnToday - account.WithdrawnToday != result.Notes.Value) return "cumul du jour incorrect";
                break;

            case Operation.Deposit d:
                var loaded = after - before;
                if (loaded != (ok ? d.Notes : NoteCounts.None)) return "le module n'a pas reçu exactement le dépôt";
                if (result.Notes != NoteCounts.None) return "un dépôt ne rend pas de billets";
                if (result.Fee != 0) return "un dépôt ne prélève pas de frais";
                if (result.Account.Balance - account.Balance != loaded.Value) return "crédit incorrect";
                if (result.Account.WithdrawnToday != account.WithdrawnToday) return "un dépôt ne touche pas au cumul du jour";
                break;
        }

        return null;
    }
}

/// <summary>Une seule machine et son vrai module, qui vivent ensemble le temps d'un scénario.</summary>
file sealed class Session
{
    public Session(NoteCounts stock, Denomination? jammed = null)
    {
        Dispenser = new MoneyDispenser(stock, jammed);
        Machine = new CashMachine(Dispenser);
    }

    public MoneyDispenser Dispenser { get; }

    public CashMachine Machine { get; }

    /// <summary>Exécute, puis vérifie le résultat, le stock réel du module et les invariants de conservation.</summary>
    public AtmResult Run(AccountState account, Operation operation, AtmResult expected, NoteCounts expectedStock) =>
        Check(Machine, Dispenser, account, operation, expected, expectedStock);

    public static AtmResult Check(
        CashMachine machine, MoneyDispenser dispenser, AccountState account, Operation operation,
        AtmResult expected, NoteCounts expectedStock)
    {
        var before = dispenser.Stock;
        var result = machine.Execute(account, operation);
        var context = $"compte={account} stock={before} bloquée={dispenser.Jammed} opération={operation}";

        Assert.True(expected == result, $"{context} : attendu {expected}, obtenu {result}");
        Assert.True(expectedStock == dispenser.Stock, $"{context} : stock attendu {expectedStock}, obtenu {dispenser.Stock}");
        Assert.Null(IntegrationSpec.Invariants(account, before, dispenser.Stock, dispenser.Jammed, operation, result));
        return result;
    }
}

/// <summary>Le domaine entier de la spécification, case par case, avec le vrai module.</summary>
public class IntegrationDomainTests
{
    private const int MaxReported = 10;

    private static void Report(List<string> failures, ref int count, string message)
    {
        count++;
        if (failures.Count < MaxReported) failures.Add(message);
    }

    private static void AssertNoFailure(List<string> failures, int count, int cases) =>
        Assert.True(count == 0, $"{count} écart(s) sur {cases} cas :\n" + string.Join("\n", failures));

    /// <summary>Retrait : chaque solde, chaque cumul, chaque stock, chaque cassette bloquée, chaque montant.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Withdraw_WholeDomain_MatchesSpecification(Denomination? jammed)
    {
        var accounts = IntegrationSpec.Accounts().ToArray();
        var failures = new List<string>();
        int count = 0, cases = 0;

        foreach (var stock in IntegrationSpec.Stocks())
        foreach (var amount in IntegrationSpec.Amounts)
        {
            // La combinaison ne dépend que du stock disponible et du montant : on la calcule une fois.
            var combination = amount > 0 && amount % 10 == 0
                ? IntegrationSpec.Fewest(amount, IntegrationSpec.Available(stock, jammed))
                : null;
            var operation = new Operation.Withdraw(amount);

            foreach (var account in accounts)
            {
                cases++;
                var dispenser = new MoneyDispenser(stock, jammed);
                var result = new CashMachine(dispenser).Execute(account, operation);
                var (expected, expectedStock) = IntegrationSpec.Withdraw(account, stock, amount, combination);

                if (result != expected || dispenser.Stock != expectedStock)
                {
                    Report(failures, ref count,
                        $"compte={account} stock={stock} retrait={amount} : attendu {expected} / {expectedStock}, obtenu {result} / {dispenser.Stock}");
                }
                else if (IntegrationSpec.Invariants(account, stock, dispenser.Stock, jammed, operation, result) is { } broken)
                {
                    Report(failures, ref count, $"compte={account} stock={stock} retrait={amount} : {broken}");
                }
            }
        }

        AssertNoFailure(failures, count, cases);
    }

    /// <summary>Dépôt : chaque solde, chaque cumul, chaque stock, chaque cassette bloquée, chaque dépôt.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(Denomination.Fifty)]
    [InlineData(Denomination.Twenty)]
    [InlineData(Denomination.Ten)]
    public void Deposit_WholeDomain_MatchesSpecification(Denomination? jammed)
    {
        var accounts = IntegrationSpec.Accounts().ToArray();
        var failures = new List<string>();
        int count = 0, cases = 0;

        foreach (var stock in IntegrationSpec.Stocks())
        foreach (var notes in IntegrationSpec.Deposits())
        {
            var operation = new Operation.Deposit(notes);

            foreach (var account in accounts)
            {
                cases++;
                var dispenser = new MoneyDispenser(stock, jammed);
                var result = new CashMachine(dispenser).Execute(account, operation);
                var (expected, expectedStock) = IntegrationSpec.Deposit(account, stock, jammed, notes);

                if (result != expected || dispenser.Stock != expectedStock)
                {
                    Report(failures, ref count,
                        $"compte={account} stock={stock} dépôt={notes} : attendu {expected} / {expectedStock}, obtenu {result} / {dispenser.Stock}");
                }
                else if (IntegrationSpec.Invariants(account, stock, dispenser.Stock, jammed, operation, result) is { } broken)
                {
                    Report(failures, ref count, $"compte={account} stock={stock} dépôt={notes} : {broken}");
                }
            }
        }

        AssertNoFailure(failures, count, cases);
    }

    /// <summary>L'oracle lui-même doit retrouver les exemples de la spécification, sinon il ne prouve rien.</summary>
    [Fact]
    public void ReferenceModel_AgreesWithSpecificationExamples()
    {
        var n = IntegrationSpec.N;
        var a = (int b, int w) => new AccountState(b, w);

        Assert.Equal((new AtmResult(AtmStatus.Ok, n(0, 3, 0), 0, a(40, 60)), n(1, 0, 0)),
            IntegrationSpec.Withdraw(a(100, 0), n(1, 3, 0), null, 60));
        Assert.Equal((new AtmResult(AtmStatus.Ok, n(2, 0, 1), 5, a(0, 110)), n(3, 5, 4)),
            IntegrationSpec.Withdraw(a(115, 0), n(5, 5, 5), null, 110));
        Assert.Equal(AtmStatus.InsufficientFunds, IntegrationSpec.Withdraw(a(110, 0), n(5, 5, 5), null, 110).Result.Status);
        Assert.Equal(AtmStatus.CannotDispense, IntegrationSpec.Withdraw(a(250, 0), n(1, 0, 0), Denomination.Fifty, 50).Result.Status);
        Assert.Equal(AtmStatus.InvalidAmount, IntegrationSpec.Withdraw(a(0, 200), n(0, 0, 0), null, 5).Result.Status);
        Assert.Equal(AtmStatus.CassetteJammed, IntegrationSpec.Deposit(a(10, 30), n(0, 1, 0), Denomination.Twenty, n(0, 1, 0)).Result.Status);
        Assert.Equal((new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, a(70, 30)), n(2, 1, 1)),
            IntegrationSpec.Deposit(a(10, 30), n(1, 1, 0), null, n(1, 0, 1)));
    }
}

/// <summary>Une même machine et un même module, à travers des suites d'opérations écrites à la main.</summary>
public class IntegrationScenarioTests
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static AccountState A(int balance, int withdrawn) => new(balance, withdrawn);

    private static Operation W(int amount) => new Operation.Withdraw(amount);

    private static Operation D(int fifties, int twenties, int tens) => new Operation.Deposit(N(fifties, twenties, tens));

    private static AtmResult Ok(NoteCounts notes, int fee, AccountState account) => new(AtmStatus.Ok, notes, fee, account);

    private static AtmResult Ok(AccountState account) => new(AtmStatus.Ok, NoteCounts.None, 0, account);

    private static AtmResult No(AtmStatus status, AccountState account) => new(status, NoteCounts.None, 0, account);

    [Fact]
    public void EmptyingThenRefillingTheModule()
    {
        var s = new Session(N(1, 1, 1));

        s.Run(A(250, 0), W(80), Ok(N(1, 1, 1), 0, A(170, 80)), N(0, 0, 0));
        s.Run(A(170, 80), W(10), No(AtmStatus.CannotDispense, A(170, 80)), N(0, 0, 0));
        s.Run(A(170, 80), D(0, 0, 0), No(AtmStatus.InvalidAmount, A(170, 80)), N(0, 0, 0));
        s.Run(A(170, 80), D(0, 1, 1), Ok(A(200, 80)), N(0, 1, 1));
        s.Run(A(200, 80), W(30), Ok(N(0, 1, 1), 0, A(170, 110)), N(0, 0, 0));
    }

    [Fact]
    public void FillingACassetteNoteByNote_ThenFullDepositIsRefusedAsAWhole()
    {
        var s = new Session(N(0, 3, 0));

        s.Run(A(0, 0), D(0, 1, 0), Ok(A(20, 0)), N(0, 4, 0));
        s.Run(A(20, 0), D(0, 1, 0), Ok(A(40, 0)), N(0, 5, 0));
        s.Run(A(40, 0), D(0, 1, 0), No(AtmStatus.CassetteFull, A(40, 0)), N(0, 5, 0));
        // Tout ou rien : le billet de 50, qui aurait tenu, n'entre pas non plus.
        s.Run(A(40, 0), D(1, 1, 0), No(AtmStatus.CassetteFull, A(40, 0)), N(0, 5, 0));
        s.Run(A(40, 0), D(1, 0, 0), Ok(A(90, 0)), N(1, 5, 0));
        // 40 = 20×2 : le 50 tout juste déposé ne sert à rien.
        s.Run(A(90, 0), W(40), Ok(N(0, 2, 0), 0, A(50, 40)), N(1, 3, 0));
        s.Run(A(50, 40), D(0, 1, 0), Ok(A(70, 40)), N(1, 4, 0));
    }

    [Fact]
    public void DailyLimitAcrossSeveralWithdrawals_IsNotResetByADeposit()
    {
        var s = new Session(N(5, 5, 5));

        s.Run(A(250, 0), W(100), Ok(N(2, 0, 0), 0, A(150, 100)), N(3, 5, 5));
        s.Run(A(150, 100), W(100), Ok(N(2, 0, 0), 0, A(50, 200)), N(1, 5, 5));
        s.Run(A(50, 200), W(10), No(AtmStatus.DailyLimitExceeded, A(50, 200)), N(1, 5, 5));
        s.Run(A(50, 200), D(1, 0, 0), Ok(A(100, 200)), N(2, 5, 5));
        s.Run(A(100, 200), W(10), No(AtmStatus.DailyLimitExceeded, A(100, 200)), N(2, 5, 5));
    }

    [Fact]
    public void JammedCassette_NeverGivesNorTakesNotes_WhileTheOthersWork()
    {
        var s = new Session(N(5, 5, 5), Denomination.Fifty);

        // 100 sans billet de 50 : 20×5, cinq billets donc deux passages.
        s.Run(A(250, 0), W(100), Ok(N(0, 5, 0), 0, A(150, 100)), N(5, 0, 5));
        s.Run(A(150, 100), W(50), Ok(N(0, 0, 5), 0, A(100, 150)), N(5, 0, 0));
        // Cinq billets de 50 dans le module, mais tous bloqués.
        s.Run(A(100, 150), W(50), No(AtmStatus.CannotDispense, A(100, 150)), N(5, 0, 0));
        s.Run(A(100, 150), D(1, 0, 0), No(AtmStatus.CassetteJammed, A(100, 150)), N(5, 0, 0));
        s.Run(A(100, 150), D(1, 1, 1), No(AtmStatus.CassetteJammed, A(100, 150)), N(5, 0, 0));
        s.Run(A(100, 150), D(0, 1, 1), Ok(A(130, 150)), N(5, 1, 1));
        s.Run(A(130, 150), W(30), Ok(N(0, 1, 1), 0, A(100, 180)), N(5, 0, 0));
        Assert.Equal(Denomination.Fifty, s.Dispenser.Jammed);
    }

    [Fact]
    public void LargestWithdrawal_ElevenNotesInSeveralPasses_EmptiesTheModule()
    {
        var s = new Session(N(1, 5, 5));

        s.Run(A(205, 0), W(200), Ok(N(1, 5, 5), 5, A(0, 200)), N(0, 0, 0));
    }

    [Fact]
    public void FewestNotes_IsComputedOnTheCurrentStock_NotTheInitialOne()
    {
        var s = new Session(N(2, 3, 2));

        // Avec deux 50, 100 = 50×2.
        s.Run(A(250, 0), W(100), Ok(N(2, 0, 0), 0, A(150, 100)), N(0, 3, 2));
        // Plus de 50 : 60 = 20×3 (trois billets) plutôt que 20×2 10×2.
        s.Run(A(150, 100), W(60), Ok(N(0, 3, 0), 0, A(90, 160)), N(0, 0, 2));
        s.Run(A(90, 160), W(40), No(AtmStatus.CannotDispense, A(90, 160)), N(0, 0, 2));
        s.Run(A(90, 160), W(20), Ok(N(0, 0, 2), 0, A(70, 180)), N(0, 0, 0));
    }

    [Fact]
    public void Refusals_LeaveTheModuleIntact_ForTheNextOperation()
    {
        var s = new Session(N(1, 1, 0));

        s.Run(A(250, 0), W(-10), No(AtmStatus.InvalidAmount, A(250, 0)), N(1, 1, 0));
        s.Run(A(250, 190), W(70), No(AtmStatus.DailyLimitExceeded, A(250, 190)), N(1, 1, 0));
        s.Run(A(65, 0), W(70), No(AtmStatus.InsufficientFunds, A(65, 0)), N(1, 1, 0));
        s.Run(A(250, 0), W(60), No(AtmStatus.CannotDispense, A(250, 0)), N(1, 1, 0));
        s.Run(A(250, 0), W(70), Ok(N(1, 1, 0), 0, A(180, 70)), N(0, 0, 0));
    }

    [Fact]
    public void MachineKeepsNoAccountMemory_OnlyTheModuleCarriesState()
    {
        var s = new Session(N(5, 5, 5));

        // Le même compte, présenté deux fois : la machine ne se souvient pas du premier retrait...
        s.Run(A(250, 150), W(50), Ok(N(1, 0, 0), 0, A(200, 200)), N(4, 5, 5));
        s.Run(A(250, 150), W(50), Ok(N(1, 0, 0), 0, A(200, 200)), N(3, 5, 5));
        // ...mais le module, lui, s'est vidé d'autant.
        s.Run(A(250, 0), W(150), Ok(N(3, 0, 0), 5, A(95, 150)), N(0, 5, 5));
    }

    [Fact]
    public void MachineReadsTheModuleAtEachCall_EvenWhenItIsChangedFromOutside()
    {
        var s = new Session(N(0, 0, 0));

        s.Run(A(250, 0), W(50), No(AtmStatus.CannotDispense, A(250, 0)), N(0, 0, 0));
        Assert.True(s.Dispenser.Load(N(1, 0, 0)));
        s.Run(A(250, 0), W(50), Ok(N(1, 0, 0), 0, A(200, 50)), N(0, 0, 0));
        Assert.True(s.Dispenser.Load(N(0, 5, 0)));
        s.Run(A(200, 0), D(0, 1, 0), No(AtmStatus.CassetteFull, A(200, 0)), N(0, 5, 0));
    }

    [Fact]
    public void TwoMachinesSharingOneModule_SeeEachOthersOperations()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var first = new CashMachine(dispenser);
        var second = new CashMachine(dispenser);

        Session.Check(first, dispenser, A(250, 0), W(50), Ok(N(1, 0, 0), 0, A(200, 50)), N(0, 0, 0));
        Session.Check(second, dispenser, A(250, 0), W(50), No(AtmStatus.CannotDispense, A(250, 0)), N(0, 0, 0));
        Session.Check(second, dispenser, A(0, 0), D(1, 1, 1), Ok(A(80, 0)), N(1, 1, 1));
        Session.Check(first, dispenser, A(80, 0), W(80), Ok(N(1, 1, 1), 0, A(0, 80)), N(0, 0, 0));
    }
}

/// <summary>
/// Longues suites d'opérations tirées au hasard (graine fixe, donc reproductibles) sur une même machine et un même module.
/// Le stock attendu est suivi par l'oracle, indépendamment du module.
/// </summary>
public class IntegrationSequenceTests
{
    public static TheoryData<int> Seeds() => [.. Enumerable.Range(1, 40)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void RandomWalk_MatchesSpecificationAtEveryStep(int seed)
    {
        var random = new Random(seed);
        var stock = new NoteCounts(random.Next(6), random.Next(6), random.Next(6));
        var jammed = IntegrationSpec.Jams[random.Next(IntegrationSpec.Jams.Length)];
        var dispenser = new MoneyDispenser(stock, jammed);
        var machine = new CashMachine(dispenser);

        var account = RandomAccount(random);
        for (var step = 0; step < 2_000; step++)
        {
            Operation operation = random.Next(3) < 2
                ? new Operation.Withdraw(IntegrationSpec.Amounts[random.Next(IntegrationSpec.Amounts.Length)])
                : new Operation.Deposit(new NoteCounts(random.Next(2), random.Next(2), random.Next(2)));

            var (expected, expectedStock) = IntegrationSpec.Expected(account, stock, jammed, operation);
            var result = Session.Check(machine, dispenser, account, operation, expected, expectedStock);
            stock = expectedStock;

            // On enchaîne sur le compte rendu quand il reste dans le domaine, sinon on repart d'un autre compte.
            account = IntegrationSpec.InDomain(result.Account) && random.Next(4) > 0 ? result.Account : RandomAccount(random);
        }

        Assert.Equal(jammed, dispenser.Jammed);
    }

    private static AccountState RandomAccount(Random random) => new(random.Next(51) * 5, random.Next(21) * 10);
}
