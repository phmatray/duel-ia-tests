# Le harness du banc

Ce qui existe aujourd'hui : **le passage des sondes** (protocole §6), la vraie chaîne du banc sans aucune session d'agent.

```bash
cd videos/006-mutation-testing/banc
python3 harness/sondes.py                       # écrit journal/journal.jsonl (repart d'un journal neuf)
python3 harness/analyse.py journal/journal.jsonl # ajoute les verdicts et les scores : le seul calcul
cd app && dotnet run --urls http://127.0.0.1:5180 -- --Banc:Journal=../journal/journal.jsonl
```

- `sondes.py` applique 9 patchs connus à une copie de la référence (le premier est neutre : l'arbitre doit dire « pas de bug »),
  passe la suite d'exemples `sonde-tests/` (7 tests, verte sur la référence) et l'arbitre, et écrit le journal (`../journal/FORMAT.md`).
- `analyse.py` décide passé, pris ou non conforme, et écrit les scores par manche et le total (manche 0). Pour les sondes, la détection
  est simplifiée : une seule suite, prise dès qu'un test rougit (le protocole §6 en demande davantage pour la campagne).
- Résultat du 6 novembre : la sonde neutre est bien « non conforme » ; sur 8 vrais bugs, la suite d'exemples en prend 3 et en laisse
  passer 5. Ce sont des sondes, pas le duel : l'écran l'affiche.

**Le lanceur de sessions** (`lanceur.py`, image `Dockerfile` épinglée) : une session `claude -p` isolée, traduite en événements du journal. Il lit le jeton de l'abonnement dans `~/.config/banc-006/token` (jamais dans le dépôt). Son test : `sh harness/test-technique.sh`, une session hors expérience, dans `journal/technique.jsonl`.

**Pas encore fait :** les rôles de la campagne (bâtisseur, gardiens, saboteurs) et leurs prompts, les autres suites, la détection complète
du §6, le pilote. Le bouton « Lancer la session suivante » de l'app attend cette commande (`Banc:Commande`).
