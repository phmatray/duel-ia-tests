#!/bin/sh
# Le test technique du lanceur (une seule session, hors expérience) : il écrit dans journal/technique.jsonl, jamais dans le
# journal de la campagne. Usage : sh harness/test-technique.sh
set -e
ICI=$(cd "$(dirname "$0")" && pwd)
TRAVAIL=$(mktemp -d)/travail
J="$ICI/../journal/technique.jsonl"
printf '{"t":"%s","type":"campagne","nom":"test technique du lanceur","modele":"claude-opus-5-5","sessions_prevues":1,"fictif":false,"pilote":false,"technique":true}\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$J"
python3 "$ICI/lanceur.py" --session 1 --role technique --prompt "$ICI/prompts/test-technique.md" --travail "$TRAVAIL" --tours-max 12 --journal "$J"
echo "ok.txt : $(cat "$TRAVAIL/ok.txt" 2>/dev/null || echo absent)"
echo "transcription : $(dirname "$TRAVAIL")/session-1.stream.jsonl"
