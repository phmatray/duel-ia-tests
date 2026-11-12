using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Atm.Tests;

/// <summary>
/// Tests de propriétés : des règles de la spécification qui doivent rester vraies pour toutes les entrées du domaine,
/// vérifiées sur des cas tirés au hasard par FsCheck (graine et nombre de cas fixés, donc reproductibles).
/// Les générateurs de <see cref="Domaine"/> ne produisent que des valeurs du domaine de la spec.
/// </summary>
public class Proprietes
{
    /// <summary>
    /// Graine fixe. FsCheck 3 attend « graine,gamma » avec un gamma impair : « 42,0 » est rejeté
    /// (<c>Gamma must be odd</c>) et fait planter tout le processus de test. On garde donc la graine 42 avec le gamma 1.
    /// </summary>
    private const string Graine = "42,1";

    private const int Cas = 200;

    // ======================================================================================================
    //  Le domaine de la spécification et ses générateurs
    // ======================================================================================================

    /// <summary>Le compte, le stock du module et la cassette bloquée avant l'opération.</summary>
    public sealed record Situation(AccountState Compte, NoteCounts Stock, Denomination? Bloquee);

    /// <summary>Un montant de retrait du domaine : -10, 0, 5 et les multiples de 10 de 10 à 220.</summary>
    public sealed record Montant(int Valeur);

    /// <summary>Un dépôt du domaine : 0 ou 1 billet par coupure (éventuellement vide).</summary>
    public sealed record Depot(NoteCounts Billets);

    /// <summary>Une opération quelconque du domaine (retrait ou dépôt).</summary>
    public sealed record Op(Operation Valeur);

    /// <summary>Un retrait qui passe les contrôles 1 à 3 (montant, plafond, fonds) ; le stock, lui, est quelconque.</summary>
    public sealed record RetraitFinance(Situation Situation, int Montant);

    /// <summary>
    /// Un retrait qui passe les quatre contrôles : il existe une combinaison exacte dans les billets disponibles.
    /// Une fois sur deux, la plus petite combinaison compte au moins 5 billets, donc plusieurs passages.
    /// </summary>
    public sealed record RetraitServable(Situation Situation, int Montant);

    /// <summary>Un retrait valide qui ferait dépasser le plafond du jour ; solde et stock quelconques.</summary>
    public sealed record RetraitHorsPlafond(Situation Situation, int Montant);

    /// <summary>Un retrait valide, sous le plafond, mais que le solde ne couvre pas (frais compris) ; stock quelconque.</summary>
    public sealed record RetraitSansFonds(Situation Situation, int Montant);

    /// <summary>Un dépôt contenant un billet de la cassette bloquée ; stock quelconque (éventuellement plein).</summary>
    public sealed record DepotBloque(Situation Situation, NoteCounts Billets);

    /// <summary>Un dépôt non vide sans billet de la cassette bloquée ; il peut ou non faire déborder une cassette.</summary>
    public sealed record DepotSansBloque(Situation Situation, NoteCounts Billets);

    /// <summary>Un dépôt qui passe les trois contrôles, avec un compte qui reste dans le domaine une fois crédité.</summary>
    public sealed record DepotAcceptable(Situation Situation, NoteCounts Billets);

    /// <summary>Un retrait de billets à 0 ou 1 par coupure, servable tel quel, qu'on peut donc redéposer.</summary>
    public sealed record RetraitRedeposable(Situation Situation, NoteCounts Billets);

    /// <summary>Un tirage qui désigne, parmi les passages d'un retrait, celui qui tombera en panne.</summary>
    public sealed record Panne(int Tirage);

    /// <summary>Une session : une situation de départ et une suite d'opérations sur le même module.</summary>
    public sealed record Session(Situation Depart, IReadOnlyList<Operation> Operations)
    {
        public override string ToString() => $"{Depart}, opérations [{string.Join(", ", Operations)}]";
    }

