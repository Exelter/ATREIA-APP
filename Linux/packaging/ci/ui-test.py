"""The meter's window on an X server, every button clicked (the Linux workflow runs it): the app runs with ATREIA_UI_TEST=1, so its linux.log says where its
buttons, its scope switch, its grip and the items of an open menu are; xdotool clicks them, and the log says what each click did. Made-up game traffic
(game-traffic.py) gives the meter its lines. Pictures of the steps go to the screens folder. Exits 1 at the first thing that did not work.

Run inside the X server, from the repository: python3 Linux/packaging/ci/ui-test.py <the app> <its linux.log> <screens folder>"""
import os
import re
import subprocess
import sys
import time

APP, LOG, SCREENS = sys.argv[1], sys.argv[2], sys.argv[3]
failures = []
shots = 0


def log_text():
    try:
        with open(LOG, encoding="utf-8", errors="replace") as f:
            return f.read()
    except FileNotFoundError:
        return ""


def where(name, timeout=8.0):
    """The last place the log gave for a part ("ui: <name> x,y"), waiting for it."""
    end = time.time() + timeout
    while time.time() < end:
        found = None
        for line in log_text().splitlines():
            m = re.search(r"ui: (.+) (-?\d+),(-?\d+)$", line)
            if m and m.group(1) == name:
                found = (int(m.group(2)), int(m.group(3)))
            elif line.endswith("ui: " + name + " hidden"):
                found = None
        if found:
            return found
        time.sleep(0.25)
    return None


def xdo(*args):
    subprocess.run(["xdotool", *[str(a) for a in args]], check=False)


def click(name, timeout=8.0):
    at = where(name, timeout)
    if at is None:
        fail(f"no place in the log for '{name}'")
        return False
    xdo("mousemove", at[0], at[1])
    time.sleep(0.15)
    xdo("click", 1)
    time.sleep(0.6)
    return True


def expect(pattern, what, since, timeout=6.0):
    """Waits for a log line after the first <since> characters matching <pattern>."""
    end = time.time() + timeout
    while time.time() < end:
        text = log_text()[since:]
        m = re.search(pattern, text)
        if m:
            print(f"ok    {what}: {m.group(0)}")
            return True
        time.sleep(0.25)
    fail(f"{what}: nothing like /{pattern}/ in the log")
    return False


def fail(text):
    print(f"FAIL  {text}")
    failures.append(text)


def mark():
    return len(log_text())


def shot(name):
    global shots
    shots += 1
    path = os.path.join(SCREENS, f"linux-ui-{shots:02d}-{name}.png")
    subprocess.run(f"xwd -root -silent | convert xwd:- '{path}'", shell=True, check=False)


def menu_item(menu, item, pattern, what):
    since = mark()
    if click(menu) and click("item:" + item):
        expect(pattern, what, since)


def window_xy():
    out = subprocess.run(["xdotool", "search", "--name", "^Atreia App$", "getwindowgeometry"], capture_output=True, text=True).stdout
    m = re.search(r"Position: (-?\d+),(-?\d+)", out)
    return (int(m.group(1)), int(m.group(2))) if m else None


