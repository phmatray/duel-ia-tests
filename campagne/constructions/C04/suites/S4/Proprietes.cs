using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Atm.Tests;

// ================================================================================================================
// Tests de propriétés : des règles de la spécification qui doivent tenir pour toutes les entrées du domaine,
// vérifiées sur des cas tirés au hasard par FsCheck. Chaque propriété fixe sa graine et son nombre de cas.
// ================================================================================================================

/// <summary>Une situation du domaine : un compte, le stock du module et sa cassette bloquée.</summary>
public sealed record Situation(AccountState Compte, NoteCounts Stock, Denomination? Bloquee);

/// <summary>Un montant de retrait du domaine : -10, 0, 5, et les multiples de 10 de 10 à 220.</summary>
public sealed record Montant(int Valeur);

/// <summary>Un dépôt du domaine : 0 ou 1 billet par coupure.</summary>
public sealed record Depot(NoteCounts Billets);

/// <summary>Billets à ajouter au stock (le stock reste plafonné à 5 par cassette).</summary>
public sealed record Ajout(NoteCounts Billets);

/// <summary>Un nombre tiré au hasard, pour choisir parmi quelques possibilités connues seulement dans le test.</summary>
public sealed record Tirage(int Valeur);

/// <summary>Un retrait qui passe les contrôles 1 à 3 (montant, plafond, solde) ; le stock est quelconque.</summary>
public sealed record RetraitAdmissible(Situation Situation, int Montant);

/// <summary>Un retrait valide sous le plafond du jour, avec un solde proche de montant + frais (de -15 à +10 €).</summary>
public sealed record RetraitAuBordDuSolde(Situation Situation, int Montant);

/// <summary>Un retrait qui passe les quatre contrôles : une combinaison existe parmi les billets disponibles.</summary>
public sealed record RetraitPossible(Situation Situation, int Montant);

/// <summary>Un retrait possible dont la combinaison compte au plus un billet par coupure, donc redéposable.</summary>
public sealed record RetraitRedeposable(Situation Situation, int Montant);

/// <summary>Un dépôt qui passe les trois contrôles, sur un compte quelconque du domaine.</summary>
public sealed record DepotPossible(Situation Situation, NoteCounts Billets);

/// <summary>Un dépôt possible, sur un compte qui reste dans le domaine et peut retirer la somme déposée.</summary>
public sealed record DepotRetirable(Situation Situation, NoteCounts Billets);

/// <summary>Une suite d'opérations du domaine, jouées sur le même module à partir d'une situation de départ.</summary>
public sealed record Session(Situation Depart, Operation[] Operations)
{
    public override string ToString() => $"Session {{ Depart = {Depart}, Operations = [{string.Join(", ", Operations)}] }}";
}

/// <summary>Les générateurs : ils ne produisent que des valeurs du domaine de la spécification.</summary>
public static class Generateurs
{
    private static readonly Denomination?[] Cassettes = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static readonly int[] MontantsDuDomaine = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly NoteCounts Plein = new(5, 5, 5);

    /// <summary>Un multiple de <paramref name="pas"/> entre <paramref name="min"/> et <paramref name="max"/>, bornes comprises.</summary>
    private static Gen<int> Multiple(int pas, int min, int max) =>
        Gen.Choose((min + pas - 1) / pas, max / pas).Select(k => k * pas);

    private static Gen<NoteCounts> Billets(NoteCounts min, NoteCounts max) =>
        from fifties in Gen.Choose(min.Fifties, max.Fifties)
        from twenties in Gen.Choose(min.Twenties, max.Twenties)
        from tens in Gen.Choose(min.Tens, max.Tens)
        select new NoteCounts(fifties, twenties, tens);

    /// <summary>Au plus <paramref name="max"/> billets par coupure, et aucun de la cassette bloquée.</summary>
    private static NoteCounts Plafond(int max, Denomination? bloquee) => new(
        bloquee == Denomination.Fifty ? 0 : max,
        bloquee == Denomination.Twenty ? 0 : max,
        bloquee == Denomination.Ten ? 0 : max);

    private static readonly Gen<AccountState> Comptes =
        from solde in Multiple(5, 0, 250)
        from retire in Multiple(10, 0, 200)
        select new AccountState(solde, retire);

