using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le contrat du vrai module, tel que décrit par la spec.</summary>
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

    [Fact]
    public void Dispense_sort_les_billets()
    {
        var d = new MoneyDispenser(N(5, 5, 5));
        Assert.True(d.Dispense(N(2, 1, 1)));
        Assert.Equal(N(3, 4, 4), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, 5, 5, 5, null)]                 // passage vide
    [InlineData(5, 0, 0, 5, 5, 5, null)]                 // plus de 4 billets
    [InlineData(2, 2, 1, 5, 5, 5, null)]
    [InlineData(2, 0, 0, 1, 5, 5, null)]                 // plus que le stock
    [InlineData(0, 1, 0, 5, 5, 5, Denomination.Twenty)]  // cassette bloquée
    public void Dispense_refuse_sans_rien_changer(int f, int t, int n, int sf, int st, int sn, Denomination? jammed)
    {
        var d = new MoneyDispenser(N(sf, st, sn), jammed);
        Assert.False(d.Dispense(N(f, t, n)));
        Assert.Equal(N(sf, st, sn), d.Stock);
    }

    [Fact]
    public void Dispense_accepte_4_billets()
    {
        var d = new MoneyDispenser(N(5, 5, 5));
        Assert.True(d.Dispense(N(0, 0, 4)));
    }

    [Fact]
    public void Load_range_les_billets()
    {
        var d = new MoneyDispenser(N(4, 0, 2));
        Assert.True(d.Load(N(1, 1, 1)));
        Assert.Equal(N(5, 1, 3), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 1, 1, null)]               // dépôt vide
    [InlineData(1, 1, 0, 5, 0, 0, null)]               // dépasse 5 dans une cassette
    [InlineData(0, 0, 1, 1, 1, 1, Denomination.Ten)]   // cassette bloquée
    public void Load_refuse_sans_rien_changer(int f, int t, int n, int sf, int st, int sn, Denomination? jammed)
    {
        var d = new MoneyDispenser(N(sf, st, sn), jammed);
        Assert.False(d.Load(N(f, t, n)));
        Assert.Equal(N(sf, st, sn), d.Stock);
    }
}
