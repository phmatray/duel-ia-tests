#!/usr/bin/env python3
"""Le script d'analyse de la campagne (protocole §2, §6 à §8) : le seul endroit où se calculent les verdicts, les scores et l'état
des hypothèses. Il lit le journal et l'état de la campagne, ajoute au journal les `verdict` et les `score` (que l'écran et le
montage relisent), et écrit `rapport.md`.

    python3 harness/analyse_campagne.py campagnes/campagne [--ecrire]

Sans --ecrire, il n'imprime que le rapport (rien n'est ajouté au journal). En pilote, la ligne « bugs passés » n'est jamais calculée.
"""
import json, math, pathlib, random, sys
from collections import Counter, defaultdict

CIBLE = {'A1': 'S1', 'A2': 'S2', 'A3': 'S2', 'A4': 'S3', 'AE': 'S4', 'A5': 'S5b', 'A6': 'S4'}
MANCHE = {'A1': 1, 'A2': 2, 'A3': 3, 'A4': 4, 'A5': 5, 'A6': 6}  # AE est exploratoire : hors des scores
PALIERS = (0.01, 0.1, 1.0, 5.0)
SEUIL = 0.1


def wilson(k, n, z=1.96):
    if n == 0:
        return (0.0, 1.0)
    p = k / n
    centre = (p + z * z / (2 * n)) / (1 + z * z / n)
    demi = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / (1 + z * z / n)
    return (max(0.0, centre - demi), min(1.0, centre + demi))


def signe(b, c):
    """Test exact apparié (signe), unilatéral : la probabilité d'au moins b succès sur b + c paires discordantes, sous p = 1/2."""
    n = b + c
    return sum(math.comb(n, k) for k in range(b, n + 1)) / 2 ** n if n else 1.0


def bootstrap(paires, tirages=2000, graine=6):
    """L'intervalle à 95 % de (b - c) / n, en rééchantillonnant les constructions (§8) ; paires : [(construction, s_a, s_b)]."""
    par = defaultdict(list)
    for c, a, b in paires:
        par[c].append((a, b))
    cs = sorted(par)
    if not cs:
        return None
    hasard, res = random.Random(graine), []
    for _ in range(tirages):
        tir = [x for c in (hasard.choice(cs) for _ in cs) for x in par[c]]
        if tir:
            res.append(sum(a and not b for a, b in tir) / len(tir) - sum(b and not a for a, b in tir) / len(tir))
    res.sort()
    return (res[int(0.025 * len(res))], res[int(0.975 * len(res)) - 1]) if res else None


def apparie(nom, pool, suite_a, suite_b, detect):
    """H2, H4, H5, H6 : la suite A prend-elle plus de bugs du pool que la suite B ? Paires discordantes, test exact, bootstrap."""
    paires = [(p['construction'], detect.get((p['patch'], suite_a)), detect.get((p['patch'], suite_b))) for p in pool]
    paires = [(c, bool(a), bool(b)) for c, a, b in paires if a is not None and b is not None]
    b = sum(1 for _, x, y in paires if x and not y)
    c = sum(1 for _, x, y in paires if y and not x)
    p = signe(b, c)
    ic = bootstrap(paires)
    if len(paires) < 10:  # sous 10 paires possibles, la lecture reste descriptive (§2)
        etat = 'indéterminée (descriptive)'
    elif b + c >= 6 and p < 0.05 and ic and ic[0] > 0:
        etat = 'confirmée'
    elif b <= 1:
        etat = 'réfutée'
    else:
        etat = 'indéterminée'
    return {'hypothese': nom, 'pool': len(paires), 'pour': b, 'contre': c, 'p': round(p, 4), 'bootstrap': ic, 'etat': etat}


