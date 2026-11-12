namespace Atm.Tests;

/// <summary>Tests ajoutés pour tuer les mutants qui survivaient à la suite existante.</summary>
public class Renforts
{
    [Fact]
    public void Unknown_operation_error_names_the_operation_type()
    {
        var machine = new CashMachine(new MoneyDispenser(NoteCounts.None));

        var error = Assert.Throws<ArgumentException>(() => machine.Execute(default, new UnknownOperation()));

        Assert.Equal("operation", error.ParamName);
        Assert.Contains(nameof(UnknownOperation), error.Message);
    }
}
