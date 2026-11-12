using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Atm.Tests;

// Tests de propriétés (FsCheck) : des règles de spec.md qui doivent tenir pour TOUTE entrée du domaine,
// vérifiées sur des cas tirés au hasard avec une graine et un nombre de cas fixes.
//
// Les générateurs (Domaine) ne produisent que des valeurs du domaine de la spec, avec le vrai MoneyDispenser.
// Les règles sont réécrites d'après la spec, sans réutiliser le code ni les constantes de CashMachine.

/// <summary>Réglages communs : chaque propriété les reprend explicitement, pour être reproductible.</summary>
internal static class Reglages
{
    /// <summary>
    /// Graine 42. FsCheck 3.3.0 exige un gamma impair : "42,0" lève « Gamma must be odd » et fait planter l'hôte de test.
    /// "42,1" est la graine valide la plus proche.
    /// </summary>
    public const string Graine = "42,1";

    public const ulong Seed = 42;

    public const ulong Gamma = 1;

    public const int Cas = 200;
}

/// <summary>Le domaine de la spécification, sous forme de générateurs FsCheck.</summary>
public static class Domaine
{
    /// <summary>Un retrait : compte, module (stock et cassette bloquée), montant.</summary>
    public sealed record Retrait(AccountState Compte, NoteCounts Stock, Denomination? Bloquee, int Montant);

    /// <summary>Un dépôt : compte, module, billets déposés.</summary>
    public sealed record Depot(AccountState Compte, NoteCounts Stock, Denomination? Bloquee, NoteCounts Billets);

    /// <summary>Un retrait, et des billets à ajouter au stock sans dépasser 5 par cassette.</summary>
    public sealed record RetraitEtAjout(Retrait Retrait, NoteCounts Ajout);

    /// <summary>Deux opérations successives sur la même machine et le même module, avec deux comptes.</summary>
    public sealed record DeuxOperations(
        NoteCounts Stock, Denomination? Bloquee, AccountState Compte1, Operation Operation1, AccountState Compte2, Operation Operation2);

    /// <summary>Une suite d'opérations enchaînées sur un même compte, une même machine et un même module.</summary>
    public sealed record Parcours(NoteCounts Stock, Denomination? Bloquee, AccountState Compte, Operation[] Operations)
    {
        public override string ToString() =>
            $"Parcours {{ Stock = {Stock}, Bloquee = {Bloquee}, Compte = {Compte}, Operations = [{string.Join(", ", Operations.AsEnumerable())}] }}";
    }

    public static readonly int[] Montants = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    public static readonly Gen<AccountState> Compte =
        from balance in Gen.Choose(0, 50)
        from withdrawn in Gen.Choose(0, 20)
        select new AccountState(balance * 5, withdrawn * 10);

    public static readonly Gen<NoteCounts> Stock =
        from fifties in Gen.Choose(0, 5)
        from twenties in Gen.Choose(0, 5)
        from tens in Gen.Choose(0, 5)
        select new NoteCounts(fifties, twenties, tens);

    public static readonly Gen<Denomination?> Bloquee =
        Gen.Elements<Denomination?>(null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten);

    public static readonly Gen<int> Montant = Gen.Elements(Montants);

    public static readonly Gen<NoteCounts> BilletsDeposes =
        from fifties in Gen.Choose(0, 1)
        from twenties in Gen.Choose(0, 1)
        from tens in Gen.Choose(0, 1)
        select new NoteCounts(fifties, twenties, tens);

    public static readonly Gen<Operation> Operation = Gen.OneOf(
        Montant.Select(amount => (Operation)new Operation.Withdraw(amount)),
        BilletsDeposes.Select(notes => (Operation)new Operation.Deposit(notes)));

    /// <summary>N'importe quel retrait du domaine.</summary>
    private static readonly Gen<Retrait> RetraitQuelconque =
        from compte in Compte
        from stock in Stock
        from bloquee in Bloquee
        from montant in Montant
        select new Retrait(compte, stock, bloquee, montant);

    /// <summary>
    /// Un retrait qui passe les contrôles 1 à 3 (montant, plafond, solde) : seul le stock décide.
    /// Sans lui, les retraits réussis seraient rares, car le hasard pur tombe souvent sur un refus.
    /// </summary>
    private static readonly Gen<Retrait> RetraitRecevable =
        from montant in Gen.Choose(1, 20).Select(i => i * 10)
        let frais = montant > 100 ? 5 : 0
        from withdrawn in Gen.Choose(0, (200 - montant) / 10).Select(i => i * 10)
        from balance in Gen.Choose((montant + frais) / 5, 50).Select(i => i * 5)
        from stock in Stock
        from bloquee in Bloquee
        select new Retrait(new AccountState(balance, withdrawn), stock, bloquee, montant);

