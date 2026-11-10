"""Refits where each world map's picture lies (Client/assets/aion2/maps/maps.json: scale, offsetX, offsetY) from the minimap reader's checks against
the game's own reports: the site's export (admin: /api/admin/map-points?since=2026-11-01&limit=20000&perInstall=100000 -> map-points.json), and/or
players' minimap-log.txt and minimap-calibration.json.

    python3 Tools/maps/fit_maps.py map-points.json [minimap-log.txt ...] [minimap-calibration.json ...] [--offset-only=verteron,...]

Checks made with earlier numbers (their "stamp") are read again with today's maps.json, so a refit builds on everything collected so far; the log
lines carry no numbers and are taken as made with today's.

Every PC reads off by an amount of its own (10 November 2026: 0 to 30 m, the same all over a map; the owner's PC about 30 m, three others the same),
from its screen and minimap layout - the resolution, the window size, the minimap's zoom. Fitted as one map against everyone's checks without that, a
PC's own offset is taken for the map's, and the checks of one PC alone look like a map whose tiles lie off in all directions (they did: the first
refit from the owner's checks shifted Altgard's tiles by up to 80 m, which everyone's checks did not bear out). So each axis is fitted as

    game = A * reading + B + offset[setup]

where a setup is one installation with one window size and minimap zoom (the "setup" the app sends with its checks from 1.21.8; before that, the
installation alone), each setup weighed alike, robust (checks far off the line left out), the setups' offsets put to a median of zero (the map is
right for a typical PC; each PC's own offset is what the app's calibration takes off on that PC within minutes). A reading is
(picture pixel - offset) / scale, so the picture lies right with scale / A and offset - scale * B / A on that axis.

Printed per map: the fit, the new numbers, every setup's own offset (and the resolutions' together: an offset shared by everyone with one resolution
points at the app's minimap layout for that resolution), and the tests that say how far to trust it: each setup left out in turn and fitted from the
others, its error as a new PC sees it and once its own offset is known (that is the map's own error), against the old numbers the same two ways; and
the checks of the later half of the days against a fit of the earlier half. Take a map's new numbers only when every setup with enough checks gets
better left out, and the map's own error is a few metres. With checks in a small part of a map only, fit its offset alone (--offset-only): a scale
from a few hundred metres is a guess.

A tile that lies off on its own can still get a shift of its own in maps.json ("tileShift": {"column_row": [x, y]}, world units, game place minus
picture place). Printed per tile: what is left there once every setup's own offset is taken off, from the setups that played there; take a tile's
shift only when several setups agree on it.
"""
import collections
import json
import math
import random
import re
import statistics as st
import sys

MAPS = json.load(open("Client/assets/aion2/maps/maps.json", encoding="utf-8"))["maps"]
MIN_SETUP = 20   # checks a setup needs to count for the offsets' median and the tests


def walk(spd, lag, fit):
    return spd <= 8 and abs(lag) <= 0.4 and fit >= 0.3


def load(files):
    """(map, stamp) -> [(rx, ry, gx, gy, setup, day)]"""
    pairs = collections.defaultdict(list)
    for f in files:
        if f.endswith(".json"):
            data = json.load(open(f, encoding="utf-8-sig"))
            if isinstance(data, dict) and isinstance(data.get("maps"), list):
                for m in data["maps"]:
                    for p in m["points"]:
                        rx, ry, gx, gy, spd, lag, fit, inst = p[:8]
                        day = p[8] if len(p) > 8 else ""
                        setup = p[10] if len(p) > 10 and p[10] else "?"
                        if walk(spd, lag, fit):
                            pairs[(m["map"], m["stamp"])].append((rx, ry, gx, gy, f"{inst}|{setup}", day))
            else:   # minimap-calibration.json: {map(@window): {Stamp, Pairs: [[rx, ry, gx, gy]]}} (already only walking checks)
                for key, v in data.items():
                    for p in v.get("Pairs", []):
                        pairs[(key.split("@")[0], v.get("Stamp", ""))].append((p[0], p[1], p[2], p[3], f"{f}|{key}", ""))
        else:
            for line in open(f, encoding="utf-8", errors="replace"):
                m = re.search(r" ([\w-]+): [\d.]+ m off .*?, ([\d.]+) m/s, ([\d.]+) s from the reading; game (-?\d+) (-?\d+), reading (-?\d+) (-?\d+)", line)
                if m and walk(float(m.group(2)), float(m.group(3)), 1):
                    pairs[(m.group(1), "log")].append((float(m.group(6)), float(m.group(7)), float(m.group(4)), float(m.group(5)), f, ""))
    return pairs


def line(pts):
    w = sum(p[2] for p in pts)
    mx = sum(p[0] * p[2] for p in pts) / w
    my = sum(p[1] * p[2] for p in pts) / w
    sxx = sum((p[0] - mx) ** 2 * p[2] for p in pts)
    sxy = sum((p[0] - mx) * (p[1] - my) * p[2] for p in pts)
    a = sxy / sxx if sxx else 1.0
    return a, my - a * mx