    private static readonly Gen<Denomination?> Bloquees = Gen.Elements(Cassettes);

    private static readonly Gen<Situation> SituationsDuDomaine =
        from compte in Comptes
        from stock in Billets(NoteCounts.None, Plein)
        from bloquee in Bloquees
        select new Situation(compte, stock, bloquee);

    private static readonly Gen<int> MontantsGen = Gen.Elements(MontantsDuDomaine);

    private static readonly Gen<NoteCounts> DepotsGen = Billets(NoteCounts.None, new(1, 1, 1));

    /// <summary>Un compte qui passe le plafond et le solde pour <paramref name="montant"/>.</summary>
    private static Gen<AccountState> ComptesAdmissibles(int montant) =>
        from retire in Multiple(10, 0, 200 - montant)
        from solde in Multiple(5, montant + (montant > 100 ? 5 : 0), 250)
        select new AccountState(solde, retire);

    /// <summary>Une combinaison disponible de 10 à 200 €, un stock qui la contient, et un compte admissible.</summary>
    private static Gen<(Situation, int)> RetraitsPossiblesGen(int maxParCoupure) =>
        from bloquee in Bloquees
        from combinaison in Billets(NoteCounts.None, Plafond(maxParCoupure, bloquee)).Where(n => n.Value is >= 10 and <= 200)
        from stock in Billets(combinaison, Plein)
        from compte in ComptesAdmissibles(combinaison.Value)
        select (new Situation(compte, stock, bloquee), combinaison.Value);

    private static Gen<(Situation, NoteCounts)> DepotsPossiblesGen(bool retirable) =>
        from bloquee in Bloquees
        from depot in Billets(NoteCounts.None, Plafond(1, bloquee)).Where(n => n.Total > 0)
        from stock in Billets(NoteCounts.None, Plein - depot)
        from solde in Multiple(5, 0, retirable ? 250 - depot.Value : 250)
        from retire in Multiple(10, 0, retirable ? 200 - depot.Value : 200)
        select (new Situation(new AccountState(solde, retire), stock, bloquee), depot);

    private static readonly Gen<Operation> OperationsGen = Gen.OneOf(
        MontantsGen.Select(Operation (a) => new Operation.Withdraw(a)),
        DepotsGen.Select(Operation (n) => new Operation.Deposit(n)));

    public static Arbitrary<Situation> Situations() => SituationsDuDomaine.ToArbitrary();

    public static Arbitrary<Montant> Montants() => MontantsGen.Select(a => new Montant(a)).ToArbitrary();

    public static Arbitrary<Depot> Depots() => DepotsGen.Select(n => new Depot(n)).ToArbitrary();

    public static Arbitrary<Operation> Operations() => OperationsGen.ToArbitrary();

    public static Arbitrary<Ajout> Ajouts() => Billets(NoteCounts.None, Plein).Select(n => new Ajout(n)).ToArbitrary();

    /// <summary>De 0 à 59 : un multiple de 1, 2 et 3, pour choisir uniformément parmi 1, 2 ou 3 possibilités.</summary>
    public static Arbitrary<Tirage> Tirages() => Gen.Choose(0, 59).Select(t => new Tirage(t)).ToArbitrary();

    public static Arbitrary<RetraitAdmissible> RetraitsAdmissibles() =>
        (from montant in Multiple(10, 10, 200)
         from compte in ComptesAdmissibles(montant)
         from stock in Billets(NoteCounts.None, Plein)
         from bloquee in Bloquees
         select new RetraitAdmissible(new Situation(compte, stock, bloquee), montant)).ToArbitrary();

    public static Arbitrary<RetraitAuBordDuSolde> RetraitsAuBordDuSolde() =>
        (from montant in Multiple(10, 10, 200)
         from retire in Multiple(10, 0, 200 - montant)
         from ecart in Gen.Elements(-15, -10, -5, 0, 5, 10)
         let solde = Math.Clamp(montant + (montant > 100 ? 5 : 0) + ecart, 0, 250)
         from stock in Billets(NoteCounts.None, Plein)
         from bloquee in Bloquees
         select new RetraitAuBordDuSolde(new Situation(new AccountState(solde, retire), stock, bloquee), montant)).ToArbitrary();

