using static Atm.Tests.Harness;

namespace Atm.Tests;

/// <summary>
/// Module de test : se comporte comme le vrai module, enregistre chaque appel,
/// et peut simuler une panne (rendre <c>false</c>) au n-ième appel.
/// </summary>
internal sealed class ScriptedDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    private readonly MoneyDispenser _real = new(stock, jammed);

    public int? FailDispenseOnCall { get; init; }
    public int? FailLoadOnCall { get; init; }

    public List<NoteCounts> DispenseCalls { get; } = [];
    public List<NoteCounts> LoadCalls { get; } = [];

    public NoteCounts Stock => _real.Stock;
    public Denomination? Jammed => _real.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        DispenseCalls.Add(notes);
        return DispenseCalls.Count != FailDispenseOnCall && _real.Dispense(notes);
    }

    public bool Load(NoteCounts notes)
    {
        LoadCalls.Add(notes);
        return LoadCalls.Count != FailLoadOnCall && _real.Load(notes);
    }
}

/// <summary>Section « Refus et pannes » de la spec : le module rend <c>false</c> malgré les contrôles.</summary>
public class DispenserFaultTests
{
    private static NoteCounts Sum(IEnumerable<NoteCounts> passes) => passes.Aggregate(NoteCounts.None, (a, b) => a + b);

    [Fact]
    public void Withdraw_PassesHaveAtMostFourNotes_AndAddUpToTheNotes()
    {
        var dispenser = new ScriptedDispenser(N(1, 5, 5));
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(1, 5, 5), result.Notes);
        Assert.All(dispenser.DispenseCalls, pass => Assert.InRange(pass.Total, 1, 4));
        Assert.Equal(result.Notes, Sum(dispenser.DispenseCalls));
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void Deposit_GoesThroughTheModule()
    {
        var dispenser = new ScriptedDispenser(N(1, 1, 1));
        var result = new CashMachine(dispenser).Execute(new AccountState(0, 0), new Operation.Deposit(N(1, 1, 1)));

        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(N(1, 1, 1), Sum(dispenser.LoadCalls));
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Theory]
    [InlineData(0, 0, 60)]          // InsufficientFunds
    [InlineData(250, 200, 10)]      // DailyLimitExceeded
    [InlineData(250, 0, 5)]         // InvalidAmount
    public void WithdrawRefusal_DoesNotCallTheModule(int balance, int withdrawnToday, int amount)
    {
        var dispenser = new ScriptedDispenser(FullStock);
        new CashMachine(dispenser).Execute(new AccountState(balance, withdrawnToday), new Operation.Withdraw(amount));

        Assert.Empty(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void Withdraw_FirstPassFails_DispenserFault_NothingOut()
    {
        var dispenser = new ScriptedDispenser(FullStock) { FailDispenseOnCall = 1 };
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(60));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
        Assert.Single(dispenser.DispenseCalls);
    }

    [Fact]
    public void Withdraw_LaterPassFails_DispenserFault_NotesAreThoseAlreadyOut_AndStops()
    {
        // 200 avec 50×1 20×5 10×5 : 11 billets, donc au moins 3 passages. Le 2e échoue.
        var dispenser = new ScriptedDispenser(N(1, 5, 5)) { FailDispenseOnCall = 2 };
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(200));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(2, dispenser.DispenseCalls.Count);              // on s'arrête aussitôt
        Assert.Equal(dispenser.DispenseCalls[0], result.Notes);       // seuls les billets du 1er passage sont sortis
        Assert.Equal(N(1, 5, 5) - result.Notes, dispenser.Stock);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
    }

    [Fact]
    public void Withdraw_WithFee_PassFails_FeeIsZero()
    {
        var dispenser = new ScriptedDispenser(N(1, 5, 5)) { FailDispenseOnCall = 1 };
        var result = new CashMachine(dispenser).Execute(new AccountState(250, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(250, 0), result.Account);
    }

    [Fact]
    public void Deposit_LoadFails_DispenserFault_NotesEmpty()
    {
        var dispenser = new ScriptedDispenser(N(1, 1, 1)) { FailLoadOnCall = 1 };
        var result = new CashMachine(dispenser).Execute(new AccountState(100, 30), new Operation.Deposit(N(1, 0, 1)));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(new AccountState(100, 30), result.Account);
        Assert.Equal(N(1, 1, 1), dispenser.Stock);
    }
}
