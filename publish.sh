#!/usr/bin/env bash
# publish.sh [version]
#
# Builds the release artifacts on Linux. Each is self-contained (the .NET
# runtime and the native libraries ride along) and carries no game data:
# players point the game at their own Dungeon Siege install.
#
#   publish/SiegeFX-<version>-linux-x64.tar.gz        the game for Linux, one
#                                                     folder: run ./SiegeFX
#   publish/SiegeFX-<version>-win-x64.zip             the game for Windows,
#                                                     SiegeFX.exe as one file
#   publish/SiegeSmith-v<csproj Version>-win-x64.zip  the modding studio
#
# <version> (e.g. v0.5.0) names the SiegeFX artifacts and is the version the
# build reports in its session log; the default, "dev", keeps the version in
# the csproj. Both games bundle the optional EOS module (internet play), each
# with its own native library, when an EOS SDK is unpacked at $EOS_SDK_ROOT
# (default: EOS/SDK beside the repo folder), with the game's eos_config.txt
# from $EOS_CONFIG (default: the save folder); without the SDK they are LAN and
# direct-IP builds. DOTNET picks the dotnet executable (default: dotnet on PATH).
set -euo pipefail
repo=$(cd "$(dirname "$0")" && pwd)
dotnet=${DOTNET:-dotnet}
ver=${1:-dev}
out="$repo/publish"
stamp=()
[ "$ver" != dev ] && stamp=(-p:Version="${ver#v}")

# Every artifact starts from clean build trees, so nothing stale is published.
rm -rf "$out"
for p in SiegeFX.Runtime SiegeFX.Core SiegeFX.Audio SiegeFX.Net.Eos SiegeSmith; do
    rm -rf "$repo/src/$p/bin" "$repo/src/$p/obj"
done
mkdir -p "$out"

echo "=== EOS module (optional; a LAN and direct-IP build without it) ==="
eos_sdk=${EOS_SDK_ROOT:-$(dirname "$repo")/EOS/SDK}
creds=${EOS_CONFIG:-${XDG_DATA_HOME:-$HOME/.local/share}/SiegeFX/Saves/eos_config.txt}
eos=false
# The module is stamped with the game's version: it binds to the SiegeFX.Core
# the game carries, and inside the single-file Windows exe no other copy exists.
eos_build() {   # eos_build <EosPlatform> <output folder>
    "$dotnet" build "$repo/src/SiegeFX.Net.Eos" -c Release --nologo -p:EosSdkRoot="$eos_sdk" \
        -p:EosPlatform="$1" "${stamp[@]}" -o "$2" >> "$out/eos-build.log" 2>&1
}
if [ ! -d "$eos_sdk/Source" ]; then
    echo "  no EOS SDK at $eos_sdk - shipping LAN/direct-IP only."
elif ! eos_build Windows64 "$out/eos-win" || ! eos_build Linux "$out/eos-linux"; then
    echo "  EOS module build failed (see $out/eos-build.log) - shipping LAN/direct-IP only."
else
    eos=true
    [ -f "$creds" ] || echo "  ! no eos_config.txt at $creds - internet play stays on LAN until a player supplies credentials."
fi
bundle_eos() {  # bundle_eos <artifact folder> <module build> <native library>
    $eos || return 0
    cp "$2/SiegeFX.Net.Eos.dll" "$2/$3" "$1/" && echo "  + SiegeFX.Net.Eos.dll, $3"
    if [ -f "$creds" ]; then cp "$creds" "$1/eos_config.txt" && echo "  + eos_config.txt (the game's credentials)"; fi
}

echo
echo "=== SiegeFX for Linux (self-contained linux-x64) ==="
linux="SiegeFX-$ver-linux-x64"
stage="$out/$linux"
"$dotnet" publish "$repo/src/SiegeFX.Runtime" -c Release -f net10.0 -r linux-x64 --self-contained \
    -p:DebugType=embedded "${stamp[@]}" -o "$stage" --nologo
cp "$repo/packaging/README.txt" "$stage/README.txt"
cp "$repo/LICENSE" "$repo/THIRD-PARTY-NOTICES.txt" "$stage/"
bundle_eos "$stage" "$out/eos-linux" libEOSSDK-Linux-Shipping.so
# The desktop entry and the icons, laid out as under /usr/share so a package
# or a per-user install copies them as they are. The icons are the PNG frames
# of the Windows icon, copied out byte for byte.
install -Dm644 "$repo/packaging/siegefx.desktop" "$stage/share/applications/siegefx.desktop"
ico="$repo/src/SiegeFX.Runtime/Assets/siegefx.ico"
u32() { od -An -tu4 -j "$1" -N4 "$ico" | tr -d ' '; }
frames=$(od -An -tu2 -j 4 -N2 "$ico" | tr -d ' ')
for ((i = 0; i < frames; i++)); do
    entry=$((6 + 16 * i))
    px=$(od -An -tu1 -j "$entry" -N1 "$ico" | tr -d ' ')
    [ "$px" -eq 0 ] && px=256
    png="$stage/share/icons/hicolor/${px}x${px}/apps/siegefx.png"
    mkdir -p "$(dirname "$png")"
    tail -c +$(($(u32 $((entry + 12))) + 1)) "$ico" | head -c "$(u32 $((entry + 8)))" > "$png"
    if [ "$(od -An -tx1 -N8 "$png" | tr -d ' ')" != 89504e470d0a1a0a ]; then
        echo "publish: icon frame ${px}x${px} in $ico is not a PNG" >&2
        exit 1
    fi
done
tar -C "$out" --sort=name --owner=0 --group=0 --numeric-owner -czf "$out/$linux.tar.gz" "$linux"

echo
echo "=== SiegeFX for Windows (single file, self-contained win-x64) ==="
win="$out/SiegeFX"
"$dotnet" publish "$repo/src/SiegeFX.Runtime" -c Release -f net10.0-windows10.0.22621.0 \
    -p:PublishSingleFile=true -p:DebugType=embedded "${stamp[@]}" -o "$win" --nologo
bundle_eos "$win" "$out/eos-win" EOSSDK-Win64-Shipping.dll
cp "$repo/THIRD-PARTY-NOTICES.txt" "$win/"

echo
echo "=== SiegeSmith (single file, self-contained win-x64) ==="
smith="$out/SiegeSmith"
"$dotnet" publish "$repo/src/SiegeSmith" -c Release -p:PublishSingleFile=true -p:DebugType=embedded \
    -o "$smith" --nologo
smith_ver=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$repo/src/SiegeSmith/SiegeSmith.csproj" | head -1)

# The Windows folders should hold the exe, THIRD-PARTY-NOTICES.txt and the
# optional EOS files (SiegeFX) and the exe alone (SiegeSmith); list them so a
# growing file count is seen.
echo
echo "=== publish/SiegeFX ===";    ls -1 "$win"
echo "=== publish/SiegeSmith ==="; ls -1 "$smith"

echo
echo "=== zipping ==="
(cd "$win" && zip -q -r "$out/SiegeFX-$ver-win-x64.zip" .)
(cd "$smith" && zip -q -r "$out/SiegeSmith-v$smith_ver-win-x64.zip" .)
ls -sh1 "$out"/*.tar.gz "$out"/*.zip
