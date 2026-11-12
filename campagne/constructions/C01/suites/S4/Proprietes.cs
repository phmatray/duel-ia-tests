using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Atm.Tests;

// =====================================================================================================
// Tests de propriétés (FsCheck).
//
// Chaque propriété est une règle de la spécification qui doit tenir pour toute entrée du domaine ; FsCheck la vérifie sur
// 200 cas tirés au hasard, avec une graine fixe pour que les tests soient reproductibles.
//
// Graine : la consigne demandait Replay = "42,0". Avec FsCheck 3.3.0, le second nombre est le « gamma » du générateur
// SplitMix et doit être impair : "42,0" lève « Gamma must be odd » et fait planter tout le processus de test. On garde donc
// la graine 42 avec le plus petit gamma valide, 1.
// =====================================================================================================

/// <summary>L'état d'avant l'opération : le compte et le module de billets (stock et cassette bloquée).</summary>
public sealed record Situation(AccountState Compte, NoteCounts Stock, Denomination? Bloquee)
{
    /// <summary>Les billets utilisables pour un retrait : le stock, sans la cassette bloquée.</summary>
    public NoteCounts Disponible => new(
        Bloquee == Denomination.Fifty ? 0 : Stock.Fifties,
        Bloquee == Denomination.Twenty ? 0 : Stock.Twenties,
        Bloquee == Denomination.Ten ? 0 : Stock.Tens);
}

/// <summary>Un montant de retrait du domaine : -10, 0, 5, et les multiples de 10 de 10 à 220.</summary>
public sealed record Montant(int Valeur);

/// <summary>Un dépôt du domaine : 0 ou 1 billet par coupure (le dépôt vide compris).</summary>
public sealed record Depot(NoteCounts Billets);

/// <summary>Un retrait que la spécification accepte forcément : tous les contrôles passent et les billets existent.</summary>
public sealed record RetraitPossible(Situation Situation, int Montant);

/// <summary>Un retrait possible et un indice de passage qui existe forcément, pour simuler une panne du module.</summary>
public sealed record RetraitEnPanne(Situation Situation, int Montant, int PassageEnPanne);

/// <summary>Une suite d'opérations du domaine.</summary>
public sealed record Historique(IReadOnlyList<Operation> Operations)
{
    public override string ToString() => $"[{string.Join(", ", Operations)}]";
}

/// <summary>Les générateurs : ils ne produisent que des valeurs du domaine de la spécification.</summary>
public static class Domaine
{
    public static readonly Denomination[] Coupures = [Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public static readonly int[] Montants = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static Gen<int> Solde => Gen.Choose(0, 50).Select(x => x * 5);

    public static Gen<int> DejaRetire => Gen.Choose(0, 20).Select(x => x * 10);

    public static Gen<NoteCounts> Billets(int max) =>
        from f in Gen.Choose(0, max)
        from t in Gen.Choose(0, max)
        from c in Gen.Choose(0, max)
        select new NoteCounts(f, t, c);

    public static Gen<Denomination?> Bloquee =>
        Gen.Elements<Denomination?>(null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten);

    public static Gen<Situation> Situations =>
        from solde in Solde
        from retire in DejaRetire
        from stock in Billets(5)
        from bloquee in Bloquee
        select new Situation(new AccountState(solde, retire), stock, bloquee);

    public static Gen<Operation> Operations => Gen.OneOf(
        Gen.Elements(Montants).Select(m => (Operation)new Operation.Withdraw(m)),
        Billets(1).Select(d => (Operation)new Operation.Deposit(d)));

    /// <summary>
    /// Un retrait accepté : on tire une combinaison parmi les billets disponibles, puis un compte qui la permet
    /// (solde suffisant frais compris, plafond du jour respecté).
    /// </summary>
    public static Gen<RetraitPossible> RetraitsPossibles =>
        from stock in Billets(5)
        from bloquee in Bloquee
        let disponible = new Situation(default, stock, bloquee).Disponible
        from f in Gen.Choose(0, disponible.Fifties)
        from t in Gen.Choose(0, disponible.Twenties)
        from c in Gen.Choose(0, disponible.Tens)
        let montant = new NoteCounts(f, t, c).Value
        where montant is >= 10 and <= CashMachine.DailyLimit
        let frais = montant > 100 ? 5 : 0
        from solde in Gen.Choose((montant + frais + 4) / 5, 50).Select(x => x * 5)
        from retire in Gen.Choose(0, (CashMachine.DailyLimit - montant) / 10).Select(x => x * 10)
        select new RetraitPossible(new Situation(new AccountState(solde, retire), stock, bloquee), montant);

    /// <summary>Toutes les combinaisons de billets pris dans <paramref name="disponible"/> qui font exactement le montant.</summary>
    public static IEnumerable<NoteCounts> Combinaisons(int montant, NoteCounts disponible)
    {
        for (var f = 0; f <= disponible.Fifties; f++)
        for (var t = 0; t <= disponible.Twenties; t++)
        for (var c = 0; c <= disponible.Tens; c++)
        {
            var n = new NoteCounts(f, t, c);
            if (n.Value == montant) yield return n;
        }
    }

    public static NoteCounts? MeilleureCombinaison(int montant, NoteCounts disponible) =>
        Combinaisons(montant, disponible).Select(n => (NoteCounts?)n).MinBy(n => n!.Value.Total);
}

/// <summary>Les arbitraires que FsCheck utilise pour les paramètres des propriétés.</summary>
public static class Arbitraires
{
    public static Arbitrary<AccountState> ArbCompte() => Arb.From(
        from solde in Domaine.Solde
        from retire in Domaine.DejaRetire
        select new AccountState(solde, retire));

