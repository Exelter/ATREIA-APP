"""Refits where each world map's picture lies (Client/assets/aion2/maps/maps.json: scale, offsetX, offsetY) from the minimap reader's checks against
the game's own reports: the site's export (admin: /api/admin/map-points -> map-points.json), and/or players' minimap-log.txt and minimap-calibration.json.

    python3 Tools/maps/fit_maps.py map-points.json [minimap-log.txt ...] [minimap-calibration.json ...]

Per map and axis the game's place against the reading's: game = A * reading + B (robust: checks far off the line left out, three rounds), from checks
made at a walk (8 m/s at most, under 0.4 s from the reading, a fit of 0.3 or better), each installation weighed alike. A reading is
(picture pixel - offset) / scale, so the picture lies right with scale / A and offset - scale * B / A on that axis; printed per map with how far off the
checks were before and after. Only the maps whose checks cover a good part of them (several hundred metres both ways) should be changed.

Tiles that lie off on their own (10 November 2026: on Altgard the western tiles 76 m east, Dranactus' tile 6 m, the north-eastern one 72 m north - no one
scale and offset fits them all) get a shift of their own in maps.json ("tileShift": {"column_row": [x, y]}, world units, game place minus picture place),
which the app adds to every reading in that tile and to where it draws the tile. Printed per tile as "tileShift would be": the shift in maps.json now plus
the median of what is left of the checks made with these numbers (checks of other numbers count only for the line above). Take a tile's shift only from
a few checks or more that agree (a spread of a few metres).
"""
import collections
import json
import re
import statistics as st
import sys

MAPS = json.load(open("Client/assets/aion2/maps/maps.json", encoding="utf-8"))["maps"]
pairs = collections.defaultdict(list)   # (map, stamp) -> [(rx, ry, gx, gy, who)]


def walk(spd, lag, fit):
    return spd <= 8 and abs(lag) <= 0.4 and fit >= 0.3


for f in sys.argv[1:]:
    if f.endswith(".json"):
        data = json.load(open(f, encoding="utf-8"))
        if isinstance(data, dict) and "maps" in data and isinstance(data["maps"], list):
            for m in data["maps"]:
                for p in m["points"]:
                    rx, ry, gx, gy, spd, lag, fit, inst = p[:8]
                    if walk(spd, lag, fit):
                        pairs[(m["map"], m["stamp"])].append((rx, ry, gx, gy, f"{f}#{inst}"))
        else:   # minimap-calibration.json: {map: {Stamp, Pairs: [[rx, ry, gx, gy]]}} (already only walking checks)
            for m, v in data.items():
                for p in v.get("Pairs", []):
                    pairs[(m, v.get("Stamp", ""))].append((p[0], p[1], p[2], p[3], f))
    else:
        for line in open(f, encoding="utf-8", errors="replace"):
            m = re.search(r" ([\w-]+): [\d.]+ m off .*?, ([\d.]+) m/s, ([\d.]+) s from the reading; game (-?\d+) (-?\d+), reading (-?\d+) (-?\d+)", line)
            if m and walk(float(m.group(2)), float(m.group(3)), 1):
                pairs[(m.group(1), "log")].append((float(m.group(6)), float(m.group(7)), float(m.group(4)), float(m.group(5)), f))


def line(pts):
    mx = st.mean(p[0] for p in pts)
    my = st.mean(p[1] for p in pts)
    sxx = sum((p[0] - mx) ** 2 * p[2] for p in pts)
    sxy = sum((p[0] - mx) * (p[1] - my) * p[2] for p in pts)
    a = sxy / sxx if sxx else 1.0
    return a, my - a * mx


def robust(pts):
    a, b = line(pts)
    for _ in range(3):
        res = [abs(p[1] - (a * p[0] + b)) for p in pts]
        cut = max(3 * 1.4826 * st.median(res), 100)
        kept = [p for p, r in zip(pts, res) if r <= cut]
        if len(kept) < 10:
            break
        a, b = line(kept)
    return a, b


by_map = collections.defaultdict(list)
for (m, stamp), ps in pairs.items():
    by_map[m] += ps