    private static readonly int[] Montants = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly Denomination[] Coupures = [Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    public class Domaine
    {
        private static Gen<int> Multiples(int pas, int min, int max) =>
            Gen.Choose(min / pas, max / pas).Select(k => k * pas);

        private static readonly Gen<int> Solde = Multiples(5, 0, 250);

        private static readonly Gen<int> DejaRetire = Multiples(10, 0, 200);

        private static readonly Gen<AccountState> Compte =
            from solde in Solde
            from retire in DejaRetire
            select new AccountState(solde, retire);

        private static Gen<NoteCounts> Billets(NoteCounts min, NoteCounts max) =>
            from f in Gen.Choose(min.Fifties, max.Fifties)
            from t in Gen.Choose(min.Twenties, max.Twenties)
            from c in Gen.Choose(min.Tens, max.Tens)
            select new NoteCounts(f, t, c);

        private static readonly Gen<NoteCounts> Stock = Billets(NoteCounts.None, new NoteCounts(5, 5, 5));

        private static readonly Gen<NoteCounts> UnParCoupure = Billets(NoteCounts.None, new NoteCounts(1, 1, 1));

        private static readonly Gen<Denomination?> Bloquee =
            Gen.Elements<Denomination?>(null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten);

        private static readonly Gen<Situation> SituationGen =
            from compte in Compte
            from stock in Stock
            from bloquee in Bloquee
            select new Situation(compte, stock, bloquee);

        private static readonly Gen<int> MontantValide = Multiples(10, 10, 220);

        private static int Frais(int montant) => montant > 100 ? 5 : 0;

        private static readonly Gen<Operation> Operation = Gen.OneOf(
            Gen.Elements(Montants).Select(m => (Operation)new Operation.Withdraw(m)),
            UnParCoupure.Select(n => (Operation)new Operation.Deposit(n)));

        public static Arbitrary<Situation> Situations() => Arb.From(SituationGen);

        public static Arbitrary<Montant> MontantsDuDomaine() => Arb.From(Gen.Elements(Montants).Select(m => new Montant(m)));

        public static Arbitrary<Depot> Depots() => Arb.From(UnParCoupure.Select(n => new Depot(n)));

        public static Arbitrary<Op> Operations() => Arb.From(Operation.Select(o => new Op(o)));

        public static Arbitrary<Panne> Pannes() => Arb.From(Gen.Choose(0, 1000).Select(k => new Panne(k)));

        public static Arbitrary<RetraitFinance> RetraitsFinances() => Arb.From(
            from montant in Multiples(10, 10, 200)
            from retire in Multiples(10, 0, 200 - montant)
            from solde in Multiples(5, montant + Frais(montant), 250)
            from stock in Stock
            from bloquee in Bloquee
            select new RetraitFinance(new Situation(new AccountState(solde, retire), stock, bloquee), montant));

        private static readonly Gen<RetraitServable> Servable =
            from stock in Stock
            from bloquee in Bloquee
            let dispo = Disponibles(stock, bloquee)
            from choisis in Billets(NoteCounts.None, dispo)
            where choisis.Value is >= 10 and <= 200
            let montant = choisis.Value
            from retire in Multiples(10, 0, 200 - montant)
            from solde in Multiples(5, montant + Frais(montant), 250)
            select new RetraitServable(new Situation(new AccountState(solde, retire), stock, bloquee), montant);

        public static Arbitrary<RetraitServable> RetraitsServables() => Arb.From(Gen.OneOf(
            Servable,
            Servable.Where(r => Combinaisons(r.Montant, Disponibles(r.Situation.Stock, r.Situation.Bloquee))
                .Min(c => c.Total) >= 5)));

        public static Arbitrary<RetraitHorsPlafond> RetraitsHorsPlafond() => Arb.From(
            from montant in MontantValide
            from retire in Multiples(10, Math.Max(0, 200 - montant + 10), 200)
            from solde in Solde
            from stock in Stock
            from bloquee in Bloquee
            select new RetraitHorsPlafond(new Situation(new AccountState(solde, retire), stock, bloquee), montant));

        public static Arbitrary<RetraitSansFonds> RetraitsSansFonds() => Arb.From(
            from montant in Multiples(10, 10, 200)
            from retire in Multiples(10, 0, 200 - montant)
            from solde in Multiples(5, 0, montant + Frais(montant) - 5)
            from stock in Stock
            from bloquee in Bloquee
            select new RetraitSansFonds(new Situation(new AccountState(solde, retire), stock, bloquee), montant));

        public static Arbitrary<DepotBloque> DepotsBloques() => Arb.From(
            from situation in SituationGen
            from bloquee in Gen.Elements(Coupures)
            from billets in UnParCoupure
            let avecBloque = Avec(billets, bloquee, 1)
            select new DepotBloque(situation with { Bloquee = bloquee }, avecBloque));

        public static Arbitrary<DepotSansBloque> DepotsSansBloque() => Arb.From(
            from situation in SituationGen
            from billets in UnParCoupure
            let sansBloque = situation.Bloquee is { } b ? Avec(billets, b, 0) : billets
            where sansBloque.Total > 0
            select new DepotSansBloque(situation, sansBloque));

        public static Arbitrary<DepotAcceptable> DepotsAcceptables() => Arb.From(
            from billets in UnParCoupure
            where billets.Total > 0
            from stock in Billets(NoteCounts.None, new NoteCounts(5, 5, 5) - billets)
            from bloquee in Gen.Elements(new Denomination?[] { null }.Concat(
                Coupures.Where(c => billets.Of(c) == 0).Select(c => (Denomination?)c)).ToArray())
            from solde in Multiples(5, 0, 250 - billets.Value)
            from retire in DejaRetire
            select new DepotAcceptable(new Situation(new AccountState(solde, retire), stock, bloquee), billets));

        public static Arbitrary<RetraitRedeposable> RetraitsRedeposables() => Arb.From(
            from billets in UnParCoupure
            where billets.Total > 0
            from stock in Billets(billets, new NoteCounts(5, 5, 5))
            from bloquee in Gen.Elements(new Denomination?[] { null }.Concat(
                Coupures.Where(c => billets.Of(c) == 0).Select(c => (Denomination?)c)).ToArray())
            from retire in Multiples(10, 0, 200 - billets.Value)
            from solde in Multiples(5, billets.Value, 250)
            select new RetraitRedeposable(new Situation(new AccountState(solde, retire), stock, bloquee), billets));

        public static Arbitrary<Session> Sessions() => Arb.From(
            from depart in SituationGen
            from longueur in Gen.Choose(1, 12)
            from operations in Gen.ListOf(Operation, longueur)
            select new Session(depart, operations.ToList()));
    }

    // ======================================================================================================
    //  Outillage
    // ======================================================================================================

    /// <summary>
    /// Doublure autour du vrai module : enregistre chaque appel et peut simuler une panne (rendre <c>false</c>
    /// sans rien changer) au n-ième appel à <c>Dispense</c> ou à chaque appel à <c>Load</c>.
    /// </summary>
    private sealed class Espion(MoneyDispenser reel, int? panneAuPassage = null, bool panneAuDepot = false) : IMoneyDispenser
    {
        public List<(NoteCounts Billets, bool Reussi)> Passages { get; } = [];

        public List<(NoteCounts Billets, bool Reussi)> Chargements { get; } = [];

        public NoteCounts Stock => reel.Stock;

        public Denomination? Jammed => reel.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            var reussi = Passages.Count + 1 != panneAuPassage && reel.Dispense(notes);
            Passages.Add((notes, reussi));
            return reussi;
        }

        public bool Load(NoteCounts notes)
        {
            var reussi = !panneAuDepot && reel.Load(notes);
            Chargements.Add((notes, reussi));
            return reussi;
        }

        public bool AucunBilletDeplace => Passages.All(p => !p.Reussi) && Chargements.All(c => !c.Reussi);
    }

