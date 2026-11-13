#!/bin/sh
# Atreia App for Linux - installs the app for you (no system folders touched):
#   the app        ~/.local/share/atreia-app
#   a command      ~/.local/bin/atreia-app
#   a menu entry   ~/.local/share/applications/atreia-app.desktop
# and gives the app's program the right to read network traffic, which a meter that reads the game's traffic needs (the kernel's capabilities
# cap_net_raw and cap_net_admin, the way Wireshark's capture tool gets them). That one step asks for your password (sudo).
#
# Run it from the unpacked folder:   ./install.sh
# Remove it again with:              ~/.local/share/atreia-app/uninstall.sh
set -eu

here=$(cd "$(dirname "$0")" && pwd)
data="${XDG_DATA_HOME:-$HOME/.local/share}"
dest="$data/atreia-app"
bin="$HOME/.local/bin"

if [ ! -x "$here/AtreiaApp" ]; then
    echo "AtreiaApp is not next to this script - run install.sh from the unpacked atreia-app folder." >&2
    exit 1
fi

if [ "$here" != "$dest" ]; then
    echo "Installing into $dest"
    rm -rf "$dest.new"
    mkdir -p "$dest.new"
    cp -a "$here/." "$dest.new/"
    rm -rf "$dest"
    mv "$dest.new" "$dest"
fi

mkdir -p "$bin" "$data/applications" "$data/icons/hicolor/256x256/apps"
ln -sf "$dest/AtreiaApp" "$bin/atreia-app"
if [ -f "$dest/assets/app/a2h-emblem.png" ]; then
    cp "$dest/assets/app/a2h-emblem.png" "$data/icons/hicolor/256x256/apps/atreia-app.png"
fi

cat > "$data/applications/atreia-app.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Atreia App
GenericName=AION 2 companion
Comment=DPS meter and tools for AION 2
Exec="$dest/AtreiaApp"
Icon=atreia-app
Terminal=false
Categories=Game;Utility;
StartupWMClass=AtreiaApp
DESKTOP
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" >/dev/null 2>&1 || true

# libpcap: the library that reads network traffic
if ! ldconfig -p 2>/dev/null | grep -q 'libpcap\.so'; then
    echo
    echo "libpcap is missing. Install it with your package manager, then start Atreia App:"
    echo "  Debian, Ubuntu, Mint, Pop!_OS:  sudo apt install libpcap0.8"
    echo "  Fedora:                         sudo dnf install libpcap"
    echo "  Arch, Manjaro, SteamOS:         sudo pacman -S libpcap"
    echo "  openSUSE:                       sudo zypper install libpcap1"
fi

echo
echo "Giving Atreia App the right to read network traffic (asks for your password):"
setcap_cmd=""
for candidate in "$(command -v setcap 2>/dev/null || true)" /usr/sbin/setcap /sbin/setcap; do
    if [ -n "$candidate" ] && [ -x "$candidate" ]; then
        setcap_cmd=$candidate
        break
    fi
done
if [ -z "$setcap_cmd" ]; then
    echo "setcap is not installed (package libcap2-bin on Debian/Ubuntu, libcap on Fedora/Arch). Install it, then run:" >&2
    echo "  sudo setcap cap_net_raw,cap_net_admin=eip '$dest/AtreiaApp'" >&2
elif sudo "$setcap_cmd" cap_net_raw,cap_net_admin=eip "$dest/AtreiaApp"; then
    echo "Done."
else
    echo "That did not work. Run it yourself later:" >&2
    echo "  sudo setcap cap_net_raw,cap_net_admin=eip '$dest/AtreiaApp'" >&2
fi

echo
echo "Atreia App is installed: start it from your menu, or with the command atreia-app."
echo "Start AION 2 (Steam with Proton, Lutris, Heroic ...) as usual; the meter finds the game by itself."
