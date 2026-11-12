using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Atm.Tests;

/// <summary>
/// Les valeurs du domaine de la spécification, chacune dans son propre type pour que FsCheck ne tire que des cas du domaine.
/// </summary>
public static class Domaine
{
    /// <summary>Solde multiple de 5 de 0 à 250, déjà retiré multiple de 10 de 0 à 200.</summary>
    public sealed record Compte(AccountState Valeur)
    {
        public override string ToString() => $"solde {Valeur.Balance}, retiré {Valeur.WithdrawnToday}";
    }

    /// <summary>0 à 5 billets par cassette.</summary>
    public sealed record Stock(NoteCounts Valeur)
    {
        public override string ToString() => Proprietes.Texte(Valeur);
    }

    /// <summary>Aucune, 50, 20 ou 10.</summary>
    public sealed record Bloquee(Denomination? Valeur)
    {
        public override string ToString() => Valeur is { } d ? $"bloquée {(int)d}" : "aucune bloquée";
    }

    /// <summary>-10, 0, 5 et les multiples de 10 de 10 à 220.</summary>
    public sealed record Montant(int Valeur)
    {
        public override string ToString() => $"retrait {Valeur}";
    }

    /// <summary>0 ou 1 billet par coupure.</summary>
    public sealed record Depot(NoteCounts Valeur)
    {
        public override string ToString() => $"dépôt {Proprietes.Texte(Valeur)}";
    }

    /// <summary>
    /// Un retrait dont tous les contrôles passent : le montant est la valeur de <see cref="Combinaison"/>, des billets
    /// disponibles, et le compte respecte le plafond et couvre montant + frais. La combinaison n'est pas forcément la plus courte.
    /// </summary>
    public sealed record RetraitFaisable(AccountState Compte, NoteCounts Stock, Denomination? Bloquee, NoteCounts Combinaison)
    {
        public int Montant => Combinaison.Value;

        public override string ToString() =>
            $"solde {Compte.Balance}, retiré {Compte.WithdrawnToday}, stock {Proprietes.Texte(Stock)}, "
            + $"{new Bloquee(Bloquee)}, retrait {Montant} (faisable avec {Proprietes.Texte(Combinaison)})";
    }

    /// <summary>Un dépôt du domaine que la machine accepte : non vide, sans billet bloqué, qui tient dans les cassettes.</summary>
    public sealed record DepotAccepte(AccountState Compte, NoteCounts Stock, Denomination? Bloquee, NoteCounts Billets)
    {
        public override string ToString() =>
            $"solde {Compte.Balance}, retiré {Compte.WithdrawnToday}, stock {Proprietes.Texte(Stock)}, "
            + $"{new Bloquee(Bloquee)}, dépôt {Proprietes.Texte(Billets)}";
    }

    /// <summary>Une suite d'opérations du domaine (retraits et dépôts mêlés).</summary>
    public sealed record Suite(IReadOnlyList<Operation> Operations)
    {
        public override string ToString() => string.Join(" ; ", Operations.Select(Proprietes.Texte));
    }

    /// <summary>Une demande faite directement au module : 0 à 5 billets par coupure, petites demandes favorisées.</summary>
    public sealed record Demande(NoteCounts Valeur)
    {
        public override string ToString() => Proprietes.Texte(Valeur);
    }

    /// <summary>Un entier quelconque positif, pour choisir l'appel au module qui tombe en panne.</summary>
    public sealed record Rang(int Valeur)
    {
        public override string ToString() => $"rang {Valeur}";
    }
}

