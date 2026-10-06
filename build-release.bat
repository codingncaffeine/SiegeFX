@echo off
rem One-click Release build of the whole solution (engine + SiegeSmith).
rem Uses the dotnet CLI directly (the .NET 10 SDK; global.json pins the band).
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
echo   Game:       src\SiegeFX.Runtime\bin\Release\net10.0-windows10.0.22621.0\SiegeFX.exe
echo   SiegeSmith: src\SiegeSmith\bin\Release\net10.0-windows\SiegeSmith.exe
