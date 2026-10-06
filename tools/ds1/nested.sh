#!/usr/bin/env bash
# Runs a script inside a nested, virtual KWin session (with Xwayland), so
# nothing it starts appears on the desktop. The compositor gets private XDG
# dirs under <work dir>, so it never touches the real KDE configuration; the
# script restores the real ones itself if what it runs needs them.
#
#   tools/ds1/nested.sh <work dir> <timeout seconds> <script>
#
# Exit status is the compositor's (124 = timed out). KWin does not pass the
# session's exit code on, so scripts write their results to files. KWin's
# output goes to <work dir>/kwin.log.
set -uo pipefail
[ $# -eq 3 ] || { sed -n '2,11p' "$0"; exit 2; }
work=$1; secs=$2; script=$3
command -v kwin_wayland >/dev/null 2>&1 || { echo "nested: kwin_wayland not found" >&2; exit 2; }
[ -x "$script" ] || { echo "nested: $script is not an executable script" >&2; exit 2; }
case "$script" in *'"'*) echo "nested: the script path may not contain a double quote" >&2; exit 2 ;; esac
mkdir -p "$work/xdg/config" "$work/xdg/data" "$work/xdg/cache"

# --exit-with-session takes a command line and splits it on spaces, so the path
# is quoted: unquoted, a script under a folder with a space never starts, Xwayland
# is never launched, and KWin idles until the timeout with an empty log.
env -u DISPLAY -u WAYLAND_DISPLAY \
    XDG_CONFIG_HOME="$work/xdg/config" XDG_DATA_HOME="$work/xdg/data" XDG_CACHE_HOME="$work/xdg/cache" \
    timeout "$secs" kwin_wayland --virtual --xwayland --socket "nested-$$-$RANDOM" --width 1280 --height 800 \
        --exit-with-session "\"$script\"" > "$work/kwin.log" 2>&1 < /dev/null
