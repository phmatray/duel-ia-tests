namespace Atm.Tests;

/// <summary>
/// Tests complémentaires : pannes à n'importe quel passage, absence d'appel au module sur un refus,
/// découpage en passages vérifié par une doublure, absence de mémoire entre deux appels, domaine complet
/// des comptes et contrat du vrai module (qui sert d'oracle aux autres tests).
/// </summary>
public class Renforts
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly int[] Balances = Enumerable.Range(0, 51).Select(i => i * 5).ToArray();

    private static readonly int[] Withdrawns = Enumerable.Range(0, 21).Select(i => i * 10).ToArray();

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var c = 0; c <= 5; c++)
        {
            yield return N(f, t, c);
        }
    }

    private static IEnumerable<NoteCounts> AllDeposits()
    {
        for (var mask = 0; mask < 8; mask++)
        {
            yield return N(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
        }
    }

    private static NoteCounts Sum(IEnumerable<NoteCounts> notes) => notes.Aggregate(NoteCounts.None, (a, b) => a + b);

    private static NoteCounts Available(NoteCounts stock, Denomination? jammed) => N(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    /// <summary>Combinaison de référence, par recherche exhaustive.</summary>
    private static NoteCounts? Fewest(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var c = 0; c <= available.Tens; c++)
        {
            var candidate = N(f, t, c);
            if (candidate.Value == amount && (best is null || candidate.Total < best.Value.Total))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static int FeeFor(int amount) => amount > 100 ? 5 : 0;

    // ---- Pannes du module pendant un retrait -------------------------------------------------------

    [Fact]
    public void Fault_on_third_pass_reports_the_two_first_passes()
    {
        // 150 sans billet de 50 : 10 billets, donc au moins 3 passages.
        var spy = new RecordingDispenser(N(0, 5, 5)) { FailDispenseAtCall = 2 };
        var account = new AccountState(250, 0);

        var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(3, spy.DispenseCalls.Count);
        Assert.Equal(spy.DispenseCalls[0] + spy.DispenseCalls[1], result.Notes);
        Assert.Equal(8, result.Notes.Total);
        Assert.Equal(N(0, 5, 5) - result.Notes, spy.Stock);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Empty(spy.LoadCalls);
    }

    [Fact]
    public void Fault_stops_immediately_even_if_the_module_would_accept_again()
    {
        // Le module refuse une seule fois, puis accepterait : la machine ne doit pas réessayer ni continuer.
        var spy = new RecordingDispenser(N(0, 5, 5)) { FailDispenseAtCall = 1 };
        var account = new AccountState(250, 0);

        var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(2, spy.DispenseCalls.Count);
        Assert.Equal(spy.DispenseCalls[0], result.Notes);
        Assert.Empty(spy.LoadCalls);
        Assert.Equal(account, result.Account);
    }

    [Fact]
    public void Fault_at_any_pass_over_domain_reports_exactly_what_left_the_module()
    {
        var account = new AccountState(250, 0);

        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        for (var amount = 10; amount <= 200; amount += 10)
        {
            if (Fewest(amount, Available(stock, jammed)) is not { } notes)
            {
                continue;
            }

            // Passage nominal : on compte les passages.
            var nominal = new RecordingDispenser(stock, jammed);
            var ok = new CashMachine(nominal).Execute(account, new Operation.Withdraw(amount));
            Assert.Equal(AtmStatus.Ok, ok.Status);
            Assert.Equal(notes, ok.Notes);
            var passes = nominal.DispenseCalls.Count;
            Assert.InRange(passes, (notes.Total + 3) / 4, notes.Total);

            for (var k = 0; k < passes; k++)
            {
                var spy = new RecordingDispenser(stock, jammed) { FailDispenseAtCall = k };
                var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(amount));

                Assert.Equal(AtmStatus.DispenserFault, result.Status);
                Assert.Equal(k + 1, spy.DispenseCalls.Count);
                Assert.Equal(Sum(spy.DispenseCalls.Take(k)), result.Notes);
                Assert.Equal(stock - result.Notes, spy.Stock);
                Assert.Equal(0, result.Fee);
                Assert.Equal(account, result.Account);
                Assert.Empty(spy.LoadCalls);
            }
        }
    }

    [Fact]
    public void Deposit_fault_calls_load_once_and_never_dispenses()
    {
        var spy = new RecordingDispenser(N(2, 2, 2)) { FailLoad = true };
        var account = new AccountState(40, 120);

        var result = new CashMachine(spy).Execute(account, new Operation.Deposit(N(1, 0, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, account), result);
        Assert.Equal([N(1, 0, 1)], spy.LoadCalls);
        Assert.Empty(spy.DispenseCalls);
        Assert.Equal(N(2, 2, 2), spy.Stock);
    }

    // ---- Les passages, observés par une doublure -------------------------------------------------

    [Fact]
    public void Every_successful_withdrawal_uses_passes_of_one_to_four_notes_summing_to_the_combination()
    {
        // La doublure accepte n'importe quel passage : c'est la machine, pas le module, qui doit respecter la limite.
        var account = new AccountState(250, 0);

        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        for (var amount = 10; amount <= 200; amount += 10)
        {
            var spy = new RecordingDispenser(stock, jammed) { Permissive = true };
            var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(amount));

            if (result.Status != AtmStatus.Ok)
            {
                Assert.Empty(spy.DispenseCalls);
                continue;
            }

            Assert.All(spy.DispenseCalls, pass =>
            {
                Assert.InRange(pass.Total, 1, 4);
                Assert.True(pass.Fifties >= 0 && pass.Twenties >= 0 && pass.Tens >= 0);
                if (jammed is { } j)
                {
                    Assert.Equal(0, pass.Of(j));
                }
            });
            Assert.Equal(result.Notes, Sum(spy.DispenseCalls));
            Assert.Empty(spy.LoadCalls);
        }
    }

    [Fact]
    public void Successful_deposit_loads_exactly_the_deposited_notes_once()
    {
        var spy = new RecordingDispenser(N(1, 1, 1));

        var result = new CashMachine(spy).Execute(new AccountState(0, 0), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal([N(1, 1, 1)], spy.LoadCalls);
        Assert.Empty(spy.DispenseCalls);
    }

    // ---- Un refus ne sollicite jamais le module ---------------------------------------------------

    [Fact]
    public void Refused_withdrawals_never_call_the_module()
    {
        // Avec une doublure qui accepterait tout, un refus ne doit déplacer aucun billet.
        foreach (var stock in new[] { N(0, 0, 0), N(1, 1, 0), N(5, 5, 5), N(0, 0, 5) })
        foreach (var jammed in JamOptions)
        foreach (var amount in Amounts)
        foreach (var (balance, withdrawn) in new[] { (0, 0), (100, 0), (250, 150), (250, 200), (115, 0), (110, 0) })
        {
            var spy = new RecordingDispenser(stock, jammed) { Permissive = true };
            var account = new AccountState(balance, withdrawn);
            var result = new CashMachine(spy).Execute(account, new Operation.Withdraw(amount));

            if (result.Status == AtmStatus.Ok)
            {
                continue;
            }

            Assert.Empty(spy.DispenseCalls);
            Assert.Empty(spy.LoadCalls);
            Assert.Equal(stock, spy.Stock);
            Assert.Equal(new AtmResult(result.Status, NoteCounts.None, 0, account), result);
        }
    }

    [Fact]
    public void Refused_deposits_never_call_the_module()
    {
        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var deposit in AllDeposits())
        {
            var spy = new RecordingDispenser(stock, jammed) { Permissive = true };
            var account = new AccountState(35, 70);
            var result = new CashMachine(spy).Execute(account, new Operation.Deposit(deposit));

            if (result.Status == AtmStatus.Ok)
            {
                Assert.Equal([deposit], spy.LoadCalls);
                continue;
            }

            Assert.Contains(result.Status, new[] { AtmStatus.InvalidAmount, AtmStatus.CassetteJammed, AtmStatus.CassetteFull });
            Assert.Empty(spy.DispenseCalls);
            Assert.Empty(spy.LoadCalls);
            Assert.Equal(new AtmResult(result.Status, NoteCounts.None, 0, account), result);
        }
    }

    // ---- Sans mémoire entre deux appels ------------------------------------------------------------

    [Fact]
    public void Machine_reads_the_module_stock_at_each_call()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Withdraw(50)).Status);
        Assert.Equal(N(0, 0, 0), dispenser.Stock);

        // Le stock est vide : la machine ne doit pas se souvenir du billet de 50.
        var empty = machine.Execute(account, new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.CannotDispense, empty.Status);

        // Le module est rechargé hors de la machine : elle doit voir les nouveaux billets.
        Assert.True(dispenser.Load(N(0, 2, 1)));
        var reloaded = machine.Execute(account, new Operation.Withdraw(50));
        Assert.Equal(AtmStatus.Ok, reloaded.Status);
        Assert.Equal(N(0, 2, 1), reloaded.Notes);
    }

    [Fact]
    public void Deposited_notes_can_be_withdrawn_by_the_same_machine()
    {
        var dispenser = new MoneyDispenser(N(0, 4, 0));
        var machine = new CashMachine(dispenser);

        var deposit = machine.Execute(new AccountState(0, 0), new Operation.Deposit(N(1, 1, 0)));
        Assert.Equal(AtmStatus.Ok, deposit.Status);
        Assert.Equal(N(1, 5, 0), dispenser.Stock);

        // Le compte rendu est réutilisé tel quel pour l'appel suivant.
        var withdraw = machine.Execute(deposit.Account, new Operation.Withdraw(70));
        Assert.Equal(AtmStatus.Ok, withdraw.Status);
        Assert.Equal(N(1, 1, 0), withdraw.Notes);
        Assert.Equal(new AccountState(0, 70), withdraw.Account);

        // Plus de cassette pleine : un nouveau billet de 20 est accepté.
        var again = machine.Execute(withdraw.Account, new Operation.Deposit(N(0, 1, 0)));
        Assert.Equal(AtmStatus.Ok, again.Status);
        Assert.Equal(N(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Machine_does_not_remember_accounts_between_calls()
    {
        var machine = new CashMachine(new MoneyDispenser(N(5, 5, 5)));

        var first = machine.Execute(new AccountState(250, 190), new Operation.Withdraw(10));
        Assert.Equal(new AccountState(240, 200), first.Account);

        // Un autre compte, un autre jour : le plafond repart de ce que dit le compte reçu.
        var second = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(200));
        Assert.Equal(AtmStatus.Ok, second.Status);
        Assert.Equal(new AccountState(45, 200), second.Account);

        // Un solde faible : la machine ne retient pas le solde précédent.
        var third = machine.Execute(new AccountState(5, 0), new Operation.Withdraw(10));
        Assert.Equal(AtmStatus.InsufficientFunds, third.Status);
        Assert.Equal(new AccountState(5, 0), third.Account);
    }

    [Fact]
    public void Successive_deposits_see_the_cassette_filling_up()
    {
        var dispenser = new MoneyDispenser(N(0, 4, 3));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(0, 0);

        var first = machine.Execute(account, new Operation.Deposit(N(0, 1, 1)));
        Assert.Equal(AtmStatus.Ok, first.Status);
        Assert.Equal(N(0, 5, 4), dispenser.Stock);

        // La cassette de 20 est maintenant pleine : refus propre, pas une panne.
        var second = machine.Execute(first.Account, new Operation.Deposit(N(0, 1, 0)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, first.Account), second);

        var third = machine.Execute(first.Account, new Operation.Deposit(N(0, 0, 1)));
        Assert.Equal(AtmStatus.Ok, third.Status);
        Assert.Equal(N(0, 5, 5), dispenser.Stock);

        var fourth = machine.Execute(third.Account, new Operation.Deposit(N(1, 0, 1)));
        Assert.Equal(AtmStatus.CassetteFull, fourth.Status);
        Assert.Equal(N(0, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Machine_follows_a_module_whose_state_changes_between_calls()
    {
        // Un module dont l'état évolue hors de la machine (cassette débloquée, puis une autre bloquée).
        var module = new MutableDispenser { Stock = N(5, 5, 5), Jammed = Denomination.Fifty };
        var machine = new CashMachine(module);
        var account = new AccountState(250, 0);

        Assert.Equal(N(0, 2, 1), machine.Execute(account, new Operation.Withdraw(50)).Notes);
        Assert.Equal(AtmStatus.CassetteJammed, machine.Execute(account, new Operation.Deposit(N(1, 0, 0))).Status);

        module.Jammed = null;
        module.Stock = N(4, 5, 5);
        Assert.Equal(N(1, 0, 0), machine.Execute(account, new Operation.Withdraw(50)).Notes);
        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Deposit(N(1, 0, 0))).Status);

        module.Jammed = Denomination.Ten;
        module.Stock = N(0, 5, 5);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(account, new Operation.Withdraw(50)).Status);
        Assert.Equal(AtmStatus.CassetteJammed, machine.Execute(account, new Operation.Deposit(N(0, 0, 1))).Status);

        module.Jammed = null;
        module.Stock = N(0, 0, 0);
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(account, new Operation.Withdraw(10)).Status);
        module.Stock = N(5, 5, 5);
        Assert.Equal(AtmStatus.CassetteFull, machine.Execute(account, new Operation.Deposit(N(0, 0, 1))).Status);
    }

    [Fact]
    public void Jam_is_read_from_the_module_not_assumed()
    {
        var spy = new RecordingDispenser(N(5, 5, 5), Denomination.Twenty);
        var machine = new CashMachine(spy);

        var withdraw = machine.Execute(new AccountState(250, 0), new Operation.Withdraw(40));
        Assert.Equal(AtmStatus.Ok, withdraw.Status);
        Assert.Equal(N(0, 0, 4), withdraw.Notes);

        var deposit = machine.Execute(new AccountState(0, 0), new Operation.Deposit(N(0, 1, 0)));
        Assert.Equal(AtmStatus.CassetteJammed, deposit.Status);
    }

    // ---- Domaine complet des comptes ---------------------------------------------------------------

    [Fact]
    public void Withdraw_over_every_account_and_amount_with_full_stock()
    {
        foreach (var jammed in JamOptions)
        foreach (var balance in Balances)
        foreach (var withdrawn in Withdrawns)
        foreach (var amount in Amounts)
        {
            var stock = N(5, 5, 5);
            var available = Available(stock, jammed);
            var fee = FeeFor(amount);
            var account = new AccountState(balance, withdrawn);

            var expected =
                amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
                : withdrawn + amount > 200 ? AtmStatus.DailyLimitExceeded
                : amount + fee > balance ? AtmStatus.InsufficientFunds
                : Fewest(amount, available) is null ? AtmStatus.CannotDispense
                : AtmStatus.Ok;

            var dispenser = new MoneyDispenser(stock, jammed);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

            Assert.Equal(expected, result.Status);
            if (expected == AtmStatus.Ok)
            {
                var notes = Fewest(amount, available)!.Value;
                Assert.Equal(new AtmResult(AtmStatus.Ok, notes, fee, new AccountState(balance - amount - fee, withdrawn + amount)), result);
                Assert.Equal(stock - notes, dispenser.Stock);
            }
            else
            {
                Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), result);
                Assert.Equal(stock, dispenser.Stock);
            }
        }
    }

    [Fact]
    public void Deposit_over_every_account()
    {
        foreach (var balance in Balances)
        foreach (var withdrawn in Withdrawns)
        foreach (var deposit in AllDeposits().Where(d => d.Total > 0))
        {
            var dispenser = new MoneyDispenser(N(2, 3, 4));
            var account = new AccountState(balance, withdrawn);

            var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(deposit));

            Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(balance + deposit.Value, withdrawn)), result);
            Assert.Equal(N(2, 3, 4) + deposit, dispenser.Stock);
        }
    }

    [Theory]
    [InlineData(250, 0, 220, AtmStatus.DailyLimitExceeded)]
    [InlineData(250, 10, 200, AtmStatus.DailyLimitExceeded)]
    [InlineData(250, 190, 10, AtmStatus.Ok)]
    [InlineData(250, 190, 20, AtmStatus.DailyLimitExceeded)]
    [InlineData(10, 0, 10, AtmStatus.Ok)]
    [InlineData(5, 0, 10, AtmStatus.InsufficientFunds)]
    [InlineData(0, 0, 10, AtmStatus.InsufficientFunds)]
    [InlineData(205, 0, 200, AtmStatus.Ok)]
    [InlineData(200, 0, 200, AtmStatus.InsufficientFunds)]
    [InlineData(105, 0, 110, AtmStatus.InsufficientFunds)]
    [InlineData(250, 0, 15, AtmStatus.InvalidAmount)]
    [InlineData(250, 0, 105, AtmStatus.InvalidAmount)]
    public void Boundaries_of_each_check(int balance, int withdrawn, int amount, AtmStatus expected)
    {
        var result = new CashMachine(new MoneyDispenser(N(5, 5, 5))).Execute(
            new AccountState(balance, withdrawn), new Operation.Withdraw(amount));

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Daily_limit_is_checked_before_invalid_stock_and_funds()
    {
        // Montant trop élevé, solde nul, stock vide, cassette bloquée : c'est le plafond qui gagne.
        var result = new CashMachine(new MoneyDispenser(N(0, 0, 0), Denomination.Fifty)).Execute(
            new AccountState(0, 0), new Operation.Withdraw(210));

        Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
    }

    [Fact]
    public void Insufficient_funds_is_checked_before_cannot_dispense_even_with_jam()
    {
        var result = new CashMachine(new MoneyDispenser(N(5, 0, 0), Denomination.Fifty)).Execute(
            new AccountState(100, 0), new Operation.Withdraw(110));

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    // ---- Combinaisons particulières ------------------------------------------------------------------

    [Theory]
    // 80 : 50+20+10 (3 billets) plutôt que 20×4 (4 billets).
    [InlineData(1, 4, 1, null, 80, 1, 1, 1)]
    // 80 sans billet de 10 : 20×4.
    [InlineData(1, 4, 0, null, 80, 0, 4, 0)]
    // 130 : 50×2 + 20 + 10 (4 billets) plutôt que 50 + 20×4 (5 billets).
    [InlineData(5, 5, 5, null, 130, 2, 1, 1)]
    // 130 sans billet de 10 : 50 + 20×4.
    [InlineData(5, 5, 0, null, 130, 1, 4, 0)]
    // 90 avec les 50 bloqués : 20×4 + 10.
    [InlineData(5, 5, 5, Denomination.Fifty, 90, 0, 4, 1)]
    // 100 avec un seul 50 : 50 + 20×2 + 10.
    [InlineData(1, 5, 5, null, 100, 1, 2, 1)]
    // 100 sans 20 : 50×2.
    [InlineData(5, 5, 5, Denomination.Twenty, 100, 2, 0, 0)]
    // 60 avec les 10 bloqués : 50 + 10 est impossible, donc 20×3.
    [InlineData(5, 5, 5, Denomination.Ten, 60, 0, 3, 0)]
    // 150 avec les 50 bloqués : 20×5 + 10×5, soit 10 billets.
    [InlineData(5, 5, 5, Denomination.Fifty, 150, 0, 5, 5)]
    public void Fewest_notes_in_tricky_cases(int f, int t, int c, Denomination? jammed, int amount, int ef, int et, int ec)
    {
        var dispenser = new MoneyDispenser(N(f, t, c), jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(ef, et, ec), result.Notes);
        Assert.Equal(N(f, t, c) - N(ef, et, ec), dispenser.Stock);
    }

    [Theory]
    [InlineData(5, 5, 0, Denomination.Ten, 30)]
    [InlineData(0, 5, 5, Denomination.Ten, 50)]
    [InlineData(5, 0, 5, Denomination.Ten, 20)]
    [InlineData(0, 0, 5, null, 60)]
    [InlineData(1, 0, 0, null, 100)]
    public void Cannot_dispense_in_tricky_cases(int f, int t, int c, Denomination? jammed, int amount)
    {
        var dispenser = new MoneyDispenser(N(f, t, c), jammed);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new AccountState(250, 0)), result);
        Assert.Equal(N(f, t, c), dispenser.Stock);
    }

    // ---- Contrat du vrai module (oracle des autres tests) --------------------------------------------

    [Fact]
    public void Real_dispenser_rejects_passes_of_more_than_four_notes()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));

        Assert.False(dispenser.Dispense(N(0, 0, 5)));
        Assert.False(dispenser.Dispense(N(2, 2, 1)));
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
        Assert.True(dispenser.Dispense(N(2, 1, 1)));
        Assert.Equal(N(3, 4, 4), dispenser.Stock);
    }

    [Fact]
    public void Real_dispenser_rejects_empty_jammed_or_excessive_passes()
    {
        var dispenser = new MoneyDispenser(N(1, 2, 3), Denomination.Twenty);

        Assert.False(dispenser.Dispense(NoteCounts.None));
        Assert.False(dispenser.Dispense(N(0, 1, 0)));
        Assert.False(dispenser.Dispense(N(2, 0, 0)));
        Assert.False(dispenser.Dispense(N(0, 0, 4)));
        Assert.Equal(N(1, 2, 3), dispenser.Stock);
        Assert.Equal(Denomination.Twenty, dispenser.Jammed);
        Assert.True(dispenser.Dispense(N(1, 0, 3)));
        Assert.Equal(N(0, 2, 0), dispenser.Stock);
    }

    [Fact]
    public void Real_dispenser_load_is_all_or_nothing()
    {
        var dispenser = new MoneyDispenser(N(5, 0, 4), Denomination.Ten);

        Assert.False(dispenser.Load(NoteCounts.None));
        Assert.False(dispenser.Load(N(1, 1, 0)));   // cassette de 50 pleine
        Assert.False(dispenser.Load(N(0, 1, 1)));   // cassette de 10 bloquée
        Assert.Equal(N(5, 0, 4), dispenser.Stock);
        Assert.True(dispenser.Load(N(0, 5, 0)));
        Assert.Equal(N(5, 5, 4), dispenser.Stock);
    }

    // ---- Module dont l'état se modifie de l'extérieur ---------------------------------------------

    private sealed class MutableDispenser : IMoneyDispenser
    {
        public NoteCounts Stock { get; set; }

        public Denomination? Jammed { get; set; }

        public bool Dispense(NoteCounts notes)
        {
            var real = new MoneyDispenser(Stock, Jammed);
            if (!real.Dispense(notes)) return false;
            Stock = real.Stock;
            return true;
        }

        public bool Load(NoteCounts notes)
        {
            var real = new MoneyDispenser(Stock, Jammed);
            if (!real.Load(notes)) return false;
            Stock = real.Stock;
            return true;
        }
    }

    // ---- Doublure qui enregistre tous les appels -----------------------------------------------------

    private sealed class RecordingDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
    {
        private readonly MoneyDispenser _real = new(stock, jammed);
        private NoteCounts _permissiveStock = stock;

        /// <summary>Index de l'appel à <see cref="Dispense"/> qui échoue (une seule fois), ou -1.</summary>
        public int FailDispenseAtCall { get; init; } = -1;

        public bool FailLoad { get; init; }

        /// <summary>Accepte tout passage et tout dépôt, sans contrôle.</summary>
        public bool Permissive { get; init; }

        /// <summary>Tous les appels à Dispense, réussis ou non.</summary>
        public List<NoteCounts> DispenseCalls { get; } = [];

        /// <summary>Tous les appels à Load, réussis ou non.</summary>
        public List<NoteCounts> LoadCalls { get; } = [];

        public NoteCounts Stock => Permissive ? _permissiveStock : _real.Stock;

        public Denomination? Jammed => _real.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            var index = DispenseCalls.Count;
            DispenseCalls.Add(notes);
            if (index == FailDispenseAtCall)
            {
                return false;
            }

            if (Permissive)
            {
                _permissiveStock -= notes;
                return true;
            }

            return _real.Dispense(notes);
        }

        public bool Load(NoteCounts notes)
        {
            LoadCalls.Add(notes);
            if (FailLoad)
            {
                return false;
            }

            if (Permissive)
            {
                _permissiveStock += notes;
                return true;
            }

            return _real.Load(notes);
        }
    }
}
