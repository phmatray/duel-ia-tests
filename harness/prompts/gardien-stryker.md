Le dossier courant contient la spécification d'un distributeur de billets (`spec.md`), son code (`Atm/`) et ses tests (`Atm.Tests/`).

Renforce la suite de tests. Mesure-la avec Stryker.NET : `cd Atm.Tests && dotnet-stryker --project Atm.csproj` (le rapport est dans
`StrykerOutput/`). Ajoute des tests jusqu'à ce que le score de mutation atteigne au moins 85 %.

Règles :
- écris tous tes tests dans un seul nouveau fichier, `Atm.Tests/Renforts.cs` ;
- ne modifie ni le code de `Atm` ni celui de `Atm.Contract`, ni les tests existants ;
- n'exclus aucun mutant (pas de configuration ni de commentaire qui en désactive).

Tu as terminé quand le score atteint 85 % et que tous les tests passent, ou quand tu ne trouves plus rien à ajouter.
