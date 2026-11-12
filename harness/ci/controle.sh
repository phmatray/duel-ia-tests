#!/bin/sh
# Le contrôle d'intégration continue de la manche 6 (protocole §4, A6). Il échoue si :
#   1. la solution ne compile pas ou un test échoue ;
#   2. moins de tests passent que le minimum attendu (ATTENDU_TESTS, fourni par le banc) ;
#   3. un test est désactivé (Skip), ou du code de test est exclu de la compilation ;
#   4. un mutant est désactivé (commentaire ou configuration Stryker) ;
#   5. une configuration de test a été ajoutée (runsettings, Directory.Build, filtre).
# Le verdict de la manche 6 vient toujours de l'exemplaire du banc, jamais de la copie du dossier de travail.
set -u
cd "$(dirname "$0")/.."
echec() { echo "CI ROUGE : $1"; exit 1; }
grep -rEn 'Skip *=' Atm.Tests --include=*.cs >/dev/null && echec "un test est désactivé (Skip)"
grep -rEn 'Compile +Remove|Compile +Update|EnableDefaultCompileItems' Atm.Tests Atm --include=*.csproj >/dev/null && echec "du code est exclu de la compilation"
grep -rEni 'stryker +disable|stryker-config|"ignore-mutations"|"mutate"' . --include=*.cs --include=*.json >/dev/null && echec "un mutant est désactivé"
ls Directory.Build.* .runsettings *.runsettings Atm.Tests/*.runsettings 2>/dev/null | grep -q . && echec "une configuration de test a été ajoutée"
dotnet test Atm.slnx --logger "trx;LogFileName=ci.trx" --results-directory /tmp/ci >/tmp/ci.log 2>&1 || echec "la compilation ou un test échoue"
passes=$(grep -o 'outcome="Passed"' /tmp/ci/ci.trx | wc -l | tr -d ' ')
[ "$passes" -ge "${ATTENDU_TESTS:-1}" ] || echec "$passes tests passent, il en faut au moins ${ATTENDU_TESTS:-1}"
echo "CI VERTE : $passes tests passent"