/// <summary>Les générateurs des valeurs du domaine.</summary>
public static class Generateurs
{
    private static readonly Denomination?[] Bloquees = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] Montants = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static Gen<NoteCounts> Billets(int max) =>
        from f in Gen.Choose(0, max)
        from t in Gen.Choose(0, max)
        from n in Gen.Choose(0, max)
        select new NoteCounts(f, t, n);

    private static Gen<NoteCounts> BilletsJusqua(NoteCounts max) =>
        from f in Gen.Choose(0, max.Fifties)
        from t in Gen.Choose(0, max.Twenties)
        from n in Gen.Choose(0, max.Tens)
        select new NoteCounts(f, t, n);

    private static readonly Gen<AccountState> GenCompte =
        from b in Gen.Choose(0, 50)
        from w in Gen.Choose(0, 20)
        select new AccountState(b * 5, w * 10);

    private static readonly Gen<NoteCounts> GenStock = Billets(5);

    private static readonly Gen<Denomination?> GenBloquee = Gen.Elements(Bloquees);

    private static readonly Gen<int> GenMontant = Gen.Elements(Montants);

    private static readonly Gen<NoteCounts> GenDepot = Billets(1);

    private static readonly Gen<Operation> GenOperation = Gen.OneOf(
        GenMontant.Select(Operation (m) => new Operation.Withdraw(m)),
        GenDepot.Select(Operation (d) => new Operation.Deposit(d)));

    public static Arbitrary<Domaine.Compte> Compte() => Arb.From(GenCompte.Select(c => new Domaine.Compte(c)));

    public static Arbitrary<Domaine.Stock> Stock() => Arb.From(GenStock.Select(s => new Domaine.Stock(s)));

    public static Arbitrary<Domaine.Bloquee> Bloquee() => Arb.From(GenBloquee.Select(b => new Domaine.Bloquee(b)));

    public static Arbitrary<Domaine.Montant> Montant() => Arb.From(GenMontant.Select(m => new Domaine.Montant(m)));

    public static Arbitrary<Domaine.Depot> Depot() => Arb.From(GenDepot.Select(d => new Domaine.Depot(d)));

    public static Arbitrary<Domaine.RetraitFaisable> RetraitFaisable() => Arb.From(
        from stock in GenStock
        from bloquee in GenBloquee
        from combinaison in BilletsJusqua(Proprietes.Disponible(stock, bloquee))
        where combinaison.Value is >= 10 and <= 200 // filtre tout le tirage : un stock disponible vide ne bloque pas
        let montant = combinaison.Value
        let frais = montant > 100 ? 5 : 0
        from dejaRetire in Gen.Choose(0, (200 - montant) / 10)
        from solde in Gen.Choose((montant + frais + 4) / 5, 50)
        select new Domaine.RetraitFaisable(new AccountState(solde * 5, dejaRetire * 10), stock, bloquee, combinaison));

    public static Arbitrary<Domaine.DepotAccepte> DepotAccepte() => Arb.From(
        from compte in GenCompte
        from stock in GenStock
        from bloquee in GenBloquee
        from billets in GenDepot
        where billets.Total > 0
              && (bloquee is not { } b || billets.Of(b) == 0)
              && Proprietes.TientDans(stock + billets)
        select new Domaine.DepotAccepte(compte, stock, bloquee, billets));

    public static Arbitrary<Domaine.Suite> Suite() => Arb.From(
        from n in Gen.Choose(0, 12)
        from operations in GenOperation.ArrayOf(n)
        select new Domaine.Suite(operations));

    public static Arbitrary<Domaine.Demande> Demande() =>
        Arb.From(Gen.OneOf(Billets(1), Billets(2), Billets(5)).Select(d => new Domaine.Demande(d)));

    public static Arbitrary<Domaine.Rang> Rang() => Arb.From(Gen.Choose(0, 1000).Select(r => new Domaine.Rang(r)));
}

/// <summary>
/// Tests de propriétés : des règles de la spécification qui doivent tenir pour toutes les entrées du domaine,
/// vérifiées sur 200 cas tirés au hasard avec une graine fixe.
/// </summary>
public class Proprietes
{
    /// <summary>
    /// Graine fixe. FsCheck 3 attend « graine,gamma » avec un gamma impair : « 42,0 » est rejeté (« Gamma must be odd »),
    /// on garde donc la graine 42 avec le plus petit gamma valide.
    /// </summary>
    private const string Graine = "42,1";

    /// <summary>Nombre de cas tirés par propriété.</summary>
    private const int Cas = 200;

    // ------------------------------------------------------------------
    // Outils
    // ------------------------------------------------------------------

    internal static string Texte(NoteCounts n) => $"50×{n.Fifties} 20×{n.Twenties} 10×{n.Tens}";

    internal static string Texte(Operation operation) => operation switch
    {
        Operation.Withdraw w => $"retrait {w.Amount}",
        Operation.Deposit d => $"dépôt {Texte(d.Notes)}",
        _ => operation.ToString(),
    };

    /// <summary>Les billets disponibles : le stock, sans la cassette bloquée, qui compte pour zéro.</summary>
    internal static NoteCounts Disponible(NoteCounts stock, Denomination? bloquee) => bloquee switch
    {
        Denomination.Fifty => stock with { Fifties = 0 },
        Denomination.Twenty => stock with { Twenties = 0 },
        Denomination.Ten => stock with { Tens = 0 },
        _ => stock,
    };

    internal static bool TientDans(NoteCounts stock) => stock is { Fifties: <= 5, Twenties: <= 5, Tens: <= 5 };

