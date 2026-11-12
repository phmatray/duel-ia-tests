namespace Atm.Tests;

/// <summary>
/// Tests de renfort : interactions avec le module (aucun appel sur un refus, passages valides, dépôt en un seul
/// chargement), pannes en cours de retrait, lecture de l'état du module à chaque appel et parcours croisés du domaine.
/// </summary>
public class Renforts
{
    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] DomainAmounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static NoteCounts Full => new(5, 5, 5);

    private static IEnumerable<NoteCounts> AllStocks() =>
        from f in Enumerable.Range(0, 6)
        from t in Enumerable.Range(0, 6)
        from n in Enumerable.Range(0, 6)
        select new NoteCounts(f, t, n);

    private static IEnumerable<NoteCounts> AllDeposits() =>
        from f in Enumerable.Range(0, 2)
        from t in Enumerable.Range(0, 2)
        from n in Enumerable.Range(0, 2)
        select new NoteCounts(f, t, n);

    private static NoteCounts Sum(IEnumerable<NoteCounts> passes) => passes.Aggregate(NoteCounts.None, (a, b) => a + b);

    // ---------------------------------------------------------------------------------------------------------------
    // Oracles indépendants, écrits directement d'après la spécification.
    // ---------------------------------------------------------------------------------------------------------------

    private static AtmResult WithdrawOracle(AccountState account, NoteCounts stock, Denomination? jammed, int amount)
    {
        AtmResult Refused(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (amount <= 0 || amount % 10 != 0) return Refused(AtmStatus.InvalidAmount);
        if (account.WithdrawnToday + amount > 200) return Refused(AtmStatus.DailyLimitExceeded);
        var fee = amount > 100 ? 5 : 0;
        if (amount + fee > account.Balance) return Refused(AtmStatus.InsufficientFunds);

        var f = jammed == Denomination.Fifty ? 0 : stock.Fifties;
        var t = jammed == Denomination.Twenty ? 0 : stock.Twenties;
        var n = jammed == Denomination.Ten ? 0 : stock.Tens;

        NoteCounts? best = null;
        for (var a = 0; a <= f; a++)
        for (var b = 0; b <= t; b++)
        for (var c = 0; c <= n; c++)
        {
            var notes = new NoteCounts(a, b, c);
            if (notes.Value == amount && (best is null || notes.Total < best.Value.Total))
            {
                best = notes;
            }
        }

        return best is { } combination
            ? new AtmResult(AtmStatus.Ok, combination, fee, new(account.Balance - amount - fee, account.WithdrawnToday + amount))
            : Refused(AtmStatus.CannotDispense);
    }

    private static AtmResult DepositOracle(AccountState account, NoteCounts stock, Denomination? jammed, NoteCounts notes)
    {
        AtmResult Refused(AtmStatus status) => new(status, NoteCounts.None, 0, account);

        if (notes.Total == 0) return Refused(AtmStatus.InvalidAmount);
        if (jammed is { } j && notes.Of(j) > 0) return Refused(AtmStatus.CassetteJammed);
        var after = stock + notes;
        if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refused(AtmStatus.CassetteFull);

        return new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value });
    }

    private static void AssertValidPasses(IReadOnlyList<NoteCounts> passes, Denomination? jammed)
    {
        Assert.All(passes, pass =>
        {
            Assert.InRange(pass.Total, 1, MoneyDispenser.MaxNotesPerPass);
            Assert.True(pass.Fifties >= 0 && pass.Twenties >= 0 && pass.Tens >= 0, $"Passage négatif : {pass}");
            if (jammed is { } j)
            {
                Assert.Equal(0, pass.Of(j));
            }
        });
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Interactions avec le module, sur tout le domaine.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sur un refus, la machine ne touche pas au module (ni Dispense ni Load). Sur un succès, elle ne fait que des
    /// passages valides dont la somme est exactement la combinaison rendue, et n'appelle jamais Load.
    /// </summary>
    [Fact]
    public void Withdraw_ModuleInteractions_OverWholeStockDomain()
    {
        var account = new AccountState(250, 0);
        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var amount in DomainAmounts)
        {
            var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
            var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

            Assert.Empty(dispenser.LoadCalls);
            if (result.Status == AtmStatus.Ok)
            {
                Assert.NotEmpty(dispenser.DispenseCalls);
                AssertValidPasses(dispenser.DispenseCalls, jammed);
                Assert.Equal(result.Notes, Sum(dispenser.DispenseCalls));
                Assert.Equal(amount, result.Notes.Value);
            }
            else
            {
                Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
                Assert.Empty(dispenser.DispenseCalls);
            }
        }
    }

    /// <summary>Le nombre de passages est raisonnable : pas de passage inutile au-delà du minimum nécessaire.</summary>
    [Fact]
    public void Withdraw_NeverMakesMorePassesThanNotes()
    {
        foreach (var stock in AllStocks())
        foreach (var amount in Enumerable.Range(1, 20).Select(i => i * 10))
        {
            var dispenser = new RecordingDispenser(new MoneyDispenser(stock));
            var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(amount));

            if (result.Status == AtmStatus.Ok)
            {
                Assert.InRange(dispenser.DispenseCalls.Count, 1, result.Notes.Total);
            }
        }
    }

    /// <summary>
    /// Sur un refus de dépôt, aucun appel au module. Sur un succès, un unique Load avec exactement les billets déposés,
    /// et jamais de Dispense.
    /// </summary>
    [Fact]
    public void Deposit_ModuleInteractions_OverWholeDomain()
    {
        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var notes in AllDeposits())
        {
            var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed));
            var result = new CashMachine(dispenser).Execute(new(100, 50), new Operation.Deposit(notes));

            Assert.Empty(dispenser.DispenseCalls);
            if (result.Status == AtmStatus.Ok)
            {
                Assert.Equal([notes], dispenser.LoadCalls);
            }
            else
            {
                Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
                Assert.Empty(dispenser.LoadCalls);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Parcours croisés compte × module × opération.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Croise des comptes aux frontières (frais, plafond, solde) avec tous les stocks et cassettes bloquées : vérifie
    /// l'ordre des contrôles quand plusieurs échouent à la fois, et le compte rendu.
    /// </summary>
    [Fact]
    public void Withdraw_MatchesOracle_AcrossAccountsStocksAndJams()
    {
        int[] balances = [0, 5, 10, 45, 50, 55, 95, 100, 105, 110, 115, 120, 150, 155, 200, 205, 250];
        int[] withdrawnValues = [0, 10, 50, 100, 140, 150, 190, 200];

        foreach (var balance in balances)
        foreach (var withdrawn in withdrawnValues)
        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var amount in DomainAmounts)
        {
            var account = new AccountState(balance, withdrawn);
            var dispenser = new MoneyDispenser(stock, jammed);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Withdraw(amount));

            var expected = WithdrawOracle(account, stock, jammed, amount);
            if (expected != result)
            {
                Assert.Fail($"Solde {balance}, retiré {withdrawn}, stock {stock}, bloquée {jammed?.ToString() ?? "aucune"}, " +
                            $"retrait {amount} : attendu {expected}, obtenu {result}.");
            }

            Assert.Equal(stock - result.Notes, dispenser.Stock);
        }
    }

    /// <summary>Le dépôt sur tout le domaine du compte : le solde augmente de la valeur, le retrait du jour est intact.</summary>
    [Fact]
    public void Deposit_MatchesOracle_OverAccountDomain()
    {
        NoteCounts[] stocks = [NoteCounts.None, new(4, 4, 4), new(5, 0, 5), new(0, 5, 0)];

        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var stock in stocks)
        foreach (var jammed in JamOptions)
        foreach (var notes in AllDeposits())
        {
            var account = new AccountState(balance, withdrawn);
            var dispenser = new MoneyDispenser(stock, jammed);
            var result = new CashMachine(dispenser).Execute(account, new Operation.Deposit(notes));

            var expected = DepositOracle(account, stock, jammed, notes);
            Assert.Equal(expected, result);
            Assert.Equal(result.Status == AtmStatus.Ok ? stock + notes : stock, dispenser.Stock);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Ordre des contrôles : cas ciblés.
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void InvalidAmount_IsCheckedBeforeCannotDispense()
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(NoteCounts.None));
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(15));

        Assert.Equal(new AtmResult(AtmStatus.InvalidAmount, NoteCounts.None, 0, new(250, 0)), result);
        Assert.Empty(dispenser.DispenseCalls);
    }

    [Fact]
    public void DailyLimit_IsCheckedBeforeCannotDispense()
    {
        var result = new CashMachine(new MoneyDispenser(NoteCounts.None)).Execute(new(250, 190), new Operation.Withdraw(20));

        Assert.Equal(new AtmResult(AtmStatus.DailyLimitExceeded, NoteCounts.None, 0, new(250, 190)), result);
    }

    [Fact]
    public void InsufficientFundsBecauseOfFee_IsCheckedBeforeCannotDispense()
    {
        // 150 + 5 de frais > 150, et aucun billet : les fonds sont contrôlés d'abord.
        var result = new CashMachine(new MoneyDispenser(NoteCounts.None)).Execute(new(150, 0), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, new(150, 0)), result);
    }

    [Fact]
    public void DailyLimit_IgnoresFee_EvenWhenBalanceIsTight()
    {
        // 190 + 10 = 200 : plafond atteint pile ; les frais (5) ne comptent pas dans le plafond mais dans le solde.
        var result = new CashMachine(new MoneyDispenser(Full)).Execute(new(105, 190), new Operation.Withdraw(10));

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 0, 1), 0, new(95, 200)), result);
    }

    [Fact]
    public void Withdraw_ExactBalanceIncludingFee_IsAllowed()
    {
        var result = new CashMachine(new MoneyDispenser(Full)).Execute(new(155, 0), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(3, 0, 0), 5, new(0, 150)), result);
    }

    [Fact]
    public void Withdraw_OneStepShortOfFee_IsInsufficientFunds()
    {
        var result = new CashMachine(new MoneyDispenser(Full)).Execute(new(150, 0), new Operation.Withdraw(150));

        Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
    }

    [Fact]
    public void Withdraw_BalanceShortWithoutFee_IsInsufficientFunds()
    {
        var result = new CashMachine(new MoneyDispenser(Full)).Execute(new(95, 0), new Operation.Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.InsufficientFunds, NoteCounts.None, 0, new(95, 0)), result);
    }

    [Fact]
    public void Withdraw_WithJammedTen_AndOddTens_CannotDispense()
    {
        // 30 demande un billet de 10 (50 et 20 ne font jamais 30 sans 10) ; la cassette de 10 est bloquée.
        var dispenser = new RecordingDispenser(new MoneyDispenser(Full, Denomination.Ten));
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(30));

        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new(250, 0)), result);
        Assert.Empty(dispenser.DispenseCalls);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void Withdraw_WithJammedTen_UsesFiftyAndTwenties()
    {
        // 90 = 50 + 20×2 sans billet de 10.
        var result = new CashMachine(new MoneyDispenser(Full, Denomination.Ten)).Execute(new(250, 0), new Operation.Withdraw(90));

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 2, 0), 0, new(160, 90)), result);
    }

    [Fact]
    public void Withdraw_WhenStockIsExactlyTheAmount_EmptiesTheMachine()
    {
        var dispenser = new MoneyDispenser(new(1, 2, 1));
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(100));

        // 100 = 50 + 20×2 + 10 : le seul moyen avec ce stock (un seul billet de 50).
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 2, 1), 0, new(150, 100)), result);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Pannes du module.
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void DispenserFault_OnThirdPass_ReturnsAllNotesFromEarlierPasses()
    {
        // 150 sans billet de 50 : 20×5 + 10×5 = 10 billets, donc trois passages ; le troisième échoue.
        var stock = new NoteCounts(0, 5, 5);
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock), failOnDispenseCall: 3);
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(150));

        Assert.Equal(3, dispenser.DispenseCalls.Count);
        var delivered = dispenser.DispenseCalls[0] + dispenser.DispenseCalls[1];
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, delivered, 0, new(250, 0)), result);
        Assert.Equal(8, delivered.Total);
        Assert.Equal(stock - delivered, dispenser.Stock);
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void DispenserFault_OnFirstPass_WithFeeAmount_HasNoFeeAndUnchangedAccount()
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(Full), failOnDispenseCall: 1);
        var result = new CashMachine(dispenser).Execute(new(250, 50), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new(250, 50)), result);
        Assert.Single(dispenser.DispenseCalls);
        Assert.Empty(dispenser.LoadCalls);
        Assert.Equal(Full, dispenser.Stock);
    }

    [Fact]
    public void DispenserFault_OnFirstOfSeveralPasses_StopsImmediately()
    {
        var stock = new NoteCounts(0, 5, 5);
        var dispenser = new RecordingDispenser(new MoneyDispenser(stock), failOnDispenseCall: 1);
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new(250, 0)), result);
        Assert.Single(dispenser.DispenseCalls);
        Assert.Equal(stock, dispenser.Stock);
    }

    [Fact]
    public void DispenserFault_OnLastPass_ReturnsAllButLastPass()
    {
        // 190 = 50×3 + 20×2 : 5 billets, deux passages ; le second échoue.
        var dispenser = new RecordingDispenser(new MoneyDispenser(Full), failOnDispenseCall: 2);
        var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(190));

        Assert.Equal(2, dispenser.DispenseCalls.Count);
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, dispenser.DispenseCalls[0], 0, new(250, 0)), result);
        Assert.Equal(Full - dispenser.DispenseCalls[0], dispenser.Stock);
    }

    /// <summary>Panne à chaque passage possible, sur tous les retraits en plusieurs passages du domaine.</summary>
    [Fact]
    public void DispenserFault_AtAnyPass_ReturnsExactlyEarlierPasses()
    {
        foreach (var stock in AllStocks())
        foreach (var jammed in JamOptions)
        foreach (var amount in Enumerable.Range(1, 20).Select(i => i * 10))
        {
            var reference = new RecordingDispenser(new MoneyDispenser(stock, jammed));
            var ok = new CashMachine(reference).Execute(new(250, 0), new Operation.Withdraw(amount));
            if (ok.Status != AtmStatus.Ok)
            {
                continue;
            }

            for (var failAt = 1; failAt <= reference.DispenseCalls.Count; failAt++)
            {
                var dispenser = new RecordingDispenser(new MoneyDispenser(stock, jammed), failOnDispenseCall: failAt);
                var result = new CashMachine(dispenser).Execute(new(250, 0), new Operation.Withdraw(amount));

                Assert.Equal(failAt, dispenser.DispenseCalls.Count);
                var delivered = Sum(dispenser.DispenseCalls.Take(failAt - 1));
                Assert.Equal(new AtmResult(AtmStatus.DispenserFault, delivered, 0, new(250, 0)), result);
                Assert.Equal(stock - delivered, dispenser.Stock);
                Assert.Empty(dispenser.LoadCalls);
            }
        }
    }

    [Fact]
    public void DepositFault_DoesNotDispenseNorRetry()
    {
        var dispenser = new RecordingDispenser(new MoneyDispenser(new(1, 1, 1)), failLoad: true);
        var result = new CashMachine(dispenser).Execute(new(10, 20), new Operation.Deposit(new(1, 1, 1)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, new(10, 20)), result);
        Assert.Equal([new NoteCounts(1, 1, 1)], dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // La machine lit l'état du module à chaque appel.
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void SameMachine_SecondWithdraw_SeesStockLeftByFirst()
    {
        var dispenser = new MoneyDispenser(new(2, 0, 0));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(new(250, 0), new Operation.Withdraw(100));
        var second = machine.Execute(new(250, 0), new Operation.Withdraw(100));

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(2, 0, 0), 0, new(150, 100)), first);
        Assert.Equal(new AtmResult(AtmStatus.CannotDispense, NoteCounts.None, 0, new(250, 0)), second);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void SameMachine_WithdrawAfterDeposit_UsesDepositedNotes()
    {
        var dispenser = new MoneyDispenser(NoteCounts.None);
        var machine = new CashMachine(dispenser);

        var before = machine.Execute(new(250, 0), new Operation.Withdraw(80));
        var deposit = machine.Execute(new(0, 0), new Operation.Deposit(new(1, 1, 1)));
        var after = machine.Execute(new(250, 0), new Operation.Withdraw(80));

        Assert.Equal(AtmStatus.CannotDispense, before.Status);
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(80, 0)), deposit);
        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 1, 1), 0, new(170, 80)), after);
        Assert.Equal(NoteCounts.None, dispenser.Stock);
    }

    [Fact]
    public void SameMachine_DepositAfterWithdraw_SeesFreedRoom()
    {
        var dispenser = new MoneyDispenser(new(5, 0, 0));
        var machine = new CashMachine(dispenser);

        var full = machine.Execute(new(0, 0), new Operation.Deposit(new(1, 0, 0)));
        var withdraw = machine.Execute(new(250, 0), new Operation.Withdraw(50));
        var deposit = machine.Execute(new(0, 0), new Operation.Deposit(new(1, 0, 0)));

        Assert.Equal(AtmStatus.CassetteFull, full.Status);
        Assert.Equal(AtmStatus.Ok, withdraw.Status);
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, new(50, 0)), deposit);
        Assert.Equal(new NoteCounts(5, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Machine_ReadsJammedCassetteAtEachCall()
    {
        var dispenser = new SwitchableDispenser(new MoneyDispenser(Full));
        var machine = new CashMachine(dispenser);

        var free = machine.Execute(new(250, 0), new Operation.Withdraw(50));
        Assert.Equal(new NoteCounts(1, 0, 0), free.Notes);

        dispenser.Inner = new MoneyDispenser(Full, Denomination.Fifty);
        var jammedWithdraw = machine.Execute(new(250, 0), new Operation.Withdraw(50));
        var jammedDeposit = machine.Execute(new(0, 0), new Operation.Deposit(new(1, 0, 0)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(0, 2, 1), 0, new(200, 50)), jammedWithdraw);
        Assert.Equal(AtmStatus.CassetteJammed, jammedDeposit.Status);
    }

    [Fact]
    public void Machine_ReadsStockAtEachCall_EvenWhenChangedOutsideTheMachine()
    {
        var dispenser = new MoneyDispenser(NoteCounts.None);
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(new(250, 0), new Operation.Withdraw(50)).Status);

        Assert.True(dispenser.Load(new(1, 0, 0)));

        Assert.Equal(new AtmResult(AtmStatus.Ok, new(1, 0, 0), 0, new(200, 50)), machine.Execute(new(250, 0), new Operation.Withdraw(50)));
    }

    /// <summary>Module dont on peut remplacer l'implémentation entre deux appels.</summary>
    private sealed class SwitchableDispenser(IMoneyDispenser inner) : IMoneyDispenser
    {
        public IMoneyDispenser Inner { get; set; } = inner;

        public NoteCounts Stock => Inner.Stock;

        public Denomination? Jammed => Inner.Jammed;

        public bool Dispense(NoteCounts notes) => Inner.Dispense(notes);

        public bool Load(NoteCounts notes) => Inner.Load(notes);
    }
}
