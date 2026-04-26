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
echo   16. Phase 11c - Nav follower (walk fh_r1 corridor tick-by-tick)
echo   17. Phase 11d - Actors wander fh_r1 (181 followers on nav mesh)
echo   18. Phase 12a - Template stats (goblin grunt + all 3W_goblin_* prefix)
echo   19. Phase 12b - Combat sim (1000 duels: grunt vs grunt, guard vs grunt)
echo   20. Phase 12c - Debug attack in fh_r1 (press F to hit nearest goblin)
echo   21. Phase 12d - Loot table (grunt + krug scout, 10000-roll distribution)
echo   22. Phase 13a-e - Farmboy PC + chase cam + LMB move + RMB attack + fair-fight stats (fh_r1)
echo   23. Phase 14a-d - Pickup + equipment + weapon render (fh_r1)
echo   24. Phase 15a   - Text overlay (DS1 copperplate font, fh_r1)
echo   25. Phase 15b   - HP/MP HUD bars (live values, fh_r1)
echo   26. Phase 15c   - Grid inventory panel (press I to toggle, fh_r1)
echo   27. Phase 15d   - Pause menu (Esc to open; Resume / Quit, fh_r1)
echo   28. Phase 16a   - Formulas dump (formulas.gas -^> typed values)
echo   29. Phase 16b   - HP/MP regen (~0.25/0.333 per sec at 10/10/10, fh_r1)
echo   30. Phase 16c   - NPC aggro: walk into a krug, watch HP drop + regen (fh_r1)
echo   31. Phase 16d   - XP + level: kill goblins, watch Lv/XP line on HUD (fh_r1)
echo   32. Phase 17a   - Spells: dump catalog + show spell_zap by magic level
echo   33. Phase 17a   - Spells: cast spell_zap with Q (mana 1, dmg 4-7 at L1, fh_r1)
echo   34. Phase 17b   - Spell visuals: cyan bolt + face-snap on Q-cast (fh_r1)
echo   35. Phase 17c   - Heal spell + W slot: spell_healing_wind self-cast (fh_r1)
echo   36. Phase 18a   - Audio: cast SFX (Q = zap_cast.wav, W = healing_wind_cast.wav, fh_r1)
echo   37. Phase 18b   - Audio: melee swing/hit/miss + monster death + level-up SFX (fh_r1)
echo   38. Phase 18c   - Audio: 3D positional pan + falloff (walk away from a kill, listen, fh_r1)
echo   39. Phase 19a   - Save: SaveFile JSON round-trip self-test (no window)
echo   40. Phase 19c   - Save/Load: F5 quicksave + F9 quickload (kill stuff, F5, kill more, F9, fh_r1)
echo   41. Phase 20a   - Dialogue parser self-test + RMB-talk to Edgaar in fh_r1
echo   42. Phase 20b   - Quest log overlay (Accept Edgaar quest, press L, fh_r1)
echo   43. Phase 20c   - Kill objectives + goal markers (kill 5 krug for Edgaar, fh_r1)
echo   44. Phase 20d   - Vendor trade + gold purse (talk to Norick, buy/sell, fh_r1)
echo   45. Phase 21a-1 - Neighbor terrain preload (fh_r1 + first-ring neighbors visible)
echo   46. Phase 21a-2 - Cross-boundary nav + actors + dialogue (walk into neighbor regions)
echo   47. Phase 21a-3 - Rolling preload (no more invisible wall, walk arbitrarily far)
echo   48. Phase 21b-1 - --diag mode (startup timings + per-second frame histogram)
echo   49. Phase 21c-1 - NPC textures + static props (trees/barrels/fences/crops/candles in fh_r1)
echo   50. Phase 21c-5 - Headless prop-texture audit across all 81 regions (CLI, no window)
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
if /i "%CHOICE%"=="16" goto T16
if /i "%CHOICE%"=="17" goto T17
if /i "%CHOICE%"=="18" goto T18
if /i "%CHOICE%"=="19" goto T19
if /i "%CHOICE%"=="20" goto T20
if /i "%CHOICE%"=="21" goto T21
if /i "%CHOICE%"=="22" goto T22
if /i "%CHOICE%"=="23" goto T23
if /i "%CHOICE%"=="24" goto T24
if /i "%CHOICE%"=="25" goto T25
if /i "%CHOICE%"=="26" goto T26
if /i "%CHOICE%"=="27" goto T27
if /i "%CHOICE%"=="28" goto T28
if /i "%CHOICE%"=="29" goto T29
if /i "%CHOICE%"=="30" goto T30
if /i "%CHOICE%"=="31" goto T31
if /i "%CHOICE%"=="32" goto T32
if /i "%CHOICE%"=="33" goto T33
if /i "%CHOICE%"=="34" goto T34
if /i "%CHOICE%"=="35" goto T35
if /i "%CHOICE%"=="36" goto T36
if /i "%CHOICE%"=="37" goto T37
if /i "%CHOICE%"=="38" goto T38
if /i "%CHOICE%"=="39" goto T39
if /i "%CHOICE%"=="40" goto T40
if /i "%CHOICE%"=="41" goto T41
if /i "%CHOICE%"=="42" goto T42
if /i "%CHOICE%"=="43" goto T43
if /i "%CHOICE%"=="44" goto T44
if /i "%CHOICE%"=="45" goto T45
if /i "%CHOICE%"=="46" goto T46
if /i "%CHOICE%"=="47" goto T47
if /i "%CHOICE%"=="48" goto T48
if /i "%CHOICE%"=="49" goto T49
if /i "%CHOICE%"=="50" goto T50
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

