using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Atm.Tests;

/// <summary>
/// Tests de propriétés : des règles de la spécification qui doivent tenir pour toute entrée du domaine,
/// vérifiées sur des cas tirés au hasard avec une graine fixe.
/// Sauf mention contraire, la machine travaille avec le vrai <see cref="MoneyDispenser"/>.
/// </summary>
public class Proprietes
{
    // Graine fixe pour la reproductibilité. FsCheck 3 lit « graine,gamma » et exige un gamma impair :
    // « 42,0 » est refusé (ArgumentException « Gamma must be odd », qui fait tomber tout le processus de test).
    private const string Seed = "42,1";
    private const int Cases = 200;

    private const int DailyLimit = 200;
    private const int Capacity = 5;
    private const int MaxNotesPerPass = 4;

    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static int FeeFor(int amount) => amount > 100 ? 5 : 0;

    private static bool IsValidAmount(int amount) => amount > 0 && amount % 10 == 0;

    private static readonly Denomination[] Denominations = [Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    // ==== Situations ====

    /// <summary>L'état de départ : un compte, et le module de billets (stock et cassette bloquée).</summary>
    public sealed record Situation(AccountState Account, NoteCounts Stock, Denomination? Jammed)
    {
        public MoneyDispenser NewDispenser() => new(Stock, Jammed);

        /// <summary>Le stock utilisable : la cassette bloquée compte pour zéro.</summary>
        public NoteCounts Available => Usable(Stock, Jammed);

        public override string ToString() =>
            $"solde {Account.Balance}, retiré {Account.WithdrawnToday}, stock 50×{Stock.Fifties} 20×{Stock.Twenties} 10×{Stock.Tens}, "
            + $"bloquée {Jammed?.ToString() ?? "aucune"}";
    }

    public sealed record Case(Situation Situation, Operation Operation)
    {
        public override string ToString() => $"{Situation} ; {Describe(Operation)}";
    }

    private static string Describe(Operation operation) => operation switch
    {
        Operation.Withdraw { Amount: var amount } => $"retrait {amount}",
        Operation.Deposit { Notes: var n } => $"dépôt 50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}",
        _ => operation.ToString(),
    };

    private static NoteCounts Usable(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    private static bool FitsIn(NoteCounts notes, NoteCounts stock) =>
        notes.Fifties <= stock.Fifties && notes.Twenties <= stock.Twenties && notes.Tens <= stock.Tens;

    /// <summary>Toutes les combinaisons tirées de <paramref name="available"/> qui font exactement <paramref name="amount"/>.</summary>
    private static List<NoteCounts> ExactCombinations(int amount, NoteCounts available)
    {
        var found = new List<NoteCounts>();
        for (var a = 0; a <= available.Fifties; a++)
            for (var b = 0; b <= available.Twenties; b++)
                for (var c = 0; c <= available.Tens; c++)
                    if (a * 50 + b * 20 + c * 10 == amount)
                        found.Add(N(a, b, c));

        return found;
    }

    // ==== Générateurs du domaine ====

    private static readonly int[] DomainAmounts = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly Gen<int> Balance = Gen.Choose(0, 50).Select(i => i * 5);

    private static readonly Gen<int> Withdrawn = Gen.Choose(0, 20).Select(i => i * 10);

    private static readonly Gen<AccountState> Account =
        from balance in Balance
        from withdrawn in Withdrawn
        select new AccountState(balance, withdrawn);

    /// <summary>Le nombre de billets d'une cassette, avec un poids renforcé sur la cassette vide et la cassette pleine.</summary>
    private static readonly Gen<int> CassetteCount = Gen.Frequency(
        (2, Gen.Choose(0, Capacity)),
        (1, Gen.Elements(0, Capacity)));

    private static readonly Gen<NoteCounts> Stock =
        from a in CassetteCount
        from b in CassetteCount
        from c in CassetteCount
        select N(a, b, c);

    private static readonly Gen<Denomination?> Jam =
        Gen.Elements<Denomination?>(null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten);

    private static readonly Gen<Situation> AnySituation =
        from account in Account
        from stock in Stock
        from jammed in Jam
        select new Situation(account, stock, jammed);

    /// <summary>Un montant du domaine ; les trois montants invalides (-10, 0, 5) sortent une fois sur quatre.</summary>
    private static readonly Gen<int> Amount = Gen.Frequency(
        (1, Gen.Elements(DomainAmounts.Where(a => !IsValidAmount(a)).ToArray())),
        (3, Gen.Elements(DomainAmounts.Where(IsValidAmount).ToArray())));

    /// <summary>Un dépôt du domaine : 0 ou 1 billet par coupure (le dépôt vide compris).</summary>
    private static readonly Gen<NoteCounts> DepositNotes =
        from a in Gen.Choose(0, 1)
        from b in Gen.Choose(0, 1)
        from c in Gen.Choose(0, 1)
        select N(a, b, c);

    /// <summary>Un retrait tiré uniformément dans le domaine : la plupart sont refusés par le plafond du jour.</summary>
    private static readonly Gen<(Situation Situation, int Amount)> UniformWithdrawal =
        from situation in AnySituation
        from amount in Amount
        select (situation, amount);

    /// <summary>Un dépôt tiré uniformément dans le domaine (le dépôt vide compris).</summary>
    private static readonly Gen<(Situation Situation, NoteCounts Notes)> UniformDeposit =
        from situation in AnySituation
        from notes in DepositNotes
        select (situation, notes);

    /// <summary>Un retrait valide qui respecte le plafond du jour ; le solde, lui, est quelconque.</summary>
    private static readonly Gen<(Situation Situation, int Amount)> WithinLimitWithdrawal =
        from amount in Gen.Choose(1, DailyLimit / 10).Select(i => i * 10)
        from withdrawn in Gen.Choose(0, (DailyLimit - amount) / 10).Select(i => i * 10)
        from balance in Balance
        from stock in Stock
        from jammed in Jam
        select (new Situation(new AccountState(balance, withdrawn), stock, jammed), amount);

    /// <summary>
    /// Un retrait qui passe les contrôles 1 à 3 : montant valide, plafond du jour respecté, solde suffisant frais compris.
    /// Seul le stock peut encore le refuser.
    /// </summary>
    private static readonly Gen<(Situation Situation, int Amount)> AdmissibleWithdrawal =
        from amount in Gen.Choose(1, DailyLimit / 10).Select(i => i * 10)
        from withdrawn in Gen.Choose(0, (DailyLimit - amount) / 10).Select(i => i * 10)
        from balance in Gen.Choose((amount + FeeFor(amount)) / 5, 50).Select(i => i * 5)
        from stock in Stock
        from jammed in Jam
        select (new Situation(new AccountState(balance, withdrawn), stock, jammed), amount);

    /// <summary>
    /// Un retrait admissible que le stock peut servir : on tire d'abord une combinaison hors de la cassette bloquée,
    /// puis un stock qui la contient. La spécification attend alors <see cref="AtmStatus.Ok"/>.
    /// </summary>
    private static readonly Gen<(Situation Situation, int Amount)> ServableWithdrawal =
        from jammed in Jam
        from combination in Stock
            .Select(c => Usable(c, jammed))
            .Where(c => c.Value is > 0 and <= DailyLimit)
        from extra in Stock
        let stock = N(
            Math.Max(combination.Fifties, extra.Fifties),
            Math.Max(combination.Twenties, extra.Twenties),
            Math.Max(combination.Tens, extra.Tens))
        let amount = combination.Value
        from withdrawn in Gen.Choose(0, (DailyLimit - amount) / 10).Select(i => i * 10)
        from balance in Gen.Choose((amount + FeeFor(amount)) / 5, 50).Select(i => i * 5)
        select (new Situation(new AccountState(balance, withdrawn), stock, jammed), amount);

    /// <summary>Un dépôt que la spécification accepte : non vide, hors de la cassette bloquée, sans déborder.</summary>
    private static readonly Gen<(Situation Situation, NoteCounts Notes)> AcceptedDeposit =
        from notes in DepositNotes.Where(n => n.Total > 0)
        from jammed in Jam.Where(j => j is not { } d || notes.Of(d) == 0)
        from stock in Stock.Select(s => N(
            Math.Min(s.Fifties, Capacity - notes.Fifties),
            Math.Min(s.Twenties, Capacity - notes.Twenties),
            Math.Min(s.Tens, Capacity - notes.Tens)))
        from account in Account
        select (new Situation(account, stock, jammed), notes);

    /// <summary>La combinaison attendue, trouvée par énumération : la plus courte parmi les combinaisons exactes.</summary>
    private static NoteCounts? Fewest(int amount, NoteCounts available) =>
        ExactCombinations(amount, available) is { Count: > 0 } exact ? exact.MinBy(c => c.Total) : null;

    private static bool IsDepositable(NoteCounts notes) => notes is { Fifties: <= 1, Twenties: <= 1, Tens: <= 1 };

    /// <summary>Un retrait servable dont les billets attendus (au plus 1 par coupure) forment un dépôt du domaine.</summary>
    private static readonly Gen<(Situation Situation, int Amount)> RedepositableWithdrawal =
        ServableWithdrawal.Where(w => Fewest(w.Amount, w.Situation.Available) is { } notes && IsDepositable(notes));

    /// <summary>Deux dépôts que la spécification accepte l'un après l'autre, dans un ordre comme dans l'autre.</summary>
    private static readonly Gen<(Situation Situation, NoteCounts First, NoteCounts Second)> TwoAcceptedDeposits =
        from first in DepositNotes.Where(n => n.Total > 0)
        from second in DepositNotes.Where(n => n.Total > 0)
        let both = first + second
        from jammed in Jam.Where(j => j is not { } d || both.Of(d) == 0)
        from stock in Stock.Select(s => N(
            Math.Min(s.Fifties, Capacity - both.Fifties),
            Math.Min(s.Twenties, Capacity - both.Twenties),
            Math.Min(s.Tens, Capacity - both.Tens)))
        from account in Account
        select (new Situation(account, stock, jammed), first, second);

    /// <summary>
    /// Un retrait du domaine : un tiers tiré uniformément, un tiers dans le plafond, un tiers admissible,
    /// pour que chaque statut, succès compris, sorte souvent.
    /// </summary>
    private static readonly Gen<(Situation Situation, int Amount)> AnyWithdrawal =
        Gen.OneOf(UniformWithdrawal, WithinLimitWithdrawal, AdmissibleWithdrawal);

    /// <summary>Un dépôt du domaine : moitié tiré uniformément, moitié accepté.</summary>
    private static readonly Gen<(Situation Situation, NoteCounts Notes)> AnyDeposit = Gen.OneOf(UniformDeposit, AcceptedDeposit);

    /// <summary>Une opération quelconque du domaine, retrait ou dépôt.</summary>
    private static readonly Gen<Case> AnyCase = Gen.OneOf(
        AnyWithdrawal.Select(w => new Case(w.Situation, new Operation.Withdraw(w.Amount))),
        AnyDeposit.Select(d => new Case(d.Situation, new Operation.Deposit(d.Notes))));

    private static Arbitrary<T> Arb<T>(Gen<T> gen) => gen.ToArbitrary();

    private static (AtmResult Result, MoneyDispenser Dispenser) Run(Situation situation, Operation operation)
    {
        var dispenser = situation.NewDispenser();
        var result = new CashMachine(dispenser).Execute(situation.Account, operation);

        return (result, dispenser);
    }

    private static (AtmResult Result, MoneyDispenser Dispenser) Withdraw(Situation situation, int amount) =>
        Run(situation, new Operation.Withdraw(amount));

    private static (AtmResult Result, MoneyDispenser Dispenser) Deposit(Situation situation, NoteCounts notes) =>
        Run(situation, new Operation.Deposit(notes));

    private static void AssertNothingChanged(AtmResult result, Situation situation, IMoneyDispenser dispenser)
    {
        Assert.Equal(NoteCounts.None, result.Notes);
        Assert.Equal(0, result.Fee);
        Assert.Equal(situation.Account, result.Account);
        Assert.Equal(situation.Stock, dispenser.Stock);
        Assert.Equal(situation.Jammed, dispenser.Jammed);
    }

    // ==== Propriétés communes aux deux opérations ====

    private static readonly AtmStatus[] WithdrawStatuses =
        [AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.DailyLimitExceeded, AtmStatus.InsufficientFunds, AtmStatus.CannotDispense];

    private static readonly AtmStatus[] DepositStatuses =
        [AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.CassetteJammed, AtmStatus.CassetteFull];

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property In_the_domain_the_real_module_never_faults_and_the_status_belongs_to_the_operation() =>
        Prop.ForAll(Arb(AnyCase), @case =>
        {
            var (result, _) = Run(@case.Situation, @case.Operation);

            Assert.NotEqual(AtmStatus.DispenserFault, result.Status);
            Assert.Contains(result.Status, @case.Operation is Operation.Withdraw ? WithdrawStatuses : DepositStatuses);
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property A_refusal_changes_nothing() =>
        Prop.ForAll(Arb(AnyCase), @case =>
        {
            var (result, dispenser) = Run(@case.Situation, @case.Operation);

            if (result.Status != AtmStatus.Ok)
            {
                AssertNothingChanged(result, @case.Situation, dispenser);
            }
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Money_is_conserved_between_the_module_and_the_account() =>
        Prop.ForAll(Arb(AnyCase), @case =>
        {
            var (result, dispenser) = Run(@case.Situation, @case.Operation);
            var before = @case.Situation;

            var deposited = @case.Operation is Operation.Deposit { Notes: var notes } && result.Status == AtmStatus.Ok
                ? notes
                : NoteCounts.None;

            // Les billets sortis et entrés expliquent exactement la variation du stock…
            Assert.Equal(before.Stock - result.Notes + deposited, dispenser.Stock);
            // …et le compte suit les billets, frais en plus ; le plafond du jour ne compte que les billets sortis.
            Assert.Equal(before.Account.Balance - result.Notes.Value - result.Fee + deposited.Value, result.Account.Balance);
            Assert.Equal(before.Account.WithdrawnToday + result.Notes.Value, result.Account.WithdrawnToday);
            Assert.Equal(before.Jammed, dispenser.Jammed);
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property The_machine_has_no_memory_between_calls() =>
        Prop.ForAll(Arb(from history in AnyCase.ArrayOf() from final in AnyCase select (history, final)), input =>
        {
            var (history, final) = input;

            // Une machine qui a déjà servi, sur un module qu'elle a elle-même modifié…
            var dispenser = final.Situation.NewDispenser();
            var used = new CashMachine(dispenser);
            foreach (var past in history)
            {
                used.Execute(past.Situation.Account, past.Operation);
            }

            var stockNow = dispenser.Stock;
            var afterHistory = used.Execute(final.Situation.Account, final.Operation);

            // …répond comme une machine neuve sur un module neuf dans le même état.
            var fresh = new Situation(final.Situation.Account, stockNow, final.Situation.Jammed);
            var (expected, freshDispenser) = Run(fresh, final.Operation);

            Assert.Equal(expected, afterHistory);
            Assert.Equal(freshDispenser.Stock, dispenser.Stock);
        });

    // ==== Retrait : l'ordre des contrôles ====

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Withdraw_amount_is_invalid_exactly_when_not_a_positive_multiple_of_ten() =>
        Prop.ForAll(Arb(AnyWithdrawal), input =>
        {
            var (result, _) = Withdraw(input.Situation, input.Amount);

            Assert.Equal(!IsValidAmount(input.Amount), result.Status == AtmStatus.InvalidAmount);
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Valid_withdrawal_over_the_daily_limit_is_refused_whatever_the_balance_and_stock() =>
        Prop.ForAll(Arb(AnyWithdrawal), input =>
        {
            var (situation, amount) = input;
            var (result, _) = Withdraw(situation, amount);

            var overLimit = IsValidAmount(amount) && situation.Account.WithdrawnToday + amount > DailyLimit;
            Assert.Equal(overLimit, result.Status == AtmStatus.DailyLimitExceeded);
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Withdrawal_within_the_limit_but_beyond_the_funds_is_refused_whatever_the_stock() =>
        Prop.ForAll(Arb(AnyWithdrawal), input =>
        {
            var (situation, amount) = input;
            var (result, _) = Withdraw(situation, amount);

            var insufficient = IsValidAmount(amount)
                && situation.Account.WithdrawnToday + amount <= DailyLimit
                && amount + FeeFor(amount) > situation.Account.Balance;
            Assert.Equal(insufficient, result.Status == AtmStatus.InsufficientFunds);
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Admissible_withdrawal_is_refused_exactly_when_no_combination_of_available_notes_fits() =>
        Prop.ForAll(Arb(AdmissibleWithdrawal), input =>
        {
            var (situation, amount) = input;
            var (result, _) = Withdraw(situation, amount);

            var possible = ExactCombinations(amount, situation.Available).Count > 0;
            Assert.Equal(possible ? AtmStatus.Ok : AtmStatus.CannotDispense, result.Status);
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property The_limit_and_the_funds_are_inclusive_and_the_fee_stays_out_of_the_limit() =>
        Prop.ForAll(Arb(ServableWithdrawal), input =>
        {
            var (situation, amount) = input;
            var fee = FeeFor(amount);
            var exact = situation with { Account = new AccountState(amount + fee, DailyLimit - amount) };

            // Pile au plafond (les frais n'y comptent pas) et pile au solde (frais compris) : accepté.
            Assert.Equal(AtmStatus.Ok, Withdraw(exact, amount).Result.Status);

            // 10 € de trop dans le plafond, 5 € de moins sur le solde : refusé.
            if (exact.Account.WithdrawnToday + 10 <= DailyLimit)
            {
                var over = exact with { Account = exact.Account with { WithdrawnToday = exact.Account.WithdrawnToday + 10 } };
                Assert.Equal(AtmStatus.DailyLimitExceeded, Withdraw(over, amount).Result.Status);
            }

            var poorer = exact with { Account = exact.Account with { Balance = exact.Account.Balance - 5 } };
            Assert.Equal(AtmStatus.InsufficientFunds, Withdraw(poorer, amount).Result.Status);
        });

    // ==== Retrait : le succès ====

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Servable_withdrawal_succeeds_and_pays_out_exactly_the_amount() =>
        Prop.ForAll(Arb(ServableWithdrawal), input =>
        {
            var (situation, amount) = input;
            var (result, dispenser) = Withdraw(situation, amount);
            var fee = FeeFor(amount);

            Assert.Equal(AtmStatus.Ok, result.Status);
            Assert.Equal(amount, result.Notes.Value);
            Assert.True(FitsIn(result.Notes, situation.Available), "les billets viennent du stock utilisable");
            Assert.Equal(fee, result.Fee);
            Assert.Equal(
                new AccountState(situation.Account.Balance - amount - fee, situation.Account.WithdrawnToday + amount),
                result.Account);
            Assert.Equal(situation.Stock - result.Notes, dispenser.Stock);
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property The_fee_is_five_euros_exactly_above_one_hundred() =>
        Prop.ForAll(Arb(ServableWithdrawal), input =>
        {
            var (result, _) = Withdraw(input.Situation, input.Amount);

            Assert.Equal(input.Amount > 100 ? 5 : 0, result.Fee);
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property The_combination_is_the_unique_one_with_the_fewest_notes() =>
        Prop.ForAll(Arb(ServableWithdrawal), input =>
        {
            var (situation, amount) = input;
            var (result, _) = Withdraw(situation, amount);

            foreach (var other in ExactCombinations(amount, situation.Available))
            {
                Assert.True(other.Total >= result.Notes.Total, $"{other} compte moins de billets que {result.Notes}");
                if (other.Total == result.Notes.Total)
                {
                    Assert.Equal(result.Notes, other);
                }
            }
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property A_successful_withdrawal_keeps_the_account_in_the_domain() =>
        Prop.ForAll(Arb(AnyWithdrawal), input =>
        {
            var (result, _) = Withdraw(input.Situation, input.Amount);
            var after = result.Account;

            Assert.True(after.Balance is >= 0 and <= 250 && after.Balance % 5 == 0, $"solde {after.Balance}");
            Assert.True(after.WithdrawnToday is >= 0 and <= DailyLimit && after.WithdrawnToday % 10 == 0, $"retiré {after.WithdrawnToday}");
        });

    // ==== Retrait : propriétés métamorphiques ====

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property A_richer_account_gets_the_same_withdrawal() =>
        Prop.ForAll(Arb(from w in AnyWithdrawal from extra in Gen.Choose(0, 50) select (w.Situation, w.Amount, Extra: extra * 5)), input =>
        {
            var (situation, amount, extra) = input;
            var richer = situation with { Account = situation.Account with { Balance = Math.Min(250, situation.Account.Balance + extra) } };

            var (result, _) = Withdraw(situation, amount);
            var (richerResult, _) = Withdraw(richer, amount);

            if (result.Status == AtmStatus.Ok)
            {
                Assert.Equal(AtmStatus.Ok, richerResult.Status);
                Assert.Equal(result.Notes, richerResult.Notes);
                Assert.Equal(result.Fee, richerResult.Fee);
                Assert.Equal(richer.Account.Balance - situation.Account.Balance, richerResult.Account.Balance - result.Account.Balance);
            }
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Having_withdrawn_less_today_never_turns_a_success_into_a_refusal() =>
        Prop.ForAll(Arb(from w in AnyWithdrawal from less in Gen.Choose(0, 20) select (w.Situation, w.Amount, Less: less * 10)), input =>
        {
            var (situation, amount, less) = input;
            var fresher = situation with
            {
                Account = situation.Account with { WithdrawnToday = Math.Max(0, situation.Account.WithdrawnToday - less) },
            };

            var (result, _) = Withdraw(situation, amount);
            var (fresherResult, _) = Withdraw(fresher, amount);

            if (result.Status == AtmStatus.Ok)
            {
                Assert.Equal(AtmStatus.Ok, fresherResult.Status);
                Assert.Equal(result.Notes, fresherResult.Notes);
                Assert.Equal(result.Fee, fresherResult.Fee);
            }
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property More_notes_in_the_module_never_turn_a_success_into_a_refusal_nor_need_more_notes() =>
        Prop.ForAll(Arb(from w in AnyWithdrawal from added in Stock select (w.Situation, w.Amount, Added: added)), input =>
        {
            var (situation, amount, added) = input;
            var fuller = situation with
            {
                Stock = N(
                    Math.Min(Capacity, situation.Stock.Fifties + added.Fifties),
                    Math.Min(Capacity, situation.Stock.Twenties + added.Twenties),
                    Math.Min(Capacity, situation.Stock.Tens + added.Tens)),
            };

            var (result, _) = Withdraw(situation, amount);
            var (fullerResult, _) = Withdraw(fuller, amount);

            if (result.Status == AtmStatus.Ok)
            {
                Assert.Equal(AtmStatus.Ok, fullerResult.Status);
                Assert.True(fullerResult.Notes.Total <= result.Notes.Total);
                Assert.Equal(result.Fee, fullerResult.Fee);
                Assert.Equal(result.Account, fullerResult.Account);
            }
            else if (fullerResult.Status != AtmStatus.Ok)
            {
                // Plus de billets ne change que le dernier contrôle : les autres refus restent les mêmes.
                Assert.Equal(result.Status, fullerResult.Status);
            }
            else
            {
                Assert.Equal(AtmStatus.CannotDispense, result.Status);
            }
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property A_jammed_cassette_withdraws_like_an_empty_cassette() =>
        Prop.ForAll(Arb(AnyWithdrawal), input =>
        {
            var (situation, amount) = input;
            var (jammedResult, jammedDispenser) = Withdraw(situation, amount);
            var (emptyResult, emptyDispenser) = Withdraw(situation with { Stock = situation.Available, Jammed = null }, amount);

            Assert.Equal(emptyResult, jammedResult);
            // La cassette bloquée garde ses billets ; le reste du stock évolue comme si elle était vide.
            Assert.Equal(situation.Stock - situation.Available + emptyDispenser.Stock, jammedDispenser.Stock);
        });

    // ==== Retrait : le passage par le module ====

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Notes_leave_in_the_fewest_passes_of_at_most_four_notes_from_the_available_stock() =>
        Prop.ForAll(Arb(AnyWithdrawal), input =>
        {
            var (situation, amount) = input;
            var recorder = new RecordingDispenser(situation.NewDispenser());
            var result = new CashMachine(recorder).Execute(situation.Account, new Operation.Withdraw(amount));

            Assert.Empty(recorder.Loads);
            if (result.Status != AtmStatus.Ok)
            {
                Assert.Empty(recorder.Passes);
                return;
            }

            Assert.All(recorder.Passes, pass =>
            {
                Assert.InRange(pass.Total, 1, MaxNotesPerPass);
                Assert.True(pass is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0 }, $"passage {pass}");
            });
            Assert.Equal(result.Notes, recorder.Passes.Aggregate(NoteCounts.None, (sum, pass) => sum + pass));
            // « autant de passages que nécessaire » : pas un de plus.
            Assert.Equal((result.Notes.Total + MaxNotesPerPass - 1) / MaxNotesPerPass, recorder.Passes.Count);
        });

    // ==== Dépôt ====

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Deposit_status_is_the_first_failing_check() =>
        Prop.ForAll(Arb(AnyDeposit), input =>
        {
            var (situation, notes) = input;
            var (result, _) = Deposit(situation, notes);
            var after = situation.Stock + notes;

            var expected =
                notes.Total == 0 ? AtmStatus.InvalidAmount
                : situation.Jammed is { } jammed && notes.Of(jammed) > 0 ? AtmStatus.CassetteJammed
                : Denominations.Any(d => after.Of(d) > Capacity) ? AtmStatus.CassetteFull
                : AtmStatus.Ok;
            Assert.Equal(expected, result.Status);
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Successful_deposit_credits_the_balance_and_fills_the_module() =>
        Prop.ForAll(Arb(AnyDeposit), input =>
        {
            var (situation, notes) = input;
            var recorder = new RecordingDispenser(situation.NewDispenser());
            var result = new CashMachine(recorder).Execute(situation.Account, new Operation.Deposit(notes));

            Assert.Empty(recorder.Passes);
            if (result.Status != AtmStatus.Ok)
            {
                Assert.Empty(recorder.Loads);
                return;
            }

            Assert.Equal(
                new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, situation.Account with { Balance = situation.Account.Balance + notes.Value }),
                result);
            Assert.Equal(notes, recorder.Loads.Aggregate(NoteCounts.None, (sum, load) => sum + load));
            Assert.Equal(situation.Stock + notes, recorder.Stock);
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Two_deposits_commute() =>
        Prop.ForAll(Arb(TwoAcceptedDeposits), input =>
        {
            var (situation, first, second) = input;

            (AtmStatus, AtmStatus, AccountState, NoteCounts) Both(NoteCounts x, NoteCounts y)
            {
                var dispenser = situation.NewDispenser();
                var machine = new CashMachine(dispenser);
                var r1 = machine.Execute(situation.Account, new Operation.Deposit(x));
                var r2 = machine.Execute(r1.Account, new Operation.Deposit(y));
                return (r1.Status, r2.Status, r2.Account, dispenser.Stock);
            }

            var (a1, a2, accountA, stockA) = Both(first, second);
            var (b1, b2, accountB, stockB) = Both(second, first);

            Assert.Equal((AtmStatus.Ok, AtmStatus.Ok), (a1, a2));
            Assert.Equal((AtmStatus.Ok, AtmStatus.Ok), (b1, b2));
            Assert.Equal(accountA, accountB);
            Assert.Equal(stockA, stockB);
            Assert.Equal(situation.Stock + first + second, stockA);
        });

    // ==== Allers-retours entre retrait et dépôt ====

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Depositing_back_the_withdrawn_notes_restores_the_module_and_costs_only_the_fee() =>
        Prop.ForAll(Arb(RedepositableWithdrawal), input =>
        {
            var (situation, amount) = input;
            var dispenser = situation.NewDispenser();
            var machine = new CashMachine(dispenser);

            var withdrawal = machine.Execute(situation.Account, new Operation.Withdraw(amount));
            var notes = withdrawal.Notes;
            Assert.Equal(AtmStatus.Ok, withdrawal.Status);
            Assert.True(IsDepositable(notes), $"{notes} n'est pas un dépôt du domaine");

            var deposit = machine.Execute(withdrawal.Account, new Operation.Deposit(notes));

            Assert.Equal(AtmStatus.Ok, deposit.Status);
            Assert.Equal(situation.Stock, dispenser.Stock);
            Assert.Equal(
                new AccountState(situation.Account.Balance - withdrawal.Fee, situation.Account.WithdrawnToday + amount),
                deposit.Account);
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property Deposited_notes_can_always_be_withdrawn_right_away() =>
        Prop.ForAll(Arb(AcceptedDeposit), input =>
        {
            var (situation, notes) = input;
            var room = DailyLimit - notes.Value;
            var account = situation.Account with { WithdrawnToday = Math.Min(situation.Account.WithdrawnToday, room - room % 10) };
            var dispenser = situation.NewDispenser();
            var machine = new CashMachine(dispenser);

            var deposit = machine.Execute(account, new Operation.Deposit(notes));
            Assert.Equal(AtmStatus.Ok, deposit.Status);

            // Les billets déposés sont hors de la cassette bloquée, et le solde vient d'augmenter de leur valeur.
            var withdrawal = machine.Execute(deposit.Account, new Operation.Withdraw(notes.Value));

            Assert.Equal(AtmStatus.Ok, withdrawal.Status);
            Assert.True(withdrawal.Notes.Total <= notes.Total);
            Assert.Equal(account.Balance, withdrawal.Account.Balance);
        });

    // ==== Pannes du module ====

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property A_dispenser_fault_stops_at_once_and_reports_only_the_notes_already_out() =>
        Prop.ForAll(Arb(from w in ServableWithdrawal from k in Gen.Choose(1, 100) select (w.Situation, w.Amount, K: k)), input =>
        {
            var (situation, amount, k) = input;

            // Combien de passages ce retrait demande-t-il ?
            var probe = new RecordingDispenser(situation.NewDispenser());
            new CashMachine(probe).Execute(situation.Account, new Operation.Withdraw(amount));
            var failOn = 1 + (k - 1) % probe.Passes.Count;

            var faulty = new FaultyDispenser(situation.NewDispenser(), failOnDispense: failOn);
            var result = new CashMachine(faulty).Execute(situation.Account, new Operation.Withdraw(amount));

            Assert.Equal(AtmStatus.DispenserFault, result.Status);
            Assert.Equal(0, result.Fee);
            Assert.Equal(situation.Account, result.Account);
            Assert.Equal(failOn, faulty.DispenseCalls);
            Assert.Equal(faulty.Dispensed, result.Notes);
            Assert.Equal(situation.Stock - result.Notes, faulty.Stock);
        });

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property A_load_fault_returns_no_notes_and_leaves_the_account_unchanged() =>
        Prop.ForAll(Arb(AcceptedDeposit), input =>
        {
            // Un dépôt que tous les contrôles acceptent : seul le module peut encore le faire échouer.
            var (situation, notes) = input;

            var faulty = new FaultyDispenser(situation.NewDispenser(), failLoad: true);
            var result = new CashMachine(faulty).Execute(situation.Account, new Operation.Deposit(notes));

            Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, situation.Account), result);
            Assert.Equal(situation.Stock, faulty.Stock);
            Assert.Equal(0, faulty.DispenseCalls);
        });

    // ==== La spécification elle-même ====

    [Property(Replay = Seed, MaxTest = Cases)]
    public Property With_these_denominations_the_shortest_combination_is_always_unique() =>
        Prop.ForAll(Arb(from stock in Stock from amount in Gen.Choose(1, 40).Select(i => i * 10) select (stock, amount)), input =>
        {
            var exact = ExactCombinations(input.amount, input.stock);
            if (exact.Count > 0)
            {
                var fewest = exact.Min(c => c.Total);
                Assert.Single(exact, c => c.Total == fewest);
            }
        });

    // ==== Doublures ====

    /// <summary>Enregistre chaque appel avant de le transmettre au vrai module.</summary>
    private sealed class RecordingDispenser(IMoneyDispenser inner) : IMoneyDispenser
    {
        public List<NoteCounts> Passes { get; } = [];

        public List<NoteCounts> Loads { get; } = [];

        public NoteCounts Stock => inner.Stock;

        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            Passes.Add(notes);
            return inner.Dispense(notes);
        }

        public bool Load(NoteCounts notes)
        {
            Loads.Add(notes);
            return inner.Load(notes);
        }
    }

    /// <summary>Le vrai module, qui tombe en panne au n-ième passage de sortie, ou à tout dépôt.</summary>
    private sealed class FaultyDispenser(IMoneyDispenser inner, int failOnDispense = 0, bool failLoad = false) : IMoneyDispenser
    {
        public int DispenseCalls { get; private set; }

        public NoteCounts Dispensed { get; private set; }

        public NoteCounts Stock => inner.Stock;

        public Denomination? Jammed => inner.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            DispenseCalls++;
            if (DispenseCalls == failOnDispense || !inner.Dispense(notes))
            {
                return false;
            }

            Dispensed += notes;
            return true;
        }

        public bool Load(NoteCounts notes) => !failLoad && inner.Load(notes);
    }
}
