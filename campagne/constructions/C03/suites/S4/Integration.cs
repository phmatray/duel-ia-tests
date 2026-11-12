namespace Atm.Tests;

/// <summary>
/// Tests d'intégration : <see cref="CashMachine"/> travaille avec le vrai <see cref="MoneyDispenser"/>, sans doublure.
/// Chaque opération est vérifiée sur son résultat et sur le stock du module juste après.
/// L'accent est mis sur ce qu'un appel isolé ne montre pas : l'état du module qui évolue d'une opération à l'autre,
/// le partage d'un module entre machines, et la cohérence du couple sur tout le domaine.
/// </summary>
public class Integration
{
    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static Operation W(int amount) => new Operation.Withdraw(amount);

    private static Operation D(int fifties, int twenties, int tens) => new Operation.Deposit(N(fifties, twenties, tens));

    private static readonly Denomination?[] JamOptions = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Amounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly int[] Balances = [.. Enumerable.Range(0, 51).Select(i => i * 5)];

    private static readonly int[] Withdrawns = [.. Enumerable.Range(0, 21).Select(i => i * 10)];

    private static readonly NoteCounts[] Deposits =
        [.. Enumerable.Range(0, 8).Select(m => N(m & 1, (m >> 1) & 1, (m >> 2) & 1))];

    // ---- Modèle de référence, écrit indépendamment de CashMachine ----------------------------------

    private static NoteCounts Available(NoteCounts stock, Denomination? jammed) => N(
        jammed == Denomination.Fifty ? 0 : stock.Fifties,
        jammed == Denomination.Twenty ? 0 : stock.Twenties,
        jammed == Denomination.Ten ? 0 : stock.Tens);

