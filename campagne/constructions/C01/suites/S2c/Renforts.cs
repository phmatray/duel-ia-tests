namespace Atm.Tests;

/// <summary>Tests de renfort : domaine complet, appels au module, pannes à chaque passage, réutilisation de la machine.</summary>
public class Renforts
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static AccountState A(int balance, int withdrawn) => new(balance, withdrawn);

    public static TheoryData<Denomination?> Jams => new() { null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten };

    private static readonly int[] Balances = Enumerable.Range(0, 51).Select(i => i * 5).ToArray();

    private static readonly int[] WithdrawnValues = Enumerable.Range(0, 21).Select(i => i * 10).ToArray();

    private static readonly int[] Amounts = new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10)).ToArray();

    private static readonly NoteCounts[] Deposits =
        (from f in new[] { 0, 1 } from t in new[] { 0, 1 } from c in new[] { 0, 1 } select N(f, t, c)).ToArray();

    private static readonly Operation[] Operations =
        Amounts.Select(a => (Operation)new Operation.Withdraw(a))
            .Concat(Deposits.Select(d => (Operation)new Operation.Deposit(d)))
            .ToArray();

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var c = 0; c <= 5; c++)
            yield return N(f, t, c);
    }

    private static NoteCounts Sum(IEnumerable<NoteCounts> notes) => notes.Aggregate(NoteCounts.None, (a, b) => a + b);

    /// <summary>Un espion autour du vrai module : enregistre chaque appel et son résultat.</summary>
    private sealed class Spy : IMoneyDispenser
    {
        private MoneyDispenser real = new(NoteCounts.None);

        public List<(NoteCounts Notes, bool Ok)> Dispensed { get; } = [];

        public List<(NoteCounts Notes, bool Ok)> Loaded { get; } = [];

        public void Reset(NoteCounts stock, Denomination? jammed)
        {
            real = new MoneyDispenser(stock, jammed);
            Dispensed.Clear();
            Loaded.Clear();
        }

        public NoteCounts Stock => real.Stock;

        public Denomination? Jammed => real.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            var ok = real.Dispense(notes);
            Dispensed.Add((notes, ok));
            return ok;
        }

        public bool Load(NoteCounts notes)
        {
            var ok = real.Load(notes);
            Loaded.Add((notes, ok));
            return ok;
        }
    }

    // ---- Le domaine entier, avec contrôle des appels au module ------------------------------------

    /// <summary>
    /// Compare la machine au modèle de référence sur tout le domaine (chaque solde, chaque montant déjà retiré),
    /// et vérifie en plus la façon dont le module est sollicité.
    /// </summary>
    [Theory]
    [MemberData(nameof(Jams))]
    public void WholeDomain_MatchesOracle_AndUsesDispenserCorrectly(Denomination? jammed)
    {
        var spy = new Spy();
        var machine = new CashMachine(spy);
        var failures = new List<string>();

        foreach (var stock in Stocks())
        foreach (var balance in Balances)
        foreach (var withdrawn in WithdrawnValues)
        foreach (var operation in Operations)
        {
            if (failures.Count > 10) break;

            var account = A(balance, withdrawn);
            spy.Reset(stock, jammed);
            var actual = machine.Execute(account, operation);
            var expected = Oracle.Expected(account, stock, jammed, operation);
            string context() => $"{account} {stock} {jammed} {operation}";

            if (actual != expected)
            {
                failures.Add($"{context()}: attendu {expected}, obtenu {actual}");
                continue;
            }

            if (spy.Dispensed.Any(p => !p.Ok) || spy.Loaded.Any(p => !p.Ok))
            {
                failures.Add($"{context()}: le vrai module a refusé un appel");
                continue;
            }

            if (expected.Status != AtmStatus.Ok)
            {
                if (spy.Dispensed.Count != 0 || spy.Loaded.Count != 0 || spy.Stock != stock)
                {
                    failures.Add($"{context()}: refus {expected.Status} mais le module a été sollicité");
                }

                continue;
            }

            switch (operation)
            {
                case Operation.Withdraw:
                    var passes = spy.Dispensed.Select(p => p.Notes).ToList();
                    if (spy.Loaded.Count != 0
                        || passes.Any(p => p.Total is < 1 or > MoneyDispenser.MaxNotesPerPass)
                        || (jammed is { } j && passes.Any(p => p.Of(j) != 0))
                        || Sum(passes) != expected.Notes
                        || spy.Stock != stock - expected.Notes)
                    {
                        failures.Add($"{context()}: passages incorrects [{string.Join(", ", passes)}]");
                    }

                    break;

                case Operation.Deposit(var deposited):
                    if (spy.Dispensed.Count != 0
                        || Sum(spy.Loaded.Select(p => p.Notes)) != deposited
                        || spy.Stock != stock + deposited)
                    {
                        failures.Add($"{context()}: chargement incorrect [{string.Join(", ", spy.Loaded)}]");
                    }

                    break;
            }
        }

        Assert.Empty(failures);
    }

    // ---- Pannes du module, à chaque passage possible ----------------------------------------------

    [Theory]
    [MemberData(nameof(Jams))]
    public void Withdraw_FaultAtEveryPass_StopsAndReturnsNotesAlreadyOut(Denomination? jammed)
    {
        var account = A(250, 0);
        var failures = new List<string>();
        var faultsSeen = new HashSet<int>();

        foreach (var stock in Stocks())
        foreach (var amount in Amounts)
        {
            var operation = new Operation.Withdraw(amount);
            var expected = Oracle.Expected(account, stock, jammed, operation);
            if (expected.Status != AtmStatus.Ok) continue;

            for (var failAt = 0; failAt < 4; failAt++)
            {
                var dispenser = new FaultyDispenser(stock, jammed) { FailDispenseAt = failAt };
                var actual = new CashMachine(dispenser).Execute(account, operation);
                var context = $"{stock} {jammed} retrait {amount}, panne au passage {failAt}";

                if (failAt >= dispenser.Dispensed.Count)
                {
                    // Pas assez de passages pour atteindre la panne : retrait normal.
                    if (actual != expected) failures.Add($"{context}: attendu {expected}, obtenu {actual}");
                    continue;
                }

                faultsSeen.Add(failAt);
                var alreadyOut = Sum(dispenser.Dispensed.Take(failAt));
                var wanted = new AtmResult(AtmStatus.DispenserFault, alreadyOut, 0, account);
                if (actual != wanted
                    || dispenser.Dispensed.Count != failAt + 1
                    || dispenser.Loaded.Count != 0
                    || dispenser.Stock != stock - alreadyOut)
                {
                    failures.Add($"{context}: attendu {wanted}, obtenu {actual}, appels {dispenser.Dispensed.Count}");
                }

                if (failures.Count > 10) break;
            }
        }

        Assert.Empty(failures);
        // Les retraits les plus longs ont bien été exercés : trois passages (10 billets) quand 50 ou rien n'est
        // bloqué, deux passages sinon (au plus 8 billets).
        Assert.Contains(jammed is null or Denomination.Fifty ? 2 : 1, faultsSeen);
    }

    [Fact]
    public void Withdraw_FaultOnThirdPass_ReturnsTheEightNotesAlreadyOut()
    {
        // 150 sans billet de 50 : 20×5 + 10×5, soit 10 billets, donc au moins trois passages.
        var dispenser = new FaultyDispenser(N(0, 5, 5)) { FailDispenseAt = 2 };
        var r = new CashMachine(dispenser).Execute(A(250, 40), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(A(250, 40), r.Account);
        Assert.Equal(8, r.Notes.Total);
        Assert.Equal(Sum(dispenser.Dispensed.Take(2)), r.Notes);
        Assert.Equal(N(0, 5, 5) - r.Notes, dispenser.Stock);
        Assert.Equal(3, dispenser.Dispensed.Count);
    }

    [Theory]
    [MemberData(nameof(Jams))]
    public void Deposit_LoadFault_ForEveryAcceptedDeposit(Denomination? jammed)
    {
        var account = A(100, 50);
        var failures = new List<string>();

        foreach (var stock in Stocks())
        foreach (var notes in Deposits)
        {
            var operation = new Operation.Deposit(notes);
            if (Oracle.Expected(account, stock, jammed, operation).Status != AtmStatus.Ok) continue;

            var dispenser = new FaultyDispenser(stock, jammed) { FailLoad = true };
            var actual = new CashMachine(dispenser).Execute(account, operation);
            var wanted = new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account);

            if (actual != wanted || dispenser.Dispensed.Count != 0 || dispenser.Loaded.Count != 1 || dispenser.Stock != stock)
            {
                failures.Add($"{stock} {jammed} dépôt {notes}: attendu {wanted}, obtenu {actual}");
            }
        }

        Assert.Empty(failures);
    }

    // ---- Refus : chaque statut, aucun appel au module ---------------------------------------------

    public static TheoryData<int, int, int, int, int, Denomination?, Operation, AtmStatus> Refusals => new()
    {
        { 250, 0, 5, 5, 5, null, new Operation.Withdraw(-10), AtmStatus.InvalidAmount },
        { 250, 0, 5, 5, 5, null, new Operation.Withdraw(0), AtmStatus.InvalidAmount },
        { 250, 0, 5, 5, 5, null, new Operation.Withdraw(5), AtmStatus.InvalidAmount },
        { 250, 100, 5, 5, 5, null, new Operation.Withdraw(110), AtmStatus.DailyLimitExceeded },
        { 250, 0, 5, 5, 5, null, new Operation.Withdraw(220), AtmStatus.DailyLimitExceeded },
        { 100, 0, 5, 5, 5, null, new Operation.Withdraw(110), AtmStatus.InsufficientFunds },
        { 250, 0, 5, 5, 5, Denomination.Ten, new Operation.Withdraw(30), AtmStatus.CannotDispense },
        { 250, 0, 0, 0, 2, null, new Operation.Withdraw(30), AtmStatus.CannotDispense },
        { 250, 0, 5, 5, 5, Denomination.Fifty, new Operation.Withdraw(200), AtmStatus.CannotDispense },
        { 50, 0, 1, 1, 1, null, new Operation.Deposit(NoteCounts.None), AtmStatus.InvalidAmount },
        { 50, 0, 1, 1, 1, Denomination.Fifty, new Operation.Deposit(N(1, 0, 0)), AtmStatus.CassetteJammed },
        { 50, 0, 1, 1, 1, Denomination.Twenty, new Operation.Deposit(N(1, 1, 1)), AtmStatus.CassetteJammed },
        { 50, 0, 4, 4, 5, null, new Operation.Deposit(N(1, 1, 1)), AtmStatus.CassetteFull },
        { 50, 0, 5, 0, 0, Denomination.Ten, new Operation.Deposit(N(1, 1, 0)), AtmStatus.CassetteFull },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Refusal_ChangesNothing_AndNeverCallsDispenser(
        int balance, int withdrawn, int fifties, int twenties, int tens, Denomination? jammed, Operation operation, AtmStatus status)
    {
        var stock = N(fifties, twenties, tens);
        var dispenser = new FaultyDispenser(stock, jammed);
        var r = new CashMachine(dispenser).Execute(A(balance, withdrawn), operation);

        Assert.Equal(new AtmResult(status, NoteCounts.None, 0, A(balance, withdrawn)), r);
        Assert.Empty(dispenser.Dispensed);
        Assert.Empty(dispenser.Loaded);
        Assert.Equal(stock, dispenser.Stock);
    }

    // ---- Combinaisons calculées à la main ---------------------------------------------------------

    public static TheoryData<int, int, int, Denomination?, int, int, int, int> Combinations => new()
    {
        // stock 50/20/10, bloquée, montant -> billets 50/20/10
        { 1, 4, 0, null, 80, 0, 4, 0 },              // 50 laisse 30, impayable en 20 : il faut 20×4
        { 2, 3, 0, null, 110, 1, 3, 0 },             // 50×2 laisse 10, impayable : 50 + 20×3
        { 1, 5, 0, null, 100, 0, 5, 0 },             // 50 laisse 50, impayable en 20 : 20×5
        { 5, 5, 5, null, 90, 1, 2, 0 },              // 50 + 20×2 (3 billets) plutôt que 50 + 20 + 10×2
        { 5, 5, 5, null, 30, 0, 1, 1 },
        { 5, 5, 5, null, 200, 4, 0, 0 },
        { 0, 0, 5, null, 50, 0, 0, 5 },              // cinq billets de 10 : deux passages
        { 5, 5, 5, Denomination.Fifty, 150, 0, 5, 5 },
        { 5, 5, 5, Denomination.Twenty, 70, 1, 0, 2 },
        { 5, 5, 5, Denomination.Ten, 70, 1, 1, 0 },
        { 5, 5, 5, Denomination.Ten, 60, 0, 3, 0 },
        { 3, 5, 5, null, 200, 3, 2, 1 },
    };

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Withdraw_HandComputedCombination(
        int fifties, int twenties, int tens, Denomination? jammed, int amount, int wf, int wt, int wc)
    {
        var stock = N(fifties, twenties, tens);
        var dispenser = new FaultyDispenser(stock, jammed);
        var r = new CashMachine(dispenser).Execute(A(250, 0), new Operation.Withdraw(amount));

        var fee = amount > 100 ? 5 : 0;
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(wf, wt, wc), fee, A(250 - amount - fee, amount)), r);
        Assert.Equal(stock - N(wf, wt, wc), dispenser.Stock);
        Assert.Equal(N(wf, wt, wc), Sum(dispenser.Dispensed));
        Assert.All(dispenser.Dispensed, p => Assert.InRange(p.Total, 1, MoneyDispenser.MaxNotesPerPass));
    }

    // ---- Machine sans mémoire : réutilisation avec un module dont l'état évolue -------------------

    [Fact]
    public void SameMachine_ReadsCurrentStockOnEveryCall()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, machine.Execute(A(250, 0), new Operation.Withdraw(50)).Status);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);

        // Le stock est vide maintenant : plus rien à sortir.
        Assert.Equal(
            new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, A(200, 50)),
            machine.Execute(A(200, 50), new Operation.Withdraw(50)));

        // Un dépôt remplit la cassette, le retrait suivant l'utilise.
        Assert.Equal(AtmStatus.Ok, machine.Execute(A(200, 50), new Operation.Deposit(N(0, 1, 1))).Status);
        Assert.Equal(
            new AtmResult(AtmStatus.Ok, N(0, 1, 1), 0, A(200, 80)),
            machine.Execute(A(230, 50), new Operation.Withdraw(30)));

        // Une cassette pleine après dépôts refuse le suivant.
        var full = new CashMachine(new MoneyDispenser(N(0, 0, 4)));
        Assert.Equal(AtmStatus.Ok, full.Execute(A(0, 0), new Operation.Deposit(N(0, 0, 1))).Status);
        Assert.Equal(AtmStatus.CassetteFull, full.Execute(A(0, 0), new Operation.Deposit(N(0, 0, 1))).Status);
    }

    [Theory]
    [MemberData(nameof(Jams))]
    public void SameMachine_LongRandomSequence_MatchesOracleAtEveryStep(Denomination? jammed)
    {
        var random = new Random(20261106 + ((int?)jammed ?? 0));

        for (var run = 0; run < 200; run++)
        {
            var dispenser = new MoneyDispenser(N(random.Next(6), random.Next(6), random.Next(6)), jammed);
            var machine = new CashMachine(dispenser);
            var account = A(Balances[random.Next(Balances.Length)], WithdrawnValues[random.Next(WithdrawnValues.Length)]);

            for (var step = 0; step < 50; step++)
            {
                var operation = Operations[random.Next(Operations.Length)];
                var stock = dispenser.Stock;
                var expected = Oracle.Expected(account, stock, jammed, operation);
                var actual = machine.Execute(account, operation);

                Assert.Equal(expected, actual);
                var expectedStock = actual.Status != AtmStatus.Ok ? stock
                    : operation is Operation.Deposit(var d) ? stock + d
                    : stock - expected.Notes;
                Assert.Equal(expectedStock, dispenser.Stock);

                account = actual.Account;
                if (account.Balance > 250 || random.Next(5) == 0)
                {
                    // Reste dans le domaine : on change de compte de temps en temps.
                    account = A(Balances[random.Next(Balances.Length)], WithdrawnValues[random.Next(WithdrawnValues.Length)]);
                }
            }
        }
    }
}