    /// <summary>
    /// Un retrait au bord des contrôles 2 et 3 : cumul et solde juste autour de leur seuil (frais compris pour le solde).
    /// Le hasard pur n'y tombe presque jamais, or c'est là que se cachent les erreurs de comparaison ou de frais oubliés.
    /// </summary>
    private static readonly Gen<Retrait> RetraitALaLimite =
        from montant in Gen.Choose(1, 22).Select(i => i * 10)
        let frais = montant > 100 ? 5 : 0
        from ecartCumul in Gen.Elements(-10, 0, 10)
        from ecartSolde in Gen.Elements(-5 - frais, -frais, 0, 5)
        from stock in Gen.OneOf(Stock, Gen.Constant(new NoteCounts(5, 5, 5))) // un stock plein, pour que le succès reste possible
        from bloquee in Bloquee
        let withdrawn = Math.Clamp(200 - montant + ecartCumul, 0, 200)
        let balance = Math.Clamp(montant + frais + ecartSolde, 0, 250)
        select new Retrait(new AccountState(balance, withdrawn), stock, bloquee, montant);

    /// <summary>Un retrait d'un montant invalide (négatif, nul ou non multiple de 10), le reste au hasard.</summary>
    private static readonly Gen<Retrait> RetraitInvalide =
        from compte in Compte
        from stock in Stock
        from bloquee in Bloquee
        from montant in Gen.Elements(Montants.Where(m => m <= 0 || m % 10 != 0))
        select new Retrait(compte, stock, bloquee, montant);

    /// <summary>Le mélange : surtout des retraits recevables, pour que les règles des succès soient souvent exercées.</summary>
    private static readonly Gen<Retrait> RetraitGen = Gen.Frequency(
        (3, RetraitQuelconque), (4, RetraitRecevable), (2, RetraitALaLimite), (1, RetraitInvalide));

    private static readonly Gen<Depot> DepotQuelconque =
        from compte in Compte
        from stock in Stock
        from bloquee in Bloquee
        from billets in BilletsDeposes
        select new Depot(compte, stock, bloquee, billets);

    /// <summary>Un dépôt accepté : non vide, hors cassette bloquée, qui tient dans les cassettes.</summary>
    private static readonly Gen<Depot> DepotRecevable =
        from compte in Compte
        from bloquee in Bloquee
        from billets in BilletsDeposes.Where(b => b.Total > 0 && (bloquee is not { } j || b.Of(j) == 0))
        from fifties in Gen.Choose(0, 5 - billets.Fifties)
        from twenties in Gen.Choose(0, 5 - billets.Twenties)
        from tens in Gen.Choose(0, 5 - billets.Tens)
        select new Depot(compte, new NoteCounts(fifties, twenties, tens), bloquee, billets);

    private static readonly Gen<Depot> DepotGen = Gen.OneOf(DepotQuelconque, DepotRecevable);

    public static Arbitrary<Retrait> Retraits() => RetraitGen.ToArbitrary();

    public static Arbitrary<Depot> Depots() => DepotGen.ToArbitrary();

    public static Arbitrary<RetraitEtAjout> RetraitsEtAjouts() =>
        (from retrait in RetraitGen
         from fifties in Gen.Choose(0, 5 - retrait.Stock.Fifties)
         from twenties in Gen.Choose(0, 5 - retrait.Stock.Twenties)
         from tens in Gen.Choose(0, 5 - retrait.Stock.Tens)
         select new RetraitEtAjout(retrait, new NoteCounts(fifties, twenties, tens))).ToArbitrary();

    public static Arbitrary<DeuxOperations> DeuxOperationsSuccessives() =>
        (from stock in Stock
         from bloquee in Bloquee
         from compte1 in Compte
         from operation1 in Operation
         from compte2 in Compte
         from operation2 in Operation
         select new DeuxOperations(stock, bloquee, compte1, operation1, compte2, operation2)).ToArbitrary();

