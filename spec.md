# Distributeur de billets : spécification

Version 0.2 (6 novembre 2026 : le domaine précise le module de billets). Tu implémentes `CashMachine.Execute` dans `Atm/CashMachine.cs`. Le projet `Atm.Contract` est fourni et ne se modifie pas.

## Le modèle

`Execute(account, operation)` reçoit un compte et une opération, et rend un résultat. La machine reçoit à sa construction un
`IMoneyDispenser`, le module qui détient les billets. Toute sortie ou entrée de billets passe par ce module.

- **Compte :** `Balance`, le solde, et `WithdrawnToday`, ce qui a déjà été retiré aujourd'hui, en euros.
- **Billets :** des coupures de 50, 20 et 10 €, une cassette par coupure, 5 billets au plus par cassette.
- **Résultat :** `Status`, `Notes` (les billets sortis), `Fee` (les frais prélevés) et `Account` (le compte après l'opération).

La machine est sans mémoire entre deux appels : elle ne dépend que du compte, de l'opération et du module.

## Le module de billets

`MoneyDispenser` est le vrai module. Son contrat, défini par `IMoneyDispenser` :

- `Stock` : les billets présents dans les cassettes.
- `Jammed` : la cassette bloquée, ou `null`. Une seule à la fois au plus.
- `Dispense(notes)` : sort ces billets, en un passage, tout ou rien. Rend `false`, sans rien changer, si le passage est vide, compte
  plus de **4 billets**, touche la cassette bloquée ou demande plus que le stock.
- `Load(notes)` : range ces billets, tout ou rien. Rend `false`, sans rien changer, si le dépôt est vide, touche la cassette bloquée
  ou dépasse 5 billets dans une cassette.

## Le retrait : `Withdraw(amount)`

Les contrôles se font **dans cet ordre**. Le premier qui échoue donne le statut.

| # | Condition de refus | Statut |
|---|---|---|
| 1 | `amount` est négatif, nul ou n'est pas un multiple de 10 | `InvalidAmount` |
| 2 | `WithdrawnToday + amount` dépasse 200 | `DailyLimitExceeded` |
| 3 | `amount + frais` dépasse `Balance` | `InsufficientFunds` |
| 4 | Aucune combinaison de billets disponibles ne fait exactement `amount` | `CannotDispense` |

**Les frais :** 5 € si `amount` est strictement supérieur à 100 €, 0 € sinon. Ils ne comptent pas dans le plafond du jour.

**Les billets disponibles :** le stock, sans la cassette bloquée, qui compte pour zéro.

**La combinaison :** parmi celles qui font exactement `amount`, celle qui compte **le moins de billets**. Avec ces trois coupures, elle
est toujours unique.

**La sortie :** les billets sortent en autant de passages que nécessaire, dans l'ordre que tu veux, 4 billets au plus par passage.

**Le succès :** statut `Ok`, `Notes` = la combinaison, `Fee` = les frais, `Balance` diminue de `amount + frais`, `WithdrawnToday` augmente
de `amount`.

## Le dépôt : `Deposit(notes)`

Les contrôles se font dans cet ordre.

| # | Condition de refus | Statut |
|---|---|---|
| 1 | Le dépôt ne contient aucun billet | `InvalidAmount` |
| 2 | Il contient un billet de la cassette bloquée | `CassetteJammed` |
| 3 | Il ferait dépasser 5 billets dans une cassette | `CassetteFull` |

**Le succès :** statut `Ok`, les billets rejoignent le module, `Balance` augmente de leur valeur, `WithdrawnToday` ne change pas, `Notes`
reste vide et `Fee` reste à 0.

## Refus et pannes

**Sur un refus,** rien ne change : `Notes` vide, `Fee` à 0, compte identique, aucun billet déplacé.

**Si le module rend `false` malgré ces contrôles,** c'est une panne. Le vrai module ne le fait jamais dans le domaine : ce cas reste à
traiter, mais il ne fait partie d'aucun résultat attendu. Dans ce cas : statut `DispenserFault`, on s'arrête aussitôt, `Fee` à 0, compte
identique. `Notes` contient les billets déjà sortis lors d'un retrait, et reste vide lors d'un dépôt.

## Le domaine

Le comportement n'est défini que sur ces valeurs. En dehors, il est libre.

| Valeur | Domaine |
|---|---|
| `Balance` | multiples de 5, de 0 à 250 |
| `WithdrawnToday` | multiples de 10, de 0 à 200 |
| Stock | 0 à 5 billets par cassette |
| Cassette bloquée | aucune, 50, 20 ou 10 |
| `amount` | -10, 0, 5, et les multiples de 10 de 10 à 220 |
| Dépôt | 0 ou 1 billet par coupure |
| Module de billets | le vrai `MoneyDispenser`, construit avec ce stock et cette cassette bloquée |

## Exemples

Stock noté `50×a 20×b 10×c`.

| Solde | Déjà retiré | Stock | Bloquée | Opération | Résultat |
|---|---|---|---|---|---|
| 100 | 0 | 50×1 20×3 10×0 | aucune | Retrait 60 | `Ok`, 20×3, solde 40, retiré 60 |
| 100 | 0 | 50×1 20×1 10×1 | aucune | Retrait 80 | `Ok`, 50×1 20×1 10×1, solde 20, retiré 80 |
| 105 | 0 | 50×5 20×5 10×5 | aucune | Retrait 100 | `Ok`, 50×2, frais 0, solde 5 |
| 115 | 0 | 50×5 20×5 10×5 | aucune | Retrait 110 | `Ok`, 50×2 10×1, frais 5, solde 0, retiré 110 |
| 110 | 0 | 50×5 20×5 10×5 | aucune | Retrait 110 | `InsufficientFunds` |
| 250 | 150 | 50×5 20×5 10×5 | aucune | Retrait 60 | `DailyLimitExceeded` |
| 250 | 0 | 50×1 20×0 10×0 | 50 | Retrait 50 | `CannotDispense` |
| 250 | 0 | 50×2 20×2 10×0 | 50 | Retrait 40 | `Ok`, 20×2, solde 210 |
| 0 | 200 | 50×0 20×0 10×0 | aucune | Retrait 5 | `InvalidAmount` |
| 10 | 0 | 50×0 20×5 10×0 | aucune | Dépôt 20×1 | `CassetteFull` |
| 10 | 30 | 50×0 20×1 10×0 | 20 | Dépôt 20×1 | `CassetteJammed` |
| 10 | 30 | 50×1 20×1 10×0 | aucune | Dépôt 50×1 10×1 | `Ok`, stock 50×2 20×1 10×1, solde 70, retiré 30 |
