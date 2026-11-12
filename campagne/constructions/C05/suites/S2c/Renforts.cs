namespace Atm.Tests;

/// <summary>Tests complémentaires : pannes à chaque passage, état du module relu à chaque appel, refus sans effet sur le module.</summary>
public class Renforts
{
    private static readonly Denomination?[] Jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static IEnumerable<NoteCounts> AllStocks()
    {
        for (var f = 0; f <= 5; f++)
            for (var t = 0; t <= 5; t++)
                for (var c = 0; c <= 5; c++)
                    yield return new NoteCounts(f, t, c);
    }

    private static NoteCounts Sum(IEnumerable<NoteCounts> passes) => passes.Aggregate(NoteCounts.None, (a, b) => a + b);

    // ---------- Pannes du module au cours d'un retrait ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Dispenser_fault_reports_every_note_already_out_and_stops(int failingCall)
    {
        // 150 € sans billet de 50 : 5×20 + 5×10, soit 10 billets, donc au moins trois passages.
        var stock = new NoteCounts(0, 5, 5);
        var dispenser = new RecordingDispenser(stock) { FailDispenseOnCall = failingCall };
        var account = new AccountState(250, 0);

        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(failingCall, dispenser.DispenseCalls.Count);
        var alreadyOut = Sum(dispenser.DispenseCalls.Take(failingCall - 1));
        Assert.Equal(alreadyOut, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock - alreadyOut, dispenser.Stock);
    }

    [Fact]
    public void Dispenser_fault_on_third_pass_accumulates_the_first_two_passes()
    {
        var stock = new NoteCounts(0, 5, 5);
        var dispenser = new RecordingDispenser(stock) { FailDispenseOnCall = 3 };

        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(dispenser.DispenseCalls[0] + dispenser.DispenseCalls[1], result.Notes);
        Assert.Equal(3, dispenser.DispenseCalls.Count);
    }

    [Fact]
    public void Dispenser_fault_on_any_pass_of_any_multi_pass_withdrawal()
    {
        // Tous les retraits réussis qui demandent plusieurs passages : on fait tomber chaque passage en panne.
        int[] amounts = [.. Enumerable.Range(1, 20).Select(i => i * 10)];
        var account = new AccountState(250, 0);

        foreach (var stock in AllStocks())
        foreach (var jam in Jams)
        foreach (var amount in amounts)
        {
            var probe = new RecordingDispenser(stock, jam);
            var ok = new CashMachine(probe).Execute(account, new Operation.Withdraw(amount));
            if (ok.Status != AtmStatus.Ok || probe.DispenseCalls.Count < 2)
            {
                continue;
            }

            for (var failing = 1; failing <= probe.DispenseCalls.Count; failing++)
            {
                var dispenser = new RecordingDispenser(stock, jam) { FailDispenseOnCall = failing };
                var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

                var alreadyOut = Sum(dispenser.DispenseCalls.Take(failing - 1));
                Assert.Equal(new AtmResult(AtmStatus.DispenserFault, alreadyOut, 0, account), result);
                Assert.Equal(failing, dispenser.DispenseCalls.Count);
                Assert.Equal(stock - alreadyOut, dispenser.Stock);
            }
        }
    }

    // ---------- Le module est relu à chaque appel ----------