    private static NoteCounts? Fewest(int amount, NoteCounts available)
    {
        NoteCounts? best = null;
        for (var f = 0; f <= available.Fifties; f++)
        for (var t = 0; t <= available.Twenties; t++)
        for (var c = 0; c <= available.Tens; c++)
        {
            var candidate = N(f, t, c);
            if (candidate.Value == amount && (best is null || candidate.Total < best.Value.Total))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Le résultat attendu et le stock attendu après l'opération.</summary>
    private static (AtmResult Result, NoteCounts Stock) Expected(
        AccountState account, NoteCounts stock, Denomination? jammed, Operation operation)
    {
        (AtmResult, NoteCounts) Refused(AtmStatus status) => (new AtmResult(status, NoteCounts.None, 0, account), stock);

        switch (operation)
        {
            case Operation.Withdraw { Amount: var amount }:
            {
                if (amount <= 0 || amount % 10 != 0) return Refused(AtmStatus.InvalidAmount);
                if (account.WithdrawnToday + amount > 200) return Refused(AtmStatus.DailyLimitExceeded);
                var fee = amount > 100 ? 5 : 0;
                if (amount + fee > account.Balance) return Refused(AtmStatus.InsufficientFunds);
                if (Fewest(amount, Available(stock, jammed)) is not { } notes) return Refused(AtmStatus.CannotDispense);
                var after = new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount);
                return (new AtmResult(AtmStatus.Ok, notes, fee, after), stock - notes);
            }

            case Operation.Deposit { Notes: var notes }:
            {
                if (notes.Total == 0) return Refused(AtmStatus.InvalidAmount);
                if (jammed is { } j && notes.Of(j) > 0) return Refused(AtmStatus.CassetteJammed);
                var after = stock + notes;
                if (after.Fifties > 5 || after.Twenties > 5 || after.Tens > 5) return Refused(AtmStatus.CassetteFull);
                return (new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, account with { Balance = account.Balance + notes.Value }), after);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    // ---- Outils -----------------------------------------------------------------------------------

    /// <summary>
    /// Exécute l'opération sur la machine et vérifie le résultat, le stock du module, et la conservation de l'argent :
    /// ce qui sort du module est exactement ce que la machine annonce, ce qui y entre est exactement le dépôt.
    /// </summary>
    private static AtmResult Step(CashMachine machine, MoneyDispenser dispenser, AccountState account, Operation operation,
        AtmResult expected, NoteCounts expectedStock)
    {
        var before = dispenser.Stock;
        var result = machine.Execute(account, operation);

        Assert.Equal(expected, result);
        Assert.Equal(expectedStock, dispenser.Stock);
        Assert.NotEqual(AtmStatus.DispenserFault, result.Status);

        switch (operation, result.Status)
        {
            case (Operation.Withdraw w, AtmStatus.Ok):
                Assert.Equal(before - result.Notes, dispenser.Stock);
                Assert.Equal(w.Amount, result.Notes.Value);
                Assert.Equal(account.Balance - w.Amount - result.Fee, result.Account.Balance);
                break;
            case (Operation.Deposit d, AtmStatus.Ok):
                Assert.Equal(before + d.Notes, dispenser.Stock);
                Assert.Equal(account.Balance + d.Notes.Value, result.Account.Balance);
                break;
            default:
                Assert.Equal(before, dispenser.Stock);
                Assert.Equal(account, result.Account);
                break;
        }

        return result;
    }

    /// <summary>Exécute l'opération et la compare au modèle de référence.</summary>
    private static AtmResult StepAgainstModel(CashMachine machine, MoneyDispenser dispenser, AccountState account,
        Operation operation)
    {
        var (expected, expectedStock) = Expected(account, dispenser.Stock, dispenser.Jammed, operation);
        return Step(machine, dispenser, account, operation, expected, expectedStock);
    }

    private static AtmResult Ok(NoteCounts notes, int fee, int balance, int withdrawn) =>
        new(AtmStatus.Ok, notes, fee, new AccountState(balance, withdrawn));

    private static AtmResult Refused(AtmStatus status, int balance, int withdrawn) =>
        new(status, NoteCounts.None, 0, new AccountState(balance, withdrawn));

    // ---- Scénarios écrits à la main : le stock du module évolue d'une opération à l'autre -------------

    [Fact]
    public void A_day_of_operations_on_one_module()
    {
        var dispenser = new MoneyDispenser(N(1, 3, 2));
        var machine = new CashMachine(dispenser);

        // 60 = 50×1 10×1 : 2 billets plutôt que 20×3.
        var r = Step(machine, dispenser, new AccountState(250, 0), W(60), Ok(N(1, 0, 1), 0, 190, 60), N(0, 3, 1));

        // Le même retrait, mais le module n'a plus de 50 : la machine relit le stock et sort 20×3.
        r = Step(machine, dispenser, r.Account, W(60), Ok(N(0, 3, 0), 0, 130, 120), N(0, 0, 1));

        // Il ne reste qu'un billet de 10 : 20 € ne peut pas sortir, et rien ne bouge.
        r = Step(machine, dispenser, r.Account, W(20), Refused(AtmStatus.CannotDispense, 130, 120), N(0, 0, 1));

        // Un dépôt réapprovisionne le module.
        r = Step(machine, dispenser, r.Account, D(1, 1, 1), Ok(NoteCounts.None, 0, 210, 120), N(1, 1, 2));

        // Les billets déposés ressortent aussitôt ; le plafond du jour est atteint pile.
        r = Step(machine, dispenser, r.Account, W(80), Ok(N(1, 1, 1), 0, 130, 200), N(0, 0, 1));

        // Plus rien ne sort aujourd'hui, même 10 € qui seraient disponibles.
        r = Step(machine, dispenser, r.Account, W(10), Refused(AtmStatus.DailyLimitExceeded, 130, 200), N(0, 0, 1));

        // Un dépôt reste possible au plafond, et ne touche pas à WithdrawnToday.
        r = Step(machine, dispenser, r.Account, D(0, 0, 1), Ok(NoteCounts.None, 0, 140, 200), N(0, 0, 2));

        // Le lendemain, le compte repart de zéro retiré ; le module, lui, garde son stock.
        r = Step(machine, dispenser, r.Account with { WithdrawnToday = 0 }, W(20), Ok(N(0, 0, 2), 0, 120, 20), N(0, 0, 0));

        // Module vide.
        Step(machine, dispenser, r.Account, W(10), Refused(AtmStatus.CannotDispense, 120, 20), N(0, 0, 0));
    }

    [Fact]
    public void A_jammed_cassette_stays_untouched_across_operations()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5), Denomination.Twenty);
        var machine = new CashMachine(dispenser);

        // Sans les 20 : 40 = 10×4.
        var r = Step(machine, dispenser, new AccountState(250, 0), W(40), Ok(N(0, 0, 4), 0, 210, 40), N(5, 5, 1));

        // Il ne reste qu'un 10 utilisable ; les cinq 20 bloqués ne servent pas.
        r = Step(machine, dispenser, r.Account, W(40), Refused(AtmStatus.CannotDispense, 210, 40), N(5, 5, 1));

        // On ne peut pas ranger un 20 dans la cassette bloquée, ni un 50 dans une cassette pleine.
        r = Step(machine, dispenser, r.Account, D(0, 1, 0), Refused(AtmStatus.CassetteJammed, 210, 40), N(5, 5, 1));
        r = Step(machine, dispenser, r.Account, D(1, 0, 0), Refused(AtmStatus.CassetteFull, 210, 40), N(5, 5, 1));
        r = Step(machine, dispenser, r.Account, D(1, 1, 1), Refused(AtmStatus.CassetteJammed, 210, 40), N(5, 5, 1));

        // Un 10 seul passe.
        r = Step(machine, dispenser, r.Account, D(0, 0, 1), Ok(NoteCounts.None, 0, 220, 40), N(5, 5, 2));

        // 110 = 50×2 10×1, avec frais.
        r = Step(machine, dispenser, r.Account, W(110), Ok(N(2, 0, 1), 5, 105, 150), N(3, 5, 1));

        // 50 sort en un billet ; la cassette de 20 n'a jamais bougé.
        Step(machine, dispenser, r.Account, W(50), Ok(N(1, 0, 0), 0, 55, 200), N(2, 5, 1));
    }