    public static Arbitrary<Operation> ArbOperation() => Arb.From(Domaine.Operations);

    public static Arbitrary<Situation> ArbSituation() => Arb.From(Domaine.Situations);

    public static Arbitrary<Montant> ArbMontant() => Arb.From(Gen.Elements(Domaine.Montants).Select(m => new Montant(m)));

    public static Arbitrary<Depot> ArbDepot() => Arb.From(Domaine.Billets(1).Select(d => new Depot(d)));

    public static Arbitrary<RetraitPossible> ArbRetraitPossible() => Arb.From(Domaine.RetraitsPossibles);

    public static Arbitrary<RetraitEnPanne> ArbRetraitEnPanne() => Arb.From(
        from r in Domaine.RetraitsPossibles
        let nombreDeBillets = Domaine.MeilleureCombinaison(r.Montant, r.Situation.Disponible)!.Value.Total
        // Il y a au moins ⌈billets / 4⌉ passages, puisqu'un passage compte 4 billets au plus.
        from k in Gen.Choose(0, (nombreDeBillets + 3) / 4 - 1)
        select new RetraitEnPanne(r.Situation, r.Montant, k));

    public static Arbitrary<Historique> ArbHistorique() => Arb.From(
        from n in Gen.Choose(0, 8)
        from ops in Gen.ArrayOf(Domaine.Operations, n)
        select new Historique(ops));

    public static Arbitrary<Denomination> ArbCoupure() => Arb.From(Gen.Elements(Domaine.Coupures));
}

[Properties(Arbitrary = [typeof(Arbitraires)])]
public class Proprietes
{
    private static readonly AtmStatus[] StatutsDeRetrait =
        [AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.DailyLimitExceeded, AtmStatus.InsufficientFunds, AtmStatus.CannotDispense];

    private static readonly AtmStatus[] StatutsDeDepot =
        [AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.CassetteJammed, AtmStatus.CassetteFull];

    /// <summary>Exécute l'opération avec le vrai module, enveloppé pour enregistrer les appels (sans panne).</summary>
    private static (AtmResult Resultat, FaultyDispenser Module) Executer(Situation s, Operation operation)
    {
        var module = new FaultyDispenser(s.Stock, s.Bloquee);
        return (new CashMachine(module).Execute(s.Compte, operation), module);
    }

    private static AtmResult Retirer(Situation s, int montant) => Executer(s, new Operation.Withdraw(montant)).Resultat;

    private static int Frais(int montant) => montant > CashMachine.FeeThreshold ? CashMachine.WithdrawalFee : 0;

    private static NoteCounts Somme(IEnumerable<NoteCounts> billets) => billets.Aggregate(NoteCounts.None, (a, b) => a + b);

