using static Atm.Tests.Notes;

namespace Atm.Tests;

/// <summary>Le contrat du module de billets, tel que décrit par la spec.</summary>
public class MoneyDispenserTests
{
    [Fact]
    public void Expose_stock_et_cassette_bloquee()
    {
        var d = new MoneyDispenser(N(1, 2, 3), Denomination.Twenty);
        Assert.Equal(N(1, 2, 3), d.Stock);
        Assert.Equal(Denomination.Twenty, d.Jammed);
        Assert.Null(new MoneyDispenser(N(0, 0, 0)).Jammed);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 1, 1)]
    [InlineData(0, 0, 4)]
    public void Dispense_sort_les_billets(int f, int t, int d)
    {
        var m = new MoneyDispenser(N(5, 5, 5));
        Assert.True(m.Dispense(N(f, t, d)));
        Assert.Equal(N(5, 5, 5) - N(f, t, d), m.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, null)]            // passage vide
    [InlineData(2, 2, 1, null)]            // 5 billets
    [InlineData(0, 0, 5, null)]
    [InlineData(1, 0, 0, Denomination.Fifty)]
    [InlineData(0, 1, 1, Denomination.Ten)]
    [InlineData(0, 3, 0, null)]            // plus que le stock de 20 (2)
    public void Dispense_refuse_sans_rien_changer(int f, int t, int d, Denomination? jammed)
    {
        var stock = N(5, 2, 5);
        var m = new MoneyDispenser(stock, jammed);
        Assert.False(m.Dispense(N(f, t, d)));
        Assert.Equal(stock, m.Stock);
    }

    [Fact]
    public void Load_range_les_billets()
    {
        var m = new MoneyDispenser(N(4, 0, 2));
        Assert.True(m.Load(N(1, 1, 1)));
        Assert.Equal(N(5, 1, 3), m.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, null)]
    [InlineData(0, 1, 0, Denomination.Twenty)]
    [InlineData(1, 0, 0, null)]            // cassette des 50 déjà à 5
    [InlineData(0, 1, 1, null)]            // les 20 passeraient, mais la cassette des 10 déborde : tout ou rien
    public void Load_refuse_sans_rien_changer(int f, int t, int d, Denomination? jammed)
    {
        var stock = N(5, 1, 5);
        var m = new MoneyDispenser(stock, jammed);
        Assert.False(m.Load(N(f, t, d)));
        Assert.Equal(stock, m.Stock);
    }
}