    public static Arbitrary<Parcours> ParcoursEnchaines() =>
        (from stock in Stock
         from bloquee in Bloquee
         from compte in Compte
         from longueur in Gen.Choose(1, 30)
         from operations in Operation.ArrayOf(longueur)
         select new Parcours(stock, bloquee, compte, operations)).ToArbitrary();
}

/// <summary>Les règles de la spec, réécrites indépendamment de CashMachine.</summary>
file static class Spec
{
    public static NoteCounts Disponible(NoteCounts stock, Denomination? bloquee) => new(
        bloquee == Denomination.Fifty ? 0 : stock.Fifties,
        bloquee == Denomination.Twenty ? 0 : stock.Twenties,
        bloquee == Denomination.Ten ? 0 : stock.Tens);

    /// <summary>Toutes les combinaisons de billets disponibles qui font exactement <paramref name="montant"/> (énumération brute).</summary>
    public static List<NoteCounts> Combinaisons(int montant, NoteCounts disponible)
    {
        var exactes = new List<NoteCounts>();
        for (var f = 0; f <= disponible.Fifties; f++)
        for (var t = 0; t <= disponible.Twenties; t++)
        for (var d = 0; d <= disponible.Tens; d++)
            if (f * 50 + t * 20 + d * 10 == montant)
                exactes.Add(new NoteCounts(f, t, d));

        return exactes;
    }

    public static int Frais(int montant) => montant > 100 ? 5 : 0;

    public static AtmStatus StatutDuRetrait(Domaine.Retrait r)
    {
        if (r.Montant <= 0 || r.Montant % 10 != 0) return AtmStatus.InvalidAmount;
        if (r.Compte.WithdrawnToday + r.Montant > 200) return AtmStatus.DailyLimitExceeded;
        if (r.Montant + Frais(r.Montant) > r.Compte.Balance) return AtmStatus.InsufficientFunds;
        if (Combinaisons(r.Montant, Disponible(r.Stock, r.Bloquee)).Count == 0) return AtmStatus.CannotDispense;
        return AtmStatus.Ok;
    }

    public static AtmStatus StatutDuDepot(Domaine.Depot d)
    {
        if (d.Billets.Total == 0) return AtmStatus.InvalidAmount;
        if (d.Bloquee is { } j && d.Billets.Of(j) > 0) return AtmStatus.CassetteJammed;
        var apres = d.Stock + d.Billets;
        if (apres.Fifties > 5 || apres.Twenties > 5 || apres.Tens > 5) return AtmStatus.CassetteFull;
        return AtmStatus.Ok;
    }

    public static bool DansLeDomaine(AccountState compte) =>
        compte.Balance is >= 0 and <= 250 && compte.Balance % 5 == 0
        && compte.WithdrawnToday is >= 0 and <= 200 && compte.WithdrawnToday % 10 == 0;

    public static bool DansLeDomaine(NoteCounts stock) =>
        stock is { Fifties: >= 0 and <= 5, Twenties: >= 0 and <= 5, Tens: >= 0 and <= 5 };

    public static bool BilletsDeposables(NoteCounts billets) =>
        billets is { Fifties: >= 0 and <= 1, Twenties: >= 0 and <= 1, Tens: >= 0 and <= 1 };
}

/// <summary>Exécute une opération sur une machine neuve et un vrai module enregistré.</summary>
file static class Banc
{
    public static (AtmResult Resultat, RecordingDispenser Module) Executer(
        AccountState compte, NoteCounts stock, Denomination? bloquee, Operation operation)
    {
        var module = new RecordingDispenser(new MoneyDispenser(stock, bloquee));
        var resultat = new CashMachine(module).Execute(compte, operation);
        return (resultat, module);
    }

    public static (AtmResult Resultat, RecordingDispenser Module) Executer(Domaine.Retrait r) =>
        Executer(r.Compte, r.Stock, r.Bloquee, new Operation.Withdraw(r.Montant));

    public static (AtmResult Resultat, RecordingDispenser Module) Executer(Domaine.Depot d) =>
        Executer(d.Compte, d.Stock, d.Bloquee, new Operation.Deposit(d.Billets));

    public static NoteCounts Somme(IEnumerable<NoteCounts> passages) => passages.Aggregate(NoteCounts.None, (a, b) => a + b);
}

