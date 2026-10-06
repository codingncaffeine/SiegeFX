#!/usr/bin/env bash
# Shared environment for running the original Dungeon Siege (the GOG/retail
# Windows build) through Proton on Linux. Sourced by the other scripts here.
#
# Overrides (environment):
#   DS1_GAME_DIR  the Dungeon Siege install folder (holds DSLOA.exe / DungeonSiege.exe);
#                 default: first non-comment line of ${XDG_CONFIG_HOME:-~/.config}/siegefx/ds1path.txt
#                 (the same file the engine's install discovery reads)
#   DS1_HOME      where the Wine prefix and logs live (default ~/Games/dungeon-siege)
#   PROTONPATH    Proton build for umu; default = newest GE-Proton already present in
#                 Steam's compatibilitytools.d, else "GE-Proton" (umu downloads the latest)
#   GAMEID/STORE  umu identifiers (default umu-dungeonsiege / gog)

ds1_die() { printf 'ds1: %s\n' "$*" >&2; exit 1; }

ds1_config_dir="${XDG_CONFIG_HOME:-$HOME/.config}/siegefx"
if [ -z "${DS1_GAME_DIR:-}" ] && [ -r "$ds1_config_dir/ds1path.txt" ]; then
    DS1_GAME_DIR=$(grep -vE '^[[:space:]]*(#|$)' "$ds1_config_dir/ds1path.txt" | head -1 | sed 's/^"//; s/"$//' || true)
fi
[ -n "${DS1_GAME_DIR:-}" ] || ds1_die "set DS1_GAME_DIR or write the install folder to $ds1_config_dir/ds1path.txt"
[ -d "$DS1_GAME_DIR" ] || ds1_die "DS1_GAME_DIR '$DS1_GAME_DIR' is not a folder"
export DS1_GAME_DIR

DS1_HOME="${DS1_HOME:-$HOME/Games/dungeon-siege}"
export DS1_HOME
export WINEPREFIX="$DS1_HOME/pfx"
export GAMEID="${GAMEID:-umu-dungeonsiege}"
export STORE="${STORE:-gog}"
export PROTON_LOG_DIR="${PROTON_LOG_DIR:-$DS1_HOME/logs}"

if [ -z "${PROTONPATH:-}" ]; then
    for d in "$HOME/.local/share/Steam/compatibilitytools.d" "$HOME/.steam/root/compatibilitytools.d"; do
        newest=$(ls -d "$d"/GE-Proton* 2>/dev/null | sort -V | tail -1 || true)
        if [ -n "$newest" ]; then PROTONPATH="$newest"; break; fi
    done
    PROTONPATH="${PROTONPATH:-GE-Proton}"
fi
export PROTONPATH

# The game executable: the base game's DungeonSiege.exe by default; "loa"
# picks the Legends of Aranna build (DSLOA.exe) of a retail install.
ds1_exe() {
    local f names
    if [ "${1:-base}" = loa ]; then names=(DSLOA.exe dsloa.exe)
    else names=(DungeonSiege.exe dungeonsiege.exe "Dungeon Siege.exe"); fi
    for f in "${names[@]}"; do
        [ -f "$DS1_GAME_DIR/$f" ] && { echo "$f"; return; }
    done
    ds1_die "no ${names[0]} in $DS1_GAME_DIR"
}

# WxH of the primary (else the largest) screen: xrandr on Xorg/XWayland,
# kscreen-doctor on KDE Wayland, 1920x1080 when neither answers.
ds1_screen_size() {
    local s=""
    if [ -n "${DISPLAY:-}" ] && command -v xrandr >/dev/null 2>&1; then
        s=$(xrandr --current 2>/dev/null | awk '/ connected primary/ {print $4; exit}' | cut -d+ -f1 || true)
        [ -n "$s" ] || s=$(xrandr --current 2>/dev/null | awk '/ connected/ {split($3,a,"+"); print a[1]}' \
            | awk -F x '$1*$2 > m {m=$1*$2; s=$0} END {print s}' || true)
    fi
    if [ -z "$s" ] && command -v kscreen-doctor >/dev/null 2>&1; then
        s=$(kscreen-doctor -o 2>/dev/null | sed 's/\x1b\[[0-9;]*m//g' \
            | awk '/Geometry:/ {split($3,a,"x"); if (a[1]*a[2] > m) {m=a[1]*a[2]; s=$3}} END {print s}' || true)
    fi
    case "$s" in [0-9]*x[0-9]*) echo "$s" ;; *) echo 1920x1080 ;; esac
}

# A window that leaves room for the desktop: 21:9 on ultrawides, 16:9 otherwise.
ds1_window_size() {
    local s w h
    s=$(ds1_screen_size); w=${s%x*}; h=${s#*x}
    if [ "$w" -ge 2680 ] && [ $((w * 9)) -gt $((h * 20)) ]; then echo 2560x1080
    elif [ "$w" -ge 2000 ] && [ "$h" -ge 1150 ]; then echo 1920x1080
    elif [ "$w" -ge 1400 ] && [ "$h" -ge 850 ]; then echo 1280x720
    else echo 1024x768
    fi
}
