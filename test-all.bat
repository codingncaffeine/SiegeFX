@echo off
setlocal
cd /d "%~dp0"

set DS1=D:\GOG Games\Dungeon Siege
set TOOL=src\SiegeFX.Tools\bin\Release\net8.0\siegefx.exe
set RUN=src\SiegeFX.Runtime\bin\Release\net8.0\SiegeFX.Runtime.dll
set REFS=_ds1refs

if not exist "%TOOL%" goto NEEDBUILD
if not exist "%RUN%"  goto NEEDBUILD

:MENU
cls
echo ============================================================
echo   SiegeFX v0.8.0 phase-by-phase smoke test
echo ============================================================
echo   DS1 install : %DS1%
echo.
echo   1.  Phase 1 - Tank listing (Logic.dsres)
echo   2.  Phase 2 - RAW texture decode (goblin.raw to PNG)
echo   3.  Phase 4 - Static mesh viewer (boot.asp)
echo   4.  Phase 5 - GAS parser (stitch_ds_r2.gas dump)
echo   5.  Phase 6 - Single region viewer (fh_r1)
echo   6.  Phase 6 - World streaming (walk between regions)
echo   7.  Phase 7 - Skeletal animation (goblin walk clip)
echo   8.  Phase 9a - Skrit-driven animation (basic_walk)
echo   9.  Phase 8d - Skrit tick harness (CLI, no viewer)
echo   10. Phase 10a - Template store (goblin grunt archetype resolution)
echo   11. Phase 10b - Region actor instance loader (fh_r1)
echo   12. Phase 10c+d - Spawn 181 actors, tick + broadcast (fh_r1)
echo   13. Phase 10e - Play region (walk into fh_r1, 181 actors idling)
echo   14. Phase 11a - Walkable-surface nav (fh_r1 stats + world-wide fuzz)
echo   15. Phase 11b - A* pathfinding (fh_r1 hand path + world-wide fuzz)
echo.
echo   B.  Rebuild (dotnet build -c Release)
echo   Q.  Quit
echo.
set /p CHOICE=Choose:

if /i "%CHOICE%"=="1" goto T1
if /i "%CHOICE%"=="2" goto T2
if /i "%CHOICE%"=="3" goto T3
if /i "%CHOICE%"=="4" goto T4
if /i "%CHOICE%"=="5" goto T5
if /i "%CHOICE%"=="6" goto T6
if /i "%CHOICE%"=="7" goto T7
if /i "%CHOICE%"=="8" goto T8
if /i "%CHOICE%"=="9" goto T9
if /i "%CHOICE%"=="10" goto T10
if /i "%CHOICE%"=="11" goto T11
if /i "%CHOICE%"=="12" goto T12
if /i "%CHOICE%"=="13" goto T13
if /i "%CHOICE%"=="14" goto T14
if /i "%CHOICE%"=="15" goto T15
if /i "%CHOICE%"=="B" goto BUILD
if /i "%CHOICE%"=="Q" goto END
goto MENU

:T1
echo.
echo --- Phase 1: first 40 entries of Logic.dsres ---
"%TOOL%" tank list "%DS1%\Resources\Logic.dsres" | more
pause
goto MENU

:T2
echo.
echo --- Phase 2: decoding goblin.raw to goblin.png ---
"%TOOL%" raw decode "%REFS%\goblin.raw" "%REFS%\goblin.png"
if exist "%REFS%\goblin.png" start "" "%REFS%\goblin.png"
pause
goto MENU

:T3
echo.
echo --- Phase 4: boot.asp in viewer (RMB+WASD to fly, Esc to quit) ---
dotnet "%RUN%" "%REFS%\boot.asp"
goto MENU

:T4
echo.
echo --- Phase 5: dump stitch_ds_r2.gas parse tree ---
"%TOOL%" gas dump "%REFS%\stitch_ds_r2.gas" | more
pause
goto MENU

:T5
echo.
echo --- Phase 6: load fh_r1 (Farmhouse region 1) ---
dotnet "%RUN%" --region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" /world/maps/map_world/regions/fh_r1
goto MENU