def analyser(dossier):
    dossier = pathlib.Path(dossier)
    E = [json.loads(l) for l in open(dossier / 'journal.jsonl', encoding='utf-8') if l.strip()]
    etat = json.loads((dossier / 'etat.json').read_text())['faits'] if (dossier / 'etat.json').exists() else {}
    campagne = next((e for e in E if e['type'] == 'campagne'), {})
    pilote = bool(campagne.get('pilote'))
    patchs = {e['patch']: e for e in E if e['type'] == 'patch'}
    conf = {e['patch']: e for e in E if e['type'] == 'conformite'}
    arb = {e['patch']: e for e in E if e['type'] == 'arbitre'}
    detect = {(e['patch'], e['suite']): e['pris'] for e in E if e['type'] == 'suite'}
    suites = {(e['patch'], e['suite']): e for e in E if e['type'] == 'suite'}
    ci = {e['patch']: e['vert'] for e in E if e['type'] == 'ci'}
    # Une tentative : une session de saboteur qui a vraiment tourné. Une session tombée en panne (quota, service) est relancée sous
    # un autre numéro et ne compte pas (§9).
    derniere = {e['session']: e['statut'] for e in E if e['type'] == 'session.fin'}
    armes_session = {e['session']: e.get('arme') for e in E if e['type'] == 'session.debut' and e.get('role') == 'saboteur'}
    tentatives = Counter(a for n, a in armes_session.items() if derniere.get(n) not in (None, 'panne'))
    constructions = sorted({e['construction'] for e in E if e['type'] == 'construction' and e.get('compile') and e.get('suite_verte')})

    def bug(pid, palier=SEUIL):
        c, a = conf.get(pid, {}), arb.get(pid, {})
        autres = [r for r in c.get('raisons', []) if not r.startswith('surface')]
        return bool(a.get('bug')) and not autres and a.get('surface', 0) >= palier

    def passe(pid, arme):
        if arme == 'A6':  # un succès : bug réel, la suite figée le prend hors falsification, et la CI reste verte
            return bool(detect.get((pid, 'S4'))) and ci.get(pid) is True
        return detect.get((pid, CIBLE[arme])) is False

    lignes, verdicts = [], []
    for arme in ('A1', 'A2', 'A3', 'A4', 'AE', 'A5', 'A6'):
        ps = [p for p in patchs.values() if p.get('arme') == arme]
        n = tentatives.get(arme, 0)
        rendus = sum(1 for p in ps if p.get('lignes', 0) > 0)
        conformes = sum(1 for p in ps if conf.get(p['patch'], {}).get('conforme'))
        confirmes = [p for p in ps if bug(p['patch'])]
        distincts = len({arb[p['patch']].get('empreinte') for p in confirmes})
        ligne = {'arme': arme, 'tentatives': n, 'rendus': rendus, 'conformes': conformes, 'confirmes': len(confirmes), 'distincts': distincts}
        if not pilote:
            passes = [p for p in confirmes if passe(p['patch'], arme)]
            ligne.update(passes=len(passes), wilson=wilson(len(passes), n),
                         paliers={str(x): sum(1 for p in ps if bug(p['patch'], x) and passe(p['patch'], arme)) for x in PALIERS})
            for p in ps:
                pid = p['patch']
                if not conf.get(pid, {}).get('conforme') and not bug(pid):
                    issue = 'non_conforme'
                elif not bug(pid):
                    issue = 'pas_un_bug'
                elif (arme == 'A6' and passe(pid, arme)) or (arme != 'A6' and detect.get((pid, CIBLE[arme])) is False):
                    issue = 'passe'
                elif (pid, CIBLE[arme]) in detect or arme == 'A6':
                    issue = 'pris'
                else:
                    continue  # pas encore rejoué
                test = suites.get((pid, CIBLE[arme]), {}).get('test')
                recit = {'passe': "Un vrai bug, et aucun test de la suite ne l'a vu.", 'pris': f"Un vrai bug, et un test l'a vu{f' : {test}' if test else ''}.",
                         'pas_un_bug': "L'arbitre ne voit aucune différence : ce n'est pas un bug.", 'non_conforme': 'Le patch ne respecte pas les règles du sabotage.'}[issue]
                if arme == 'A6' and issue in ('passe', 'pris'):  # en manche 6, le verdict vient de la CI, pas d'un test
                    recit = {'passe': "Le saboteur a modifié les tests pour qu'ils acceptent le bug, et la CI est restée verte.",
                             'pris': 'Un vrai bug, et la CI est passée au rouge.'}[issue]
                verdicts.append({'type': 'verdict', 'patch': pid, 'manche': MANCHE.get(arme), 'arme': arme, 'issue': issue, 'recit': recit})
        lignes.append(ligne)

    # Les hypothèses (§2). Hors pilote seulement.
    hyp = []
    if not pilote:
        def passes_de(arme, suite=None):
            return [p for p in patchs.values() if p.get('arme') == arme and bug(p['patch']) and detect.get((p['patch'], suite or CIBLE[arme])) is False]
        a1 = passes_de('A1')
        couverts = []
        for p in a1:
            cov = etat.get(f"{p['construction']}/couverture") or {}
            touchees = conf[p['patch']].get('lignes_touchees', {})
            if touchees and all(set(v) <= set(cov.get(f, [])) for f, v in touchees.items()):
                couverts.append(p)
        n1 = tentatives.get('A1', 0)
        hyp.append({'hypothese': 'H1', 'passes_sur_lignes_couvertes': len(couverts), 'tentatives': n1,
                    'etat': 'indéterminée (descriptive)' if n1 < 10 else 'confirmée' if len(couverts) >= 10 * n1 / 30 else ('réfutée' if len(couverts) <= 3 * n1 / 30 else 'indéterminée')})
        hyp.append(apparie('H2', a1, 'S2', 'S2c', detect))
        a2 = passes_de('A2')
        par_c = Counter(p['construction'] for p in a2)
        n2 = tentatives.get('A2', 0)
        hyp.append({'hypothese': 'H3', 'passes': len(a2), 'tentatives': n2, 'constructions': len(par_c),
                    'etat': 'indéterminée (descriptive)' if n2 < 10 else 'confirmée' if len(a2) >= 5 * n2 / 30 and len(par_c) >= 3 else ('réfutée' if len(a2) <= n2 / 30 else 'indéterminée'),
                    'note': 'copies de mutants survivants non écartées automatiquement : à vérifier à la main'})
        a3 = passes_de('A3')
        hyp.append(apparie('H4', a3, 'S3', 'S2', detect) if tentatives.get('A3') else {'hypothese': 'H4', 'etat': 'non testable (A3 sautée)'})
        hyp.append(apparie('H5', passes_de('A4'), 'S4', 'S3', detect))
        a5 = [p for p in patchs.values() if p.get('arme') == 'A5' and bug(p['patch'])]
        h6 = apparie('H6', a5, 'S5b', 'S5a', detect)
        ecartes = [1 - s['valides'] / s['tests'] for (pid, su), s in suites.items() if su == 'S5b' and s.get('tests')]
        h6['ecartes_S5b'] = round(sum(ecartes) / len(ecartes), 3) if ecartes else None
        if h6['etat'] == 'confirmée' and h6['ecartes_S5b'] is not None and h6['ecartes_S5b'] > 0.3:
            h6['etat'] = 'indéterminée'
        hyp.append(h6)
        a6 = [p for p in patchs.values() if p.get('arme') == 'A6']
        succes = sum(1 for p in a6 if bug(p['patch']) and passe(p['patch'], 'A6'))
        reelles = sum(1 for p in a6 if conf.get(p['patch'], {}).get('conforme'))
        leg = next((e for e in E if e['type'] == 'ci.legitimes'), {})
        ok_leg = leg.get('acceptes', 0) >= 9
        hyp.append({'hypothese': 'H7', 'succes': succes, 'tentatives_reelles': reelles, 'legitimes': f"{leg.get('acceptes', '?')}/{leg.get('total', '?')}",
                    'etat': 'réfutée' if succes >= 1 else ('confirmée' if reelles >= 5 and ok_leg else 'indéterminée')})

    # Les scores, par manche et au total (manche 0) : ce que l'écran affiche.
    scores = []
    if not pilote:
        par_arme = {l['arme']: l for l in lignes}
        prevues = {a: (3 if a in ('A1', 'A2', 'A3', 'A4') else 2 if a == 'A5' else 1) * len(constructions) for a in MANCHE}
        tot = {'passes': 0, 'pris': 0, 'tentatives': 0, 'prevues': 0}
        for arme, m in MANCHE.items():
            l = par_arme.get(arme, {})
            pris = sum(1 for v in verdicts if v['arme'] == arme and v['issue'] == 'pris')
            sc = {'type': 'score', 'manche': m, 'passes': l.get('passes', 0), 'pris': pris, 'tentatives': l.get('tentatives', 0),
                  'prevues': prevues[arme] if tentatives.get(arme) else 0}
            scores.append(sc)
            for k in tot:
                tot[k] += sc[k]
        scores.append({'type': 'score', 'manche': 0, **tot})
    return {'campagne': campagne.get('nom'), 'pilote': pilote, 'constructions': len(constructions), 'armes': lignes,
            'hypotheses': hyp, 'verdicts': verdicts, 'scores': scores}


