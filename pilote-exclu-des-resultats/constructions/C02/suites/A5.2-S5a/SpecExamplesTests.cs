using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le tableau « Exemples » de la spec, ligne par ligne.</summary>
public class SpecExamplesTests
{
    public static TheoryData<int, int, NoteCounts, int, Operation, AtmResult, NoteCounts> Examples => new()
    {
        { 100, 0, N(1, 3, 0), 0, W(60), new(AtmStatus.Ok, N(0, 3, 0), 0, Acc(40, 60)), N(1, 0, 0) },
        { 100, 0, N(1, 1, 1), 0, W(80), new(AtmStatus.Ok, N(1, 1, 1), 0, Acc(20, 80)), N(0, 0, 0) },
        { 105, 0, N(5, 5, 5), 0, W(100), new(AtmStatus.Ok, N(2, 0, 0), 0, Acc(5, 100)), N(3, 5, 5) },
        { 115, 0, N(5, 5, 5), 0, W(110), new(AtmStatus.Ok, N(2, 0, 1), 5, Acc(0, 110)), N(3, 5, 4) },
        { 110, 0, N(5, 5, 5), 0, W(110), new(AtmStatus.InsufficientFunds, N(0, 0, 0), 0, Acc(110, 0)), N(5, 5, 5) },
        { 250, 150, N(5, 5, 5), 0, W(60), new(AtmStatus.DailyLimitExceeded, N(0, 0, 0), 0, Acc(250, 150)), N(5, 5, 5) },
        { 250, 0, N(1, 0, 0), 50, W(50), new(AtmStatus.CannotDispense, N(0, 0, 0), 0, Acc(250, 0)), N(1, 0, 0) },
        { 250, 0, N(2, 2, 0), 50, W(40), new(AtmStatus.Ok, N(0, 2, 0), 0, Acc(210, 40)), N(2, 0, 0) },
        { 0, 200, N(0, 0, 0), 0, W(5), new(AtmStatus.InvalidAmount, N(0, 0, 0), 0, Acc(0, 200)), N(0, 0, 0) },
        { 10, 0, N(0, 5, 0), 0, D(0, 1, 0), new(AtmStatus.CassetteFull, N(0, 0, 0), 0, Acc(10, 0)), N(0, 5, 0) },
        { 10, 30, N(0, 1, 0), 20, D(0, 1, 0), new(AtmStatus.CassetteJammed, N(0, 0, 0), 0, Acc(10, 30)), N(0, 1, 0) },
        { 10, 30, N(1, 1, 0), 0, D(1, 0, 1), new(AtmStatus.Ok, N(0, 0, 0), 0, Acc(70, 30)), N(2, 1, 1) },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void Spec_example(int balance, int withdrawnToday, NoteCounts stock, int jammed, Operation operation,
        AtmResult expected, NoteCounts expectedStock)
    {
        var dispenser = new MoneyDispenser(stock, Jam(jammed));

        var result = new CashMachine(dispenser).Execute(Acc(balance, withdrawnToday), operation);

        Assert.Equal(expected, result);
        Assert.Equal(expectedStock, dispenser.Stock);
    }
}