[Properties(Arbitrary = [typeof(Domaine)])]
public class ProprietesDuRetrait
{
    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void LeStatut_EstDonneParLePremierControleQuiEchoue(Domaine.Retrait r)
    {
        var (resultat, _) = Banc.Executer(r);

        Assert.Equal(Spec.StatutDuRetrait(r), resultat.Status);
    }

    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void UnRefus_NeChangeRien_NiCompteNiBilletsNiFrais(Domaine.Retrait r)
    {
        var (resultat, module) = Banc.Executer(r);
        if (resultat.Status == AtmStatus.Ok) return;

        Assert.Equal(new AtmResult(resultat.Status, NoteCounts.None, 0, r.Compte), resultat);
        Assert.Equal(r.Stock, module.Stock);
        Assert.Empty(module.LoadCalls);
    }

    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void UnSucces_DebiteMontantEtFrais_EtAjouteLeMontantAuCumulDuJour(Domaine.Retrait r)
    {
        var (resultat, _) = Banc.Executer(r);
        if (resultat.Status != AtmStatus.Ok) return;

        var frais = Spec.Frais(r.Montant);
        Assert.Equal(frais, resultat.Fee);
        Assert.Equal(r.Montant, resultat.Notes.Value);
        Assert.Equal(new AccountState(r.Compte.Balance - r.Montant - frais, r.Compte.WithdrawnToday + r.Montant), resultat.Account);
        Assert.True(Spec.DansLeDomaine(resultat.Account), $"compte hors domaine : {resultat.Account}");
    }

    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void UnSucces_RendExactementLesBilletsSortisDuModule_JamaisCeuxDeLaCassetteBloquee(Domaine.Retrait r)
    {
        var (resultat, module) = Banc.Executer(r);
        if (resultat.Status != AtmStatus.Ok) return;

        Assert.Equal(r.Stock - resultat.Notes, module.Stock);
        Assert.True(Spec.DansLeDomaine(module.Stock), $"stock hors domaine : {module.Stock}");
        if (r.Bloquee is { } bloquee) Assert.Equal(0, resultat.Notes.Of(bloquee));
        Assert.Empty(module.LoadCalls);
    }

    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void UnSucces_SortLesBilletsEnPassagesDeUnAQuatreBillets(Domaine.Retrait r)
    {
        var (resultat, module) = Banc.Executer(r);
        if (resultat.Status != AtmStatus.Ok) return;

        Assert.All(module.DispenseCalls, passage => Assert.InRange(passage.Total, 1, 4));
        Assert.Equal(resultat.Notes, Banc.Somme(module.DispenseCalls));
    }

    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void UnSucces_RendLaCombinaisonExacteLaPlusCourte_QuiEstUnique(Domaine.Retrait r)
    {
        var (resultat, _) = Banc.Executer(r);
        if (resultat.Status != AtmStatus.Ok) return;

        var exactes = Spec.Combinaisons(r.Montant, Spec.Disponible(r.Stock, r.Bloquee));
        Assert.Contains(resultat.Notes, exactes);
        Assert.DoesNotContain(exactes, c => c.Total < resultat.Notes.Total);
        Assert.Single(exactes, c => c.Total == resultat.Notes.Total);
    }

    /// <summary>Pour un retrait, une cassette bloquée se comporte exactement comme une cassette vide.</summary>
    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void UneCassetteBloquee_EquivautAUneCassetteVide(Domaine.Retrait r)
    {
        if (r.Bloquee is not { } bloquee) return;

        var videe = Spec.Disponible(r.Stock, bloquee);
        var (avecBlocage, moduleBloque) = Banc.Executer(r);
        var (avecCassetteVide, moduleVide) = Banc.Executer(r with { Stock = videe, Bloquee = null });

        Assert.Equal(avecCassetteVide, avecBlocage);
        Assert.Equal(r.Stock - moduleBloque.Stock, videe - moduleVide.Stock);
    }

    /// <summary>Plus de solde ou moins de retraits déjà faits ne fait jamais échouer un retrait réussi, ni ne le change.</summary>
    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void UnSucces_ResteLeMeme_AvecPlusDeSoldeOuMoinsDeCumul(Domaine.Retrait r)
    {
        var (resultat, _) = Banc.Executer(r);
        if (resultat.Status != AtmStatus.Ok) return;

        for (var balance = r.Compte.Balance; balance <= 250; balance += 5)
        for (var withdrawn = r.Compte.WithdrawnToday; withdrawn >= 0; withdrawn -= 10)
        {
            var compte = new AccountState(balance, withdrawn);
            var (autre, _) = Banc.Executer(r with { Compte = compte });
            var debit = r.Compte.Balance - resultat.Account.Balance;

            Assert.Equal(new AtmResult(AtmStatus.Ok, resultat.Notes, resultat.Fee,
                new AccountState(balance - debit, withdrawn + r.Montant)), autre);
        }
    }

