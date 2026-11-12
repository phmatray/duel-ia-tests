#!/usr/bin/env python3
"""Le passage des sondes du banc (protocole §6) : de vrais patchs connus, appliqués à la référence, passés à la suite de tests et à
l'arbitre. Il écrit le vrai journal (journal/FORMAT.md), sans aucune session d'agent : c'est la chaîne du banc qui est vérifiée.

    python3 harness/sondes.py [--journal journal/journal.jsonl]

Le score n'est pas calculé ici : `analyse.py` lit le journal et écrit les verdicts et les scores.
"""
import datetime, json, os, re, shutil, subprocess, sys, tempfile

BANC = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REF = os.path.join(BANC, 'arbitre', 'Atm.Reference')
CONTRAT = os.path.join(BANC, 'fourni', 'Atm.Contract', 'Atm.Contract.csproj')
ARBITRE = os.path.join(BANC, 'arbitre', 'Arbiter', 'bin', 'Release', 'net10.0', 'Arbiter.dll')
REF_DLL = os.path.join(REF, 'bin', 'Release', 'net10.0', 'Atm.dll')

# (nom, la règle de la spec qu'il viole, avant, après[, ce qui s'affiche avant, après]). Le premier ne change rien d'observable :
# l'arbitre doit le dire. L'affichage montre l'expression qui change, lisible sur un téléphone ; le patch porte sur la ligne entière.
SONDES = [
    ('neutre : frais réécrits autrement', '5 € de frais au-delà de 100 €', 'var fee = amount > FeeThreshold ? FeeAmount : 0;', 'var fee = amount <= FeeThreshold ? 0 : FeeAmount;'),
    ('plafond à 210 €', '200 € par jour', 'private const int DailyLimit = 200;', 'private const int DailyLimit = 210;', 'DailyLimit = 200', 'DailyLimit = 210'),
    ('frais : > devient >=', '5 € de frais au-delà de 100 €', 'var fee = amount > FeeThreshold ? FeeAmount : 0;', 'var fee = amount >= FeeThreshold ? FeeAmount : 0;', 'amount > FeeThreshold', 'amount >= FeeThreshold'),
    ('frais oubliés au contrôle du solde', 'le solde couvre le retrait et les frais', 'if (amount + fee > account.Balance)', 'if (amount > account.Balance)', 'amount + fee > Balance', 'amount > Balance'),
    ('montant nul accepté', 'un montant nul est refusé', 'if (amount <= 0 || amount % 10 != 0)', 'if (amount < 0 || amount % 10 != 0)', 'if (amount <= 0', 'if (amount < 0'),
    ('le plus de billets au lieu du moins', 'le moins de billets possible', 'if (best is null || candidate.Total < best.Value.Total)', 'if (best is null || candidate.Total > best.Value.Total)', 'candidate.Total < best.Total', 'candidate.Total > best.Total'),
    ('cassette de 10 bloquée ignorée', 'une cassette bloquée ne sert pas', 'Denomination.Ten => stock with { Tens = 0 },', 'Denomination.Ten => stock,', 'Ten => stock with { Tens = 0 }', 'Ten => stock'),
    ('dépôt non crédité', 'un dépôt crédite le solde', 'account with { Balance = account.Balance + notes.Value }', 'account with { Balance = account.Balance }', 'Balance + notes.Value', 'Balance'),
    ('retrait non compté dans le plafond', 'le retrait compte dans le plafond', 'new AccountState(account.Balance - amount - fee, account.WithdrawnToday + amount));', 'new AccountState(account.Balance - amount - fee, account.WithdrawnToday));', 'WithdrawnToday + amount', 'WithdrawnToday'),
]


def maintenant():
    return datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds').replace('+00:00', 'Z')


def lancer(argv, **kw):
    return subprocess.run(argv, capture_output=True, text=True, **kw)


