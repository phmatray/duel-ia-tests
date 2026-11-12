namespace Atm.Tests;

/// <summary>Renforts de la suite, guidés par les mutants survivants de Stryker.NET.</summary>
public class Renforts
{
    [Fact]
    public void Execute_UnknownOperation_ThrowsWithMessageAndParameterName()
    {
        var machine = new CashMachine(new MoneyDispenser(new NoteCounts(5, 5, 5)));

        var exception = Assert.Throws<ArgumentException>(() => machine.Execute(new AccountState(0, 0), new UnknownOperation()));

        Assert.Equal("operation", exception.ParamName);
        Assert.StartsWith("Opération inconnue.", exception.Message);
    }
}
