"""Reads the app's debug recordings (Debug/aion2-*.jsonl, the "debug recording" setting) for the boss mechanics study: the game-server stream
reassembled and cut into frames the way Aion2/Capture/TcpReassembler.cs and Aion2/Protocol/Aion2FrameDecoder.cs do it, and only the frames it needs:
windups (0x0938), casts (0x0238), damage (0x0438), effects put on (0x2a38), positions (the 0x..37 movement frames, 0x3c37), monster spawns (0x4136)
and removes (0x4236). Run from the repository's root:

    python3 Tools/mechanics/recording.py <recording.jsonl> <out.pkl>

The .pkl is what Tools/mechanics/fit_areas.py reads. Nothing leaves the PC.
"""
import base64
import json
import pickle
import struct
import sys
from datetime import datetime

SYNC = set(json.load(open('Client/assets/aion2/protocol/opcodes.json', encoding='utf-8'))['syncOpcodes'])
MAX_FRAME = 16384
POS_OFFSET = {0x1a: 2, 0x1b: 2, 0x1c: 2, 0x1e: 2, 0x20: 2, 0x28: 2, 0x29: 2, 0x2a: 2, 0x1f: 1, 0x2f: 1}


def lz4(src, size):
    out = bytearray()
    i, n = 0, len(src)
    while i < n:
        token = src[i]; i += 1
        lit = token >> 4
        if lit == 15:
            while True:
                e = src[i]; i += 1; lit += e
                if e != 255:
                    break
        out += src[i:i + lit]; i += lit
        if i >= n:
            break
        off = src[i] | src[i + 1] << 8; i += 2
        ln = token & 15
        if ln == 15:
            while True:
                e = src[i]; i += 1; ln += e
                if e != 255:
                    break
        ln += 4
        if off == 0 or off > len(out):
            return None
        start = len(out) - off
        if off >= ln:
            out += out[start:start + ln]
        else:
            for k in range(ln):
                out.append(out[start + k])
    return bytes(out) if len(out) == size else None


def varint(b, p):
    v = 0
    for i in range(5):
        if p >= len(b):
            return None, p
        c = b[p]; p += 1
        v |= (c & 0x7f) << (7 * i)
        if not c & 0x80:
            return v, p
    return None, p


class Out:
    def __init__(self):
        self.spawn = {}      # entity -> list of (t, npc, x, y)
        self.remove = []     # (t, entity, reason)
        self.pos = {}        # entity -> list of (t, x, y)
        self.windup = []     # (t, actor, skill, target, ms, facing, x, y, haspos)
        self.cast = []       # (t, actor, skill, target, facing, x, y, haspos)
        self.dmg = []        # (t, actor, target, skill, amount)
        self.buff = []       # (t, caster, target, effect, duration ms)
        self.frames = 0


def aim(f):
    p = 2
    actor, p = varint(f, p)
    if actor is None or not 1 <= actor <= 10_000_000 or p + 8 > len(f):
        return None
    p += 1
    skill = struct.unpack_from('<I', f, p)[0]; p += 4
    if not 1 <= skill <= 300_000_000:
        return None
    flags = f[p + 1]; p += 2
    target, p = varint(f, p)
    if target is None or p + 4 > len(f):
        return None
    ang = struct.unpack_from('<f', f, p)[0]; p += 4
    if ang != ang or abs(ang) > 720:
        return None
    x = y = 0.0; has = False
    if flags & 2:
        if p + 12 > len(f):
            return None
        x, y, z = struct.unpack_from('<fff', f, p); p += 12
        has = abs(x) < 1e7 and abs(y) < 1e7
    return actor, skill, target, ang, x, y, has, p


