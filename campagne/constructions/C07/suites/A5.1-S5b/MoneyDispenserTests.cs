using static Atm.Tests.Support.N;

namespace Atm.Tests;

/// <summary>Le contrat du module de billets, tel que décrit par la spec.</summary>
public class MoneyDispenserTests
{
    [Fact]
    public void Expose_son_stock_et_sa_cassette_bloquee()
    {
        var dispenser = new MoneyDispenser(Notes(1, 2, 3), Denomination.Ten);

        Assert.Equal(Notes(1, 2, 3), dispenser.Stock);
        Assert.Equal(Denomination.Ten, dispenser.Jammed);
        Assert.Null(new MoneyDispenser(Notes(1, 2, 3)).Jammed);
    }

    [Fact]
    public void Dispense_sort_les_billets_d_un_coup()
    {
        var dispenser = new MoneyDispenser(Notes(5, 5, 5));

        Assert.True(dispenser.Dispense(Notes(2, 1, 1)));
        Assert.Equal(Notes(3, 4, 4), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0)]   // passage vide
    [InlineData(2, 2, 1)]   // plus de 4 billets
    [InlineData(0, 0, 5)]
    [InlineData(0, 3, 0)]   // plus que le stock
    [InlineData(0, 0, 1)]   // cassette bloquée
    public void Dispense_refuse_sans_rien_changer(int f, int t, int n)
    {
        var dispenser = new MoneyDispenser(Notes(5, 2, 5), Denomination.Ten);

        Assert.False(dispenser.Dispense(Notes(f, t, n)));
        Assert.Equal(Notes(5, 2, 5), dispenser.Stock);
    }

    [Fact]
    public void Load_range_les_billets()
    {
        var dispenser = new MoneyDispenser(Notes(4, 0, 2));

        Assert.True(dispenser.Load(Notes(1, 1, 1)));
        Assert.Equal(Notes(5, 1, 3), dispenser.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0)]   // dépôt vide
    [InlineData(1, 1, 0)]   // dépasse 5 dans la cassette de 50
    [InlineData(0, 1, 1)]   // cassette bloquée
    public void Load_refuse_sans_rien_changer(int f, int t, int n)
    {
        var dispenser = new MoneyDispenser(Notes(5, 0, 0), Denomination.Ten);

        Assert.False(dispenser.Load(Notes(f, t, n)));
        Assert.Equal(Notes(5, 0, 0), dispenser.Stock);
    }
}