    /// <summary>Un module dont on peut remplacer l'état entre deux appels, pour vérifier que la machine est sans mémoire.</summary>
    private sealed class ModuleInterchangeable(MoneyDispenser initial) : IMoneyDispenser
    {
        public MoneyDispenser Courant { get; set; } = initial;

        public NoteCounts Stock => Courant.Stock;

        public Denomination? Jammed => Courant.Jammed;

        public bool Dispense(NoteCounts notes) => Courant.Dispense(notes);

        public bool Load(NoteCounts notes) => Courant.Load(notes);
    }

    private static AtmResult Executer(Situation s, Operation operation, out Espion espion, int? panneAuPassage = null,
        bool panneAuDepot = false)
    {
        espion = new Espion(new MoneyDispenser(s.Stock, s.Bloquee), panneAuPassage, panneAuDepot);
        return new CashMachine(espion).Execute(s.Compte, operation);
    }

    private static AtmResult Executer(Situation s, Operation operation) => Executer(s, operation, out _);

    private static AtmResult Retirer(Situation s, int montant, out Espion espion) =>
        Executer(s, new Operation.Withdraw(montant), out espion);

    private static AtmResult Retirer(Situation s, int montant) => Executer(s, new Operation.Withdraw(montant));

    private static AtmResult Deposer(Situation s, NoteCounts billets, out Espion espion) =>
        Executer(s, new Operation.Deposit(billets), out espion);

