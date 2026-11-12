using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Atm.Tests;

// ====================================================================================================
// Valeurs du domaine de la spécification, tirées au hasard par FsCheck
// ====================================================================================================

/// <summary>Un compte, un stock et une cassette bloquée, tous pris dans le domaine de la spécification.</summary>
public sealed record Situation(AccountState Account, NoteCounts Stock, Denomination? Jammed)
{
    public Situation WithAccount(AccountState account) => this with { Account = account };

    public Situation WithStock(NoteCounts stock) => this with { Stock = stock };

    public Situation WithJam(Denomination? jammed) => this with { Jammed = jammed };

    /// <summary>Exécute l'opération sur une machine neuve et un vrai module : rend le résultat et le stock après coup.</summary>
    public (AtmResult Result, NoteCounts StockAfter) Run(Operation operation)
    {
        var dispenser = new MoneyDispenser(Stock, Jammed);
        var result = new CashMachine(dispenser).Execute(Account, operation);

        return (result, dispenser.Stock);
    }

    /// <summary>Comme <see cref="Run"/>, avec un espion qui note chaque appel au module.</summary>
    public (AtmResult Result, Espion Espion) RunSpied(Operation operation)
    {
        var espion = new Espion(new MoneyDispenser(Stock, Jammed));
        var result = new CashMachine(espion).Execute(Account, operation);

        return (result, espion);
    }

    public override string ToString() =>
        $"solde {Account.Balance}, retiré {Account.WithdrawnToday}, " +
        $"stock 50×{Stock.Fifties} 20×{Stock.Twenties} 10×{Stock.Tens}, bloquée {Jammed?.ToString() ?? "aucune"}";
}

/// <summary>Un montant de retrait du domaine : -10, 0, 5 et les multiples de 10 de 10 à 220.</summary>
public readonly record struct Montant(int Valeur)
{
    public Operation Retrait => new Operation.Withdraw(Valeur);

    public override string ToString() => $"retrait {Valeur}";
}

/// <summary>Un dépôt du domaine : 0 ou 1 billet par coupure (le dépôt vide compris).</summary>
public readonly record struct Depot(NoteCounts Billets)
{
    public Operation Operation => new Operation.Deposit(Billets);

    public override string ToString() => $"dépôt 50×{Billets.Fifties} 20×{Billets.Twenties} 10×{Billets.Tens}";
}

/// <summary>Un dépôt du domaine d'au moins deux billets, coupé en deux dépôts non vides et disjoints.</summary>
public readonly record struct DepotPartage(NoteCounts Premier, NoteCounts Second)
{
    public override string ToString() => $"{new Depot(Premier)} puis {new Depot(Second)}";
}

/// <summary>Des billets à ajouter à un stock (le total reste plafonné à 5 par cassette).</summary>
public readonly record struct Ajout(NoteCounts Billets)
{
    public override string ToString() => $"ajout 50×{Billets.Fifties} 20×{Billets.Twenties} 10×{Billets.Tens}";
}

/// <summary>Une opération quelconque du domaine : retrait ou dépôt.</summary>
public readonly record struct UneOperation(Operation Operation)
{
    public override string ToString() => Operation switch
    {
        Operation.Withdraw w => new Montant(w.Amount).ToString(),
        Operation.Deposit d => new Depot(d.Notes).ToString(),
        _ => Operation.ToString(),
    };
}

/// <summary>Un écart de 1 à 50 pas, pour décaler un solde (pas de 5 €) ou un retrait du jour (pas de 10 €).</summary>
public readonly record struct Ecart(int Pas);

/// <summary>Un décalage de -2 à +2 pas autour d'une frontière (un seuil de solde ou de plafond du jour).</summary>
public readonly record struct AutourDuSeuil(int Pas);

/// <summary>
/// Un retrait qui demande beaucoup de billets, donc plusieurs passages : peu de billets de 50 (ou leur cassette
/// bloquée), des cassettes de 20 et de 10 bien garnies, un montant formé de billets disponibles (au moins deux de 20
/// et deux de 10) et un compte qui laisse passer les trois premiers contrôles.
/// </summary>
public sealed record RetraitLong(Situation Situation, int Montant)
{
    public Operation Retrait => new Operation.Withdraw(Montant);

    public override string ToString() => $"{Situation}, retrait {Montant}";
}

/// <summary>Le numéro (à partir de 1) de l'appel au module qui tombe en panne.</summary>
public readonly record struct PassageEnPanne(int Numero);

