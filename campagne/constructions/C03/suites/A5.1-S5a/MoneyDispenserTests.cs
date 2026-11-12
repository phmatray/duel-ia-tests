namespace Atm.Tests;

/// <summary>Le contrat du module fourni, tel que la spec le décrit (la machine s'appuie dessus).</summary>
public class MoneyDispenserTests
{
    [Fact]
    public void Expose_stock_et_cassette_bloquee()
    {
        var d = new MoneyDispenser(new NoteCounts(1, 2, 3), Denomination.Twenty);
        Assert.Equal(new NoteCounts(1, 2, 3), d.Stock);
        Assert.Equal(Denomination.Twenty, d.Jammed);
        Assert.Null(new MoneyDispenser(NoteCounts.None).Jammed);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 2, 2)]
    [InlineData(4, 0, 0)]
    [InlineData(1, 1, 2)]
    public void Dispense_valide_retire_les_billets(int f, int t, int d)
    {
        var m = new MoneyDispenser(new NoteCounts(5, 5, 5));
        Assert.True(m.Dispense(new NoteCounts(f, t, d)));
        Assert.Equal(new NoteCounts(5 - f, 5 - t, 5 - d), m.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, null)]               // vide
    [InlineData(5, 0, 0, null)]               // plus de 4 billets
    [InlineData(2, 2, 1, null)]
    [InlineData(0, 1, 0, Denomination.Twenty)] // cassette bloquée
    [InlineData(0, 0, 4, null)]               // plus que le stock (3 billets de 10)
    public void Dispense_refuse_sans_rien_changer(int f, int t, int d, Denomination? jammed)
    {
        var stock = new NoteCounts(5, 5, 3);
        var m = new MoneyDispenser(stock, jammed);
        Assert.False(m.Dispense(new NoteCounts(f, t, d)));
        Assert.Equal(stock, m.Stock);
    }

    [Fact]
    public void Load_valide_range_les_billets()
    {
        var m = new MoneyDispenser(new NoteCounts(4, 4, 4));
        Assert.True(m.Load(new NoteCounts(1, 1, 1)));
        Assert.Equal(new NoteCounts(5, 5, 5), m.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, null)]
    [InlineData(0, 0, 1, Denomination.Ten)]
    [InlineData(1, 1, 1, null)] // les 50 dépasseraient 5
    public void Load_refuse_sans_rien_changer(int f, int t, int d, Denomination? jammed)
    {
        var stock = new NoteCounts(5, 1, 1);
        var m = new MoneyDispenser(stock, jammed);
        Assert.False(m.Load(new NoteCounts(f, t, d)));
        Assert.Equal(stock, m.Stock);
    }
}
