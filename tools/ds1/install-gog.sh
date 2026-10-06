#!/usr/bin/env bash
# Installs Dungeon Siege from GOG's offline installer into a folder of your
# choice, running the installer silently in the game's Proton prefix (the same
# way Lutris's GOG install scripts do), and records that folder as the game's
# location for play.sh and SiegeFX.
#
#   tools/ds1/install-gog.sh <setup_dungeon_siege_*.exe> <target folder>
#
# The installer's .bin parts must sit beside the .exe. The prefix is created
# first if it does not exist yet. An install log is written to the target.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
[ $# -eq 2 ] || { sed -n '2,11p' "$0"; exit 1; }
installer=$(realpath "$1")
target=$(realpath -m "$2")
[ -f "$installer" ] || { echo "install-gog: no installer at $installer" >&2; exit 1; }
[ -e "$target" ] && [ -n "$(ls -A "$target" 2>/dev/null)" ] && { echo "install-gog: $target exists and is not empty" >&2; exit 1; }

export DS1_GAME_DIR="$(dirname "$installer")"   # common.sh wants a folder; the real one is set below
. "$here/common.sh"
command -v umu-run >/dev/null 2>&1 || ds1_die "umu-run not found"
[ -d "$WINEPREFIX/drive_c" ] || DS1_GAME_DIR="$target" "$here/setup-prefix.sh"

mkdir -p "$target"
winpath="Z:$(printf '%s' "$target" | tr '/' '\\')"
echo "installer: $(basename "$installer")"
echo "target   : $target"
cd "$(dirname "$installer")"
umu-run "$installer" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /LANG=english \
    "/DIR=$winpath" "/LOG=$winpath\\install.log" > "$PROTON_LOG_DIR/install-gog.log" 2>&1 || true

exe=""
for f in DungeonSiege.exe dungeonsiege.exe DSLOA.exe; do [ -f "$target/$f" ] && { exe=$f; break; }; done
[ -n "$exe" ] || ds1_die "the installer left no game executable in $target; see $PROTON_LOG_DIR/install-gog.log and $target/install.log"

cfg="${XDG_CONFIG_HOME:-$HOME/.config}/siegefx"
mkdir -p "$cfg"
printf '%s\n' "$target" > "$cfg/ds1path.txt"
echo "installed: $target ($exe); recorded in $cfg/ds1path.txt"