    public static Arbitrary<RetraitPossible> RetraitsPossibles() =>
        RetraitsPossiblesGen(5).Select(r => new RetraitPossible(r.Item1, r.Item2)).ToArbitrary();

    public static Arbitrary<RetraitRedeposable> RetraitsRedeposables() =>
        RetraitsPossiblesGen(1).Select(r => new RetraitRedeposable(r.Item1, r.Item2)).ToArbitrary();

    public static Arbitrary<DepotPossible> DepotsPossibles() =>
        DepotsPossiblesGen(retirable: false).Select(d => new DepotPossible(d.Item1, d.Item2)).ToArbitrary();

    public static Arbitrary<DepotRetirable> DepotsRetirables() =>
        DepotsPossiblesGen(retirable: true).Select(d => new DepotRetirable(d.Item1, d.Item2)).ToArbitrary();

    public static Arbitrary<Session> Sessions() =>
        (from depart in SituationsDuDomaine
         from longueur in Gen.Choose(1, 30)
         from operations in OperationsGen.ArrayOf(longueur)
         select new Session(depart, operations)).ToArbitrary();
}

[Properties(Arbitrary = new[] { typeof(Generateurs) })]
public class Proprietes
{
    /// <summary>
    /// La graine de chaque propriété. FsCheck 3 attend « graine,gamma » avec un gamma impair : « 42,0 » est refusé
    /// (« Gamma must be odd ») et fait tomber l'hôte de tests ; « 42,1 » est la valeur valide la plus proche.
    /// </summary>
    private const string Graine = "42,1";

    private const int NombreDeCas = 200;

    private const int Plafond = 200;

    private const int Capacite = 5;

    private const int BilletsParPassage = 4;

    // ------------------------------------------------------------------------------------------------------------
    // Outils
    // ------------------------------------------------------------------------------------------------------------

    private static (AtmResult Resultat, RecordingDispenser Module) Executer(
        Situation situation, Operation operation, int? panneAuPassage = null, bool panneAuChargement = false)
    {
        var module = new RecordingDispenser(
            new MoneyDispenser(situation.Stock, situation.Bloquee), panneAuPassage, panneAuChargement);
        var resultat = new CashMachine(module).Execute(situation.Compte, operation);

        return (resultat, module);
    }

    private static AtmResult Retirer(Situation situation, int montant) =>
        Executer(situation, new Operation.Withdraw(montant)).Resultat;

    private static int Frais(int montant) => montant > 100 ? 5 : 0;

    /// <summary>Les billets disponibles : le stock, sans la cassette bloquée.</summary>
    private static NoteCounts Disponibles(Situation situation) => new(
        situation.Bloquee == Denomination.Fifty ? 0 : situation.Stock.Fifties,
        situation.Bloquee == Denomination.Twenty ? 0 : situation.Stock.Twenties,
        situation.Bloquee == Denomination.Ten ? 0 : situation.Stock.Tens);

    /// <summary>Toutes les combinaisons de billets disponibles qui font exactement <paramref name="montant"/>.</summary>
    private static List<NoteCounts> Combinaisons(int montant, NoteCounts disponibles) =>
    [
        .. from fifties in Enumerable.Range(0, disponibles.Fifties + 1)
           from twenties in Enumerable.Range(0, disponibles.Twenties + 1)
           from tens in Enumerable.Range(0, disponibles.Tens + 1)
           let billets = new NoteCounts(fifties, twenties, tens)
           where billets.Value == montant
           select billets,
    ];

    private static bool Contient(NoteCounts grand, NoteCounts petit) =>
        petit.Fifties <= grand.Fifties && petit.Twenties <= grand.Twenties && petit.Tens <= grand.Tens;