    /// <summary>Ajouter des billets au module ne fait jamais échouer un retrait réussi, ni ne demande plus de billets.</summary>
    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void PlusDeBillets_NeFaitJamaisEchouerUnSucces_NiNeDemandePlusDeBillets(Domaine.RetraitEtAjout cas)
    {
        var r = cas.Retrait;
        var (avant, _) = Banc.Executer(r);
        var (apres, _) = Banc.Executer(r with { Stock = r.Stock + cas.Ajout });

        if (avant.Status == AtmStatus.Ok)
        {
            Assert.Equal(AtmStatus.Ok, apres.Status);
            Assert.True(apres.Notes.Total <= avant.Notes.Total, $"{apres.Notes} compte plus de billets que {avant.Notes}");
            Assert.Equal(avant.Account, apres.Account);
        }

        // Un statut autre que CannotDispense ne dépend pas du stock.
        if (avant.Status != AtmStatus.Ok && avant.Status != AtmStatus.CannotDispense)
            Assert.Equal(avant, apres);
    }
}

[Properties(Arbitrary = [typeof(Domaine)])]
public class ProprietesDuDepot
{
    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void LeStatut_EstDonneParLePremierControleQuiEchoue(Domaine.Depot d)
    {
        var (resultat, _) = Banc.Executer(d);

        Assert.Equal(Spec.StatutDuDepot(d), resultat.Status);
    }

    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void UnRefus_NeChangeRien(Domaine.Depot d)
    {
        var (resultat, module) = Banc.Executer(d);
        if (resultat.Status == AtmStatus.Ok) return;

        Assert.Equal(new AtmResult(resultat.Status, NoteCounts.None, 0, d.Compte), resultat);
        Assert.Equal(d.Stock, module.Stock);
    }

    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void UnSucces_RangeLesBillets_EtCrediteLeurValeur_SansToucherAuCumulDuJour(Domaine.Depot d)
    {
        var (resultat, module) = Banc.Executer(d);
        if (resultat.Status != AtmStatus.Ok) return;

        var credite = d.Compte with { Balance = d.Compte.Balance + d.Billets.Value };
        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0, credite), resultat);
        Assert.Equal(d.Stock + d.Billets, module.Stock);
        Assert.True(Spec.DansLeDomaine(module.Stock), $"stock hors domaine : {module.Stock}");
    }

    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void UnDepot_NeSortJamaisDeBillets(Domaine.Depot d)
    {
        var (_, module) = Banc.Executer(d);

        Assert.Empty(module.DispenseCalls);
    }
}

[Properties(Arbitrary = [typeof(Domaine)])]
public class ProprietesCroisees
{
    /// <summary>Après un dépôt accepté, on peut retirer la même somme : le solde et la valeur du stock reviennent au départ.</summary>
    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void DepotPuisRetraitDeLaMemeSomme_RestaureSoldeEtValeurDuStock(Domaine.Depot d)
    {
        var valeur = d.Billets.Value;
        // On ramène le compte là où le dépôt puis le retrait restent dans le domaine (solde ≤ 250, cumul ≤ 200).
        var compte = new AccountState(Math.Min(d.Compte.Balance, 250 - valeur), Math.Min(d.Compte.WithdrawnToday, 200 - valeur));
        var module = new MoneyDispenser(d.Stock, d.Bloquee);
        var machine = new CashMachine(module);

        var depot = machine.Execute(compte, new Operation.Deposit(d.Billets));
        if (depot.Status != AtmStatus.Ok) return;
        var retrait = machine.Execute(depot.Account, new Operation.Withdraw(valeur));

        Assert.Equal(AtmStatus.Ok, retrait.Status);
        Assert.Equal(0, retrait.Fee);
        Assert.Equal(new AccountState(compte.Balance, compte.WithdrawnToday + valeur), retrait.Account);
        Assert.Equal(d.Stock.Value, module.Stock.Value);
        Assert.True(retrait.Notes.Total <= d.Billets.Total, $"{retrait.Notes} compte plus de billets que le dépôt {d.Billets}");
    }