    [Fact]
    public void Withdraw_sees_stock_emptied_by_a_previous_withdrawal()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(1, 0, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Withdraw(50)).Status);

        var second = machine.Execute(account, new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, account), second);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void Withdraw_sees_notes_brought_by_a_previous_deposit()
    {
        var dispenser = new MoneyDispenser(NoteCounts.None);
        var machine = new CashMachine(dispenser);

        var deposit = machine.Execute(new AccountState(0, 0), new Operation.Deposit(new NoteCounts(1, 0, 0)));
        Assert.Equal(new AccountState(50, 0), deposit.Account);

        var withdraw = machine.Execute(deposit.Account, new Operation.Withdraw(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(1, 0, 0), 0, new AccountState(0, 50)), withdraw);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void Deposit_sees_cassette_filled_by_a_previous_deposit()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(0, 0, 4));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(0, 0);

        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Deposit(new NoteCounts(0, 0, 1))).Status);

        var second = machine.Execute(account, new Operation.Deposit(new NoteCounts(0, 0, 1)));
        Assert.Equal(new AtmResult(AtmStatus.CassetteFull, NoteCounts.None, 0, account), second);
        Assert.Equal(new NoteCounts(0, 0, 5), dispenser.Stock);
    }

    [Fact]
    public void Deposit_sees_room_freed_by_a_previous_withdrawal()
    {
        var dispenser = new MoneyDispenser(new NoteCounts(5, 0, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(100, 0);

        var full = machine.Execute(account, new Operation.Deposit(new NoteCounts(1, 0, 0)));
        Assert.Equal(AtmStatus.CassetteFull, full.Status);

        Assert.Equal(AtmStatus.Ok, machine.Execute(account, new Operation.Withdraw(50)).Status);

        var deposit = machine.Execute(account, new Operation.Deposit(new NoteCounts(1, 0, 0)));
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(150, 0)), deposit);
        Assert.Equal(new NoteCounts(5, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Machine_sees_stock_changed_directly_on_the_module()
    {
        var dispenser = new MoneyDispenser(NoteCounts.None);
        var machine = new CashMachine(dispenser);
        var account = new AccountState(250, 0);

        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(account, new Operation.Withdraw(30)).Status);

        Assert.True(dispenser.Load(new NoteCounts(0, 1, 1)));

        var result = machine.Execute(account, new Operation.Withdraw(30));
        Assert.Equal(new AtmResult(AtmStatus.Ok, new NoteCounts(0, 1, 1), 0, new AccountState(220, 30)), result);
    }

    // ---------- Un refus ne sollicite jamais le module ----------

    [Fact]
    public void Withdraw_refusals_never_call_the_dispenser()
    {
        int[] amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];
        AccountState[] accounts = [new(250, 0), new(115, 0), new(0, 0), new(250, 190), new(60, 150)];
        var seen = new HashSet<AtmStatus>();

        foreach (var stock in AllStocks())
        foreach (var jam in Jams)
        foreach (var amount in amounts)
        foreach (var account in accounts)
        {
            var dispenser = new RecordingDispenser(stock, jam);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));
            seen.Add(result.Status);

            Assert.Empty(dispenser.LoadCalls);
            if (result.Status != AtmStatus.Ok)
            {
                Assert.Empty(dispenser.DispenseCalls);
                Assert.Equal(stock, dispenser.Stock);
            }
            else
            {
                Assert.Equal(result.Notes, Sum(dispenser.DispenseCalls));
            }
        }

        Assert.Superset(
            new HashSet<AtmStatus>
            {
                AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.DailyLimitExceeded,
                AtmStatus.InsufficientFunds, AtmStatus.CannotDispense,
            },
            seen);
    }

    [Fact]
    public void Deposit_refusals_never_call_the_dispenser()
    {
        var account = new AccountState(55, 70);
        var seen = new HashSet<AtmStatus>();

        foreach (var stock in AllStocks())
        foreach (var jam in Jams)
        for (var mask = 0; mask < 8; mask++)
        {
            var notes = new NoteCounts(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
            var dispenser = new RecordingDispenser(stock, jam);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));
            seen.Add(result.Status);

            Assert.Empty(dispenser.DispenseCalls);
            if (result.Status != AtmStatus.Ok)
            {
                Assert.Empty(dispenser.LoadCalls);
            }
            else
            {
                Assert.Equal(notes, Sum(dispenser.LoadCalls));
            }
        }

        Assert.Superset(
            new HashSet<AtmStatus>
            {
                AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.CassetteJammed, AtmStatus.CassetteFull,
            },
            seen);
    }

    // ---------- Dépôt : le solde, quel que soit le compte ----------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(250, 200)]
    [InlineData(5, 10)]
    public void Deposit_credits_balance_and_keeps_withdrawn_today(int balance, int withdrawn)
    {
        var dispenser = new MoneyDispenser(new NoteCounts(4, 4, 4));
        var result = new CashMachine(dispenser).Execute(
            new AccountState(balance, withdrawn), new Operation.Deposit(new NoteCounts(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(balance + 80, withdrawn)), result);
        Assert.Equal(new NoteCounts(5, 5, 5), dispenser.Stock);
    }
}