for m, ps in sorted(by_map.items()):
    ps = list({(round(p[0]), round(p[1]), round(p[2]), round(p[3])): p for p in ps}.values())
    who = collections.Counter(p[4] for p in ps)
    weight = {k: 1.0 / n for k, n in who.items()}   # every installation (or file) counts alike
    xs = [(p[0], p[2], weight[p[4]]) for p in ps]
    ys = [(p[1], p[3], weight[p[4]]) for p in ps]
    span = ((max(p[0] for p in ps) - min(p[0] for p in ps)) / 100, (max(p[1] for p in ps) - min(p[1] for p in ps)) / 100)
    if len(ps) >= 12:
        (ax, bx), (ay, by) = robust(xs), robust(ys)
    else:   # too few for a slope: the offset alone
        (ax, bx), (ay, by) = (1.0, st.median(p[2] - p[0] for p in ps)), (1.0, st.median(p[3] - p[1] for p in ps))
    before = [((p[2] - p[0]) ** 2 + (p[3] - p[1]) ** 2) ** 0.5 / 100 for p in ps]
    after = [((p[2] - (ax * p[0] + bx)) ** 2 + (p[3] - (ay * p[1] + by)) ** 2) ** 0.5 / 100 for p in ps]
    print(f"{m}: {len(ps)} checks from {len(who)} source(s), over {span[0]:.0f} x {span[1]:.0f} m; game = {ax:.5f} x reading {bx:+.0f}, {ay:.5f} y {by:+.0f}; "
          f"off {st.median(before):.1f} m before (worst {max(before):.1f}), {st.median(after):.1f} m after (worst {max(after):.1f})")
    # what is left per tile of the picture (a tile placed off on its own shows here): the median offset of its checks after the line
    tile = MAPS.get(m, {}).get("tile", 1024)
    if m in MAPS:
        s0, ox0, oy0 = MAPS[m]["scale"], MAPS[m]["offsetX"], MAPS[m]["offsetY"]
        cells = collections.defaultdict(list)
        for p in ps:
            cells[(int((ox0 + s0 * p[0]) // tile), int((oy0 + s0 * p[1]) // tile))].append(((p[2] - (ax * p[0] + bx)) / 100, (p[3] - (ay * p[1] + by)) / 100))
        for (c, r), d in sorted(cells.items()):
            print(f"    tile {c}_{r}: {len(d)} checks, left {st.median(x for x, _ in d):+.1f} m across, {st.median(y for _, y in d):+.1f} m down")
    if m in MAPS:
        # the tiles that lie off on their own: the median of what the game says against the reading, per tile, of the checks made with today's numbers
        shift = MAPS[m].get("tileShift", {})
        # the app's stamp of today's numbers (Aion2/MinimapCalibration.cs StampOf): with tile shifts their count and sum (column * 100 + row, |x| + |y|) follow
        stamp_now = f"2|{MAPS[m]['scale']!r}|{MAPS[m]['offsetX']!r}|{MAPS[m]['offsetY']!r}"
        if shift:
            total = sum(abs(v[0]) + abs(v[1]) + int(k.split("_")[0]) * 100 + int(k.split("_")[1]) for k, v in shift.items())
            stamp_now += f"|{len(shift)}|{total:.0f}"
        now = [p for (mm, stamp), pts in pairs.items() if mm == m and (stamp == stamp_now or (stamp == "log" and not shift)) for p in pts]
        cells = collections.defaultdict(list)
        s0, ox0, oy0 = MAPS[m]["scale"], MAPS[m]["offsetX"], MAPS[m]["offsetY"]
        for p in now:
            cells[f"{int((ox0 + s0 * p[0]) // tile)}_{int((oy0 + s0 * p[1]) // tile)}"].append((p[2] - p[0], p[3] - p[1]))
        for key, d in sorted(cells.items()):
            if len(d) >= 5:
                dx, dy = st.median(x for x, _ in d), st.median(y for _, y in d)
                mad = 1.4826 * st.median(abs(x - dx) + abs(y - dy) for x, y in d) / 100
                old = shift.get(key, [0, 0])
                print(f"    tileShift {key}: {len(d)} checks (spread {mad:.1f} m), now {old}, would be [{round(old[0] + dx, -1):.0f}, {round(old[1] + dy, -1):.0f}]")
        s, ox, oy = MAPS[m]["scale"], MAPS[m]["offsetX"], MAPS[m]["offsetY"]
        print(f"    maps.json now: scale {s}, offsetX {ox}, offsetY {oy}")
        print(f"    would be:      scaleX {s / ax!r}, scaleY {s / ay!r}, offsetX {ox - s * bx / ax!r}, offsetY {oy - s * by / ay!r}")
