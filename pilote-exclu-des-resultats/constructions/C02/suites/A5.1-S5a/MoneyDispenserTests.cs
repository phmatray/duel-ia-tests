using static Atm.Tests.N;

namespace Atm.Tests;

/// <summary>Le contrat du module de billets, tel que décrit par la spec.</summary>
public class MoneyDispenserTests
{
    [Fact]
    public void Exposes_initial_stock_and_jammed_cassette()
    {
        var dispenser = new MoneyDispenser(Notes(1, 2, 3), Denomination.Twenty);

        Assert.Equal(Notes(1, 2, 3), dispenser.Stock);
        Assert.Equal(Denomination.Twenty, dispenser.Jammed);
        Assert.Null(new MoneyDispenser(Notes(1, 2, 3)).Jammed);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(4, 0, 0)]
    [InlineData(1, 2, 1)]
    [InlineData(0, 0, 4)]
    public void Dispense_removes_notes(int n50, int n20, int n10)
    {
        var dispenser = new MoneyDispenser(Full);

        Assert.True(dispenser.Dispense(Notes(n50, n20, n10)));
        Assert.Equal(Full - Notes(n50, n20, n10), dispenser.Stock);
    }

    [Theory]
    [InlineData(5, 5, 5, null, 0, 0, 0)]  // vide
    [InlineData(5, 5, 5, null, 5, 0, 0)]  // plus de 4 billets
    [InlineData(5, 5, 5, null, 2, 2, 1)]  // plus de 4 billets
    [InlineData(5, 5, 5, 20, 0, 1, 0)]    // cassette bloquée
    [InlineData(5, 5, 5, 50, 1, 1, 1)]    // touche la cassette bloquée
    [InlineData(1, 5, 5, null, 2, 0, 0)]  // plus que le stock
    [InlineData(0, 0, 0, null, 0, 0, 1)]  // plus que le stock
    public void Dispense_refuses_without_changing_anything(int s50, int s20, int s10, int? jammed, int n50, int n20, int n10)
    {
        var dispenser = new MoneyDispenser(Notes(s50, s20, s10), Jam(jammed));

        Assert.False(dispenser.Dispense(Notes(n50, n20, n10)));
        Assert.Equal(Notes(s50, s20, s10), dispenser.Stock);
    }

    [Fact]
    public void Dispense_avoiding_jammed_cassette_works()
    {
        var dispenser = new MoneyDispenser(Full, Denomination.Fifty);

        Assert.True(dispenser.Dispense(Notes(0, 2, 2)));
        Assert.Equal(Notes(5, 3, 3), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 1, 1)]
    [InlineData(4, 4, 4, 1, 1, 1)]
    [InlineData(0, 0, 0, 5, 0, 0)]
    public void Load_adds_notes(int s50, int s20, int s10, int n50, int n20, int n10)
    {
        var dispenser = new MoneyDispenser(Notes(s50, s20, s10));

        Assert.True(dispenser.Load(Notes(n50, n20, n10)));
        Assert.Equal(Notes(s50 + n50, s20 + n20, s10 + n10), dispenser.Stock);
    }

    [Theory]
    [InlineData(1, 1, 1, null, 0, 0, 0)]  // vide
    [InlineData(1, 1, 1, 10, 0, 0, 1)]    // cassette bloquée
    [InlineData(1, 1, 1, 50, 1, 1, 0)]    // touche la cassette bloquée
    [InlineData(5, 0, 0, null, 1, 0, 0)]  // dépasse 5
    [InlineData(4, 4, 5, null, 1, 1, 1)]  // dépasse 5 dans une seule cassette
    public void Load_refuses_without_changing_anything(int s50, int s20, int s10, int? jammed, int n50, int n20, int n10)
    {
        var dispenser = new MoneyDispenser(Notes(s50, s20, s10), Jam(jammed));

        Assert.False(dispenser.Load(Notes(n50, n20, n10)));
        Assert.Equal(Notes(s50, s20, s10), dispenser.Stock);
    }
}
