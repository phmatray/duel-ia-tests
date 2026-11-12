using static Atm.Tests.Support.Notes;

namespace Atm.Tests;

/// <summary>Le contrat du module de billets, tel que décrit dans la spec.</summary>
public class MoneyDispenserTests
{
    [Fact]
    public void Dispense_sort_les_billets_en_un_passage()
    {
        var d = new MoneyDispenser(N(2, 2, 2));

        Assert.True(d.Dispense(N(1, 2, 1)));
        Assert.Equal(N(1, 0, 1), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, null)]                 // passage vide
    [InlineData(2, 2, 1, null)]                 // 5 billets > 4
    [InlineData(0, 0, 4, Denomination.Ten)]     // cassette bloquée
    [InlineData(1, 0, 0, Denomination.Fifty)]
    [InlineData(3, 0, 0, null)]                 // plus que le stock
    public void Dispense_refuse_sans_rien_changer(int f, int t, int n, Denomination? jammed)
    {
        var d = new MoneyDispenser(N(2, 2, 5), jammed);

        Assert.False(d.Dispense(N(f, t, n)));
        Assert.Equal(N(2, 2, 5), d.Stock);
    }

    [Fact]
    public void Load_range_les_billets()
    {
        var d = new MoneyDispenser(N(4, 0, 2), Denomination.Twenty);

        Assert.True(d.Load(N(1, 0, 3)));
        Assert.Equal(N(5, 0, 5), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0, null)]                 // dépôt vide
    [InlineData(0, 1, 0, Denomination.Twenty)]  // cassette bloquée
    [InlineData(1, 1, 0, null)]                 // la cassette de 50 dépasserait 5
    public void Load_refuse_sans_rien_changer(int f, int t, int n, Denomination? jammed)
    {
        var d = new MoneyDispenser(N(5, 1, 1), jammed);

        Assert.False(d.Load(N(f, t, n)));
        Assert.Equal(N(5, 1, 1), d.Stock);
    }
}
