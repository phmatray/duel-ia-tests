using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le contrat du vrai module, tel que décrit dans la spec (le module est la référence du domaine).</summary>
public class MoneyDispenserContractTests
{
    [Fact]
    public void Dispense_retire_les_billets_du_stock()
    {
        var d = new MoneyDispenser(N(2, 2, 2));

        Assert.True(d.Dispense(N(1, 2, 1)));
        Assert.Equal(N(1, 0, 1), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0)]   // passage vide
    [InlineData(2, 2, 1)]   // 5 billets
    [InlineData(3, 0, 0)]   // plus que le stock
    [InlineData(0, 1, 0)]   // cassette bloquée
    public void Dispense_refuse_sans_rien_changer(int f, int t, int n)
    {
        var d = new MoneyDispenser(N(2, 5, 5), Denomination.Twenty);

        Assert.False(d.Dispense(N(f, t, n)));
        Assert.Equal(N(2, 5, 5), d.Stock);
    }

    [Fact]
    public void Dispense_accepte_4_billets()
    {
        var d = new MoneyDispenser(N(5, 5, 5));

        Assert.True(d.Dispense(N(2, 1, 1)));
    }

    [Theory]
    [InlineData(0, 0, 0)]   // dépôt vide
    [InlineData(0, 0, 1)]   // cassette bloquée
    [InlineData(1, 0, 0)]   // dépasserait 5
    public void Load_refuse_sans_rien_changer(int f, int t, int n)
    {
        var d = new MoneyDispenser(N(5, 0, 0), Denomination.Ten);

        Assert.False(d.Load(N(f, t, n)));
        Assert.Equal(N(5, 0, 0), d.Stock);
    }

    [Fact]
    public void Load_range_les_billets_jusqu_a_5_par_cassette()
    {
        var d = new MoneyDispenser(N(4, 0, 3));

        Assert.True(d.Load(N(1, 5, 2)));
        Assert.Equal(N(5, 5, 5), d.Stock);
    }
}
