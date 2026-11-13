# Atreia App for Linux

The app on Linux, while AION 2 itself runs through Proton or Wine (Steam, Lutris, Heroic, Bottles ...). The Windows app stays as it is, with its own
installer; this is an app of its own next to it.

## For players

1. Download [Atreia-App-Linux-x64.tar.gz](https://atreia.app/download/Atreia-App-Linux-x64.tar.gz) and unpack it (`tar xzf Atreia-App-Linux-x64.tar.gz`).
2. In the unpacked `atreia-app` folder, run `./install.sh` in a terminal. It installs the app for you alone (`~/.local/share/atreia-app`, a menu entry, the
   command `atreia-app`) and gives its program the right to read network traffic, which asks for your password once.
3. Start Atreia App from the menu, then the game. The meter finds the game by itself.

The meter reads the game's network traffic the way the Windows app does, never touching the game. On Linux that needs **libpcap** (most systems have it;
otherwise `libpcap0.8` on Debian/Ubuntu, `libpcap` on Fedora, Arch and SteamOS) and the right to read network traffic, which `install.sh` gives the
program (`setcap cap_net_raw,cap_net_admin=eip`, the way Wireshark's capture tool gets it). The meter's status line says when one of them is missing and
what to do. `atreia-app capture-check` in a terminal shows what the app can see.

- **Lock** (the padlock in the meter's head) keeps the meter in place, as on Windows. **Let the clicks through to the game** (the `⋯` menu) passes every
  click to the game but on the bar's buttons. This works on X11 and for the game and the meter under Xwayland in a Wayland session (the usual way Proton
  games run).
- The corner grip makes the meter wider or longer; with Ctrl it gets bigger, text and all; a double-click gives it its own size back.
- **Steam Deck**: in Desktop Mode. Gaming Mode shows no other windows over the game.
- Settings, characters and history are kept in `~/.config/AtreiaApp`; `linux.log` there notes how the capture started. `~/.local/share/atreia-app/uninstall.sh`
  removes the app (the settings stay).
- A newer version is said on the meter's status line. Download it again and run its `install.sh`: it replaces the old one and gives the new program the right
  to read the network.

## What is in it so far

The meter over the game, as the Windows meter: DPS, rDPS (buffs credited), healing, damage taken, Buffs on you and All in one, picked in its pill; the
switch for me, the group, the corps and everyone, with the Stagger and Grind views; the bars in parts (own damage, buffs for others, healing on others) and
their legend; the boss line with its health, when it dies and its enrage, and the boss's next casts; the dummy parse; the counters at its foot. It folds to
its bar between fights, follows the newest boss fight on its own and resets between fights, as on Windows. The `⋯` menu pauses it, counts the open world
too, keeps it on top, checks the capture, opens the data folder and quits. The game's pictures (the image pack) come on the first start. The other windows
of the Windows app follow one by one.

## For the code

`AtreiaApp.Linux.csproj` compiles the Windows app's own files that need nothing of Windows (`Client/`: the game reading, the meter's math, the sessions,
the uploads, the data) unchanged, and adds what Linux does differently:

| Linux/ | instead of (Windows app) |
| --- | --- |
| `Platform/GameConnections.cs` - the game's connections from `/proc` | `Client/Aion2/Capture/GameConnections.cs` (iphlpapi) |
| `Platform/CaptureAdapters.cs` - libpcap's `any` device | `Client/Aion2/Capture/CaptureAdapters.cs` |
| `Platform/NpcapAvailability.cs` - libpcap | `Client/Aion2/Capture/NpcapAvailability.cs` (Npcap) |
| `Platform/CaptureRights.cs` - the right to read the network | (Windows needs none) |
| `Platform/X11Shape.cs` - clicks through the meter | WS_EX_TRANSPARENT |
| `Platform/X11Pointer.cs` - the meter moved by its head | DragMove |
| `Meter/MeterEngine*.cs` - the meter, method by method as in `MainWindow*.cs` | the meter part of `Client/Ui/MainWindow*.cs` (WPF) |
| `Ui/` - the windows, with Avalonia | `Client/Ui/` (WPF) |

`Meter/MeterEngine*.cs` follow the Windows main window's meter method by method under the same names: a change to the meter's rules there belongs here
too.

Builds and checks run on GitHub Actions (`.github/workflows/linux.yml`, on every change to `Linux/` or `Client/`): the build, the checks (`AtreiaApp
selftest`: the Windows app's checks that need no window, then the Linux parts), pictures of the meter (`AtreiaApp render <out.png> [meter|raid|heal|
taken|all|buffs|callout|stagger|stagger-failed|grind|dummy|bar|waiting|permission|fonts]`), the real window on an X server with every button clicked
(`packaging/ci/ui-test.py`), the download installed the way a player does and run against made-up game traffic, and a download of the build. A release
(`.github/workflows/release.yml`) builds the Linux download after the Windows one and puts it on atreia.app with `updates/linux.json`. By hand:
`dotnet publish Linux/AtreiaApp.Linux.csproj -c Release -r linux-x64 --self-contained -o out`.
