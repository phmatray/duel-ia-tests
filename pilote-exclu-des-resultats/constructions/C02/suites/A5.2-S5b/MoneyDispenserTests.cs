using static Atm.Tests.N;

namespace Atm.Tests;

/// <summary>Le contrat du module de billets tel que décrit par la spec.</summary>
public class MoneyDispenserTests
{
    [Fact]
    public void Exposes_StockAndJammed()
    {
        var d = new MoneyDispenser(Notes(1, 2, 3), Denomination.Twenty);
        Assert.Equal(Notes(1, 2, 3), d.Stock);
        Assert.Equal(Denomination.Twenty, d.Jammed);
        Assert.Null(new MoneyDispenser(Full).Jammed);
    }

    [Theory]
    [InlineData(4, 0, 0)]
    [InlineData(0, 4, 0)]
    [InlineData(1, 1, 2)]
    [InlineData(0, 0, 1)]
    public void Dispense_UpTo4Notes_RemovesThem(int f, int t, int n)
    {
        var d = new MoneyDispenser(Full);
        Assert.True(d.Dispense(Notes(f, t, n)));
        Assert.Equal(Full - Notes(f, t, n), d.Stock);
    }

    [Theory]
    [InlineData(0, 0, 0)] // vide
    [InlineData(5, 0, 0)] // plus de 4 billets
    [InlineData(2, 2, 1)]
    public void Dispense_EmptyOrMoreThan4_FailsWithoutChange(int f, int t, int n)
    {
        var d = new MoneyDispenser(Full);
        Assert.False(d.Dispense(Notes(f, t, n)));
        Assert.Equal(Full, d.Stock);
    }

    [Fact]
    public void Dispense_TouchingJammedCassette_FailsWithoutChange()
    {
        var d = new MoneyDispenser(Full, Denomination.Ten);
        Assert.False(d.Dispense(Notes(1, 0, 1)));
        Assert.Equal(Full, d.Stock);
        Assert.True(d.Dispense(Notes(1, 1, 0)));
    }

    [Fact]
    public void Dispense_MoreThanStock_FailsWithoutChange()
    {
        var d = new MoneyDispenser(Notes(1, 1, 1));
        Assert.False(d.Dispense(Notes(2, 0, 0)));
        Assert.Equal(Notes(1, 1, 1), d.Stock);
    }

    [Fact]
    public void Load_AddsNotes_UpTo5PerCassette()
    {
        var d = new MoneyDispenser(Notes(4, 0, 3));
        Assert.True(d.Load(Notes(1, 1, 1)));
        Assert.Equal(Notes(5, 1, 4), d.Stock);
    }

    [Fact]
    public void Load_Empty_FailsWithoutChange()
    {
        var d = new MoneyDispenser(Notes(1, 1, 1));
        Assert.False(d.Load(NoteCounts.None));
        Assert.Equal(Notes(1, 1, 1), d.Stock);
    }

    [Fact]
    public void Load_TouchingJammedCassette_FailsWithoutChange()
    {
        var d = new MoneyDispenser(Notes(1, 1, 1), Denomination.Fifty);
        Assert.False(d.Load(Notes(1, 1, 0)));
        Assert.Equal(Notes(1, 1, 1), d.Stock);
    }

    [Fact]
    public void Load_OverflowingACassette_FailsWithoutChange()
    {
        var d = new MoneyDispenser(Notes(0, 5, 0));
        Assert.False(d.Load(Notes(1, 1, 1)));
        Assert.Equal(Notes(0, 5, 0), d.Stock);
    }
}