:T16
echo.
echo --- Phase 11c: nav follower walks fh_r1 corridor (10,0,10 to 30,0,30 at 6 u/s) ---
echo [expect: reaches goal in ~100 ticks (20 Hz), ~32 units walked vs ~28 straight-line]
echo.
"%TOOL%" region follow "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" /world/maps/map_world/regions/fh_r1 "10,0,10" "30,0,30"
pause
goto MENU

:T17
echo.
echo --- Phase 11d: fh_r1 with 181 wandering actors on nav mesh ---
echo [expect: nav mesh ~27k tri / 0 non-manifold; 181 followers wandering]
echo [RMB+WASD to fly, Esc to quit — watch actors pathing around obstacles]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
goto MENU

:T18
echo.
echo --- Phase 12a: combat stats for 3W_goblin_grunt ---
echo [expect: life 1162, damage 142-204, defense 554, walk 2.51, combatant=yes]
echo.
"%TOOL%" templates stats "%DS1%\Resources\Logic.dsres" 3W_goblin_grunt
echo.
echo --- Phase 12a: all 3W_goblin_* variants (3 combatants, 5 inert parts) ---
echo.
"%TOOL%" templates stats "%DS1%\Resources\Logic.dsres" --prefix=3W_goblin
pause
goto MENU

:T19
echo.
echo --- Phase 12b: grunt vs grunt, 1000 duels, seed=42 ---
echo [expect: 1000/1000 kills, mean hits ~10, mean damage ~112]
echo.
"%TOOL%" templates combat "%DS1%\Resources\Logic.dsres" 3W_goblin_grunt 3W_goblin_grunt --duels=1000 --seed=42
echo.
echo --- Phase 12b: guard vs grunt, 1000 duels, seed=42 ---
echo [expect: 1000/1000 kills, mean hits ~4, mean damage ~284]
echo.
"%TOOL%" templates combat "%DS1%\Resources\Logic.dsres" 3W_goblin_guard 3W_goblin_grunt --duels=1000 --seed=42
pause
goto MENU

:T20
echo.
echo --- Phase 12c: debug attack in fh_r1 ---
echo [walk up to a goblin, press F — expect "debug-attack: hit ... for ~N (M/1163)"]
echo [after ~5-6 hits the actor freezes in place with *** DEAD *** log]
echo [RMB+WASD to fly, F to attack nearest combatant in front, Esc to quit]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
goto MENU

