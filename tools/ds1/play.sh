#!/usr/bin/env bash
# Runs the original Dungeon Siege through Proton (umu-launcher).
#
#   tools/ds1/play.sh [--loa] [--fullscreen] [--size WxH] [--intro] [--sound off]
#                     [--wined3d] [--wayland|--x11] [--gamescope] [-- extra game args]
#
#   --loa         run a retail install's Legends of Aranna (DSLOA.exe; it asks
#                 for its disc) instead of DungeonSiege.exe
#   --fullscreen  fullscreen at the screen's size (default: a window, 2560x1080 on
#                 ultrawides, 1920x1080 otherwise)
#   --size WxH    explicit resolution
#   --intro       play the intro movies (skipped by default; reports say they
#                 play black under Proton)
#   --sound off   start the game silent (sound=false)
#   --wined3d     Direct3D through wined3d/OpenGL instead of DXVK (PROTON_USE_WINED3D=1)
#   --wayland     Proton's native Wayland driver (PROTON_ENABLE_WAYLAND=1); default is
#                 Proton's own default, X11 (XWayland on a Wayland desktop)
#   --gamescope   run inside a gamescope session of the chosen size
#   Everything after "--" is passed to the game unchanged (e.g. maxfps=120).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
. "$here/common.sh"

exe=base; fullscreen=false; size=""; intro=false; sound=true; gamescope=false
while [ $# -gt 0 ]; do
    case "$1" in
        --loa)        exe=loa ;;
        --fullscreen) fullscreen=true ;;
        --size)       size="$2"; shift ;;
        --intro)      intro=true ;;
        --sound)      [ "${2:-on}" = off ] && sound=false; shift ;;
        --wined3d)    export PROTON_USE_WINED3D=1 ;;
        --wayland)    export PROTON_ENABLE_WAYLAND=1 ;;
        --x11)        unset PROTON_ENABLE_WAYLAND ;;
        --gamescope)  gamescope=true ;;
        --)           shift; break ;;
        -h|--help)    sed -n '2,20p' "$0"; exit 0 ;;
        *)            ds1_die "unknown option $1 (try --help)" ;;
    esac
    shift
done

[ -d "$WINEPREFIX/drive_c" ] || ds1_die "no prefix yet; run $here/setup-prefix.sh first"
if [ -z "$size" ]; then
    if $fullscreen; then size=$(ds1_screen_size); else size=$(ds1_window_size); fi
fi
case "$size" in [0-9]*x[0-9]*) ;; *) ds1_die "--size wants WxH, got '$size'" ;; esac
w=${size%x*}; h=${size#*x}

args=(width="$w" height="$h" fullscreen="$fullscreen")
$intro || args+=(nointro=true)
$sound || args+=(sound=false)

mkdir -p "$PROTON_LOG_DIR"
cd "$DS1_GAME_DIR"
cmd=(umu-run "$(ds1_exe "$exe")" "${args[@]}" "$@")
if $gamescope; then
    gs=(gamescope -W "$w" -H "$h")
    $fullscreen && gs+=(-f)
    cmd=("${gs[@]}" -- "${cmd[@]}")
fi
exec "${cmd[@]}"
