@echo off
setlocal EnableDelayedExpansion
rem ===========================================================================
rem  publish-alpha.bat [version]
rem
rem  Builds the two alpha release artifacts as SINGLE-FILE self-contained
rem  executables (no loose DLLs; .NET runtime + native libraries ride inside,
rem  self-extracting on first launch):
rem
rem    publish\SiegeFX\SiegeFX.exe          the game
rem      (+ SiegeFX.Net.Eos.dll, EOSSDK-Win64-Shipping.dll, eos_config.txt
rem       when the EOS SDK + creds are on the build machine - INTERNET play)
rem    publish\SiegeSmith\SiegeSmith.exe    the modding studio
rem    publish\SiegeFX-<version>-win-x64.zip
rem    publish\SiegeSmith-v<csproj Version>-win-x64.zip
rem
rem  [version] stamps the SiegeFX zip name (e.g. v0.0.2); defaults to "dev".
rem  SiegeSmith's zip is stamped from the <Version> in its own csproj.
rem  Clean-room note: artifacts contain ZERO GPG assets - testers point the
rem  game at their own Dungeon Siege install.
rem ===========================================================================

set "VER=%~1"
if "%VER%"=="" set "VER=dev"
cd /d "%~dp0"

echo.
echo === publishing SiegeFX (single-file, self-contained win-x64) ===
if exist "publish\SiegeFX" rmdir /s /q "publish\SiegeFX"
dotnet publish src/SiegeFX.Runtime -c Release -p:PublishSingleFile=true -p:DebugType=embedded -o publish\SiegeFX --nologo
if errorlevel 1 goto :fail

rem --- Bundle Epic Online Services (INTERNET play). EOS is a reflection-loaded
rem     optional module (not a project reference), so `dotnet publish` above does
rem     NOT pull it in - it must be copied beside the exe as loose files: the
rem     managed wrapper, the native SDK (P/Invoke, can't ride inside single-file),
rem     and the game creds. All three are optional: a build machine without the
rem     EOS SDK or creds still ships a working LAN / direct-IP release. The creds
rem     are the GAME's identity (a GameClient client id that ships in game
rem     binaries by design, regenerable), NOT a personal secret - the standard
rem     EOS distribution model.
echo.
echo === bundling EOS ^(optional; LAN/direct-IP-only release if unavailable^) ===
dotnet build src/SiegeFX.Net.Eos -c Release --nologo
if errorlevel 1 (
  echo   EOS module build failed or SDK missing - shipping LAN/direct-IP only.
) else (
  set "EOSBIN=src\SiegeFX.Net.Eos\bin\Release\net11.0"
  if exist "!EOSBIN!\SiegeFX.Net.Eos.dll" ( copy /y "!EOSBIN!\SiegeFX.Net.Eos.dll" "publish\SiegeFX\" >nul && echo   + SiegeFX.Net.Eos.dll )
  if exist "!EOSBIN!\EOSSDK-Win64-Shipping.dll" ( copy /y "!EOSBIN!\EOSSDK-Win64-Shipping.dll" "publish\SiegeFX\" >nul && echo   + EOSSDK-Win64-Shipping.dll )
  if exist "%LOCALAPPDATA%\SiegeFX\Saves\eos_config.txt" (
    copy /y "%LOCALAPPDATA%\SiegeFX\Saves\eos_config.txt" "publish\SiegeFX\" >nul && echo   + eos_config.txt ^(bundled game creds^)
  ) else (
    echo   ! no eos_config.txt found - INTERNET play falls back to LAN until a joiner supplies creds.
  )
)

rem --- Reproduce the EOS SDK's third-party license notices beside the build.
rem     Required by the EOS terms whenever the SDK ships; harmless in a
rem     LAN/direct-IP-only build. See THIRD-PARTY-NOTICES.txt / README.
if exist "THIRD-PARTY-NOTICES.txt" ( copy /y "THIRD-PARTY-NOTICES.txt" "publish\SiegeFX\" >nul && echo   + THIRD-PARTY-NOTICES.txt )

echo.
echo === publishing SiegeSmith (single-file, self-contained win-x64) ===
if exist "publish\SiegeSmith" rmdir /s /q "publish\SiegeSmith"
dotnet publish src/SiegeSmith -c Release -p:PublishSingleFile=true -p:DebugType=embedded -o publish\SiegeSmith --nologo
if errorlevel 1 goto :fail

rem SiegeFX should be just the exe plus the 3 optional EOS files and
rem THIRD-PARTY-NOTICES.txt; SiegeSmith just its exe. Anything else is a
rem packaging regression worth seeing - list both folders so a growing file
rem count is obvious.
echo.
echo === publish\SiegeFX ===
dir /b "publish\SiegeFX"
echo === publish\SiegeSmith ===
dir /b "publish\SiegeSmith"

echo.
echo === zipping ===
powershell -NoProfile -Command ^
  "$smithVer = (Select-Xml -Path 'src/SiegeSmith/SiegeSmith.csproj' -XPath '//Version').Node.InnerText;" ^
  "Compress-Archive -Force -Path 'publish\SiegeFX\*' -DestinationPath 'publish\SiegeFX-%VER%-win-x64.zip';" ^
  "Compress-Archive -Force -Path 'publish\SiegeSmith\*' -DestinationPath ('publish\SiegeSmith-v' + $smithVer + '-win-x64.zip');" ^
  "Get-ChildItem publish\*.zip | ForEach-Object { '{0}  {1:N1} MB' -f $_.Name, ($_.Length / 1MB) }"
if errorlevel 1 goto :fail

echo.
echo Done. Artifacts under publish\.
exit /b 0

:fail
echo.
echo publish-alpha FAILED (see output above).
exit /b 1
