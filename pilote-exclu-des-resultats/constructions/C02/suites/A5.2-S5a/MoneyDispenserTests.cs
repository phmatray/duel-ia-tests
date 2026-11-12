using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le contrat du vrai module de billets, tel que décrit par la spec.</summary>
public class MoneyDispenserTests
{
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(4, 0, 0)]
    [InlineData(1, 2, 1)]
    [InlineData(0, 0, 4)]
    public void Dispense_removes_notes_in_one_pass(int f, int t, int n)
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));

        Assert.True(dispenser.Dispense(N(f, t, n)));
        Assert.Equal(N(5 - f, 5 - t, 5 - n), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]     // passage vide
    [InlineData(5, 0, 0, 0)]     // plus de 4 billets
    [InlineData(2, 2, 1, 0)]
    [InlineData(1, 0, 0, 50)]    // touche la cassette bloquée
    [InlineData(0, 1, 1, 20)]
    [InlineData(0, 0, 1, 10)]
    public void Dispense_refuses_without_changing_anything(int f, int t, int n, int jammed)
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5), Jam(jammed));

        Assert.False(dispenser.Dispense(N(f, t, n)));
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Dispense_refuses_more_than_stock()
    {
        var dispenser = new MoneyDispenser(N(1, 2, 0));

        Assert.False(dispenser.Dispense(N(1, 1, 1)));
        Assert.False(dispenser.Dispense(N(2, 0, 0)));
        Assert.Equal(N(1, 2, 0), dispenser.Stock);
    }

    [Fact]
    public void Load_adds_notes()
    {
        var dispenser = new MoneyDispenser(N(4, 0, 2), Denomination.Twenty);

        Assert.True(dispenser.Load(N(1, 0, 1)));
        Assert.Equal(N(5, 0, 3), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]   // dépôt vide
    [InlineData(1, 0, 0, 50)]  // cassette bloquée
    [InlineData(1, 1, 1, 10)]
    [InlineData(0, 2, 0, 0)]   // 4 + 2 > 5
    [InlineData(1, 1, 1, 0)]   // une cassette déborde
    public void Load_refuses_without_changing_anything(int f, int t, int n, int jammed)
    {
        var dispenser = new MoneyDispenser(N(1, 4, 5), Jam(jammed));

        Assert.False(dispenser.Load(N(f, t, n)));
        Assert.Equal(N(1, 4, 5), dispenser.Stock);
    }
}