    private static int FraisAttendus(int montant) => montant > 100 ? 5 : 0;

    private static NoteCounts Disponibles(NoteCounts stock, Denomination? bloquee) =>
        bloquee is { } b ? Avec(stock, b, 0) : stock;

    private static NoteCounts Avec(NoteCounts billets, Denomination coupure, int nombre) => coupure switch
    {
        Denomination.Fifty => billets with { Fifties = nombre },
        Denomination.Twenty => billets with { Twenties = nombre },
        _ => billets with { Tens = nombre },
    };

    private static bool Contient(NoteCounts grand, NoteCounts petit) =>
        petit.Fifties <= grand.Fifties && petit.Twenties <= grand.Twenties && petit.Tens <= grand.Tens;

    private static bool Positif(NoteCounts n) => n is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0 };

    private static bool DansLesCassettes(NoteCounts n) =>
        n is { Fifties: >= 0 and <= 5, Twenties: >= 0 and <= 5, Tens: >= 0 and <= 5 };

    private static NoteCounts Max(NoteCounts a, NoteCounts b) =>
        new(Math.Max(a.Fifties, b.Fifties), Math.Max(a.Twenties, b.Twenties), Math.Max(a.Tens, b.Tens));

    private static bool CompteDuDomaine(AccountState c) =>
        c.Balance is >= 0 and <= 250 && c.Balance % 5 == 0
        && c.WithdrawnToday is >= 0 and <= 200 && c.WithdrawnToday % 10 == 0;

    /// <summary>Toutes les façons de faire exactement <paramref name="montant"/> avec ces billets.</summary>
    private static IEnumerable<NoteCounts> Combinaisons(int montant, NoteCounts dispo)
    {
        for (var f = 0; f <= dispo.Fifties; f++)
        for (var t = 0; t <= dispo.Twenties; t++)
        for (var c = 0; c <= dispo.Tens; c++)
        {
            if (f * 50 + t * 20 + c * 10 == montant)
            {
                yield return new NoteCounts(f, t, c);
            }
        }
    }

    private static NoteCounts Somme(IEnumerable<NoteCounts> billets) => billets.Aggregate(NoteCounts.None, (a, b) => a + b);

    private static void AssertRefus(AtmResult resultat, AtmStatus statut, Situation s, Espion espion)
    {
        Assert.Equal(new AtmResult(statut, NoteCounts.None, 0, s.Compte), resultat);
        Assert.Equal(s.Stock, espion.Stock);
        Assert.True(espion.AucunBilletDeplace, "un refus ne déplace aucun billet");
    }

    /// <summary>
    /// Les règles valables pour toute opération du domaine exécutée sur le vrai module, quel que soit son statut.
    /// </summary>
    private static void VerifierInvariants(Situation s, Operation operation, AtmResult r, Espion espion)
    {
        var apres = espion.Stock;

        // Le vrai module ne refuse jamais ce que la machine a contrôlé : jamais de panne.
        Assert.NotEqual(AtmStatus.DispenserFault, r.Status);
        Assert.True(espion.Passages.All(p => p.Reussi) && espion.Chargements.All(c => c.Reussi),
            "la machine ne demande au vrai module que ce qu'il peut faire");

        // La cassette bloquée reste bloquée, n'est jamais sollicitée et son contenu ne bouge pas.
        Assert.Equal(s.Bloquee, espion.Jammed);
        if (s.Bloquee is { } bloquee)
        {
            Assert.Equal(s.Stock.Of(bloquee), apres.Of(bloquee));
            Assert.All(espion.Passages.Concat(espion.Chargements), p => Assert.Equal(0, p.Billets.Of(bloquee)));
        }

        // Le stock reste dans les cassettes ; le plafond du jour et le solde restent respectés.
        Assert.True(DansLesCassettes(apres), $"stock hors des cassettes : {apres}");
        Assert.InRange(r.Account.Balance, 0, int.MaxValue);
        Assert.InRange(r.Account.WithdrawnToday, s.Compte.WithdrawnToday, 200);

        // Conservation de l'argent : ce que le compte perd (hors frais) est ce que le module perd, et inversement.
        Assert.Equal(apres.Value - s.Stock.Value, r.Account.Balance - s.Compte.Balance + r.Fee);

        // Les frais : 5 € sur un retrait réussi de plus de 100 €, rien sinon.
        Assert.Equal(r.Status == AtmStatus.Ok && operation is Operation.Withdraw { Amount: > 100 } ? 5 : 0, r.Fee);

        switch (operation)
        {
            case Operation.Withdraw { Amount: var montant }:
                Assert.Contains(r.Status, new[]
                {
                    AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.DailyLimitExceeded, AtmStatus.InsufficientFunds,
                    AtmStatus.CannotDispense,
                });
                Assert.Empty(espion.Chargements);
                Assert.Equal(s.Stock - r.Notes, apres); // les billets rendus sont exactement ceux qui ont quitté le module
                if (r.Status == AtmStatus.Ok)
                {
                    Assert.Equal(montant, r.Notes.Value);
                    Assert.Equal(new AccountState(s.Compte.Balance - montant - FraisAttendus(montant),
                        s.Compte.WithdrawnToday + montant), r.Account);
                }

                break;

            case Operation.Deposit { Notes: var billets }:
                Assert.Contains(r.Status, new[]
                {
                    AtmStatus.Ok, AtmStatus.InvalidAmount, AtmStatus.CassetteJammed, AtmStatus.CassetteFull,
                });
                Assert.Empty(espion.Passages);
                Assert.Equal(NoteCounts.None, r.Notes);
                if (r.Status == AtmStatus.Ok)
                {
                    Assert.Equal(s.Stock + billets, apres);
                    Assert.Equal(s.Compte with { Balance = s.Compte.Balance + billets.Value }, r.Account);
                }

                break;
        }

        if (r.Status != AtmStatus.Ok)
        {
            AssertRefus(r, r.Status, s, espion);
        }
    }

    // ======================================================================================================
    //  Toute opération
    // ======================================================================================================

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void ToutOperation_RespecteLesInvariants(Situation s, Op op)
    {
        var resultat = Executer(s, op.Valeur, out var espion);

        VerifierInvariants(s, op.Valeur, resultat, espion);
    }

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void ToutOperation_EstDeterministe(Situation s, Op op)
    {
        var premier = Executer(s, op.Valeur, out var espion1);
        var second = Executer(s, op.Valeur, out var espion2);

        Assert.Equal(premier, second);
        Assert.Equal(espion1.Stock, espion2.Stock);
    }

    /// <summary>
    /// La machine est sans mémoire : après une première opération quelconque, une seconde opération sur un autre état
    /// du module donne exactement ce que donnerait une machine neuve.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void LaMachine_EstSansMemoire(Situation s1, Op op1, Situation s2, Op op2)
    {
        var module = new ModuleInterchangeable(new MoneyDispenser(s1.Stock, s1.Bloquee));
        var machine = new CashMachine(module);
        machine.Execute(s1.Compte, op1.Valeur);

        module.Courant = new MoneyDispenser(s2.Stock, s2.Bloquee);
        var obtenu = machine.Execute(s2.Compte, op2.Valeur);

        var temoin = new MoneyDispenser(s2.Stock, s2.Bloquee);
        var attendu = new CashMachine(temoin).Execute(s2.Compte, op2.Valeur);

        Assert.Equal(attendu, obtenu);
        Assert.Equal(temoin.Stock, module.Stock);
    }

    /// <summary>
    /// Une suite d'opérations sur le même module (tant que le compte reste dans le domaine) respecte à chaque pas les
    /// invariants, et les billets ne font que circuler entre les comptes et le module.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void UneSession_RespecteLesInvariantsAChaquePas(Session session)
    {
        var reel = new MoneyDispenser(session.Depart.Stock, session.Depart.Bloquee);
        var compte = session.Depart.Compte;
        var fraisCumules = 0;

        foreach (var operation in session.Operations)
        {
            var avant = new Situation(compte, reel.Stock, reel.Jammed);
            var espion = new Espion(reel);
            var resultat = new CashMachine(espion).Execute(compte, operation);

            VerifierInvariants(avant, operation, resultat, espion);

            fraisCumules += resultat.Fee;
            compte = resultat.Account;
            if (!CompteDuDomaine(compte))
            {
                break;
            }
        }

        Assert.Equal(reel.Stock.Value - session.Depart.Stock.Value,
            compte.Balance - session.Depart.Compte.Balance + fraisCumules);
    }

    // ======================================================================================================
    //  Retrait : l'ordre des contrôles
    // ======================================================================================================

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_MontantInvalide_EstRefuse_QuelsQueSoientCompteEtStock(Situation s)
    {
        foreach (var montant in new[] { -10, 0, 5 })
        {
            var resultat = Retirer(s, montant, out var espion);

            AssertRefus(resultat, AtmStatus.InvalidAmount, s, espion);
        }
    }

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_AuDelaDuPlafond_EstRefuse_QuelsQueSoientSoldeEtStock(RetraitHorsPlafond cas)
    {
        var resultat = Retirer(cas.Situation, cas.Montant, out var espion);

        AssertRefus(resultat, AtmStatus.DailyLimitExceeded, cas.Situation, espion);
    }

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_NonCouvertParLeSolde_EstRefuse_QuelQueSoitLeStock(RetraitSansFonds cas)
    {
        var resultat = Retirer(cas.Situation, cas.Montant, out var espion);

        AssertRefus(resultat, AtmStatus.InsufficientFunds, cas.Situation, espion);
    }

    /// <summary>Une fois les trois premiers contrôles passés, le retrait réussit si et seulement si une combinaison existe.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_Finance_ReussitSsiUneCombinaisonExiste(RetraitFinance cas)
    {
        var s = cas.Situation;
        var possible = Combinaisons(cas.Montant, Disponibles(s.Stock, s.Bloquee)).Any();

        var resultat = Retirer(s, cas.Montant, out var espion);

        if (possible)
        {
            Assert.Equal(AtmStatus.Ok, resultat.Status);
        }
        else
        {
            AssertRefus(resultat, AtmStatus.CannotDispense, s, espion);
        }
    }

    // ======================================================================================================
    //  Retrait réussi
    // ======================================================================================================

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_Servable_RendLeMontantExact_EtDebiteMontantPlusFrais(RetraitServable cas)
    {
        var s = cas.Situation;
        var frais = FraisAttendus(cas.Montant);

        var resultat = Retirer(s, cas.Montant, out var espion);

        Assert.Equal(AtmStatus.Ok, resultat.Status);
        Assert.Equal(cas.Montant, resultat.Notes.Value);
        Assert.Equal(frais, resultat.Fee);
        Assert.Equal(s.Compte.Balance - cas.Montant - frais, resultat.Account.Balance);
        Assert.Equal(s.Compte.WithdrawnToday + cas.Montant, resultat.Account.WithdrawnToday); // sans les frais
        Assert.Equal(s.Stock - resultat.Notes, espion.Stock);
    }

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_Servable_NUtiliseQueDesBilletsDisponibles(RetraitServable cas)
    {
        var s = cas.Situation;

        var resultat = Retirer(s, cas.Montant);

        Assert.True(Positif(resultat.Notes), $"billets négatifs : {resultat.Notes}");
        Assert.True(Contient(Disponibles(s.Stock, s.Bloquee), resultat.Notes),
            $"{resultat.Notes} n'est pas pris dans les billets disponibles {Disponibles(s.Stock, s.Bloquee)}");
    }

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_Servable_AucuneCombinaisonNeCompteMoinsDeBillets(RetraitServable cas)
    {
        var s = cas.Situation;

        var resultat = Retirer(s, cas.Montant);

        var meilleure = Combinaisons(cas.Montant, Disponibles(s.Stock, s.Bloquee)).Min(c => c.Total);
        Assert.Equal(meilleure, resultat.Notes.Total);
    }

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_Servable_SortEnPassagesDeUnAQuatreBillets_QuiFontLaCombinaison(RetraitServable cas)
    {
        var s = cas.Situation;

        var resultat = Retirer(s, cas.Montant, out var espion);

        Assert.NotEmpty(espion.Passages);
        Assert.All(espion.Passages, p =>
        {
            Assert.True(Positif(p.Billets), $"passage négatif : {p.Billets}");
            Assert.InRange(p.Billets.Total, 1, 4);
            Assert.True(p.Reussi);
        });
        Assert.Equal(resultat.Notes, Somme(espion.Passages.Select(p => p.Billets)));
    }

    // ======================================================================================================
    //  Retrait : relations entre situations voisines
    // ======================================================================================================

    /// <summary>Bloquer une cassette revient, pour un retrait, à la vider.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_CassetteBloquee_EquivautACassetteVide(RetraitFinance cas)
    {
        var s = cas.Situation;
        var videe = s with { Stock = Disponibles(s.Stock, s.Bloquee), Bloquee = null };

        var bloque = Retirer(s, cas.Montant, out var espionBloque);
        var vide = Retirer(videe, cas.Montant, out var espionVide);

        Assert.Equal(vide, bloque);
        Assert.Equal(espionVide.Stock, Disponibles(espionBloque.Stock, s.Bloquee));
    }

    /// <summary>Plus de solde ne change rien, sauf à lever un refus pour fonds insuffisants.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_PlusDeSolde_NeChangeRien_SaufLesFondsInsuffisants(Situation s, Montant m, Situation autre)
    {
        var ecart = Math.Max(0, autre.Compte.Balance - s.Compte.Balance);
        var riche = s with { Compte = s.Compte with { Balance = s.Compte.Balance + ecart } };

        var avant = Retirer(s, m.Valeur);
        var apres = Retirer(riche, m.Valeur);

        if (avant.Status != AtmStatus.InsufficientFunds)
        {
            var decale = avant.Account with { Balance = avant.Account.Balance + ecart };
            Assert.Equal(avant with { Account = decale }, apres);
        }
    }

    /// <summary>Moins de retraits déjà faits ne change rien, sauf à lever un refus pour plafond dépassé.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_MoinsDejaRetire_NeChangeRien_SaufLePlafond(Situation s, Montant m, Situation autre)
    {
        var ecart = Math.Max(0, s.Compte.WithdrawnToday - autre.Compte.WithdrawnToday);
        var frais = s with { Compte = s.Compte with { WithdrawnToday = s.Compte.WithdrawnToday - ecart } };

        var avant = Retirer(s, m.Valeur);
        var apres = Retirer(frais, m.Valeur);

        if (avant.Status != AtmStatus.DailyLimitExceeded)
        {
            var decale = avant.Account with { WithdrawnToday = avant.Account.WithdrawnToday - ecart };
            Assert.Equal(avant with { Account = decale }, apres);
        }
    }

    /// <summary>
    /// Plus de billets dans le module (même cassette bloquée) ne fait jamais échouer un retrait qui réussissait, ni ne
    /// demande plus de billets. (Que les contrôles 1 à 3 ne dépendent pas du stock est vérifié plus haut.)
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_PlusDeBillets_NeNuitJamais(RetraitFinance cas, Situation autre)
    {
        var s = cas.Situation;
        var garni = s with { Stock = Max(s.Stock, autre.Stock) };

        var avant = Retirer(s, cas.Montant);
        var apres = Retirer(garni, cas.Montant);

        if (avant.Status == AtmStatus.Ok)
        {
            Assert.Equal(AtmStatus.Ok, apres.Status);
            Assert.Equal(avant.Fee, apres.Fee);
            Assert.Equal(avant.Account, apres.Account);
            Assert.InRange(apres.Notes.Total, 0, avant.Notes.Total);
        }
        else
        {
            Assert.Equal(AtmStatus.CannotDispense, avant.Status);
            Assert.Contains(apres.Status, new[] { AtmStatus.Ok, AtmStatus.CannotDispense });
        }
    }

    // ======================================================================================================
    //  Retrait : pannes du module
    // ======================================================================================================

    /// <summary>
    /// Si le module refuse un passage, la machine s'arrête aussitôt : statut <c>DispenserFault</c>, aucun frais, compte
    /// identique, et seuls les billets des passages réussis sont rendus. Le passage en panne est tiré parmi ceux que
    /// la machine effectue sans panne (elle est déterministe), du premier au dernier.
    /// </summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Retrait_PanneDuModule_ArreteAussitot_EtRendLesBilletsDejaSortis(RetraitServable cas, Panne panne)
    {
        var s = cas.Situation;
        Retirer(s, cas.Montant, out var sansPanne);
        var auPassage = 1 + panne.Tirage % sansPanne.Passages.Count;

        var resultat = Executer(s, new Operation.Withdraw(cas.Montant), out var espion, panneAuPassage: auPassage);

        var sortis = Somme(espion.Passages.Where(p => p.Reussi).Select(p => p.Billets));
        Assert.Equal(auPassage, espion.Passages.Count); // plus aucun passage après la panne
        Assert.Equal(auPassage - 1, espion.Passages.Count(p => p.Reussi));
        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, sortis, 0, s.Compte), resultat);
        Assert.Equal(s.Stock - sortis, espion.Stock);
        Assert.Empty(espion.Chargements);
    }

    // ======================================================================================================
    //  Dépôt
    // ======================================================================================================

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Depot_Vide_EstRefuse_QuelleQueSoitLaSituation(Situation s)
    {
        var resultat = Deposer(s, NoteCounts.None, out var espion);

        AssertRefus(resultat, AtmStatus.InvalidAmount, s, espion);
    }

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Depot_AvecUnBilletDeLaCassetteBloquee_EstRefuse_MemeSiUneCassetteDeborderait(DepotBloque cas)
    {
        var resultat = Deposer(cas.Situation, cas.Billets, out var espion);

        AssertRefus(resultat, AtmStatus.CassetteJammed, cas.Situation, espion);
    }

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Depot_SansBilletBloque_EstRefuseSsiUneCassetteDeborde(DepotSansBloque cas)
    {
        var s = cas.Situation;
        var deborde = !DansLesCassettes(s.Stock + cas.Billets);

        var resultat = Deposer(s, cas.Billets, out var espion);

        if (deborde)
        {
            AssertRefus(resultat, AtmStatus.CassetteFull, s, espion);
        }
        else
        {
            Assert.Equal(AtmStatus.Ok, resultat.Status);
        }
    }

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Depot_Accepte_CrediteSaValeur_EtRangeLesBillets(DepotAcceptable cas)
    {
        var s = cas.Situation;

        var resultat = Deposer(s, cas.Billets, out var espion);

        Assert.Equal(new AtmResult(AtmStatus.Ok, NoteCounts.None, 0,
            new AccountState(s.Compte.Balance + cas.Billets.Value, s.Compte.WithdrawnToday)), resultat);
        Assert.Equal(s.Stock + cas.Billets, espion.Stock);
        Assert.Empty(espion.Passages);
    }

    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void Depot_PanneDuModule_NeChangeRien(DepotAcceptable cas)
    {
        var s = cas.Situation;

        var resultat = Executer(s, new Operation.Deposit(cas.Billets), out var espion, panneAuDepot: true);

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, s.Compte), resultat);
        Assert.Equal(s.Stock, espion.Stock);
        Assert.Single(espion.Chargements); // on s'arrête aussitôt
        Assert.Empty(espion.Passages);
    }

    // ======================================================================================================
    //  Allers-retours
    // ======================================================================================================

    /// <summary>Redéposer les billets qu'on vient de retirer est accepté et remet le module dans son état initial.</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void RetirerPuisRedeposerLesMemesBillets_RestaureLeModule(RetraitRedeposable cas)
    {
        var s = cas.Situation;
        var module = new MoneyDispenser(s.Stock, s.Bloquee);
        var machine = new CashMachine(module);

        var retrait = machine.Execute(s.Compte, new Operation.Withdraw(cas.Billets.Value));
        Assert.Equal(AtmStatus.Ok, retrait.Status);
        Assert.Equal(0, retrait.Fee);
        Assert.True(Contient(new NoteCounts(1, 1, 1), retrait.Notes), $"combinaison non minimale : {retrait.Notes}");

        var depot = machine.Execute(retrait.Account, new Operation.Deposit(retrait.Notes));

        Assert.Equal(AtmStatus.Ok, depot.Status);
        Assert.Equal(s.Stock, module.Stock);
        Assert.Equal(new AccountState(s.Compte.Balance, s.Compte.WithdrawnToday + cas.Billets.Value), depot.Account);
    }

    /// <summary>Après un dépôt accepté, retirer sa valeur est toujours possible (si le plafond le permet).</summary>
    [Property(Replay = Graine, MaxTest = Cas, Arbitrary = new[] { typeof(Domaine) })]
    public void DeposerPuisRetirerSaValeur_Reussit(DepotAcceptable cas)
    {
        var s = cas.Situation;
        var valeur = cas.Billets.Value;
        var compte = s.Compte with { WithdrawnToday = Math.Min(s.Compte.WithdrawnToday, 200 - valeur) };
        var module = new MoneyDispenser(s.Stock, s.Bloquee);
        var machine = new CashMachine(module);

        var depot = machine.Execute(compte, new Operation.Deposit(cas.Billets));
        Assert.Equal(AtmStatus.Ok, depot.Status);

        var retrait = machine.Execute(depot.Account, new Operation.Withdraw(valeur));

        Assert.Equal(AtmStatus.Ok, retrait.Status);
        Assert.Equal(valeur, retrait.Notes.Value);
        Assert.Equal(s.Stock.Value, module.Stock.Value);
        Assert.Equal(compte with { WithdrawnToday = compte.WithdrawnToday + valeur }, retrait.Account);
    }
}
