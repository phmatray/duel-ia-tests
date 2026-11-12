namespace Atm.Tests;

/// <summary>
/// Tests complémentaires : parcours croisé complet du domaine, machine réutilisée sur plusieurs appels,
/// pannes à chaque passage d'un retrait, et contrat du vrai module de billets.
/// </summary>
public class Renforts
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var a = 0; a <= 5; a++)
            for (var b = 0; b <= 5; b++)
                for (var c = 0; c <= 5; c++)
                    yield return N(a, b, c);
    }

    private static readonly NoteCounts[] Stocks = AllStocks().ToArray();

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly NoteCounts[] Deposits =
        Enumerable.Range(0, 8).Select(m => N(m & 1, (m >> 1) & 1, (m >> 2) & 1)).ToArray();

    private static IEnumerable<AccountState> AllAccounts()
    {
        for (var balance = 0; balance <= 250; balance += 5)
            for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
                yield return new AccountState(balance, withdrawn);
    }

    private static int Count(NoteCounts notes, Denomination? denomination) => denomination switch
    {
        Denomination.Fifty => notes.Fifties,
        Denomination.Twenty => notes.Twenties,
        Denomination.Ten => notes.Tens,
        _ => 0,
    };

    private static bool FitsIn(NoteCounts notes, NoteCounts stock) =>
        notes.Fifties <= stock.Fifties && notes.Twenties <= stock.Twenties && notes.Tens <= stock.Tens;

    /// <summary>Oracle : la combinaison exacte la plus courte parmi les billets hors cassette bloquée.</summary>
    private static NoteCounts? FewestNotes(int amount, NoteCounts stock, Denomination? jammed)
    {
        var exact = Stocks
            .Where(c => c.Value == amount && FitsIn(c, stock) && Count(c, jammed) == 0)
            .ToList();
        if (exact.Count == 0)
        {
            return null;
        }

        var min = exact.Min(c => c.Total);
        var best = exact.Where(c => c.Total == min).ToList();
        Assert.True(best.Count == 1, $"Plusieurs combinaisons minimales pour {amount} : {string.Join(", ", best)}.");

        return best[0];
    }

    // ---- Parcours croisé complet : compte × stock × cassette bloquée × montant ----

    [Fact]
    public void Withdraw_matches_specification_over_the_full_cross_product_of_the_domain()
    {
        foreach (var stock in Stocks)
            foreach (var jammed in JamOptions)
                foreach (var amount in Amounts)
                {
                    var combination = amount > 0 && amount % 10 == 0 ? FewestNotes(amount, stock, jammed) : null;
                    var fee = amount > 100 ? 5 : 0;

                    foreach (var account in AllAccounts())
                    {
                        var expectedStatus =
                            amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
                            : account.WithdrawnToday + amount > 200 ? AtmStatus.DailyLimitExceeded
                            : amount + fee > account.Balance ? AtmStatus.InsufficientFunds
                            : combination is null ? AtmStatus.CannotDispense
                            : AtmStatus.Ok;

                        var expected = expectedStatus == AtmStatus.Ok
                            ? new AtmResult(AtmStatus.Ok, combination!.Value, fee,
                                new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount))
                            : new AtmResult(expectedStatus, NoteCounts.None, 0, account);
                        var expectedStock = stock - expected.Notes;

                        var dispenser = new MoneyDispenser(stock, jammed);
                        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

                        if (result != expected || dispenser.Stock != expectedStock)
                        {
                            Assert.Fail(
                                $"Retrait {amount}, compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"} : " +
                                $"attendu {expected} et stock {expectedStock}, obtenu {result} et stock {dispenser.Stock}.");
                        }
                    }
                }
    }

    [Fact]
    public void Deposit_matches_specification_over_the_full_cross_product_of_the_domain()
    {
        foreach (var stock in Stocks)
            foreach (var jammed in JamOptions)
                foreach (var notes in Deposits)
                {
                    var after = stock + notes;
                    var expectedStatus =
                        notes.Total == 0 ? AtmStatus.InvalidAmount
                        : Count(notes, jammed) > 0 ? AtmStatus.CassetteJammed
                        : after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5 ? AtmStatus.CassetteFull
                        : AtmStatus.Ok;
                    var expectedStock = expectedStatus == AtmStatus.Ok ? after : stock;

                    foreach (var account in AllAccounts())
                    {
                        var expected = expectedStatus == AtmStatus.Ok
                            ? new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value })
                            : new AtmResult(expectedStatus, NoteCounts.None, 0, account);

                        var dispenser = new MoneyDispenser(stock, jammed);
                        var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));

                        if (result != expected || dispenser.Stock != expectedStock)
                        {
                            Assert.Fail(
                                $"Dépôt {notes}, compte {account}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"} : " +
                                $"attendu {expected} et stock {expectedStock}, obtenu {result} et stock {dispenser.Stock}.");
                        }
                    }
                }
    }

    // ---- Retraits : passages ----

    [Fact]
    public void Every_successful_withdrawal_dispenses_in_valid_passes_summing_to_the_notes()
    {
        foreach (var stock in Stocks)
            foreach (var jammed in JamOptions)
                foreach (var amount in Amounts)
                {
                    var recorder = new RecordingDispenser(new MoneyDispenser(stock, jammed));
                    var result = new CashMachine(recorder).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));

                    if (result.Status != AtmStatus.Ok)
                    {
                        Assert.Empty(recorder.Passes);
                        Assert.Empty(recorder.Loads);
                        continue;
                    }

                    Assert.NotEmpty(recorder.Passes);
                    Assert.All(recorder.Passes, pass =>
                    {
                        Assert.InRange(pass.Total, 1, 4);
                        Assert.True(pass.Fifties >= 0 && pass.Twenties >= 0 && pass.Tens >= 0, $"Passage négatif : {pass}.");
                        Assert.Equal(0, Count(pass, jammed));
                    });
                    Assert.Equal(result.Notes, recorder.Passes.Aggregate(NoteCounts.None, (sum, pass) => sum + pass));
                    Assert.Empty(recorder.Loads);
                }
    }

    [Fact]
    public void Deposit_never_dispenses_and_refusals_never_reach_the_module()
    {
        foreach (var stock in Stocks)
            foreach (var jammed in JamOptions)
                foreach (var notes in Deposits)
                {
                    var recorder = new RecordingDispenser(new MoneyDispenser(stock, jammed));
                    var result = new CashMachine(recorder).Execute(new AccountState(100, 40), new Operation.Deposit(notes));

                    Assert.Empty(recorder.Passes);
                    if (result.Status == AtmStatus.Ok)
                    {
                        Assert.Equal(notes, recorder.Loads.Aggregate(NoteCounts.None, (sum, load) => sum + load));
                    }
                    else
                    {
                        Assert.Empty(recorder.Loads);
                    }
                }
    }

    // ---- Machine réutilisée : elle relit le module et ne garde rien entre deux appels ----

    [Fact]
    public void Same_machine_sees_stock_emptied_by_a_previous_withdrawal()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));
        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(50));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new AccountState(250, 0)), second);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Same_machine_sees_notes_loaded_by_a_previous_deposit()
    {
        var dispenser = new MoneyDispenser(N(0, 0, 0));
        var machine = new CashMachine(dispenser);

        var deposit = machine.Execute(new AccountState(0, 0), new Operation.Deposit(N(1, 1, 1)));
        var withdraw = machine.Execute(deposit.Account, new Operation.Withdraw(70));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(80, 0)), deposit);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 0), 0, new AccountState(10, 70)), withdraw);
        Assert.Equal(N(0, 0, 1), dispenser.Stock);
    }

    [Fact]
    public void Same_machine_sees_cassette_filled_by_a_previous_deposit()
    {
        var dispenser = new MoneyDispenser(N(4, 0, 0));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new AccountState(0, 0), new Operation.Deposit(N(1, 0, 0)));
        var second = machine.Execute(new AccountState(0, 0), new Operation.Deposit(N(1, 0, 0)));

        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, new AccountState(0, 0)), second);
        Assert.Equal(N(5, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Same_machine_keeps_no_account_memory_between_calls()
    {
        var machine = new CashMachine(new MoneyDispenser(N(5, 5, 5)));

        var first = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(150));
        // Le compte passé ici ignore le premier retrait : seule compte la valeur reçue.
        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(150));
        var third = machine.Execute(new AccountState(20, 190), new Operation.Withdraw(10));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(3, 0, 0), 5, new AccountState(95, 150)), first);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 2, 1), 5, new AccountState(95, 150)), second);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 0, 1), 0, new AccountState(10, 200)), third);
    }

    [Fact]
    public void Repeated_identical_calls_on_fresh_modules_give_identical_results()
    {
        var account = new AccountState(200, 30);
        var results = Enumerable.Range(0, 3)
            .Select(_ => new CashMachine(new MoneyDispenser(N(2, 3, 4), Denomination.Fifty))
                .Execute(account, new Operation.Withdraw(90)))
            .ToList();

        Assert.All(results, r => Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 3, 3), 0, new AccountState(110, 120)), r));
    }

    // ---- Pannes à chaque passage d'un retrait en plusieurs passages ----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Withdraw_fault_on_any_pass_stops_immediately_and_returns_notes_already_dispensed(int failingCall)
    {
        // 200 € avec 50×1 20×5 10×5 : 11 billets, donc au moins 3 passages.
        var dispenser = new ScriptedDispenser(N(1, 5, 5), failingDispenseCall: failingCall);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
        Assert.Equal(failingCall, dispenser.DispenseCalls);
        Assert.Equal(dispenser.Dispensed, result.Notes);
        Assert.Equal(N(1, 5, 5) - dispenser.Dispensed, dispenser.Stock);
        Assert.Equal(0, dispenser.LoadCalls);
    }

    [Fact]
    public void Withdraw_without_fault_through_a_double_is_ok()
    {
        // Même double, sans panne : vérifie que les tests de panne ne réussissent pas par accident.
        var dispenser = new ScriptedDispenser(N(1, 5, 5), failingDispenseCall: 0);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(200));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 5, 5), 5, new AccountState(45, 200)), result);
        Assert.Equal(N(1, 5, 5), dispenser.Dispensed);
    }

    [Fact]
    public void Withdraw_fault_with_fee_keeps_fee_at_zero_and_account_unchanged()
    {
        var dispenser = new ScriptedDispenser(N(5, 5, 5), failingDispenseCall: 1);
        var result = new CashMachine(dispenser).Execute(new AccountState(115, 0), new Operation.Withdraw(110));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(115, 0)), result);
    }

    [Fact]
    public void Deposit_fault_does_not_dispense_and_leaves_everything_unchanged()
    {
        var dispenser = new ScriptedDispenser(N(1, 1, 1), failingDispenseCall: 0, failLoad: true);
        var result = new CashMachine(dispenser).Execute(new AccountState(10, 20), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(10, 20)), result);
        Assert.Equal(1, dispenser.LoadCalls);
        Assert.Equal(0, dispenser.DispenseCalls);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Withdraw_refusal_does_not_call_the_module_even_if_it_would_accept()
    {
        // Le double accepte tout : un refus doit venir des contrôles de la machine, pas du module.
        var dispenser = new ScriptedDispenser(N(5, 5, 5), failingDispenseCall: 0);
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.InvalidAmount, machine.Execute(new AccountState(250, 0), new Operation.Withdraw(5)).Status);
        Assert.Equal(AtmStatus.DailyLimitExceeded, machine.Execute(new AccountState(250, 150), new Operation.Withdraw(60)).Status);
        Assert.Equal(AtmStatus.InsufficientFunds, machine.Execute(new AccountState(110, 0), new Operation.Withdraw(110)).Status);
        Assert.Equal(0, dispenser.DispenseCalls);
        Assert.Equal(0, dispenser.LoadCalls);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    // ---- Contrat du vrai module de billets ----

    [Fact]
    public void MoneyDispenser_exposes_its_construction_state()
    {
        var dispenser = new MoneyDispenser(N(1, 2, 3), Denomination.Twenty);

        Assert.Equal(N(1, 2, 3), dispenser.Stock);
        Assert.Equal(Denomination.Twenty, dispenser.Jammed);
        Assert.Null(new MoneyDispenser(N(0, 0, 0)).Jammed);
    }

    [Fact]
    public void MoneyDispenser_dispense_follows_its_contract()
    {
        foreach (var stock in Stocks)
            foreach (var jammed in JamOptions)
                foreach (var notes in Stocks)
                {
                    var dispenser = new MoneyDispenser(stock, jammed);
                    var expected = notes.Total > 0 && notes.Total <= 4 && Count(notes, jammed) == 0 && FitsIn(notes, stock);

                    Assert.Equal(expected, dispenser.Dispense(notes));
                    Assert.Equal(expected ? stock - notes : stock, dispenser.Stock);
                    Assert.Equal(jammed, dispenser.Jammed);
                }
    }

    [Fact]
    public void MoneyDispenser_load_follows_its_contract()
    {
        foreach (var stock in Stocks)
            foreach (var jammed in JamOptions)
                foreach (var notes in Stocks)
                {
                    var dispenser = new MoneyDispenser(stock, jammed);
                    var after = stock + notes;
                    var expected = notes.Total > 0 && Count(notes, jammed) == 0
                        && after.Fifties <= 5 && after.Twenties <= 5 && after.Tens <= 5;

                    Assert.Equal(expected, dispenser.Load(notes));
                    Assert.Equal(expected ? after : stock, dispenser.Stock);
                    Assert.Equal(jammed, dispenser.Jammed);
                }
    }

    [Fact]
    public void MoneyDispenser_successive_dispenses_draw_down_the_stock()
    {
        var dispenser = new MoneyDispenser(N(1, 5, 5));

        Assert.True(dispenser.Dispense(N(1, 3, 0)));
        Assert.True(dispenser.Dispense(N(0, 2, 2)));
        Assert.False(dispenser.Dispense(N(0, 1, 0)));
        Assert.True(dispenser.Dispense(N(0, 0, 3)));
        Assert.False(dispenser.Dispense(N(0, 0, 1)));
        Assert.Equal(N(0, 0, 0), dispenser.Stock);
    }

    // ---- Doublures ----

    private sealed class RecordingDispenser(IMoneyDispenser inner) : IMoneyDispenser
    {
        public List<NoteCounts> Passes { get; } = [];

        public List<NoteCounts> Loads { get; } = [];

        public NoteCounts Stock => inner.Stock;

        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            Passes.Add(notes);
            return inner.Dispense(notes);
        }

        public bool Load(NoteCounts notes)
        {
            Loads.Add(notes);
            return inner.Load(notes);
        }
    }

    /// <summary>Module permissif qui échoue sur un appel choisi à <c>Dispense</c> (0 : jamais) ou sur tout <c>Load</c>.</summary>
    private sealed class ScriptedDispenser(NoteCounts stock, int failingDispenseCall, bool failLoad = false) : IMoneyDispenser
    {
        public NoteCounts Stock { get; private set; } = stock;

        public Denomination? Jammed => null;

        public int DispenseCalls { get; private set; }

        public int LoadCalls { get; private set; }

        public NoteCounts Dispensed { get; private set; }

        public bool Dispense(NoteCounts notes)
        {
            DispenseCalls++;
            if (DispenseCalls == failingDispenseCall)
            {
                return false;
            }

            Stock -= notes;
            Dispensed += notes;
            return true;
        }

        public bool Load(NoteCounts notes)
        {
            LoadCalls++;
            if (failLoad)
            {
                return false;
            }

            Stock += notes;
            return true;
        }
    }
}