public static class Domaine
{
    public static readonly Denomination?[] Bloquees = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static readonly Denomination[] Coupures = [Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static readonly int[] Montants = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static Gen<NoteCounts> Stock =>
        from f in Gen.Choose(0, 5)
        from t in Gen.Choose(0, 5)
        from d in Gen.Choose(0, 5)
        select new NoteCounts(f, t, d);

    public static Gen<NoteCounts> BilletsDepot =>
        from f in Gen.Choose(0, 1)
        from t in Gen.Choose(0, 1)
        from d in Gen.Choose(0, 1)
        select new NoteCounts(f, t, d);

    public static Gen<AccountState> CompteQuelconque =>
        from b in Gen.Choose(0, 50)
        from w in Gen.Choose(0, 20)
        select new AccountState(b * 5, w * 10);

    /// <summary>Un compte qui passe souvent les trois premiers contrôles, pour atteindre le choix des billets.</summary>
    public static Gen<AccountState> CompteAise =>
        from b in Gen.Choose(45, 50)
        from w in Gen.Choose(0, 2)
        select new AccountState(b * 5, w * 10);

    public static Gen<Situation> Situation =>
        from compte in Gen.Frequency((1, CompteQuelconque), (1, CompteAise))
        from stock in Stock
        from bloquee in Gen.Elements(Bloquees)
        select new Situation(compte, stock, bloquee);
}

/// <summary>Les générateurs que FsCheck utilise pour les paramètres des propriétés.</summary>
public static class Generateurs
{
    public static Arbitrary<Situation> Situation() => Arb.From(Domaine.Situation);

    public static Arbitrary<Montant> Montant() => Arb.From(Gen.Elements(Domaine.Montants).Select(m => new Montant(m)));

    public static Arbitrary<Depot> Depot() => Arb.From(Domaine.BilletsDepot.Select(b => new Depot(b)));

    public static Arbitrary<Ajout> Ajout() => Arb.From(Domaine.Stock.Select(b => new Ajout(b)));

    public static Arbitrary<DepotPartage> DepotPartage() => Arb.From(
        from total in Domaine.BilletsDepot.Where(b => b.Total >= 2)
        from masque in Domaine.BilletsDepot
        let premier = new NoteCounts(
            total.Fifties & masque.Fifties, total.Twenties & masque.Twenties, total.Tens & masque.Tens)
        where premier.Total > 0 && premier != total
        select new DepotPartage(premier, total - premier));

    public static Arbitrary<UneOperation> UneOperation() => Arb.From(Gen.Frequency(
        (2, Gen.Elements(Domaine.Montants).Select(m => new UneOperation(new Operation.Withdraw(m)))),
        (1, Domaine.BilletsDepot.Select(b => new UneOperation(new Operation.Deposit(b))))));

    public static Arbitrary<Ecart> Ecart() => Arb.From(Gen.Choose(1, 50).Select(n => new Ecart(n)));

    public static Arbitrary<AutourDuSeuil> AutourDuSeuil() => Arb.From(Gen.Choose(-2, 2).Select(n => new AutourDuSeuil(n)));

    public static Arbitrary<RetraitLong> RetraitLong() => Arb.From(
        from f in Gen.Choose(0, 1)
        from t in Gen.Choose(3, 5)
        from d in Gen.Choose(3, 5)
        from bloquee in Gen.Elements<Denomination?>(null, Denomination.Fifty)
        from pris50 in Gen.Choose(0, bloquee is null ? f : 0)
        from pris20 in Gen.Choose(2, t)
        from pris10 in Gen.Choose(2, d)
        select new RetraitLong(
            new Situation(new AccountState(250, 0), new NoteCounts(f, t, d), bloquee),
            pris50 * 50 + pris20 * 20 + pris10 * 10));

    public static Arbitrary<PassageEnPanne> PassageEnPanne() => Arb.From(Gen.Choose(1, 3).Select(n => new PassageEnPanne(n)));
}

/// <summary>Un module qui délègue au vrai et note chaque appel avec sa réponse.</summary>
public sealed class Espion(IMoneyDispenser inner) : IMoneyDispenser
{
    public List<(NoteCounts Notes, bool Ok)> Dispenses { get; } = [];

    public List<(NoteCounts Notes, bool Ok)> Loads { get; } = [];

    public NoteCounts Stock => inner.Stock;

    public Denomination? Jammed => inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var ok = inner.Dispense(notes);
        Dispenses.Add((notes, ok));

        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        var ok = inner.Load(notes);
        Loads.Add((notes, ok));

        return ok;
    }
}

/// <summary>
/// Un vrai module qui tombe en panne : le n-ième appel à <c>Dispense</c> rend <c>false</c> sans rien sortir,
/// et <c>Load</c> rend toujours <c>false</c> sans rien ranger si on le demande.
/// </summary>
public sealed class ModuleEnPanne(NoteCounts stock, Denomination? jammed, int dispenseEnPanne, bool loadEnPanne) : IMoneyDispenser
{
    private readonly MoneyDispenser _inner = new(stock, jammed);

    public List<(NoteCounts Notes, bool Ok)> Dispenses { get; } = [];

    public int Loads { get; private set; }

    public NoteCounts Stock => _inner.Stock;

    public Denomination? Jammed => _inner.Jammed;

