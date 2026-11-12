namespace Atm.Tests;

/// <summary>
/// Tests d'intégration : <see cref="CashMachine"/> travaille avec le vrai <see cref="MoneyDispenser"/>, sans doublure.
/// Après chaque opération, on vérifie le résultat et le stock du module.
/// </summary>
public class Integration
{
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly int[] Balances = Enumerable.Range(0, 51).Select(i => i * 5).ToArray();

    private static readonly int[] Withdrawns = Enumerable.Range(0, 21).Select(i => i * 10).ToArray();

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
            for (var t = 0; t <= 5; t++)
                for (var c = 0; c <= 5; c++)
                    yield return new NoteCounts(f, t, c);
    }

    private static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var mask = 0; mask < 8; mask++)
            yield return new NoteCounts(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
    }

    private static Denomination? Jam(int jammed) => jammed == 0 ? null : (Denomination)jammed;

    // ---------- Modèle de référence, indépendant de l'implémentation ----------

    private static NoteCounts Usable(NoteCounts stock, Denomination? jam) => new(
        jam == Denomination.Fifty ? 0 : stock.Fifties,
        jam == Denomination.Twenty ? 0 : stock.Twenties,
        jam == Denomination.Ten ? 0 : stock.Tens);

    /// <summary>La combinaison exacte la plus courte : on essaie 0 billet, puis 1, puis 2…</summary>
    private static NoteCounts? FewestNotes(int amount, NoteCounts usable)
    {
        for (var total = 0; total <= usable.Total; total++)
        {
            var found = new List<NoteCounts>();
            for (var f = 0; f <= Math.Min(total, usable.Fifties); f++)
                for (var t = 0; t <= Math.Min(total - f, usable.Twenties); t++)
                {
                    var c = total - f - t;
                    if (c <= usable.Tens && f * 50 + t * 20 + c * 10 == amount)
                    {
                        found.Add(new NoteCounts(f, t, c));
                    }
                }

            if (found.Count > 0)
            {
                Assert.Single(found); // unicité annoncée par la spécification
                return found[0];
            }
        }

        return null;
    }

    private static AtmResult ExpectedWithdraw(AccountState a, NoteCounts? combination, int amount)
    {
        AtmResult Refused(AtmStatus s) => new(s, NoteCounts.None, 0, a);

        if (amount <= 0 || amount % 10 != 0) return Refused(AtmStatus.InvalidAmount);
        if (a.WithdrawnToday + amount > 200) return Refused(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > a.Balance) return Refused(AtmStatus.InsufficientFunds);
        if (combination is not { } notes) return Refused(AtmStatus.CannotDispense);

        return new AtmResult(AtmStatus.Ok, notes, fee, new AccountState(a.Balance - amount - fee, a.WithdrawnToday + amount));
    }

    private static AtmResult ExpectedWithdraw(AccountState a, NoteCounts stock, Denomination? jam, int amount) =>
        ExpectedWithdraw(a, amount > 0 && amount % 10 == 0 ? FewestNotes(amount, Usable(stock, jam)) : null, amount);

    private static AtmResult ExpectedDeposit(AccountState a, NoteCounts stock, Denomination? jam, NoteCounts notes)
    {
        AtmResult Refused(AtmStatus s) => new(s, NoteCounts.None, 0, a);

        if (notes.Total == 0) return Refused(AtmStatus.InvalidAmount);
        if (jam is { } j && notes.Of(j) > 0) return Refused(AtmStatus.CassetteJammed);
        if (stock.Fifties + notes.Fifties > 5 || stock.Twenties + notes.Twenties > 5 || stock.Tens + notes.Tens > 5)
            return Refused(AtmStatus.CassetteFull);

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, a with { Balance = a.Balance + notes.Value });
    }

    /// <summary>Le stock attendu du module après une opération, d'après le résultat attendu.</summary>
    private static NoteCounts ExpectedStock(NoteCounts before, Operation operation, AtmResult expected) =>
        expected.Status != AtmStatus.Ok ? before
        : operation is Operation.Deposit d ? before + d.Notes
        : before - expected.Notes;

    private static void AssertOperation(
        AtmResult expected, NoteCounts expectedStock, AtmResult actual, MoneyDispenser dispenser, Denomination? jam)
    {
        Assert.NotEqual(AtmStatus.DispenserFault, actual.Status); // le vrai module ne tombe jamais en panne dans le domaine
        Assert.Equal(expected, actual);
        Assert.Equal(expectedStock, dispenser.Stock);
        Assert.Equal(jam, dispenser.Jammed);
    }

    // ---------- Exemples de la spécification, de bout en bout ----------

    public static TheoryData<int, int, int, int, int, int, Operation, AtmResult, NoteCounts> SpecExamples() => new()
    {
        // solde, retiré, stock 50/20/10, bloquée, opération, résultat attendu, stock attendu
        { 100, 0, 1, 3, 0, 0, new Operation.Withdraw(60),
            new(AtmStatus.Ok, new(0, 3, 0), 0, new(40, 60)), new(1, 0, 0) },
        { 100, 0, 1, 1, 1, 0, new Operation.Withdraw(80),
            new(AtmStatus.Ok, new(1, 1, 1), 0, new(20, 80)), new(0, 0, 0) },
        { 105, 0, 5, 5, 5, 0, new Operation.Withdraw(100),
            new(AtmStatus.Ok, new(2, 0, 0), 0, new(5, 100)), new(3, 5, 5) },
        { 115, 0, 5, 5, 5, 0, new Operation.Withdraw(110),
            new(AtmStatus.Ok, new(2, 0, 1), 5, new(0, 110)), new(3, 5, 4) },
        { 110, 0, 5, 5, 5, 0, new Operation.Withdraw(110),
            new(AtmStatus.InsufficientFunds, default, 0, new(110, 0)), new(5, 5, 5) },
        { 250, 150, 5, 5, 5, 0, new Operation.Withdraw(60),
            new(AtmStatus.DailyLimitExceeded, default, 0, new(250, 150)), new(5, 5, 5) },
        { 250, 0, 1, 0, 0, 50, new Operation.Withdraw(50),
            new(AtmStatus.CannotDispense, default, 0, new(250, 0)), new(1, 0, 0) },
        { 250, 0, 2, 2, 0, 50, new Operation.Withdraw(40),
            new(AtmStatus.Ok, new(0, 2, 0), 0, new(210, 40)), new(2, 0, 0) },
        { 0, 200, 0, 0, 0, 0, new Operation.Withdraw(5),
            new(AtmStatus.InvalidAmount, default, 0, new(0, 200)), new(0, 0, 0) },
        { 10, 0, 0, 5, 0, 0, new Operation.Deposit(new(0, 1, 0)),
            new(AtmStatus.CassetteFull, default, 0, new(10, 0)), new(0, 5, 0) },
        { 10, 30, 0, 1, 0, 20, new Operation.Deposit(new(0, 1, 0)),
            new(AtmStatus.CassetteJammed, default, 0, new(10, 30)), new(0, 1, 0) },
        { 10, 30, 1, 1, 0, 0, new Operation.Deposit(new(1, 0, 1)),
            new(AtmStatus.Ok, default, 0, new(70, 30)), new(2, 1, 1) },
    };

    [Theory]
    [MemberData(nameof(SpecExamples))]
    public void Spec_examples_with_the_real_dispenser(
        int balance, int withdrawn, int f, int t, int c, int jammed, Operation operation,
        AtmResult expected, NoteCounts expectedStock)
    {
        var dispenser = new MoneyDispenser(new NoteCounts(f, t, c), Jam(jammed));

        var actual = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), operation);

        AssertOperation(expected, expectedStock, actual, dispenser, Jam(jammed));
    }

    // ---------- Parcours du domaine ----------

    [Fact]
    public void Withdraw_over_every_stock_jam_and_amount()
    {
        // Comptes choisis aux frontières du plafond, des frais et du solde.
        AccountState[] accounts =
        [
            new(250, 0), new(250, 200), new(250, 190), new(250, 100), new(0, 0), new(5, 0), new(10, 0),
            new(100, 0), new(105, 0), new(110, 0), new(115, 0), new(205, 0), new(200, 10), new(55, 70),
        ];

        foreach (var stock in AllStocks())
        foreach (var jam in Jams)
        foreach (var amount in Amounts)
        foreach (var account in accounts)
        {
            var dispenser = new MoneyDispenser(stock, jam);
            var operation = new Operation.Withdraw(amount);
            var expected = ExpectedWithdraw(account, stock, jam, amount);

            var actual = new CashMachine(dispenser).Execute(account, operation);

            AssertOperation(expected, ExpectedStock(stock, operation, expected), actual, dispenser, jam);
        }
    }

    [Fact]
    public void Withdraw_over_every_account_and_amount()
    {
        // Tous les comptes du domaine, sur des stocks qui couvrent succès, pénurie et cassette bloquée.
        (NoteCounts Stock, Denomination? Jam)[] modules =
        [
            (new(5, 5, 5), null), (new(1, 5, 5), null), (new(0, 0, 0), null), (new(5, 5, 0), null),
            (new(5, 5, 5), Denomination.Fifty), (new(2, 3, 1), Denomination.Twenty), (new(4, 1, 5), Denomination.Ten),
        ];

        foreach (var (stock, jam) in modules)
        foreach (var amount in Amounts)
        {
            var combination = amount > 0 && amount % 10 == 0 ? FewestNotes(amount, Usable(stock, jam)) : null;
            foreach (var balance in Balances)
            foreach (var withdrawn in Withdrawns)
            {
                var account = new AccountState(balance, withdrawn);
                var dispenser = new MoneyDispenser(stock, jam);
                var operation = new Operation.Withdraw(amount);
                var expected = ExpectedWithdraw(account, combination, amount);

                var actual = new CashMachine(dispenser).Execute(account, operation);

                AssertOperation(expected, ExpectedStock(stock, operation, expected), actual, dispenser, jam);
            }
        }
    }

    [Fact]
    public void Deposit_over_every_stock_jam_and_deposit()
    {
        AccountState[] accounts = [new(0, 0), new(250, 200), new(55, 70), new(5, 10)];

        foreach (var stock in AllStocks())
        foreach (var jam in Jams)
        foreach (var notes in AllDeposits())
        foreach (var account in accounts)
        {
            var dispenser = new MoneyDispenser(stock, jam);
            var operation = new Operation.Deposit(notes);
            var expected = ExpectedDeposit(account, stock, jam, notes);

            var actual = new CashMachine(dispenser).Execute(account, operation);

            AssertOperation(expected, ExpectedStock(stock, operation, expected), actual, dispenser, jam);
        }
    }

    [Fact]
    public void Deposit_over_every_account()
    {
        foreach (var notes in AllDeposits())
        foreach (var balance in Balances)
        foreach (var withdrawn in Withdrawns)
        {
            var stock = new NoteCounts(4, 4, 4);
            var account = new AccountState(balance, withdrawn);
            var dispenser = new MoneyDispenser(stock);
            var operation = new Operation.Deposit(notes);
            var expected = ExpectedDeposit(account, stock, null, notes);

            var actual = new CashMachine(dispenser).Execute(account, operation);

            AssertOperation(expected, ExpectedStock(stock, operation, expected), actual, dispenser, null);
        }
    }

    // ---------- Sorties en plusieurs passages avec le vrai module ----------

    [Theory]
    // stock 50/20/10, montant, billets attendus 50/20/10
    [InlineData(1, 5, 5, 200, 1, 5, 5)]   // 11 billets : au moins trois passages
    [InlineData(0, 5, 5, 150, 0, 5, 5)]   // 10 billets
    [InlineData(0, 5, 0, 100, 0, 5, 0)]   // 5 billets d'une seule coupure
    [InlineData(0, 0, 5, 50, 0, 0, 5)]
    [InlineData(4, 0, 0, 200, 4, 0, 0)]   // exactement 4 billets : un seul passage plein
    [InlineData(3, 2, 0, 190, 3, 2, 0)]
    public void Large_withdrawals_go_through_the_real_dispenser(int f, int t, int c, int amount, int ef, int et, int ec)
    {
        var stock = new NoteCounts(f, t, c);
        var dispenser = new MoneyDispenser(stock);
        var notes = new NoteCounts(ef, et, ec);

        var actual = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));

        var fee = amount > 100 ? 5 : 0;
        var expected = new AtmResult(AtmStatus.Ok, notes, fee, new AccountState(250 - amount - fee, amount));
        AssertOperation(expected, stock - notes, actual, dispenser, null);
    }

    // ---------- Suites d'opérations sur le même module ----------

    [Fact]
    public void A_day_at_the_machine()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(1, 2, 1), Denomination.Ten);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(150, 0);

        void Step(Operation operation, AtmResult expected, NoteCounts expectedStock)
        {
            var actual = machine.Execute(account, operation);
            AssertOperation(expected, expectedStock, actual, dispenser, Denomination.Ten);
            account = actual.Account;
        }

        // 90 € : la cassette de 10 est bloquée, et 50 + 2×20 = 90.
        Step(new Operation.Withdraw(90), new(AtmStatus.Ok, new(1, 2, 0), 0, new(60, 90)), new(0, 0, 1));
        // Plus rien d'utilisable : le billet de 10 reste coincé.
        Step(new Operation.Withdraw(10), new(AtmStatus.CannotDispense, default, 0, new(60, 90)), new(0, 0, 1));
        // Un dépôt qui touche la cassette bloquée est refusé en entier.
        Step(new Operation.Deposit(new(1, 1, 1)), new(AtmStatus.CassetteJammed, default, 0, new(60, 90)), new(0, 0, 1));
        // Sans le billet de 10, il passe et remplit le module.
        Step(new Operation.Deposit(new(1, 1, 0)), new(AtmStatus.Ok, default, 0, new(130, 90)), new(1, 1, 1));
        Step(new Operation.Deposit(new(0, 1, 0)), new(AtmStatus.Ok, default, 0, new(150, 90)), new(1, 2, 1));
        // Les billets déposés ressortent aussitôt.
        Step(new Operation.Withdraw(70), new(AtmStatus.Ok, new(1, 1, 0), 0, new(80, 160)), new(0, 1, 1));
        // 40 € : le plafond (160 + 40 = 200) passe, mais il ne reste qu'un 20 utilisable.
        Step(new Operation.Withdraw(40), new(AtmStatus.CannotDispense, default, 0, new(80, 160)), new(0, 1, 1));
        Step(new Operation.Withdraw(20), new(AtmStatus.Ok, new(0, 1, 0), 0, new(60, 180)), new(0, 0, 1));
        // Le plafond du jour est désormais le premier obstacle.
        Step(new Operation.Deposit(new(1, 1, 0)), new(AtmStatus.Ok, default, 0, new(130, 180)), new(1, 1, 1));
        Step(new Operation.Withdraw(50), new(AtmStatus.DailyLimitExceeded, default, 0, new(130, 180)), new(1, 1, 1));
        Step(new Operation.Withdraw(20), new(AtmStatus.Ok, new(0, 1, 0), 0, new(110, 200)), new(1, 0, 1));
        Step(new Operation.Withdraw(10), new(AtmStatus.DailyLimitExceeded, default, 0, new(110, 200)), new(1, 0, 1));
    }

    [Fact]
    public void Repeated_withdrawals_drain_the_cassettes_one_after_another()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(2, 0, 5));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        (AtmResult, NoteCounts)[] expectedSteps =
        [
            (new(AtmStatus.Ok, new(1, 0, 0), 0, new(200, 50)), new(1, 0, 5)),
            (new(AtmStatus.Ok, new(1, 0, 0), 0, new(150, 100)), new(0, 0, 5)),
            (new(AtmStatus.Ok, new(0, 0, 5), 0, new(100, 150)), new(0, 0, 0)), // les 50 épuisés : 5×10
            (new(AtmStatus.CannotDispense, default, 0, new(100, 150)), new(0, 0, 0)),
        ];

        foreach (var (expected, expectedStock) in expectedSteps)
        {
            var actual = machine.Execute(account, new Operation.Withdraw(50));
            AssertOperation(expected, expectedStock, actual, dispenser, null);
            account = actual.Account;
        }
    }

    [Fact]
    public void Withdrawing_then_depositing_the_same_notes_restores_the_stock()
    {
        var stock = new NoteCounts(3, 2, 4);
        var dispenser = new MoneyDispenser(stock);
        var machine = new CashMachine(dispenser);

        var withdrawal = machine.Execute(new AccountState(200, 0), new Operation.Withdraw(80));
        AssertOperation(new(AtmStatus.Ok, new(1, 1, 1), 0, new(120, 80)), new(2, 1, 3), withdrawal, dispenser, null);

        var deposit = machine.Execute(withdrawal.Account, new Operation.Deposit(withdrawal.Notes));
        AssertOperation(new(AtmStatus.Ok, default, 0, new(200, 80)), stock, deposit, dispenser, null);
    }

    [Fact]
    public void The_machine_reads_the_module_afresh_at_each_call()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(0, 0, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        AssertOperation(new(AtmStatus.CannotDispense, default, 0, account), new(0, 0, 0),
            machine.Execute(account, new Operation.Withdraw(50)), dispenser, null);

        // Le module est rechargé directement, hors de la machine.
        Assert.True(dispenser.Load(new NoteCounts(1, 0, 0)));
        AssertOperation(new(AtmStatus.Ok, new(1, 0, 0), 0, new(200, 50)), new(0, 0, 0),
            machine.Execute(account, new Operation.Withdraw(50)), dispenser, null);

        // Le module est rempli directement : le dépôt suivant ne tient plus.
        Assert.True(dispenser.Load(new NoteCounts(5, 0, 0)));
        AssertOperation(new(AtmStatus.CassetteFull, default, 0, account), new(5, 0, 0),
            machine.Execute(account, new Operation.Deposit(new(1, 0, 0))), dispenser, null);

        // Le module est vidé directement : le dépôt passe.
        Assert.True(dispenser.Dispense(new NoteCounts(1, 0, 0)));
        AssertOperation(new(AtmStatus.Ok, default, 0, new(300, 0)), new(5, 0, 0),
            machine.Execute(account, new Operation.Deposit(new(1, 0, 0))), dispenser, null);
    }

    [Fact]
    public void Two_machines_sharing_one_module_see_each_other()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(1, 0, 0));
        var first = new CashMachine(dispenser);
        var second = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        AssertOperation(new(AtmStatus.Ok, new(1, 0, 0), 0, new(200, 50)), new(0, 0, 0),
            first.Execute(account, new Operation.Withdraw(50)), dispenser, null);
        AssertOperation(new(AtmStatus.CannotDispense, default, 0, account), new(0, 0, 0),
            second.Execute(account, new Operation.Withdraw(50)), dispenser, null);
        AssertOperation(new(AtmStatus.Ok, default, 0, new(300, 0)), new(1, 0, 0),
            second.Execute(account, new Operation.Deposit(new(1, 0, 0))), dispenser, null);
        AssertOperation(new(AtmStatus.Ok, new(1, 0, 0), 0, new(200, 50)), new(0, 0, 0),
            first.Execute(account, new Operation.Withdraw(50)), dispenser, null);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Random_sequences_on_one_module_match_the_model(int seed)
    {
        var random = new Random(seed);

        for (var run = 0; run < 50; run++)
        {
            var jam = Jams[random.Next(Jams.Length)];
            var stock = new NoteCounts(random.Next(6), random.Next(6), random.Next(6));
            var dispenser = new MoneyDispenser(stock, jam);
            var machine = new CashMachine(dispenser);
            var account = new AccountState(Balances[random.Next(Balances.Length)], Withdrawns[random.Next(Withdrawns.Length)]);

            for (var step = 0; step < 40; step++)
            {
                Operation operation = random.Next(3) == 0
                    ? new Operation.Deposit(AllDeposits().ElementAt(random.Next(8)))
                    : new Operation.Withdraw(Amounts[random.Next(Amounts.Length)]);

                var expected = operation switch
                {
                    Operation.Withdraw w => ExpectedWithdraw(account, stock, jam, w.Amount),
                    Operation.Deposit d => ExpectedDeposit(account, stock, jam, d.Notes),
                    _ => throw new InvalidOperationException(),
                };
                var expectedStock = ExpectedStock(stock, operation, expected);

                var actual = machine.Execute(account, operation);

                AssertOperation(expected, expectedStock, actual, dispenser, jam);
                if (jam is { } j)
                {
                    Assert.Equal(stock.Of(j), dispenser.Stock.Of(j)); // la cassette bloquée ne bouge jamais
                }

                stock = expectedStock;
                account = actual.Account;

                // On reste dans le domaine : au-delà, le comportement est libre.
                if (account.Balance > 250)
                {
                    account = account with { Balance = 250 };
                }
                if (account.WithdrawnToday == 200 && random.Next(4) == 0)
                {
                    account = account with { WithdrawnToday = 0 }; // un nouveau jour
                }
            }
        }
    }
}
