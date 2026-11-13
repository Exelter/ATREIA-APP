#!/bin/sh
# Atreia App for Linux - removes what install.sh put in place. Your settings, characters and history stay in ~/.config/AtreiaApp
# (delete that folder too to remove everything).
set -eu

data="${XDG_DATA_HOME:-$HOME/.local/share}"
rm -f "$HOME/.local/bin/atreia-app" "$data/applications/atreia-app.desktop" "$data/icons/hicolor/256x256/apps/atreia-app.png"
rm -rf "$data/atreia-app"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" >/dev/null 2>&1 || true
echo "Atreia App is removed. Your settings and history are still in ~/.config/AtreiaApp."