    /// <summary>Redéposer les billets d'un retrait réussi remet le module exactement dans son état de départ.</summary>
    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void RetraitPuisRedepotDesBillets_RestaureLeStock_EtNeCouteQueLesFrais(Domaine.Retrait r)
    {
        var module = new MoneyDispenser(r.Stock, r.Bloquee);
        var machine = new CashMachine(module);

        var retrait = machine.Execute(r.Compte, new Operation.Withdraw(r.Montant));
        if (retrait.Status != AtmStatus.Ok || !Spec.BilletsDeposables(retrait.Notes)) return;
        var depot = machine.Execute(retrait.Account, new Operation.Deposit(retrait.Notes));

        Assert.Equal(AtmStatus.Ok, depot.Status);
        Assert.Equal(r.Stock, module.Stock);
        Assert.Equal(new AccountState(r.Compte.Balance - retrait.Fee, r.Compte.WithdrawnToday + r.Montant), depot.Account);
    }

    /// <summary>
    /// La machine est sans mémoire : après une première opération, elle répond à la suivante exactement comme une machine neuve
    /// branchée sur un module neuf dans le même état.
    /// </summary>
    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void LaMachine_EstSansMemoire_EntreDeuxAppels(Domaine.DeuxOperations cas)
    {
        var module = new MoneyDispenser(cas.Stock, cas.Bloquee);
        var machine = new CashMachine(module);
        machine.Execute(cas.Compte1, cas.Operation1);

        var neuf = new MoneyDispenser(module.Stock, module.Jammed);
        var attendu = new CashMachine(neuf).Execute(cas.Compte2, cas.Operation2);
        var obtenu = machine.Execute(cas.Compte2, cas.Operation2);

        Assert.Equal(attendu, obtenu);
        Assert.Equal(neuf.Stock, module.Stock);
    }

    /// <summary>
    /// Sur une suite d'opérations enchaînées, rien ne se crée ni ne se perd : les billets passent du module à la poche du client
    /// et inversement, l'argent du compte aussi, à part les frais. Le vrai module ne tombe jamais en panne.
    /// </summary>
    [Property(Replay = Reglages.Graine, MaxTest = Reglages.Cas)]
    public void UneSuiteDOperations_ConserveBilletsEtArgent(Domaine.Parcours parcours)
    {
        var module = new MoneyDispenser(parcours.Stock, parcours.Bloquee);
        var machine = new CashMachine(module);
        var compte = parcours.Compte;
        var poche = NoteCounts.None; // billets reçus moins billets déposés, par coupure (peut être négatif)
        var fraisPayes = 0;

        foreach (var operation in parcours.Operations)
        {
            if (!Spec.DansLeDomaine(compte)) break;

            var cumulAvant = compte.WithdrawnToday;
            var resultat = machine.Execute(compte, operation);
            Assert.NotEqual(AtmStatus.DispenserFault, resultat.Status);

            if (resultat.Status == AtmStatus.Ok)
            {
                poche = operation is Operation.Deposit depot ? poche - depot.Notes : poche + resultat.Notes;
                fraisPayes += resultat.Fee;
            }

            compte = resultat.Account;
            Assert.Equal(parcours.Stock, module.Stock + poche);
            Assert.Equal(parcours.Compte.Balance, compte.Balance + poche.Value + fraisPayes);
            Assert.True(Spec.DansLeDomaine(module.Stock), $"stock hors domaine : {module.Stock}");
            Assert.True(compte.Balance >= 0, $"solde négatif : {compte}");
            Assert.InRange(compte.WithdrawnToday, cumulAvant, 200);
            Assert.Equal(parcours.Bloquee, module.Jammed);
        }
    }
}

/// <summary>
/// Les propriétés ci-dessus ne prouvent rien si le hasard évite certains cas : on vérifie, avec la même graine et le même
/// nombre de cas, que les générateurs atteignent bien chaque statut et les cas limites intéressants.
/// </summary>
public class CouvertureDesGenerateurs
{
    private static readonly Config Reglage = Config.QuickThrowOnFailure.WithReplay(Reglages.Seed, Reglages.Gamma).WithMaxTest(Reglages.Cas);

    private static List<T> Tirer<T>(Arbitrary<T> arbitraire)
    {
        var tires = new List<T>();
        Check.One(Reglage, Prop.ForAll(arbitraire, (T cas) => { tires.Add(cas); return true; }));
        return tires;
    }

