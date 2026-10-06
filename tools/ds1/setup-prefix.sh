#!/usr/bin/env bash
# Builds the Proton prefix for the original Dungeon Siege once (one to two
# minutes) and records the game folder for play.sh.
#
#   tools/ds1/setup-prefix.sh [/path/to/Dungeon Siege]
#
# The path is remembered in ${XDG_CONFIG_HOME:-~/.config}/siegefx/ds1path.txt
# (not overwritten if it already exists). Needs umu-launcher; the Proton build
# is picked by common.sh (newest GE-Proton on disk, else downloaded by umu).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)

if [ $# -ge 1 ]; then
    cfg="${XDG_CONFIG_HOME:-$HOME/.config}/siegefx"
    mkdir -p "$cfg"
    [ -e "$cfg/ds1path.txt" ] || printf '%s\n' "$1" > "$cfg/ds1path.txt"
    export DS1_GAME_DIR="$1"
fi
. "$here/common.sh"
command -v umu-run >/dev/null 2>&1 || ds1_die "umu-run not found (Arch: pacman -S umu-launcher; Debian/Ubuntu: the umu-launcher .deb from its GitHub releases)"

mkdir -p "$WINEPREFIX" "$PROTON_LOG_DIR"
echo "game   : $DS1_GAME_DIR ($(ds1_exe))"
echo "prefix : $WINEPREFIX"
echo "proton : $PROTONPATH"
echo "building the prefix (log: $PROTON_LOG_DIR/setup.log) ..."
set +o pipefail
umu-run wineboot -u > "$PROTON_LOG_DIR/setup.log" 2>&1
set -o pipefail
if [ ! -d "$WINEPREFIX/drive_c/users/steamuser" ]; then
    tail -20 "$PROTON_LOG_DIR/setup.log" >&2
    ds1_die "the prefix did not build; see $PROTON_LOG_DIR/setup.log"
fi

# Windows always carries MFC 4.2 (mfc42.dll); a Wine prefix does not. The retail
# Legends of Aranna build failed at startup without it ("Couldn't load ...
# library", Error Code 4), so the prefix gets Microsoft's redistributable copy.
if ! grep -qx mfc42 "$WINEPREFIX/winetricks.log" 2>/dev/null; then
    echo "installing MFC 4.2 (winetricks mfc42) ..."
    set +o pipefail
    umu-run winetricks -q mfc42 >> "$PROTON_LOG_DIR/setup.log" 2>&1
    set -o pipefail
    grep -qx mfc42 "$WINEPREFIX/winetricks.log" 2>/dev/null || ds1_die "mfc42 did not install; see $PROTON_LOG_DIR/setup.log"
fi
echo "saves  : $WINEPREFIX/drive_c/users/steamuser/Documents/Dungeon Siege/"
echo "done. Play with: $here/play.sh   (add --fullscreen once the windowed run looks right)"
