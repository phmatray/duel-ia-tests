using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le contrat du vrai module, tel que décrit dans la spec (§ Le module de billets).</summary>
public class MoneyDispenserContractTests
{
    [Theory]
    [InlineData(0, 0, 0)]  // vide
    [InlineData(4, 1, 0)]  // 5 billets > 4
    [InlineData(2, 2, 1)]
    [InlineData(0, 0, 3)]  // plus que le stock (10×2)
    public void Dispense_refuses_and_changes_nothing(int n50, int n20, int n10)
    {
        var dispenser = new MoneyDispenser(N(5, 5, 2));

        Assert.False(dispenser.Dispense(N(n50, n20, n10)));
        Assert.Equal(N(5, 5, 2), dispenser.Stock);
    }

    [Fact]
    public void Dispense_refuses_jammed_cassette()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5), Denomination.Twenty);

        Assert.False(dispenser.Dispense(N(1, 1, 0)));
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Dispense_removes_up_to_4_notes()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5), Denomination.Twenty);

        Assert.True(dispenser.Dispense(N(2, 0, 2)));
        Assert.Equal(N(3, 5, 3), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]  // vide
    [InlineData(0, 1, 0, 20)] // cassette bloquée
    [InlineData(2, 0, 0, 0)]  // 50 : 4+2 > 5
    [InlineData(0, 0, 1, 0)]  // 10 : 5+1 > 5
    public void Load_refuses_and_changes_nothing(int n50, int n20, int n10, int jam)
    {
        var dispenser = new MoneyDispenser(N(4, 0, 5), Jam(jam));

        Assert.False(dispenser.Load(N(n50, n20, n10)));
        Assert.Equal(N(4, 0, 5), dispenser.Stock);
    }

    [Fact]
    public void Load_adds_notes()
    {
        var dispenser = new MoneyDispenser(N(4, 0, 4), Denomination.Twenty);

        Assert.True(dispenser.Load(N(1, 0, 1)));
        Assert.Equal(N(5, 0, 5), dispenser.Stock);
        Assert.Equal(Denomination.Twenty, dispenser.Jammed);
    }
}
