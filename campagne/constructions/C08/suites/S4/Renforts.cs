namespace Atm.Tests;

public class Renforts
{
    private static (AtmResult Result, MoneyDispenser Dispenser) Run(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        var dispenser = new MoneyDispenser(stock, jammed);
        var result = new CashMachine(dispenser).Execute(account, operation);

        return (result, dispenser);
    }

    // ----- Dépôt : capacité de cassette, borne incluse -----

    public static TheoryData<NoteCounts, NoteCounts> DepositsFillingACassetteExactly => new()
    {
        { new(4, 0, 0), new(1, 0, 0) },
        { new(0, 4, 0), new(0, 1, 0) },
        { new(0, 0, 4), new(0, 0, 1) },
        { new(4, 4, 4), new(1, 1, 1) },
    };

    [Theory]
    [MemberData(nameof(DepositsFillingACassetteExactly))]
    public void Deposit_filling_a_cassette_to_exactly_five_succeeds(NoteCounts stock, NoteCounts notes)
    {
        var (result, dispenser) = Run(new AccountState(20, 40), stock, null, new Operation.Deposit(notes));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(20 + notes.Value, 40), result.Account);
        Assert.Equal(stock + notes, dispenser.Stock);
    }

    public static TheoryData<NoteCounts, NoteCounts> DepositsOverflowingOneCassette => new()
    {
        { new(5, 0, 0), new(1, 1, 1) },
        { new(0, 5, 0), new(1, 1, 1) },
        { new(0, 0, 5), new(1, 1, 1) },
    };

    [Theory]
    [MemberData(nameof(DepositsOverflowingOneCassette))]
    public void Deposit_overflowing_any_single_cassette_is_refused_entirely(NoteCounts stock, NoteCounts notes)
    {
        var account = new AccountState(20, 40);
        var (result, dispenser) = Run(account, stock, null, new Operation.Deposit(notes));

        Assert.Equal(AtmStatus.CassetteFull, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
    }

    // ----- Dépôt : parcours exhaustif du domaine (oracle indépendant) -----

    [Fact]
    public void Deposit_matches_oracle_over_whole_domain()
    {
        Denomination?[] jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];
        AccountState[] accounts = [new(0, 0), new(125, 70), new(250, 200)];

        foreach (var account in accounts)
        foreach (var jammed in jams)
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
        for (var df = 0; df <= 1; df++)
        for (var dt = 0; dt <= 1; dt++)
        for (var dn = 0; dn <= 1; dn++)
        {
            var stock = new NoteCounts(f, t, n);
            var notes = new NoteCounts(df, dt, dn);
            var (result, dispenser) = Run(account, stock, jammed, new Operation.Deposit(notes));
            var expected = DepositOracle(stock, jammed, notes);

            Assert.Equal(expected, result.Status);
            Assert.Equal(NoteCounts.None, result.Notes);
            Assert.Equal(0, result.Fee);
            if (expected == AtmStatus.Ok)
            {
                Assert.Equal(account with { Balance = account.Balance + df * 50 + dt * 20 + dn * 10 }, result.Account);
                Assert.Equal(new NoteCounts(f + df, t + dt, n + dn), dispenser.Stock);
            }
            else
            {
                Assert.Equal(account, result.Account);
                Assert.Equal(stock, dispenser.Stock);
            }
        }
    }

    private static AtmStatus DepositOracle(NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        if (notes.Fifties + notes.Twenties + notes.Tens == 0) return AtmStatus.InvalidAmount;
        if ((jammed == Denomination.Fifty && notes.Fifties > 0)
            || (jammed == Denomination.Twenty && notes.Twenties > 0)
            || (jammed == Denomination.Ten && notes.Tens > 0))
            return AtmStatus.CassetteJammed;
        if (stock.Fifties + notes.Fifties > 5 || stock.Twenties + notes.Twenties > 5 || stock.Tens + notes.Tens > 5)
            return AtmStatus.CassetteFull;

        return AtmStatus.Ok;
    }

    // ----- Retrait : passages -----

    [Fact]
    public void Every_withdrawal_in_domain_uses_passes_of_one_to_four_notes_summing_to_the_combination()
    {
        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var n = 0; n <= 5; n++)
        for (var amount = 10; amount <= 200; amount += 10)
        {
            var dispenser = new RecordingDispenser(new MoneyDispenser(new NoteCounts(f, t, n)));
            var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(amount));

            if (result.Status != AtmStatus.Ok)
            {
                Assert.Empty(dispenser.Passes);
                continue;
            }

            Assert.All(dispenser.Passes, pass => Assert.InRange(pass.Total, 1, 4));
            Assert.Equal(result.Notes, dispenser.Passes.Aggregate(NoteCounts.None, (sum, pass) => sum + pass));
            Assert.Equal(amount, result.Notes.Value);
        }
    }

    // ----- Pannes : arrêt immédiat -----

    [Fact]
    public void Dispenser_fault_stops_immediately_without_further_passes()
    {
        var dispenser = new FailingDispenser(new NoteCounts(3, 2, 1));
        var account = new AccountState(250, 0);
        var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(1, dispenser.DispenseCalls);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
    }

    // ----- Doubles de test -----

    private sealed class RecordingDispenser(IMoneyDispenser inner) : IMoneyDispenser
    {
        public List<NoteCounts> Passes { get; } = [];
        public NoteCounts Stock => inner.Stock;
        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            Passes.Add(notes);
            return inner.Dispense(notes);
        }

        public bool Load(NoteCounts notes) => inner.Load(notes);
    }

    private sealed class FailingDispenser(NoteCounts stock) : IMoneyDispenser
    {
        public int DispenseCalls { get; private set; }
        public NoteCounts Stock { get; } = stock;
        public Denomination? Jammed => null;

        public bool Dispense(NoteCounts notes)
        {
            DispenseCalls++;
            return false;
        }

        public bool Load(NoteCounts notes) => false;
    }
}
