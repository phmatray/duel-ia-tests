using static Atm.Tests.Helpers;

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
        Assert.Null(new MoneyDispenser(N(1, 2, 3)).Jammed);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 1, 1)]
    [InlineData(0, 0, 4)]
    public void Dispense_sort_les_billets(int f, int t, int d)
    {
        var m = new MoneyDispenser(Full);

        Assert.True(m.Dispense(N(f, t, d)));
        Assert.Equal(Full - N(f, t, d), m.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]    // passage vide
    [InlineData(2, 2, 1, 0)]    // 5 billets
    [InlineData(0, 5, 0, 0)]    // 5 billets
    [InlineData(1, 0, 0, 50)]   // cassette bloquée
    [InlineData(0, 1, 1, 10)]   // cassette bloquée
    public void Dispense_refuse_sans_rien_changer(int f, int t, int d, int jammed)
    {
        var m = new MoneyDispenser(Full, J(jammed));

        Assert.False(m.Dispense(N(f, t, d)));
        Assert.Equal(Full, m.Stock);
    }

    [Fact]
    public void Dispense_refuse_plus_que_le_stock()
    {
        var m = new MoneyDispenser(N(1, 1, 1));

        Assert.False(m.Dispense(N(2, 0, 0)));
        Assert.Equal(N(1, 1, 1), m.Stock);
    }

    [Fact]
    public void Load_range_les_billets()
    {
        var m = new MoneyDispenser(N(4, 0, 3), Denomination.Twenty);

        Assert.True(m.Load(N(1, 0, 2)));
        Assert.Equal(N(5, 0, 5), m.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0, 0)]   // dépôt vide
    [InlineData(1, 1, 1, 20, 0, 1, 0)]  // cassette bloquée
    [InlineData(5, 0, 0, 0, 1, 0, 0)]   // dépasse 5
    [InlineData(4, 4, 5, 0, 1, 1, 1)]   // une cassette dépasse : tout ou rien
    public void Load_refuse_sans_rien_changer(int sf, int st, int sd, int jammed, int f, int t, int d)
    {
        var m = new MoneyDispenser(N(sf, st, sd), J(jammed));

        Assert.False(m.Load(N(f, t, d)));
        Assert.Equal(N(sf, st, sd), m.Stock);
    }
}
