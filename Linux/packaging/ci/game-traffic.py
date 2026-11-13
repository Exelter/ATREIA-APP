"""Made-up game traffic on this machine, for the Linux workflow's capture check: a server on 127.0.0.1:13328 (the game server's port) sends a client the
four damage frames of the real 2026-09-30 capture (the same as the Windows app's check RunAion2RealCaptureScenario: Keen Strike 381, Keen Strike 588
crit, Rupture Strike 432, Blood Absorption 65 heal), five times over a few seconds. The app, listening with libpcap, must read them."""
import socket
import time

FRAMES = [
    "04388484030600be08fe26a8004f02000002433baf4101000000ba4ffd020100",
    "04388484032600be08fe26a8005703000002433baf4101000000ba4fcc0401020100",
    "04388484032600be080e4ea8004b02000002837dbe4101000000ba4fb00301010100",
    "0438be080400be0857fcb2004a020792ea4501000000ba4f410100",
]


def wire():
    out = bytearray()
    for hexframe in FRAMES:
        body = bytes.fromhex(hexframe)
        length = len(body) + 4  # LEB128(length) + body, length = body + 4
        while length >= 0x80:
            out.append((length & 0x7F) | 0x80)
            length >>= 7
        out.append(length)
        out += body
    return bytes(out)


server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
server.bind(("127.0.0.1", 13328))
server.listen(1)
client = socket.create_connection(("127.0.0.1", 13328))
conn, _ = server.accept()
data = wire()
for _ in range(5):
    conn.sendall(data)
    client.recv(65536)
    time.sleep(0.6)
conn.close()
client.close()
server.close()
print(f"sent the four frames five times ({len(data)} bytes each)")
