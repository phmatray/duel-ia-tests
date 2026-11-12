namespace Atm.Tests;

public class Renforts
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    // ---- Arguments ----

    private sealed record UnknownOperation : Operation;

    [Fact]
    public void Execute_rejects_unknown_operation_with_its_type_name()
    {
        var machine = new CashMachine(new MoneyDispenser(N(5, 5, 5)));

        var error = Assert.Throws<ArgumentException>(() => machine.Execute(new AccountState(100, 0), new UnknownOperation()));

        Assert.IsType<ArgumentException>(error);
        Assert.Equal("operation", error.ParamName);
        Assert.Contains(nameof(UnknownOperation), error.Message);
    }

    [Fact]
    public void Null_arguments_name_the_faulty_parameter()
    {
        var ctor = Assert.Throws<ArgumentNullException>(() => new CashMachine(null!));
        Assert.Equal("dispenser", ctor.ParamName);

        var execute = Assert.Throws<ArgumentNullException>(() =>
            new CashMachine(new MoneyDispenser(N(5, 5, 5))).Execute(new AccountState(0, 0), null!));
        Assert.Equal("operation", execute.ParamName);
    }

    // ---- Pannes : on s'arrête aussitôt ----

    [Fact]
    public void Withdraw_fault_stops_immediately_without_further_passes()
    {
        // 200 € avec le stock 50×1 20×5 10×5 : il faut tout sortir, 11 billets, donc trois passages.
        var dispenser = new FaultyDispenser(N(1, 5, 5), failOnDispenseCall: 2);
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(2, dispenser.DispenseCalls);
        Assert.Equal(dispenser.Dispensed, result.Notes);
        Assert.InRange(result.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
    }

    [Fact]
    public void Deposit_fault_calls_load_once_and_moves_nothing()
    {
        var dispenser = new FaultyDispenser(N(1, 1, 1), failOnDispenseCall: 0, failLoad: true);
        var result = new CashMachine(dispenser).Execute(new AccountState(10, 20), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new AccountState(10, 20)), result);
        Assert.Equal(1, dispenser.LoadCalls);
        Assert.Equal(0, dispenser.DispenseCalls);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }

    // ---- Refus : aucun appel au module ----

    [Theory]
    [InlineData(250, 0, 15)]   // InvalidAmount
    [InlineData(250, 150, 60)] // DailyLimitExceeded
    [InlineData(110, 0, 110)]  // InsufficientFunds
    public void Refused_withdraw_never_calls_the_dispenser(int balance, int withdrawn, int amount)
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5), failOnDispenseCall: 0);
        new CashMachine(dispenser).Execute(new AccountState(balance, withdrawn), new Operation.Withdraw(amount));

        Assert.Equal(0, dispenser.DispenseCalls);
        Assert.Equal(0, dispenser.LoadCalls);
    }

    // ---- Doublure ----

    private sealed class FaultyDispenser(NoteCounts stock, int failOnDispenseCall, bool failLoad = false) : IMoneyDispenser
    {
        public NoteCounts Stock { get; private set; } = stock;

        public Denomination? Jammed => null;

        public int DispenseCalls { get; private set; }

        public int LoadCalls { get; private set; }

        public NoteCounts Dispensed { get; private set; }

        public bool Dispense(NoteCounts notes)
        {
            DispenseCalls++;
            if (DispenseCalls == failOnDispenseCall)
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
