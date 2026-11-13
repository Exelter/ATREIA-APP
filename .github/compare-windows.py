"""Temporary (with windows-same.yml): draws every picture of the Screens workflow with the Windows app built from main and from this branch, one
build right after the other per picture, and compares the two pixel by pixel; then runs both builds' self-checks. Prints a line per picture; for a
picture that differs, its size, how many pixels differ and where, and both pictures (base64, collapsed). Exits 1 when anything differs."""
import base64
import os
import re
import subprocess
import sys

from PIL import Image, ImageChops

root = os.getcwd()
yml = open(os.path.join("main", ".github", "workflows", "screens.yml"), encoding="utf-8").read()
draw = yml[yml.index("$shots = @("):yml.index("foreach ($s in $shots)")]
shots = [re.findall(r"'([^']*)'", line) for line in draw.splitlines() if line.strip().startswith("@('")]
print(f"{len(shots)} pictures")

os.makedirs("shots-main", exist_ok=True)
os.makedirs("shots-branch", exist_ok=True)


def render(build, shot):
    out_dir = os.path.join(root, "out-" + build)
    target = os.path.join(root, "shots-" + build, shot[1])
    exe = os.path.join(out_dir, "AtreiaApp.exe")
    result = subprocess.run([exe, shot[0], target] + shot[2:], cwd=out_dir, capture_output=True, text=True, timeout=180)
    return target, result.returncode


def show(label, path):
    print(f"::group::{label} (base64)")
    with open(path, "rb") as f:
        print(base64.b64encode(f.read()).decode())
    print("::endgroup::")


different = 0
for shot in shots:
    a, code_a = render("main", shot)
    b, code_b = render("branch", shot)
    name = f"{shot[0]} {' '.join(shot[2:])} -> {shot[1]}"
    if not os.path.exists(a) or not os.path.exists(b):
        print(f"MISSING  {name}: main exit {code_a} {'drawn' if os.path.exists(a) else 'no picture'}, branch exit {code_b} {'drawn' if os.path.exists(b) else 'no picture'}")
        if os.path.exists(a) != os.path.exists(b):
            different += 1
        continue
    if open(a, "rb").read() == open(b, "rb").read():
        print(f"same     {name}")
        continue
    ia, ib = Image.open(a).convert("RGBA"), Image.open(b).convert("RGBA")
    if ia.size != ib.size:
        print(f"DIFFERS  {name}: size {ia.size} vs {ib.size}")
        different += 1
        show("main " + shot[1], a)
        show("branch " + shot[1], b)
        continue
    diff = ImageChops.difference(ia, ib)
    box = diff.getbbox()
    if box is None:
        print(f"same     {name} (same pixels, other bytes)")
        continue
    count = sum(1 for px in diff.getdata() if px != (0, 0, 0, 0))
    print(f"DIFFERS  {name}: {count} pixel(s) in {box}")
    different += 1
    show("main " + shot[1], a)
    show("branch " + shot[1], b)

for build in ("main", "branch"):
    out_dir = os.path.join(root, "out-" + build)
    result = subprocess.run([os.path.join(out_dir, "AtreiaApp.exe"), "selftest"], cwd=out_dir, capture_output=True, text=True, timeout=900)
    lines = result.stdout.splitlines()
    passed = any("ALL CHECKS PASSED" in line for line in lines)
    print(f"selftest {build}: exit {result.returncode}, {'ALL CHECKS PASSED' if passed else 'FAILED'}, {len(lines)} lines")
    open(f"selftest-{build}.txt", "w", encoding="utf-8").write(result.stdout)
    if not passed:
        different += 1
        print("\n".join(lines[-40:]))

checks = [open(f"selftest-{b}.txt", encoding="utf-8").read().splitlines() for b in ("main", "branch")]
heads = [[line for line in c if line.startswith("[selftest]")] for c in checks]
print(f"check headings: main {len(heads[0])}, branch {len(heads[1])}, same list: {heads[0] == heads[1]}")
if heads[0] != heads[1]:
    different += 1
    print("only main:", [h for h in heads[0] if h not in heads[1]][:20])
    print("only branch:", [h for h in heads[1] if h not in heads[0]][:20])

print(f"{different} difference(s)")
sys.exit(1 if different else 0)