def robust(pts):
    a, b = line(pts)
    for _ in range(3):
        res = [abs(p[1] - (a * p[0] + b)) for p in pts]
        cut = max(3 * 1.4826 * st.median(res), 300)
        kept = [p for p, r in zip(pts, res) if r <= cut]
        if len(kept) < 20:
            break
        a, b = line(kept)
    return a, b


def joint(ps, axis, offset_only):
    """game = a * reading + b + offset[setup] on one axis (0: x, 1: y); the setups' offsets have a median of zero."""
    r, g = axis, axis + 2
    by = collections.defaultdict(list)
    for p in ps:
        by[p[4]].append(p)
    weight = {k: 1.0 / len(v) for k, v in by.items()}
    off = collections.defaultdict(float)
    a, b = 1.0, 0.0
    for _ in range(8):
        if offset_only:
            a, b = 1.0, st.median(st.median(p[g] - p[r] - off[k] for p in v) for k, v in by.items())
        else:
            a, b = robust([(p[r], p[g] - off[p[4]], weight[p[4]]) for p in ps])
        off = collections.defaultdict(float, {k: st.median(p[g] - (a * p[r] + b) for p in v) for k, v in by.items()})
        big = [off[k] for k, v in by.items() if len(v) >= MIN_SETUP] or list(off.values())
        centre = st.median(big)
        off = collections.defaultdict(float, {k: v - centre for k, v in off.items()})
        b += centre
    return a, b, off


def error(p, fx, fy, own=(0.0, 0.0)):
    return math.hypot(p[2] - (fx[0] * p[0] + fx[1]) - own[0], p[3] - (fy[0] * p[1] + fy[1]) - own[1]) / 100