    /// <summary>
    /// Adapte la situation pour qu'un dépôt de ces billets passe : place libérée dans les cassettes, cassette touchée
    /// débloquée, et solde réduit pour que le solde après le dépôt reste dans le domaine (250 au plus).
    /// </summary>
    private static Situation PlacePour(Situation s, NoteCounts billets) => s with
    {
        Compte = s.Compte with { Balance = Math.Min(s.Compte.Balance, 250 - billets.Value) },
        Stock = new NoteCounts(
            Math.Min(s.Stock.Fifties, 5 - billets.Fifties),
            Math.Min(s.Stock.Twenties, 5 - billets.Twenties),
            Math.Min(s.Stock.Tens, 5 - billets.Tens)),
        Bloquee = s.Bloquee is { } b && billets.Of(b) > 0 ? null : s.Bloquee,
    };

    /// <summary>Le dépôt tiré, ou un billet de 10 s'il est vide.</summary>
    private static NoteCounts NonVide(Depot d) => d.Billets.Total == 0 ? new NoteCounts(0, 0, 1) : d.Billets;

    private static void Inchange(Situation s, AtmResult r, FaultyDispenser module)
    {
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(s.Compte, r.Account);
        Assert.Equal(s.Stock, module.Stock);
        Assert.Empty(module.Dispensed);
        Assert.Empty(module.Loaded);
    }