    [Fact]
    public void A_refused_operation_succeeds_once_the_module_changes()
    {
        var dispenser = new MoneyDispenser(N(0, 5, 0));
        var machine = new CashMachine(dispenser);
        var account = new AccountState(100, 0);

        // La cassette de 20 est pleine : le dépôt d'un 20 est refusé.
        Step(machine, dispenser, account, D(0, 1, 0), Refused(AtmStatus.CassetteFull, 100, 0), N(0, 5, 0));

        // 10 € ne peut pas sortir d'un module sans billet de 10.
        Step(machine, dispenser, account, W(10), Refused(AtmStatus.CannotDispense, 100, 0), N(0, 5, 0));

        // Un retrait libère une place dans la cassette de 20…
        var r = Step(machine, dispenser, account, W(20), Ok(N(0, 1, 0), 0, 80, 20), N(0, 4, 0));

        // … et le même dépôt passe maintenant.
        r = Step(machine, dispenser, r.Account, D(0, 1, 0), Ok(NoteCounts.None, 0, 100, 20), N(0, 5, 0));

        // Un dépôt de 10 rend possible le retrait de 10 refusé plus haut.
        r = Step(machine, dispenser, r.Account, D(0, 0, 1), Ok(NoteCounts.None, 0, 110, 20), N(0, 5, 1));
        Step(machine, dispenser, r.Account, W(10), Ok(N(0, 0, 1), 0, 100, 30), N(0, 5, 0));
    }

    [Fact]
    public void Two_machines_sharing_a_module_see_each_others_changes()
    {
        var dispenser = new MoneyDispenser(N(1, 0, 1));
        var first = new CashMachine(dispenser);
        var second = new CashMachine(dispenser);
        var account = new AccountState(200, 0);

        // La première machine prend le 50.
        Step(first, dispenser, account, W(50), Ok(N(1, 0, 0), 0, 150, 50), N(0, 0, 1));

        // La seconde ne peut plus le sortir : elle ne garde aucune image du stock.
        Step(second, dispenser, account, W(50), Refused(AtmStatus.CannotDispense, 200, 0), N(0, 0, 1));

        // La seconde remet un 50, la première le ressort.
        Step(second, dispenser, account, D(1, 0, 0), Ok(NoteCounts.None, 0, 250, 0), N(1, 0, 1));
        Step(first, dispenser, account, W(60), Ok(N(1, 0, 1), 0, 140, 60), N(0, 0, 0));
    }