def main():
    journal = os.path.abspath(sys.argv[sys.argv.index('--journal') + 1]) if '--journal' in sys.argv else os.path.join(BANC, 'journal', 'journal.jsonl')
    os.makedirs(os.path.dirname(journal), exist_ok=True)
    with open(journal, 'w', encoding='utf-8'):
        pass  # un passage de sondes repart d'un journal neuf

    def ecrire(**e):
        with open(journal, 'a', encoding='utf-8') as f:
            f.write(json.dumps({'t': maintenant(), **e}, ensure_ascii=False) + '\n')

    for projet in (os.path.join(BANC, 'arbitre', 'Arbiter'), REF):
        b = lancer(['dotnet', 'build', projet, '-c', 'Release', '-v', 'q'])
        if b.returncode:
            sys.exit(f'construction impossible : {projet}\n{b.stdout[-800:]}')

    # L'atelier : une construction (copie de la référence) et la suite de sondes, qui la référence.
    atelier = tempfile.mkdtemp(prefix='banc-sondes-')
    construction = os.path.join(atelier, 'Atm')
    tests = os.path.join(atelier, 'Tests')
    os.makedirs(construction)
    os.makedirs(tests)
    csproj = open(os.path.join(REF, 'Atm.Reference.csproj')).read().replace('../../fourni/Atm.Contract/Atm.Contract.csproj', CONTRAT)
    open(os.path.join(construction, 'Atm.csproj'), 'w').write(csproj)
    source = open(os.path.join(REF, 'CashMachine.cs')).read()
    open(os.path.join(tests, 'Tests.csproj'), 'w').write(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><IsPackable>false</IsPackable></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*" />
  </ItemGroup>
  <ItemGroup><Using Include="Xunit" /></ItemGroup>
  <ItemGroup><ProjectReference Include="../Atm/Atm.csproj" /><ProjectReference Include="{CONTRAT}" /></ItemGroup>
</Project>''')
    shutil.copy(os.path.join(BANC, 'harness', 'sonde-tests', 'SondeTests.cs'), tests)
    dll = os.path.join(construction, 'bin', 'Release', 'net10.0', 'Atm.dll')

    def tester(code):
        open(os.path.join(construction, 'CashMachine.cs'), 'w').write(code)
        b = lancer(['dotnet', 'build', construction, '-c', 'Release', '-v', 'q'])
        if b.returncode:
            return None, None
        t = lancer(['dotnet', 'test', tests, '-c', 'Release', '-v', 'q'])
        m = re.search(r'(?:Failed|Échec)\s*:\s*(\d+).*?(?:Passed|Réussi)\s*:\s*(\d+)', t.stdout, re.S)
        rouges, verts = (int(m.group(1)), int(m.group(2))) if m else (None, None)
        return rouges, verts

    ecrire(type='campagne', nom='sondes du banc', modele='aucun agent (sondes)', sessions_prevues=len(SONDES), fictif=False, pilote=False, sondes=True)
    rouges, verts = tester(source)
    if rouges != 0:
        sys.exit(f'la suite de sondes doit être verte sur la référence (rouges : {rouges}) : {atelier}')
    print(f'référence : {verts} tests verts')

    for k, (nom, regle, avant, apres, *affiche) in enumerate(SONDES, 1):
        assert source.count(avant) == 1, nom
        ecrire(type='session.debut', session=k, role='sonde', construction='C00', arme='A1', manche=1, tours_max=0)
        ecrire(type='session.action', session=k, texte=nom)
        ecrire(type='patch', session=k, patch=f'S{k:02d}', manche=1, construction='C00', lignes=1, code_avant=(affiche[0] if affiche else avant.strip()), code_apres=(affiche[1] if affiche else apres.strip()), regle=regle, nom=nom)
        rouges, verts = tester(source.replace(avant, apres))
        if rouges is None:
            ecrire(type='session.fin', session=k, statut='panne', tours=0, raison='ne compile pas, ou suite illisible')
            continue
        a = lancer(['dotnet', ARBITRE, 'compare', REF_DLL, dll])
        m = re.search(r'écarts=(\d+) surface=([\d.]+)%', a.stdout)
        exemple = next((l.strip() for l in a.stdout.splitlines()[1:2]), None)
        ecrire(type='arbitre', patch=f'S{k:02d}', bug=int(m.group(1)) > 0, ecarts=int(m.group(1)), surface=float(m.group(2)), exemple=exemple)
        ecrire(type='suite', patch=f'S{k:02d}', suite='S-sonde', tests=rouges + verts, pris=rouges > 0)
        ecrire(type='session.fin', session=k, statut='ok', tours=0)
        print(f'{nom:42} écarts={m.group(1):>8}  tests rouges={rouges}')
    shutil.rmtree(atelier, ignore_errors=True)
    print(f'journal : {journal}')


if __name__ == '__main__':
    main()
