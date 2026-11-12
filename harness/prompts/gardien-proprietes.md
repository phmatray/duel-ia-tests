Le dossier courant contient la spécification d'un distributeur de billets (`spec.md`), son code (`Atm/`) et ses tests (`Atm.Tests/`).

Ajoute des tests de propriétés avec FsCheck (paquet `FsCheck.Xunit`, version 3.3.0) : des règles qui doivent rester vraies pour toutes
les entrées du domaine de la spec, vérifiées sur des cas tirés au hasard. Fixe la graine (`Replay = "42,0"`) et le nombre de cas
(`MaxTest = 200`) de chaque propriété, pour que les tests soient reproductibles.

Règles :
- écris tous tes tests dans un seul nouveau fichier, `Atm.Tests/Proprietes.cs` ;
- ne modifie ni le code de `Atm` ni celui de `Atm.Contract`, ni les tests existants.

Tu as terminé quand tous les tests passent et que tu ne trouves plus rien d'utile à ajouter.
