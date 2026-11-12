# Le journal du banc

Le banc écrit **un événement par ligne**, en JSON (JSONL), dans `journal.jsonl`, sans jamais réécrire une ligne. L'application et les
gabarits de montage le **relisent** : ils n'influencent aucune session et ne calculent aucun score (`../../protocole.md`, §9).

Chaque ligne porte `t` (heure ISO 8601, UTC) et `type`. Champs communs quand ils ont un sens : `session` (numéro), `manche` (1 à 6),
`construction` (`C01`…), `arme` (`A1`…`A6`, `AE`).

| `type` | Écrit par | Champs |
|---|---|---|
| `campagne` | le banc, en tête | `nom`, `modele`, `sessions_prevues`, `fictif` (vrai seulement pour un journal d'exemple), `sondes` (vrai : un passage de patchs sondes, sans agent, pas le duel), `technique` (vrai : le test du lanceur, une session hors expérience), `pilote` (vrai : la ligne « bugs passés » est masquée) |
| `session.debut` | le banc | `session`, `role` (`batisseur`, `refactoriseur`, `gardien`, `saboteur`, `redacteur`), `construction`, `arme`, `manche`, `tours_max` |
| `session.tour` | le banc | `session`, `tours` |
| `session.action` | le banc | `session`, `texte` (la dernière commande ou action, une ligne) |
| `session.fin` | le banc | `session`, `statut` (`ok`, `panne`), `tours`, `raison` (pour une panne) |
| `patch` | le banc | `session`, `patch` (identifiant), `manche`, `construction`, `lignes`, `code_avant`, `code_apres` (ce qui change, tel qu'on le lit à l'écran : l'expression touchée, 34 caractères au plus), `regle` (la phrase de la spec qu'elle viole, pour l'écran) |
| `arbitre` | l'arbitre | `patch`, `bug` (vrai ou faux), `ecarts`, `surface` (en %, sans arrondi), `exemple` (une paire divergente en clair : « tu retires 210 € pour 200 € de plafond ») |
| `suite` | le banc | `patch`, `suite` (`S1`, `S2`…), `tests`, `pris` (vrai si au moins un test valide le prend, §6) |
| `verdict` | le script d'analyse | `patch`, `manche`, `issue` (`passe`, `pris`, `non_conforme`), `recit` |
| `score` | le script d'analyse | `manche` (0 : le total de la campagne), `passes`, `pris`, `tentatives`, `prevues` |

**Règles :** un `score` n'est écrit que par le script d'analyse. Un journal où `campagne.pilote` est vrai ne contient pas de `passes`
lisible : l'application les masque. Un journal d'exemple porte `fictif: true`, et tout ce qui le montre l'écrit à l'écran.
