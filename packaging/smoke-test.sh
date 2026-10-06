#!/usr/bin/env bash
# packaging/smoke-test.sh <SiegeFX-<version>-linux-x64.tar.gz | installed command>
#                         [--ds1=PATH] [--expect-version=X.Y.Z] [--eos]
#
# Launches what a release ships - the program in a fresh extraction of the
# tarball, or an installed command such as /usr/bin/siegefx - in a throwaway
# environment: HOME, the XDG folders and the runtime folder live in the work
# folder, nothing of the running desktop session is passed on, and audio goes
# to OpenAL's null device.
#   1. the three data-free self-tests (save round trip, dialogue, multiplayer
#      loopback) start the program and its runtime;
#   2. the session log names the build: the version in the tarball's name (or
#      --expect-version) and the portable linux-x64 runtime;
#   (--eos) internet play: the bundled EOS module and credentials log in to
#      Epic and round-trip a lobby (needs the network; a test identity of its
#      own, kept in scratch/eos-test-identity);
# and with a Dungeon Siege install (--ds1), on a private Xvfb server:
#   3. the main menu renders offscreen (GLFW and OpenGL);
#   4. the game boots to its menu with OpenAL up, and the OpenAL and GLFW
#      libraries it mapped are listed (the system's or the bundled copies).
# Exit 0 only when every step passes. Work folder: scratch/smoke in the repo,
# or $SMOKE_WORK.
set -uo pipefail
repo=$(cd "$(dirname "$0")/.." && pwd)
target=""; ds1=""; expect=""; eos=false
for arg in "$@"; do
    case "$arg" in
        --ds1=*) ds1=${arg#--ds1=} ;;
        --expect-version=*) expect=${arg#--expect-version=} ;;
        --eos) eos=true ;;
        -h|--help) sed -n '2,22p' "$0"; exit 0 ;;
        -*) echo "smoke-test: unknown argument $arg" >&2; exit 2 ;;
        *) target=$arg ;;
    esac
done
work=${SMOKE_WORK:-$repo/scratch/smoke}
rm -rf "$work"
mkdir -p "$work/extract" "$work/home" "$work/runtime" "$work/tmp"
chmod 700 "$work/runtime"
case "$target" in
    *.tar.gz)
        [ -f "$target" ] || { echo "smoke-test: no tarball at '$target'" >&2; exit 2; }
        name=$(basename "$target" .tar.gz)
        if [ -z "$expect" ]; then
            expect=${name#SiegeFX-v}; expect=${expect%-linux-x64}
            [ "$name" = SiegeFX-dev-linux-x64 ] && expect=""
        fi
        tar -xzf "$target" -C "$work/extract"
        exe="$work/extract/$name/SiegeFX" ;;
    *)
        exe=$target ;;
esac
if [ ! -x "$exe" ]; then
    echo "smoke-test: no program at '$exe'" >&2
    exit 2
fi

failed=0
step() {
    local label=$1; shift
    if "$@" > "$work/$label.log" 2>&1; then
        echo "PASS  $label"
    else
        echo "FAIL  $label  (log: $work/$label.log)"
        tail -5 "$work/$label.log" | sed 's/^/        /'
        failed=1
    fi
}
# The game's whole environment: only what is named here.
genv=(HOME="$work/home" PATH=/usr/bin:/bin LANG=C.UTF-8 TMPDIR="$work/tmp"
      XDG_RUNTIME_DIR="$work/runtime" XDG_DATA_HOME="$work/home/.local/share"
      XDG_CONFIG_HOME="$work/home/.config" XDG_CACHE_HOME="$work/home/.cache"
      ALSOFT_DRIVERS=null)
[ -n "$ds1" ] && genv+=(SIEGEFX_DS1="$ds1")
newest_log() { ls -t "$work/home/.local/share/SiegeFX/logs"/session-*.log 2> /dev/null | head -1; }

for t in save dialogue net; do
    step "selftest-$t" env -i "${genv[@]}" timeout 120 "$exe" "--selftest-$t"
done

# The build line: "[log] SiegeFX <version>+<commit> on <OS> (<RID>)".
build_line() {
    local log line
    log=$(newest_log)
    line=$(grep -m1 '^\[log\] SiegeFX ' "$log") || { echo "no build line in '$log'"; return 1; }
    echo "$line"
    if [ -n "$expect" ] && [[ "$line" != "[log] SiegeFX $expect+"* ]]; then
        echo "expected version $expect"; return 1
    fi
    [[ "$line" == *"(linux-x64)" ]] || { echo "expected the linux-x64 runtime"; return 1; }
}
step build-line build_line
sed 's/^/        /' "$work/build-line.log"

if $eos; then
    mkdir -p -- "$repo/scratch/eos-test-identity"
    step eos env -i "${genv[@]}" SIEGEFX_EOS_CACHE="$repo/scratch/eos-test-identity" \
        timeout 120 "$exe" --selftest-eos
    grep 'selftest-eos\] ' "$work/eos.log" | sed 's/^/        /'
fi

if [ -n "$ds1" ]; then
    if ! command -v Xvfb > /dev/null; then
        echo "FAIL  display  (Xvfb is not installed; the display steps need a private X server)"
        exit 1
    fi
    n=90
    while [ -e "/tmp/.X11-unix/X$n" ] || [ -e "/tmp/.X$n-lock" ]; do n=$((n + 1)); done
    Xvfb ":$n" -screen 0 1280x720x24 -nolisten tcp > "$work/xvfb.log" 2>&1 &
    xvfb=$!
    trap 'kill "$xvfb" 2> /dev/null' EXIT
    for _ in $(seq 50); do [ -e "/tmp/.X11-unix/X$n" ] && break; sleep 0.1; done
    genv+=(DISPLAY=":$n" XDG_SESSION_TYPE=x11)

    menu_shot() {
        env -i "${genv[@]}" timeout 120 "$exe" --frontend-shot "$ds1/Resources/Logic.dsres" \
            "$ds1/Resources/Objects.dsres" MainMenu --t=3.4 --out="$work/menu.png" || return 1
        [ "$(od -An -tx1 -N8 "$work/menu.png" | tr -d ' ')" = 89504e470d0a1a0a ] || { echo "no PNG written"; return 1; }
        echo "menu.png: $(stat -c %s "$work/menu.png") bytes"
    }
    step menu-shot menu_shot

    # Boot until the session log says OpenAL is up (env execs into the game,
    # so $! is the game), list the libraries it mapped, then end it.
    boot() {
        env -i "${genv[@]}" "$exe" --noVideo > "$work/boot-console.log" 2>&1 &
        local pid=$! log up=false
        for _ in $(seq 600); do
            log=$(newest_log)
            if [ -n "$log" ] && grep -q 'audio: OpenAL Soft up' "$log"; then up=true; break; fi
            kill -0 "$pid" 2> /dev/null || break
            sleep 0.1
        done
        grep -E 'lib(openal|glfw)\.so' "/proc/$pid/maps" 2> /dev/null | sed -E 's/^([^ ]+ +){5}//' | sort -u
        kill "$pid" 2> /dev/null
        for _ in $(seq 50); do kill -0 "$pid" 2> /dev/null || break; sleep 0.1; done
        kill -KILL "$pid" 2> /dev/null
        wait "$pid" 2> /dev/null
        $up || { echo "OpenAL never came up; console:"; tail -20 "$work/boot-console.log"; return 1; }
        echo "OpenAL up"
    }
    step boot-audio boot
    sed 's/^/        /' "$work/boot-audio.log"
fi
exit $failed
