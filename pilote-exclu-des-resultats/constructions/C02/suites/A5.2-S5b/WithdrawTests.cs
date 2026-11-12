using static Atm.Tests.N;

namespace Atm.Tests;

public class WithdrawTests
{
    private static (AtmResult Result, SpyDispenser Dispenser) Run(
        int balance, int withdrawnToday, NoteCounts stock, Denomination? jammed, int amount)
    {
        var dispenser = SpyDispenser.Real(stock, jammed);
        var machine = new CashMachine(dispenser);
        var result = machine.Execute(Account(balance, withdrawnToday), Withdraw(amount));
        return (result, dispenser);
    }

    private static void AssertRefused(AtmResult result, AtmStatus status, AccountState account, SpyDispenser dispenser, NoteCounts stock)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(account, result.Account);
        Assert.Equal(stock, dispenser.Stock);
        Assert.Empty(dispenser.Dispenses);
        Assert.Empty(dispenser.Loads);
    }

    private static void AssertOk(AtmResult result, SpyDispenser dispenser, NoteCounts initialStock,
        NoteCounts notes, int fee, int balance, int withdrawnToday)
    {
        Assert.Equal(AtmStatus.Ok, result.Status);
        Assert.Equal(notes, result.Notes);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(Account(balance, withdrawnToday), result.Account);
        Assert.Equal(initialStock - notes, dispenser.Stock);
        Assert.Equal(notes, dispenser.TotalDispensed);
        Assert.All(dispenser.Dispenses, d => Assert.True(d.Result));
        Assert.All(dispenser.Dispenses, d => Assert.InRange(d.Notes.Total, 1, 4));
        Assert.Empty(dispenser.Loads);
    }

    // ---------- Exemples de la spec ----------

    [Fact]
    public void Example_Withdraw60_With50x1_20x3_GivesThreeTwenties()
    {
        var stock = Notes(1, 3, 0);
        var (r, d) = Run(100, 0, stock, null, 60);
        AssertOk(r, d, stock, Notes(0, 3, 0), fee: 0, balance: 40, withdrawnToday: 60);
    }

    [Fact]
    public void Example_Withdraw80_With50x1_20x1_10x1_GivesOneOfEach()
    {
        var stock = Notes(1, 1, 1);
        var (r, d) = Run(100, 0, stock, null, 80);
        AssertOk(r, d, stock, Notes(1, 1, 1), fee: 0, balance: 20, withdrawnToday: 80);
    }

    [Fact]
    public void Example_Withdraw100_NoFee()
    {
        var (r, d) = Run(105, 0, Full, null, 100);
        AssertOk(r, d, Full, Notes(2, 0, 0), fee: 0, balance: 5, withdrawnToday: 100);
    }

    [Fact]
    public void Example_Withdraw110_Fee5()
    {
        var (r, d) = Run(115, 0, Full, null, 110);
        AssertOk(r, d, Full, Notes(2, 0, 1), fee: 5, balance: 0, withdrawnToday: 110);
    }

    [Fact]
    public void Example_Withdraw110_Balance110_InsufficientFundsBecauseOfFee()
    {
        var (r, d) = Run(110, 0, Full, null, 110);
        AssertRefused(r, AtmStatus.InsufficientFunds, Account(110, 0), d, Full);
    }

    [Fact]
    public void Example_Withdraw60_AlreadyWithdrawn150_DailyLimitExceeded()
    {
        var (r, d) = Run(250, 150, Full, null, 60);
        AssertRefused(r, AtmStatus.DailyLimitExceeded, Account(250, 150), d, Full);
    }

    [Fact]
    public void Example_Withdraw50_OnlyFiftyCassetteJammed_CannotDispense()
    {
        var stock = Notes(1, 0, 0);
        var (r, d) = Run(250, 0, stock, Denomination.Fifty, 50);
        AssertRefused(r, AtmStatus.CannotDispense, Account(250, 0), d, stock);
    }

    [Fact]
    public void Example_Withdraw40_FiftyJammed_GivesTwoTwenties()
    {
        var stock = Notes(2, 2, 0);
        var (r, d) = Run(250, 0, stock, Denomination.Fifty, 40);
        AssertOk(r, d, stock, Notes(0, 2, 0), fee: 0, balance: 210, withdrawnToday: 40);
    }

    [Fact]
    public void Example_Withdraw5_InvalidAmount()
    {
        var stock = Notes(0, 0, 0);
        var (r, d) = Run(0, 200, stock, null, 5);
        AssertRefused(r, AtmStatus.InvalidAmount, Account(0, 200), d, stock);
    }

    // ---------- Contrôle 1 : montant ----------

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void InvalidAmount_WhenNegativeZeroOrNotMultipleOf10(int amount)
    {
        var (r, d) = Run(250, 0, Full, null, amount);
        AssertRefused(r, AtmStatus.InvalidAmount, Account(250, 0), d, Full);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void InvalidAmount_IsCheckedBeforeEverythingElse(int amount)
    {
        // Plafond atteint, solde nul, stock vide, cassette bloquée : le montant prime.
        var stock = Notes(0, 0, 0);
        var (r, d) = Run(0, 200, stock, Denomination.Ten, amount);
        AssertRefused(r, AtmStatus.InvalidAmount, Account(0, 200), d, stock);
    }

    // ---------- Contrôle 2 : plafond du jour ----------

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(10, 200)]
    [InlineData(190, 20)]
    [InlineData(200, 10)]
    public void DailyLimitExceeded_WhenWithdrawnTodayPlusAmountAbove200(int withdrawnToday, int amount)
    {
        var (r, d) = Run(250, withdrawnToday, Full, null, amount);
        AssertRefused(r, AtmStatus.DailyLimitExceeded, Account(250, withdrawnToday), d, Full);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    public void DailyLimit_ReachingExactly200_IsAllowed(int withdrawnToday, int amount)
    {
        var (r, _) = Run(250, withdrawnToday, Full, null, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(200, r.Account.WithdrawnToday);
    }

    [Fact]
    public void DailyLimit_DoesNotCountFee()
    {
        // 90 + 110 = 200 : pile au plafond, les 5 € de frais n'y comptent pas.
        var (r, d) = Run(115, 90, Full, null, 110);
        AssertOk(r, d, Full, Notes(2, 0, 1), fee: 5, balance: 0, withdrawnToday: 200);
    }

    [Fact]
    public void DailyLimit_IsCheckedBeforeFundsAndDispensability()
    {
        var stock = Notes(0, 0, 0);
        var (r, d) = Run(0, 150, stock, Denomination.Fifty, 60);
        AssertRefused(r, AtmStatus.DailyLimitExceeded, Account(0, 150), d, stock);
    }

    // ---------- Contrôle 3 : solde et frais ----------

    [Theory]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(120, 5)]
    [InlineData(200, 5)]
    [InlineData(10, 0)]
    [InlineData(90, 0)]
    public void Fee_Is5OnlyStrictlyAbove100(int amount, int expectedFee)
    {
        var (r, _) = Run(250, 0, Full, null, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedFee, r.Fee);
        Assert.Equal(250 - amount - expectedFee, r.Account.Balance);
        Assert.Equal(amount, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(100, 10)]
    [InlineData(40, 70)]
    [InlineData(150, 50)]
    [InlineData(190, 10)]
    [InlineData(10, 100)]
    public void Fee_DependsOnlyOnAmount_NotOnWhatWasAlreadyWithdrawn(int withdrawnToday, int amount)
    {
        // amount <= 100 : pas de frais, même si WithdrawnToday + amount dépasse 100.
        var (r, _) = Run(250, withdrawnToday, Full, null, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Account(250 - amount, withdrawnToday + amount), r.Account);
    }

    [Fact]
    public void BalanceEqualToAmount_AlreadyWithdrawnSome_IsOk_NoFee()
    {
        // 50 <= 100 : pas de frais, donc 50 € de solde suffisent, quel que soit le déjà-retiré.
        var (r, d) = Run(50, 90, Full, null, 50);
        AssertOk(r, d, Full, Notes(1, 0, 0), fee: 0, balance: 0, withdrawnToday: 140);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(100, 100)]
    [InlineData(105, 100)]
    [InlineData(205, 200)]
    public void BalanceExactlyCoveringAmountPlusFee_IsOk(int balance, int amount)
    {
        var fee = amount > 100 ? 5 : 0;
        var (r, _) = Run(balance, 0, Full, null, amount);
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(balance - amount - fee, r.Account.Balance);
    }

    [Theory]
    [InlineData(110, 120)] // 120 + 5 > 110
    [InlineData(200, 200)] // 200 + 5 > 200
    [InlineData(105, 110)]
    [InlineData(110, 110)]
    public void InsufficientFunds_WhenAmountPlusFeeExceedsBalance(int balance, int amount)
    {
        var (r, d) = Run(balance, 0, Full, null, amount);
        AssertRefused(r, AtmStatus.InsufficientFunds, Account(balance, 0), d, Full);
    }

    [Fact]
    public void InsufficientFunds_IsCheckedBeforeDispensability()
    {
        var stock = Notes(0, 0, 0);
        var (r, d) = Run(20, 0, stock, null, 30);
        AssertRefused(r, AtmStatus.InsufficientFunds, Account(20, 0), d, stock);
    }

    // ---------- Contrôle 4 : combinaison ----------

    [Fact]
    public void CannotDispense_WhenStockEmpty()
    {
        var stock = Notes(0, 0, 0);
        var (r, d) = Run(250, 0, stock, null, 10);
        AssertRefused(r, AtmStatus.CannotDispense, Account(250, 0), d, stock);
    }

    [Fact]
    public void CannotDispense_WhenStockValueTooLow()
    {
        var stock = Notes(1, 1, 1); // 80 €
        var (r, d) = Run(250, 0, stock, null, 90);
        AssertRefused(r, AtmStatus.CannotDispense, Account(250, 0), d, stock);
    }

    [Theory]
    [InlineData(0, 5, 0, 30)]   // que des 20 : 30 impossible
    [InlineData(0, 5, 0, 10)]
    [InlineData(2, 0, 0, 60)]   // que des 50 : 60 impossible
    [InlineData(1, 1, 0, 30)]
    [InlineData(1, 2, 0, 80)]   // 50+20+20 = 90, 20+20 = 40, 50+20 = 70 : 80 impossible
    public void CannotDispense_WhenNoExactCombination(int fifties, int twenties, int tens, int amount)
    {
        var stock = Notes(fifties, twenties, tens);
        var (r, d) = Run(250, 0, stock, null, amount);
        AssertRefused(r, AtmStatus.CannotDispense, Account(250, 0), d, stock);
    }

    [Theory]
    [InlineData(Denomination.Fifty, 50)]
    [InlineData(Denomination.Twenty, 20)]
    [InlineData(Denomination.Ten, 10)]
    public void JammedCassette_CountsForZero(Denomination jammed, int amount)
    {
        var stock = jammed switch
        {
            Denomination.Fifty => Notes(5, 0, 0),
            Denomination.Twenty => Notes(0, 5, 0),
            _ => Notes(0, 0, 5),
        };
        var (r, d) = Run(250, 0, stock, jammed, amount);
        AssertRefused(r, AtmStatus.CannotDispense, Account(250, 0), d, stock);
    }

    [Fact]
    public void JammedTens_ForcesCombinationWithoutTens()
    {
        // Sans blocage : 50+10 (2 billets). Avec les 10 bloqués : 20×3.
        var (r, d) = Run(250, 0, Full, Denomination.Ten, 60);
        AssertOk(r, d, Full, Notes(0, 3, 0), fee: 0, balance: 190, withdrawnToday: 60);
    }

    [Fact]
    public void JammedTwenties_ForcesCombinationWithoutTwenties()
    {
        // Sans blocage : 20×2. Avec les 20 bloqués : 10×4.
        var (r, d) = Run(250, 0, Full, Denomination.Twenty, 40);
        AssertOk(r, d, Full, Notes(0, 0, 4), fee: 0, balance: 210, withdrawnToday: 40);
    }

    [Fact]
    public void JammedFifties_ForcesCombinationWithoutFifties()
    {
        // Sans blocage : 50×2. Avec les 50 bloqués : 20×5.
        var (r, d) = Run(250, 0, Full, Denomination.Fifty, 100);
        AssertOk(r, d, Full, Notes(0, 5, 0), fee: 0, balance: 150, withdrawnToday: 100);
    }

    // ---------- La combinaison : le moins de billets ----------

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
    public void FullStock_GivesFewestNotes(int amount, int fifties, int twenties, int tens)
    {
        var (r, d) = Run(250, 0, Full, null, amount);
        var fee = amount > 100 ? 5 : 0;
        AssertOk(r, d, Full, Notes(fifties, twenties, tens), fee, 250 - amount - fee, amount);
    }

    [Theory]
    [InlineData(1, 3, 0, 60, 0, 3, 0)]   // le glouton (50 puis bloqué) échoue
    [InlineData(1, 4, 0, 80, 0, 4, 0)]
    [InlineData(1, 3, 0, 110, 1, 3, 0)]
    [InlineData(3, 4, 0, 130, 1, 4, 0)]  // 50×2 + 30 impossible → 50 + 20×4
    [InlineData(0, 5, 5, 90, 0, 4, 1)]
    [InlineData(1, 0, 5, 90, 1, 0, 4)]
    [InlineData(1, 5, 5, 200, 1, 5, 5)]  // 11 billets, la seule combinaison
    [InlineData(5, 5, 0, 160, 2, 3, 0)]  // 50×3 + 10 impossible → 50×2 + 20×3
    [InlineData(0, 0, 5, 50, 0, 0, 5)]
    public void LimitedStock_GivesFewestNotesAmongAvailable(
        int sf, int st, int sn, int amount, int fifties, int twenties, int tens)
    {
        var stock = Notes(sf, st, sn);
        var (r, d) = Run(250, 0, stock, null, amount);
        var fee = amount > 100 ? 5 : 0;
        AssertOk(r, d, stock, Notes(fifties, twenties, tens), fee, 250 - amount - fee, amount);
    }

    // ---------- La sortie : passages de 4 billets au plus ----------

    [Fact]
    public void ManyNotes_AreDispensedInSeveralPassesOfAtMost4()
    {
        var stock = Notes(1, 5, 5);
        var (r, d) = Run(250, 0, stock, null, 200);
        AssertOk(r, d, stock, Notes(1, 5, 5), fee: 5, balance: 45, withdrawnToday: 200);
        Assert.True(d.Dispenses.Count >= 3);
    }

    [Fact]
    public void FiveNotes_NeedAtLeastTwoPasses()
    {
        var (r, d) = Run(250, 0, Full, Denomination.Fifty, 100); // 20×5
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.True(d.Dispenses.Count >= 2);
        Assert.All(d.Dispenses, p => Assert.InRange(p.Notes.Total, 1, 4));
        Assert.Equal(Notes(0, 5, 0), d.TotalDispensed);
    }

    [Fact]
    public void Withdraw70_AfterAlreadyWithdrawn40_NoFee_AccountUpdated()
    {
        var (r, _) = Run(250, 40, Full, null, 70);
        Assert.Equal(Account(180, 110), r.Account);
    }
}
