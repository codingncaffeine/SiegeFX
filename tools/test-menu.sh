#!/usr/bin/env bash
# Generates test-all.sh, the test menu for Linux, from test-all.bat, so the
# two menus are one source: edit test-all.bat, then run this script.
#
#   tools/test-menu.sh           write test-all.sh
#   tools/test-menu.sh --check   exit 1 when test-all.sh is not what
#                                test-all.bat generates (CI runs this)
#
# tools/test-menu.awk translates the menu and the tests; the parameters and
# the helpers the tests call are below.
set -euo pipefail
repo=$(cd "$(dirname "$0")/.." && pwd)

generate() {
    cat <<'EOF'
#!/usr/bin/env bash
# test-all.sh - GENERATED from test-all.bat by tools/test-menu.sh: edit
# test-all.bat and run tools/test-menu.sh (CI checks that the two agree).
#
# The phase-by-phase smoke test menu.
#   ./test-all.sh [--ds1=PATH] [--tool=PATH] [--run=PATH] [--refs=PATH]
set -u
cd "$(dirname "$0")" || exit 1

DS1=${SIEGEFX_DS1:-}
cfg="${XDG_CONFIG_HOME:-$HOME/.config}/siegefx/ds1path.txt"
if [ -z "$DS1" ] && [ -r "$cfg" ]; then
    DS1=$(grep -vE '^[[:space:]]*(#|$)' "$cfg" | head -1 | sed 's/^"//; s/"$//')
fi
TOOL=src/SiegeFX.Tools/bin/Release/net10.0/siegefx
RUN=src/SiegeFX.Runtime/bin/Release/net10.0/SiegeFX
REFS=_ds1refs
for arg in "$@"; do
    case "$arg" in
        --ds1=*) DS1=${arg#--ds1=} ;;
        --tool=*) TOOL=${arg#--tool=} ;;
        --run=*) RUN=${arg#--run=} ;;
        --refs=*) REFS=${arg#--refs=} ;;
        -h|--help)
            echo "Usage: ./test-all.sh [--ds1=PATH] [--tool=PATH] [--run=PATH] [--refs=PATH]"
            echo
            echo "  --ds1=PATH   Dungeon Siege install root (default: SIEGEFX_DS1, else"
            echo "               the first line of ~/.config/siegefx/ds1path.txt: ${DS1:-none})"
            echo "  --tool=PATH  siegefx CLI path           (default: $TOOL)"
            echo "  --run=PATH   SiegeFX engine path        (default: $RUN)"
            echo "  --refs=PATH  local reference assets dir (default: $REFS)"
            exit 0 ;;
        *) echo "unknown argument: $arg"; echo "run \"./test-all.sh --help\" for the parameter list"; exit 1 ;;
    esac
done
# What the tests name with the Windows variables of the same names.
TEMP=${TMPDIR:-/tmp}
LOCALAPPDATA=${XDG_DATA_HOME:-$HOME/.local/share}
dotnet=${DOTNET:-dotnet}
ERRORLEVEL=0

pause() { read -rp "Press Enter to continue . . . " _ || true; }
show_crash_log() {
    local log
    log="$(dirname "$RUN")/siegefx_crash.log"
    if [ -f "$log" ]; then
        echo "--- crash log ---"
        cat "$log"
        echo "------------------"
    fi
}
build() {
    echo
    "$dotnet" build SiegeFX.sln -c Release
    pause
}

if [ ! -x "$TOOL" ] || [ ! -x "$RUN" ]; then
    echo
    echo "Build output missing. Running dotnet build -c Release first..."
    if ! "$dotnet" build SiegeFX.sln -c Release; then
        echo "Build failed. Fix errors and re-run."
        pause
        exit 1
    fi
fi

EOF
    awk -f "$repo/tools/test-menu.awk" "$repo/test-all.bat"
}

out="$repo/test-all.sh"
case "${1:-}" in
    --check)
        if ! generate | cmp -s - "$out"; then
            echo "test-all.sh is not what test-all.bat generates: run tools/test-menu.sh" >&2
            exit 1
        fi
        echo "test-all.sh matches test-all.bat" ;;
    "")
        tmp=$(mktemp "$out.XXXXXX")
        trap 'rm -f -- "$tmp"' EXIT
        generate > "$tmp"
        bash -n "$tmp"
        chmod 755 "$tmp"
        mv "$tmp" "$out"
        echo "wrote $out" ;;
    *) echo "usage: tools/test-menu.sh [--check]" >&2; exit 2 ;;
esac
