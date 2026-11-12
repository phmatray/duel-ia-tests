using static Atm.Tests.TestSupport;

namespace Atm.Tests;

/// <summary>Le contrat du vrai module, tel que la spec le décrit (la machine s'appuie dessus).</summary>
public class MoneyDispenserContractTests
{
    [Fact]
    public void Dispense_sort_les_billets_demandes()
    {
        var d = new MoneyDispenser(N(2, 2, 2));
        Assert.True(d.Dispense(N(1, 2, 1)));
        Assert.Equal(N(1, 0, 1), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0)]   // passage vide
    [InlineData(2, 2, 1)]   // 5 billets
    [InlineData(3, 0, 0)]   // plus que le stock
    [InlineData(0, 0, 1)]   // cassette bloquée
    public void Dispense_refuse_sans_rien_changer(int f, int t, int n)
    {
        var d = new MoneyDispenser(N(2, 5, 5), Denomination.Ten);
        Assert.False(d.Dispense(N(f, t, n)));
        Assert.Equal(N(2, 5, 5), d.Stock);
    }

    [Fact]
    public void Load_range_les_billets()
    {
        var d = new MoneyDispenser(N(4, 0, 1));
        Assert.True(d.Load(N(1, 1, 1)));
        Assert.Equal(N(5, 1, 2), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0)]   // dépôt vide
    [InlineData(1, 0, 0)]   // cassette bloquée
    [InlineData(0, 1, 1)]   // dépasse 5 billets
    public void Load_refuse_sans_rien_changer(int f, int t, int n)
    {
        var d = new MoneyDispenser(N(0, 0, 5), Denomination.Fifty);
        Assert.False(d.Load(N(f, t, n)));
        Assert.Equal(N(0, 0, 5), d.Stock);
    }
}