:T21
echo.
echo --- Phase 12d: loot table for 3W_goblin_grunt (structure + 10000-roll distribution) ---
echo [expect: 100%% equipped hm_g_c_1h1m_low, ~6%% common drops, ~0.1%% rare/unique]
echo.
"%TOOL%" templates loot "%DS1%\Resources\Logic.dsres" 3W_goblin_grunt --rolls=10000 --seed=42
echo.
echo --- Phase 12d: loot table for krug_scout (common fh_r1 spawn, 1000 rolls) ---
echo [expect: 100%% equipped dg_g_c_1h_fun, ~12%% drop one of melee/potion/mana]
echo.
"%TOOL%" templates loot "%DS1%\Resources\Logic.dsres" krug_scout --rolls=1000 --seed=42
pause
goto MENU

:T22
echo.
echo --- Phase 13a-e: Farmboy PC + chase cam + LMB move + RMB attack + fair-fight stats (fh_r1) ---
echo [expect: one Farmboy (male human) spawns at the NPC centroid]
echo [LMB on terrain to move; RMB-tap on a goblin to attack]
echo [RMB-drag still orbits the yaw — tap vs drag split by pixel drift]
echo [C toggles chase/fly cam; F still fires the camera-forward debug attack]
echo [13e: NPCs move at template walk_velocity; Farmboy hits with 1-3 dmg (multi-hit kills)]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
goto MENU

