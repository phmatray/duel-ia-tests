using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le contrat du vrai module, tel que la spec le décrit (la machine s'appuie dessus).</summary>
public class MoneyDispenserTests
{
    [Fact]
    public void Expose_le_stock_et_la_cassette_bloquee()
    {
        var dispenser = new MoneyDispenser(Notes(1, 2, 3), Denomination.Twenty);

        Assert.Equal(Notes(1, 2, 3), dispenser.Stock);
        Assert.Equal(Denomination.Twenty, dispenser.Jammed);
        Assert.Null(new MoneyDispenser(Notes(1, 2, 3)).Jammed);
    }

    [Fact]
    public void Dispense_sort_les_billets()
    {
        var dispenser = new MoneyDispenser(Notes(5, 5, 5));

        Assert.True(dispenser.Dispense(Notes(2, 1, 1)));
        Assert.Equal(Notes(3, 4, 4), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, null)]                    // passage vide
    [InlineData(2, 2, 1, null)]                    // plus de 4 billets
    [InlineData(5, 0, 0, null)]
    [InlineData(1, 0, 0, Denomination.Fifty)]      // cassette bloquée
    [InlineData(0, 1, 1, Denomination.Ten)]
    [InlineData(0, 0, 4, null)]                    // plus que le stock (10×3)
    public void Dispense_refuse_sans_rien_changer(int f, int t, int n, Denomination? jammed)
    {
        var dispenser = new MoneyDispenser(Notes(5, 5, 3), jammed);

        Assert.False(dispenser.Dispense(Notes(f, t, n)));
        Assert.Equal(Notes(5, 5, 3), dispenser.Stock);
    }

    [Fact]
    public void Load_range_les_billets()
    {
        var dispenser = new MoneyDispenser(Notes(4, 0, 2));

        Assert.True(dispenser.Load(Notes(1, 1, 1)));
        Assert.Equal(Notes(5, 1, 3), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, null)]                    // dépôt vide
    [InlineData(0, 1, 0, Denomination.Twenty)]     // cassette bloquée
    [InlineData(1, 0, 0, null)]                    // dépasse 5 dans la cassette de 50
    [InlineData(0, 1, 1, null)]                    // dépasse 5 dans la cassette de 10
    public void Load_refuse_sans_rien_changer(int f, int t, int n, Denomination? jammed)
    {
        var dispenser = new MoneyDispenser(Notes(5, 2, 5), jammed);

        Assert.False(dispenser.Load(Notes(f, t, n)));
        Assert.Equal(Notes(5, 2, 5), dispenser.Stock);
    }
}
