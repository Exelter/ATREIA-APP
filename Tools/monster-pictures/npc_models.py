"""The model of every monster, for its picture (Client/assets/aion2/npcs/npc_models.json).

The game has a picture of a monster's model only for some of them: the pets (a pet's key is the model of its monster: KrallWar_01) and the named
monsters (bosses/field/mob_<model>.png). Which model a monster wears is not in any table the app has, but the game's spawn lists name their spawns
after it: "M_AR1_RR_DrakanElite_01_10" spawns Abyss Elite Priests, "M_D1_WGC_AltKnife_05_01_42" Blades Elite Sentinels. A monster gets the model most of
of its spawns that name one are named after (at least 60 % of them: a monster now and then spawned in another kind's group keeps its own; one spawned
as often with another kind, a Krall scout in its Karnifs' groups, gets none), and none when no spawn names one.

Usage: python3 npc_models.py <spawn export .json> <Client/assets/aion2/npcs/npc_models.json>
The export is the game client's spawn lists (MapData.dat SpawnInfoList) and NPC table (NpcData.dat) as JSON:
  {"maps": {"<map id>": [["<spawn name>", <npc id>, x, y, z, ...], ...]}, "templates": {"<npc id>": {"type": "Monster", ...}}}
Only monsters ("type": "Monster") are kept.
"""
import json
import re
import sys
from collections import Counter, defaultdict

# words of spawn names that are no model ("Draconic_001", "Wave_01", "Altgard_..."): the model is a word in capitals then _NN (and its colour, _V01/_CV01)
NOT_MODELS = {
    'multi', 'single', 'wave', 'boss', 'midboss', 'common', 'com', 'draconic', 'awaken', 'altgard', 'verteron', 'light', 'dark', 'mob', 'npc', 'bsz',
    'squad', 'dungeon', 'monster', 'turret', 'trap', 'quest', 'event', 'world', 'field', 'abyss', 'defence', 'defense', 'spawn', 'group', 'test',
    'dummy', 'random', 'elite', 'normal', 'named', 'hero', 'phase', 'step', 'stage', 'area', 'zone', 'camp', 'base', 'fort', 'gate', 'door',
}
TOKEN = re.compile(r'(?<![A-Za-z0-9])([A-Z][A-Za-z]{2,})_(\d{2})(?:_(C?V\d{2}))?(?![0-9])')
# a squad's spawns name their models without the line: "Squad_AR1_RR_Drakan01_DrakanElite02" (the first is taken, as for a single model)
SQUAD = re.compile(r'(?<![A-Za-z0-9])([A-Z][a-z]+(?:[A-Z][a-z]+)*)(\d{2})(?![0-9])')


def model_in(spawn_name):
    for m in TOKEN.finditer(spawn_name):
        if m.group(1).lower() in NOT_MODELS:
            continue
        return (m.group(1) + '_' + m.group(2) + ('_' + m.group(3) if m.group(3) else '')).lower()
    if spawn_name.startswith('Squad_'):
        for m in SQUAD.finditer(spawn_name):
            if len(m.group(1)) >= 4 and m.group(1).lower() not in NOT_MODELS:
                return (m.group(1) + '_' + m.group(2)).lower()
    return None


def build(export):
    monsters = {int(k) for k, v in (export.get('templates') or {}).items() if isinstance(v, dict) and v.get('type') == 'Monster'}
    named = defaultdict(Counter)
    for rows in export['maps'].values():
        for row in rows:
            if not (isinstance(row, list) and len(row) > 1 and isinstance(row[0], str) and isinstance(row[1], int)):
                continue
            npc = row[1]
            if monsters and npc not in monsters:
                continue
            model = model_in(row[0])
            if model:
                named[npc][model] += 1
    models = {}
    for npc, counts in named.items():
        model, n = counts.most_common(1)[0]
        if n >= 0.6 * sum(counts.values()):
            models[str(npc)] = model
    return models


def main(source, target):
    with open(source, encoding='utf-8') as f:
        models = build(json.load(f))
    out = {'_readme': 'npc id -> the model of the monster, from the names of its spawns in the game client (Tools/monster-pictures/npc_models.py)',
           'models': dict(sorted(models.items(), key=lambda kv: int(kv[0])))}
    with open(target, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(out, f, ensure_ascii=False, separators=(',', ':'))
        f.write('\n')
    print(f'{len(models)} monsters with a model -> {target}')


if __name__ == '__main__':
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