def drag(start, by, ctrl=False):
    xdo("mousemove", start[0], start[1])
    time.sleep(0.2)
    if ctrl:
        xdo("keydown", "ctrl")
    xdo("mousedown", 1)
    time.sleep(0.2)
    steps = 6
    for i in range(1, steps + 1):
        xdo("mousemove", start[0] + by[0] * i // steps, start[1] + by[1] * i // steps)
        time.sleep(0.08)
    xdo("mouseup", 1)
    if ctrl:
        xdo("keyup", "ctrl")
    time.sleep(0.6)


app = subprocess.Popen([APP], env={**os.environ, "ATREIA_UI_TEST": "1"}, stdout=open(os.path.join(SCREENS, "..", "ui-app.log"), "w"), stderr=subprocess.STDOUT)
try:
    if where("pill", 25) is None:
        fail("the meter did not come up")
        raise SystemExit
    shot("start")

    # the game's traffic: the meter's lines (the fold is off in this test's settings, so the body shows without a boss)
    since = mark()
    subprocess.run([sys.executable, "Linux/packaging/ci/game-traffic.py"], check=False)
    expect(r"capture: Connected", "the game's traffic is read", 0, 10)
    time.sleep(2.5)
    shot("lines")

    # the pill's menu: every measure and view
    for item, word in [("rDPS (buffs credited)", "rDPS"), ("Healing", "Heal"), ("Tanked (damage taken)", "Tank"), ("All in one", "All"),
                       ("Buffs on you", "Buffs"), ("Grind: Kinah, Abyss Points and loot per hour", "Grind"), ("DPS", "DPS")]:
        menu_item("pill", item, re.escape(f"action: menu {item} -> {word} ·"), f"the pill's {item}")
        shot("mode-" + word.lower())

    # the scope switch: the two views and the four scopes
    for tag, state in [("Stagger", "Stagger · view"), ("Grind", "Grind · view"), ("Me", "DPS · scope Me"), ("Group", "DPS · scope Group"),
                       ("Corps", "DPS · scope Corps"), ("All", "DPS · scope All")]:
        since = mark()
        if click("scope:" + tag):
            expect(re.escape(f"action: scope switch {tag} -> {state}"), f"the scope switch's {tag}", since)
        if tag in ("Stagger", "Grind"):
            shot("view-" + tag.lower())

    # the reset
    since = mark()
    if click("reset"):
        expect(r"action: clear -> DPS · scope All · 0 lines", "the reset empties the lines", since)

    # moving the meter by its head
    time.sleep(1.3)
    before = window_xy()
    head = where("head")
    if head and before:
        drag(head, (90, 60))
        after = window_xy()
        moved = after is not None and abs(after[0] - before[0] - 90) <= 6 and abs(after[1] - before[1] - 60) <= 6
        print(("ok    " if moved else "FAIL  ") + f"the head moves the meter: {before} -> {after}")
        if not moved:
            failures.append("the head did not move the meter")

    # the lock: in place, no grip, the buttons still work
    since = mark()
    if click("lock"):
        expect(r"action: locked in place", "the lock", since)
        before = window_xy()
        head = where("head")
        if head:
            drag(head, (80, 40))
        after = window_xy()
        stays = before is not None and after == before
        print(("ok    " if stays else "FAIL  ") + f"locked, the head does not move it: {before} -> {after}")
        if not stays:
            failures.append("the locked meter moved")
        if where("grip", 2) is not None:
            fail("the grip is still there on the locked meter")
        else:
            print("ok    locked, the grip is gone")
        menu_item("pill", "Healing", re.escape("action: menu Healing -> Heal ·"), "locked, the pill still works")
        menu_item("pill", "DPS", re.escape("action: menu DPS -> DPS ·"), "and back to DPS")
        since = mark()
        if click("lock"):
            expect(r"action: unlocked", "the lock again", since)

    # the grip: wider, then with Ctrl bigger, then a double-click gives the meter its own size back
    grip = where("grip")
    if grip:
        since = mark()
        drag(grip, (60, 0))
        expect(r"size: width 530, lines up to ten, scale 1\.00", "the grip makes the meter wider", since)
        shot("wider")
        time.sleep(1.3)  # the grip's new place comes with the meter's next second
        grip = where("grip")
        since = mark()
        drag(grip, (53, 53), ctrl=True)
        expect(r"size: width 530, lines up to ten, scale 1\.1\d", "the grip with Ctrl makes it bigger", since)
        shot("bigger")
        time.sleep(1.3)
        grip = where("grip")
        since = mark()
        xdo("mousemove", grip[0], grip[1])
        time.sleep(0.2)
        xdo("click", "--repeat", 2, "--delay", 80, 1)
        expect(r"size: width the bar's, lines up to ten", "a double-click on the grip gives the meter its width back", since)

    # the menu: pause and on again, the open world, always on top, the clicks through
    menu_item("menu", "Pause (nothing is counted until you go on)", r"action: menu Pause.* -> .*paused", "the pause")
    shot("paused")
    menu_item("menu", "Go on counting", r"action: menu Go on counting -> DPS · scope All · \d+ lines · (open|folded)(?! · paused)", "counting again")
    menu_item("menu", "Count the open world too", r"action: menu Count the open world too -> DPS · scope All · \d+ lines · (open|folded) · open world",
              "the open world on")
    menu_item("menu", "Count the open world too", r"action: menu Count the open world too -> DPS · scope All · \d+ lines · (open|folded)(?! · open world)",
              "the open world off")
    menu_item("menu", "Always on top", r"action: menu Always on top", "always on top off")
    menu_item("menu", "Always on top", r"action: menu Always on top", "always on top on")
    menu_item("menu", "Let the clicks through to the game (but on these buttons)", r"input region: the bar's buttons alone", "the clicks through")
    menu_item("menu", "Let the clicks through to the game (but on these buttons)", r"input region: the whole window again", "the clicks taken again")

    # quit from the menu: the app ends and keeps where it stood
    since = mark()
    if click("menu") and click("item:Quit Atreia App"):
        expect(r"action: quit at -?\d+,-?\d+", "quit", since)
        try:
            app.wait(10)
            print(f"ok    the app ended ({app.returncode})")
        except subprocess.TimeoutExpired:
            fail("the app did not end after Quit")
finally:
    if app.poll() is None:
        app.kill()

print(f"{len(failures)} failure(s)")
sys.exit(1 if failures else 0)
