using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Atm.Tests;

/// <summary>
/// Tests de propriétés : des règles de la spécification qui doivent rester vraies pour toutes les entrées du domaine,
/// vérifiées par FsCheck sur 200 cas tirés au hasard, avec une graine fixe pour que chaque exécution rejoue les mêmes cas.
/// <para>
/// Les générateurs ne produisent que des valeurs du domaine de la spec (le comportement est libre en dehors).
/// Certains générateurs sont ciblés : ils construisent des cas où un contrôle précis échoue (ou où tous passent),
/// pour que chaque règle soit éprouvée sur de nombreux cas et pas seulement sur ceux que le hasard uniforme rend fréquents.
/// </para>
/// <para>
/// Le module est toujours le vrai <see cref="MoneyDispenser"/>, enveloppé dans un <see cref="Espion"/> qui se contente de
/// noter les appels ; seules les propriétés de panne demandent à l'espion de faire échouer un appel.
/// </para>
/// <para>
/// Graine : la consigne demandait <c>Replay = "42,0"</c>, mais FsCheck 3.3.0 refuse un gamma pair
/// (« Gamma must be odd, given: 0 ») et l'hôte de test s'arrête. On garde la graine 42 avec le plus petit gamma valide, 1.
/// </para>
/// </summary>
public class Proprietes
{
    // ---- Modèle du domaine ---------------------------------------------------------------------------

    /// <summary>Un compte et un module de billets, tels qu'ils sont avant l'opération.</summary>
    public sealed record Situation(AccountState Compte, NoteCounts Stock, Denomination? Bloquee);

    public sealed record Retrait(Situation Avant, int Montant)
    {
        public Operation Operation => new Operation.Withdraw(Montant);
    }

    public sealed record Depot(Situation Avant, NoteCounts Billets)
    {
        public Operation Operation => new Operation.Deposit(Billets);
    }

    public sealed record Cas(Situation Avant, Operation Operation);

    private static NoteCounts N(int fifties, int twenties, int tens) => new(fifties, twenties, tens);

    private static readonly Denomination[] Coupures = [Denomination.Fifty, Denomination.Twenty, Denomination.Ten];

    private static int Frais(int montant) => montant > 100 ? 5 : 0;

    /// <summary>Les billets disponibles : le stock, sans la cassette bloquée, qui compte pour zéro.</summary>
    private static NoteCounts Disponibles(NoteCounts stock, Denomination? bloquee) => N(
        bloquee == Denomination.Fifty ? 0 : stock.Fifties,
        bloquee == Denomination.Twenty ? 0 : stock.Twenties,
        bloquee == Denomination.Ten ? 0 : stock.Tens);

    /// <summary>Toutes les combinaisons de billets disponibles qui font exactement <paramref name="montant"/>.</summary>
    private static List<NoteCounts> Combinaisons(int montant, NoteCounts dispo)
    {
        var combinaisons = new List<NoteCounts>();
        for (var f = 0; f <= dispo.Fifties; f++)
        for (var t = 0; t <= dispo.Twenties; t++)
        for (var c = 0; c <= dispo.Tens; c++)
        {
            if (N(f, t, c).Value == montant)
            {
                combinaisons.Add(N(f, t, c));
            }
        }

        return combinaisons;
    }

    /// <summary>La combinaison attendue pour un retrait réalisable : celle qui compte le moins de billets.</summary>
    private static NoteCounts PlusPetite(Retrait r) =>
        Combinaisons(r.Montant, Disponibles(r.Avant.Stock, r.Avant.Bloquee)).MinBy(c => c.Total);

    private static bool AuPlus(NoteCounts a, NoteCounts b) =>
        a.Fifties <= b.Fifties && a.Twenties <= b.Twenties && a.Tens <= b.Tens;

