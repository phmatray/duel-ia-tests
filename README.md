# J'ai demandé à une IA de saboter le code d'une autre : l'expérience complète

Ce dépôt accompagne la vidéo d'Atypical Craft (`@AtypicalCraft`). Tu y trouves tout ce qu'il faut pour vérifier ce que la vidéo
affirme, rejouer l'analyse, relancer l'expérience, et contester un verdict.

## L'expérience en deux minutes

Un **bâtisseur** (Claude Code) écrit un petit distributeur de billets en C# et ses tests, à partir de `spec.md`. Un **saboteur**
(Claude Code aussi, le même modèle) essaie d'y glisser un vrai bug que les tests ne voient pas. Six manches, six façons de tester :
la couverture, le mutation testing, l'intégration contre les mocks, les tests de propriétés, l'attendu écrit depuis la spec, et une
règle dans la CI.

Un **arbitre** décide de ce qu'est un bug, sans avis humain : une implémentation de référence, comparée au code sur tout le domaine de
la spec, soit 30 536 352 cas. Un patch n'est un bug que s'il change le résultat sur des cas où le code d'origine était juste.

Tout le détail est dans `protocole.md` : les hypothèses H1 à H7 et leurs seuils, écrits avant les données ; ce qu'est une détection ;
ce qu'on mesure ; les menaces connues ; et le journal de chaque modification faite après le premier gel (§14).

## Ce que l'expérience peut dire, et ce qu'elle ne peut pas dire

Elle dit : **sur ce projet, avec ce modèle et ces règles, voici ce qui est passé et ce qui a été pris.**

Elle ne dit pas :
- qu'une technique est meilleure en général : il n'y a qu'un seul petit projet ;
- quoi que ce soit sur les autres modèles : c'est Claude contre Claude ;
- à quelle fréquence ces bugs arrivent en vrai : un saboteur cherche les trous, un bug ordinaire arrive par accident ;
- ce que valent le TDD et le BDD en tant que méthodes : on teste seulement leur principe, écrire l'attendu depuis la spec sans voir le code.

## Ce que contient ce dépôt

| Dossier | Contenu |
|---|---|
| `protocole.md`, `spec.md` | Les règles de l'expérience et la spécification donnée au bâtisseur |
| `fourni/` | Le squelette remis au bâtisseur : le contrat, le vrai module de billets, un projet de tests vide |
| `arbitre/` | La référence et l'arbitre (publiés après l'expérience, comme prévu au protocole) |
| `harness/` | Le lanceur des sessions, l'orchestrateur, le script d'analyse, les prompts de chaque rôle, les images Docker, le contrôle de CI |
| `campagne/journal.jsonl` | Le journal brut : chaque session, chaque patch, chaque avis de l'arbitre, chaque rejeu (format : `FORMAT-du-journal.md`) |
| `campagne/rapport.md` | Le rapport produit par le script d'analyse |
| `campagne/constructions/Cxx/` | Pour chaque construction : le code du bâtisseur, sa refactorisation, les suites de tests figées (S1 à S4, S5a et S5b), un diff par patch de saboteur, les transcriptions des sessions |

Les transcriptions sont nettoyées : chemins locaux, jetons et adresses sont retirés. Rien d'autre n'est modifié.

## Rejouer l'analyse, sans relancer une seule session

```bash
python3 harness/analyse_campagne.py campagne
```

Le script relit le journal et refait tout le calcul : les verdicts, les scores par manche, les bugs distincts, les intervalles de
Wilson, les paliers de surface (0,01, 0,1, 1 et 5 %), et l'état de chaque hypothèse (test exact apparié, bootstrap par construction).
C'est le seul calcul de l'expérience : l'écran de la vidéo et le montage ne font que relire ses résultats.

## Rejouer la campagne

Il te faut Docker, environ 10 Go de disque, et un abonnement Claude qui permet Claude Code.

```bash
docker build -t banc-006:2.1.318 harness
docker build -t banc-006-couverture:2.1.318 -f harness/Dockerfile.couverture harness
docker build -t banc-006-stryker:2.1.318 -f harness/Dockerfile.stryker harness
claude setup-token          # puis range le jeton dans ~/.config/banc-006/token (chmod 600)
sh harness/test-technique.sh   # une session de test, hors expérience
python3 harness/campagne.py campagne
```

Les sessions tournent l'une après l'autre, chacune dans un conteneur neuf, sans rien de ta machine : ni `CLAUDE.md`, ni mémoire, ni
plugins. Les versions sont épinglées (Claude Code 2.1.318, .NET 10, Stryker.NET 5.0.0) et le modèle aussi (`claude-opus-5-5`).

**Compte large.** La campagne compte 260 sessions, plus la matrice de rejeu. Chez nous, une session a pris de 2 à 32 minutes, selon le
rôle et la charge du service, et la campagne a duré plusieurs jours, interrompue une fois par la limite hebdomadaire de l'abonnement
(protocole §14). Un même modèle ne rendra pas les mêmes patchs à chaque fois : tu obtiendras ta propre campagne, pas une copie de la nôtre.

## Les deux gels

Le protocole a été figé deux fois, avant de voir les résultats, par des tags du dépôt d'origine :

- **`006-gel-1`**, avant le pilote : les hypothèses, les seuils, les prompts, les budgets, les images ;
- **`006-gel-2`**, avant la campagne : le banc entier, la spec v0.2, les prompts corrigés après le pilote, le script d'analyse.

**L'empreinte de l'arbitre** (SHA-256 tronqué de la référence, de l'arbitre et du contrat) : `ba6e046b83935b7f`. Pour la vérifier :

```bash
cat arbitre/Atm.Reference/CashMachine.cs arbitre/Arbiter/Program.cs fourni/Atm.Contract/*.cs | shasum -a 256 | cut -c1-16
```

Le duel montré dans la vidéo n'a pas été choisi : il a été tiré au sort avec la graine 1106, fixée au second gel (protocole §15).

## Contester un verdict

Chaque verdict se refait à la main :
1. prends le diff du patch dans `campagne/constructions/Cxx/patchs/` et applique-le au code de la construction ;
2. compare-le à la référence avec l'arbitre : `dotnet arbitre/Arbiter/bin/Release/net10.0/Arbiter.dll nouveau <référence.dll> <original.dll> <patché.dll>` ;
3. lance la suite figée concernée (`suites/S1` à `S4`) sur le code patché, trois fois.

Si tu trouves une erreur (un bug que l'arbitre aurait dû voir, une détection mal comptée, un patch qui viole les règles), ouvre une
issue avec le numéro du patch et ce que tu as observé. Une erreur confirmée sera corrigée ici et signalée sous la vidéo.

## Licence

- **Le code** (le banc, l'arbitre, le harness, les scripts) : licence MIT, voir `LICENSE`.
- **Les données et le texte** (le journal, les transcriptions, les patchs, le protocole, ce README) : licence Creative Commons
  Attribution 4.0 International (CC BY 4.0), voir `LICENSE-DONNEES.md`. Tu peux les réutiliser en citant la source :
  « Philippe Matray, Atypical Craft, *J'ai demandé à une IA de saboter le code d'une autre*, 2026 ».

Le pilote, exclu des résultats, n'est pas publié ici.