    [Fact]
    public void A_machine_carries_nothing_from_one_account_to_another()
    {
        var dispenser = new MoneyDispenser(N(5, 5, 5));
        var machine = new CashMachine(dispenser);

        // Un compte au plafond, puis un autre compte : seul le module est commun.
        Step(machine, dispenser, new AccountState(250, 200), W(10), Refused(AtmStatus.DailyLimitExceeded, 250, 200), N(5, 5, 5));
        Step(machine, dispenser, new AccountState(20, 0), W(20), Ok(N(0, 1, 0), 0, 0, 20), N(5, 4, 5));
        Step(machine, dispenser, new AccountState(0, 0), W(20), Refused(AtmStatus.InsufficientFunds, 0, 0), N(5, 4, 5));
        Step(machine, dispenser, new AccountState(250, 0), W(200), Ok(N(4, 0, 0), 5, 45, 200), N(1, 4, 5));
    }

    // ---- Découpage en passages : le vrai module refuse plus de 4 billets à la fois -----------------

    [Theory]
    [InlineData(0, 0, 5, 50, 0, 0, 5)]   // 5 billets
    [InlineData(0, 5, 0, 100, 0, 5, 0)]  // 5 billets
    [InlineData(0, 5, 5, 110, 0, 5, 1)]  // 6 billets
    [InlineData(1, 0, 5, 100, 1, 0, 5)]  // 6 billets
    [InlineData(0, 5, 3, 130, 0, 5, 3)]  // 8 billets
    [InlineData(1, 5, 4, 190, 1, 5, 4)]  // 10 billets, au-delà de 2 passages
    [InlineData(0, 5, 5, 150, 0, 5, 5)]  // 10 billets, le module se vide
    public void Withdrawals_of_more_than_four_notes_go_through_the_real_module(
        int f, int t, int c, int amount, int nf, int nt, int nc)
    {
        var stock = N(f, t, c);
        var notes = N(nf, nt, nc);
        var dispenser = new MoneyDispenser(stock);
        var fee = amount > 100 ? 5 : 0;

        Step(new CashMachine(dispenser), dispenser, new AccountState(250, 0), W(amount),
            Ok(notes, fee, 250 - amount - fee, amount), stock - notes);
    }

    [Fact]
    public void Withdrawal_of_many_notes_next_to_a_jammed_cassette()
    {
        // La cassette de 50 est bloquée : chaque passage doit l'éviter, sinon le module refuse.
        var dispenser = new MoneyDispenser(N(5, 5, 5), Denomination.Fifty);

        Step(new CashMachine(dispenser), dispenser, new AccountState(250, 0), W(150),
            Ok(N(0, 5, 5), 5, 95, 150), N(5, 0, 0));
    }

    // ---- Tout le domaine des comptes ----------------------------------------------------------------

    private static readonly (NoteCounts Stock, Denomination? Jammed)[] Modules =
    [
        (N(0, 0, 0), null),
        (N(5, 5, 5), null),
        (N(5, 5, 5), Denomination.Fifty),
        (N(5, 5, 5), Denomination.Twenty),
        (N(5, 5, 5), Denomination.Ten),
        (N(1, 3, 0), null),
        (N(0, 5, 5), null),
        (N(4, 5, 0), Denomination.Ten),
        (N(2, 0, 4), Denomination.Twenty),
    ];

    [Fact]
    public void Every_account_and_operation_of_the_domain_against_the_reference_model()
    {
        Operation[] operations = [.. Amounts.Select(W), .. Deposits.Select(d => new Operation.Deposit(d))];

        foreach (var (stock, jammed) in Modules)
        foreach (var balance in Balances)
        foreach (var withdrawn in Withdrawns)
        foreach (var operation in operations)
        {
            var dispenser = new MoneyDispenser(stock, jammed);
            StepAgainstModel(new CashMachine(dispenser), dispenser, new AccountState(balance, withdrawn), operation);
        }
    }

    [Fact]
    public void Every_module_of_the_domain_against_the_reference_model_with_a_tight_account()
    {
        // Complète les tests existants : un compte où solde, plafond et billets jouent tous à la limite.
        AccountState[] accounts = [new(105, 100), new(115, 90), new(55, 150), new(250, 190)];
        Operation[] operations = [.. Amounts.Select(W), .. Deposits.Select(d => new Operation.Deposit(d))];

        for (var f = 0; f <= 5; f++)
        for (var t = 0; t <= 5; t++)
        for (var c = 0; c <= 5; c++)
        foreach (var jammed in JamOptions)
        foreach (var account in accounts)
        foreach (var operation in operations)
        {
            var dispenser = new MoneyDispenser(N(f, t, c), jammed);
            StepAgainstModel(new CashMachine(dispenser), dispenser, account, operation);
        }
    }

