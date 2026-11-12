using static Atm.Tests.Harness;

namespace Atm.Tests;

public class WithdrawTests
{
    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void InvalidAmount_IsRefused(int amount)
    {
        AssertRefused(AtmStatus.InvalidAmount, Withdraw(250, 0, FullStock, amount), 250, 0, FullStock);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void InvalidAmount_ComesBeforeAllOtherChecks(int amount)
    {
        // Plafond atteint, solde nul, stock vide : le montant invalide l'emporte.
        AssertRefused(AtmStatus.InvalidAmount, Withdraw(0, 200, N(), amount), 0, 200, N());
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(200, 10)]
    [InlineData(150, 60)]
    [InlineData(110, 100)]
    public void OverDailyLimit_IsRefused(int withdrawnToday, int amount)
    {
        AssertRefused(AtmStatus.DailyLimitExceeded, Withdraw(250, withdrawnToday, FullStock, amount), 250, withdrawnToday, FullStock);
    }

    [Theory]
    [InlineData(0, 200, 205, 4, 0, 0)]
    [InlineData(150, 50, 250, 1, 0, 0)]
    [InlineData(190, 10, 250, 0, 0, 1)]
    public void ExactlyAtDailyLimit_IsAccepted(int withdrawnToday, int amount, int balance, int f, int t, int te)
    {
        var run = Withdraw(balance, withdrawnToday, FullStock, amount);
        AssertWithdrawOk(run, balance, withdrawnToday, FullStock, amount, N(f, t, te), SpecOracle.Fee(amount));
        Assert.Equal(withdrawnToday + amount, run.Result.Account.WithdrawnToday);
    }

    [Fact]
    public void Fee_DoesNotCountTowardsDailyLimit()
    {
        // 90 + 110 = 200 : pile au plafond ; les 5 € de frais n'y comptent pas.
        var run = Withdraw(250, 90, FullStock, 110);
        AssertWithdrawOk(run, 250, 90, FullStock, 110, N(2, 0, 1), 5);
        Assert.Equal(new AccountState(135, 200), run.Result.Account);
    }

    [Fact]
    public void DailyLimit_ComesBeforeInsufficientFundsAndCannotDispense()
    {
        AssertRefused(AtmStatus.DailyLimitExceeded, Withdraw(0, 200, N(), 10), 0, 200, N());
        AssertRefused(AtmStatus.DailyLimitExceeded, Withdraw(0, 150, N(), 60, Denomination.Ten), 0, 150, N());
    }

    // ---- Contrôle 3 : solde insuffisant ----

    [Theory]
    [InlineData(0, 10)]
    [InlineData(95, 100)]
    [InlineData(105, 110)]
    [InlineData(110, 110)]
    [InlineData(200, 200)]
    [InlineData(204, 200)]
    public void InsufficientFunds_IsRefused(int balance, int amount)
    {
        AssertRefused(AtmStatus.InsufficientFunds, Withdraw(balance, 0, FullStock, amount), balance, 0, FullStock);
    }

    [Theory]
    [InlineData(10, 10, 0, 0, 1)]
    [InlineData(100, 100, 2, 0, 0)]
    [InlineData(115, 110, 2, 0, 1)]
    [InlineData(205, 200, 4, 0, 0)]
    public void BalanceExactlyCoversAmountPlusFee_IsAccepted(int balance, int amount, int f, int t, int te)
    {
        var run = Withdraw(balance, 0, FullStock, amount);
        AssertWithdrawOk(run, balance, 0, FullStock, amount, N(f, t, te), SpecOracle.Fee(amount));
        Assert.Equal(0, run.Result.Account.Balance);
    }

    [Fact]
    public void InsufficientFunds_ComesBeforeCannotDispense()
    {
        AssertRefused(AtmStatus.InsufficientFunds, Withdraw(50, 0, N(), 60), 50, 0, N());
        AssertRefused(AtmStatus.InsufficientFunds, Withdraw(0, 0, N(5, 0, 0), 50, Denomination.Fifty), 0, 0, N(5, 0, 0));
    }

    // ---- Frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Fee_IsFiveOnlyAboveHundred(int amount, int expectedFee)
    {
        var run = Withdraw(250, 0, FullStock, amount);
        Assert.Equal(AtmStatus.Ok, run.Result.Status);
        Assert.Equal(expectedFee, run.Result.Fee);
        Assert.Equal(250 - amount - expectedFee, run.Result.Account.Balance);
        Assert.Equal(amount, run.Result.Account.WithdrawnToday);
    }

    // ---- Contrôle 4 : impossible à servir ----

    [Theory]
    [InlineData(10)]
    [InlineData(200)]
    public void EmptyStock_CannotDispense(int amount)
    {
        AssertRefused(AtmStatus.CannotDispense, Withdraw(250, 0, N(), amount), 250, 0, N());
    }

    [Theory]
    [InlineData(0, 5, 0, 30)]   // que des 20 : 30 impossible
    [InlineData(0, 5, 0, 10)]
    [InlineData(1, 0, 0, 60)]   // un 50 seul : 60 impossible
    [InlineData(1, 1, 1, 90)]   // 50+20+10 = 80 < 90
    [InlineData(2, 0, 0, 150)]
    [InlineData(5, 0, 0, 120)]  // que des 50
    [InlineData(1, 0, 3, 90)]   // 50+30 = 80 au mieux
    public void NoExactCombination_CannotDispense(int f, int t, int te, int amount)
    {
        var stock = N(f, t, te);
        AssertRefused(AtmStatus.CannotDispense, Withdraw(250, 0, stock, amount), 250, 0, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50)]
    [InlineData(Denomination.Twenty, 20)]
    [InlineData(Denomination.Ten, 10)]
    public void JammedCassette_CountsAsEmpty(Denomination jammed, int amount)
    {
        // Seule la cassette bloquée pourrait servir le montant.
        var stock = jammed switch
        {
            Denomination.Fifty => N(5, 0, 0),
            Denomination.Twenty => N(0, 5, 0),
            _ => N(0, 0, 5),
        };
        AssertRefused(AtmStatus.CannotDispense, Withdraw(250, 0, stock, amount, jammed), 250, 0, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 100, 0, 5, 0)]   // sans les 50 : 20×5
    [InlineData(Denomination.Fifty, 70, 0, 3, 1)]
    [InlineData(Denomination.Twenty, 80, 1, 0, 3)]   // sans les 20 : 50 + 10×3
    [InlineData(Denomination.Twenty, 40, 0, 0, 4)]
    [InlineData(Denomination.Ten, 90, 1, 2, 0)]
    [InlineData(Denomination.Ten, 60, 0, 3, 0)]      // sans les 10 : 20×3
    public void JammedCassette_IsAvoided(Denomination jammed, int amount, int f, int t, int te)
    {
        var run = Withdraw(250, 0, FullStock, amount, jammed);
        AssertWithdrawOk(run, 250, 0, FullStock, amount, N(f, t, te), SpecOracle.Fee(amount));
        Assert.Equal(0, run.Result.Notes.Of(jammed));
    }

    // ---- La combinaison : le moins de billets ----

    [Theory]
    [InlineData(10, 0, 0, 1)]
    [InlineData(20, 0, 1, 0)]
    [InlineData(30, 0, 1, 1)]
    [InlineData(40, 0, 2, 0)]
    [InlineData(50, 1, 0, 0)]
    [InlineData(60, 1, 0, 1)]
    [InlineData(70, 1, 1, 0)]
    [InlineData(80, 1, 1, 1)]
    [InlineData(90, 1, 2, 0)]
    [InlineData(100, 2, 0, 0)]
    [InlineData(110, 2, 0, 1)]
    [InlineData(120, 2, 1, 0)]
    [InlineData(130, 2, 1, 1)]
    [InlineData(140, 2, 2, 0)]
    [InlineData(150, 3, 0, 0)]
    [InlineData(160, 3, 0, 1)]
    [InlineData(170, 3, 1, 0)]
    [InlineData(180, 3, 1, 1)]
    [InlineData(190, 3, 2, 0)]
    [InlineData(200, 4, 0, 0)]
    public void FullStock_FewestNotes(int amount, int f, int t, int te)
    {
        var run = Withdraw(250, 0, FullStock, amount);
        AssertWithdrawOk(run, 250, 0, FullStock, amount, N(f, t, te), SpecOracle.Fee(amount));
    }

    [Theory]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // le glouton (50 d'abord) échouerait
    [InlineData(1, 4, 0, 80, 0, 4, 0)]
    [InlineData(1, 3, 0, 110, 1, 3, 0)]
    [InlineData(3, 5, 0, 160, 2, 3, 0)]
    [InlineData(2, 0, 5, 130, 2, 0, 3)]
    [InlineData(1, 5, 1, 130, 1, 4, 0)]  // 50+20×4 : seule combinaison possible avec un seul 10
    [InlineData(0, 2, 5, 90, 0, 2, 5)]
    [InlineData(1, 1, 5, 120, 1, 1, 5)]
    public void LimitedStock_FewestNotesAmongAvailable(int sf, int st, int ste, int amount, int f, int t, int te)
    {
        var stock = N(sf, st, ste);
        var run = Withdraw(250, 0, stock, amount);
        AssertWithdrawOk(run, 250, 0, stock, amount, N(f, t, te), SpecOracle.Fee(amount));
    }

    // ---- La sortie : plusieurs passages de 4 billets au plus ----

    [Theory]
    [InlineData(0, 5, 0, 100, 0, 5, 0)]   // 5 billets : deux passages
    [InlineData(0, 4, 1, 90, 0, 4, 1)]
    [InlineData(1, 5, 5, 200, 1, 5, 5)]   // 11 billets : trois passages
    [InlineData(0, 5, 5, 150, 0, 5, 5)]   // 10 billets
    [InlineData(2, 2, 5, 190, 2, 2, 5)]
    public void MoreThanFourNotes_AreDispensedInSeveralPasses(int sf, int st, int ste, int amount, int f, int t, int te)
    {
        var stock = N(sf, st, ste);
        var run = Withdraw(250, 0, stock, amount);
        AssertWithdrawOk(run, 250, 0, stock, amount, N(f, t, te), SpecOracle.Fee(amount));
    }

    [Fact]
    public void Withdraw_EmptiesStockCompletely_WhenNeeded()
    {
        var stock = N(1, 2, 1);
        var run = Withdraw(250, 0, stock, 100);
        AssertWithdrawOk(run, 250, 0, stock, 100, N(1, 2, 1), 0);
        Assert.Equal(N(), run.Dispenser.Stock);
    }
}