    private static void AssertRefusSansEffet(AtmResult resultat, RecordingDispenser module, Situation situation)
    {
        Assert.Equal(new AtmResult(resultat.Status, NoteCounts.None, 0, situation.Compte), resultat);
        Assert.Equal(situation.Stock, module.Stock);
        Assert.Empty(module.DispenseCalls);
        Assert.Empty(module.LoadCalls);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Toutes les opérations
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Le vrai module ne tombe jamais en panne dans le domaine, et sa cassette bloquée ne change pas.</summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void VraiModule_JamaisDePanne(Situation situation, Operation operation)
    {
        var (resultat, module) = Executer(situation, operation);

        Assert.NotEqual(AtmStatus.DispenserFault, resultat.Status);
        Assert.Equal(situation.Bloquee, module.Jammed);
    }

    /// <summary>Sur un refus, rien ne change : pas de billets, pas de frais, compte identique, aucun appel au module.</summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void Refus_NeChangeRien(Situation situation, Operation operation)
    {
        var (resultat, module) = Executer(situation, operation);

        if (resultat.Status != AtmStatus.Ok)
        {
            AssertRefusSansEffet(resultat, module, situation);
        }
    }

    /// <summary>L'argent ne se crée ni ne se perd : solde moins valeur du stock ne bouge que des frais prélevés.</summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void Argent_SeConserve(Situation situation, Operation operation)
    {
        var (resultat, module) = Executer(situation, operation);

        Assert.Equal(
            situation.Compte.Balance - situation.Stock.Value - resultat.Fee,
            resultat.Account.Balance - module.Stock.Value);
    }

    /// <summary>La cassette bloquée garde ses billets, quelle que soit l'opération.</summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void CassetteBloquee_NeBougeJamais(Situation situation, Operation operation)
    {
        var (_, module) = Executer(situation, operation);

        if (situation.Bloquee is { } cassette)
        {
            Assert.Equal(situation.Stock.Of(cassette), module.Stock.Of(cassette));
            Assert.All(module.DispenseCalls, passage => Assert.Equal(0, passage.Of(cassette)));
            Assert.All(module.LoadCalls, depot => Assert.Equal(0, depot.Of(cassette)));
        }
    }

    /// <summary>
    /// La machine est sans mémoire : après une première opération, la suivante donne le même résultat qu'une machine
    /// neuve sur un module neuf dans le même état.
    /// </summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void Machine_SansMemoire(Situation situation, Operation premiere, Operation seconde)
    {
        var module = new MoneyDispenser(situation.Stock, situation.Bloquee);
        var machine = new CashMachine(module);
        machine.Execute(situation.Compte, premiere);
        var stockIntermediaire = module.Stock;

        var resultat = machine.Execute(situation.Compte, seconde);

        var temoin = new MoneyDispenser(stockIntermediaire, situation.Bloquee);
        Assert.Equal(new CashMachine(temoin).Execute(situation.Compte, seconde), resultat);
        Assert.Equal(temoin.Stock, module.Stock);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Le retrait
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Le statut d'un retrait est celui du premier contrôle qui échoue, dans l'ordre de la spécification.</summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void Retrait_StatutDuPremierControleQuiEchoue(Situation situation, Montant montant)
    {
        var a = montant.Valeur;
        var compte = situation.Compte;
        var attendu =
            a <= 0 || a % 10 != 0 ? AtmStatus.InvalidAmount
            : compte.WithdrawnToday + a > Plafond ? AtmStatus.DailyLimitExceeded
            : a + Frais(a) > compte.Balance ? AtmStatus.InsufficientFunds
            : Combinaisons(a, Disponibles(situation)).Count == 0 ? AtmStatus.CannotDispense
            : AtmStatus.Ok;

        Assert.Equal(attendu, Retirer(situation, a).Status);
    }

    /// <summary>
    /// À la frontière du solde : le retrait est refusé pour InsufficientFunds exactement quand le solde ne couvre pas
    /// montant + frais ; sinon le solde ne bloque pas (Ok ou CannotDispense selon les billets).
    /// </summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void Retrait_LeSoldeDoitCouvrirMontantEtFrais(RetraitAuBordDuSolde retrait)
    {
        var (situation, montant) = retrait;
        var statut = Retirer(situation, montant).Status;

        if (montant + Frais(montant) > situation.Compte.Balance)
        {
            Assert.Equal(AtmStatus.InsufficientFunds, statut);
        }
        else
        {
            Assert.Contains(statut, new[] { AtmStatus.Ok, AtmStatus.CannotDispense });
        }
    }

    /// <summary>
    /// Un retrait réussi rend exactement le montant, prélève les bons frais, met le compte à jour, sort les billets du
    /// stock et ne prend que des billets disponibles. Le compte rendu reste dans le domaine.
    /// </summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void RetraitReussi_MontantFraisCompteEtStock(RetraitPossible retrait)
    {
        var (situation, montant) = retrait;
        var (resultat, module) = Executer(situation, new Operation.Withdraw(montant));

        Assert.Equal(AtmStatus.Ok, resultat.Status);
        Assert.Equal(montant, resultat.Notes.Value);
        Assert.Equal(Frais(montant), resultat.Fee);
        Assert.Equal(
            new AccountState(situation.Compte.Balance - montant - Frais(montant), situation.Compte.WithdrawnToday + montant),
            resultat.Account);
        Assert.True(Contient(Disponibles(situation), resultat.Notes), $"{resultat.Notes} hors des billets disponibles.");
        Assert.Equal(situation.Stock - resultat.Notes, module.Stock);
        Assert.Empty(module.LoadCalls);

        Assert.InRange(resultat.Account.Balance, 0, 250);
        Assert.Equal(0, resultat.Account.Balance % 5);
        Assert.InRange(resultat.Account.WithdrawnToday, 0, Plafond);
    }

    /// <summary>Les billets sortent en passages de 1 à 4 billets, dont la somme fait exactement la combinaison.</summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void RetraitReussi_PassagesDeQuatreBilletsAuPlus(RetraitPossible retrait)
    {
        var (situation, montant) = retrait;
        var (resultat, module) = Executer(situation, new Operation.Withdraw(montant));

        Assert.NotEmpty(module.DispenseCalls);
        Assert.All(module.DispenseCalls, passage =>
        {
            Assert.InRange(passage.Total, 1, BilletsParPassage);
            Assert.True(passage is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0 }, $"Passage négatif : {passage}.");
        });
        Assert.Equal(resultat.Notes, module.DispenseCalls.Aggregate(NoteCounts.None, (somme, passage) => somme + passage));
    }

    /// <summary>La combinaison choisie compte le moins de billets possible, et c'est la seule à le faire.</summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void RetraitReussi_LeMoinsDeBilletsPossible(RetraitPossible retrait)
    {
        var (situation, montant) = retrait;
        var resultat = Retirer(situation, montant);

        var combinaisons = Combinaisons(montant, Disponibles(situation));
        Assert.Contains(resultat.Notes, combinaisons);
        Assert.All(
            combinaisons.Where(c => c != resultat.Notes),
            autre => Assert.True(autre.Total > resultat.Notes.Total, $"{autre} fait aussi {montant} € avec moins ou autant de billets que {resultat.Notes}."));
    }

    /// <summary>Le choix des billets ne dépend pas du compte : un compte riche et vierge obtient le même.</summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void Combinaison_NeDependPasDuCompte(RetraitAdmissible retrait)
    {
        var (situation, montant) = retrait;
        var resultat = Retirer(situation, montant);
        var riche = Retirer(situation with { Compte = new AccountState(250, 0) }, montant);

        Assert.Equal(riche.Status, resultat.Status);
        Assert.Equal(riche.Notes, resultat.Notes);
    }

    /// <summary>Une cassette bloquée compte exactement comme une cassette vide.</summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void Retrait_CassetteBloqueeEquivautACassetteVide(Situation situation, Montant montant)
    {
        var vide = situation with { Stock = Disponibles(situation), Bloquee = null };

        Assert.Equal(Retirer(vide, montant.Valeur), Retirer(situation, montant.Valeur));
    }

    /// <summary>
    /// Plus de billets ne rend jamais un retrait plus difficile : ce qui réussissait réussit encore, avec au plus autant de
    /// billets, et seul un CannotDispense peut devenir Ok.
    /// </summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void Retrait_PlusDeStockNeGenePas(RetraitAdmissible retrait, Ajout ajout)
    {
        var (situation, montant) = retrait;
        var somme = situation.Stock + ajout.Billets;
        var garni = situation with
        {
            Stock = new(Math.Min(Capacite, somme.Fifties), Math.Min(Capacite, somme.Twenties), Math.Min(Capacite, somme.Tens)),
        };

        var avant = Retirer(situation, montant);
        var apres = Retirer(garni, montant);

        if (avant.Status == AtmStatus.Ok)
        {
            Assert.Equal(AtmStatus.Ok, apres.Status);
            Assert.True(apres.Notes.Total <= avant.Notes.Total, $"{apres.Notes} compte plus de billets que {avant.Notes}.");
        }
        else
        {
            Assert.Equal(AtmStatus.CannotDispense, avant.Status);
        }
    }

    /// <summary>
    /// Retirer puis redéposer les mêmes billets rend le stock et le solde de départ (pas de frais sous 100 €),
    /// mais le cumul du jour garde la trace du retrait.
    /// </summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void RetraitPuisDepot_RestaureStockEtSolde(RetraitRedeposable retrait)
    {
        var (situation, montant) = retrait;
        var module = new MoneyDispenser(situation.Stock, situation.Bloquee);
        var machine = new CashMachine(module);

        var retire = machine.Execute(situation.Compte, new Operation.Withdraw(montant));
        Assert.Equal(AtmStatus.Ok, retire.Status);
        var redepose = machine.Execute(retire.Account, new Operation.Deposit(retire.Notes));

        Assert.Equal(AtmStatus.Ok, redepose.Status);
        Assert.Equal(situation.Stock, module.Stock);
        Assert.Equal(situation.Compte with { WithdrawnToday = situation.Compte.WithdrawnToday + montant }, redepose.Account);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Le dépôt
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Le statut d'un dépôt est celui du premier contrôle qui échoue, dans l'ordre de la spécification.</summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void Depot_StatutDuPremierControleQuiEchoue(Situation situation, Depot depot)
    {
        var billets = depot.Billets;
        var apres = situation.Stock + billets;
        var attendu =
            billets.Total == 0 ? AtmStatus.InvalidAmount
            : situation.Bloquee is { } cassette && billets.Of(cassette) > 0 ? AtmStatus.CassetteJammed
            : apres.Fifties > Capacite || apres.Twenties > Capacite || apres.Tens > Capacite ? AtmStatus.CassetteFull
            : AtmStatus.Ok;

        Assert.Equal(attendu, Executer(situation, new Operation.Deposit(billets)).Resultat.Status);
    }

    /// <summary>
    /// Un dépôt réussi range les billets en un seul chargement, crédite leur valeur, ne touche pas au cumul du jour, ne
    /// sort aucun billet et ne prélève aucun frais.
    /// </summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void DepotReussi_CrediteEtRange(DepotPossible depot)
    {
        var (situation, billets) = depot;
        var (resultat, module) = Executer(situation, new Operation.Deposit(billets));

        Assert.Equal(
            new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, situation.Compte with { Balance = situation.Compte.Balance + billets.Value }),
            resultat);
        Assert.Equal(situation.Stock + billets, module.Stock);
        Assert.Equal([billets], module.LoadCalls);
        Assert.Empty(module.DispenseCalls);
    }

    /// <summary>
    /// L'argent déposé peut être retiré aussitôt : on retrouve le solde de départ, le cumul du jour augmente de la somme,
    /// et la valeur du stock revient à son niveau de départ.
    /// </summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void DepotPuisRetrait_LaSommeDeposeeEstRetirable(DepotRetirable depot)
    {
        var (situation, billets) = depot;
        var module = new MoneyDispenser(situation.Stock, situation.Bloquee);
        var machine = new CashMachine(module);

        var depose = machine.Execute(situation.Compte, new Operation.Deposit(billets));
        Assert.Equal(AtmStatus.Ok, depose.Status);
        var retire = machine.Execute(depose.Account, new Operation.Withdraw(billets.Value));

        Assert.Equal(AtmStatus.Ok, retire.Status);
        Assert.Equal(0, retire.Fee);
        Assert.Equal(situation.Compte with { WithdrawnToday = situation.Compte.WithdrawnToday + billets.Value }, retire.Account);
        Assert.Equal(situation.Stock.Value, module.Stock.Value);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Les pannes du module
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Si un passage échoue lors d'un retrait : DispenserFault, arrêt aussitôt, pas de frais, compte identique, et
    /// <c>Notes</c> contient exactement les billets des passages déjà réussis.
    /// </summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void PanneAuRetrait_ArretAussitotEtBilletsDejaSortis(RetraitPossible retrait, Tirage tirage)
    {
        var (situation, montant) = retrait;
        var passages = Executer(situation, new Operation.Withdraw(montant)).Module.DispenseCalls;
        var passageEnPanne = 1 + tirage.Valeur % passages.Count;

        var (resultat, module) = Executer(situation, new Operation.Withdraw(montant), panneAuPassage: passageEnPanne);

        var dejaSortis = module.DispenseCalls.Take(passageEnPanne - 1).Aggregate(NoteCounts.None, (somme, passage) => somme + passage);
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, dejaSortis, 0, situation.Compte), resultat);
        Assert.Equal(passageEnPanne, module.DispenseCalls.Count);
        Assert.Equal(situation.Stock - dejaSortis, module.Stock);
        Assert.Empty(module.LoadCalls);
    }

