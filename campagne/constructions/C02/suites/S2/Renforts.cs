namespace Atm.Tests;

public class Renforts
{
    [Fact]
    public void UnknownOperation_MessageNamesTheOperationType()
    {
        var machine = new CashMachine(new MoneyDispenser(default));

        var exception = Assert.Throws<ArgumentException>(() => machine.Execute(new(0, 0), new UnknownOperation()));

        Assert.Equal("operation", exception.ParamName);
        Assert.Contains(nameof(UnknownOperation), exception.Message);
    }

    [Fact]
    public void NullOperation_NamesTheParameter()
    {
        var machine = new CashMachine(new MoneyDispenser(default));

        var exception = Assert.Throws<ArgumentNullException>(() => machine.Execute(new(0, 0), null!));

        Assert.Equal("operation", exception.ParamName);
    }

    [Fact]
    public void NullDispenser_NamesTheParameter()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new CashMachine(null!));

        Assert.Equal("dispenser", exception.ParamName);
    }
}
