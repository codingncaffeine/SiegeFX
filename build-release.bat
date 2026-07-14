@echo off
rem One-click Release build of the whole solution (engine + SiegeSmith).
rem Uses the dotnet CLI directly, so it works even when Visual Studio's
rem "Use previews of the .NET SDK" option is off (net11.0 is a preview SDK).
rem NOTE: close SiegeSmith/SiegeFX first - a running exe locks its own file.
cd /d "%~dp0"
dotnet build SiegeFX.sln -c Release --nologo
if errorlevel 1 (
    echo.
    echo BUILD FAILED - see errors above. Is SiegeSmith or the game still running?
    exit /b 1
)
echo.
echo Build OK.
echo   Game:       src\SiegeFX.Runtime\bin\Release\net11.0\SiegeFX.exe
echo   SiegeSmith: src\SiegeSmith\bin\Release\net11.0-windows\SiegeSmith.exe
