using static Atm.Tests.N;

namespace Atm.Tests;

/// <summary>Le contrat du vrai module, tel que décrit par la spec (sert d'appui aux tests de la machine).</summary>
public class MoneyDispenserTests
{
    [Fact]
    public void Dispense_removes_notes_from_stock()
    {
        var d = new MoneyDispenser(C(2, 2, 2));
        Assert.True(d.Dispense(C(1, 2, 1)));
        Assert.Equal(C(1, 0, 1), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, null)]                 // passage vide
    [InlineData(2, 2, 1, null)]                 // 5 billets
    [InlineData(1, 0, 0, Denomination.Fifty)]   // cassette bloquée
    [InlineData(0, 3, 0, null)]                 // plus que le stock
    public void Dispense_refuses_without_changing_anything(int f, int t, int ten, Denomination? jam)
    {
        var d = new MoneyDispenser(C(2, 2, 2), jam);
        Assert.False(d.Dispense(C(f, t, ten)));
        Assert.Equal(C(2, 2, 2), d.Stock);
    }

    [Fact]
    public void Load_adds_notes_to_stock()
    {
        var d = new MoneyDispenser(C(4, 0, 2));
        Assert.True(d.Load(C(1, 1, 1)));
        Assert.Equal(C(5, 1, 3), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, null)]
    [InlineData(0, 1, 0, Denomination.Twenty)]
    [InlineData(1, 1, 1, null)]   // la cassette de 10 déborderait
    public void Load_refuses_without_changing_anything(int f, int t, int ten, Denomination? jam)
    {
        var d = new MoneyDispenser(C(1, 1, 5), jam);
        Assert.False(d.Load(C(f, t, ten)));
        Assert.Equal(C(1, 1, 5), d.Stock);
    }
}
