#!/usr/bin/env python3
"""Le lanceur d'une session du duel (protocole §3) : un `claude -p` neuf, dans un conteneur sans rien de l'hôte, dont la sortie
devient des événements du journal (journal/FORMAT.md).

    python3 harness/lanceur.py --session N --role ROLE --prompt FICHIER --travail DOSSIER [--tours-max 40] [--manche M]
            [--construction C01] [--arme A1] [--journal journal/journal.jsonl] [--modele claude-opus-5-5] [--effort high]

Le jeton de l'abonnement (`claude setup-token`) est lu dans ~/.config/banc-006/token (chmod 600), jamais dans le dépôt, et passé
au conteneur par une variable d'environnement. Le dossier de travail est le seul montage : ni l'arbitre ni le journal n'y sont.
Le budget est en tours : au-delà de --tours-max, la session est arrêtée et finit en panne « budget ».
"""
import argparse, datetime, json, os, pathlib, subprocess, sys

IMAGE = 'banc-006:2.1.318'
JETON = pathlib.Path.home() / '.config' / 'banc-006' / 'token'
OUTILS = 'Bash,Read,Edit,Write,Glob,Grep'


def maintenant():
    return datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds').replace('+00:00', 'Z')


def main():
    a = argparse.ArgumentParser()
    a.add_argument('--session', type=int, required=True)
    a.add_argument('--role', required=True)
    a.add_argument('--prompt', required=True)
    a.add_argument('--travail', required=True)
    a.add_argument('--tours-max', type=int, default=40)
    a.add_argument('--manche', type=int)
    a.add_argument('--construction')
    a.add_argument('--arme')
    a.add_argument('--journal', default=str(pathlib.Path(__file__).resolve().parents[1] / 'journal' / 'journal.jsonl'))
    a.add_argument('--modele', default='claude-opus-5-5')
    a.add_argument('--effort', default='high')
    a.add_argument('--image', default=IMAGE)
    o = a.parse_args()

    def ecrire(**e):
        with open(o.journal, 'a', encoding='utf-8') as f:
            f.write(json.dumps({'t': maintenant(), **e}, ensure_ascii=False) + '\n')

    if not JETON.exists() or JETON.stat().st_mode & 0o077:
        sys.exit(f'jeton absent ou trop ouvert : {JETON} (claude setup-token, puis chmod 600)')
    travail = pathlib.Path(o.travail).resolve()
    travail.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, CLAUDE_CODE_OAUTH_TOKEN=JETON.read_text().strip())
    commande = ['docker', 'run', '--rm', '-i', '-e', 'CLAUDE_CODE_OAUTH_TOKEN', '-e', 'NUGET_PACKAGES=/nuget', '-v', 'banc-006-nuget:/nuget',
                '-v', f'{travail}:/travail', o.image,
                'claude', '-p', '--safe-mode', '--no-session-persistence', '--strict-mcp-config', '--disable-slash-commands',
                '--dangerously-skip-permissions', '--tools', OUTILS, '--model', o.modele, '--effort', o.effort,
                '--output-format', 'stream-json', '--verbose']

    ecrire(type='session.debut', session=o.session, role=o.role, construction=o.construction, arme=o.arme, manche=o.manche, tours_max=o.tours_max)
    brut = open(travail.parent / f'session-{o.session}.stream.jsonl', 'w', encoding='utf-8')  # la transcription, hors du travail
    p = subprocess.Popen(commande, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, env=env)
    p.stdin.write(pathlib.Path(o.prompt).read_text())
    p.stdin.close()
    tours, fin, vus = 0, None, set()
    for ligne in p.stdout:
        brut.write(ligne)
        try:
            m = json.loads(ligne)
        except json.JSONDecodeError:
            continue
        if m.get('type') == 'assistant':
            ident = m.get('message', {}).get('id')
            if ident not in vus:
                vus.add(ident)
                tours += 1
                ecrire(type='session.tour', session=o.session, tours=tours)
            for bloc in m.get('message', {}).get('content', []):
                if bloc.get('type') == 'tool_use':
                    entree = bloc.get('input', {})
                    texte = entree.get('command') or entree.get('file_path') or entree.get('pattern') or bloc.get('name')
                    ecrire(type='session.action', session=o.session, texte=f"{bloc.get('name')} : {str(texte).splitlines()[0][:80]}")
            if tours >= o.tours_max:
                p.kill()
                fin = ('budget', 'budget de tours atteint')
                break
        elif m.get('type') == 'result':
            fin = ('ok', None) if not m.get('is_error') else ('panne', str(m.get('result') or m.get('subtype'))[:120])
    p.wait()
    brut.close()
    if fin is None:
        fin = ('panne', (p.stderr.read() or f'code {p.returncode}').strip().splitlines()[-1][:120] if p.stderr else f'code {p.returncode}')
    ecrire(type='session.fin', session=o.session, statut=fin[0], tours=tours, **({'raison': fin[1]} if fin[1] else {}))
    print(json.dumps({'session': o.session, 'statut': fin[0], 'tours': tours, 'raison': fin[1]}, ensure_ascii=False))


if __name__ == '__main__':
    main()
