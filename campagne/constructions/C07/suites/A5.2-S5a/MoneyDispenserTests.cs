using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le contrat du vrai module, tel que la spec le décrit (sur lui reposent les autres tests).</summary>
public class MoneyDispenserTests
{
    [Fact]
    public void Dispense_sort_les_billets_en_un_passage()
    {
        var d = new MoneyDispenser(N(5, 5, 5));
        Assert.True(d.Dispense(N(2, 1, 1)));
        Assert.Equal(N(3, 4, 4), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0)] // vide
    [InlineData(5, 0, 0)] // plus de 4 billets
    [InlineData(2, 2, 1)]
    [InlineData(0, 0, 4)] // plus que le stock de 10 (3)
    public void Dispense_refuse_sans_rien_changer(int f, int t, int d)
    {
        var dispenser = new MoneyDispenser(N(5, 5, 3));
        Assert.False(dispenser.Dispense(N(f, t, d)));
        Assert.Equal(N(5, 5, 3), dispenser.Stock);
    }

    [Fact]
    public void Dispense_refuse_la_cassette_bloquee()
    {
        var d = new MoneyDispenser(N(5, 5, 5), Denomination.Twenty);
        Assert.False(d.Dispense(N(1, 1, 0)));
        Assert.Equal(N(5, 5, 5), d.Stock);
        Assert.True(d.Dispense(N(1, 0, 1)));
    }

    [Fact]
    public void Load_range_les_billets()
    {
        var d = new MoneyDispenser(N(4, 0, 2));
        Assert.True(d.Load(N(1, 1, 1)));
        Assert.Equal(N(5, 1, 3), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, null)] // vide
    [InlineData(1, 1, 1, 50)]   // cassette bloquée
    [InlineData(0, 1, 1, 10)]
    [InlineData(1, 0, 0, null)] // la cassette de 50 dépasserait 5
    public void Load_refuse_sans_rien_changer(int f, int t, int d, int? jam)
    {
        var dispenser = new MoneyDispenser(N(5, 2, 2), Jam(jam ?? 0));
        Assert.False(dispenser.Load(N(f, t, d)));
        Assert.Equal(N(5, 2, 2), dispenser.Stock);
    }
}
