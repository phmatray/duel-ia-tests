Le dossier courant contient la spécification d'un distributeur de billets (`spec.md`), son code (`Atm/`) et ses tests (`Atm.Tests/`).

Ajoute des tests d'intégration : ils font travailler `CashMachine` avec le vrai module `MoneyDispenser`, sans doublure ni mock, et
vérifient le résultat et le stock du module après chaque opération.

Règles :
- écris tous tes tests dans un seul nouveau fichier, `Atm.Tests/Integration.cs` ;
- ne modifie ni le code de `Atm` ni celui de `Atm.Contract`, ni les tests existants.

Tu as terminé quand tous les tests passent et que tu ne trouves plus rien d'utile à ajouter.