    private static bool Positifs(NoteCounts n) => n is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0 };

    private static bool Deborde(NoteCounts stock) => stock is { Fifties: > 5 } or { Twenties: > 5 } or { Tens: > 5 };

    private static NoteCounts Avec(NoteCounts n, Denomination d, int nombre) => d switch
    {
        Denomination.Fifty => n with { Fifties = nombre },
        Denomination.Twenty => n with { Twenties = nombre },
        _ => n with { Tens = nombre },
    };

    private static NoteCounts Somme(IEnumerable<NoteCounts> billets) => billets.Aggregate(NoteCounts.None, (a, b) => a + b);

    // ---- Espion autour du vrai module ----------------------------------------------------------------

    /// <summary>
    /// Le vrai module, dont on note chaque appel. Par défaut il ne change rien au comportement ; on peut lui demander
    /// de rendre <c>false</c> à un passage donné (sans sortir de billets) ou au dépôt, pour simuler une panne.
    /// </summary>
    private sealed class Espion(NoteCounts stock, Denomination? bloquee, int panneAuPassage = -1, bool panneAuDepot = false)
        : IMoneyDispenser
    {
        private readonly MoneyDispenser _vrai = new(stock, bloquee);

        public List<(NoteCounts Billets, bool Reponse)> Sorties { get; } = [];

        public List<(NoteCounts Billets, bool Reponse)> Rangements { get; } = [];

        public NoteCounts Stock => _vrai.Stock;

        public Denomination? Jammed => _vrai.Jammed;

        public bool Dispense(NoteCounts notes)
        {
            var reponse = Sorties.Count != panneAuPassage && _vrai.Dispense(notes);
            Sorties.Add((notes, reponse));
            return reponse;
        }

        public bool Load(NoteCounts notes)
        {
            var reponse = !panneAuDepot && _vrai.Load(notes);
            Rangements.Add((notes, reponse));
            return reponse;
        }

        public int Appels => Sorties.Count + Rangements.Count;
    }

    private static (AtmResult Resultat, Espion Module) Executer(Situation s, Operation operation,
        int panneAuPassage = -1, bool panneAuDepot = false)
    {
        var module = new Espion(s.Stock, s.Bloquee, panneAuPassage, panneAuDepot);
        var resultat = new CashMachine(module).Execute(s.Compte, operation);
        return (resultat, module);
    }

    private static void AssertRienNeChange(Situation avant, AtmResult resultat, Espion module)
    {
        Assert.Equal(NoteCounts.None, resultat.Notes);
        Assert.Equal(0, resultat.Fee);
        Assert.Equal(avant.Compte, resultat.Account);
        Assert.Equal(avant.Stock, module.Stock);
    }

    // ---- Générateurs du domaine ----------------------------------------------------------------------

    private static Gen<int> MultiplesEntre(int pas, int min, int max) =>
        Gen.Choose((min + pas - 1) / pas, max / pas).Select(k => k * pas);

    private static readonly Gen<int> Soldes = MultiplesEntre(5, 0, 250);

    private static readonly Gen<int> DejaRetires = MultiplesEntre(10, 0, 200);

    private static readonly Gen<AccountState> Comptes =
        from solde in Soldes
        from deja in DejaRetires
        select new AccountState(solde, deja);

    private static Gen<NoteCounts> AuPlusDe(NoteCounts max) =>
        from f in Gen.Choose(0, max.Fifties)
        from t in Gen.Choose(0, max.Twenties)
        from c in Gen.Choose(0, max.Tens)
        select N(f, t, c);

    private static readonly Gen<NoteCounts> Stocks = AuPlusDe(N(5, 5, 5));

    private static readonly Gen<Denomination?> Bloquees =
        Gen.Elements<Denomination?>(null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten);

    private static readonly Gen<Situation> Situations =
        from compte in Comptes
        from stock in Stocks
        from bloquee in Bloquees
        select new Situation(compte, stock, bloquee);

    private static readonly int[] TousLesMontants = [-10, 0, 5, .. Enumerable.Range(1, 22).Select(i => i * 10)];

    private static readonly Gen<int> Montants = Gen.Elements(TousLesMontants);

    private static readonly Gen<int> MontantsInvalides = Gen.Elements(-10, 0, 5);

    /// <summary>Un dépôt du domaine : 0 ou 1 billet par coupure.</summary>
    private static readonly Gen<NoteCounts> Billets = AuPlusDe(N(1, 1, 1));

    private static readonly Gen<NoteCounts> BilletsNonVides = Billets.Where(b => b.Total > 0);

    private static readonly Gen<Retrait> Retraits =
        from s in Situations
        from montant in Montants
        select new Retrait(s, montant);

    private static readonly Gen<Depot> Depots =
        from s in Situations
        from billets in Billets
        select new Depot(s, billets);

    private static readonly Gen<Operation> Operations = Gen.OneOf(
        Montants.Select(m => (Operation)new Operation.Withdraw(m)),
        Billets.Select(b => (Operation)new Operation.Deposit(b)));

    private static readonly Gen<Cas> TousLesCas =
        from s in Situations
        from operation in Operations
        select new Cas(s, operation);

    /// <summary>Un retrait qui passe les trois premiers contrôles : montant valide, plafond et solde respectés.</summary>
    private static readonly Gen<Retrait> RetraitsAutorises =
        from montant in MultiplesEntre(10, 10, 200)
        from deja in MultiplesEntre(10, 0, 200 - montant)
        from solde in MultiplesEntre(5, montant + Frais(montant), 250)
        from stock in Stocks
        from bloquee in Bloquees
        select new Retrait(new Situation(new AccountState(solde, deja), stock, bloquee), montant);

    /// <summary>
    /// Un retrait qui passe tous les contrôles. Construit à partir d'un sous-ensemble des billets disponibles,
    /// pour qu'une combinaison existe à coup sûr (pas forcément celle que la machine doit choisir).
    /// </summary>
    private static readonly Gen<Retrait> RetraitsRealisables =
        from stock in Stocks
        from bloquee in Bloquees
        from choisis in AuPlusDe(Disponibles(stock, bloquee))
        where choisis.Value is >= 10 and <= 200
        let montant = choisis.Value
        from deja in MultiplesEntre(10, 0, 200 - montant)
        from solde in MultiplesEntre(5, montant + Frais(montant), 250)
        select new Retrait(new Situation(new AccountState(solde, deja), stock, bloquee), montant);

    private static readonly Gen<Depot> DepotsAcceptables =
        Depots.Where(d => d.Billets.Total > 0
                          && (d.Avant.Bloquee is not { } b || d.Billets.Of(b) == 0)
                          && !Deborde(d.Avant.Stock + d.Billets));

    // ---- Toutes opérations confondues ----------------------------------------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Dans_le_domaine_le_vrai_module_ne_tombe_jamais_en_panne() =>
        Prop.ForAll(TousLesCas.ToArbitrary(), cas =>
        {
            var (resultat, module) = Executer(cas.Avant, cas.Operation);

            Assert.NotEqual(AtmStatus.DispenserFault, resultat.Status);
            Assert.All(module.Sorties, s => Assert.True(s.Reponse));
            Assert.All(module.Rangements, r => Assert.True(r.Reponse));
        });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property L_argent_ne_se_cree_ni_ne_se_perd() =>
        Prop.ForAll(TousLesCas.ToArbitrary(), cas =>
        {
            var (resultat, module) = Executer(cas.Avant, cas.Operation);
            var accepte = resultat.Status == AtmStatus.Ok;
            var deposes = cas.Operation is Operation.Deposit d && accepte ? d.Notes : NoteCounts.None;

            // Côté client : ce que le solde perd se retrouve en billets dans sa main ou en frais.
            var variationSolde = resultat.Account.Balance - cas.Avant.Compte.Balance;
            Assert.Equal(deposes.Value, variationSolde + resultat.Notes.Value + resultat.Fee);

            // Côté machine : chaque billet sorti a quitté le stock, chaque billet déposé l'a rejoint.
            Assert.Equal(cas.Avant.Stock + deposes, module.Stock + resultat.Notes);
        });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Le_resultat_reste_dans_les_bornes_du_domaine() =>
        Prop.ForAll(TousLesCas.ToArbitrary(), cas =>
        {
            var (resultat, module) = Executer(cas.Avant, cas.Operation);
            var apres = resultat.Account;

            Assert.Contains(resultat.Fee, new[] { 0, 5 });
            Assert.True(Positifs(resultat.Notes));
            Assert.True(apres.Balance >= 0 && apres.Balance % 5 == 0, $"solde après : {apres.Balance}");
            Assert.True(apres.WithdrawnToday is >= 0 and <= 200 && apres.WithdrawnToday % 10 == 0,
                $"déjà retiré après : {apres.WithdrawnToday}");
            Assert.True(apres.WithdrawnToday >= cas.Avant.Compte.WithdrawnToday);
            Assert.True(Positifs(module.Stock) && !Deborde(module.Stock), $"stock après : {module.Stock}");
        });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property La_cassette_bloquee_ne_bouge_jamais() =>
        Prop.ForAll(TousLesCas.Where(c => c.Avant.Bloquee is not null).ToArbitrary(), cas =>
        {
            var (resultat, module) = Executer(cas.Avant, cas.Operation);
            var bloquee = cas.Avant.Bloquee!.Value;

            Assert.Equal(cas.Avant.Stock.Of(bloquee), module.Stock.Of(bloquee));
            Assert.Equal(0, resultat.Notes.Of(bloquee));
            Assert.Equal(bloquee, module.Jammed);
        });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Un_refus_ne_change_rien_et_ne_sollicite_pas_le_module() =>
        Prop.ForAll(TousLesCas.ToArbitrary(), cas =>
        {
            var (resultat, module) = Executer(cas.Avant, cas.Operation);
            if (resultat.Status == AtmStatus.Ok)
            {
                return;
            }

            AssertRienNeChange(cas.Avant, resultat, module);
            Assert.Equal(0, module.Appels);
        });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property La_machine_est_sans_memoire_entre_deux_appels() =>
        Prop.ForAll(
            (from s in Situations
             from appels in Gen.ArrayOf(from compte in Comptes from operation in Operations select (compte, operation))
                 .Where(a => a.Length > 0)
             select (s, appels)).ToArbitrary(),
            entree =>
            {
                // Une même machine, un même module, une suite d'appels sur des comptes quelconques…
                var (s, appels) = entree;
                var module = new MoneyDispenser(s.Stock, s.Bloquee);
                var machine = new CashMachine(module);
                foreach (var (compte, operation) in appels)
                {
                    var stockAvant = module.Stock;
                    var resultat = machine.Execute(compte, operation);

                    // … rend à chaque appel ce que rendrait une machine neuve devant le même compte et le même module.
                    var (attendu, moduleNeuf) = Executer(new Situation(compte, stockAvant, s.Bloquee), operation);
                    Assert.Equal(attendu, resultat);
                    Assert.Equal(moduleNeuf.Stock, module.Stock);
                }
            });

    // ---- Retrait : ordre des contrôles ---------------------------------------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Un_montant_invalide_est_refuse_quel_que_soit_le_reste() =>
        Prop.ForAll(
            (from s in Situations from montant in MontantsInvalides select new Retrait(s, montant)).ToArbitrary(),
            retrait => Assert.Equal(AtmStatus.InvalidAmount, Executer(retrait.Avant, retrait.Operation).Resultat.Status));

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Le_plafond_du_jour_est_controle_avant_le_solde_et_les_billets() =>
        Prop.ForAll(
            (from montant in MultiplesEntre(10, 10, 220)
             from deja in MultiplesEntre(10, Math.Max(0, 210 - montant), 200)
             from solde in Soldes
             from stock in Stocks
             from bloquee in Bloquees
             select new Retrait(new Situation(new AccountState(solde, deja), stock, bloquee), montant)).ToArbitrary(),
            retrait => Assert.Equal(AtmStatus.DailyLimitExceeded, Executer(retrait.Avant, retrait.Operation).Resultat.Status));

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Le_solde_est_controle_avant_les_billets_et_compte_les_frais() =>
        Prop.ForAll(
            (from montant in MultiplesEntre(10, 10, 200)
             from deja in MultiplesEntre(10, 0, 200 - montant)
             from solde in MultiplesEntre(5, 0, montant + Frais(montant) - 5)
             from stock in Stocks
             from bloquee in Bloquees
             select new Retrait(new Situation(new AccountState(solde, deja), stock, bloquee), montant)).ToArbitrary(),
            retrait => Assert.Equal(AtmStatus.InsufficientFunds, Executer(retrait.Avant, retrait.Operation).Resultat.Status));

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Un_retrait_autorise_est_servi_si_et_seulement_si_une_combinaison_existe() =>
        Prop.ForAll(RetraitsAutorises.ToArbitrary(), retrait =>
        {
            var s = retrait.Avant;
            var existe = Combinaisons(retrait.Montant, Disponibles(s.Stock, s.Bloquee)).Count > 0;

            var statut = Executer(s, retrait.Operation).Resultat.Status;

            Assert.Equal(existe ? AtmStatus.Ok : AtmStatus.CannotDispense, statut);
        });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Sans_combinaison_exacte_le_retrait_est_refuse() =>
        Prop.ForAll(
            RetraitsAutorises
                .Where(r => Combinaisons(r.Montant, Disponibles(r.Avant.Stock, r.Avant.Bloquee)).Count == 0)
                .ToArbitrary(),
            retrait => Assert.Equal(AtmStatus.CannotDispense, Executer(retrait.Avant, retrait.Operation).Resultat.Status));

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Les_trois_premiers_controles_ne_dependent_pas_du_module() =>
        Prop.ForAll(
            (from retrait in Retraits from stock in Stocks from bloquee in Bloquees select (retrait, stock, bloquee))
            .ToArbitrary(),
            entree =>
            {
                var (retrait, autreStock, autreBloquee) = entree;
                var statut = Executer(retrait.Avant, retrait.Operation).Resultat.Status;
                if (statut is not (AtmStatus.InvalidAmount or AtmStatus.DailyLimitExceeded or AtmStatus.InsufficientFunds))
                {
                    return;
                }

                var autreModule = retrait.Avant with { Stock = autreStock, Bloquee = autreBloquee };
                Assert.Equal(statut, Executer(autreModule, retrait.Operation).Resultat.Status);
            });

    // ---- Retrait accepté -----------------------------------------------------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Un_retrait_realisable_est_accepte_et_debite_montant_et_frais() =>
        Prop.ForAll(RetraitsRealisables.ToArbitrary(), retrait =>
        {
            var avant = retrait.Avant;
            var (resultat, module) = Executer(avant, retrait.Operation);

            Assert.Equal(AtmStatus.Ok, resultat.Status);
            Assert.Equal(retrait.Montant, resultat.Notes.Value);
            Assert.Equal(Frais(retrait.Montant), resultat.Fee);
            Assert.Equal(
                new AccountState(avant.Compte.Balance - retrait.Montant - resultat.Fee,
                    avant.Compte.WithdrawnToday + retrait.Montant),
                resultat.Account);
            Assert.Equal(avant.Stock - resultat.Notes, module.Stock);
            Assert.Empty(module.Rangements);
        });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property La_combinaison_rendue_est_l_unique_qui_compte_le_moins_de_billets() =>
        Prop.ForAll(RetraitsRealisables.ToArbitrary(), retrait =>
        {
            var s = retrait.Avant;
            var rendus = Executer(s, retrait.Operation).Resultat.Notes;

            var dispo = Disponibles(s.Stock, s.Bloquee);
            Assert.True(Positifs(rendus) && AuPlus(rendus, dispo), $"{rendus} hors des billets disponibles {dispo}");
            Assert.All(
                Combinaisons(retrait.Montant, dispo).Where(c => c != rendus),
                autre => Assert.True(autre.Total > rendus.Total, $"{autre} fait aussi bien que {rendus}"));
        });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Les_billets_sortent_en_aussi_peu_de_passages_que_possible_de_quatre_billets_au_plus() =>
        Prop.ForAll(RetraitsRealisables.ToArbitrary(), retrait =>
        {
            var (resultat, module) = Executer(retrait.Avant, retrait.Operation);
            var passages = module.Sorties.Select(s => s.Billets).ToList();

            Assert.All(passages, p => Assert.True(Positifs(p) && p.Total is >= 1 and <= 4, $"passage {p}"));
            Assert.Equal(resultat.Notes, Somme(passages));
            Assert.Equal((resultat.Notes.Total + 3) / 4, passages.Count);
        });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Les_frais_s_appliquent_strictement_au_dessus_de_100_et_hors_plafond() =>
        Prop.ForAll(RetraitsRealisables.ToArbitrary(), retrait =>
        {
            var resultat = Executer(retrait.Avant, retrait.Operation).Resultat;

            Assert.Equal(retrait.Montant > 100 ? 5 : 0, resultat.Fee);
            Assert.Equal(retrait.Avant.Compte.WithdrawnToday + retrait.Montant, resultat.Account.WithdrawnToday);
        });

    // ---- Retrait : propriétés métamorphiques ---------------------------------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Un_solde_plus_eleve_ne_change_pas_un_retrait_accepte() =>
        Prop.ForAll(
            (from r in RetraitsRealisables
             from solde in MultiplesEntre(5, r.Avant.Compte.Balance, 250)
             select (r, solde)).ToArbitrary(),
            entree =>
            {
                var (retrait, solde) = entree;
                var ecart = solde - retrait.Avant.Compte.Balance;
                var plusRiche = retrait.Avant with { Compte = retrait.Avant.Compte with { Balance = solde } };

                var reference = Executer(retrait.Avant, retrait.Operation).Resultat;
                var resultat = Executer(plusRiche, retrait.Operation).Resultat;

                Assert.Equal(
                    reference with { Account = reference.Account with { Balance = reference.Account.Balance + ecart } },
                    resultat);
            });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Avoir_moins_retire_aujourd_hui_ne_change_pas_un_retrait_accepte() =>
        Prop.ForAll(
            (from r in RetraitsRealisables
             from deja in MultiplesEntre(10, 0, r.Avant.Compte.WithdrawnToday)
             select (r, deja)).ToArbitrary(),
            entree =>
            {
                var (retrait, deja) = entree;
                var ecart = retrait.Avant.Compte.WithdrawnToday - deja;
                var moinsRetire = retrait.Avant with { Compte = retrait.Avant.Compte with { WithdrawnToday = deja } };

                var reference = Executer(retrait.Avant, retrait.Operation).Resultat;
                var resultat = Executer(moinsRetire, retrait.Operation).Resultat;

                Assert.Equal(
                    reference with
                    {
                        Account = reference.Account with { WithdrawnToday = reference.Account.WithdrawnToday - ecart },
                    },
                    resultat);
            });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Plus_de_billets_garde_un_retrait_realisable_sans_demander_plus_de_billets() =>
        Prop.ForAll(
            (from r in RetraitsRealisables
             from ajout in AuPlusDe(N(5, 5, 5) - r.Avant.Stock)
             from debloquer in Gen.Elements(false, true)
             select (r, ajout, debloquer)).ToArbitrary(),
            entree =>
            {
                var (retrait, ajout, debloquer) = entree;
                var mieuxGarni = retrait.Avant with
                {
                    Stock = retrait.Avant.Stock + ajout,
                    Bloquee = debloquer ? null : retrait.Avant.Bloquee,
                };

                var reference = Executer(retrait.Avant, retrait.Operation).Resultat;
                var resultat = Executer(mieuxGarni, retrait.Operation).Resultat;

                Assert.Equal(AtmStatus.Ok, resultat.Status);
                Assert.True(resultat.Notes.Total <= reference.Notes.Total,
                    $"{resultat.Notes} compte plus de billets que {reference.Notes}");
            });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Bloquer_une_cassette_que_le_retrait_n_utilise_pas_ne_change_rien() =>
        Prop.ForAll(
            (from r in RetraitsRealisables.Where(r => r.Avant.Bloquee is null)
             let inutilisees = Coupures.Where(d => PlusPetite(r).Of(d) == 0).ToArray()
             where inutilisees.Length > 0
             from d in Gen.Elements(inutilisees)
             select (r, d)).ToArbitrary(),
            entree =>
            {
                var (retrait, coupure) = entree;
                var bloquee = retrait.Avant with { Bloquee = coupure };

                var reference = Executer(retrait.Avant, retrait.Operation).Resultat;
                var resultat = Executer(bloquee, retrait.Operation).Resultat;

                Assert.Equal(0, reference.Notes.Of(coupure));
                Assert.Equal(reference, resultat);
            });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Pour_un_retrait_la_cassette_bloquee_compte_pour_zero() =>
        Prop.ForAll(Retraits.Where(r => r.Avant.Bloquee is not null).ToArbitrary(), retrait =>
        {
            // Bloquée avec n'importe quel nombre de billets, ou débloquée mais vide : même résultat.
            var bloquee = retrait.Avant.Bloquee!.Value;
            var videEtDebloquee = retrait.Avant with { Stock = Avec(retrait.Avant.Stock, bloquee, 0), Bloquee = null };

            var avecBloquee = Executer(retrait.Avant, retrait.Operation).Resultat;
            var sansBloquee = Executer(videEtDebloquee, retrait.Operation).Resultat;

            Assert.Equal(sansBloquee, avecBloquee);
        });

    // ---- Dépôt ---------------------------------------------------------------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Un_depot_vide_est_refuse_quel_que_soit_le_reste() =>
        Prop.ForAll(Situations.ToArbitrary(), s =>
        {
            var (resultat, module) = Executer(s, new Operation.Deposit(NoteCounts.None));

            Assert.Equal(AtmStatus.InvalidAmount, resultat.Status);
            AssertRienNeChange(s, resultat, module);
        });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Un_billet_de_la_cassette_bloquee_est_refuse_avant_de_controler_la_place() =>
        Prop.ForAll(
            (from compte in Comptes
             from stock in Stocks
             from bloquee in Gen.Elements(Coupures)
             from billets in Billets
             select new Depot(new Situation(compte, stock, bloquee), Avec(billets, bloquee, 1))).ToArbitrary(),
            depot =>
            {
                var (resultat, module) = Executer(depot.Avant, depot.Operation);

                Assert.Equal(AtmStatus.CassetteJammed, resultat.Status);
                AssertRienNeChange(depot.Avant, resultat, module);
            });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Un_depot_qui_ferait_deborder_une_cassette_est_refuse() =>
        Prop.ForAll(
            (from compte in Comptes
             from pleine in Gen.Elements(Coupures)
             from stock in Stocks
             from bloquee in Bloquees.Where(b => b != pleine)
             from billets in Billets
             let horsBloquee = bloquee is { } b ? Avec(billets, b, 0) : billets
             select new Depot(new Situation(compte, Avec(stock, pleine, 5), bloquee), Avec(horsBloquee, pleine, 1)))
            .ToArbitrary(),
            depot =>
            {
                var (resultat, module) = Executer(depot.Avant, depot.Operation);

                Assert.Equal(AtmStatus.CassetteFull, resultat.Status);
                AssertRienNeChange(depot.Avant, resultat, module);
            });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Un_depot_acceptable_range_les_billets_et_credite_le_solde() =>
        Prop.ForAll(DepotsAcceptables.ToArbitrary(), depot =>
        {
            var avant = depot.Avant;
            var (resultat, module) = Executer(avant, depot.Operation);

            Assert.Equal(AtmStatus.Ok, resultat.Status);
            Assert.Equal(NoteCounts.None, resultat.Notes);
            Assert.Equal(0, resultat.Fee);
            Assert.Equal(avant.Compte with { Balance = avant.Compte.Balance + depot.Billets.Value }, resultat.Account);
            Assert.Equal(avant.Stock + depot.Billets, module.Stock);
            Assert.Equal([(depot.Billets, true)], module.Rangements);
            Assert.Empty(module.Sorties);
        });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Le_sort_d_un_depot_ne_depend_pas_du_compte() =>
        Prop.ForAll(
            (from d in Depots from compte in Comptes select (d, compte)).ToArbitrary(),
            entree =>
            {
                var (depot, autreCompte) = entree;
                var (reference, moduleReference) = Executer(depot.Avant, depot.Operation);
                var (resultat, module) = Executer(depot.Avant with { Compte = autreCompte }, depot.Operation);

                Assert.Equal(reference.Status, resultat.Status);
                Assert.Equal(moduleReference.Stock, module.Stock);
                Assert.Equal(reference.Account.Balance - depot.Avant.Compte.Balance,
                    resultat.Account.Balance - autreCompte.Balance);
            });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Des_billets_tout_juste_deposes_peuvent_etre_retires() =>
        Prop.ForAll(
            (from d in DepotsAcceptables
             let valeur = d.Billets.Value
             from solde in MultiplesEntre(5, valeur, 250)
             from deja in MultiplesEntre(10, 0, 200 - valeur)
             select (d, new AccountState(solde, deja))).ToArbitrary(),
            entree =>
            {
                var (depot, compte) = entree;
                var module = new MoneyDispenser(depot.Avant.Stock, depot.Avant.Bloquee);
                var machine = new CashMachine(module);

                Assert.Equal(AtmStatus.Ok, machine.Execute(depot.Avant.Compte, depot.Operation).Status);
                var retrait = machine.Execute(compte, new Operation.Withdraw(depot.Billets.Value));

                Assert.Equal(AtmStatus.Ok, retrait.Status);
                Assert.Equal(depot.Billets.Value, retrait.Notes.Value);
            });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Deux_depots_qui_tiennent_ensemble_sont_acceptes_dans_les_deux_ordres_avec_le_meme_effet() =>
        Prop.ForAll(
            (from s in Situations
             from a in BilletsNonVides
             from b in BilletsNonVides
             select (s, a, b))
            .Where(e => (e.s.Bloquee is not { } j || (e.a + e.b).Of(j) == 0) && !Deborde(e.s.Stock + e.a + e.b))
            .ToArbitrary(),
            entree =>
            {
                var (s, a, b) = entree;

                (AtmResult Premier, AtmResult Second, NoteCounts Stock) DansLOrdre(NoteCounts x, NoteCounts y)
                {
                    var module = new MoneyDispenser(s.Stock, s.Bloquee);
                    var machine = new CashMachine(module);
                    var premier = machine.Execute(s.Compte, new Operation.Deposit(x));
                    var second = machine.Execute(premier.Account, new Operation.Deposit(y));
                    return (premier, second, module.Stock);
                }

                var ab = DansLOrdre(a, b);
                var ba = DansLOrdre(b, a);

                Assert.All(new[] { ab.Premier, ab.Second, ba.Premier, ba.Second },
                    r => Assert.Equal(AtmStatus.Ok, r.Status));
                Assert.Equal(s.Stock + a + b, ab.Stock);
                Assert.Equal(ab.Stock, ba.Stock);
                Assert.Equal(s.Compte with { Balance = s.Compte.Balance + a.Value + b.Value }, ab.Second.Account);
                Assert.Equal(ab.Second.Account, ba.Second.Account);
            });

    // ---- Pannes du module ----------------------------------------------------------------------------

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Une_panne_en_cours_de_retrait_arrete_tout_et_rend_les_billets_deja_sortis() =>
        Prop.ForAll(
            (from r in RetraitsRealisables from rang in Gen.Choose(0, 3) select (r, rang)).ToArbitrary(),
            entree =>
            {
                var (retrait, rang) = entree;
                var nombreDePassages = Executer(retrait.Avant, retrait.Operation).Module.Sorties.Count;
                var panne = rang % nombreDePassages;

                var (resultat, module) = Executer(retrait.Avant, retrait.Operation, panneAuPassage: panne);

                Assert.Equal(AtmStatus.DispenserFault, resultat.Status);
                Assert.Equal(0, resultat.Fee);
                Assert.Equal(retrait.Avant.Compte, resultat.Account);
                Assert.Equal(panne + 1, module.Sorties.Count);
                Assert.False(module.Sorties[^1].Reponse);
                Assert.Equal(Somme(module.Sorties.Where(s => s.Reponse).Select(s => s.Billets)), resultat.Notes);
                Assert.Equal(retrait.Avant.Stock - resultat.Notes, module.Stock);
                Assert.Empty(module.Rangements);
            });

    [Property(Replay = "42,1", MaxTest = 200)]
    public Property Une_panne_au_depot_ne_credite_rien() =>
        Prop.ForAll(DepotsAcceptables.ToArbitrary(), depot =>
        {
            var (resultat, module) = Executer(depot.Avant, depot.Operation, panneAuDepot: true);

            Assert.Equal(AtmStatus.DispenserFault, resultat.Status);
            AssertRienNeChange(depot.Avant, resultat, module);
            Assert.Single(module.Rangements);
            Assert.Empty(module.Sorties);
        });
}
