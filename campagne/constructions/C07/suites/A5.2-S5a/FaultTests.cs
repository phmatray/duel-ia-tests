using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Le module rend <c>false</c> malgré les contrôles : panne.</summary>
public class FaultTests
{
    [Fact]
    public void Panne_au_premier_passage_d_un_retrait()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5)) { FailDispenseFromCall = 1 };
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(Acc(250, 0), Withdraw(80));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(250, 0)), result);
    }

    [Fact]
    public void Panne_au_premier_passage_d_un_retrait_avec_frais()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5)) { FailDispenseFromCall = 1 };
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(Acc(250, 0), Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(250, 0)), result);
    }

    [Theory]
    [InlineData(200, 2, 5, 5)] // 50×2 20×5 : 7 billets, au moins deux passages
    [InlineData(100, 0, 5, 5)] // 20×5 : 5 billets, au moins deux passages
    [InlineData(190, 5, 5, 5)] // 50×3 20×2 : 5 billets, au moins deux passages
    public void Panne_au_second_passage_rend_les_billets_deja_sortis(int amount, int fifties, int twenties, int tens)
    {
        var dispenser = new FaultyDispenser(N(fifties, twenties, tens)) { FailDispenseFromCall = 2 };
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(Acc(250, 0), Withdraw(amount));

        Assert.Equal(AtmStatus.DispenserFault, result.Status);
        Assert.Single(dispenser.Dispensed);
        Assert.Equal(dispenser.Dispensed[0], result.Notes);
        Assert.True(result.Notes.Total > 0);
        Assert.True(result.Notes.Value < amount);
        Assert.Equal(0, result.Fee);
        Assert.Equal(Acc(250, 0), result.Account);
    }

    [Fact]
    public void Panne_on_s_arrete_aussitot()
    {
        var dispenser = new FaultyDispenser(N(2, 5, 5)) { FailDispenseFromCall = 1 };
        var machine = new CashMachine(dispenser);

        machine.Execute(Acc(250, 0), Withdraw(200));

        // Le premier passage a échoué : aucun passage n'a été accepté et aucun dépôt tenté.
        Assert.Empty(dispenser.Dispensed);
        Assert.Empty(dispenser.Loaded);
    }

    [Fact]
    public void Panne_d_un_depot()
    {
        var dispenser = new FaultyDispenser(N(1, 1, 1)) { FailLoad = true };
        var machine = new CashMachine(dispenser);

        var result = machine.Execute(Acc(10, 30), Deposit(N(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(10, 30)), result);
        Assert.Empty(dispenser.Dispensed);
    }

    [Fact]
    public void Le_vrai_module_ne_tombe_jamais_en_panne_dans_le_domaine()
    {
        // Couvert exhaustivement par DomainSweepTests ; ici un cas témoin.
        var (machine, _) = Real(N(5, 5, 5));

        var result = machine.Execute(Acc(250, 0), Withdraw(180));

        Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
    }
}