def decode(f, t, o, nested=False):
    if len(f) < 2:
        return
    op = f[0] << 8 | f[1]
    o.frames += 1
    if op == 0xFFFF and not nested:
        if len(f) <= 6:
            return
        size = struct.unpack_from('<I', f, 2)[0]
        raw = lz4(f[6:], size) if 0 < size <= 1 << 20 else None
        if raw is None:
            return
        p = 0
        while p < len(raw):
            ln, q = varint(raw, p)
            if ln is None:
                break
            prefix = q - p
            total = ln + prefix - 4
            if total - prefix < 2 or p + total > len(raw):
                break
            decode(raw[p + prefix:p + total], t, o, True)
            p += total
        return
    if op == 0x0938:
        a = aim(f)
        if a:
            ms, _ = varint(f, a[7])
            if ms is not None and 50 <= ms <= 120_000:
                o.windup.append((t, a[0], a[1], a[2], ms, a[3], a[4], a[5], a[6]))
        return
    if op == 0x0238:
        a = aim(f)
        if a:
            o.cast.append((t, a[0], a[1], a[2], a[3], a[4], a[5], a[6]))
        return
    if op == 0x0438:
        p = 2
        target, p = varint(f, p)
        if target is None or len(f) < p + 2:
            return
        flags = f[p]; p += 2
        if not flags & 4:
            return
        actor, p = varint(f, p)
        if actor is None or len(f) < p + 6:
            return
        skill = struct.unpack_from('<I', f, p)[0]; p += 4
        marker = -1
        for i in range(p + 2, len(f) - 3):
            if 1 <= f[i] <= 9 and f[i + 1] == 0 and f[i + 2] == 0 and f[i + 3] == 0:
                marker = i
                break
        amount = 0
        if marker >= 0:
            q = marker + 4
            _, q = varint(f, q)
            if q < len(f):
                amount, q = varint(f, q)
        o.dmg.append((t, actor, target, skill, amount or 0))
        return
    if op == 0x2a38:
        p = 2
        tg, p = varint(f, p)
        if tg is None or p + 2 > len(f) or f[p] != 1:
            return
        p += 2
        inst, p = varint(f, p)
        if inst is None or p + 20 > len(f):
            return
        effect, duration, stays = struct.unpack_from('<III', f, p)
        p += 20
        caster, p = varint(f, p)
        if caster is None or not 1 <= caster <= 10_000_000:
            return
        o.buff.append((t, caster, tg, effect, -1 if stays else duration))
        return
    if op == 0x4136:
        p = 2
        e, p = varint(f, p)
        if e is None or len(f) < p + 7:
            return
        if f[p + 2] == 1 and p + 3 < len(f):
            p += 1 + f[p + 3]
            if len(f) < p + 7:
                return
        p += 3
        npc = struct.unpack_from('<I', f, p)[0]
        x = y = None
        if len(f) >= p + 14:
            x, y = struct.unpack_from('<ff', f, p + 6)
            if not (abs(x) < 1e6 and abs(y) < 1e6):
                x = y = None
        o.spawn.setdefault(e, []).append((t, npc, x, y))
        if x is not None:
            o.pos.setdefault(e, []).append((t, x, y))
        return
    if op == 0x4236:
        p = 2
        e, p = varint(f, p)
        if e is not None:
            o.remove.append((t, e, f[p + 1] if p + 1 < len(f) else 0))
        return
    if op == 0x3c37:
        p = 2
        e, p = varint(f, p)
        if e is None or len(f) < p + 9 or f[p] != 3:
            return
        x, y = struct.unpack_from('<ff', f, p + 1)
        if 1 < abs(x) < 200_000 and 1 < abs(y) < 200_000:
            o.pos.setdefault(e, []).append((t, x, y))
        return
    if op & 0xff == 0x37 and (op >> 8) in POS_OFFSET:
        off = POS_OFFSET[op >> 8]
        p = 2
        e, p = varint(f, p)
        if e is None or not 1 <= e <= 10_000_000 or len(f) < p + off + 8:
            return
        x, y = struct.unpack_from('<ff', f, p + off)
        if 1 < abs(x) < 200_000 and 1 < abs(y) < 200_000:
            o.pos.setdefault(e, []).append((t, x, y))


class Stream:
    def __init__(self):
        self.next = None
        self.buf = bytearray()
        self.pending = {}
        self.synced = False


def frame_ok(buf, pos):
    ln, q = varint(buf, pos)
    if ln is None:
        return None
    prefix = q - pos
    total = ln + prefix - 4
    if total - prefix < 2 or total > MAX_FRAME or pos + total > len(buf):
        return None
    opc = buf[pos + prefix] << 8 | buf[pos + prefix + 1]
    if opc not in SYNC:
        return None
    return total


def sync(buf):
    for start in range(min(len(buf), 4096)):
        pos, chain = start, 0
        while chain < 3:
            total = frame_ok(buf, pos)
            if total is None:
                break
            pos += total
            chain += 1
            if pos == len(buf):
                chain = 3
        if chain >= 3:
            return start
    return None


def feed(st, t, o):
    buf = st.buf
    if not st.synced:
        s = sync(buf)
        if s is None:
            if len(buf) > 65536:
                del buf[:len(buf) - 4096]
            return
        del buf[:s]
        st.synced = True
    pos = 0
    while pos < len(buf):
        ln, q = varint(buf, pos)
        if ln is None:
            if q - pos >= 5:
                st.synced = False
                pos += 1
                break
            break
        prefix = q - pos
        total = ln + prefix - 4
        if total - prefix < 2 or total > 65536:
            st.synced = False
            pos += 1
            break
        if pos + total > len(buf):
            break
        decode(bytes(buf[pos + prefix:pos + total]), t, o)
        pos += total
    del buf[:pos]
    if not st.synced:
        feed(st, t, o)


def run(path):
    o = Out()
    streams = {}
    for line in open(path, encoding='utf-8'):
        if not line.startswith('{'):
            continue
        d = json.loads(line)
        if not d.get('Server'):
            continue
        t = datetime.fromisoformat(d['T']).timestamp()
        key = d['From'] + '>' + d['To']
        st = streams.setdefault(key, Stream())
        data = base64.b64decode(d['Data'])
        seq = d['Seq']
        if st.next is None:
            st.next = seq
        diff = (seq - st.next) & 0xFFFFFFFF
        if diff >= 0x80000000:   # old: a retransmission, take what is new of it
            back = (st.next - seq) & 0xFFFFFFFF
            if back >= len(data):
                continue
            data = data[back:]
            seq = st.next
            diff = 0
        if diff > 0:
            st.pending[seq] = data
            if len(st.pending) > 200:   # a hole that will not fill: go on after it
                first = min(st.pending, key=lambda s: (s - st.next) & 0xFFFFFFFF)
                st.next = first
                st.buf.clear()
                st.synced = False
            else:
                continue
        else:
            st.buf += data
            st.next = (seq + len(data)) & 0xFFFFFFFF
        while st.next in st.pending:
            chunk = st.pending.pop(st.next)
            st.buf += chunk
            st.next = (st.next + len(chunk)) & 0xFFFFFFFF
        feed(st, t, o)
    return o


if __name__ == '__main__':
    o = run(sys.argv[1])
    print(f'frames {o.frames}, spawns {len(o.spawn)}, positions {sum(len(v) for v in o.pos.values())} of {len(o.pos)} entities, windups {len(o.windup)}, casts {len(o.cast)}, damage {len(o.dmg)}')
    pickle.dump({'spawn': o.spawn, 'remove': o.remove, 'pos': o.pos, 'windup': o.windup, 'cast': o.cast, 'dmg': o.dmg, 'buff': o.buff}, open(sys.argv[2], 'wb'))
