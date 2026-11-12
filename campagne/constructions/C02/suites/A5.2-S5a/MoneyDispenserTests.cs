using static Atm.Tests.Atm;

namespace Atm.Tests;

/// <summary>Le contrat du module de billets fourni, tel que décrit par la spécification.</summary>
public class MoneyDispenserTests
{
    [Fact]
    public void Exposes_stock_and_jammed_cassette()
    {
        var dispenser = new MoneyDispenser(Notes(1, 2, 3), Denomination.Twenty);

        Assert.Equal(Notes(1, 2, 3), dispenser.Stock);
        Assert.Equal(Denomination.Twenty, dispenser.Jammed);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 1, 1)]
    [InlineData(0, 0, 4)]
    public void Dispense_removes_notes_in_one_pass(int f, int t, int d)
    {
        var dispenser = new MoneyDispenser(Full);

        Assert.True(dispenser.Dispense(Notes(f, t, d)));
        Assert.Equal(Full - Notes(f, t, d), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, 5, 5, 5, null)]                     // passage vide
    [InlineData(2, 2, 1, 5, 5, 5, null)]                     // plus de 4 billets
    [InlineData(0, 0, 5, 5, 5, 5, null)]
    [InlineData(1, 1, 0, 5, 5, 5, Denomination.Twenty)]     // touche la cassette bloquée
    [InlineData(2, 0, 0, 1, 5, 5, null)]                     // plus que le stock
    public void Dispense_refuses_without_changing_anything(int f, int t, int d, int sf, int st, int sd, Denomination? jammed)
    {
        var stock = Notes(sf, st, sd);
        var dispenser = new MoneyDispenser(stock, jammed);

        Assert.False(dispenser.Dispense(Notes(f, t, d)));
        Assert.Equal(stock, dispenser.Stock);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(0, 0, 1)]
    public void Load_adds_notes(int f, int t, int d)
    {
        var dispenser = new MoneyDispenser(Notes(4, 4, 4));

        Assert.True(dispenser.Load(Notes(f, t, d)));
        Assert.Equal(Notes(4, 4, 4) + Notes(f, t, d), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 1, 1, null)]                // dépôt vide
    [InlineData(0, 0, 1, 1, 1, 1, Denomination.Ten)]    // touche la cassette bloquée
    [InlineData(1, 1, 0, 5, 1, 1, null)]                // dépasse 5 dans une cassette
    public void Load_refuses_without_changing_anything(int f, int t, int d, int sf, int st, int sd, Denomination? jammed)
    {
        var stock = Notes(sf, st, sd);
        var dispenser = new MoneyDispenser(stock, jammed);

        Assert.False(dispenser.Load(Notes(f, t, d)));
        Assert.Equal(stock, dispenser.Stock);
    }
}
