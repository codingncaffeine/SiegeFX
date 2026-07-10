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

echo.
echo === publishing SiegeSmith (single-file, self-contained win-x64) ===
if exist "publish\SiegeSmith" rmdir /s /q "publish\SiegeSmith"
dotnet publish src/SiegeSmith -c Release -p:PublishSingleFile=true -p:DebugType=embedded -o publish\SiegeSmith --nologo
if errorlevel 1 goto :fail

rem Anything besides the exe in the output is a packaging regression worth
rem seeing - list both folders so a growing file count is obvious.
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
