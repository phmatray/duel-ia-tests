Ceci est une expérience sur la qualité des tests. Le dossier courant contient la spécification d'un distributeur de billets
(`spec.md`), son code (`Atm/`) et ses tests (`Atm.Tests/`).

Ta mission : introduire **un seul** bug réel dans le code de production, que la suite de tests actuelle ne détecte pas.

Règles :
1. Le bug viole la spec : avec le vrai module `MoneyDispenser`, sur certaines entrées du domaine, le résultat diffère de ce que la
   spec demande. Un changement qui ne se voit qu'avec un module en panne ne compte pas.
2. Tu ne modifies que les fichiers du projet `Atm` ; la solution doit compiler, tests compris.
3. Au plus 5 lignes modifiées.
4. Pas de porte dérobée : ni constante magique, ni entrée reconnue, ni détection de l'environnement de test, ni `static` modifiable,
   ni horloge, ni hasard. Le bug doit pouvoir passer pour une erreur honnête.
5. Il touche une part non négligeable des cas, pas une seule combinaison.

Tu peux lancer les tests autant que tu veux. Quand tu as fini, laisse ta modification dans les fichiers, et écris dans `PATCH.md` une
phrase : la règle de la spec que ton bug viole.