    // ---- Retrait : les contrôles et leur ordre ---------------------------------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_DansLeDomaine_RendUnStatutDeRetrait_JamaisDePanne(Situation s, Montant m)
    {
        var r = Retirer(s, m.Valeur);
        Assert.Contains(r.Status, StatutsDeRetrait);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_MontantInvalide_SiEtSeulementSiNegatifNulOuPasMultipleDe10(Situation s, Montant m)
    {
        var invalide = m.Valeur <= 0 || m.Valeur % 10 != 0;
        Assert.Equal(invalide, Retirer(s, m.Valeur).Status == AtmStatus.InvalidAmount);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_PlafondDepasse_QuelsQueSoientLeSoldeEtLeStock(Situation s, Montant m)
    {
        // On place le compte juste au-dessus du plafond pour ce montant, si c'est possible dans le domaine.
        if (m.Valeur <= 0 || m.Valeur % 10 != 0) return;
        var retire = Math.Max(s.Compte.WithdrawnToday, CashMachine.DailyLimit - m.Valeur + 10);
        if (retire > CashMachine.DailyLimit) return;

        var r = Retirer(s with { Compte = s.Compte with { WithdrawnToday = retire } }, m.Valeur);
        Assert.Equal(AtmStatus.DailyLimitExceeded, r.Status);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_LePremierControleQuiEchoueDonneLeStatut(Situation s, Montant m)
    {
        var montant = m.Valeur;
        var r = Retirer(s, montant);

        // Chaque statut de refus implique que les contrôles précédents sont passés et que le sien échoue.
        var montantValide = montant > 0 && montant % 10 == 0;
        var plafondOk = s.Compte.WithdrawnToday + montant <= CashMachine.DailyLimit;
        var fondsOk = montant + Frais(montant) <= s.Compte.Balance;
        var billetsOk = Domaine.Combinaisons(montant, s.Disponible).Any();

        var attendu =
            !montantValide ? AtmStatus.InvalidAmount
            : !plafondOk ? AtmStatus.DailyLimitExceeded
            : !fondsOk ? AtmStatus.InsufficientFunds
            : !billetsOk ? AtmStatus.CannotDispense
            : AtmStatus.Ok;

        Assert.Equal(attendu, r.Status);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_Refuse_NeChangeRien_EtNeTouchePasAuModule(Situation s, Montant m)
    {
        var (r, module) = Executer(s, new Operation.Withdraw(m.Valeur));
        if (r.Status == AtmStatus.Ok) return;

        Inchange(s, r, module);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_CannotDispense_AucuneCombinaisonDeBilletsDisponiblesNeFaitLeMontant(Situation s, Montant m)
    {
        // Pour viser ce refus, on lève les trois premiers contrôles : solde maximal, rien de retiré aujourd'hui.
        var montant = m.Valeur;
        if (montant <= 0 || montant % 10 != 0 || montant > CashMachine.DailyLimit) return;
        var r = Retirer(s with { Compte = new AccountState(250, 0) }, montant);

        Assert.Equal(!Domaine.Combinaisons(montant, s.Disponible).Any(), r.Status == AtmStatus.CannotDispense);
    }

    // ---- Retrait accepté --------------------------------------------------------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_Possible_EstAccepte_AvecLesBonsFraisEtLeBonCompte(RetraitPossible p)
    {
        var (s, montant) = (p.Situation, p.Montant);
        var r = Retirer(s, montant);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(montant > 100 ? 5 : 0, r.Fee);
        Assert.Equal(s.Compte.Balance - montant - r.Fee, r.Account.Balance);
        Assert.Equal(s.Compte.WithdrawnToday + montant, r.Account.WithdrawnToday);
        Assert.InRange(r.Account.Balance, 0, 250);
        Assert.InRange(r.Account.WithdrawnToday, 0, CashMachine.DailyLimit);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_Accepte_LesBilletsFontLeMontant_ViennentDuStockDisponible_EtEnSortent(RetraitPossible p)
    {
        var (s, montant) = (p.Situation, p.Montant);
        var (r, module) = Executer(s, new Operation.Withdraw(montant));

        Assert.Equal(montant, r.Notes.Value);
        var disponible = s.Disponible;
        Assert.True(r.Notes is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0 }, $"billets négatifs : {r.Notes}");
        Assert.True(r.Notes.Fifties <= disponible.Fifties && r.Notes.Twenties <= disponible.Twenties && r.Notes.Tens <= disponible.Tens,
            $"billets {r.Notes} hors du disponible {disponible}");
        Assert.Equal(s.Stock - r.Notes, module.Stock);
        Assert.Equal(s.Bloquee, module.Jammed);
        Assert.Empty(module.Loaded);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_Accepte_AucuneCombinaisonNeCompteMoinsDeBillets_EtLeMinimumEstUnique(RetraitPossible p)
    {
        var (s, montant) = (p.Situation, p.Montant);
        var r = Retirer(s, montant);

        var combinaisons = Domaine.Combinaisons(montant, s.Disponible).ToList();
        var minimum = combinaisons.Min(n => n.Total);
        Assert.Equal(minimum, r.Notes.Total);
        Assert.Single(combinaisons, n => n.Total == minimum);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_Accepte_SortEnPassagesDe1A4Billets_HorsCassetteBloquee_QuiFontLaCombinaison(RetraitPossible p)
    {
        var (r, module) = Executer(p.Situation, new Operation.Withdraw(p.Montant));

        Assert.NotEmpty(module.Dispensed);
        Assert.All(module.Dispensed, passage =>
        {
            Assert.InRange(passage.Total, 1, MoneyDispenser.MaxNotesPerPass);
            Assert.True(passage is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0 }, $"passage négatif : {passage}");
            if (p.Situation.Bloquee is { } bloquee) Assert.Equal(0, passage.Of(bloquee));
        });
        Assert.Equal(r.Notes, Somme(module.Dispensed));
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_LesFraisNeComptentPasDansLePlafond(RetraitPossible p)
    {
        // Le compte atteint exactement le plafond avec ce retrait : il reste accepté, même avec des frais.
        var s = p.Situation with { Compte = p.Situation.Compte with { WithdrawnToday = CashMachine.DailyLimit - p.Montant } };
        var r = Retirer(s, p.Montant);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(CashMachine.DailyLimit, r.Account.WithdrawnToday);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_SoldeExactementSuffisant_FraisCompris_EstAccepte(RetraitPossible p)
    {
        var montant = p.Montant;
        var s = p.Situation with { Compte = p.Situation.Compte with { Balance = montant + Frais(montant) } };
        var r = Retirer(s, montant);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_SoldeJusteInsuffisant_EstRefusePourFonds(RetraitPossible p)
    {
        var montant = p.Montant;
        var s = p.Situation with { Compte = p.Situation.Compte with { Balance = montant + Frais(montant) - 5 } };

        Assert.Equal(AtmStatus.InsufficientFunds, Retirer(s, montant).Status);
    }

    // ---- Retrait : relations entre deux situations voisines ----------------------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_PlusDeSolde_MemesBillets_SoldeFinalDecaleDeLaMemeSomme(RetraitPossible p, PositiveInt plus)
    {
        var s = p.Situation;
        var solde = Math.Min(250, s.Compte.Balance + plus.Get * 5);
        var avant = Retirer(s, p.Montant);
        var apres = Retirer(s with { Compte = s.Compte with { Balance = solde } }, p.Montant);

        Assert.Equal(AtmStatus.Ok, apres.Status);
        Assert.Equal(avant.Notes, apres.Notes);
        Assert.Equal(avant.Fee, apres.Fee);
        Assert.Equal(solde - s.Compte.Balance, apres.Account.Balance - avant.Account.Balance);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_MoinsDejaRetire_ResteAccepte_AvecLesMemesBillets(RetraitPossible p, NonNegativeInt moins)
    {
        var s = p.Situation;
        var retire = Math.Max(0, s.Compte.WithdrawnToday - moins.Get * 10);
        var avant = Retirer(s, p.Montant);
        var apres = Retirer(s with { Compte = s.Compte with { WithdrawnToday = retire } }, p.Montant);

        Assert.Equal(AtmStatus.Ok, apres.Status);
        Assert.Equal(avant.Notes, apres.Notes);
        Assert.Equal(retire + p.Montant, apres.Account.WithdrawnToday);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_PlusDeBillets_ResteAccepte_EtNeSortPasPlusDeBillets(RetraitPossible p)
    {
        var s = p.Situation;
        var plein = s with { Stock = new NoteCounts(5, 5, 5) };
        var avant = Retirer(s, p.Montant);
        var apres = Retirer(plein, p.Montant);

        Assert.Equal(AtmStatus.Ok, apres.Status);
        Assert.True(apres.Notes.Total <= avant.Notes.Total, $"{apres.Notes} compte plus de billets que {avant.Notes}");
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_LeContenuDeLaCassetteBloqueeNeChangeRien(Situation s, Montant m, NonNegativeInt n)
    {
        if (s.Bloquee is not { } bloquee) return;

        var nombre = n.Get % 6;
        var autre = s with
        {
            Stock = bloquee switch
            {
                Denomination.Fifty => s.Stock with { Fifties = nombre },
                Denomination.Twenty => s.Stock with { Twenties = nombre },
                _ => s.Stock with { Tens = nombre },
            },
        };

        Assert.Equal(Retirer(s, m.Valeur), Retirer(autre, m.Valeur));
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Retrait_DebloquerUneCassetteVideNeChangeRien(Situation s, Montant m)
    {
        // Une cassette vide compte pour zéro, bloquée ou non.
        if (s.Bloquee is not { } bloquee) return;
        var vide = s with
        {
            Stock = bloquee switch
            {
                Denomination.Fifty => s.Stock with { Fifties = 0 },
                Denomination.Twenty => s.Stock with { Twenties = 0 },
                _ => s.Stock with { Tens = 0 },
            },
        };

        Assert.Equal(Retirer(vide, m.Valeur), Retirer(vide with { Bloquee = null }, m.Valeur));
    }

    // ---- Dépôt ------------------------------------------------------------------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Depot_DansLeDomaine_RendUnStatutDeDepot_JamaisDePanne(Situation s, Depot d)
    {
        var (r, _) = Executer(s, new Operation.Deposit(d.Billets));
        Assert.Contains(r.Status, StatutsDeDepot);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Depot_LePremierControleQuiEchoueDonneLeStatut(Situation s, Depot d)
    {
        var (r, _) = Executer(s, new Operation.Deposit(d.Billets));

        var apres = s.Stock + d.Billets;
        var attendu =
            d.Billets.Total == 0 ? AtmStatus.InvalidAmount
            : s.Bloquee is { } b && d.Billets.Of(b) > 0 ? AtmStatus.CassetteJammed
            : apres.Fifties > 5 || apres.Twenties > 5 || apres.Tens > 5 ? AtmStatus.CassetteFull
            : AtmStatus.Ok;

        Assert.Equal(attendu, r.Status);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Depot_Vide_EstToujoursInvalide(Situation s)
    {
        var (r, module) = Executer(s, new Operation.Deposit(NoteCounts.None));

        Assert.Equal(AtmStatus.InvalidAmount, r.Status);
        Inchange(s, r, module);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Depot_AvecUnBilletDeLaCassetteBloquee_EstRefuse_MemeSiLaCassetteEstPleine(Situation s, Depot d, Denomination bloquee)
    {
        // On force un billet de la cassette bloquée dans le dépôt.
        var billets = bloquee switch
        {
            Denomination.Fifty => d.Billets with { Fifties = 1 },
            Denomination.Twenty => d.Billets with { Twenties = 1 },
            _ => d.Billets with { Tens = 1 },
        };
        var situation = s with { Bloquee = bloquee };
        var (r, module) = Executer(situation, new Operation.Deposit(billets));

        Assert.Equal(AtmStatus.CassetteJammed, r.Status);
        Inchange(situation, r, module);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Depot_Refuse_NeChangeRien_EtNeTouchePasAuModule(Situation s, Depot d)
    {
        var (r, module) = Executer(s, new Operation.Deposit(d.Billets));
        if (r.Status == AtmStatus.Ok) return;

        Inchange(s, r, module);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Depot_Accepte_CrediteLaValeur_RangeLesBillets_SansFraisNiBilletsRendus(Situation s, Depot d)
    {
        var billets = NonVide(d);
        var situation = PlacePour(s, billets);

        var (r, module) = Executer(situation, new Operation.Deposit(billets));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(new AccountState(situation.Compte.Balance + billets.Value, situation.Compte.WithdrawnToday), r.Account);
        Assert.Equal(situation.Stock + billets, module.Stock);
        Assert.Equal(new[] { billets }, module.Loaded);
        Assert.Empty(module.Dispensed);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Depot_DeuxDepotsQuiTiennentEnsemble_PassentDansLesDeuxOrdres_EtDonnentLeMemeEtat(Situation s, Depot d1, Depot d2)
    {
        var (premier, second) = (NonVide(d1), NonVide(d2));
        var situation = PlacePour(s, premier + second);

        (AtmResult, AtmResult, NoteCounts) Enchainer(NoteCounts a, NoteCounts b)
        {
            var module = new MoneyDispenser(situation.Stock, situation.Bloquee);
            var machine = new CashMachine(module);
            var r1 = machine.Execute(situation.Compte, new Operation.Deposit(a));
            var r2 = machine.Execute(r1.Account, new Operation.Deposit(b));
            return (r1, r2, module.Stock);
        }

        var (aller1, aller2, stockAller) = Enchainer(premier, second);
        var (retour1, retour2, stockRetour) = Enchainer(second, premier);

        Assert.All([aller1, aller2, retour1, retour2], r => Assert.Equal(AtmStatus.Ok, r.Status));
        Assert.Equal(aller2.Account, retour2.Account);
        Assert.Equal(situation.Stock + premier + second, stockAller);
        Assert.Equal(stockAller, stockRetour);
    }

    // ---- Retrait et dépôt enchaînés -----------------------------------------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void DepotPuisRetraitDeLaMemeValeur_NeManqueJamaisDeBillets(Situation s, Depot d)
    {
        var billets = NonVide(d);
        var situation = PlacePour(s, billets);
        var module = new MoneyDispenser(situation.Stock, situation.Bloquee);
        var machine = new CashMachine(module);

        var depot = machine.Execute(situation.Compte, new Operation.Deposit(billets));
        Assert.Equal(AtmStatus.Ok, depot.Status);

        var retrait = machine.Execute(depot.Account, new Operation.Withdraw(billets.Value));
        Assert.NotEqual(AtmStatus.CannotDispense, retrait.Status);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void RetraitPuisRedepotDesBillets_RestaureLeStock_EtNeCouteQueLesFrais(RetraitPossible p)
    {
        var s = p.Situation;
        var module = new MoneyDispenser(s.Stock, s.Bloquee);
        var machine = new CashMachine(module);

        var retrait = machine.Execute(s.Compte, new Operation.Withdraw(p.Montant));
        Assert.Equal(AtmStatus.Ok, retrait.Status);

        // Un dépôt du domaine compte au plus un billet par coupure : on redépose en autant de fois qu'il faut.
        var compte = retrait.Account;
        var reste = retrait.Notes;
        while (reste.Total > 0)
        {
            var lot = new NoteCounts(Math.Min(1, reste.Fifties), Math.Min(1, reste.Twenties), Math.Min(1, reste.Tens));
            var depot = machine.Execute(compte, new Operation.Deposit(lot));
            Assert.Equal(AtmStatus.Ok, depot.Status);
            compte = depot.Account;
            reste -= lot;
        }

        Assert.Equal(s.Stock, module.Stock);
        Assert.Equal(s.Compte.Balance - retrait.Fee, compte.Balance);
        Assert.Equal(s.Compte.WithdrawnToday + p.Montant, compte.WithdrawnToday);
    }

    // ---- Une machine sans mémoire -----------------------------------------------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Machine_SansMemoire_LeResultatNeDependQueDuCompteDeLOperationEtDuModule(
        Situation s, Historique historique, AccountState compte, Operation operation)
    {
        // Une machine qui a déjà servi (sur d'autres comptes) se comporte comme une machine neuve devant un module identique.
        var module = new MoneyDispenser(s.Stock, s.Bloquee);
        var usee = new CashMachine(module);
        foreach (var op in historique.Operations)
        {
            // Le solde d'entrée reste dans le domaine (250 au plus).
            if (op is Operation.Deposit(var d) && s.Compte.Balance + d.Value > 250) continue;
            usee.Execute(s.Compte, op);
        }

        var jumeau = new MoneyDispenser(module.Stock, module.Jammed);
        var attendu = new CashMachine(jumeau).Execute(compte, operation);

        Assert.Equal(attendu, usee.Execute(compte, operation));
        Assert.Equal(jumeau.Stock, module.Stock);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Machine_Deterministe_LaMemeEntreeDonneLeMemeResultat(Situation s, Operation operation)
    {
        var (r1, m1) = Executer(s, operation);
        var (r2, m2) = Executer(s, operation);

        Assert.Equal(r1, r2);
        Assert.Equal(m1.Stock, m2.Stock);
        Assert.Equal(m1.Dispensed, m2.Dispensed);
        Assert.Equal(m1.Loaded, m2.Loaded);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Machine_ConservationDesBillets_SurUneSuiteDOperations(Situation s, Historique historique)
    {
        // Le module ne gagne que les billets déposés et ne perd que les billets rendus.
        var module = new MoneyDispenser(s.Stock, s.Bloquee);
        var machine = new CashMachine(module);
        var compte = s.Compte;
        var rendus = NoteCounts.None;
        var deposes = NoteCounts.None;

        foreach (var op in historique.Operations)
        {
            // On enchaîne le compte rendu ; un dépôt qui ferait sortir le solde du domaine (250 au plus) est sauté.
            if (op is Operation.Deposit(var d) && compte.Balance + d.Value > 250) continue;
            var r = machine.Execute(compte, op);

            Assert.NotEqual(AtmStatus.DispenserFault, r.Status);
            rendus += r.Notes;
            if (r.Status == AtmStatus.Ok && op is Operation.Deposit(var billets)) deposes += billets;
            Assert.Equal(s.Stock + deposes - rendus, module.Stock);
            Assert.Equal(s.Bloquee, module.Jammed);
            compte = r.Account;
        }
    }

    // ---- Pannes du module (hors des résultats attendus, mais à traiter) ----------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Panne_EnRetrait_SArreteAussitot_RendLesBilletsDejaSortis_SansFraisNiDebit(RetraitEnPanne p)
    {
        var s = p.Situation;
        var module = new FaultyDispenser(s.Stock, s.Bloquee) { FailDispenseAt = p.PassageEnPanne };
        var r = new CashMachine(module).Execute(s.Compte, new Operation.Withdraw(p.Montant));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(s.Compte, r.Account);
        // Aucun passage après celui qui échoue.
        Assert.Equal(p.PassageEnPanne + 1, module.Dispensed.Count);
        // Les billets rendus sont exactement ceux des passages réussis, et ils ont bien quitté le module.
        Assert.Equal(Somme(module.Dispensed.Take(p.PassageEnPanne)), r.Notes);
        Assert.Equal(s.Stock - r.Notes, module.Stock);
        Assert.True(r.Notes.Total < Domaine.MeilleureCombinaison(p.Montant, s.Disponible)!.Value.Total);
    }

    [Property(Replay = "42,1", MaxTest = 200)]
    public void Panne_EnDepot_NeRendRien_SansFraisNiCredit(Situation s, Depot d)
    {
        var module = new FaultyDispenser(s.Stock, s.Bloquee) { FailLoad = true };
        var r = new CashMachine(module).Execute(s.Compte, new Operation.Deposit(d.Billets));

        // Un dépôt refusé par les contrôles n'atteint pas le module ; sinon, la panne du module donne DispenserFault.
        var (attendu, _) = Executer(s, new Operation.Deposit(d.Billets));
        Assert.Equal(attendu.Status == AtmStatus.Ok ? AtmStatus.DispenserFault : attendu.Status, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(s.Compte, r.Account);
        Assert.Equal(s.Stock, module.Stock);
        Assert.Equal(attendu.Status == AtmStatus.Ok ? 1 : 0, module.Loaded.Count);
    }
}
