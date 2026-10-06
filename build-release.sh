#!/usr/bin/env bash
# One-command Release build of the whole solution (engine, siegefx CLI,
# SiegeSmith). Uses the dotnet CLI directly (the .NET 10 SDK; global.json pins
# the band); DOTNET picks the executable (default: dotnet on PATH).
set -euo pipefail
cd "$(dirname "$0")"
if ! "${DOTNET:-dotnet}" build SiegeFX.sln -c Release --nologo; then
    echo
    echo "BUILD FAILED - see the errors above."
    exit 1
fi
echo
echo "Build OK."
echo "  Game:       src/SiegeFX.Runtime/bin/Release/net10.0/SiegeFX"
echo "  CLI:        src/SiegeFX.Tools/bin/Release/net10.0/siegefx"
echo "  SiegeSmith: src/SiegeSmith/bin/Release/net10.0-windows/ (WPF: runs on Windows)"
