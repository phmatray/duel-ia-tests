namespace Atm.Tests;

/// <summary>
/// Tests de renfort : parcours exhaustifs du dépôt et des comptes, passages vérifiés indépendamment du vrai module,
/// arrêt immédiat sur panne et absence de mémoire entre deux appels.
/// </summary>
public class Renforts
{
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly int[] Balances = Enumerable.Range(0, 51).Select(i => i * 5).ToArray();

    private static readonly int[] Withdrawns = Enumerable.Range(0, 21).Select(i => i * 10).ToArray();

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
        {
            yield return new NoteCounts(f, t, n);
        }
    }

    private static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var f = 0; f <= 1; f++)
        for (var t = 0; t <= 1; t++)
        for (var n = 0; n <= 1; n++)
        {
            yield return new NoteCounts(f, t, n);
        }
    }

    private static int Count(NoteCounts notes, Denomination? denomination) => denomination switch
    {
        Denomination.Fifty => notes.Fifties,
        Denomination.Twenty => notes.Twenties,
        Denomination.Ten => notes.Tens,
        _ => 0,
    };

    // ----- Oracles indépendants -----

    private static (AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account) WithdrawOracle(
        AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var refused = (AtmStatus status) => (status, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return refused(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return refused(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return refused(AtmStatus.InsufficientFunds);

        var maxF = jammed == Denomination.Fifty ? 0 : stock.Fifties;
        var maxT = jammed == Denomination.Twenty ? 0 : stock.Twenties;
        var maxN = jammed == Denomination.Ten ? 0 : stock.Tens;
        NoteCounts? best = null;
        for (var f = 0; f <= maxF; f++)
        for (var t = 0; t <= maxT; t++)
        for (var n = 0; n <= maxN; n++)
        {
            if (50 * f + 20 * t + 10 * n != amount) continue;
            var candidate = new NoteCounts(f, t, n);
            if (best is not { } b || candidate.Total < b.Total) best = candidate;
        }

        if (best is not { } notes) return refused(AtmStatus.CannotDispense);

        return (AtmStatus.Ok, notes, fee, new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));
    }

    private static (AtmStatus Status, AccountState Account, NoteCounts StockAfter) DepositOracle(
        AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts deposit)
    {
        if (deposit.Fifties + deposit.Twenties + deposit.Tens == 0) return (AtmStatus.InvalidAmount, account, stock);
        if (Count(deposit, jammed) > 0) return (AtmStatus.CassetteJammed, account, stock);
        if (stock.Fifties + deposit.Fifties > 5 || stock.Twenties + deposit.Twenties > 5 || stock.Tens + deposit.Tens > 5)
        {
            return (AtmStatus.CassetteFull, account, stock);
        }

        var value = 50 * deposit.Fifties + 20 * deposit.Twenties + 10 * deposit.Tens;
        var after = new NoteCounts(stock.Fifties + deposit.Fifties, stock.Twenties + deposit.Twenties, stock.Tens + deposit.Tens);

        return (AtmStatus.Ok, account with { Balance = account.Balance + value }, after);
    }

    // ----- Vérifications communes -----

    private static void CheckWithdraw(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        var spy = new SpyDispenser(stock, jammed);
        var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(amount));
        var expected = WithdrawOracle(account, stock, jammed, amount);
        var context = $"compte {account}, stock {stock}, bloquée {jammed}, retrait {amount}";

        Assert.True(expected.Status == result.Status, $"{context} : attendu {expected.Status}, obtenu {result.Status}");
        Assert.True(expected.Notes == result.Notes, $"{context} : billets attendus {expected.Notes}, obtenus {result.Notes}");
        Assert.True(expected.Fee == result.Fee, $"{context} : frais attendus {expected.Fee}, obtenus {result.Fee}");
        Assert.True(expected.Account == result.Account, $"{context} : compte attendu {expected.Account}, obtenu {result.Account}");
        Assert.True(stock - expected.Notes == spy.Stock, $"{context} : stock final {spy.Stock}");
        Assert.True(spy.Loads.Count == 0, $"{context} : un retrait ne range aucun billet");

        if (expected.Status != AtmStatus.Ok)
        {
            Assert.True(spy.Attempts.Count == 0, $"{context} : un refus ne sort aucun billet");
            return;
        }

        var sum = NoteCounts.None;
        Assert.True(spy.Attempts.SequenceEqual(spy.Passes), $"{context} : un passage a été refusé par le module");
        foreach (var pass in spy.Attempts)
        {
            Assert.True(pass.Fifties >= 0 && pass.Twenties >= 0 && pass.Tens >= 0, $"{context} : passage {pass} négatif");
            Assert.True(pass.Total is >= 1 and <= 4, $"{context} : passage {pass} hors de 1 à 4 billets");
            Assert.True(Count(pass, jammed) == 0, $"{context} : passage {pass} touche la cassette bloquée");
            sum += pass;
        }

        Assert.True(sum == expected.Notes, $"{context} : les passages totalisent {sum}");
    }

    private static void CheckDeposit(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts deposit)
    {
        var spy = new SpyDispenser(stock, jammed);
        var result = new CashMachine(spy).Execute(account, new Operation.Deposit(deposit));
        var expected = DepositOracle(account, stock, jammed, deposit);
        var context = $"compte {account}, stock {stock}, bloquée {jammed}, dépôt {deposit}";

        Assert.True(expected.Status == result.Status, $"{context} : attendu {expected.Status}, obtenu {result.Status}");
        Assert.True(result.Notes == NoteCounts.None, $"{context} : un dépôt ne rend aucun billet");
        Assert.True(result.Fee == 0, $"{context} : un dépôt ne prélève aucuns frais");
        Assert.True(expected.Account == result.Account, $"{context} : compte attendu {expected.Account}, obtenu {result.Account}");
        Assert.True(expected.StockAfter == spy.Stock, $"{context} : stock attendu {expected.StockAfter}, obtenu {spy.Stock}");
        Assert.True(spy.Attempts.Count == 0, $"{context} : un dépôt ne sort aucun billet");
        Assert.True(spy.Loads.Count == (expected.Status == AtmStatus.Ok ? 1 : 0), $"{context} : {spy.Loads.Count} appel(s) à Load");
    }

    // ----- Retrait : parcours exhaustifs -----

    [Fact]
    public void Withdraw_matches_oracle_over_all_accounts_and_amounts()
    {
        NoteCounts[] stocks = [new(5, 5, 5), new(0, 0, 0), new(1, 3, 0), new(0, 5, 5)];

        foreach (var stock in stocks)
        foreach (var jammed in Jams)
        foreach (var balance in Balances)
        foreach (var withdrawn in Withdrawns)
        foreach (var amount in Amounts)
        {
            CheckWithdraw(new AccountState(balance, withdrawn), stock, jammed, amount);
        }
    }

    [Fact]
    public void Withdraw_matches_oracle_over_all_stocks_jams_and_amounts_with_passes_checked()
    {
        foreach (var stock in AllStocks())
        foreach (var jammed in Jams)
        foreach (var amount in Amounts)
        {
            CheckWithdraw(new AccountState(250, 0), stock, jammed, amount);
            CheckWithdraw(new AccountState(205, 0), stock, jammed, amount);
            CheckWithdraw(new AccountState(250, 90), stock, jammed, amount);
        }
    }

    [Fact]
    public void Withdraw_matches_oracle_on_random_sample_of_whole_domain()
    {
        var random = new Random(20261108);
        var stocks = AllStocks().ToArray();

        for (var i = 0; i < 50_000; i++)
        {
            var account = new AccountState(Balances[random.Next(Balances.Length)], Withdrawns[random.Next(Withdrawns.Length)]);
            CheckWithdraw(account, stocks[random.Next(stocks.Length)], Jams[random.Next(Jams.Length)],
                Amounts[random.Next(Amounts.Length)]);
        }
    }

    // ----- Retrait : frontières explicites -----

    [Theory]
    [InlineData(10, 10, AtmStatus.Ok)]
    [InlineData(10, 5, AtmStatus.InsufficientFunds)]
    [InlineData(100, 100, AtmStatus.Ok)]
    [InlineData(100, 95, AtmStatus.InsufficientFunds)]
    [InlineData(110, 115, AtmStatus.Ok)]
    [InlineData(110, 110, AtmStatus.InsufficientFunds)]
    [InlineData(200, 205, AtmStatus.Ok)]
    [InlineData(200, 200, AtmStatus.InsufficientFunds)]
    public void Balance_must_cover_amount_plus_fee(int amount, int balance, AtmStatus expected)
    {
        var dispenser = new MoneyDispenser(new NoteCounts(5, 5, 5));
        var result = new CashMachine(dispenser).Execute(new AccountState(balance, 0), new Operation.Withdraw(amount));

        Assert.Equal(expected, result.Status);
        if (expected == AtmStatus.Ok)
        {
            Assert.Equal(balance - amount - (amount > 100 ? 5 : 0), result.Account.Balance);
            Assert.Equal(amount, result.Account.WithdrawnToday);
        }
    }

    [Theory]
    [InlineData(190, 10, AtmStatus.Ok)]
    [InlineData(190, 20, AtmStatus.DailyLimitExceeded)]
    [InlineData(200, 10, AtmStatus.DailyLimitExceeded)]
    [InlineData(0, 220, AtmStatus.DailyLimitExceeded)]
    [InlineData(100, 100, AtmStatus.Ok)]
    [InlineData(100, 110, AtmStatus.DailyLimitExceeded)]
    public void Daily_limit_boundaries(int withdrawn, int amount, AtmStatus expected)
    {
        var dispenser = new MoneyDispenser(new NoteCounts(5, 5, 5));
        var result = new CashMachine(dispenser).Execute(new AccountState(250, withdrawn), new Operation.Withdraw(amount));

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(10, 0, 0, 1)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(30, 0, 1, 1)]
    [InlineData(40, 0, 2, 0)]
    [InlineData(50, 1, 0, 0)]
    [InlineData(60, 1, 0, 1)]
    [InlineData(70, 1, 1, 0)]
    [InlineData(80, 1, 1, 1)]
    [InlineData(90, 1, 2, 0)]
    [InlineData(100, 2, 0, 0)]
    [InlineData(130, 2, 1, 1)]
    [InlineData(150, 3, 0, 0)]
    [InlineData(190, 3, 2, 0)]
    [InlineData(200, 4, 0, 0)]
    public void Full_stock_gives_fewest_notes(int amount, int fifties, int twenties, int tens)
    {
        var dispenser = new MoneyDispenser(new NoteCounts(5, 5, 5));
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(fifties, twenties, tens), result.Notes);
        Assert.Equal(new NoteCounts(5 - fifties, 5 - twenties, 5 - tens), dispenser.Stock);
    }

    [Fact]
    public void Fewest_notes_prefers_twenties_over_tens_when_fifties_jammed()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(5, 5, 5), Denomination.Fifty);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(100));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(0, 5, 0), result.Notes);
        Assert.Equal(new NoteCounts(5, 0, 5), dispenser.Stock);
    }

    [Fact]
    public void Biggest_withdrawal_needs_three_passes_of_at_most_four_notes()
    {
        // 200 € = 50×1 + 20×5 + 10×5 : 11 billets, la seule combinaison possible.
        var spy = new SpyDispenser(new NoteCounts(1, 5, 5), null);
        var result = new CashMachine(spy).Execute(new AccountState(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new NoteCounts(1, 5, 5), result.Notes);
        Assert.Equal(5, result.Fee);
        Assert.Equal(new AccountState(45, 200), result.Account);
        Assert.Equal(NoteCounts.None, spy.Stock);
        Assert.True(spy.Passes.Count >= 3);
        Assert.All(spy.Passes, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(result.Notes, spy.Passes.Aggregate(NoteCounts.None, (a, b) => a + b));
    }

    [Fact]
    public void Exactly_four_notes_and_exactly_five_notes()
    {
        var four = new SpyDispenser(new NoteCounts(0, 4, 0), null);
        var r4 = new CashMachine(four).Execute(new AccountState(250, 0), new Operation.Withdraw(80));
        Assert.Equal(AtmStatus.Ok, r4.Status);
        Assert.Equal(new NoteCounts(0, 4, 0), r4.Notes);
        Assert.Equal(NoteCounts.None, four.Stock);

        var five = new SpyDispenser(new NoteCounts(0, 0, 5), null);
        var r5 = new CashMachine(five).Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.Ok, r5.Status);
        Assert.Equal(new NoteCounts(0, 0, 5), r5.Notes);
        Assert.Equal(NoteCounts.None, five.Stock);
        Assert.True(five.Passes.Count >= 2);
        Assert.All(five.Passes, pass => Assert.InRange(pass.Total, 1, 4));
    }

    // ----- Dépôt : parcours exhaustifs -----

    [Fact]
    public void Deposit_matches_oracle_over_all_stocks_jams_and_deposits()
    {
        AccountState[] accounts = [new(0, 0), new(10, 30), new(250, 200), new(135, 70)];

        foreach (var account in accounts)
        foreach (var stock in AllStocks())
        foreach (var jammed in Jams)
        foreach (var deposit in AllDeposits())
        {
            CheckDeposit(account, stock, jammed, deposit);
        }
    }

    [Fact]
    public void Deposit_matches_oracle_over_all_accounts()
    {
        NoteCounts[] stocks = [new(0, 0, 0), new(4, 4, 4), new(5, 0, 5)];

        foreach (var stock in stocks)
        foreach (var jammed in Jams)
        foreach (var balance in Balances)
        foreach (var withdrawn in Withdrawns)
        foreach (var deposit in AllDeposits())
        {
            CheckDeposit(new AccountState(balance, withdrawn), stock, jammed, deposit);
        }
    }

    [Theory]
    [InlineData(1, 0, 0, 50)]
    [InlineData(0, 1, 0, 20)]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, 1, 1, 80)]
    public void Deposit_credits_value_of_each_denomination(int fifties, int twenties, int tens, int value)
    {
        var deposit = new NoteCounts(fifties, twenties, tens);
        var dispenser = new MoneyDispenser(new NoteCounts(2, 2, 2));
        var result = new CashMachine(dispenser).Execute(new AccountState(15, 40), new Operation.Deposit(deposit));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(15 + value, 40), result.Account);
        Assert.Equal(new NoteCounts(2, 2, 2) + deposit, dispenser.Stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    public void Each_cassette_can_be_full(Denomination full, int fifties, int twenties, int tens)
    {
        var stock = new NoteCounts(full == Denomination.Fifty ? 5 : 0, full == Denomination.Twenty ? 5 : 0,
            full == Denomination.Ten ? 5 : 0);
        var spy = new SpyDispenser(stock, null);
        var result = new CashMachine(spy).Execute(new AccountState(10, 0),
            new Operation.Deposit(new NoteCounts(fifties, twenties, tens)));

        Assert.Equal(AtmStatus.CassetteFull, result.Status);
        Assert.Equal(new AccountState(10, 0), result.Account);
        Assert.Equal(stock, spy.Stock);
        Assert.Empty(spy.Loads);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 1, 0, 0)]
    [InlineData(Denomination.Twenty, 0, 1, 0)]
    [InlineData(Denomination.Ten, 0, 0, 1)]
    [InlineData(Denomination.Ten, 1, 1, 1)]
    public void Each_cassette_can_be_jammed(Denomination jammed, int fifties, int twenties, int tens)
    {
        var stock = new NoteCounts(0, 0, 0);
        var spy = new SpyDispenser(stock, jammed);
        var result = new CashMachine(spy).Execute(new AccountState(10, 0),
            new Operation.Deposit(new NoteCounts(fifties, twenties, tens)));

        Assert.Equal(AtmStatus.CassetteJammed, result.Status);
        Assert.Equal(new AccountState(10, 0), result.Account);
        Assert.Equal(stock, spy.Stock);
        Assert.Empty(spy.Loads);
    }

    [Fact]
    public void Deposit_filling_cassette_to_exactly_five_succeeds()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(4, 4, 4));
        var result = new CashMachine(dispenser).Execute(new AccountState(0, 0), new Operation.Deposit(new NoteCounts(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(new AccountState(80, 0), result.Account);
        Assert.Equal(new NoteCounts(5, 5, 5), dispenser.Stock);
    }

    // ----- Pannes : arrêt immédiat -----

    [Fact]
    public void Withdraw_fault_stops_immediately_without_further_passes()
    {
        var spy = new SpyDispenser(new NoteCounts(1, 5, 5), null) { FailOnDispenseCall = 2 };
        var account = new AccountState(250, 0);
        var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(2, spy.DispenseCalls);
        Assert.Single(spy.Passes);
        Assert.Equal(spy.Passes[0], result.Notes);
        Assert.Equal(new NoteCounts(1, 5, 5) - result.Notes, spy.Stock);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Empty(spy.Loads);
    }

    [Fact]
    public void Withdraw_fault_on_last_pass_returns_all_previous_passes()
    {
        var spy = new SpyDispenser(new NoteCounts(1, 5, 5), null) { FailOnDispenseCall = 3 };
        var account = new AccountState(250, 0);
        var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(3, spy.DispenseCalls);
        Assert.Equal(2, spy.Passes.Count);
        Assert.Equal(spy.Passes[0] + spy.Passes[1], result.Notes);
        Assert.Equal(new NoteCounts(1, 5, 5) - result.Notes, spy.Stock);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
    }

    [Fact]
    public void Withdraw_fault_on_first_pass_is_not_retried()
    {
        var spy = new SpyDispenser(new NoteCounts(5, 5, 5), null) { FailOnDispenseCall = 1 };
        var account = new AccountState(250, 0);
        var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(1, spy.DispenseCalls);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(new NoteCounts(5, 5, 5), spy.Stock);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
    }

    [Fact]
    public void Deposit_fault_is_not_retried_and_changes_nothing()
    {
        var spy = new SpyDispenser(new NoteCounts(1, 1, 1), Denomination.Fifty) { FailOnLoadCall = 1 };
        var account = new AccountState(10, 30);
        var result = new CashMachine(spy).Execute(account, new Operation.Deposit(new NoteCounts(0, 1, 1)));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(1, spy.LoadCalls);
        Assert.Empty(spy.Loads);
        Assert.Equal(new NoteCounts(1, 1, 1), spy.Stock);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(0, spy.DispenseCalls);
    }

    // ----- Sans mémoire entre deux appels -----

    [Fact]
    public void Machine_reads_live_dispenser_state_on_every_call()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(0, 5, 0));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(40));
        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(new NoteCounts(0, 2, 0), first.Notes);
        Assert.Equal(new NoteCounts(0, 3, 0), dispenser.Stock);

        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(70));
        Assert.Equal(AtmStatus.CannotDispense, second.Status);
        Assert.Equal(new NoteCounts(0, 3, 0), dispenser.Stock);

        var deposit = machine.Execute(new AccountState(0, 0), new Operation.Deposit(new NoteCounts(0, 0, 1)));
        Assert.Equal(AtmStatus.Ok, deposit.Status);
        Assert.Equal(new AccountState(10, 0), deposit.Account);
        Assert.Equal(new NoteCounts(0, 3, 1), dispenser.Stock);

        var third = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(70));
        Assert.Equal(AtmStatus.Ok, third.Status);
        Assert.Equal(new NoteCounts(0, 3, 1), third.Notes);
        Assert.Equal(NoteCounts.None, dispenser.Stock);

        var full = machine.Execute(new AccountState(0, 0), new Operation.Deposit(new NoteCounts(1, 1, 1)));
        Assert.Equal(AtmStatus.Ok, full.Status);
        Assert.Equal(new NoteCounts(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Machine_uses_only_the_account_it_is_given()
    {
        var machine = new CashMachine(new MoneyDispenser(new NoteCounts(5, 5, 5)));

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(150));
        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(new AccountState(95, 150), first.Account);

        // Un nouvel appel repart du compte fourni, pas du résultat précédent.
        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(new AccountState(200, 50), second.Account);

        var refused = machine.Execute(new AccountState(20, 0), new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.InsufficientFunds, refused.Status);
        Assert.Equal(new AccountState(20, 0), refused.Account);

        var afterRefusal = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.Ok, afterRefusal.Status);
        Assert.Equal(new NoteCounts(1, 0, 0), afterRefusal.Notes);
    }

    [Fact]
    public void Fault_does_not_leave_the_machine_in_a_bad_state()
    {
        var spy = new SpyDispenser(new NoteCounts(5, 5, 5), null) { FailOnDispenseCall = 1 };
        var machine = new CashMachine(spy);

        var fault = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.DispenserFault, fault.Status);

        var ok = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.Ok, ok.Status);
        Assert.Equal(new NoteCounts(1, 0, 0), ok.Notes);
        Assert.Equal(new NoteCounts(4, 5, 5), spy.Stock);
    }

    // ----- Double de test -----

    /// <summary>
    /// Enveloppe le vrai module, enregistre les passages et dépôts réussis, et peut simuler une panne au N-ième appel.
    /// </summary>
    private sealed class SpyDispenser(NoteCounts stock, Denomination? jammed) : IMoneyDispenser
    {
        private readonly MoneyDispenser inner = new(stock, jammed);

        public int FailOnDispenseCall { get; init; }
        public int FailOnLoadCall { get; init; }
        public int DispenseCalls { get; private set; }
        public int LoadCalls { get; private set; }
        public List<NoteCounts> Attempts { get; } = [];
        public List<NoteCounts> Passes { get; } = [];
        public List<NoteCounts> Loads { get; } = [];

        public NoteCounts Stock => inner.Stock;
        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            Attempts.Add(notes);
            if (++DispenseCalls == FailOnDispenseCall || !inner.Dispense(notes)) return false;
            Passes.Add(notes);
            return true;
        }

        public bool Load(NoteCounts notes)
        {
            if (++LoadCalls == FailOnLoadCall || !inner.Load(notes)) return false;
            Loads.Add(notes);
            return true;
        }
    }
}