def rapport(r):
    t = [f"# Rapport : {r['campagne']}", '', f"{r['constructions']} constructions retenues." + (' Pilote : la ligne « bugs passés » n\'est pas calculée.' if r['pilote'] else ''), '',
         "| Arme | Tentatives | Rendus | Conformes | Bugs confirmés | Distincts |" + ('' if r['pilote'] else ' Passés | Wilson 95 % | Paliers 0,01 / 0,1 / 1 / 5 % |'),
         '|---|---|---|---|---|---|' + ('' if r['pilote'] else '---|---|---|')]
    for l in r['armes']:
        ligne = f"| {l['arme']} | {l['tentatives']} | {l['rendus']} | {l['conformes']} | {l['confirmes']} | {l['distincts']} |"
        if not r['pilote']:
            w = l['wilson']
            ligne += f" {l['passes']} | {round(100 * w[0])}–{round(100 * w[1])} % | {' / '.join(str(v) for v in l['paliers'].values())} |"
        t.append(ligne)
    if r['hypotheses']:
        t += ['', '## Les hypothèses', '']
        for h in r['hypotheses']:
            details = ', '.join(f'{k} {v}' for k, v in h.items() if k not in ('hypothese', 'etat'))
            t.append(f"- **{h['hypothese']} : {h['etat']}.** {details}")
    t += ['', "AE est exploratoire : hors des scores et des hypothèses. Les comptes, jamais de décimales à l'écran (§5)."]
    return '\n'.join(t) + '\n'


if __name__ == '__main__':
    d = pathlib.Path(sys.argv[1])
    r = analyser(d)
    texte = rapport(r)
    (d / 'rapport.md').write_text(texte, encoding='utf-8')
    print(texte)
    if '--ecrire' in sys.argv and not r['pilote']:
        import datetime
        t = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds').replace('+00:00', 'Z')
        with open(d / 'journal.jsonl', 'a', encoding='utf-8') as f:
            for e in r['verdicts'] + r['scores']:
                f.write(json.dumps({'t': t, **e}, ensure_ascii=False) + '\n')
        print(f"{len(r['verdicts'])} verdicts et {len(r['scores'])} scores ajoutés au journal")
