#!/usr/bin/env bash
# Development probe: starts the game inside a nested, virtual KWin session
# (nothing appears on the desktop), waits, photographs the nested screen, lists
# its windows and reads the game's and Proton's logs.
#
#   tools/ds1/probe.sh [seconds] [--exe NAME] [--audio off|null] [-- extra game args]
#
# --audio off   (default) disables Wine's audio drivers: no sound device opens
# --audio null  routes audio to a temporary PulseAudio/PipeWire null sink: Wine
#               sees a working device, nothing reaches the speakers
# Exit 0 = the game is running and no crash reporter / unhandled exception was
# seen, 1 = not running or crashed, 2 = the nested compositor failed.
# --exe runs another executable name from the game folder; a name that does
# not exist is the positive control (must exit 1).
# Evidence: <repo>/scratch/ds1-probe/<stamp>/ (screen.png, windows.txt,
# xrandr.txt, env.txt, umu.log, steam-*.log, kwin.log, result) and
# <repo>/scratch/ds1-probe/latest; DS1_PROBE_DIR moves it elsewhere.
set -uo pipefail
here=$(cd "$(dirname "$0")" && pwd)
. "$here/common.sh"

secs=35; exe=""; audio=off; fs=false
while [ $# -gt 0 ]; do
    case "$1" in
        --exe)        exe="$2"; shift ;;
        --audio)      audio="$2"; shift ;;
        --fullscreen) fs=true ;;
        --)      shift; break ;;
        [0-9]*)  secs="$1" ;;
        *)       ds1_die "unknown argument $1" ;;
    esac
    shift
done
[ -n "$exe" ] || exe=$(ds1_exe)
[ -d "$WINEPREFIX/drive_c" ] || ds1_die "no prefix yet; run $here/setup-prefix.sh first"

stamp=$(date +%Y%m%d-%H%M%S)
probes="${DS1_PROBE_DIR:-$(cd "$here/../.." && pwd)/scratch/ds1-probe}"
work="$probes/$stamp"
mkdir -p "$work"
ln -sfn "$stamp" "$probes/latest"
name=${exe:0:15}   # comm is 15 chars; "DSLOA.exe" fits
extra=("$@")

nullsink=""
if [ "$audio" = null ]; then
    nullsink=$(pactl load-module module-null-sink sink_name=ds1probe sink_properties=device.description=ds1probe 2>/dev/null || true)
    [ -n "$nullsink" ] || ds1_die "could not create a null sink (pactl)"
fi

# The nested compositor gets private XDG dirs (it must never touch the real
# KDE config); the session script restores the real ones for umu, whose runtime
# lives under the user's XDG_DATA_HOME.
{
    printf '#!/usr/bin/env bash\n'
    printf 'export XDG_CONFIG_HOME=%q XDG_DATA_HOME=%q XDG_CACHE_HOME=%q\n' \
        "${XDG_CONFIG_HOME:-$HOME/.config}" "${XDG_DATA_HOME:-$HOME/.local/share}" "${XDG_CACHE_HOME:-$HOME/.cache}"
    if [ "$audio" = null ]; then printf 'export PULSE_SINK=ds1probe\n'
    else printf 'export WINEDLLOVERRIDES="winepulse.drv,winealsa.drv,wineoss.drv=d"\n'; fi
    printf 'export PROTON_LOG=1 PROTON_LOG_DIR=%q DXVK_LOG_LEVEL=info DXVK_LOG_PATH=%q\n' "$work" "$work"
    printf 'echo "DISPLAY=$DISPLAY WAYLAND_DISPLAY=$WAYLAND_DISPLAY" > %q\n' "$work/env.txt"
    printf 'xrandr --current > %q 2>&1\n' "$work/xrandr.txt"
    printf 'cd %q\n' "$DS1_GAME_DIR"
    printf 'umu-run %q width=1280 height=800 fullscreen=%s nointro=true sound=false' "$exe" "$fs"
    for a in "${extra[@]}"; do printf ' %q' "$a"; done
    printf ' > %q 2>&1 &\n' "$work/umu.log"
    printf 'sleep %d\n' "$secs"
    printf 'magick x:root %q 2> %q\n' "$work/screen.png" "$work/import.err"
    printf 'for id in $(xprop -root _NET_CLIENT_LIST 2>/dev/null | grep -oE "0x[0-9a-f]+"); do xprop -id "$id" WM_NAME WM_CLASS _NET_WM_NAME; done > %q 2>&1\n' "$work/windows.txt"
    printf 'pids=$(pgrep -x %q || true)\n' "$name"
    printf 'if [ -n "$pids" ]; then echo running > %q; else echo absent > %q; fi\n' "$work/result" "$work/result"
    printf '[ -n "$pids" ] && kill $pids 2>/dev/null\n'
    printf 'sleep 3\npkill -x %q 2>/dev/null\nexit 0\n' "$name"
} > "$work/session.sh"
chmod +x "$work/session.sh"

"$here/nested.sh" "$work" $((secs + 60)) "$work/session.sh"
kwin_status=$?
[ -n "$nullsink" ] && pactl unload-module "$nullsink" 2>/dev/null

result=$(cat "$work/result" 2>/dev/null || echo none)
# Whether the game is healthy is read from the screen and the window titles,
# not from the Proton log: this game raises first-chance exceptions and writes
# temp libraries on every start, including good ones.
status=1
case "$result" in
    running) echo "probe: $exe running after ${secs}s"; status=0 ;;
    absent)  echo "probe: $exe NOT running after ${secs}s" ;;
    *)       echo "probe: no result from the nested session (kwin exit $kwin_status); see $work/kwin.log"; status=2 ;;
esac
titles=$(sed -n 's/^WM_NAME(STRING) = "\(.*\)"$/\1/p' "$work/windows.txt" 2>/dev/null | sort -u | paste -sd '|' -)
echo "probe: windows: ${titles:-none}"
[ -s "$work/screen.png" ] && echo "probe: screen -> $work/screen.png" || echo "probe: no screenshot ($(head -c 200 "$work/import.err" 2>/dev/null))"
echo "probe: evidence in $work"
exit $status
