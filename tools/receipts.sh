#!/usr/bin/env bash
# The pre-push gate: build with warnings as errors, the data-free unit tests,
# the engine's self-tests, the parity ledger against its committed baseline
# (goldens/parity-ledger.baseline.txt) and every spell's effect trace against
# goldens/sfx-timelines. Exit 0 only when every step passes. Needs a Dungeon
# Siege install for everything after the unit tests.
#
#   tools/receipts.sh [--ds1=PATH] [--update-baseline]
#
# The install comes from --ds1, else SIEGEFX_DS1, else the first line of
# ${XDG_CONFIG_HOME:-~/.config}/siegefx/ds1path.txt. DOTNET picks the dotnet
# executable (default: dotnet on PATH). --update-baseline skips the trace
# comparison and, after every other step has passed, records this run's ledger
# and effect traces as the new baselines; commit them with the change that
# moved them. Logs and gap lists land in scratch/receipts/.
set -uo pipefail
repo=$(cd "$(dirname "$0")/.." && pwd)
dotnet=${DOTNET:-dotnet}
ds1=""; update=false
for arg in "$@"; do
    case "$arg" in
        --ds1=*) ds1=${arg#--ds1=} ;;
        --update-baseline) update=true ;;
        -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
        *) echo "receipts: unknown argument $arg" >&2; exit 2 ;;
    esac
done
if [ -z "$ds1" ]; then ds1=${SIEGEFX_DS1:-}; fi
cfg="${XDG_CONFIG_HOME:-$HOME/.config}/siegefx/ds1path.txt"
if [ -z "$ds1" ] && [ -r "$cfg" ]; then
    ds1=$(grep -vE '^[[:space:]]*(#|$)' "$cfg" | head -1 | sed 's/^"//; s/"$//' || true)
fi
if [ -z "$ds1" ] || [ ! -f "$ds1/Resources/Logic.dsres" ]; then
    echo "receipts: no Dungeon Siege install (pass --ds1=PATH or set SIEGEFX_DS1)" >&2
    exit 2
fi

work="$repo/scratch/receipts"
rm -rf "$work"
mkdir -p "$work/profile/data" "$work/profile/config"
failed=0
step() {
    local name=$1; shift
    if "$@" > "$work/$name.log" 2>&1; then
        echo "PASS  $name"
    else
        echo "FAIL  $name  (log: $work/$name.log)"
        tail -5 "$work/$name.log" | sed 's/^/        /'
        failed=1
    fi
}

step build "$dotnet" build "$repo/SiegeFX.sln" -c Release --nologo -warnaserror
step tests "$dotnet" test "$repo/tests/SiegeFX.Tests" -c Release --no-build --nologo

game="$repo/src/SiegeFX.Runtime/bin/Release/net10.0/SiegeFX"
for t in save dialogue net; do
    step "selftest-$t" env XDG_DATA_HOME="$work/profile/data" XDG_CONFIG_HOME="$work/profile/config" \
        SIEGEFX_DS1="$ds1" ALSOFT_DRIVERS=null timeout 180 "$game" "--selftest-$t"
done

cli="$repo/src/SiegeFX.Tools/bin/Release/net10.0/siegefx"
baseline="$repo/goldens/parity-ledger.baseline.txt"
step parity-ledger "$cli" parity ledger "$ds1" --out="$work/parity" --baseline="$baseline"
sed -n '/^  section/,/^$/p' "$work/parity-ledger.log"
sed -n '/^against baseline/,$p' "$work/parity-ledger.log" | sed 's/^/  /'

# Every spell's cast effect, run through the sfx VM with a fixed seed, must
# reproduce its committed trace byte for byte: none changed, missing or new.
goldens="$repo/goldens/sfx-timelines"
logic="$ds1/Resources/Logic.dsres"
sfx_goldens() {
    "$cli" sfx timeline "$logic" --all --out="$work/sfx-timelines" || return 1
    diff -r "$goldens" "$work/sfx-timelines" && return 0
    echo "effect traces that differ from goldens/sfx-timelines:"
    diff -rq "$goldens" "$work/sfx-timelines" | sed "s#$work/##g; s#$repo/##g"
    return 1
}
if $update; then
    echo "SKIP  sfx-goldens  (--update-baseline regenerates them)"
else
    step sfx-goldens sfx_goldens
fi

if $update && [ $failed -eq 0 ]; then
    "$cli" parity ledger "$ds1" --write-baseline="$baseline" > /dev/null && echo "baseline updated: $baseline"
    rm -rf "$work/sfx-timelines"
    if "$cli" sfx timeline "$logic" --all --out="$work/sfx-timelines" > /dev/null; then
        rm -f "$goldens"/*.txt && cp "$work/sfx-timelines"/*.txt "$goldens"/
        echo "effect goldens regenerated: $(git -C "$repo" status --short -- goldens/sfx-timelines | wc -l) files changed"
    fi
fi
exit $failed
