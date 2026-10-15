#!/usr/bin/env bash
# packaging/smoke-test-windows.sh <SiegeFX-<version>-win-x64.zip> [seconds]
#
# Launches the Windows build as shipped, under Proton (umu-run), inside a
# nested virtual KWin session (tools/ds1/nested.sh: nothing reaches the
# desktop), with a Wine prefix of its own and no audio device. It boots
# straight to the main menu (--noVideo) against the Dungeon Siege install
# named in ~/.config/siegefx/ds1path.txt. After [seconds] (default 40) it
# photographs the screen and reads the build's own session log.
# Then it closes the game and runs --selftest-eos: the bundled EOS module and
# credentials log in to Epic and round-trip a lobby (needs the network).
# Exit 0 only when the build is still running, its log names the expected
# version (from the zip's name) on Windows, no crash log was written, and
# internet play works. Evidence: scratch/smoke-windows/ (screen.png,
# session.log, umu.log, eos.txt); the Wine prefix persists in
# scratch/smoke-windows-pfx so Proton sets it up once.
set -uo pipefail
repo=$(cd "$(dirname "$0")/.." && pwd)
zip=${1:?usage: packaging/smoke-test-windows.sh <SiegeFX-vX.Y.Z-win-x64.zip> [seconds]}
secs=${2:-40}
[ -f "$zip" ] || { echo "smoke-test-windows: no zip at '$zip'" >&2; exit 2; }
. "$repo/tools/ds1/common.sh"
ver=$(basename "$zip" .zip); ver=${ver#SiegeFX-v}; ver=${ver%-win-x64}

work="$repo/scratch/smoke-windows"
pfx="$repo/scratch/smoke-windows-pfx"
logs="$pfx/drive_c/users/steamuser/AppData/Local/SiegeFX/logs"
rm -rf -- "$work" "$logs"
mkdir -p -- "$work/app" "$pfx" "$repo/scratch/eos-test-identity-win"
unzip -q "$zip" -d "$work/app"
[ -f "$work/app/SiegeFX.exe" ] || { echo "smoke-test-windows: SiegeFX.exe missing from the zip" >&2; exit 1; }
# The game folder as Wine sees it (drive Z: is the Linux root).
winpath() { printf 'Z:%s' "$1" | tr '/' '\\'; }
ds1_win=$(winpath "$DS1_GAME_DIR")

# Silent by construction, not by setting: the game runs where no sound device
# and no audio server exists (bwrap hides /dev/snd and puts /dev/null over the
# PipeWire and PulseAudio sockets); Wine's audio drivers are switched off too.
# Only the game is walled in: the nested compositor runs as usual.
command -v bwrap > /dev/null || { echo "smoke-test-windows: bwrap is needed to keep the run silent" >&2; exit 2; }
rt=${XDG_RUNTIME_DIR:-/run/user/$(id -u)}
quiet="bwrap --dev-bind / / --tmpfs /dev/snd --die-with-parent"
for sock in "$rt/pipewire-0" "$rt/pipewire-0-manager" "$rt/pulse/native"; do
    [ -S "$sock" ] && quiet+=" --ro-bind /dev/null $(printf %q "$sock")"
done

{
    printf '#!/usr/bin/env bash\n'
    printf 'export XDG_CONFIG_HOME=%q XDG_DATA_HOME=%q XDG_CACHE_HOME=%q\n' \
        "${XDG_CONFIG_HOME:-$HOME/.config}" "${XDG_DATA_HOME:-$HOME/.local/share}" "${XDG_CACHE_HOME:-$HOME/.cache}"
    printf 'export WINEPREFIX=%q GAMEID=0\n' "$pfx"
    printf 'export WINEDLLOVERRIDES="winepulse.drv,winealsa.drv,wineoss.drv=d" SIEGEFX_DS1=%q\n' "$ds1_win"
    printf 'cd %q\n' "$work/app"
    # The control: what the walled-in game can see of the sound system.
    printf '%s sh -c %q %q > %q 2>&1\n' "$quiet" \
        'n=$(ls /dev/snd | wc -l); s=0; for f in "$0"/pipewire-0 "$0"/pipewire-0-manager "$0"/pulse/native; do [ -S "$f" ] && s=$((s+1)); done; echo "sound devices $n, audio sockets $s"' \
        "$rt" "$work/silence.txt"
    printf '%s umu-run SiegeFX.exe --noVideo > %q 2>&1 &\n' "$quiet" "$work/umu.log"
    # A new prefix is created on the first launch; time the run from when the
    # program itself is up.
    printf 'for _ in $(seq 240); do pgrep -x SiegeFX.exe > /dev/null && break; sleep 1; done\n'
    printf 'sleep %d\n' "$secs"
    printf 'magick x:root %q 2> %q\n' "$work/screen.png" "$work/import.err"
    printf 'if pgrep -x SiegeFX.exe > /dev/null; then echo running > %q; else echo absent > %q; fi\n' \
        "$work/result" "$work/result"
    printf 'pkill -x SiegeFX.exe 2> /dev/null; sleep 3; pkill -9 -x SiegeFX.exe 2> /dev/null\n'
    printf 'SIEGEFX_EOS_CACHE=%q timeout 180 %s umu-run SiegeFX.exe --selftest-eos > %q 2>&1\nexit 0\n' \
        "$(winpath "$repo/scratch/eos-test-identity-win")" "$quiet" "$work/eos.txt"
} > "$work/session.sh"
chmod +x "$work/session.sh"
"$repo/tools/ds1/nested.sh" "$work" $((secs + 300)) "$work/session.sh"

failed=0
silence=$(cat "$work/silence.txt" 2> /dev/null || echo "no control ran")
if [ "$silence" = "sound devices 0, audio sockets 0" ]; then echo "PASS  silent ($silence)"; else echo "FAIL  not silent ($silence)"; failed=1; fi
result=$(cat "$work/result" 2> /dev/null || echo none)
if [ "$result" = running ]; then echo "PASS  running after ${secs}s"; else echo "FAIL  not running after ${secs}s ($result)"; failed=1; fi
# Logs are cleared per run: the menu run wrote the oldest, the EOS run the next.
log=$(ls -tr "$logs"/session-*.log 2> /dev/null | head -1)
if [ -n "$log" ]; then
    cp "$log" "$work/session.log"
    # Session log lines start with the time; compare from "[log]" on.
    line=$(tr -d '\r' < "$log" | grep -m1 -E '^([0-9:.]+ )?\[log\] SiegeFX ')
    line="[log]${line#*\[log\]}"
    echo "      $line"
    if [[ "$line" == "[log] SiegeFX $ver+"*"(win-x64)" ]]; then echo "PASS  build line"; else echo "FAIL  build line (expected $ver on win-x64)"; failed=1; fi
else
    echo "FAIL  no session log in the prefix"; failed=1
fi
if ls "$logs"/crash-*.log > /dev/null 2>&1; then
    echo "FAIL  a crash log was written"; failed=1
fi
# Under Proton the game's stdout is not relayed: its session log carries the
# self-test (the newest log, the EOS run).
eoslog=$(ls -t "$logs"/session-*.log 2> /dev/null | head -1)
tr -d '\r' < "$eoslog" 2> /dev/null | grep 'selftest-eos\] ' | sed 's/^/      /'
if [ "$eoslog" != "$log" ] && tr -d '\r' < "$eoslog" 2> /dev/null | grep -q 'selftest-eos\] internet play works'; then
    echo "PASS  internet play (EOS)"
else
    echo "FAIL  internet play (EOS) - see $eoslog"; failed=1
fi
[ -s "$work/screen.png" ] && echo "      screen: $work/screen.png"
exit $failed
