#!/usr/bin/env bash
# The pre-push gate: build with warnings as errors, the data-free unit tests,
# the engine's self-tests, the parity ledger against its committed baseline
# (goldens/parity-ledger.baseline.txt) and every spell's effect trace against
# goldens/sfx-timelines. Exit 0 only when every step passes. Needs a Dungeon
# Siege install for everything after the unit tests.
#
# Three guards keep the original game's content respected: guard-repo (no game
# files in git), guard-text (no string in the built code or in goldens/ copies
# the game's text; existing cases are in goldens/guard-text.baseline.txt) and
# install-untouched (no file in the install changed during the run). Every run
# writes scratch/receipts/summary.md, ready to paste into a pull request.
#
#   tools/receipts.sh [--ds1=PATH] [--update-baseline]
#
# The install comes from --ds1, else SIEGEFX_DS1, else the first line of
# ${XDG_CONFIG_HOME:-~/.config}/siegefx/ds1path.txt. DOTNET picks the dotnet
# executable (default: dotnet on PATH). --update-baseline skips the trace
# comparison and, after every other step has passed, records this run's ledger,
# effect traces and guard-text hits as the new baselines; commit them with the
# change that moved them. Logs and gap lists land in scratch/receipts/.
set -uo pipefail
repo=$(cd "$(dirname "$0")/.." && pwd)
dotnet=${DOTNET:-dotnet}
ds1=""; update=false
for arg in "$@"; do
    case "$arg" in
        --ds1=*) ds1=${arg#--ds1=} ;;
        --update-baseline) update=true ;;
        -h|--help) sed -n '2,21p' "$0"; exit 0 ;;
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
results=()   # "name|PASS|seconds" for the summary
step() {
    local name=$1; shift
    local t0=$SECONDS
    if "$@" > "$work/$name.log" 2>&1; then
        echo "PASS  $name"
        results+=("$name|PASS|$((SECONDS - t0))")
    else
        echo "FAIL  $name  (log: $work/$name.log)"
        tail -5 "$work/$name.log" | sed 's/^/        /'
        results+=("$name|FAIL|$((SECONDS - t0))")
        failed=1
    fi
}
skip() { echo "SKIP  $1  ($2)"; results+=("$1|SKIP: $2|"); }

step build "$dotnet" build "$repo/SiegeFX.sln" -c Release --nologo -warnaserror
step tests "$dotnet" test "$repo/tests/SiegeFX.Tests" -c Release --no-build --nologo
step test-menu "$repo/tools/test-menu.sh" --check

cli="$repo/src/SiegeFX.Tools/bin/Release/net10.0/siegefx"
step guard-repo "$cli" guard repo "$repo"

# Everything after this reads the install; it must leave it as it found it.
snapshot="$work/install-before.txt"
"$cli" guard install "$ds1" --snapshot="$snapshot" > "$work/install-snapshot.log" 2>&1 || snapshot=""

game="$repo/src/SiegeFX.Runtime/bin/Release/net10.0/SiegeFX"
for t in save dialogue net; do
    step "selftest-$t" env XDG_DATA_HOME="$work/profile/data" XDG_CONFIG_HOME="$work/profile/config" \
        SIEGEFX_DS1="$ds1" ALSOFT_DRIVERS=null timeout 180 "$game" "--selftest-$t"
done

baseline="$repo/goldens/parity-ledger.baseline.txt"
step parity-ledger "$cli" parity ledger "$ds1" --out="$work/parity" --baseline="$baseline"
sed -n '/^  section/,/^$/p' "$work/parity-ledger.log"
sed -n '/^against baseline/,$p' "$work/parity-ledger.log" | sed 's/^/  /'

# No string in the built code or in goldens/ may copy the original game's text
# (quest text, dialogue, interface labels): it is read from the data, typos and
# all. Copies that predate the guard are recorded, by hash, in its baseline.
text_baseline="$repo/goldens/guard-text.baseline.txt"
if $update; then
    skip guard-text "--update-baseline records it"
else
    step guard-text "$cli" guard text "$ds1" --repo="$repo" --baseline="$text_baseline"
fi

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

if [ -n "$snapshot" ]; then
    step install-untouched "$cli" guard install "$ds1" --compare="$snapshot"
else
    step install-untouched false
fi

if $update && [ $failed -eq 0 ]; then
    "$cli" parity ledger "$ds1" --write-baseline="$baseline" > /dev/null && echo "baseline updated: $baseline"
    "$cli" guard text "$ds1" --repo="$repo" --write-baseline="$text_baseline" > /dev/null && echo "baseline updated: $text_baseline"
    rm -rf "$work/sfx-timelines"
    if "$cli" sfx timeline "$logic" --all --out="$work/sfx-timelines" > /dev/null; then
        rm -f "$goldens"/*.txt && cp "$work/sfx-timelines"/*.txt "$goldens"/
        echo "effect goldens regenerated: $(git -C "$repo" status --short -- goldens/sfx-timelines | wc -l) files changed"
    fi
fi

# The summary a pull request carries: what ran, on what, and how it went.
# No local paths, so it can be pasted as is.
summary="$work/summary.md"
{
    echo "### Receipts"
    echo
    printf '`%s` at `%s` · %s · .NET SDK %s · %s\n' \
        "$(git -C "$repo" rev-parse --abbrev-ref HEAD 2>/dev/null)" "$(git -C "$repo" rev-parse --short HEAD 2>/dev/null)" \
        "$(uname -s)" "$("$dotnet" --version 2>/dev/null)" "$(date -u +%Y-%m-%d)"
    [ -n "$(git -C "$repo" status --porcelain 2>/dev/null)" ] && echo && echo "_Uncommitted changes were present._"
    echo
    echo "| Step | Result | Time |"
    echo "|---|---|---|"
    for r in "${results[@]}"; do
        IFS='|' read -r name result secs <<< "$r"
        echo "| $name | $result | ${secs:+${secs}s} |"
    done
    ledger=$(sed -n '/^  section/,/^$/p' "$work/parity-ledger.log" 2>/dev/null)
    if [ -n "$ledger" ]; then
        echo
        echo "<details><summary>Parity ledger</summary>"
        echo
        echo '```'
        echo "$ledger"
        sed -n '/^against baseline/,$p' "$work/parity-ledger.log" | sed 's#^\(against baseline \).*\(goldens/\)#\1\2#'
        echo '```'
        echo
        echo "</details>"
    fi
} > "$summary"
echo "summary for the pull request: $summary"
exit $failed
