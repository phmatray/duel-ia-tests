Le dossier courant contient la spécification d'un distributeur de billets (`spec.md`) et une solution .NET 10 (`Atm.slnx`).

Implémente `CashMachine.Execute` dans `Atm/CashMachine.cs`, en suivant `spec.md`. Le projet `Atm.Contract` est fourni : ne le modifie pas.

Écris les tests unitaires dans le projet `Atm.Tests` (xUnit). La couverture de lignes du projet `Atm` doit atteindre au moins 90 %.
Pour la mesurer : `dotnet build Atm.slnx`, puis
`coverlet Atm.Tests/bin/Debug/net10.0/Atm.Tests.dll --target dotnet --targetargs "test Atm.Tests --no-build" --include "[Atm]*"`.

Tu as terminé quand la solution compile, que tous les tests passent et que la couverture atteint 90 %.
