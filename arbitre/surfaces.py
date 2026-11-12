#!/usr/bin/env python3
"""Mesure la surface de bugs typiques, écrits à la main sur la référence.

Sert à régler le seuil de surface du protocole (§4) et à fournir les « bugs connus » des patchs sondes (§6).
"""
import os, re, shutil, subprocess, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REF = os.path.join(HERE, "Atm.Reference")
BIN = "bin/Release/net10.0"
ARBITER = os.path.join(HERE, "Arbiter", BIN, "Arbiter.dll")
REFERENCE_DLL = os.path.join(REF, BIN, "Atm.dll")

BUGS = [
    ("montant nul accepté", "amount <= 0 ||", "amount < 0 ||"),
    ("multiple de 5 accepté", "amount % 10 != 0", "amount % 5 != 0"),
    ("plafond : > devient >=", "+ amount > DailyLimit", "+ amount >= DailyLimit"),
    ("plafond à 210 €", "DailyLimit = 200", "DailyLimit = 210"),
    ("plafond ignoré si solde élevé", "if (account.WithdrawnToday + amount > DailyLimit)", "if (account.WithdrawnToday + amount > DailyLimit && account.Balance < 200)"),
    ("frais : > devient >=", "amount > FeeThreshold", "amount >= FeeThreshold"),
    ("frais à 10 €", "FeeAmount = 5", "FeeAmount = 10"),
    ("seuil des frais à 110 €", "FeeThreshold = 100", "FeeThreshold = 110"),
    ("frais oubliés au contrôle du solde", "amount + fee > account.Balance", "amount > account.Balance"),
    ("solde : > devient >=", "amount + fee > account.Balance", "amount + fee >= account.Balance"),
    ("frais non débités", "account.Balance - amount - fee", "account.Balance - amount"),
    ("frais comptés dans le plafond", "account.WithdrawnToday + amount));", "account.WithdrawnToday + amount + fee));"),
    ("retrait non compté dans le plafond", "account.WithdrawnToday + amount));", "account.WithdrawnToday));"),
    ("frais non rendus dans le résultat", "            plan,\n            fee,", "            plan,\n            0,"),
    ("cassette de 10 bloquée ignorée", "Denomination.Ten => stock with { Tens = 0 }", "Denomination.Ten => stock"),
    ("le plus de billets au lieu du moins", "candidate.Total < best.Value.Total", "candidate.Total > best.Value.Total"),
    ("première combinaison trouvée", "best is null || candidate.Total < best.Value.Total", "best is null"),
    ("5 billets par passage (panne du module)", "var fifties = Math.Min(left.Fifties, MoneyDispenser.MaxNotesPerPass);\n        var twenties = Math.Min(left.Twenties, MoneyDispenser.MaxNotesPerPass - fifties);\n        var tens = Math.Min(left.Tens, MoneyDispenser.MaxNotesPerPass - fifties - twenties);", "var fifties = Math.Min(left.Fifties, 5);\n        var twenties = Math.Min(left.Twenties, 5 - fifties);\n        var tens = Math.Min(left.Tens, 5 - fifties - twenties);"),
    ("dépôt non crédité", "Balance = account.Balance + notes.Value", "Balance = account.Balance"),
    ("dépôt : cassette pleine trop tôt", "after.Tens > MoneyDispenser.CassetteCapacity", "after.Tens >= MoneyDispenser.CassetteCapacity"),
    ("dépôt : cassette bloquée ignorée", "if (dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)", "if (false && dispenser.Jammed is { } jammed && notes.Of(jammed) > 0)"),
    ("ordre : solde contrôlé avant le plafond", None, None),
]
ORDER_OLD = re.compile(r"(        if \(account\.WithdrawnToday \+ amount > DailyLimit\)\n.*?\n        }\n\n)(        var fee = .*?\n        if \(amount \+ fee > account\.Balance\)\n.*?\n        }\n\n)", re.S)


def build_candidate(source):
    root = tempfile.mkdtemp(prefix="sonde-")
    shutil.copy(os.path.join(REF, "Atm.Reference.csproj"), root)
    csproj = os.path.join(root, "Atm.Reference.csproj")
    contract = os.path.normpath(os.path.join(HERE, "..", "fourni", "Atm.Contract", "Atm.Contract.csproj"))
    text = open(csproj).read().replace("../../fourni/Atm.Contract/Atm.Contract.csproj", contract)
    open(csproj, "w").write(text)
    open(os.path.join(root, "CashMachine.cs"), "w").write(source)
    run = subprocess.run(["dotnet", "build", root, "-c", "Release", "-v", "q"], capture_output=True, text=True)
    return root, run


source = open(os.path.join(REF, "CashMachine.cs")).read()
rows = []
for name, old, new in BUGS:
    if old is None:
        mutated, count = ORDER_OLD.subn(lambda m: m.group(2) + m.group(1), source, count=1)
    else:
        count = source.count(old)
        mutated = source.replace(old, new, 1)
    if count == 0:
        rows.append((None, name, "motif introuvable"))
        continue
    root, built = build_candidate(mutated)
    if built.returncode != 0:
        rows.append((None, name, "ne compile pas"))
    else:
        out = subprocess.run(["dotnet", ARBITER, "compare", REFERENCE_DLL, os.path.join(root, BIN, "Atm.dll")], capture_output=True, text=True).stdout
        rows.append((float(re.search(r"surface=([\d.]+)%", out).group(1)), name, ""))
    shutil.rmtree(root, ignore_errors=True)

rows.sort(key=lambda row: (row[0] is None, -(row[0] or 0)))
for surface, name, note in rows:
    print(f"{'  ?   ' if surface is None else f'{surface:6.3f}%'}  {name} {note}")
measured = [r[0] for r in rows if r[0] is not None]
for threshold in (5, 1, 0.1):
    print(f"≥ {threshold} % : {sum(s >= threshold for s in measured)}/{len(measured)}")
