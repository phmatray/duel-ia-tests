# Rapport : 006 duel : campagne

10 constructions retenues.

| Arme | Tentatives | Rendus | Conformes | Bugs confirmés | Distincts | Passés | Wilson 95 % | Paliers 0,01 / 0,1 / 1 / 5 % |
|---|---|---|---|---|---|---|---|---|
| A1 | 30 | 25 | 22 | 14 | 12 | 14 | 30–64 % | 14 / 14 / 10 / 1 |
| A2 | 30 | 27 | 24 | 16 | 14 | 16 | 36–70 % | 16 / 16 / 11 / 2 |
| A3 | 30 | 28 | 23 | 14 | 11 | 14 | 30–64 % | 14 / 14 / 11 / 2 |
| A4 | 30 | 6 | 6 | 0 | 0 | 0 | 0–11 % | 0 / 0 / 0 / 0 |
| AE | 10 | 3 | 3 | 0 | 0 | 0 | 0–28 % | 0 / 0 / 0 / 0 |
| A5 | 20 | 20 | 19 | 19 | 7 | 0 | 0–16 % | 0 / 0 / 0 / 0 |
| A6 | 10 | 10 | 10 | 10 | 5 | 10 | 72–100 % | 10 / 10 / 3 / 1 |

## Les hypothèses

- **H1 : indéterminée.** passes_sur_lignes_couvertes 9, tentatives 30
- **H2 : réfutée.** pool 14, pour 0, contre 9, p 1.0, bootstrap (-1.0, -0.23529411764705882)
- **H3 : confirmée.** passes 16, tentatives 30, constructions 7, note copies de mutants survivants non écartées automatiquement : à vérifier à la main
- **H4 : confirmée.** pool 14, pour 14, contre 0, p 0.0001, bootstrap (1.0, 1.0)
- **H5 : indéterminée (descriptive).** pool 0, pour 0, contre 0, p 1.0, bootstrap None
- **H6 : réfutée.** pool 19, pour 0, contre 0, p 1.0, bootstrap (0.0, 0.0), ecartes_S5b 0.001
- **H7 : réfutée.** succes 10, tentatives_reelles 10, legitimes 10/10

AE est exploratoire : hors des scores et des hypothèses. Les comptes, jamais de décimales à l'écran (§5).