    public bool Dispense(NoteCounts notes)
    {
        var ok = Dispenses.Count + 1 != dispenseEnPanne && _inner.Dispense(notes);
        Dispenses.Add((notes, ok));

        return ok;
    }

    public bool Load(NoteCounts notes)
    {
        Loads++;

        return !loadEnPanne && _inner.Load(notes);
    }
}

// ====================================================================================================
// Les propriétés
// ====================================================================================================

/// <summary>
/// Tests de propriétés : des règles de la spécification qui doivent tenir pour toute entrée du domaine.
/// Chaque propriété est vérifiée sur <see cref="Cas"/> cas tirés avec la graine <see cref="Graine"/>, donc reproductibles.
/// </summary>
public class Proprietes
{
    // FsCheck 3 lit Replay comme « graine,gamma » et exige un gamma impair : "42,0" fait planter l'exécution
    // (« Gamma must be odd, given: 0 »). On garde la graine 42 avec le plus petit gamma valide.
    private const string Graine = "42,1";
    private const int Cas = 200;

    private static readonly AtmStatus[] StatutsRetrait =
        [AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.DailyLimitExceeded, AtmStatus.InsufficientFunds, AtmStatus.CannotDispense];

    private static readonly AtmStatus[] StatutsDepot =
        [AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.CassetteJammed, AtmStatus.CassetteFull];

    // ---------- Outils tirés directement de la spécification ----------

    private static int Frais(int amount) => amount > 100 ? 5 : 0;

    private static NoteCounts Disponible(NoteCounts stock, Denomination? jammed) => jammed switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    /// <summary>Toutes les combinaisons de billets pris dans <paramref name="disponible"/> qui font exactement le montant.</summary>
    private static List<NoteCounts> Combinaisons(int amount, NoteCounts disponible)
    {
        var combinaisons = new List<NoteCounts>();
        for (var f = 0; f <= disponible.Fifties; f++)
        for (var t = 0; t <= disponible.Twenties; t++)
        for (var d = 0; d <= disponible.Tens; d++)
        {
            var candidate = new NoteCounts(f, t, d);
            if (candidate.Value == amount)
            {
                combinaisons.Add(candidate);
            }
        }

        return combinaisons;
    }

    private static bool Contient(NoteCounts grand, NoteCounts petit) =>
        petit.Fifties <= grand.Fifties && petit.Twenties <= grand.Twenties && petit.Tens <= grand.Tens;