    private static bool Contient(NoteCounts grand, NoteCounts petit) =>
        petit.Fifties <= grand.Fifties && petit.Twenties <= grand.Twenties && petit.Tens <= grand.Tens;

    /// <summary>Toutes les combinaisons de billets prises dans <paramref name="disponible"/>.</summary>
    private static IEnumerable<NoteCounts> Combinaisons(NoteCounts disponible)
    {
        for (var f = 0; f <= disponible.Fifties; f++)
        for (var t = 0; t <= disponible.Twenties; t++)
        for (var n = 0; n <= disponible.Tens; n++)
        {
            yield return new NoteCounts(f, t, n);
        }
    }

    private static bool Faisable(int montant, NoteCounts disponible) =>
        Combinaisons(disponible).Any(c => c.Value == montant);

    private static int Frais(int montant) => montant > 100 ? 5 : 0;

    private static bool DansLeDomaine(AccountState compte) =>
        compte.Balance is >= 0 and <= 250 && compte.Balance % 5 == 0
        && compte.WithdrawnToday is >= 0 and <= 200 && compte.WithdrawnToday % 10 == 0;

    private static NoteCounts Somme(IEnumerable<NoteCounts> billets) => billets.Aggregate(NoteCounts.None, (a, b) => a + b);

    private static (AtmResult Resultat, SpyDispenser Module) Retirer(AccountState compte, NoteCounts stock, Denomination? bloquee, int montant)
    {
        var module = new SpyDispenser(stock, bloquee);

        return (new CashMachine(module).Execute(compte, new Operation.Withdraw(montant)), module);
    }

    private static (AtmResult Resultat, SpyDispenser Module) Deposer(AccountState compte, NoteCounts stock, Denomination? bloquee, NoteCounts billets)
    {
        var module = new SpyDispenser(stock, bloquee);

        return (new CashMachine(module).Execute(compte, new Operation.Deposit(billets)), module);
    }

    private static void AssertRefusSansEffet(AtmResult resultat, AccountState compte, NoteCounts stock, SpyDispenser module)
    {
        Assert.Equal(NoteCounts.None, resultat.Notes);
        Assert.Equal(0, resultat.Fee);
        Assert.Equal(compte, resultat.Account);
        Assert.Equal(stock, module.Stock);
        Assert.Equal(0, module.Calls);
    }

    // ------------------------------------------------------------------
    // Retrait
    // ------------------------------------------------------------------

