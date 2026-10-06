#!/usr/bin/env bash
# Puts a "Dungeon Siege" launcher in this user's application menu, with the
# game's own icon: GOG's goggame-*.ico when present, else the icon inside the
# exe (via 7z), else a square crop of the splash image.
#
#   tools/ds1/install-desktop-entry.sh [--fullscreen]
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
. "$here/common.sh"

extra=""
[ "${1:-}" = --fullscreen ] && extra=" --fullscreen"

data="${XDG_DATA_HOME:-$HOME/.local/share}"
apps="$data/applications"
icons="$data/icons/hicolor/256x256/apps"
mkdir -p "$apps" "$icons"
icon="$icons/dungeon-siege.png"

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
ico=$(ls "$DS1_GAME_DIR"/goggame-*.ico 2>/dev/null | head -1 || true)
if [ -z "$ico" ]; then
    sevenzip=$(command -v 7z || command -v 7zz || true)
    if [ -n "$sevenzip" ] && "$sevenzip" e -y -o"$tmp" "$DS1_GAME_DIR/$(ds1_exe)" '.rsrc/ICON/*' >/dev/null 2>&1; then
        ico=$(ls -S "$tmp"/* 2>/dev/null | head -1 || true)
    fi
fi
if [ -n "$ico" ]; then
    # The largest frame of the .ico, scaled to 256.
    magick "$ico" "$tmp/frame-%02d.png" 2>/dev/null || true
    best=$(for f in "$tmp"/frame-*.png; do [ -f "$f" ] && echo "$(magick identify -format '%w' "$f") $f"; done \
        | sort -n | tail -1 | cut -d' ' -f2- || true)
    [ -n "$best" ] && magick "$best" -resize 256x256 "$icon"
elif [ -f "$DS1_GAME_DIR/SplashImage.bmp" ]; then
    magick "$DS1_GAME_DIR/SplashImage.bmp" -gravity center -crop 1:1 +repage -resize 256x256 "$icon"
fi

cat > "$apps/dungeon-siege.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Dungeon Siege
Comment=Dungeon Siege through Proton
Exec="$here/play.sh"$extra
Path=$DS1_GAME_DIR
Icon=$icon
Terminal=false
Categories=Game;RolePlaying;
Keywords=siege;gpg;rpg;
EOF
update-desktop-database "$apps" 2>/dev/null || true
echo "installed $apps/dungeon-siege.desktop (icon: $icon)"
