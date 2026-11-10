"""Measures the boss mechanics of Abyssal Forge: Ludra (Rahu, Ketu, the Fusion Reactor, Eternal Ludra and the objects they leave on the floor) from
debug recordings read by Tools/mechanics/recording.py, and writes Client/assets/aion2/bosses/mechanic_areas.json, what the replay draws and the cast
bar counts down with. Run from the repository's root:

    python3 Tools/mechanics/fit_areas.py rec-1.pkl rec-2.pkl ...

For every windup of a monster in those fights: who it hit (damage or a status from the same caster, with that skill or one of its family, from 0.4 s
before the windup's end to 2.5 s after) and where every player of the group stood at the first hit (or 0.3 s after the end, when it hit nobody), around
the windup's own place (the spot it aims at) and its facing (degrees, atan2 of y and x: the windup's place lies ahead of the caster along it). The shape
is the game client's own word for the skill (assets/aion2/skills/skill_shapes.json); its size is the one that best tells the players it hit from the ones
it missed (F1 of the hits). Without a word: a circle, unless a ring, a cone or a line is clearly better. Kept only when the fit is good (F1 0.55 or more)
or the skill hit nearly everybody (the whole room); the time from the windup's end to the first hit is kept for every skill seen land four times.
Objects that hit players around themselves without winding up (the hazards: the pools, totems and orbs a boss leaves) are measured the same way around
the object, with how long they stay.
"""
import bisect
import collections
import json
import math
import pickle
import statistics as st
import sys

LUDRA = {2301006: 'Isolation Specialist Rahu', 2301007: 'Abyssal Wings Ketu', 2301008: 'Central Abyss Fusion Reactor', 2301014: 'Eternal Ludra'}
SHAPES = json.load(open('Client/assets/aion2/skills/skill_shapes.json', encoding='utf-8'))
MIN_F1, MIN_HAZARD_F1 = 0.55, 0.7


def angdiff(a, b):
    return (a - b + 180) % 360 - 180


class Tracks:
    def __init__(self, pos):
        self.t, self.x, self.y = {}, {}, {}
        for e, pts in pos.items():
            pts.sort()
            self.t[e] = [p[0] for p in pts]
            self.x[e] = [p[1] for p in pts]
            self.y[e] = [p[2] for p in pts]

    def at(self, e, t, hold=20.0, gap=6.0):
        ts = self.t.get(e)
        if not ts:
            return None
        i = bisect.bisect_right(ts, t)
        if i == 0:
            return (self.x[e][0], self.y[e][0]) if ts[0] - t < 1.0 else None
        t0, x0, y0 = ts[i - 1], self.x[e][i - 1], self.y[e][i - 1]
        if i < len(ts) and ts[i] - t0 <= gap:
            f = (t - t0) / (ts[i] - t0) if ts[i] > t0 else 0
            return (x0 + (self.x[e][i] - x0) * f, y0 + (self.y[e][i] - y0) * f)
        return (x0, y0) if t - t0 <= hold else None