    // ---- Suites d'opérations sur un même module, contre le modèle de référence ----------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Random_sequences_on_a_shared_module_match_the_reference_model(int seed)
    {
        var random = new Random(seed);

        for (var run = 0; run < 200; run++)
        {
            var jammed = JamOptions[random.Next(JamOptions.Length)];
            var dispenser = new MoneyDispenser(N(random.Next(6), random.Next(6), random.Next(6)), jammed);
            var machine = new CashMachine(dispenser);
            var account = new AccountState(Balances[random.Next(Balances.Length)], Withdrawns[random.Next(Withdrawns.Length)]);

            for (var step = 0; step < 40; step++)
            {
                // Parfois, un nouveau jour commence : seul le compte change, le module garde son stock.
                if (random.Next(10) == 0)
                {
                    account = account with { WithdrawnToday = 0 };
                }

                var operation = NextOperation(random, account);
                account = StepAgainstModel(machine, dispenser, account, operation).Account;
            }
        }
    }

    /// <summary>Une opération du domaine qui laisse le compte dans le domaine (solde au plus 250).</summary>
    private static Operation NextOperation(Random random, AccountState account)
    {
        while (true)
        {
            if (random.Next(2) == 0)
            {
                return W(Amounts[random.Next(Amounts.Length)]);
            }

            var deposit = Deposits[random.Next(Deposits.Length)];
            if (account.Balance + deposit.Value <= 250)
            {
                return new Operation.Deposit(deposit);
            }
        }
    }

    [Fact]
    public void Withdrawing_until_the_module_is_empty_then_refilling_it()
    {
        foreach (var jammed in JamOptions)
        {
            var full = N(5, 5, 5);
            var dispenser = new MoneyDispenser(full, jammed);
            var machine = new CashMachine(dispenser);
            var jammedStock = jammed is { } j ? full.Of(j) : 0;
            var available = Available(full, jammed).Value;

            // On retire 10 € à la fois, chaque fois un nouveau jour, jusqu'à ce que plus rien ne sorte.
            var account = new AccountState(250, 0);
            var dispensed = 0;
            while (true)
            {
                var r = StepAgainstModel(machine, dispenser, account with { WithdrawnToday = 0 }, W(10));
                if (r.Status != AtmStatus.Ok) break;
                dispensed += 10;
                account = r.Account;
            }

            // Avec seulement des 10 € à la fois, ce sont les billets de 10 qui sortent, et eux seuls.
            Assert.Equal(jammed == Denomination.Ten ? 0 : 50, dispensed);
            Assert.Equal(full - N(0, 0, dispensed / 10), dispenser.Stock);

            // Puis on retire tout ce qui reste d'utilisable par gros montants.
            while (Available(dispenser.Stock, jammed).Value > 0)
            {
                var amount = Math.Min(Available(dispenser.Stock, jammed).Value, 200);
                var before = dispenser.Stock;
                var r = StepAgainstModel(machine, dispenser, new AccountState(250, 0), W(amount));
                Assert.Equal(AtmStatus.Ok, r.Status);
                Assert.True(dispenser.Stock.Total < before.Total);
                dispensed += amount;
            }

            // Tout l'utilisable est sorti ; la cassette bloquée est intacte.
            Assert.Equal(available, dispensed);
            Assert.Equal(jammedStock, dispenser.Stock.Total);

            // On remplit à nouveau, un billet à la fois, jusqu'à la capacité.
            for (var i = 0; i < 5; i++)
            {
                foreach (var d in Deposits.Where(d => d.Total == 1 && (jammed is not { } jj || d.Of(jj) == 0)))
                {
                    var r = StepAgainstModel(machine, dispenser, new AccountState(0, 0), new Operation.Deposit(d));
                    Assert.Equal(AtmStatus.Ok, r.Status);
                }
            }

            Assert.Equal(full, dispenser.Stock);

            // Le module est plein : tout dépôt est refusé.
            foreach (var d in Deposits)
            {
                var r = StepAgainstModel(machine, dispenser, new AccountState(0, 0), new Operation.Deposit(d));
                Assert.NotEqual(AtmStatus.Ok, r.Status);
            }
        }
    }
}