    private static bool EstPositif(NoteCounts notes) => notes is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0 };

    private static NoteCounts Plafonne(NoteCounts notes) =>
        new(Math.Min(notes.Fifties, 5), Math.Min(notes.Twenties, 5), Math.Min(notes.Tens, 5));

    private static bool DansLeDomaine(AccountState account) =>
        account.Balance is >= 0 and <= 250 && account.Balance % 5 == 0
        && account.WithdrawnToday is >= 0 and <= 200 && account.WithdrawnToday % 10 == 0;

    private static NoteCounts Somme(IEnumerable<NoteCounts> notes) => notes.Aggregate(NoteCounts.None, (a, b) => a + b);

    private static void AssertRefusSansEffet(Situation situation, AtmResult result, NoteCounts stockApres)
    {
        Assert.Equal(new AtmResult(result.Status, NoteCounts.None, 0, situation.Account), result);
        Assert.Equal(situation.Stock, stockApres);
    }

    // ====================================================================================================
    // Retrait
    // ====================================================================================================

    /// <summary>Le statut est celui du premier contrôle qui échoue, dans l'ordre de la spécification ; sinon Ok.</summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_LeStatutEstCeluiDuPremierControleQuiEchoue(Situation situation, Montant montant)
    {
        var (account, amount) = (situation.Account, montant.Valeur);
        var attendu =
            amount <= 0 || amount % 10 != 0 ? AtmStatus.InvalidAmount
            : account.WithdrawnToday + amount > 200 ? AtmStatus.DailyLimitExceeded
            : amount + Frais(amount) > account.Balance ? AtmStatus.InsufficientFunds
            : Combinaisons(amount, Disponible(situation.Stock, situation.Jammed)).Count == 0 ? AtmStatus.CannotDispense
            : AtmStatus.Ok;

        var (result, _) = situation.Run(montant.Retrait);

        Assert.Equal(attendu, result.Status);
        Assert.Contains(result.Status, StatutsRetrait);
    }

    /// <summary>
    /// Autour du seuil du solde : le retrait est refusé en InsufficientFunds exactement quand le montant plus les
    /// frais dépasse le solde. Le solde est tiré à quelques pas de 5 € de <c>montant + frais</c>.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_LeSoldeDoitCouvrirLeMontantEtLesFrais(Situation situation, Montant montant, AutourDuSeuil seuil)
    {
        var amount = montant.Valeur;
        if (amount <= 0 || amount % 10 != 0 || amount > 200)
        {
            return;
        }

        var balance = Math.Clamp(amount + Frais(amount) + seuil.Pas * 5, 0, 250);
        var auSeuil = situation.WithAccount(new AccountState(balance, 0));

        var (result, _) = auSeuil.Run(montant.Retrait);

        if (amount + Frais(amount) > balance)
        {
            Assert.Equal(AtmStatus.InsufficientFunds, result.Status);
        }
        else
        {
            Assert.Contains(result.Status, new[] { AtmStatus.Ok, AtmStatus.CannotDispense });
        }
    }

    /// <summary>
    /// Autour du plafond du jour : le retrait est refusé en DailyLimitExceeded exactement quand le retrait du jour
    /// plus le montant dépasse 200, frais non compris. Le retrait du jour est tiré à quelques pas de 10 € du seuil.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_LePlafondDuJourPorteSurLeMontantSansLesFrais(Situation situation, Montant montant, AutourDuSeuil seuil)
    {
        var amount = montant.Valeur;
        if (amount <= 0 || amount % 10 != 0)
        {
            return;
        }

        var withdrawn = Math.Clamp(200 - amount + seuil.Pas * 10, 0, 200);
        var auSeuil = situation.WithAccount(new AccountState(250, withdrawn));

        var (result, _) = auSeuil.Run(montant.Retrait);

        if (withdrawn + amount > 200)
        {
            Assert.Equal(AtmStatus.DailyLimitExceeded, result.Status);
        }
        else
        {
            Assert.Contains(result.Status, new[] { AtmStatus.Ok, AtmStatus.CannotDispense });
        }
    }

    /// <summary>Sur un refus, rien ne change : pas de billets, pas de frais, compte et stock identiques.</summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_UnRefusNeChangeRien(Situation situation, Montant montant)
    {
        var (result, stockApres) = situation.Run(montant.Retrait);

        if (result.Status != AtmStatus.Ok)
        {
            AssertRefusSansEffet(situation, result, stockApres);
        }
    }

    /// <summary>Un retrait réussi : frais, solde et retrait du jour bougent exactement comme la spécification le dit.</summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_UnSuccesDebiteMontantEtFrais(Situation situation, Montant montant)
    {
        var amount = montant.Valeur;
        var (result, _) = situation.Run(montant.Retrait);

        if (result.Status == AtmStatus.Ok)
        {
            Assert.Equal(Frais(amount), result.Fee);
            Assert.Equal(situation.Account.Balance - amount - Frais(amount), result.Account.Balance);
            Assert.Equal(situation.Account.WithdrawnToday + amount, result.Account.WithdrawnToday);
            Assert.True(DansLeDomaine(result.Account), $"compte hors domaine après un retrait : {result.Account}");
        }
    }

    /// <summary>
    /// Un retrait réussi sort exactement le montant, en billets disponibles (jamais de la cassette bloquée),
    /// et le stock perd exactement ces billets.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_LesBilletsSortisFontLeMontantEtViennentDuStockDisponible(Situation situation, Montant montant)
    {
        var (result, stockApres) = situation.Run(montant.Retrait);

        if (result.Status == AtmStatus.Ok)
        {
            Assert.Equal(montant.Valeur, result.Notes.Value);
            Assert.True(EstPositif(result.Notes), $"billets négatifs : {result.Notes}");
            Assert.True(Contient(Disponible(situation.Stock, situation.Jammed), result.Notes),
                $"{result.Notes} n'est pas pris dans le stock disponible");
            Assert.Equal(situation.Stock - result.Notes, stockApres);
        }
    }

    /// <summary>
    /// La combinaison rendue compte le moins de billets parmi toutes celles qui font le montant avec les billets
    /// disponibles, et c'est la seule de cette taille.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_LaCombinaisonEstLaPlusCourteEtUnique(Situation situation, Montant montant)
    {
        var (result, _) = situation.Run(montant.Retrait);

        if (result.Status == AtmStatus.Ok)
        {
            var toutes = Combinaisons(montant.Valeur, Disponible(situation.Stock, situation.Jammed));
            var plusCourte = toutes.Min(c => c.Total);

            Assert.Equal(plusCourte, result.Notes.Total);
            Assert.Equal(result.Notes, Assert.Single(toutes, c => c.Total == plusCourte));
        }
    }

    /// <summary>
    /// Les billets sortent en passages acceptés de 1 à 4 billets, dont la somme fait la combinaison ; un retrait ne
    /// range jamais rien, et un refus n'appelle jamais le module.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_LesBilletsSortentParPassagesDeQuatreAuPlus(Situation situation, Montant montant)
    {
        var (result, espion) = situation.RunSpied(montant.Retrait);

        Assert.Empty(espion.Loads);
        if (result.Status != AtmStatus.Ok)
        {
            Assert.Empty(espion.Dispenses);
            return;
        }

        Assert.All(espion.Dispenses, passage =>
        {
            Assert.True(passage.Ok, $"passage refusé par le module : {passage.Notes}");
            Assert.InRange(passage.Notes.Total, 1, 4);
        });
        Assert.Equal(result.Notes, Somme(espion.Dispenses.Select(p => p.Notes)));
    }

    /// <summary>
    /// Les trois premiers contrôles ne regardent pas le module : changer le stock ou la cassette bloquée ne fait
    /// passer de l'un à l'autre que Ok et CannotDispense.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_LesTroisPremiersControlesNeDependentPasDuModule(Situation situation, Situation autre, Montant montant)
    {
        var autreModule = situation.WithStock(autre.Stock).WithJam(autre.Jammed);

        var (result, _) = situation.Run(montant.Retrait);
        var (resultAutre, _) = autreModule.Run(montant.Retrait);

        AtmStatus[] dependDuModule = [AtmStatus.Ok, AtmStatus.CannotDispense];
        if (dependDuModule.Contains(result.Status))
        {
            Assert.Contains(resultAutre.Status, dependDuModule);
        }
        else
        {
            Assert.Equal(result, resultAutre);
        }
    }

    /// <summary>
    /// Plus de billets dans les cassettes ne nuit jamais : un retrait possible le reste, avec autant de billets
    /// ou moins, les mêmes frais et le même compte après.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_PlusDeStockNeNuitJamais(Situation situation, Ajout ajout, Montant montant)
    {
        var mieuxGarnie = situation.WithStock(Plafonne(situation.Stock + ajout.Billets));

        var (result, _) = situation.Run(montant.Retrait);
        var (resultGarni, _) = mieuxGarnie.Run(montant.Retrait);

        switch (result.Status)
        {
            case AtmStatus.Ok:
                Assert.Equal(AtmStatus.Ok, resultGarni.Status);
                Assert.InRange(resultGarni.Notes.Total, 1, result.Notes.Total);
                Assert.Equal(result.Fee, resultGarni.Fee);
                Assert.Equal(result.Account, resultGarni.Account);
                break;
            case AtmStatus.CannotDispense:
                Assert.Contains(resultGarni.Status, new[] { AtmStatus.Ok, AtmStatus.CannotDispense });
                break;
            default:
                Assert.Equal(result, resultGarni);
                break;
        }
    }

    /// <summary>
    /// Bloquer une cassette revient à la vider : le résultat est le même qu'avec une cassette vide et rien de
    /// bloqué, et le stock de la cassette bloquée ne bouge pas.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_UneCassetteBloqueeCompteCommeVide(Situation situation, Montant montant)
    {
        var videeSansBlocage = situation.WithStock(Disponible(situation.Stock, situation.Jammed)).WithJam(null);

        var (result, stockApres) = situation.Run(montant.Retrait);
        var (resultVide, _) = videeSansBlocage.Run(montant.Retrait);

        Assert.Equal(resultVide, result);
        if (situation.Jammed is { } bloquee)
        {
            Assert.Equal(situation.Stock.Of(bloquee), stockApres.Of(bloquee));
        }
    }

    /// <summary>
    /// Débloquer une cassette dont la combinaison n'utilise aucun billet ne change pas le résultat ; et un retrait
    /// possible avec une cassette bloquée l'est aussi sans blocage.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_DebloquerUneCassetteNeNuitJamais(Situation situation, Montant montant)
    {
        var debloquee = situation.WithJam(null);

        var (result, _) = situation.Run(montant.Retrait);
        var (resultDebloque, _) = debloquee.Run(montant.Retrait);

        if (result.Status == AtmStatus.Ok)
        {
            Assert.Equal(AtmStatus.Ok, resultDebloque.Status);
            Assert.True(resultDebloque.Notes.Total <= result.Notes.Total);
        }

        if (resultDebloque.Status == AtmStatus.Ok
            && (situation.Jammed is not { } bloquee || resultDebloque.Notes.Of(bloquee) == 0))
        {
            Assert.Equal(resultDebloque, result);
        }
    }

    /// <summary>
    /// Un solde plus élevé ne change le résultat que du même écart de solde, sauf à lever un refus
    /// InsufficientFunds (qui ne peut alors devenir ni InvalidAmount ni DailyLimitExceeded).
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_UnSoldePlusEleveNeChangeQueLeSolde(Situation situation, Montant montant, Ecart ecart)
    {
        var account = situation.Account;
        var delta = Math.Min(250 - account.Balance, ecart.Pas * 5);
        var plusRiche = situation.WithAccount(account with { Balance = account.Balance + delta });

        var (result, _) = situation.Run(montant.Retrait);
        var (resultRiche, _) = plusRiche.Run(montant.Retrait);

        if (result.Status == AtmStatus.InsufficientFunds)
        {
            Assert.Contains(resultRiche.Status, new[] { AtmStatus.Ok, AtmStatus.InsufficientFunds, AtmStatus.CannotDispense });
        }
        else
        {
            var decale = result.Account with { Balance = result.Account.Balance + delta };
            Assert.Equal(result with { Account = decale }, resultRiche);
        }
    }

    /// <summary>
    /// Avoir moins retiré aujourd'hui ne change le résultat que du même écart, sauf à lever un refus
    /// DailyLimitExceeded (qui ne peut alors pas devenir InvalidAmount).
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_MoinsRetireAujourdhuiNeChangeQueLeRetraitDuJour(Situation situation, Montant montant, Ecart ecart)
    {
        var account = situation.Account;
        var delta = Math.Min(account.WithdrawnToday, ecart.Pas * 10);
        var moinsRetire = situation.WithAccount(account with { WithdrawnToday = account.WithdrawnToday - delta });

        var (result, _) = situation.Run(montant.Retrait);
        var (resultMoins, _) = moinsRetire.Run(montant.Retrait);

        if (result.Status == AtmStatus.DailyLimitExceeded)
        {
            Assert.NotEqual(AtmStatus.InvalidAmount, resultMoins.Status);
        }
        else
        {
            var decale = result.Account with { WithdrawnToday = result.Account.WithdrawnToday - delta };
            Assert.Equal(result with { Account = decale }, resultMoins);
        }
    }

    /// <summary>
    /// Une combinaison d'au plus un billet par coupure, disponible et permise par le compte, est exactement ce que
    /// la machine sort ; la redéposer ensuite rend au module son stock de départ et au compte son solde.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Retrait_PuisRedepotDesMemesBilletsRestaureLeModule(Situation situation, Depot depot)
    {
        var billets = depot.Billets;
        if (billets.Total == 0)
        {
            return;
        }

        var bloqueeTouchee = situation.Jammed is { } bloquee && billets.Of(bloquee) > 0;
        var amount = billets.Value;
        var depart = situation
            .WithJam(bloqueeTouchee ? null : situation.Jammed)
            .WithStock(Plafonne(situation.Stock + billets))
            .WithAccount(new AccountState(
                Math.Max(situation.Account.Balance, amount),
                Math.Min(situation.Account.WithdrawnToday, 200 - amount)));

        var dispenser = new MoneyDispenser(depart.Stock, depart.Jammed);
        var machine = new CashMachine(dispenser);

        var retrait = machine.Execute(depart.Account, new Operation.Withdraw(amount));
        Assert.Equal(new AtmResult(AtmStatus.Ok, billets, 0, depart.Account with
        {
            Balance = depart.Account.Balance - amount,
            WithdrawnToday = depart.Account.WithdrawnToday + amount,
        }), retrait);

        var redepot = machine.Execute(retrait.Account, new Operation.Deposit(retrait.Notes));
        Assert.Equal(AtmStatus.Ok, redepot.Status);
        Assert.Equal(depart.Stock, dispenser.Stock);
        Assert.Equal(depart.Account.Balance, redepot.Account.Balance);
        Assert.Equal(depart.Account.WithdrawnToday + amount, redepot.Account.WithdrawnToday);
    }

    // ====================================================================================================
    // Dépôt
    // ====================================================================================================

    /// <summary>Le statut est celui du premier contrôle qui échoue, dans l'ordre de la spécification ; sinon Ok.</summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Depot_LeStatutEstCeluiDuPremierControleQuiEchoue(Situation situation, Depot depot)
    {
        var billets = depot.Billets;
        var apres = situation.Stock + billets;
        var attendu =
            billets.Total == 0 ? AtmStatus.InvalidAmount
            : situation.Jammed is { } bloquee && billets.Of(bloquee) > 0 ? AtmStatus.CassetteJammed
            : Domaine.Coupures.Any(c => apres.Of(c) > 5) ? AtmStatus.CassetteFull
            : AtmStatus.Ok;

        var (result, _) = situation.Run(depot.Operation);

        Assert.Equal(attendu, result.Status);
        Assert.Contains(result.Status, StatutsDepot);
    }

    /// <summary>Sur un refus, rien ne change : pas de billets, pas de frais, compte et stock identiques.</summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Depot_UnRefusNeChangeRien(Situation situation, Depot depot)
    {
        var (result, stockApres) = situation.Run(depot.Operation);

        if (result.Status != AtmStatus.Ok)
        {
            AssertRefusSansEffet(situation, result, stockApres);
        }
    }

    /// <summary>
    /// Un dépôt réussi range exactement les billets déposés, crédite leur valeur, ne touche pas au retrait du
    /// jour, ne sort rien et ne prend pas de frais.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Depot_UnSuccesRangeLesBilletsEtCrediteLeurValeur(Situation situation, Depot depot)
    {
        var (result, stockApres) = situation.Run(depot.Operation);

        if (result.Status == AtmStatus.Ok)
        {
            var credite = situation.Account with { Balance = situation.Account.Balance + depot.Billets.Value };
            Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, credite), result);
            Assert.Equal(situation.Stock + depot.Billets, stockApres);
        }
    }

    /// <summary>Un dépôt ne sort jamais de billets ; un refus n'appelle pas le module ; un succès range le dépôt.</summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Depot_NeFaitQueRangerDesBillets(Situation situation, Depot depot)
    {
        var (result, espion) = situation.RunSpied(depot.Operation);

        Assert.Empty(espion.Dispenses);
        if (result.Status != AtmStatus.Ok)
        {
            Assert.Empty(espion.Loads);
            return;
        }

        Assert.All(espion.Loads, load => Assert.True(load.Ok, $"dépôt refusé par le module : {load.Notes}"));
        Assert.Equal(depot.Billets, Somme(espion.Loads.Select(l => l.Notes)));
    }

    /// <summary>
    /// Une cassette bloquée que le dépôt ne touche pas ne change rien ; un dépôt non vide qui la touche est
    /// toujours refusé en CassetteJammed, quel que soit le remplissage.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Depot_LaCassetteBloqueeNeCompteQueSiLeDepotLaTouche(Situation situation, Depot depot)
    {
        var (result, stockApres) = situation.Run(depot.Operation);
        var (resultDebloque, stockDebloque) = situation.WithJam(null).Run(depot.Operation);

        if (situation.Jammed is { } bloquee && depot.Billets.Of(bloquee) > 0)
        {
            Assert.Equal(AtmStatus.CassetteJammed, result.Status);
        }
        else
        {
            Assert.Equal(resultDebloque, result);
            Assert.Equal(stockDebloque, stockApres);
        }
    }

    /// <summary>
    /// Déposer deux lots disjoints l'un après l'autre revient à les déposer ensemble : les deux réussissent si et
    /// seulement si le dépôt groupé réussit, avec le même stock et le même compte à la fin.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Depot_EnDeuxFoisEgaleDepotGroupe(Situation situation, DepotPartage partage)
    {
        var dispenser = new MoneyDispenser(situation.Stock, situation.Jammed);
        var machine = new CashMachine(dispenser);
        var premier = machine.Execute(situation.Account, new Operation.Deposit(partage.Premier));
        var second = machine.Execute(premier.Account, new Operation.Deposit(partage.Second));

        var (groupe, stockGroupe) = situation.Run(new Operation.Deposit(partage.Premier + partage.Second));

        var deuxOk = premier.Status == AtmStatus.Ok && second.Status == AtmStatus.Ok;
        Assert.Equal(groupe.Status == AtmStatus.Ok, deuxOk);
        if (deuxOk)
        {
            Assert.Equal(groupe.Account, second.Account);
            Assert.Equal(stockGroupe, dispenser.Stock);
        }
    }

    /// <summary>
    /// Ce qu'on vient de déposer peut toujours être retiré aussitôt : le retrait de la même valeur n'est refusé
    /// ni pour fonds insuffisants ni faute de billets (seul le plafond du jour peut l'empêcher).
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Depot_PuisRetraitDeLaMemeValeurEstToujoursServi(Situation situation, Depot depot)
    {
        var dispenser = new MoneyDispenser(situation.Stock, situation.Jammed);
        var machine = new CashMachine(dispenser);

        var depose = machine.Execute(situation.Account, depot.Operation);
        if (depose.Status != AtmStatus.Ok)
        {
            return;
        }

        var retrait = machine.Execute(depose.Account, new Operation.Withdraw(depot.Billets.Value));

        Assert.Contains(retrait.Status, new[] { AtmStatus.Ok, AtmStatus.DailyLimitExceeded });
        if (retrait.Status == AtmStatus.Ok)
        {
            Assert.Equal(situation.Account.Balance, retrait.Account.Balance);
        }
    }

    // ====================================================================================================
    // Toutes opérations
    // ====================================================================================================

    /// <summary>
    /// La machine est sans mémoire : après une première opération, la suivante donne le même résultat et le même
    /// stock qu'une machine neuve sur un module neuf qui aurait ce stock.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void LaMachineEstSansMemoire(Situation situation, UneOperation premiere, Situation autre, UneOperation seconde)
    {
        var compteSuivant = autre.Account;

        var dispenser = new MoneyDispenser(situation.Stock, situation.Jammed);
        var machine = new CashMachine(dispenser);
        machine.Execute(situation.Account, premiere.Operation);

        var neuve = situation.WithStock(dispenser.Stock).WithAccount(compteSuivant);
        var (attendu, stockAttendu) = neuve.Run(seconde.Operation);

        var result = machine.Execute(compteSuivant, seconde.Operation);

        Assert.Equal(attendu, result);
        Assert.Equal(stockAttendu, dispenser.Stock);
    }

    /// <summary>
    /// Une suite d'opérations dans le domaine conserve billets et argent : le stock final est le stock de départ plus
    /// les billets déposés moins les billets sortis, et le solde bouge des dépôts moins les retraits et les frais.
    /// Le compte reste dans le domaine tant qu'on ne dépose pas au-delà de 250.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void UneSuiteDOperationsConserveBilletsEtArgent(Situation situation, UneOperation[] operations)
    {
        var dispenser = new MoneyDispenser(situation.Stock, situation.Jammed);
        var machine = new CashMachine(dispenser);
        var account = situation.Account;
        var (deposes, sortis, frais) = (NoteCounts.None, NoteCounts.None, 0);

        foreach (var operation in operations)
        {
            if (operation.Operation is Operation.Deposit d && account.Balance + d.Notes.Value > 250)
            {
                continue; // le solde sortirait du domaine
            }

            var result = machine.Execute(account, operation.Operation);
            Assert.NotEqual(AtmStatus.DispenserFault, result.Status);

            if (result.Status == AtmStatus.Ok && operation.Operation is Operation.Deposit depot)
            {
                deposes += depot.Notes;
            }

            sortis += result.Notes;
            frais += result.Fee;
            account = result.Account;

            Assert.True(DansLeDomaine(account), $"compte hors domaine : {account}");
            Assert.All(Domaine.Coupures, c => Assert.InRange(dispenser.Stock.Of(c), 0, 5));
        }

        Assert.Equal(situation.Stock + deposes - sortis, dispenser.Stock);
        Assert.Equal(situation.Account.Balance + deposes.Value - sortis.Value - frais, account.Balance);
        Assert.Equal(situation.Account.WithdrawnToday + sortis.Value, account.WithdrawnToday);
    }

    /// <summary>
    /// Le résultat ne dépend que du compte, de l'opération et du module : rejouer la même opération sur un module
    /// identique donne le même résultat et le même stock.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void LeResultatEstDeterministe(Situation situation, UneOperation operation)
    {
        var (premier, stockPremier) = situation.Run(operation.Operation);
        var (second, stockSecond) = situation.Run(operation.Operation);

        Assert.Equal(premier, second);
        Assert.Equal(stockPremier, stockSecond);
    }

    // ====================================================================================================
    // Pannes du module
    // ====================================================================================================

    /// <summary>
    /// Si le module refuse un passage, la machine s'arrête aussitôt : DispenserFault, pas de frais, compte
    /// identique, et <c>Notes</c> contient exactement les billets des passages déjà sortis. Si la panne arrive
    /// après le dernier passage, le retrait se déroule normalement.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Panne_EnRetraitArreteAussitotEtRendLesBilletsDejaSortis(Situation situation, Montant montant, PassageEnPanne panne) =>
        AssertPanneEnRetrait(situation, montant.Retrait, panne);

    /// <summary>
    /// La même règle sur des retraits en plusieurs passages, pour que la panne tombe souvent après des billets déjà
    /// sortis : ceux-là, et eux seuls, figurent dans <c>Notes</c>.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Panne_AuMilieuDUnRetraitLongRendLesBilletsDejaSortis(RetraitLong retrait, PassageEnPanne panne) =>
        AssertPanneEnRetrait(retrait.Situation, retrait.Retrait, panne);

    private static void AssertPanneEnRetrait(Situation situation, Operation retrait, PassageEnPanne panne)
    {
        var (normal, espion) = situation.RunSpied(retrait);
        var module = new ModuleEnPanne(situation.Stock, situation.Jammed, panne.Numero, loadEnPanne: false);

        var result = new CashMachine(module).Execute(situation.Account, retrait);

        if (espion.Dispenses.Count < panne.Numero)
        {
            Assert.Equal(normal, result);
            return;
        }

        var dejaSortis = Somme(module.Dispenses.Take(panne.Numero - 1).Select(p => p.Notes));
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, dejaSortis, 0, situation.Account), result);
        Assert.Equal(panne.Numero, module.Dispenses.Count);
        Assert.Equal(situation.Stock - dejaSortis, module.Stock);
        Assert.Equal(0, module.Loads);
    }

    /// <summary>
    /// Si le module refuse de ranger un dépôt qui a passé les contrôles : DispenserFault, aucun billet, pas de
    /// frais, compte identique. Les refus, eux, restent les mêmes.
    /// </summary>
    [Property(Arbitrary = [typeof(Generateurs)], Replay = Graine, MaxTest = Cas)]
    public void Panne_EnDepotRendDispenserFaultSansRienChanger(Situation situation, Depot depot)
    {
        var (normal, _) = situation.Run(depot.Operation);
        var module = new ModuleEnPanne(situation.Stock, situation.Jammed, dispenseEnPanne: 0, loadEnPanne: true);

        var result = new CashMachine(module).Execute(situation.Account, depot.Operation);

        var attendu = normal.Status == AtmStatus.Ok
            ? new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, situation.Account)
            : normal;
        Assert.Equal(attendu, result);
        Assert.Equal(situation.Stock, module.Stock);
        Assert.Empty(module.Dispenses);
    }
}
