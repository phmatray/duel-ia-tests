namespace Atm.Tests;

public class GuardTests
{
    [Fact]
    public void NullDispenser_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CashMachine(null!));
    }

    [Fact]
    public void NullOperation_Throws()
    {
        var machine = new CashMachine(new MoneyDispenser(default));

        Assert.Throws<ArgumentNullException>(() => machine.Execute(new(0, 0), null!));
    }

    [Fact]
    public void UnknownOperation_Throws()
    {
        var machine = new CashMachine(new MoneyDispenser(default));

        Assert.Throws<ArgumentException>(() => machine.Execute(new(0, 0), new UnknownOperation()));
    }
}