:T23
echo.
echo --- Phase 14a-d: Pickup + equipment + weapon render (fh_r1) ---
echo [Farmboy spawns visibly wielding dg_g_d_1h_fun (fun dagger, 2-4 dmg)]
echo [kill a goblin, walk onto the beige pile cube to auto-pickup]
echo [upgraded weapons auto-equip and swap the rendered model on the hand]
echo [console logs equipment, pickup, equipped, and weapon-load events]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T24
echo.
echo --- Phase 15a: Text overlay (DS1 copperplate-light, fh_r1) ---
echo [expect: white "SiegeFX" tag in the top-left corner of the window]
echo [a second line shows the Farmboy's live x/y/z as he moves]
echo [text uses DS1's b_gui_fnt_12p_copperplate-light font from Objects.dsres]
echo [console prints "hud font: ..." once the atlas decodes]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T25
echo.
echo --- Phase 15b: HP/MP HUD bars (fh_r1) ---
echo [expect: red HP bar under the SiegeFX/coords text in the top-left]
echo [bar is 200px wide, captioned "HP 50/50" (Farmboy starts full)]
echo [no MP bar — Farmboy template has max_mana=0 so it stays hidden]
echo [walk into a krug and let it hit you to see the HP bar drain]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T26
echo.
echo --- Phase 15c: Grid inventory panel (fh_r1) ---
echo [press I to toggle a centered 8x5 inventory grid]
echo [picked-up items fill cells left-to-right, top-to-bottom]
echo [each cell shows the trimmed template ref (icons land in a later phase)]
echo [press I again or Esc to dismiss the panel]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T27
echo.
echo --- Phase 15d: Pause menu (fh_r1) ---
echo [press Esc to open a centered "Paused" panel with two buttons]
echo [hover a button to highlight; LMB clicks while paused don't retarget]
echo [Resume closes the menu; Quit closes the window]
echo [Esc again from the menu also resumes]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T28
echo.
echo --- Phase 16a: Formulas dump (Logic.dsres -^> formulas.gas) ---
echo [expect: 10/10/10 -^> MaxLife=49.0  MaxMana=30.0]
echo [expect: gains rows sum to 1.00; XP table ~151 entries]
echo [expect: lr 1/4 -^> 0.250 HP/sec at str=10; mr 1/3 -^> 0.333 MP/sec at int=10]
echo.
"%TOOL%" formulas dump "%DS1%\Resources\Logic.dsres"
pause
goto MENU

:T29
echo.
echo --- Phase 16b: HP/MP regen (fh_r1) ---
echo [press H to take 5 HP + 5 MP off the player (offline regen check)]
echo [then sit still and watch the bars climb back up]
echo [at 10/10/10 a fresh hero recovers ~0.25 HP/sec and ~0.333 MP/sec]
echo [full HP from 0 takes ~3 min; full MP from 0 takes ~90 sec]
echo [also tests the shutdown-crash fix on Esc-^>Quit and window X]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T31
echo.
echo --- Phase 16d: XP + level (fh_r1) ---
echo [HUD shows "Lv 1  XP 0/N" under the HP/MP bars on spawn]
echo [click goblins to attack; XP ticks up by damage dealt + kill bonus]
echo [level 2 fires around ~150-200 xp; console prints "*** LEVEL UP! ***"]
echo [on level-up: STR/DEX/INT auto-grow by Melee proportional gains (0.64/0.27/0.09)]
echo [HP bar max grows on level-up; current HP unchanged (DS1 doesn't auto-heal)]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T32
echo.
echo --- Phase 17a: Spells dump + spell_zap evaluated ---
"%TOOL%" spells dump "%DS1%\Resources\Logic.dsres"
echo.
"%TOOL%" spells show "%DS1%\Resources\Logic.dsres" spell_zap
pause
goto MENU

:T33
echo.
echo --- Phase 17a: Cast spell_zap (fh_r1) ---
echo [HUD shows "spellbook: primary <- spell_zap" in the launch log]
echo [aim cursor at a krug, press Q to cast: mana drops by 1, target takes 4-7 dmg]
echo [outside 8u range: "out of range" floats up; no mana: "no mana" floats up]
echo [cooldown is 0.15s so spam-Q just rate-limits to ~6 casts/sec]
echo [kills via spell credit XP under SkillKind.CombatMagic]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T34
echo.
echo --- Phase 17b: Spell visuals (fh_r1) ---
echo [press Q on a krug; a cyan bolt streaks from your chest to the target]
echo [bolt lasts ~0.3s with a 5-dot fading trail]
echo [PC snaps to face the target on cast (no more shooting out of his back)]
echo [damage popup + mana drain are unchanged from 17a]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T35
echo.
echo --- Phase 17c: Heal spell + W slot (fh_r1) ---
echo [Q casts spell_zap (offensive instant-hit, primary slot)]
echo [W casts spell_healing_wind (self-target heal, secondary slot)]
echo [W is silent at full HP ("at full health"); take damage from a krug first]
echo [L1 heal: ~3.77 HP for 10.3 mana (so a fresh hero gets ~3 casts)]
echo [3-second cooldown on the heal slot, independent from the Q cooldown]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T38
echo.
echo --- Phase 18c: 3D positional audio (fh_r1) ---
echo [Hit a krug to your LEFT: hit/miss sound pans left in headphones]
echo [Walk ~30 units away from a fight: hits and screams fade out (max=40 units)]
echo [Cast SFX (Q/W) stay player-locked since they're "your" sounds]
echo [Console logs: 'audio: ... InverseDistanceClamped attenuation']
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T37
echo.
echo --- Phase 18b: Combat SFX (fh_r1) ---
echo [RMB on a krug: swing variant + hit (or miss if dealt=0)]
echo [Kill it: matching die_*.wav fires (krug_scout / krug_dog / goblin / gremal)]
echo [Cross an XP threshold: level_up_melee.wav punctuates the LEVEL UP toast]
echo [Console should show:  audio: 'swing_01..04', 'hit_flesh_1..5', 'die_*', 'level_up' loaded]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T36
echo.
echo --- Phase 18a: Audio engine + cast SFX (fh_r1) ---
echo [Q (zap)  plays /sound/effects/s_e_spell_zap_cast.wav from Sound.dsres]
echo [W (heal) plays /sound/effects/s_e_spell_healing_wind_cast.wav]
echo [Console should show:  audio: OpenAL Soft up (16 voices)]
echo [And:  audio: 'spell_zap_cast' / 'spell_healing_wind_cast' loaded]
echo [SFX-disabled fallback: any OpenAL failure leaves the rest of the game playable]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T39
echo.
echo --- Phase 19a: SaveFile JSON round-trip (no window) ---
echo [expect: "[selftest-save] OK - 3 actor(s), player + camera, schema v1 round-tripped at <path>"]
echo [exits 0 on success, 1 with field-by-field diffs on failure]
echo.
dotnet "%RUN%" --selftest-save
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
pause
goto MENU

:T40
echo.
echo --- Phase 19c: F5 quicksave + F9 quickload (fh_r1) ---
echo [Pre-save: kill 1-2 krug or take HP damage to make state interesting]
echo [Press F5: console logs "save: wrote N actor(s) + M pile(s) -^> ...quicksave.save"]
echo [Continue: kill more stuff, pick up loot, walk around, level up]
echo [Press F9: scene snaps back to F5 state — dead krug revive (if alive at save)]
echo [or stay dead (if dead at save); HP/MP/XP/Level revert; piles return]
echo [Save lives at: %%LOCALAPPDATA%%\SiegeFX\Saves\quicksave.save]
echo [Region check: a save from a different region path is refused on F9]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T41
echo.
echo --- Phase 20a: dialogue parser self-test (no window) ---
echo [expect: "[selftest-dialogue] OK - edgaar branching tree (3 nodes...) parsed correctly"]
echo [exits 0 on success, 1 with field diffs on failure]
echo.
dotnet "%RUN%" --selftest-dialogue
set EXITCODE=%ERRORLEVEL%
echo.
echo === selftest exited with code %EXITCODE% ===
echo.
echo --- Phase 20a: visual walkthrough (fh_r1) ---
echo [Walk to Edgaar (the farmer) and right-click him: dialogue panel opens.]
echo [Node 1 has a "More" button: click to advance.]
echo [Node 2 is the quest fork with "Accept" / "Decline":]
echo [  Accept   -^> console logs "talk: quest_edgaar_basement activated"]
echo [  Decline  -^> jumps to the polite-tail node, then "Continue" closes.]
echo [Esc while open closes the panel without firing the quest.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T30
echo.
echo --- Phase 16c: NPC aggro (fh_r1) ---
echo [walk into a krug pen and stop within ~8u; the krug should chase]
echo [once they're adjacent (~1.8u) they swing every 1.5s and chip ~4-8 HP]
echo [step away past ~14u to disengage; HP regen kicks back in]
echo [chickens should still wander uninterested - they have no [attack] block]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T42
echo.
echo --- Phase 20b: Quest log overlay (fh_r1) ---
echo [RMB Edgaar, click "More", click "Accept" — console logs activation]
echo [Press L: quest log opens, "Edgaar Basement" listed under ACTIVE]
echo [Re-pitch: RMB Edgaar, Accept again — log says "re-pitched (already in journal)"]
echo [F5 quicksave + F9 quickload: quest survives the round-trip]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T43
echo.
echo --- Phase 20c: Kill objectives + goal markers (fh_r1) ---
echo [Accept Edgaar's basement quest — krug-kill objective: 0/5]
echo [Quest log (L) shows "(0/5)" beside the objective line]
echo [Yellow chevron paints above the nearest live krug; clamps to screen edge if behind]
echo [Each krug kill increments progress + a "+gold" floats off the corpse]
echo [On the 5th kill the entry flips to COMPLETED and the marker disappears]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T44
echo.
echo --- Phase 20d: Vendor trade + gold purse (fh_r1) ---
echo [Persistent "Gold: N" line under the Lv/XP readout, gold-tinted]
echo [RMB Norick (the trader NPC) — dialogue panel opens]
echo [Walk through dialogue to close it; vendor panel auto-opens (FOR SALE / YOUR ITEMS)]
echo [Click Buy on Iron Two-Handed Sword (50g) — gold debits, item lands in inventory]
echo [Buying a weapon_hand item also auto-equips it via the existing pickup path]
echo [Click Sell on any inventory row — gold credits half list price (5g for unknowns)]
echo [Insufficient gold: console logs "trade: cannot afford ..." and the trade is rejected]
echo [Esc closes the vendor panel without firing the pause menu]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T45
echo.
echo --- Phase 21a-1: Neighbor terrain preload (fh_r1) ---
echo [Launch log shows "neighbor preload: N/M region(s) (X instance(s) ...)"]
echo [Expect M (declared) ~= 2-4 for fh_r1; N (placed) should equal M]
echo [unresolved + dangling stitch counts should be 0 for shipped fh_r1]
echo [In-game: fly to the south/east edge of fh_r1 — neighbor terrain is visible]
echo [WITHOUT this load, the world would just end at the region boundary]
echo [Actors / nav / dialogue still operate only inside fh_r1 — that's 21a-2]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T46
echo.
echo --- Phase 21a-2: Cross-boundary nav + actors + dialogue (fh_r1 + neighbors) ---
echo [Builds on 21a-1: neighbor regions are now first-class for gameplay too]
echo [Launch log: "neighbor preload..." THEN actor + nav line includes neighbors]
echo [In-game: walk south/east past the old fh_r1 boundary — no more invisible wall]
echo [Nav mesh now spans player region + first-ring neighbors as one graph]
echo [Neighbor actors (krug/goblins beyond the boundary) are alive and aggro you]
echo [RMB an NPC living in a neighbor region — their dialogue tree opens normally]
echo [If you fly past the *outer* edge of the preloaded ring you'll still hit a wall]
echo [That's expected — eviction + rolling preload is 21a-3]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T48
echo.
echo --- Phase 21b-1: --diag startup timings + frame histogram ---
echo [Boots fh_r1 with --diag; expect a "diag: startup timings" table at end of OnLoad]
echo [Then a per-second "diag: frame avg=... p50=... p99=... max=..." line during play]
echo [Stages measured: region, neighbor preload, world, play actors, anim, skrit]
echo [Per-frame stats: avg, p50, p99, max in ms; FPS; live actor + region counts]
echo [21b-2/3 will use this output to target the actual hotspots]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1 --diag
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T50
echo.
echo --- Phase 21c-5: Headless prop-texture audit (all regions) ---
echo [Walks every static-prop placement in EVERY shipped DS1 region through]
echo [the same texset rules the runtime uses (template override -^> BMSH]
echo [default -^> -01..-08 variant fallback) and prints per-template misses.]
echo [With Terrain.dsres in the resolver, expect 0 untextured + exit code 0.]
echo.
"%TOOL%" region prop-textures "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" all --terrain="%DS1%\Resources\Terrain.dsres" --top=10 --list-misses
set EXITCODE=%ERRORLEVEL%
echo.
echo === audit exited with code %EXITCODE% ===
pause
goto MENU

:T49
echo.
echo --- Phase 21c-1: NPC textures + static props ---
echo [NPCs (Edgaar/Norick/krug/goblin/farmboy) now render with their authored albedo]
echo [Static props from non_interactive/container/inventory/interactive/emitter .gas:]
echo [   trees, bushes, foliage, candles, chairs, tables, baskets, jugs, dishes,]
echo [   barrels, crates, woodboxes, breakable doors, respawn statues, smoke emitters]
echo [Launch log shows: "static props: N/M placed (K unique mesh(es); skipped ...)"]
echo [The world should look densely populated, not bare terrain dotted with NPCs]
echo [Cross into a neighbor — log shows another "static props:" line for the new region]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T47
echo.
echo --- Phase 21a-3: Rolling preload (no invisible wall) ---
echo [Builds on 21a-2: ring extends as the player crosses into new regions]
echo [Launch log: "neighbor preload..." for the initial fh_r1 ring]
echo [Walk south/east past the boundary — log shows "region change: ... -> ..."]
echo [Then "rolling preload: +N region(s)" + "rolling spawn: M actor(s) live"]
echo [The PC's nav follower is reseated onto the new mesh — clicks route into new terrain]
echo [Already-spawned actors (NPCs + player) keep their world coords across re-anchors]
echo [Walk far enough and the ring keeps extending — no fixed outer wall anymore]
echo [Memory grows monotonically (no eviction in MVP) — fine for ~150-region single sessions]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX.Runtime exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net8.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
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
