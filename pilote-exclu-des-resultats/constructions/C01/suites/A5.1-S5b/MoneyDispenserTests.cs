namespace Atm.Tests;

/// <summary>Le contrat du module de billets, tel que décrit dans la spec.</summary>
public class MoneyDispenserTests
{
    [Fact]
    public void Exposes_its_stock_and_jammed_cassette()
    {
        var dispenser = new MoneyDispenser(Notes(1, 2, 3), Denomination.Twenty);

        Assert.Equal(Notes(1, 2, 3), dispenser.Stock);
        Assert.Equal(Denomination.Twenty, dispenser.Jammed);
        Assert.Null(new MoneyDispenser(Notes(0, 0, 0)).Jammed);
    }

    [Theory]
    [InlineData(4, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(0, 2, 2)]
    [InlineData(0, 0, 1)]
    public void Dispense_removes_up_to_4_notes(int fifties, int twenties, int tens)
    {
        var dispenser = new MoneyDispenser(Notes(5, 5, 5));

        Assert.True(dispenser.Dispense(Notes(fifties, twenties, tens)));
        Assert.Equal(Notes(5 - fifties, 5 - twenties, 5 - tens), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0)] // vide
    [InlineData(5, 0, 0)] // plus de 4 billets
    [InlineData(2, 2, 1)]
    public void Dispense_refuses_empty_or_more_than_4_notes(int fifties, int twenties, int tens)
    {
        var dispenser = new MoneyDispenser(Notes(5, 5, 5));

        Assert.False(dispenser.Dispense(Notes(fifties, twenties, tens)));
        Assert.Equal(Notes(5, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Dispense_refuses_more_than_the_stock()
    {
        var dispenser = new MoneyDispenser(Notes(1, 0, 3));

        Assert.False(dispenser.Dispense(Notes(2, 0, 0)));
        Assert.False(dispenser.Dispense(Notes(0, 1, 0)));
        Assert.Equal(Notes(1, 0, 3), dispenser.Stock);
    }

    [Fact]
    public void Dispense_refuses_to_touch_the_jammed_cassette()
    {
        var dispenser = new MoneyDispenser(Notes(2, 2, 2), Denomination.Ten);

        Assert.False(dispenser.Dispense(Notes(1, 0, 1)));
        Assert.Equal(Notes(2, 2, 2), dispenser.Stock);
        Assert.True(dispenser.Dispense(Notes(1, 1, 0)));
        Assert.Equal(Notes(1, 1, 2), dispenser.Stock);
    }

    [Fact]
    public void Load_adds_notes_up_to_5_per_cassette()
    {
        var dispenser = new MoneyDispenser(Notes(4, 0, 3));

        Assert.True(dispenser.Load(Notes(1, 1, 1)));
        Assert.Equal(Notes(5, 1, 4), dispenser.Stock);
    }

    [Fact]
    public void Load_refuses_empty_deposit()
    {
        var dispenser = new MoneyDispenser(Notes(1, 1, 1));

        Assert.False(dispenser.Load(NoteCounts.None));
        Assert.Equal(Notes(1, 1, 1), dispenser.Stock);
    }

    [Fact]
    public void Load_refuses_more_than_5_in_a_cassette_all_or_nothing()
    {
        var dispenser = new MoneyDispenser(Notes(0, 5, 0));

        Assert.False(dispenser.Load(Notes(1, 1, 1)));
        Assert.Equal(Notes(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void Load_refuses_to_touch_the_jammed_cassette_all_or_nothing()
    {
        var dispenser = new MoneyDispenser(Notes(0, 0, 0), Denomination.Fifty);

        Assert.False(dispenser.Load(Notes(1, 1, 0)));
        Assert.Equal(Notes(0, 0, 0), dispenser.Stock);
    }
}
