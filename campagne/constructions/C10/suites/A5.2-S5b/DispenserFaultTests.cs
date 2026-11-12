using Atm.Tests.Support;
using static Atm.Tests.Support.Notes;

namespace Atm.Tests;

/// <summary>« Si le module rend false malgré ces contrôles, c'est une panne. »</summary>
public class DispenserFaultTests
{
    private static void AssertStoppedAtFailure(List<(NoteCounts Notes, bool Ok)> calls)
    {
        var failure = calls.FindIndex(c => !c.Ok);
        Assert.True(failure >= 0, "le module en panne n'a jamais été sollicité");
        Assert.Equal(calls.Count - 1, failure); // aucun appel après l'échec
    }

    [Theory]
    [InlineData(60, 100)]   // 20×3, un passage, sans frais
    [InlineData(110, 250)]  // 50×2 10×1, un passage, avec frais
    [InlineData(190, 250)]  // 50×3 20×2, plusieurs passages, échec dès le premier
    public void Panne_au_premier_passage_du_retrait(int amount, int balance)
    {
        var account = new AccountState(balance, 0);
        var dispenser = new FaultyDispenser(N(5, 5, 5)) { FailDispenseAtCall = 1 };

        var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(N(5, 5, 5), dispenser.Stock);
        AssertStoppedAtFailure(dispenser.DispenseCalls);
    }

    [Theory]
    [InlineData(190, 5, 5, 5, 2)]  // 50×3 20×2 : 5 billets, 2 passages au moins
    [InlineData(150, 0, 5, 5, 2)]  // 20×5 10×5 : 10 billets, 3 passages au moins
    [InlineData(150, 0, 5, 5, 3)]
    [InlineData(200, 2, 5, 0, 2)]  // 50×2 20×5 : 7 billets
    public void Panne_en_cours_de_retrait_rend_les_billets_deja_sortis(int amount, int f, int t, int n, int failingCall)
    {
        var account = new AccountState(250, 0);
        var stock = N(f, t, n);
        var dispenser = new FaultyDispenser(stock) { FailDispenseAtCall = failingCall };

        var r = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.All(dispenser.DispenseCalls, c => Assert.InRange(c.Notes.Total, 1, MoneyDispenser.MaxNotesPerPass));
        AssertStoppedAtFailure(dispenser.DispenseCalls);

        var dispensed = dispenser.DispenseCalls.Where(c => c.Ok).Aggregate(NoteCounts.None, (acc, c) => acc + c.Notes);
        Assert.Equal(failingCall - 1, dispenser.DispenseCalls.Count(c => c.Ok));
        Assert.Equal(dispensed, r.Notes);
        Assert.Equal(stock - dispensed, dispenser.Stock);
        Assert.True(r.Notes.Total > 0);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(0, 1, 1)]
    public void Panne_au_depot(int f, int t, int n)
    {
        var account = new AccountState(40, 60);
        var dispenser = new FaultyDispenser(N(1, 1, 1)) { FailLoadAtCall = 1 };

        var r = new CashMachine(dispenser).Execute(account, new Operation.Deposit(N(f, t, n)));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        AssertStoppedAtFailure(dispenser.LoadCalls);
    }

    [Fact]
    public void Panne_au_second_chargement_du_depot_laisse_Notes_vide_et_le_compte_intact()
    {
        // Si la machine range les billets en plusieurs fois, un échec tardif reste une panne.
        var account = new AccountState(40, 60);
        var dispenser = new FaultyDispenser(N(1, 1, 1)) { FailLoadAtCall = 2 };

        var r = new CashMachine(dispenser).Execute(account, new Operation.Deposit(N(1, 1, 1)));

        if (dispenser.LoadCalls.Count < 2)
        {
            // Un seul chargement : la panne ne s'est pas produite, le dépôt réussit normalement.
            Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new AccountState(120, 60)), r);
            return;
        }

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        AssertStoppedAtFailure(dispenser.LoadCalls);
    }
}
