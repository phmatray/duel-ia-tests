namespace Atm.Tests;

public class Renforts
{
    [Fact]
    public void Unknown_operation_exception_names_the_parameter_and_explains_why()
    {
        var machine = new CashMachine(new MoneyDispenser(new NoteCounts(5, 5, 5)));

        var ex = Assert.Throws<ArgumentException>(() => machine.Execute(new AccountState(0, 0), new UnknownOperation()));

        Assert.Equal("operation", ex.ParamName);
        // Le message doit expliquer le refus, pas se réduire au seul suffixe « (Parameter 'operation') ».
        var explanation = ex.Message.Replace("(Parameter 'operation')", "").Trim();
        Assert.NotEmpty(explanation);
    }

    private sealed record UnknownOperation : Operation;
}
