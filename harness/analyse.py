#!/usr/bin/env python3
"""Le script d'analyse : le seul endroit où un verdict et un score se calculent (protocole §6 et §9).

    python3 harness/analyse.py journal/journal.jsonl

Il lit le journal et y ajoute un `verdict` par patch (passé, pris, ou non conforme si l'arbitre ne voit aucun bug) et un `score` par
manche, plus le total (manche 0). Ici, la détection est simplifiée aux sondes : une seule suite, prise si un test rougit.
"""
import datetime, json, sys

chemin = sys.argv[1]
evenements = [json.loads(l) for l in open(chemin, encoding='utf-8') if l.strip()]
patchs = {e['patch']: e for e in evenements if e['type'] == 'patch'}
arbitre = {e['patch']: e for e in evenements if e['type'] == 'arbitre'}
suites = {e['patch']: e for e in evenements if e['type'] == 'suite'}
campagne = next((e for e in evenements if e['type'] == 'campagne'), {})
t = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds').replace('+00:00', 'Z')

sortie, scores = [], {}
for pid, p in patchs.items():
    a, s = arbitre.get(pid), suites.get(pid)
    if not a or not s:
        continue
    nom = p.get('nom', pid)
    if not a['bug']:
        issue, recit = 'non_conforme', f"{nom} : l'arbitre ne voit aucun écart, ce n'est pas un bug."
    elif s['pris']:
        issue, recit = 'pris', f"{nom} : un test de la suite l'a vu."
    else:
        issue, recit = 'passe', f"{nom} : aucun test de la suite ne l'a vu."
    sortie.append({'t': t, 'type': 'verdict', 'patch': pid, 'manche': p['manche'], 'issue': issue, 'recit': recit})
    if issue != 'non_conforme':
        sc = scores.setdefault(p['manche'], {'passes': 0, 'pris': 0})
        sc['passes' if issue == 'passe' else 'pris'] += 1

prevues = campagne.get('sessions_prevues', 0)
for manche in range(1, 7):
    sc = scores.get(manche, {'passes': 0, 'pris': 0})
    n = sc['passes'] + sc['pris']
    # Des sondes : il n'y a pas de tentatives prévues, chaque sonde jugée compte.
    sortie.append({'t': t, 'type': 'score', 'manche': manche, **sc, 'tentatives': n, 'prevues': n if campagne.get('sondes') else (prevues if manche in scores else 0)})
total = {'passes': sum(s['passes'] for s in scores.values()), 'pris': sum(s['pris'] for s in scores.values())}
sortie.append({'t': t, 'type': 'score', 'manche': 0, **total, 'tentatives': total['passes'] + total['pris'], 'prevues': total['passes'] + total['pris'] if campagne.get('sondes') else prevues})
with open(chemin, 'a', encoding='utf-8') as f:
    for e in sortie:
        f.write(json.dumps(e, ensure_ascii=False) + '\n')
print(f"{len([e for e in sortie if e['type'] == 'verdict'])} verdicts ; passés {total['passes']}, pris {total['pris']}")
