#!/usr/bin/env python3
"""Le tirage du duel filmé (protocole §9.6 et §15) : un patch tiré au sort, avec la graine fixée au second tag, parmi les patchs
jugés, c'est-à-dire les bugs réels dont l'analyse a rendu le verdict « passé » ou « pris ». On le montre quel qu'il soit.

    python3 harness/tirage.py campagnes/campagne

Le tirage est déterministe : la même campagne donne toujours le même patch. Il s'écrit dans le journal (`tirage`).
"""
import datetime, json, pathlib, random, sys

GRAINE = 1106  # fixée au second tag (006-gel-2)

d = pathlib.Path(sys.argv[1])
E = [json.loads(l) for l in open(d / 'journal.jsonl', encoding='utf-8') if l.strip()]
verdicts = {}
for e in E:
    if e['type'] == 'verdict':
        verdicts[e['patch']] = e  # le dernier écrit fait foi
juges = sorted(p for p, v in verdicts.items() if v['issue'] in ('passe', 'pris'))
if not juges:
    sys.exit("aucun patch jugé : l'analyse n'a pas encore écrit ses verdicts")
choisi = random.Random(GRAINE).choice(juges)
v = verdicts[choisi]
patch = next(e for e in E if e['type'] == 'patch' and e['patch'] == choisi)
session = next((e['session'] for e in E if e['type'] == 'session.debut' and e.get('construction') == patch['construction']
                and e.get('arme') == patch.get('arme') and e.get('role') == 'saboteur'), None)
t = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds').replace('+00:00', 'Z')
with open(d / 'journal.jsonl', 'a', encoding='utf-8') as f:
    f.write(json.dumps({'t': t, 'type': 'tirage', 'graine': GRAINE, 'parmi': len(juges), 'patch': choisi, 'issue': v['issue']}, ensure_ascii=False) + '\n')
print(json.dumps({'graine': GRAINE, 'parmi': len(juges), 'patch': choisi, 'arme': patch.get('arme'), 'construction': patch['construction'],
                  'issue': v['issue'], 'code_avant': patch['code_avant'], 'code_apres': patch['code_apres']}, ensure_ascii=False, indent=1))
