namespace Atm.Tests;

/// <summary>
/// Tests d'intégration : <see cref="CashMachine"/> travaille avec le vrai <see cref="MoneyDispenser"/>, sans doublure.
/// Après chaque opération, on vérifie le résultat rendu et le stock réellement présent dans le module.
/// </summary>
public class Integration
{
    private static readonly NoteCounts Full = new(5, 5, 5);

    private static readonly NoteCounts Empty = new(0, 0, 0);

    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly NoteCounts[] Deposits =
        Enumerable.Range(0, 8).Select(m => new NoteCounts(m & 1, (m >> 1) & 1, (m >> 2) & 1)).ToArray();

    // ------------------------------------------------------------------
    // Outils
    // ------------------------------------------------------------------

    private static IEnumerable<NoteCounts> Stocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
        {
            yield return new NoteCounts(f, t, n);
        }
    }

    private static IEnumerable<AccountState> Accounts()
    {
        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        {
            yield return new AccountState(balance, withdrawn);
        }
    }

    private static bool InDomain(AccountState a) =>
        a.Balance is >= 0 and <= 250 && a.Balance % 5 == 0 && a.WithdrawnToday is >= 0 and <= 200 && a.WithdrawnToday % 10 == 0;

    private static AtmResult Refused(AtmStatus status, AccountState account) => new(status, NoteCounts.None, 0, account);

    /// <summary>Oracle indépendant de l'implémentation : recherche exhaustive de la combinaison.</summary>
    private static AtmResult ExpectedWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        if (amount <= 0 || amount % 10 != 0) return Refused(AtmStatus.InvalidAmount, account);
        if (account.WithdrawnToday + amount > 200) return Refused(AtmStatus.DailyLimitExceeded, account);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return Refused(AtmStatus.InsufficientFunds, account);

        var usable = new NoteCounts(
            jammed == Denomination.Fifty ? 0 : stock.Fifties,
            jammed == Denomination.Twenty ? 0 : stock.Twenties,
            jammed == Denomination.Ten ? 0 : stock.Tens);

        var candidates = new List<NoteCounts>();
        for (var f = 0; f <= usable.Fifties; f++)
        for (var t = 0; t <= usable.Twenties; t++)
        for (var n = 0; n <= usable.Tens; n++)
        {
            var c = new NoteCounts(f, t, n);
            if (c.Value == amount) candidates.Add(c);
        }

        if (candidates.Count == 0) return Refused(AtmStatus.CannotDispense, account);

        var fewest = candidates.Min(c => c.Total);
        var best = Assert.Single(candidates, c => c.Total == fewest);

        return new AtmResult(AtmStatus.Ok, best, fee, new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    private static AtmResult ExpectedDeposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        if (notes.Total == 0) return Refused(AtmStatus.InvalidAmount, account);
        if (jammed is { } j && notes.Of(j) > 0) return Refused(AtmStatus.CassetteJammed, account);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refused(AtmStatus.CassetteFull, account);

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value });
    }

    private static AtmResult Expected(AccountState account, NoteCounts stock, Denomination? jammed, Operation op) => op switch
    {
        Operation.Withdraw w => ExpectedWithdraw(account, stock, jammed, w.Amount),
        Operation.Deposit d => ExpectedDeposit(account, stock, jammed, d.Notes),
        _ => throw new ArgumentException("Opération inconnue.", nameof(op)),
    };

    /// <summary>Le stock attendu dans le module après un résultat réussi (rien ne bouge sinon).</summary>
    private static NoteCounts ExpectedStock(NoteCounts before, Operation op, AtmResult expected) =>
        expected.Status != AtmStatus.Ok ? before
        : op is Operation.Deposit d ? before + d.Notes
        : before - expected.Notes;

    /// <summary>Exécute une opération avec la machine et le module donnés, et vérifie résultat et stock.</summary>
    private static AtmResult Check(CashMachine machine, MoneyDispenser dispenser, AccountState account, Operation op)
    {
        var before = dispenser.Stock;
        var jammed = dispenser.Jammed;
        var expected = Expected(account, before, jammed, op);

        var actual = machine.Execute(account, op);

        Assert.Equal(expected, actual);
        Assert.NotEqual(AtmStatus.DispenserFault, actual.Status);
        Assert.Equal(ExpectedStock(before, op, expected), dispenser.Stock);
        Assert.Equal(jammed, dispenser.Jammed);

        return actual;
    }

    private static AtmResult CheckFresh(AccountState account, NoteCounts stock, Denomination? jammed, Operation op)
    {
        var dispenser = new MoneyDispenser(stock, jammed);

        return Check(new CashMachine(dispenser), dispenser, account, op);
    }

    // ------------------------------------------------------------------
    // Exemples de la spécification, avec le vrai module
    // ------------------------------------------------------------------

    public static TheoryData<int, int, NoteCounts, Denomination?, int, AtmStatus, NoteCounts, int, int, int> WithdrawExamples => new()
    {
        { 100, 0, new(1, 3, 0), null, 60, AtmStatus.Ok, new(0, 3, 0), 0, 40, 60 },
        { 100, 0, new(1, 1, 1), null, 80, AtmStatus.Ok, new(1, 1, 1), 0, 20, 80 },
        { 105, 0, Full, null, 100, AtmStatus.Ok, new(2, 0, 0), 0, 5, 100 },
        { 115, 0, Full, null, 110, AtmStatus.Ok, new(2, 0, 1), 5, 0, 110 },
        { 110, 0, Full, null, 110, AtmStatus.InsufficientFunds, Empty, 0, 110, 0 },
        { 250, 150, Full, null, 60, AtmStatus.DailyLimitExceeded, Empty, 0, 250, 150 },
        { 250, 0, new(1, 0, 0), Denomination.Fifty, 50, AtmStatus.CannotDispense, Empty, 0, 250, 0 },
        { 250, 0, new(2, 2, 0), Denomination.Fifty, 40, AtmStatus.Ok, new(0, 2, 0), 0, 210, 40 },
        { 0, 200, Empty, null, 5, AtmStatus.InvalidAmount, Empty, 0, 0, 200 },
    };

    [Theory]
    [MemberData(nameof(WithdrawExamples))]
    public void Spec_withdraw_examples_with_real_dispenser(
        int balance, int withdrawn, NoteCounts stock, Denomination? jammed, int amount,
        AtmStatus status, NoteCounts notes, int fee, int balanceAfter, int withdrawnAfter)
    {
        var dispenser = new MoneyDispenser(stock, jammed);

        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));

        Assert.Equal(new AtmResult(status, notes, fee, new AccountState(balanceAfter, withdrawnAfter)), result);
        Assert.Equal(stock - notes, dispenser.Stock);
    }

    public static TheoryData<int, int, NoteCounts, Denomination?, NoteCounts, AtmStatus, NoteCounts, int> DepositExamples => new()
    {
        { 10, 0, new(0, 5, 0), null, new(0, 1, 0), AtmStatus.CassetteFull, new(0, 5, 0), 10 },
        { 10, 30, new(0, 1, 0), Denomination.Twenty, new(0, 1, 0), AtmStatus.CassetteJammed, new(0, 1, 0), 10 },
        { 10, 30, new(1, 1, 0), null, new(1, 0, 1), AtmStatus.Ok, new(2, 1, 1), 70 },
    };

    [Theory]
    [MemberData(nameof(DepositExamples))]
    public void Spec_deposit_examples_with_real_dispenser(
        int balance, int withdrawn, NoteCounts stock, Denomination? jammed, NoteCounts deposit,
        AtmStatus status, NoteCounts stockAfter, int balanceAfter)
    {
        var dispenser = new MoneyDispenser(stock, jammed);

        var result = new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Deposit(deposit));

        Assert.Equal(new AtmResult(status, NoteCounts.None, 0, new AccountState(balanceAfter, withdrawn)), result);
        Assert.Equal(stockAfter, dispenser.Stock);
    }

    // ------------------------------------------------------------------
    // Balayage du domaine, une opération par module neuf
    // ------------------------------------------------------------------

    [Fact]
    public void Withdraw_over_every_stock_jam_and_amount()
    {
        AccountState[] accounts = [new(250, 0), new(105, 0), new(250, 100), new(55, 150), new(0, 0)];
        foreach (var account in accounts)
        foreach (var stock in Stocks())
        foreach (var jammed in Jams)
        foreach (var amount in Amounts)
        {
            CheckFresh(account, stock, jammed, new Operation.Withdraw(amount));
        }
    }

    [Fact]
    public void Withdraw_over_every_account_and_amount()
    {
        (NoteCounts Stock, Denomination? Jam)[] modules = [(Full, null), (new(2, 1, 4), Denomination.Fifty), (new(4, 3, 0), Denomination.Ten), (new(1, 0, 2), null)];
        foreach (var account in Accounts())
        foreach (var (stock, jammed) in modules)
        foreach (var amount in Amounts)
        {
            CheckFresh(account, stock, jammed, new Operation.Withdraw(amount));
        }
    }

    [Fact]
    public void Deposit_over_every_stock_jam_and_deposit()
    {
        AccountState[] accounts = [new(0, 0), new(35, 70), new(250, 200)];
        foreach (var account in accounts)
        foreach (var stock in Stocks())
        foreach (var jammed in Jams)
        foreach (var notes in Deposits)
        {
            CheckFresh(account, stock, jammed, new Operation.Deposit(notes));
        }
    }

    [Fact]
    public void Deposit_over_every_account()
    {
        foreach (var account in Accounts())
        foreach (var notes in Deposits)
        {
            CheckFresh(account, new(2, 3, 4), null, new Operation.Deposit(notes));
            CheckFresh(account, new(5, 0, 4), Denomination.Twenty, new Operation.Deposit(notes));
        }
    }

    // ------------------------------------------------------------------
    // Enchaînements : une même machine, un même module, plusieurs opérations
    // ------------------------------------------------------------------

    public static TheoryData<Denomination?, int> Walks()
    {
        var data = new TheoryData<Denomination?, int>();
        foreach (var jammed in Jams)
        foreach (var seed in new[] { 1, 2, 3, 4, 5 })
        {
            data.Add(jammed, seed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Walks))]
    public void Random_sequences_of_operations_keep_module_and_results_consistent(Denomination? jammed, int seed)
    {
        var random = new Random(seed);
        var dispenser = new MoneyDispenser(new NoteCounts(random.Next(6), random.Next(6), random.Next(6)), jammed);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        var outcomes = new HashSet<AtmStatus>();
        for (var step = 0; step < 2000; step++)
        {
            Operation op = random.Next(2) == 0
                ? new Operation.Withdraw(Amounts[random.Next(Amounts.Length)])
                : new Operation.Deposit(Deposits[random.Next(Deposits.Length)]);

            var result = Check(machine, dispenser, account, op);
            outcomes.Add(result.Status);

            // Le compte rendu sert à l'opération suivante ; on le renouvelle s'il sort du domaine ou de temps en temps.
            account = result.Account;
            if (!InDomain(account) || random.Next(10) == 0)
            {
                account = new AccountState(random.Next(51) * 5, random.Next(21) * 10);
            }
        }

        // La marche doit avoir réellement exercé les chemins principaux.
        Assert.Contains(AtmStatus.Ok, outcomes);
        Assert.Contains(AtmStatus.CassetteFull, outcomes);
        if (jammed is not null)
        {
            Assert.Contains(AtmStatus.CannotDispense, outcomes);
            Assert.Contains(AtmStatus.CassetteJammed, outcomes);
        }
    }

    [Fact]
    public void Successive_withdrawals_empty_the_module_then_cannot_dispense()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(2, 1, 1));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(100));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 0), 0, new(150, 100)), first);
        Assert.Equal(new NoteCounts(0, 1, 1), dispenser.Stock);

        // Il n'y a plus de 50 : 50 € n'est plus faisable, même si le compte et le plafond le permettent.
        var second = machine.Execute(first.Account, new Operation.Withdraw(50));
        Assert.Equal(Refused(AtmStatus.CannotDispense, first.Account), second);
        Assert.Equal(new NoteCounts(0, 1, 1), dispenser.Stock);

        var third = machine.Execute(first.Account, new Operation.Withdraw(30));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 1, 1), 0, new(120, 130)), third);
        Assert.Equal(Empty, dispenser.Stock);

        var fourth = machine.Execute(third.Account, new Operation.Withdraw(10));
        Assert.Equal(Refused(AtmStatus.CannotDispense, third.Account), fourth);
        Assert.Equal(Empty, dispenser.Stock);
    }

    [Fact]
    public void Deposited_notes_become_available_for_a_later_withdrawal()
    {
        var dispenser = new MoneyDispenser(Empty);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(0, 0);

        Assert.Equal(Refused(AtmStatus.InsufficientFunds, account), machine.Execute(account, new Operation.Withdraw(80)));

        var deposit = machine.Execute(account, new Operation.Deposit(new(1, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(80, 0)), deposit);
        Assert.Equal(new NoteCounts(1, 1, 1), dispenser.Stock);

        var withdraw = machine.Execute(deposit.Account, new Operation.Withdraw(80));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 1, 1), 0, new(0, 80)), withdraw);
        Assert.Equal(Empty, dispenser.Stock);
    }

    [Fact]
    public void Withdrawal_frees_room_for_a_deposit_that_was_refused_as_full()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(5, 0, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(100, 0);

        Assert.Equal(Refused(AtmStatus.CassetteFull, account), machine.Execute(account, new Operation.Deposit(new(1, 0, 0))));
        Assert.Equal(new NoteCounts(5, 0, 0), dispenser.Stock);

        var withdraw = machine.Execute(account, new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 0), 0, new(50, 50)), withdraw);
        Assert.Equal(new NoteCounts(4, 0, 0), dispenser.Stock);

        var deposit = machine.Execute(withdraw.Account, new Operation.Deposit(new(1, 0, 0)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(100, 50)), deposit);
        Assert.Equal(new NoteCounts(5, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Daily_limit_accumulates_across_withdrawals_and_ignores_deposits()
    {
        var dispenser = new MoneyDispenser(Full);
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(150));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(3, 0, 0), 5, new(95, 150)), first);
        Assert.Equal(new NoteCounts(2, 5, 5), dispenser.Stock);

        // La cassette de 20 est pleine : seul le billet de 50 peut entrer.
        Assert.Equal(Refused(AtmStatus.CassetteFull, first.Account), machine.Execute(first.Account, new Operation.Deposit(new(1, 1, 0))));
        Assert.Equal(new NoteCounts(2, 5, 5), dispenser.Stock);

        var deposit = machine.Execute(first.Account, new Operation.Deposit(new(1, 0, 0)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(145, 150)), deposit);
        Assert.Equal(new NoteCounts(3, 5, 5), dispenser.Stock);

        var tooMuch = machine.Execute(deposit.Account, new Operation.Withdraw(60));
        Assert.Equal(Refused(AtmStatus.DailyLimitExceeded, deposit.Account), tooMuch);
        Assert.Equal(new NoteCounts(3, 5, 5), dispenser.Stock);

        var last = machine.Execute(deposit.Account, new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 0), 0, new(95, 200)), last);
        Assert.Equal(new NoteCounts(2, 5, 5), dispenser.Stock);

        Assert.Equal(Refused(AtmStatus.DailyLimitExceeded, last.Account), machine.Execute(last.Account, new Operation.Withdraw(10)));
        Assert.Equal(new NoteCounts(2, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Jammed_cassette_stays_untouched_across_a_sequence()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(3, 2, 5), Denomination.Fifty);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        // Sans les 50, il reste 20×2 + 10×5 = 90 € au plus : 100 € est impossible.
        var hundred = machine.Execute(account, new Operation.Withdraw(100));
        Assert.Equal(Refused(AtmStatus.CannotDispense, account), hundred);

        var ninety = machine.Execute(account, new Operation.Withdraw(90));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 2, 5), 0, new(160, 90)), ninety);
        Assert.Equal(new NoteCounts(3, 0, 0), dispenser.Stock);

        Assert.Equal(Refused(AtmStatus.CassetteJammed, ninety.Account), machine.Execute(ninety.Account, new Operation.Deposit(new(1, 1, 1))));
        Assert.Equal(new NoteCounts(3, 0, 0), dispenser.Stock);

        var refill = machine.Execute(ninety.Account, new Operation.Deposit(new(0, 1, 1)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(190, 90)), refill);
        Assert.Equal(new NoteCounts(3, 1, 1), dispenser.Stock);

        Assert.Equal(Refused(AtmStatus.CannotDispense, refill.Account), machine.Execute(refill.Account, new Operation.Withdraw(50)));
        Assert.Equal(new NoteCounts(3, 1, 1), dispenser.Stock);
        Assert.Equal(Denomination.Fifty, dispenser.Jammed);
    }

    [Fact]
    public void Refused_operations_leave_the_module_fully_usable()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(1, 1, 1), Denomination.Ten);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        Operation[] refusals =
        [
            new Operation.Withdraw(-10), new Operation.Withdraw(0), new Operation.Withdraw(5), new Operation.Withdraw(210),
            new Operation.Withdraw(10), new Operation.Deposit(NoteCounts.None), new Operation.Deposit(new(0, 0, 1)),
        ];
        foreach (var op in refusals)
        {
            var result = machine.Execute(account, op);
            Assert.NotEqual(AtmStatus.Ok, result.Status);
            Assert.Equal(Refused(result.Status, account), result);
            Assert.Equal(new NoteCounts(1, 1, 1), dispenser.Stock);
        }

        var ok = machine.Execute(account, new Operation.Withdraw(70));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 1, 0), 0, new(180, 70)), ok);
        Assert.Equal(new NoteCounts(0, 0, 1), dispenser.Stock);
    }

    [Fact]
    public void Withdrawal_needing_three_passes_empties_the_cassettes_exactly()
    {
        // 150 € sans billets de 50 : 20×5 + 10×5, dix billets, donc au moins trois passages de 4 au plus.
        var dispenser = new MoneyDispenser(new NoteCounts(4, 5, 5), Denomination.Fifty);
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 5, 5), 5, new(95, 150)), result);
        Assert.Equal(new NoteCounts(4, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Largest_withdrawal_with_small_notes_only()
    {
        // 200 € avec seulement des 20 et des 10 disponibles : 20×5 + 10×5 = 150 < 200, donc impossible.
        var dispenser = new MoneyDispenser(new NoteCounts(5, 5, 5), Denomination.Fifty);
        var account = new AccountState(250, 0);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(200));

        Assert.Equal(Refused(AtmStatus.CannotDispense, account), result);
        Assert.Equal(Full, dispenser.Stock);
    }

    // ------------------------------------------------------------------
    // Absence de mémoire : la machine lit l'état courant du module à chaque appel
    // ------------------------------------------------------------------

    [Fact]
    public void Machine_sees_notes_loaded_directly_into_the_module_after_construction()
    {
        var dispenser = new MoneyDispenser(Empty);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        Assert.Equal(Refused(AtmStatus.CannotDispense, account), machine.Execute(account, new Operation.Withdraw(50)));

        Assert.True(dispenser.Load(new NoteCounts(1, 0, 0)));

        var result = machine.Execute(account, new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 0), 0, new(200, 50)), result);
        Assert.Equal(Empty, dispenser.Stock);
    }

    [Fact]
    public void Machine_sees_notes_dispensed_directly_from_the_module_after_construction()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(0, 5, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        Assert.True(dispenser.Dispense(new NoteCounts(0, 4, 0)));

        Assert.Equal(Refused(AtmStatus.CannotDispense, account), machine.Execute(account, new Operation.Withdraw(40)));
        Assert.Equal(new NoteCounts(0, 1, 0), dispenser.Stock);

        // Et la cassette libérée accepte de nouveau un dépôt.
        var deposit = machine.Execute(account, new Operation.Deposit(new(0, 1, 0)));
        Assert.Equal(AtmStatus.Ok, deposit.Status);
        Assert.Equal(new NoteCounts(0, 2, 0), dispenser.Stock);
    }

    [Fact]
    public void Two_machines_sharing_one_module_see_each_others_operations()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(1, 0, 4));
        var a = new CashMachine(dispenser);
        var b = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 0), 0, new(200, 50)), a.Execute(account, new Operation.Withdraw(50)));
        Assert.Equal(new NoteCounts(0, 0, 4), dispenser.Stock);

        // Sans 50 dans le module, l'autre machine doit passer par les 10.
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 0, 4), 0, new(210, 40)), b.Execute(account, new Operation.Withdraw(40)));
        Assert.Equal(Empty, dispenser.Stock);

        Assert.Equal(AtmStatus.Ok, b.Execute(account, new Operation.Deposit(new(1, 1, 1))).Status);
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 1, 1), 0, new(170, 80)), a.Execute(account, new Operation.Withdraw(80)));
        Assert.Equal(Empty, dispenser.Stock);
    }

    [Fact]
    public void Same_call_on_identical_modules_gives_identical_results()
    {
        foreach (var stock in Stocks())
        foreach (var jammed in Jams)
        {
            var op = new Operation.Withdraw(110);
            var account = new AccountState(200, 50);
            var d1 = new MoneyDispenser(stock, jammed);
            var d2 = new MoneyDispenser(stock, jammed);

            Assert.Equal(new CashMachine(d1).Execute(account, op), new CashMachine(d2).Execute(account, op));
            Assert.Equal(d1.Stock, d2.Stock);
        }
    }
}
