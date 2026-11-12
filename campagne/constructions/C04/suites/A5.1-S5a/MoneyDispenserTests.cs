using static Atm.Tests.TestKit;

namespace Atm.Tests;

/// <summary>Le contrat du vrai module, tel que décrit par la spec (le module est fourni).</summary>
public class MoneyDispenserTests
{
    [Fact]
    public void Dispense_removes_the_notes_from_stock()
    {
        var d = new MoneyDispenser(N(2, 2, 2));
        Assert.True(d.Dispense(N(1, 2, 1)));
        Assert.Equal(N(1, 0, 1), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0)]   // passage vide
    [InlineData(2, 2, 1)]   // plus de 4 billets
    [InlineData(3, 0, 0)]   // plus que le stock
    public void Dispense_refuses_without_changing_anything(int f, int t, int d)
    {
        var dispenser = new MoneyDispenser(N(2, 5, 5));
        Assert.False(dispenser.Dispense(N(f, t, d)));
        Assert.Equal(N(2, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Dispense_refuses_the_jammed_cassette()
    {
        var dispenser = new MoneyDispenser(N(2, 2, 2), Denomination.Twenty);
        Assert.False(dispenser.Dispense(N(0, 1, 0)));
        Assert.True(dispenser.Dispense(N(1, 0, 1)));
        Assert.Equal(N(1, 2, 1), dispenser.Stock);
    }

    [Fact]
    public void Load_adds_notes_up_to_5_per_cassette()
    {
        var dispenser = new MoneyDispenser(N(4, 0, 3));
        Assert.True(dispenser.Load(N(1, 5, 2)));
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, null)]  // dépôt vide
    [InlineData(0, 0, 2, null)]  // dépasse 5 billets
    [InlineData(1, 0, 0, 50)]    // cassette bloquée
    public void Load_refuses_without_changing_anything(int f, int t, int d, int? jammed)
    {
        var dispenser = new MoneyDispenser(N(1, 1, 4), jammed is { } j ? ParseJammed(j) : null);
        Assert.False(dispenser.Load(N(f, t, d)));
        Assert.Equal(N(1, 1, 4), dispenser.Stock);
    }
}