def gather(paths):
    win, haz, life = collections.defaultdict(list), collections.defaultdict(list), collections.defaultdict(list)
    for path in paths:
        d = pickle.load(open(path, 'rb'))
        tr = Tracks(d['pos'])
        npc_of, spawn_at = {}, {}
        for e, l in d['spawn'].items():
            for (t, npc, x, y) in l:
                npc_of[e] = npc
                spawn_at[e] = t
        bosses = {e for e, n in npc_of.items() if n in LUDRA}
        players = {a for (t, a, v, sk, amt) in d['dmg'] if v in bosses and a not in npc_of and len(tr.t.get(a, [])) >= 20}
        times = sorted(t for (t, a, v, sk, amt) in d['dmg'] if v in bosses or a in bosses)

        def in_fight(t):
            i = bisect.bisect_left(times, t - 15)
            return i < len(times) and times[i] <= t + 15

        hits = collections.defaultdict(list)
        for (t, a, v, sk, amt) in d['dmg']:
            if v in players and a in npc_of and 1_000_000 <= sk < 10_000_000:
                hits[a].append((t, v, sk))
        for (t, c, v, eff, dur) in d['buff']:
            if v in players and c in npc_of and 1_000_000 <= eff // 10 < 10_000_000:
                hits[c].append((t, v, eff // 10))
        for a in hits:
            hits[a].sort()
        removed = {e: t for (t, e, r) in d['remove']}
        for e, t in spawn_at.items():
            if e in removed and in_fight(t):
                life[npc_of[e]].append(removed[e] - t)
        used = set()
        for (t0, actor, skill, target, ms, facing, x, y, has) in d['windup']:
            if not has or actor not in npc_of or not in_fight(t0) or not 1_000_000 <= skill < 10_000_000:
                continue
            end = t0 + ms / 1000
            got = [h for h in hits.get(actor, []) if end - 0.4 <= h[0] <= end + 2.5 and (h[2] == skill or h[2] // 10 == skill // 10)]
            used.update((actor,) + h for h in got)
            when = min((h[0] for h in got), default=end + 0.3)
            victims = {h[1] for h in got}
            pts = [(p[0] - x, p[1] - y, q in victims) for q in players for p in [tr.at(q, when)] if p]
            if len(pts) >= 3:
                win[(npc_of[actor], skill)].append(dict(f=facing, pts=pts, land=(when - end) if got else None, ms=ms))
        for a, hs in hits.items():
            if npc_of[a] in LUDRA:
                continue
            groups = []
            for h in hs:
                if (a,) + h in used:
                    continue
                if groups and h[0] - groups[-1][-1][0] <= 0.4 and h[2] == groups[-1][-1][2]:
                    groups[-1].append(h)
                else:
                    groups.append([h])
            for g in groups:
                c = tr.at(a, g[0][0], hold=600, gap=60)
                if not c:
                    continue
                victims = {h[1] for h in g}
                pts = [(p[0] - c[0], p[1] - c[1], q in victims) for q in players for p in [tr.at(q, g[0][0])] if p]
                if len(pts) >= 3:
                    haz[(npc_of[a], g[0][2])].append(pts)
    return win, haz, life


def f1(rows, pred):
    tp = fp = fn = 0
    for r in rows:
        p = pred(r)
        if p and r[2]:
            tp += 1
        elif p:
            fp += 1
        elif r[2]:
            fn += 1
    return (2 * tp / (2 * tp + fp + fn)) if tp else 0.0


def dist(r):
    return math.hypot(r[0], r[1])


def ang(r):
    return abs(angdiff(math.degrees(math.atan2(r[1], r[0])), r[3]))


def along(r):
    th = math.radians(r[3])
    return r[0] * math.cos(th) + r[1] * math.sin(th), -r[0] * math.sin(th) + r[1] * math.cos(th)


def candidates(word):
    if word in ('circle', 'target'):
        for R in range(150, 7001, 50):
            yield {'radius': R}, (lambda R: lambda r: dist(r) <= R)(R)
    elif word in ('cone', 'triangle'):
        for A in (15, 22, 30, 45, 60, 75, 90, 120, 150):
            for R in range(300, 7001, 100):
                yield {'radius': R, 'half': A}, (lambda A, R: lambda r: dist(r) <= R and ang(r) <= A)(A, R)
    elif word == 'line':
        for W in (100, 150, 200, 300, 400, 500, 700, 1000):
            for L in range(400, 7001, 200):
                yield {'length': L, 'width': W, 'behind': W}, (lambda W, L: lambda r: -W <= along(r)[0] <= L and abs(along(r)[1]) <= W)(W, L)
    elif word == 'cross':
        for W in (100, 150, 200, 300, 400, 500, 700):
            for L in range(400, 7001, 200):
                yield {'length': L, 'width': W}, (lambda W, L: lambda r: (abs(along(r)[0]) <= L and abs(along(r)[1]) <= W) or (abs(along(r)[1]) <= L and abs(along(r)[0]) <= W))(W, L)
    elif word in ('ring', 'ringarc'):
        for Rin in range(200, 4001, 100):
            for Rout in range(Rin + 200, 7001, 100):
                if word == 'ring':
                    yield {'inner': Rin, 'radius': Rout}, (lambda a, b: lambda r: a <= dist(r) <= b)(Rin, Rout)
                else:
                    for A in (45, 60, 90, 120):
                        yield {'inner': Rin, 'radius': Rout, 'half': A}, (lambda a, b, A: lambda r: a <= dist(r) <= b and ang(r) <= A)(Rin, Rout, A)


def word_of(skill):
    if str(skill) in SHAPES:
        return SHAPES[str(skill)]
    base = skill // 10 * 10
    family = {SHAPES[str(base + v)] for v in range(10) if str(base + v) in SHAPES}
    return family.pop() if len(family) == 1 else None


def main(paths):
    win, haz, life = gather(paths)
    areas, hazards, objects = {}, {}, set()
    for (npc, skill), ss in sorted(win.items()):
        rows = [(dx, dy, h, s['f']) for s in ss for (dx, dy, h) in s['pts']]
        hit = sum(r[2] for r in rows)
        lands = [s['land'] for s in ss if s['land'] is not None]
        if hit < 5 or len(lands) < 4:
            continue
        entry = {'landMs': round(st.median(lands) * 1000), 'seen': len(lands)}
        word = word_of(skill)
        if word == 'wide' or (word is None and hit / len(rows) >= 0.85):
            entry['shape'] = 'room'
        else:
            best = None
            for w in ([word] if word else ['circle', 'cone', 'line', 'ring']):
                for par, pred in candidates(w):
                    score = f1(rows, pred) + (0.05 if w == 'circle' and not word else 0)
                    if best is None or score > best[0]:
                        best = (score, w, par)
            score, w, par = best
            score -= 0.05 if w == 'circle' and not word else 0
            if score >= MIN_F1:
                entry.update({'shape': 'circle' if w == 'target' else w, **par})
                if w == 'target':
                    entry['onTarget'] = True
                entry['fit'] = round(score, 2)
        areas[str(skill)] = entry
        if npc >= 2_920_000:   # an object a boss summons (the followers and other monsters of the run are not)
            objects.add(npc)
    for (npc, skill), groups in sorted(haz.items()):
        rows = [(dx, dy, h, 0) for g in groups for (dx, dy, h) in g]
        if len(groups) < 8 or sum(r[2] for r in rows) < 8 or npc < 2_920_000 or skill // 10_000 == 123:   # 123xxxx: a follower's skill (an id seen again in another run)
            continue
        score, radius = max((f1(rows, lambda r: dist(r) <= R), R) for R in range(100, 4001, 50))
        if score < MIN_HAZARD_F1 or not life.get(npc):
            continue
        h = hazards.setdefault(str(npc), {'radius': radius, 'seconds': round(st.median(life[npc]), 1), 'skills': [], 'seen': 0, 'fit': round(score, 2)})
        h['skills'].append(skill)
        h['seen'] += len(groups)
        objects.add(npc)
    out = {
        '_note': [
            'Boss mechanics of Abyssal Forge: Ludra as they land, measured from 31 pulls of Eternal Ludra and the other bosses of the run (debug recordings of',
            '7 and 8 November 2026, Tools/mechanics/fit_areas.py). areas: per skill a monster winds up, its shape on the floor around the spot the windup aims at',
            '(circle; ring: inner to radius; cone: radius and half the angle around the facing; line: from behind the spot to length ahead of it, width to each side;',
            'cross: two such bars of length each way; room: everybody), sizes in centimetres, onTarget: the spot is the targeted player, landMs: the time from the end',
            'of the windup to the first hit, seen: how many landings it rests on, fit: how well the shape told the players it hit from the ones it missed (F1).',
            'A skill without a shape here keeps the game client\'s word (skill_shapes.json) and the size the meter learns. hazards: per object a boss leaves on the floor,',
            'the radius it hits players in, how long it stays (seconds) and its skills. objects: every such object (the replay follows them).'],
        'areas': areas,
        'hazards': hazards,
        'objects': sorted(objects),
    }
    with open('Client/assets/aion2/bosses/mechanic_areas.json', 'w', encoding='utf-8') as f:
        json.dump(out, f, indent=1)
        f.write('\n')
    print(f'{len(areas)} skills ({sum(1 for a in areas.values() if "shape" in a)} with a shape), {len(hazards)} hazards, {len(objects)} objects')


if __name__ == '__main__':
    main(sys.argv[1:])