    [Fact]
    public void LesRetraits_AtteignentChaqueStatut_EtLesCasLimites()
    {
        var tous = Tirer(Domaine.Retraits()).Select(r => (Cas: r, Resultat: Banc.Executer(r).Resultat)).ToList();
        var reussis = tous.Where(x => x.Resultat.Status == AtmStatus.Ok).ToList();

        Assert.Equal(Reglages.Cas, tous.Count);
        Assert.True(reussis.Count >= Reglages.Cas / 4, $"seulement {reussis.Count} retraits réussis sur {tous.Count}");

        foreach (var statut in Enum.GetValues<AtmStatus>().Where(s => s is not (AtmStatus.CassetteJammed or AtmStatus.CassetteFull or AtmStatus.DispenserFault)))
            Couvre(tous, $"statut {statut}", x => x.Resultat.Status == statut);

        // Chaque montant invalide, y compris quand les contrôles suivants seraient passés.
        foreach (var invalide in Domaine.Montants.Where(m => m <= 0 || m % 10 != 0))
            Couvre(tous, $"montant invalide {invalide} avec plafond et solde suffisants",
                x => x.Cas.Montant == invalide && x.Cas.Compte.WithdrawnToday + invalide <= 200 && x.Cas.Compte.Balance >= invalide);

        Couvre(reussis, "succès avec frais", x => x.Resultat.Fee == 5);
        Couvre(reussis, "succès sans frais", x => x.Resultat.Fee == 0);
        Couvre(reussis, "succès en plusieurs passages", x => x.Resultat.Notes.Total > 4);
        Couvre(reussis, "succès avec une cassette bloquée", x => x.Cas.Bloquee is not null);
        Couvre(reussis, "succès dont les billets sont redéposables", x => Spec.BilletsDeposables(x.Resultat.Notes));

        // Les seuils exacts des contrôles 2 et 3.
        Couvre(reussis, "succès qui porte le cumul à 200 tout juste", x => x.Resultat.Account.WithdrawnToday == 200);
        Couvre(tous, "plafond dépassé de 10 tout juste",
            x => x.Cas.Compte.WithdrawnToday + x.Cas.Montant == 210 && x.Resultat.Status == AtmStatus.DailyLimitExceeded);
        Couvre(reussis, "succès avec frais qui vide le solde tout juste", x => x.Resultat.Account.Balance == 0 && x.Resultat.Fee == 5);
        Couvre(tous, "solde qui couvre le montant mais pas les frais",
            x => x.Cas.Montant > 100 && x.Cas.Compte.Balance == x.Cas.Montant && x.Resultat.Status == AtmStatus.InsufficientFunds);
    }

    [Fact]
    public void LesDepots_AtteignentChaqueStatut()
    {
        var tous = Tirer(Domaine.Depots()).Select(d => (Cas: d, Resultat: Banc.Executer(d).Resultat)).ToList();

        Assert.Equal(Reglages.Cas, tous.Count);
        Assert.True(tous.Count(x => x.Resultat.Status == AtmStatus.Ok) >= Reglages.Cas / 4, "trop peu de dépôts réussis");
        foreach (var statut in new[] { AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.CassetteJammed, AtmStatus.CassetteFull })
            Couvre(tous, $"statut {statut}", x => x.Resultat.Status == statut);
        Couvre(tous, "cassette bloquée ET pleine (le blocage l'emporte)", x =>
            x.Cas.Bloquee is { } j && x.Cas.Billets.Of(j) > 0 && x.Cas.Stock.Of(j) == 5);
        Couvre(tous, "succès qui remplit une cassette tout juste", x =>
            x.Resultat.Status == AtmStatus.Ok && (x.Cas.Stock + x.Cas.Billets) is var s && (s.Fifties == 5 || s.Twenties == 5 || s.Tens == 5));
    }

    private static void Couvre<T>(IEnumerable<T> cas, string description, Func<T, bool> critere) =>
        Assert.True(cas.Any(critere), $"aucun cas tiré ne couvre : {description}");

    [Fact]
    public void LesParcours_EnchainentPlusieursOperationsDansLeDomaine()
    {
        var longueurs = Tirer(Domaine.ParcoursEnchaines()).Select(p => p.Operations.Length).ToList();

        Assert.Contains(longueurs, n => n >= 20);
        Assert.Contains(longueurs, n => n == 1);
    }
}