:T6
echo.
echo --- Phase 6: world streaming starting at fh_r1 ---
dotnet "%RUN%" --world "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres"
goto MENU

:T7
echo.
echo --- Phase 7: goblin walk animation ---
dotnet "%RUN%" --anim "%REFS%\goblin.asp" "%REFS%\goblin_walk.prs" "%REFS%\goblin.raw"
goto MENU

:T8
echo.
echo --- Phase 9a: skrit-driven goblin animation (basic_walk.skrit) ---
dotnet "%RUN%" --skrit-anim "%REFS%\goblin.asp" "%REFS%\skrit\basic_walk.skrit" "%REFS%\goblin_walk.prs" --texture "%REFS%\goblin.raw"
goto MENU

:T9
echo.
echo --- Phase 8d: tick basic_walk.skrit for 40 logic frames ---
"%TOOL%" skrit tick "%REFS%\skrit\basic_walk.skrit" --ticks=40 --subanims=1 --event=OnStartChore$
pause
goto MENU

:T10
echo.
echo --- Phase 10a: template store - resolve 3W_goblin_grunt archetype ---
echo [expect: chain 3W_goblin_grunt -^> 3W_base_goblin -^> actor_evil -^> actor,
echo          aspect.model inherited, 5 chore entries]
echo.
"%TOOL%" templates show "%DS1%\Resources\Logic.dsres" 3W_goblin_grunt
echo.
echo --- now listing all 3W_goblin_* templates ---
"%TOOL%" templates list "%DS1%\Resources\Logic.dsres" --prefix=3W_goblin
pause
goto MENU

:T11
echo.
echo --- Phase 10b: actor instances in fh_r1 (Farmhouse region 1) ---
echo [expect: ~181 actors, templates include krug_scout/phrak/gremal/chicken]
echo.
"%TOOL%" region actors "%DS1%\Maps\World.dsmap" /world/maps/map_world/regions/fh_r1
pause
goto MENU

:T12
echo.
echo --- Phase 10c+d: spawn 181 actors in fh_r1, broadcast OnStartChore$ via bus ---
echo [expect: spawned 181/181, all in LoopForever$, bus posted 1 / delivered 181]
echo.
"%TOOL%" region spawn "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1 --broadcast=OnStartChore$
pause
goto MENU

:T13
echo.
echo --- Phase 10e: fh_r1 with terrain + 181 actors, skrit-driven ---
echo [RMB+WASD to fly, Esc to quit]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
goto MENU

:T14
echo.
echo --- Phase 11a: fh_r1 walkable-surface nav stats ---
echo [expect: ~1700 snodes placed, ~2400 floor groupings, ~27k floor faces]
echo.
"%TOOL%" region nav "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" /world/maps/map_world/regions/fh_r1
echo.
echo --- Phase 11a: world-wide nav fuzz (81 regions, ~7400 unique SNOs) ---
echo [expect: 0 region failures, floor ~160k, water ~20k, ignored ~430k]
echo.
"%TOOL%" region nav-fuzz "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres"
pause
goto MENU

:T15
echo.
echo --- Phase 11b: hand-picked path in fh_r1 (10,0,10 to 30,0,30) ---
echo [expect: ~30-40 tris, ~35-unit centroid length]
echo.
"%TOOL%" region path "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" /world/maps/map_world/regions/fh_r1 "10,0,10" "30,0,30"
echo.
echo --- Phase 11b: world-wide A* fuzz (81 regions, 20 samples each) ---
echo [expect: biggest-component A* = 100%%; random-pair ~40%% reflects topology]
echo.
"%TOOL%" region path-fuzz "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres"
pause
goto MENU

:BUILD
echo.
dotnet build -c Release
pause
goto MENU

:NEEDBUILD
echo.
echo Build output missing. Running dotnet build -c Release first...
dotnet build -c Release
if errorlevel 1 (
  echo Build failed. Fix errors and re-run.
  pause
  goto END
)
goto MENU

:END
endlocal