def report(m, ps, offset_only):
    ps = list({(round(p[0]), round(p[1]), round(p[2]), round(p[3]), p[4]): p for p in ps}.values())
    by = collections.defaultdict(list)
    for p in ps:
        by[p[4]].append(p)
    span = ((max(p[0] for p in ps) - min(p[0] for p in ps)) / 100, (max(p[1] for p in ps) - min(p[1] for p in ps)) / 100)
    ax, bx, ox = joint(ps, 0, offset_only)
    ay, by_, oy = joint(ps, 1, offset_only)
    print(f"{m}: {len(ps)} checks from {len(by)} setup(s), over {span[0]:.0f} x {span[1]:.0f} m; game = {ax:.5f} x reading {bx:+.0f}, {ay:.5f} y {by_:+.0f}"
          f"{' (offset only)' if offset_only else ''}")
    old = ((1.0, 0.0), (1.0, 0.0))
    print("    setup (installation|window|zoom): checks, own offset x y (m)")
    for k, v in sorted(by.items(), key=lambda kv: -len(kv[1])):
        print(f"      {k}: {len(v)}, {ox[k] / 100:+.0f} {oy[k] / 100:+.0f}")
    # per resolution (window size): the setups' offsets in metres and in minimap pixels (metres / the setup's metres a pixel), what
    # Client/assets/aion2/maps/minimap_setups.json takes ("offsets": {"2560x1440": [x px, y px]}) when several setups of one size agree
    res = collections.defaultdict(list)
    for k, v in by.items():
        parts = k.split("|")
        if len(v) >= MIN_SETUP and len(parts) >= 3 and parts[1] != "?":
            zoom = float(parts[2]) if parts[2] else 0
            res[parts[1]].append((ox[k], oy[k], ox[k] / 100 / zoom if zoom else None, oy[k] / 100 / zoom if zoom else None))
    for window, offs in sorted(res.items()):
        px = [o for o in offs if o[2] is not None]
        print(f"    resolution {window}: {len(offs)} setup(s), offset {st.median(o[0] for o in offs) / 100:+.0f} {st.median(o[1] for o in offs) / 100:+.0f} m"
              + (f", {st.median(o[2] for o in px):+.1f} {st.median(o[3] for o in px):+.1f} minimap px (spread {max(o[2] for o in px) - min(o[2] for o in px):.1f} x"
                 f" {max(o[3] for o in px) - min(o[3] for o in px):.1f} px)" if px else ""))
    # each setup left out: its error as a new PC sees it, and with its own offset taken off (the map's own error); the old numbers the same two ways
    print("    left out in turn (checks: old map new PC / with own offset -> new map new PC / with own offset, median m):")
    better = True
    for k, test in sorted(by.items(), key=lambda kv: -len(kv[1])):
        if len(test) < MIN_SETUP:
            continue
        train = [p for p in ps if p[4] != k]
        if len({p[4] for p in train}) < 2:
            continue
        fx, fy = joint(train, 0, offset_only)[:2], joint(train, 1, offset_only)[:2]
        own_new = (st.median(p[2] - (fx[0] * p[0] + fx[1]) for p in test), st.median(p[3] - (fy[0] * p[1] + fy[1]) for p in test))
        own_old = (st.median(p[2] - p[0] for p in test), st.median(p[3] - p[1] for p in test))
        e = [st.median(error(p, *old) for p in test), st.median(error(p, *old, own_old) for p in test),
             st.median(error(p, fx, fy) for p in test), st.median(error(p, fx, fy, own_new) for p in test)]
        better &= e[2] <= e[0] and e[3] <= e[1] + 1
        print(f"      {k}: {len(test)}: {e[0]:.1f} / {e[1]:.1f} -> {e[2]:.1f} / {e[3]:.1f}")
    days = sorted({p[5] for p in ps if p[5]})
    if len(days) >= 2:
        cut = days[len(days) // 2]
        early, late = [p for p in ps if p[5] and p[5] < cut], [p for p in ps if p[5] >= cut]
        if len({p[4] for p in early}) >= 2 and len(late) >= MIN_SETUP:
            fx, fy = joint(early, 0, offset_only)[:2], joint(early, 1, offset_only)[:2]
            print(f"    the days from {cut} against a fit of the days before: {st.median(error(p, *old) for p in late):.1f} m -> {st.median(error(p, fx, fy) for p in late):.1f} m")
    # how much the fit moves with which setups happen to be in it, at the far corners of the checks
    xs, ys = [p[0] for p in ps], [p[1] for p in ps]
    corners = [(x, y) for x in (min(xs), max(xs)) for y in (min(ys), max(ys))]
    keys = list(by)
    moves = []
    rng = random.Random(7)
    for _ in range(60):
        pick = [rng.choice(keys) for _ in keys]
        sample = [p for k in pick for p in by[k]]
        if len({p[4] for p in sample}) < 2:
            continue
        fx, fy = joint(sample, 0, offset_only)[:2], joint(sample, 1, offset_only)[:2]
        moves.append(max(math.hypot((fx[0] * x + fx[1]) - (ax * x + bx), (fy[0] * y + fy[1]) - (ay * y + by_)) / 100 for x, y in corners))
    if moves:
        moves.sort()
        print(f"    with other setups the fit moves at the corners of the checks by {moves[len(moves) // 2]:.1f} m (half the time), {moves[int(len(moves) * 0.9)]:.1f} m (nine in ten)")
    if m in MAPS:
        s, ox0, oy0, tile = MAPS[m]["scale"], MAPS[m]["offsetX"], MAPS[m]["offsetY"], MAPS[m]["tile"]
        cells = collections.defaultdict(lambda: collections.defaultdict(list))
        for p in ps:
            cells[f"{int((ox0 + s * p[0]) // tile)}_{int((oy0 + s * p[1]) // tile)}"][p[4]].append(
                (p[2] - (ax * p[0] + bx) - ox[p[4]], p[3] - (ay * p[1] + by_) - oy[p[4]]))
        print("    left per tile once every setup's own offset is off (setups there: median x y m):")
        for key, setups in sorted(cells.items()):
            meds = [(st.median(d[0] for d in v), st.median(d[1] for d in v)) for v in setups.values() if len(v) >= 3]
            if meds:
                print(f"      {key}: {len(meds)}: {st.median(d[0] for d in meds) / 100:+.0f} {st.median(d[1] for d in meds) / 100:+.0f}")
        print(f"    maps.json now: scale {s!r}, offsetX {ox0!r}, offsetY {oy0!r}")
        sx, sy = s / ax, s / ay
        print(f"    would be:      scale {(sx + sy) / 2!r} (x {sx!r}, y {sy!r}), offsetX {ox0 - s * bx / ax!r}, offsetY {oy0 - s * by_ / ay!r}")
        print(f"    every setup better left out: {better}")


def main(argv):
    offset_only = set()
    files = []
    for a in argv:
        if a.startswith("--offset-only"):
            offset_only.update(x for x in a.split("=", 1)[-1].split(",") if x and not x.startswith("--"))
        else:
            files.append(a)
    pairs = load(files)
    by_map = collections.defaultdict(list)
    for (m, stamp), ps in pairs.items():
        parts = stamp.split("|")
        if m in MAPS and len(parts) == 4 and parts[0] == "2":
            # made with other numbers (an earlier version of maps.json): the same picture pixel read with today's numbers
            s0, x0, y0 = float(parts[1]), float(parts[2]), float(parts[3])
            s, ox, oy = MAPS[m]["scale"], MAPS[m]["offsetX"], MAPS[m]["offsetY"]
            ps = [((x0 + s0 * p[0] - ox) / s, (y0 + s0 * p[1] - oy) / s) + tuple(p[2:]) for p in ps]
        elif m in MAPS and stamp != "log":
            print(f"{m}: {len(ps)} checks made with numbers that can't be read ({stamp}) left out")
            continue
        by_map[m] += ps
    for m, ps in sorted(by_map.items()):
        if len(ps) >= 12:
            report(m, ps, m in offset_only)


if __name__ == "__main__":
    main(sys.argv[1:])