    /// <summary>Si le chargement échoue lors d'un dépôt : DispenserFault, pas de billets, pas de frais, rien ne change.</summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void PanneAuDepot_RienNeChange(DepotPossible depot)
    {
        var (situation, billets) = depot;
        var (resultat, module) = Executer(situation, new Operation.Deposit(billets), panneAuChargement: true);

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, situation.Compte), resultat);
        Assert.Equal(situation.Stock, module.Stock);
        Assert.Equal([billets], module.LoadCalls);
        Assert.Empty(module.DispenseCalls);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Les sessions
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Une suite d'opérations sur le même module, le compte enchaîné d'une opération à la suivante : les invariants
    /// tiennent à chaque pas, et chaque résultat est celui qu'aurait rendu une machine neuve sur un module neuf dans le
    /// même état (la machine ne garde rien d'un appel à l'autre). Les dépôts qui feraient sortir le solde du domaine (au-delà de 250 €) sont sautés.
    /// </summary>
    [Property(Replay = Graine, MaxTest = NombreDeCas)]
    public void Session_LesInvariantsTiennentAChaquePas(Session session)
    {
        var depart = session.Depart;
        var module = new MoneyDispenser(depart.Stock, depart.Bloquee);
        var machine = new CashMachine(module);
        var compte = depart.Compte;
        var fraisPayes = 0;

        foreach (var operation in session.Operations)
        {
            if (operation is Operation.Deposit { Notes: var depot } && compte.Balance + depot.Value > 250)
            {
                continue;
            }

            var stockAvant = module.Stock;
            var resultat = machine.Execute(compte, operation);

            var temoin = new MoneyDispenser(stockAvant, depart.Bloquee);
            Assert.Equal(new CashMachine(temoin).Execute(compte, operation), resultat);
            Assert.Equal(temoin.Stock, module.Stock);

            Assert.NotEqual(AtmStatus.DispenserFault, resultat.Status);
            Assert.True(resultat.Fee is 0 or 5, $"Frais inattendus : {resultat.Fee}.");
            Assert.True(resultat.Account.WithdrawnToday >= compte.WithdrawnToday, "Le cumul du jour a baissé.");
            Assert.InRange(resultat.Account.WithdrawnToday, 0, Plafond);
            Assert.InRange(resultat.Account.Balance, 0, 250);
            Assert.InRange(module.Stock.Fifties, 0, Capacite);
            Assert.InRange(module.Stock.Twenties, 0, Capacite);
            Assert.InRange(module.Stock.Tens, 0, Capacite);
            var ranges = resultat.Status == AtmStatus.Ok && operation is Operation.Deposit d ? d.Notes : NoteCounts.None;
            Assert.Equal(stockAvant - resultat.Notes + ranges, module.Stock);
            if (depart.Bloquee is { } cassette)
            {
                Assert.Equal(depart.Stock.Of(cassette), module.Stock.Of(cassette));
            }

            fraisPayes += resultat.Fee;
            Assert.Equal(depart.Compte.Balance - depart.Stock.Value, resultat.Account.Balance - module.Stock.Value + fraisPayes);

            compte = resultat.Account;
        }
    }
}