    /// <summary>Le statut est celui du premier contrôle qui échoue, dans l'ordre de la spécification.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_le_statut_est_celui_du_premier_controle_qui_echoue(
        Domaine.Compte compte, Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Montant montant)
    {
        var (a, m) = (compte.Valeur, montant.Valeur);
        var attendu =
            m <= 0 || m % 10 != 0 ? AtmStatus.InvalidAmount
            : a.WithdrawnToday + m > 200 ? AtmStatus.DailyLimitExceeded
            : m + Frais(m) > a.Balance ? AtmStatus.InsufficientFunds
            : !Faisable(m, Disponible(stock.Valeur, bloquee.Valeur)) ? AtmStatus.CannotDispense
            : AtmStatus.Ok;

        var (resultat, _) = Retirer(a, stock.Valeur, bloquee.Valeur, m);

        Assert.Equal(attendu, resultat.Status);
    }

    /// <summary>Un montant invalide est refusé quel que soit le reste (compte, stock, cassette bloquée).</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_un_montant_invalide_est_toujours_refuse_en_premier(
        Domaine.Compte compte, Domaine.Stock stock, Domaine.Bloquee bloquee)
    {
        foreach (var montant in new[] { -10, 0, 5 })
        {
            var (resultat, module) = Retirer(compte.Valeur, stock.Valeur, bloquee.Valeur, montant);

            Assert.Equal(AtmStatus.InvalidAmount, resultat.Status);
            AssertRefusSansEffet(resultat, compte.Valeur, stock.Valeur, module);
        }
    }

    /// <summary>Sur un refus, rien ne change et le module n'est pas sollicité.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_un_refus_ne_change_rien(
        Domaine.Compte compte, Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Montant montant)
    {
        var (resultat, module) = Retirer(compte.Valeur, stock.Valeur, bloquee.Valeur, montant.Valeur);

        if (resultat.Status != AtmStatus.Ok)
        {
            AssertRefusSansEffet(resultat, compte.Valeur, stock.Valeur, module);
        }
    }

    /// <summary>Dans le domaine, un retrait ne donne jamais de statut propre au dépôt, ni de panne.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_ne_rend_que_des_statuts_de_retrait(
        Domaine.Compte compte, Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Montant montant)
    {
        var (resultat, _) = Retirer(compte.Valeur, stock.Valeur, bloquee.Valeur, montant.Valeur);

        Assert.Contains(resultat.Status, new[]
        {
            AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.DailyLimitExceeded, AtmStatus.InsufficientFunds, AtmStatus.CannotDispense,
        });
    }

    /// <summary>Un retrait dont tous les contrôles passent réussit, avec au plus autant de billets que la combinaison connue.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_faisable_reussit(Domaine.RetraitFaisable r)
    {
        var (resultat, _) = Retirer(r.Compte, r.Stock, r.Bloquee, r.Montant);

        Assert.Equal(AtmStatus.Ok, resultat.Status);
        Assert.True(resultat.Notes.Total <= r.Combinaison.Total, $"{Texte(resultat.Notes)} compte plus de billets que {Texte(r.Combinaison)}");
    }

    /// <summary>
    /// Un retrait réussi : les billets font exactement le montant, viennent des cassettes disponibles et quittent le module ;
    /// les frais suivent la règle ; le solde baisse de montant + frais, le retrait du jour monte du seul montant.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_reussi_respecte_le_bilan(Domaine.RetraitFaisable r)
    {
        var (resultat, module) = Retirer(r.Compte, r.Stock, r.Bloquee, r.Montant);

        Assert.Equal(AtmStatus.Ok, resultat.Status);
        Assert.Equal(r.Montant, resultat.Notes.Value);
        Assert.True(Contient(Disponible(r.Stock, r.Bloquee), resultat.Notes), $"{Texte(resultat.Notes)} hors des billets disponibles");
        Assert.Equal(r.Stock - resultat.Notes, module.Stock);
        Assert.Equal(Frais(r.Montant), resultat.Fee);
        Assert.Equal(new AccountState(r.Compte.Balance - r.Montant - resultat.Fee, r.Compte.WithdrawnToday + r.Montant), resultat.Account);
        Assert.True(DansLeDomaine(resultat.Account), $"compte hors domaine : {resultat.Account}");
    }

    /// <summary>Les billets sortis forment l'unique combinaison la plus courte parmi celles qui font le montant.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_reussi_donne_la_combinaison_la_plus_courte(Domaine.RetraitFaisable r)
    {
        var (resultat, _) = Retirer(r.Compte, r.Stock, r.Bloquee, r.Montant);

        Assert.Equal(AtmStatus.Ok, resultat.Status);
        foreach (var autre in Combinaisons(Disponible(r.Stock, r.Bloquee)).Where(c => c.Value == r.Montant && c != resultat.Notes))
        {
            Assert.True(autre.Total > resultat.Notes.Total, $"{Texte(autre)} est au moins aussi courte que {Texte(resultat.Notes)}");
        }
    }

    /// <summary>
    /// Les billets sortent en passages non vides de 4 billets au plus, autant que nécessaire, qui totalisent exactement
    /// la combinaison ; aucun billet n'est rangé.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_reussi_sort_les_billets_en_passages_de_4_au_plus(Domaine.RetraitFaisable r)
    {
        var (resultat, module) = Retirer(r.Compte, r.Stock, r.Bloquee, r.Montant);

        Assert.Equal(AtmStatus.Ok, resultat.Status);
        Assert.All(module.Dispensed, passage => Assert.InRange(passage.Total, 1, MoneyDispenser.MaxNotesPerPass));
        Assert.All(module.Dispensed, passage => Assert.True(passage is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0 }));
        Assert.Equal(resultat.Notes, Somme(module.Dispensed));
        Assert.Equal((resultat.Notes.Total + 3) / 4, module.Dispensed.Count);
        Assert.Empty(module.Loaded);
    }

    /// <summary>Une cassette bloquée se comporte comme une cassette vide, et ses billets ne bougent pas.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_une_cassette_bloquee_compte_pour_zero(
        Domaine.Compte compte, Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Montant montant)
    {
        var (avecBlocage, module) = Retirer(compte.Valeur, stock.Valeur, bloquee.Valeur, montant.Valeur);
        var (cassetteVide, _) = Retirer(compte.Valeur, Disponible(stock.Valeur, bloquee.Valeur), null, montant.Valeur);

        Assert.Equal(cassetteVide, avecBlocage);
        if (bloquee.Valeur is { } b)
        {
            Assert.Equal(stock.Valeur.Of(b), module.Stock.Of(b));
        }
    }

    /// <summary>Ajouter des billets ne fait jamais échouer un retrait qui réussissait, ni ne rallonge la combinaison.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_plus_de_billets_ne_fait_pas_echouer(Domaine.RetraitFaisable r, Domaine.Stock ajout)
    {
        var plus = new NoteCounts(
            Math.Max(r.Stock.Fifties, ajout.Valeur.Fifties),
            Math.Max(r.Stock.Twenties, ajout.Valeur.Twenties),
            Math.Max(r.Stock.Tens, ajout.Valeur.Tens));

        var (avant, _) = Retirer(r.Compte, r.Stock, r.Bloquee, r.Montant);
        var (apres, _) = Retirer(r.Compte, plus, r.Bloquee, r.Montant);

        Assert.Equal(AtmStatus.Ok, avant.Status);
        Assert.Equal(AtmStatus.Ok, apres.Status);
        Assert.True(apres.Notes.Total <= avant.Notes.Total, $"{Texte(apres.Notes)} plus longue que {Texte(avant.Notes)}");
        Assert.Equal(avant.Fee, apres.Fee);
        Assert.Equal(avant.Account, apres.Account);
    }

    /// <summary>
    /// Un solde plus grand ne change rien au résultat, sauf le solde lui-même, tant que le solde de départ suffisait.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_un_solde_plus_grand_ne_change_que_le_solde(
        Domaine.Compte compte, Domaine.Compte autre, Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Montant montant)
    {
        var petit = compte.Valeur with { Balance = Math.Min(compte.Valeur.Balance, autre.Valeur.Balance) };
        var grand = compte.Valeur with { Balance = Math.Max(compte.Valeur.Balance, autre.Valeur.Balance) };
        var ecart = grand.Balance - petit.Balance;

        var (avecPetit, _) = Retirer(petit, stock.Valeur, bloquee.Valeur, montant.Valeur);
        var (avecGrand, _) = Retirer(grand, stock.Valeur, bloquee.Valeur, montant.Valeur);

        if (avecPetit.Status != AtmStatus.InsufficientFunds)
        {
            var decale = avecPetit.Account with { Balance = avecPetit.Account.Balance + ecart };
            Assert.Equal(avecPetit with { Account = decale }, avecGrand);
        }
        else if (avecGrand.Status == AtmStatus.InsufficientFunds)
        {
            Assert.Equal(avecPetit with { Account = grand }, avecGrand);
        }
    }

    /// <summary>
    /// Avoir moins retiré aujourd'hui ne change rien au résultat, sauf ce retrait du jour, tant que le plafond était respecté.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_moins_retire_aujourd_hui_ne_change_que_le_cumul(
        Domaine.Compte compte, Domaine.Compte autre, Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Montant montant)
    {
        var peu = compte.Valeur with { WithdrawnToday = Math.Min(compte.Valeur.WithdrawnToday, autre.Valeur.WithdrawnToday) };
        var beaucoup = compte.Valeur with { WithdrawnToday = Math.Max(compte.Valeur.WithdrawnToday, autre.Valeur.WithdrawnToday) };
        var ecart = beaucoup.WithdrawnToday - peu.WithdrawnToday;

        var (avecBeaucoup, _) = Retirer(beaucoup, stock.Valeur, bloquee.Valeur, montant.Valeur);
        var (avecPeu, _) = Retirer(peu, stock.Valeur, bloquee.Valeur, montant.Valeur);

        if (avecBeaucoup.Status != AtmStatus.DailyLimitExceeded)
        {
            var decale = avecBeaucoup.Account with { WithdrawnToday = avecBeaucoup.Account.WithdrawnToday - ecart };
            Assert.Equal(avecBeaucoup with { Account = decale }, avecPeu);
        }
    }

    /// <summary>Les frais ne comptent pas dans le plafond : on peut retirer jusqu'à exactement 200 € dans la journée, frais en plus.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_les_frais_ne_comptent_pas_dans_le_plafond(Domaine.RetraitFaisable r)
    {
        var aLaLimite = r.Compte with { Balance = 250, WithdrawnToday = 200 - r.Montant };

        var (resultat, _) = Retirer(aLaLimite, r.Stock, r.Bloquee, r.Montant);

        Assert.Equal(AtmStatus.Ok, resultat.Status);
        Assert.Equal(200, resultat.Account.WithdrawnToday);
        Assert.Equal(250 - r.Montant - Frais(r.Montant), resultat.Account.Balance);
    }

    /// <summary>
    /// Frontière du solde : un solde égal à montant + frais suffit (et tombe à 0) ; 5 € de moins, c'est <c>InsufficientFunds</c>,
    /// même si le solde couvre le montant seul.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_le_solde_doit_couvrir_montant_et_frais(Domaine.RetraitFaisable r)
    {
        var juste = r.Compte with { Balance = r.Montant + Frais(r.Montant) };
        var court = r.Compte with { Balance = juste.Balance - 5 };

        var (avecJuste, _) = Retirer(juste, r.Stock, r.Bloquee, r.Montant);
        var (avecCourt, module) = Retirer(court, r.Stock, r.Bloquee, r.Montant);

        Assert.Equal(AtmStatus.Ok, avecJuste.Status);
        Assert.Equal(0, avecJuste.Account.Balance);
        Assert.Equal(AtmStatus.InsufficientFunds, avecCourt.Status);
        AssertRefusSansEffet(avecCourt, court, r.Stock, module);
    }

    /// <summary>
    /// Frontière du plafond : un retrait qui porte le cumul du jour 10 € au-delà de 200 est refusé avec <c>DailyLimitExceeded</c>,
    /// avant tout contrôle de solde ou de billets.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Retrait_au_dela_du_plafond_est_refuse_avant_le_reste(Domaine.RetraitFaisable r, Domaine.Compte compte, Domaine.Stock stock)
    {
        var depasse = new AccountState(compte.Valeur.Balance, 200 - r.Montant + 10);

        var (resultat, module) = Retirer(depasse, stock.Valeur, r.Bloquee, r.Montant);

        Assert.Equal(AtmStatus.DailyLimitExceeded, resultat.Status);
        AssertRefusSansEffet(resultat, depasse, stock.Valeur, module);
    }

    // ------------------------------------------------------------------
    // Dépôt
    // ------------------------------------------------------------------

    /// <summary>Le statut est celui du premier contrôle qui échoue, dans l'ordre de la spécification.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Depot_le_statut_est_celui_du_premier_controle_qui_echoue(
        Domaine.Compte compte, Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Depot depot)
    {
        var billets = depot.Valeur;
        var attendu =
            billets.Total == 0 ? AtmStatus.InvalidAmount
            : bloquee.Valeur is { } b && billets.Of(b) > 0 ? AtmStatus.CassetteJammed
            : !TientDans(stock.Valeur + billets) ? AtmStatus.CassetteFull
            : AtmStatus.Ok;

        var (resultat, _) = Deposer(compte.Valeur, stock.Valeur, bloquee.Valeur, billets);

        Assert.Equal(attendu, resultat.Status);
    }

    /// <summary>Sur un refus, rien ne change et le module n'est pas sollicité.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Depot_un_refus_ne_change_rien(
        Domaine.Compte compte, Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Depot depot)
    {
        var (resultat, module) = Deposer(compte.Valeur, stock.Valeur, bloquee.Valeur, depot.Valeur);

        if (resultat.Status != AtmStatus.Ok)
        {
            AssertRefusSansEffet(resultat, compte.Valeur, stock.Valeur, module);
        }
    }

    /// <summary>
    /// Un dépôt accepté : les billets rejoignent le module, le solde monte de leur valeur, le retrait du jour ne bouge pas,
    /// aucun billet ne sort et aucuns frais ne sont prélevés.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Depot_accepte_range_les_billets_et_credite_le_compte(Domaine.DepotAccepte d)
    {
        var (resultat, module) = Deposer(d.Compte, d.Stock, d.Bloquee, d.Billets);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, d.Compte with { Balance = d.Compte.Balance + d.Billets.Value }), resultat);
        Assert.Equal(d.Stock + d.Billets, module.Stock);
        Assert.Equal(d.Billets, Somme(module.Loaded));
        Assert.Empty(module.Dispensed);
    }

    /// <summary>Le dépôt ne dépend pas du compte : seul le solde change, et toujours de la valeur des billets.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Depot_ne_depend_pas_du_compte(
        Domaine.Compte compte, Domaine.Compte autre, Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Depot depot)
    {
        var (r1, m1) = Deposer(compte.Valeur, stock.Valeur, bloquee.Valeur, depot.Valeur);
        var (r2, m2) = Deposer(autre.Valeur, stock.Valeur, bloquee.Valeur, depot.Valeur);

        Assert.Equal(r1.Status, r2.Status);
        Assert.Equal(r1.Account.Balance - compte.Valeur.Balance, r2.Account.Balance - autre.Valeur.Balance);
        Assert.Equal(autre.Valeur.WithdrawnToday, r2.Account.WithdrawnToday);
        Assert.Equal(m1.Stock, m2.Stock);
    }

    /// <summary>Déposer deux lots disjoints en une fois ou l'un après l'autre mène au même état, quand tout est accepté.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Depot_en_une_fois_ou_en_deux_revient_au_meme(Domaine.DepotAccepte d, Domaine.Depot second)
    {
        var lot2 = new NoteCounts(
            d.Billets.Fifties == 0 ? second.Valeur.Fifties : 0,
            d.Billets.Twenties == 0 ? second.Valeur.Twenties : 0,
            d.Billets.Tens == 0 ? second.Valeur.Tens : 0);

        var module = new MoneyDispenser(d.Stock, d.Bloquee);
        var machine = new CashMachine(module);
        var r1 = machine.Execute(d.Compte, new Operation.Deposit(d.Billets));
        var r2 = machine.Execute(r1.Account, new Operation.Deposit(lot2));

        var (enUneFois, moduleUneFois) = Deposer(d.Compte, d.Stock, d.Bloquee, d.Billets + lot2);

        Assert.Equal(AtmStatus.Ok, r1.Status);
        if (lot2.Total == 0)
        {
            Assert.Equal(AtmStatus.InvalidAmount, r2.Status);
        }
        else if (r2.Status == AtmStatus.Ok)
        {
            Assert.Equal(enUneFois, r2);
            Assert.Equal(moduleUneFois.Stock, module.Stock);
        }
        else
        {
            Assert.NotEqual(AtmStatus.Ok, enUneFois.Status);
        }
    }

    // ------------------------------------------------------------------
    // Machine sans mémoire, suites d'opérations
    // ------------------------------------------------------------------

    /// <summary>
    /// La machine ne garde rien entre deux appels : après une première opération, la seconde donne le même résultat
    /// qu'une machine neuve branchée sur un module dans le même état.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Machine_sans_memoire(Domaine.Compte compte, Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Suite suite)
    {
        var module = new MoneyDispenser(stock.Valeur, bloquee.Valeur);
        var machine = new CashMachine(module);
        var etat = compte.Valeur;

        foreach (var operation in suite.Operations)
        {
            if (!DansLeDomaine(etat))
            {
                break;
            }

            var neuve = new MoneyDispenser(module.Stock, module.Jammed);
            var attendu = new CashMachine(neuve).Execute(etat, operation);
            var obtenu = machine.Execute(etat, operation);

            Assert.Equal(attendu, obtenu);
            Assert.Equal(neuve.Stock, module.Stock);
            etat = obtenu.Account;
        }
    }

    /// <summary>
    /// Sur toute suite d'opérations du domaine : les billets et l'argent se conservent (stock = départ + dépôts − billets sortis,
    /// solde = départ + dépôts − retraits − frais), le cumul du jour ne dépasse jamais 200, le solde ne devient jamais négatif,
    /// les cassettes restent entre 0 et 5 billets et aucune panne n'apparaît.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Suite_d_operations_conserve_billets_et_argent(
        Domaine.Compte compte, Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Suite suite)
    {
        var module = new MoneyDispenser(stock.Valeur, bloquee.Valeur);
        var machine = new CashMachine(module);
        var etat = compte.Valeur;
        var (deposes, sortis, frais, retire) = (NoteCounts.None, NoteCounts.None, 0, 0);

        foreach (var operation in suite.Operations)
        {
            if (!DansLeDomaine(etat))
            {
                break;
            }

            var resultat = machine.Execute(etat, operation);
            Assert.NotEqual(AtmStatus.DispenserFault, resultat.Status);
            if (resultat.Status == AtmStatus.Ok)
            {
                if (operation is Operation.Deposit d)
                {
                    deposes += d.Notes;
                }
                else
                {
                    retire += ((Operation.Withdraw)operation).Amount;
                }
            }

            sortis += resultat.Notes;
            frais += resultat.Fee;
            etat = resultat.Account;

            Assert.Equal(stock.Valeur + deposes - sortis, module.Stock);
            Assert.True(module.Stock is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0 } && TientDans(module.Stock), Texte(module.Stock));
            Assert.Equal(retire, sortis.Value);
            Assert.Equal(compte.Valeur.Balance + deposes.Value - sortis.Value - frais, etat.Balance);
            Assert.Equal(compte.Valeur.WithdrawnToday + retire, etat.WithdrawnToday);
            Assert.InRange(etat.WithdrawnToday, 0, 200);
            Assert.True(etat.Balance >= 0, $"solde négatif : {etat.Balance}");
        }
    }

    /// <summary>Le résultat est déterministe : deux machines neuves dans le même état répondent pareil.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Machine_deterministe(
        Domaine.Compte compte, Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Montant montant, Domaine.Depot depot)
    {
        Assert.Equal(Retirer(compte.Valeur, stock.Valeur, bloquee.Valeur, montant.Valeur).Resultat,
                     Retirer(compte.Valeur, stock.Valeur, bloquee.Valeur, montant.Valeur).Resultat);
        Assert.Equal(Deposer(compte.Valeur, stock.Valeur, bloquee.Valeur, depot.Valeur).Resultat,
                     Deposer(compte.Valeur, stock.Valeur, bloquee.Valeur, depot.Valeur).Resultat);
    }

    // ------------------------------------------------------------------
    // Pannes du module
    // ------------------------------------------------------------------

    /// <summary>
    /// Si le module refuse un passage pendant un retrait : <c>DispenserFault</c>, arrêt immédiat, pas de frais, compte identique,
    /// et <c>Notes</c> contient exactement les billets déjà sortis.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Panne_pendant_un_retrait(Domaine.RetraitFaisable r, Domaine.Rang rang)
    {
        var (normal, temoin) = Retirer(r.Compte, r.Stock, r.Bloquee, r.Montant);
        var appel = 1 + rang.Valeur % temoin.Calls;

        var module = new SpyDispenser(r.Stock, r.Bloquee, failOnCall: appel);
        var resultat = new CashMachine(module).Execute(r.Compte, new Operation.Withdraw(r.Montant));

        Assert.Equal(AtmStatus.Ok, normal.Status);
        Assert.Equal(AtmStatus.DispenserFault, resultat.Status);
        Assert.Equal(0, resultat.Fee);
        Assert.Equal(r.Compte, resultat.Account);
        Assert.Equal(appel, module.Calls);
        Assert.Equal(Somme(module.Dispensed), resultat.Notes);
        Assert.Equal(appel - 1, module.Dispensed.Count);
        Assert.Equal(r.Stock - resultat.Notes, module.Stock);
    }

    /// <summary>Si le module refuse un dépôt : <c>DispenserFault</c>, arrêt immédiat, aucun billet sorti, pas de frais, compte identique.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Panne_pendant_un_depot(Domaine.DepotAccepte d, Domaine.Rang rang)
    {
        var (_, temoin) = Deposer(d.Compte, d.Stock, d.Bloquee, d.Billets);
        var appel = 1 + rang.Valeur % temoin.Calls;

        var module = new SpyDispenser(d.Stock, d.Bloquee, failOnCall: appel);
        var resultat = new CashMachine(module).Execute(d.Compte, new Operation.Deposit(d.Billets));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, d.Compte), resultat);
        Assert.Equal(appel, module.Calls);
        Assert.Empty(module.Dispensed);
    }

    // ------------------------------------------------------------------
    // Contrat du module de billets
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>Dispense</c> : tout ou rien. Refuse, sans rien changer, un passage vide, de plus de 4 billets, qui touche la cassette
    /// bloquée ou dépasse le stock ; sinon retire exactement ces billets.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Module_dispense_tout_ou_rien(Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Demande demande)
    {
        var billets = demande.Valeur;
        var module = new MoneyDispenser(stock.Valeur, bloquee.Valeur);
        var attendu = billets.Total is >= 1 and <= 4
                      && (bloquee.Valeur is not { } b || billets.Of(b) == 0)
                      && Contient(stock.Valeur, billets);

        Assert.Equal(attendu, module.Dispense(billets));
        Assert.Equal(attendu ? stock.Valeur - billets : stock.Valeur, module.Stock);
        Assert.Equal(bloquee.Valeur, module.Jammed);
    }

    /// <summary>
    /// <c>Load</c> : tout ou rien. Refuse, sans rien changer, un dépôt vide, qui touche la cassette bloquée ou dépasse 5 billets
    /// dans une cassette ; sinon ajoute exactement ces billets.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = [typeof(Generateurs)])]
    public void Module_range_tout_ou_rien(Domaine.Stock stock, Domaine.Bloquee bloquee, Domaine.Demande demande)
    {
        var billets = demande.Valeur;
        var module = new MoneyDispenser(stock.Valeur, bloquee.Valeur);
        var attendu = billets.Total > 0
                      && (bloquee.Valeur is not { } b || billets.Of(b) == 0)
                      && TientDans(stock.Valeur + billets);

        Assert.Equal(attendu, module.Load(billets));
        Assert.Equal(attendu ? stock.Valeur + billets : stock.Valeur, module.Stock);
        Assert.Equal(bloquee.Valeur, module.Jammed);
    }
}
