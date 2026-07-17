@echo off
setlocal
cd /d "%~dp0"

REM Defaults — override any of these by passing --ds1=PATH, --tool=PATH,
REM --run=PATH, or --refs=PATH on the command line. Examples:
REM   test-all.bat --ds1="C:\Program Files (x86)\Steam\steamapps\common\Dungeon Siege 1"
REM   test-all.bat --ds1=D:\GOG\DS --refs=my_refs
REM Pass --help (or -h, /?) to print the parameter list.
set "DS1=D:\GOG Games\Dungeon Siege"
set "TOOL=src\SiegeFX.Tools\bin\Release\net11.0\siegefx.exe"
set "RUN=src\SiegeFX.Runtime\bin\Release\net11.0\SiegeFX.dll"
set "REFS=_ds1refs"

:parseargs
if "%~1"=="" goto doneargs
set "ARG=%~1"
if /i "%ARG%"=="--help" goto usage
if /i "%ARG%"=="-h"     goto usage
if /i "%ARG%"=="/?"     goto usage
if /i "%ARG:~0,6%"=="--ds1="  goto setds1
if /i "%ARG:~0,7%"=="--tool=" goto settool
if /i "%ARG:~0,6%"=="--run="  goto setrun
if /i "%ARG:~0,7%"=="--refs=" goto setrefs
echo unknown argument: %~1
echo run "test-all.bat --help" for the parameter list
exit /b 1

:setds1
set "DS1=%ARG:~6%"
shift
goto parseargs

:settool
set "TOOL=%ARG:~7%"
shift
goto parseargs

:setrun
set "RUN=%ARG:~6%"
shift
goto parseargs

:setrefs
set "REFS=%ARG:~7%"
shift
goto parseargs

:usage
echo Usage: test-all.bat [--ds1=PATH] [--tool=PATH] [--run=PATH] [--refs=PATH]
echo.
echo   --ds1=PATH   Dungeon Siege install root (default: %DS1%)
echo   --tool=PATH  siegefx.exe path           (default: %TOOL%)
echo   --run=PATH   SiegeFX.dll path   (default: %RUN%)
echo   --refs=PATH  local reference assets dir (default: %REFS%)
echo.
echo Quote paths that contain spaces, e.g.:
echo   test-all.bat --ds1="C:\Program Files (x86)\Steam\steamapps\common\Dungeon Siege 1"
exit /b 0

:doneargs

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
echo   44. Phase 25c   - Authored store screen (Stonebridge shopkeepers, buy/sell, bt_r1)
echo   45. Phase 21a-1 - Neighbor terrain preload (fh_r1 + first-ring neighbors visible)
echo   46. Phase 21a-2 - Cross-boundary nav + actors + dialogue (walk into neighbor regions)
echo   47. Phase 21a-3 - Rolling preload (no more invisible wall, walk arbitrarily far)
echo   48. Phase 21b-1 - --diag mode (startup timings + per-second frame histogram)
echo   49. Phase 21c-1 - NPC textures + static props (trees/barrels/fences/crops/candles in fh_r1)
echo   50. Phase 21c-5 - Headless prop-texture audit across all 81 regions (CLI, no window)
echo   51. Phase 21d-1 - Balance curves audit (XP/HP/MP/regen L1..L50, all skills, CLI)
echo   52. Phase 21d-2a-i - ASP subset fuzz (parse all .asp in Objects.dsres, validate subsets)
echo   53. Phase 21d-2a-ii - Per-subset texture render (visually verify farmboy clothing in fh_r1)
echo   54. Phase 21d-2a-iii prep - Actor-coverage audit across all 81 regions (CLI, no window)
echo   55. Phase 21d-2a-iv - BTRI cornerStart fix (visually verify farmboy webbing gone in fh_r1)
echo   56. Phase 21d-2a-v  - Farmboy texture diag (magenta fallback + tex-resolve log)
echo   57. Phase 21d-2a-v  - Subset-tint diag (each ASP subset = solid color: red/grn/blu/yel/mag)
echo   58. Phase 21d-2a-v  - Plain play after uFlipV fix (face/hair detail should render)
echo   59. Phase 21d-2a-vi - Dagger grip (90 deg X prerotation; piercing forward grip vs stab)
echo   60. Phase 21d-2a-vii - Layered equipment (boots + chest texture override on farmboy)
echo   61. Phase 21d-2a-viii-a - Hero variant audit + env-var pick (pos_a3 + skin_07 + pants_015)
echo   62. Phase 21d-2a-viii-b - Character creator UI panel (SIEGEFX_CREATOR=1; Begin to spawn)
echo   63. Phase 21d-2a-viii-c - Hero name + variant persistence through quicksave (F5/F9)
echo   64. Phase 21d-2a-ix    - Audio coverage audit (Sound.dsres histogram + gap report)
echo   65. Phase 21d-2a-xi    - Mood + region ambient bed audit (CLI; play-region for in-game loop)
echo   66. Phase 21d-2a-xii   - SED registry audit (Sound.dsres pitch jitter + cap inventory)
echo   67. Phase 9-SC-10      - Shield render verify (fh_r1 + SIEGEFX_DEBUG_DROP=shield)
echo   68. Phase 9-SC-16 B-1+B-2 - Pcontent tier + wildcards + rarity (#club/2-3, #armor/-rare/..., #*/-unique/...)
echo   69. Phase 10-SC-1 - Trigger matrix parser (fh_r1 special.gas + verb coverage)
echo   70. Phase 10-SC-2 - Full chore dictionary into Actor.Clips (fh_r1 chore coverage)
echo   71. Phase 10-SC-3 - PRS v0x202 + v0x302 loader (Objects.dsres prs fuzz, all 1962 files)
echo   72. Phase 11-SC-7 - Land water seam stitching (fh_r1 nav + amphibious path 30,30 to water)
echo   73. Phase 12-SC-3 - Mob loot frequency vs DS1 retail (krug_grunt/krug_scout/gremal)
echo   74. Phase 12-SC-4/5 - Death pose + weapon-class attack chore (VISUAL, fh_r1)
echo   75. Phase 12-SC-6 - PRS TRCR resync (Objects.dsres prs fuzz, expect 1855 v3 OK + 131 tracers)
echo   76. Phase 17-SC-A1 - SpellExpr ** power op (spells survey + show fireball/iceshard)
echo   77. Phase 17-SC-A2/A3 - SpellExpr placeholders + ternary (spells eval / show freeze)
echo   78. Phase 17-SC-B    - Per-element spell projectile/impact VFX (spells elements)
echo   79. Phase 17-SC-C    - Player chore_magic plays during moving casts
echo   80. Phase 17-SC-D    - SfxScriptStore inventory (1074 effect_script* across 14 gas files)
echo   81. Phase 17-SC-E    - Billboard particle backend (in-window F11 fire+smoke+sparks, F10 lightning)
echo   82. Phase 17-SC-F-1  - sfx_script compiler IR (parser dump for fireball_emitter)
echo   83. Phase 17-SC-F-2  - sfx_script VM receipt (TallySink: smoke_emitter + fire_emitter, 60 ticks)
echo   84. Phase 17-SC-G    - Region emitters wired (in-window fh_r1, smoke columns over chimneys)
echo   85. Phase 17-SC-H    - Spell cast sfx_script binding (spells dump w/ cast_sfx column + coverage)
echo   86. Phase 17-SC-I    - Water UV scroll + waterwheel rotation (in-window fh_r1)
echo   87. Phase 17-SC-J    - Per-instance scale_multiplier (breakable farmhouse door + foliage variation)
echo   88. Phase 21-SC-SPELL-VFX-AUDIT - Visual-coverage audit across every offensive spell (Logic.dsres)
echo   89. Phase 21-SC-SPELL-VFX-AUDIT - Visual verify fireball + iceshard (SIEGEFX_DEBUG_SPELLS launch)
echo   90. Phase 21-SC-SCROLL          - Full scroll-UI test (16-spell roster + ground pile + glitter)
echo   91. Phase 21-SC-SPELL-VISUAL    - Primitive sweep (10 spells, one per slice A-H + sphere)
echo   92. Phase 21-SC-BARREL          - Breakable barrels (cursor + spell + frags + loot, fh_r1)
echo   93. Phase 23-SC-OPTIONS         - Options Menu (F10 in-game; 4 tabs Video/Audio/Input/Game)
echo   94. Phase 24-MAINMENU            - Boot to main menu (no args; splash to logo drop to 7 buttons)
echo   95. SC-TSD-ANIM                  - Water frame-cycle + waterfall layer-2 modulate2x (fh_r1)
echo   96. SC-QUEST-OBJ-A               - Talk-to-NPC quest objective (SIEGEFX_DEBUG_QUEST=quest_seek_gyorn, talk to Edgaar)
echo   97. SC-QUEST-OBJ-F               - Full 24-quest Ehb catalog + chain (SIEGEFX_DEBUG_QUEST=quest_seek_gyorn, watch chain follow-up activate)
echo   98. SC-QUEST-OBJ-C               - Pickup quest objective (SIEGEFX_DEBUG_QUEST=quest_grab_fireshot, basement spell_fireshot)
echo   99. SC-QUEST-OBJ-D               - Deliver quest objective (SIEGEFX_DEBUG_QUEST=quest_merik_staff, hold staff + talk Merik)
echo  100. SC-HUD-DATABAR               - Bottom-row HUD buttons (pause/HP-pot/MP-pot/labels/map/journal/menu) — click each in fh_r1
echo  101. SC-HUD-OVERHEAD-BARS         - Floating HP/MP bars above every combatant (PC always on; enemies on hit/aggro)
echo  102. SC-FADE-NODES-LNODE          - Spawn at fh_r1 farmhouse basement stairs (SIEGEFX_DEBUG_SPAWN=70,-4,-65); walk down + test dungeon reveal + click-pick
echo  103. Phase 26 - PARTY RECRUIT     - Recruit Gyorn at Stonebridge (bt_r1): RMB him, click Accept; he follows + fights
echo  104. SC-WEATHER                   - Mood weather audit (CLI, self-verifying) + fh_r1 storm eyes-test (rain/fog/lightning/thunder + rain loops)
echo  105. SC-ELEVATOR                  - Ride the farmhouse grate lift (hc_r1): lever call, 5s descent with rider carry, fades, nav rebuild
echo  106. ALPHA-1 CAMPAIGN AUDIT       - Completability sweep: unhandled components + undispatched trigger verbs across all 81 regions (CLI, self-verifying)
echo  107. CRYPT STAIRS REPRO           - Spawn AT the cr_r1 stairs (path2crypts root, SIEGEFX_DEBUG_SPAWN); fade-flap / reveal / freeze repro; F7 = fade diag
echo  108. ALPHA-2: TOWN + DIALOGUE     - Stonebridge (bt_r1): retail dialogue chrome w/ Gyorn+Adwana, vendors, recruit, save/load soak (F5/F9)
echo  109. ALPHA-2: CRATES + SHRINE     - path2crypts: trapped crates spring, chests open+loot, life shrine heals, world gold credits
echo  110. ALPHA-2: DWARVEN GATE        - path2sd: stuck use-toggle gate shows authored text on click; opens only via its quest message
echo  111. ALPHA-2: STAR DEVICE         - gd_a_r1: locked usable reports Locked. without key_glb_star; crypt doors chain msg_scid_opening
echo  112. NAV VERTICAL-REBIND REPRO    - sd_r1 mine ledge (headless): horseshoe walk must reach at Y=17, never teleport to the Y=44 mountain top
echo  113. ENEMY ROAM-SIM               - headless 90s wander soak over the path2sd..sd_r2 set; expect 0 FROZEN and no field-report piles
echo  114. SC-MP-EOS P3 NET ROUND-TRIP  - headless host^<-^>client: join+snapshot+state-delta+input-authority+chat+malformed-frame safety (loopback)
echo  115. FRONTEND CHROME SHOTS        - offscreen PNG receipts of the MainMenu + SinglePlayer chrome (goldens\frontend-shots; compare vs retail)
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
if /i "%CHOICE%"=="51" goto T51
if /i "%CHOICE%"=="52" goto T52
if /i "%CHOICE%"=="53" goto T53
if /i "%CHOICE%"=="54" goto T54
if /i "%CHOICE%"=="55" goto T55
if /i "%CHOICE%"=="56" goto T56
if /i "%CHOICE%"=="57" goto T57
if /i "%CHOICE%"=="58" goto T58
if /i "%CHOICE%"=="59" goto T59
if /i "%CHOICE%"=="60" goto T60
if /i "%CHOICE%"=="61" goto T61
if /i "%CHOICE%"=="62" goto T62
if /i "%CHOICE%"=="63" goto T63
if /i "%CHOICE%"=="64" goto T64
if /i "%CHOICE%"=="65" goto T65
if /i "%CHOICE%"=="66" goto T66
if /i "%CHOICE%"=="67" goto T67
if /i "%CHOICE%"=="68" goto T68
if /i "%CHOICE%"=="69" goto T69
if /i "%CHOICE%"=="70" goto T70
if /i "%CHOICE%"=="71" goto T71
if /i "%CHOICE%"=="72" goto T72
if /i "%CHOICE%"=="73" goto T73
if /i "%CHOICE%"=="74" goto T74
if /i "%CHOICE%"=="75" goto T75
if /i "%CHOICE%"=="76" goto T76
if /i "%CHOICE%"=="77" goto T77
if /i "%CHOICE%"=="78" goto T78
if /i "%CHOICE%"=="79" goto T79
if /i "%CHOICE%"=="80" goto T80
if /i "%CHOICE%"=="81" goto T81
if /i "%CHOICE%"=="82" goto T82
if /i "%CHOICE%"=="83" goto T83
if /i "%CHOICE%"=="84" goto T84
if /i "%CHOICE%"=="85" goto T85
if /i "%CHOICE%"=="86" goto T86
if /i "%CHOICE%"=="87" goto T87
if /i "%CHOICE%"=="88" goto T88
if /i "%CHOICE%"=="89" goto T89
if /i "%CHOICE%"=="90" goto T90
if /i "%CHOICE%"=="91" goto T91
if /i "%CHOICE%"=="92" goto T92
if /i "%CHOICE%"=="93" goto T93
if /i "%CHOICE%"=="94" goto T94
if /i "%CHOICE%"=="95" goto T95
if /i "%CHOICE%"=="96" goto T96
if /i "%CHOICE%"=="97" goto T97
if /i "%CHOICE%"=="98" goto T98
if /i "%CHOICE%"=="99" goto T99
if /i "%CHOICE%"=="100" goto T100
if /i "%CHOICE%"=="101" goto T101
if /i "%CHOICE%"=="102" goto T102
if /i "%CHOICE%"=="103" goto T103
if /i "%CHOICE%"=="104" goto T104
if /i "%CHOICE%"=="105" goto T105
if /i "%CHOICE%"=="106" goto T106
if /i "%CHOICE%"=="107" goto T107
if /i "%CHOICE%"=="108" goto T108
if /i "%CHOICE%"=="109" goto T109
if /i "%CHOICE%"=="110" goto T110
if /i "%CHOICE%"=="111" goto T111
if /i "%CHOICE%"=="112" goto T112
if /i "%CHOICE%"=="113" goto T113
if /i "%CHOICE%"=="114" goto T114
if /i "%CHOICE%"=="115" goto T115
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T26
echo.
echo --- Phase 15c + SC-9/10/13/14: Grid inventory + shield-on-bone (fh_r1) ---
echo [press I to toggle a centered 8x5 inventory grid]
echo [icons land per-template (b_gui_ig_*); multi-cell weapons span 1x2 / 1x3]
echo [LMB-drag an item to a new cell to relocate it (saved across opens)]
echo [LMB-drag out of the panel to drop the item back into the world]
echo [each drop fires the template's [aspect][voice][put_down] *  cue]
echo [SC-10: kill a shield-bearing enemy + pick up the shield -> renders on shield_grip]
echo [press I again or Esc to dismiss the panel]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T44
echo.
echo --- Phase 25c: Authored store screen (Stonebridge, bt_r1) ---
echo [RMB a shopkeeper: Adwana (spells), Jonn (smith), or Gyorn - store screen opens]
echo [Right-docked authored chrome: cpbox frame, portrait box, name plate, 8x10 shelf grid]
echo [Six tabs ARMOR/WEAPONS/SHIELDS + SPELLS/POTIONS/MISC - active tab shifts down 5px]
echo [Adwana SPELLS tab: starter-band scrolls (zap/flash/fireshot tier, 20-150g range)]
echo [Hover a shelf item - name + price readout; unaffordable reads red]
echo [LMB a shelf item = buy (gold debits); your inventory co-opens - LMB your item = sell]
echo [Previous/Next page when a tab overflows the 8x10 grid; Close button exits]
echo [Insufficient gold: console logs "trade: cannot afford ..." and rejects]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/bt_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T51
echo.
echo --- Phase 21d-1: Balance curves audit (CLI, no window) ---
echo [Walks each SkillKind from L1 to L50 simulating a player who only earns]
echo [that skill's XP. Prints CumXP / STR / DEX / INT / MaxHP / MaxMP / regen rates]
echo [and time-to-full-HP / MP at every level. Flags monotonicity violations.]
echo [With shipped formulas.gas, expect 0 violations across all 4 skills.]
echo.
"%TOOL%" balance curve "%DS1%\Resources\Logic.dsres" --max-level=50 --skill=all
set EXITCODE=%ERRORLEVEL%
echo.
echo === audit exited with code %EXITCODE% ===
pause
goto MENU

:T52
echo.
echo --- Phase 21d-2a-i: ASP subset fuzz (CLI, no window) ---
echo [Parses every .asp in Objects.dsres and validates that the per-submesh]
echo [BSMM (textureIndex, faceSpan) records sum to BTRI's face count.]
echo [Histogram shows how many meshes use 1/2/N subsets — confirms farmboy is]
echo [multi-subset (skin + clothing) while 1-texture creatures stay single-subset.]
echo.
"%TOOL%" asp subset-fuzz "%DS1%\Resources\Objects.dsres"
set EXITCODE=%ERRORLEVEL%
echo.
echo === fuzz exited with code %EXITCODE% (0 = all clean) ===
pause
goto MENU

:T53
echo.
echo --- Phase 21d-2a-ii: Per-subset texture render (fh_r1) ---
echo [Farmboy ASP carves into 5 subsets across 2 textures: skin (slot 0)]
echo [covers head/hands/legs flesh, clothing (slot 1 = b_c_pos_a1_015) covers]
echo [shirt+pants strip. Renderer now binds + draws per subset, so the]
echo [clothing strip should NOT inherit the skin texture.]
echo [Visually verify: farmboy's torso/legs show fabric pattern, not skin tone.]
echo [Krug + goblin (single-subset meshes) should render unchanged.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
goto MENU

:T54
echo.
echo --- Phase 21d-2a-iii prep: Actor-coverage audit (CLI, no window) ---
echo [Mirror of `region prop-textures all` for the NPC layer. Walks every]
echo [actor.gas placement in all 81 shipped regions, resolves the template's]
echo [aspect.model -> .asp, then walks AspMesh.Subsets and probes each]
echo [slot's texture via the same (template-override-by-slot, mesh.TextureNames]
echo [slot) precedence ResolveActorTexture uses at runtime.]
echo [Catches missing meshes, missing slot textures, and parse breakers BEFORE]
echo [the Farmhouse -^> Castle Ehb playtest hits them.]
echo.
"%TOOL%" region actor-coverage "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" all --terrain="%DS1%\Resources\Terrain.dsres" --top=15
set EXITCODE=%ERRORLEVEL%
echo.
echo === audit exited with code %EXITCODE% (0 = all clean) ===
pause
goto MENU

:T55
echo.
echo --- Phase 21d-2a-iv: BTRI cornerStart fix (fh_r1) ---
echo [BTRI face indices for ASP version ^> 2.2 are subtexture-local, not]
echo [submesh-local. Without applying per-subtexture cornerStart, multi-]
echo [subtexture characters (farmboy = 2 subtextures in BSUB[0]) reference]
echo [wrong corners and render as web-like geometry: webbing between forearms,]
echo [hammer pants, partial hair, smeared face. Single-subtexture meshes]
echo [(krug, every monster) are unaffected because cornerStart[0] = 0.]
echo [Visually verify: farmboy is no longer "web-man" -- arms are detached,]
echo [body has correct silhouette. Texture coverage on clothing/hair is a]
echo [separate Phase 21d-2a-v issue.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
goto MENU

:T56
echo.
echo --- Phase 21d-2a-v diag: farmboy texture binding ---
echo [Two diagnostic switches active in this run:]
echo [  SIEGEFX_DEBUG_FALLBACK=1 -- fragment shader paints uHasTexture=0]
echo [    fragments BRIGHT MAGENTA instead of the sand-toned fallback. If]
echo [    farmboy's clothing strip turns magenta, slot 1 isn't binding a]
echo [    texture. If it stays skin/tan, the texture binds but its actual]
echo [    sampled pixels look skin-colored (texture-content question).]
echo [  SIEGEFX_TEX_RESOLVE_LOG=1 -- prints one line per (template, slot)]
echo [    on first resolve, so we can grep "tpl=farmboy" in the log to see]
echo [    OK/MISS + the resolved basename for each subset.]
echo.
echo Please run, eyeball the player, then close the window and copy the]
echo [console lines starting with "[tex-resolve" plus a one-line description]
echo [of what farmboy looked like (magenta where? skin tone where?).]
echo.
set SIEGEFX_DEBUG_FALLBACK=1
set SIEGEFX_TEX_RESOLVE_LOG=1
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set SIEGEFX_DEBUG_FALLBACK=
set SIEGEFX_TEX_RESOLVE_LOG=
goto MENU

:T57
echo.
echo --- Phase 21d-2a-v diag: subset-tint visualizer ---
echo [Each ASP subset draws as a unique solid color (no texture, no lighting):]
echo [   subset 0 = RED     subset 1 = GREEN    subset 2 = BLUE]
echo [   subset 3 = YELLOW  subset 4 = MAGENTA  (cyan/orange/purple if more)]
echo.
echo [Look at the player's farmboy. Note which body region is which color.]
echo [Expected (if BSMM-to-geometry is correct):]
echo [   subset 0 (RED, 72 tris)    = head/face/hair       -- skin texture]
echo [   subset 1 (GREEN, 300 tris) = torso/arms/legs      -- clothing texture]
echo [   subset 2 (BLUE, 142 tris)  = ?                    -- skin texture]
echo [   subset 3 (YELLOW, 112 tris)= ?                    -- skin texture]
echo [   subset 4 (MAGENTA, 80 tris)= ?                    -- skin texture]
echo.
echo [Also note ALL OTHER NPCs in fh_r1 -- if mismatches show up across many,]
echo [it's a parser issue, not a farmboy-only quirk.]
echo.
set SIEGEFX_SUBSET_TINT=1
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set SIEGEFX_SUBSET_TINT=
goto MENU

:T58
echo.
echo --- Phase 21d-2a-v: plain play after uFlipV fix ---
echo [Default skinned uFlipV is now 0 (was 1 for v2.5+). Subset-tint diag in]
echo [option 57 confirmed asp UV V[0.01,0.54] for the head subset must land in]
echo [GL V near 0 to sample the face/hair region of the .raw — the prior flip]
echo [pushed those UVs into the bottom-strip brown gradient, hence smeared face.]
echo.
echo [Look for: face features visible, hair on top of head, brown leather vest]
echo [on the chest, white peasant pants on the legs. Forearms/hands SHOULD be]
echo [skin-toned (sampling the gradient strip in the texture). Boots still]
echo [missing — that is a separate equipment-composition slice, not a tex bug.]
echo.
echo [If other NPCs in fh_r1 (Edgaar, villagers, krug attackers) now look wrong,]
echo [report which and we'll add per-mesh-version override. SIEGEFX_FORCE_FLIPV=1]
echo [forces the old behavior back if you want to A/B compare.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
goto MENU

:T59
echo.
echo --- Phase 21d-2a-vi: dagger grip + idle stance + walk anim (RESOLVED) ---
echo [Four fixes layered into this run, all driven by the same farmboy + dagger]
echo [equip slot:]
echo.
echo [  1. Dagger TEXTURE: solid grey blade (was rainbow). Weapon mesh shader]
echo [     uFlipV flipped 1 -> 0 to match every other DS1 .raw bottom-up convention.]
echo.
echo [  2. Dagger GRIP: 180-deg X prerotation on weapon_grip (ASPImport.ms says 90,]
echo [     but our pipeline empirically needs 180 - likely interaction with bind]
echo [     180-X + the FlipUp coord-system handling in the BVA path).]
echo.
echo [  3. Idle STANCE: ActorSpawner picks fs1 (1H melee) idle when the equipped]
echo [     weapon specializes weapon_melee; stance is also tried OUTSIDE the]
echo [     authored chore_stances list so chore_walk picks up fs1 even when its]
echo [     template only authors fs0.]
echo.
echo [  4. WALK animation: weapon-attach loop now mirrors the body's walk-swap]
echo [     so the dagger tracks the wrist through the walk cycle instead of]
echo [     floating at the idle pose while the arm swings through it.]
echo.
echo [F1 in-game cycles 12 grip-prerotation presets (X/Y/Z 180/90/-90 + compounds);]
echo [active preset prints to console. Default = X 180.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T60
echo.
echo --- Phase 21d-2a-vii: layered equipment composition ---
echo [base_farmboy ships [inventory][equipment]:]
echo [  es_weapon_hand = dg_g_d_1h_fun (handled by 21d-2a-vi weapon attach)]
echo [  es_feet        = bo_bo_le_light]
echo [  es_spellbook   = book_glb_magic_01 (UI-only, no 3D mesh)]
echo.
echo [DS1 layers each equipped item as a separate ASP attached to the body via]
echo [bone names. Boots, helms, gauntlets share the body's biped skeleton; their]
echo [ASPs use IDENTICAL bone names so AnimationRuntime.ComputeSkinMatrices]
echo [name-keyed bone map lets us pose them against the body's clip + time.]
echo.
echo [Boot mesh derivation:]
echo [  body.armor_version=gah_fb + body type=a1 (from aspect.model suffix)]
echo [  + armor_lookup.gas[a1]=(b,b)  + defend.armor_type=type1 (from bo_bo_le_light)]
echo [  -> m_c_gah_fb_boot_type1_b.asp]
echo [Boot texture derivation:]
echo [  defend.armor_style=068 -> b_a_boot_068.raw]
echo.
echo [Pre-flight CLI: equipment-audit dumps the resolved layers per slot]
"%TOOL%" templates equipment-audit "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" farmboy
echo.
echo [Visual: launch fh_r1 - farmboy spawns with leather boots + dagger + walk anim]
echo [Watch console: "equip: layered es_feet = bo_bo_le_light mesh=... bones=37 tex=... OK"]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T61
echo.
echo --- Phase 21d-2a-viii-a: hero variant resolver (env-var quick-pick) ---
echo [DS1 ships ~18,300 hero variants: 7 body types (pos_a1..a7) x ~32 skin tones]
echo [x ~41 pants colors per body, gendered into farmboy and farmgirl. The shipped]
echo [heroes.gas hardcodes a single (a1, skin_04, pants_008) point in that space.]
echo.
echo [The full character creator UI ships in slice viii-b (built from the authentic]
echo [DS1 character_select.gas layout under /ui/interfaces/frontend/). For now,]
echo [SIEGEFX_HERO_GENDER / _BODY / _SKIN / _PANTS env vars feed the same picker]
echo [the UI will use, exercising the resolver end-to-end without UI work.]
echo.
echo [1/2] Headless audit: enumerate every variant traceable in Objects.dsres
"%TOOL%" templates hero-variants "%DS1%\Resources\Objects.dsres"
echo.
echo [2/2] Visual: pick boy + body 3 + skin 07 + pants 015, spawn into fh_r1]
echo [Watch console: "  player: variant pick gender=Boy body=3 skin=07 pants=015"]
echo [Then: "  player: 'farmboy' did spawn" with model=m_c_gah_fb_pos_a3]
echo.
set SIEGEFX_HERO_GENDER=boy
set SIEGEFX_HERO_BODY=3
set SIEGEFX_HERO_SKIN=07
set SIEGEFX_HERO_PANTS=015
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
set SIEGEFX_HERO_GENDER=
set SIEGEFX_HERO_BODY=
set SIEGEFX_HERO_SKIN=
set SIEGEFX_HERO_PANTS=
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T64
echo.
echo --- Phase 21d-2a-ix: audio coverage audit ---
echo [Headless histogram of Sound.dsres: 626 wavs across 46 s_e_<prefix> categories,]
echo [cross-referenced against the static wired-id list in CmdAudioCoverage. Output:]
echo [per-category authored / wired / gap, plus an "unwired categories" summary.]
echo [Use --list-unwired=PREFIX to see the full unwired entries for a category.]
echo [Use --list-orphan-categories for first-5 samples of every zero-wired family.]
echo.
"%TOOL%" audio coverage "%DS1%\Resources\Sound.dsres"
echo.
echo === audit-only command (no game launch) ===
pause
goto MENU

:T65
echo.
echo --- Phase 21d-2a-xi: mood + region ambient bed audit ---
echo [Two halves: first a CLI dump of every parsed mood + the per-region default-mood]
echo [picker the runtime applies on region entry; then a play-region launch where you]
echo [can hear the looping bed. fh_r1 is intentionally silent (DS1 used positional]
echo [emitters there, not a mood track) — walk into a region with an audible bed]
echo [(e.g. cr_r1 crypts -> s_e_ambient_crypt) to confirm the swap. Watch the console:]
echo [look for "ambient: region 'X' -> mood 'Y' -> 'Z'" lines on each region change.]
echo.
"%TOOL%" mood list "%DS1%\Resources\Logic.dsres" --map=world --regions
echo.
echo --- launching play-region (Ctrl+C to skip the in-game half) ---
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === xi exited with %EXITCODE% ===
pause
goto MENU

:T66
echo.
echo --- Phase 21d-2a-xii: SED registry audit ---
echo [DS1 ships a Sound Effect Descriptor (SED) layer that authors per-fire pitch]
echo [jitter, fixed transposes, and concurrent-voice caps for every sound that wants]
echo [variation. Each SED is a *_sed.gas file in /sound/effects/; 165 ship in DS1.]
echo [The runtime loads them at audio init and applies the rate range when playing.]
echo.
echo [Three views below:]
echo [  1. Default summary - histograms + top-3-category samples]
echo [  2. Cross-aliases   - SEDs whose key name != their actual wav (sound aliasing)]
echo [  3. Filter spell    - all 2 spell SEDs (zap_cast + nova_strike_cast)]
echo.
echo --- summary ---
"%TOOL%" audio sed-list "%DS1%\Resources\Sound.dsres"
echo.
echo --- cross-aliases ---
"%TOOL%" audio sed-list "%DS1%\Resources\Sound.dsres" --show-aliases
echo.
echo --- filter spell ---
"%TOOL%" audio sed-list "%DS1%\Resources\Sound.dsres" --filter=spell
echo.
echo === xii exited with %ERRORLEVEL% ===
pause
goto MENU

:T67
echo.
echo --- Phase 9-SC-10: Shield render verify (fh_r1 + debug-drop) ---
echo [SIEGEFX_DEBUG_DROP injects a sh_m_g_c_r_s_avg loot pile 1.5u in front of]
echo [spawn so the real pickup -> auto-equip -> LoadAttachedItem path fires]
echo [without hunting for a shield-bearing mob to kill.]
echo [Walk forward, the auto-pickup picks up the shield, the [es_shield_hand]]
echo [equip log fires, and the shield should render on the PC's shield_grip]
echo [bone (left forearm) oriented like the weapon (X 180 deg + grip prerot).]
echo.
set "SIEGEFX_DEBUG_DROP=shield_hand:sh_m_g_c_r_s_avg"
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
set "SIEGEFX_DEBUG_DROP="
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T68
echo.
echo --- Phase 9-SC-16 B-1+B-2: Pcontent dump (tier + wildcards + rarity) ---
echo [B-1: #club/2-3 hits power-2/3 generic clubs only, never the unique]
echo [cb_un_2h_troll_rock. B-2: #armor/-rare(1)/28-80 picks rare-tier armor]
echo [in the right defense band (only *_ra_* templates show up); #*/-unique(2)/175-286]
echo [picks unique cross-class items (ax_un_*, sd_un_*, st_un_*, bd_un_*, etc).]
echo.
echo === B-1 verify: #club/2-3 ===
"%TOOL%" pcontent dump "%DS1%\Resources\Logic.dsres" --class=club --spec=#club/2-3 --rolls=20 --seed=42
set EXITCODE=%ERRORLEVEL%
echo.
echo === B-2 verify: #armor/-rare(1)/28-80 ===
"%TOOL%" pcontent dump "%DS1%\Resources\Logic.dsres" --class=NONE --spec=#armor/-rare(1)/28-80 --rolls=10 --seed=1
echo.
echo === B-2 verify: #*/-unique(2)/175-286 ===
"%TOOL%" pcontent dump "%DS1%\Resources\Logic.dsres" --class=NONE --spec=#*/-unique(2)/175-286 --rolls=10 --seed=2
echo.
echo === pcontent dump exited with code %EXITCODE% (last invocation: %ERRORLEVEL%) ===
pause
goto MENU

:T69
echo.
echo --- Phase 10-SC-1/b/c: trigger matrix parser + dispatcher ---
echo [fh_r1 expect: 64 placements bear [instance_triggers]; entered/left_trigger_group]
echo [warm via the synthetic trip-tick (entered=4, left=4) — proves SC-1b occupants pass.]
echo [cr_r1 expect: 92 placements, 23 when_false actions deferred to falling edge;]
echo [trip-tick at the @-0.04,-1.2,-0.04 placement reports when_false^>0 — proves SC-1c.]
echo.
echo --- fh_r1 (SC-1b: occupants/entered/left) ---
"%TOOL%" region triggers "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Logic.dsres" /world/maps/map_world/regions/fh_r1
echo.
echo --- cr_r1 (SC-1c: when_false falling-edge dispatch) ---
"%TOOL%" region triggers "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Logic.dsres" /world/maps/map_world/regions/cr_r1
pause
goto MENU

:T70
echo.
echo --- Phase 10-SC-2: full chore dictionary into Actor.Clips ---
echo [fh_r1 expect: 181/181 spawned; clip catalogue avg ~6 per actor (was 2 pre-SC-2);]
echo [chore coverage line lists chore_default / chore_walk / chore_attack / chore_die /]
echo [chore_fidget / chore_magic / chore_misc — each addressable via Actor.GetClipIndex.]
echo [Post-SC-3 expect min=3 (every actor has at least default + fidget + walk).]
echo.
"%TOOL%" region spawn "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
pause
goto MENU

:T71
echo.
echo --- Phase 10-SC-3: PRS v0x202 + v0x302 animation loader ---
echo [Objects.dsres prs fuzz: parse every shipped .prs and tally by version stamp.]
echo [Expected: 1962 / 1962 OK, 0 failures, 131 with TRCR (separate gap), 0 legacy-skip.]
echo [versions (ok): 0x3=1724, 0x202=62, 0x302=45 — full coverage of shipped DS1.]
echo.
"%TOOL%" prs fuzz "%DS1%\Resources\Objects.dsres"
pause
goto MENU

:T72
echo.
echo --- Phase 11-SC-7: land-water seam stitching ---
echo [fh_r1 nav: expect "Water seams: 37/1435 ... (37 stitched cross-kind pair(s))"]
echo [Pre-SC-7 was 0/1435 — Floor and Water lived on disconnected geometric components.]
echo [World path-fuzz: expect "Total land-water: ~4,907 cross-kind seam(s)" across 81]
echo [regions; biggest-component A* should still be ~99-100%% (now Floor-only-restricted).]
echo [Amphibious route: 30,0,30 (Floor) -- 27.57,-1.5,0.70 (Water) crosses a stitched seam]
echo [with --water=4. Default LandOnly traversal still refuses water endpoints.]
echo.
"%TOOL%" region nav "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" /world/maps/map_world/regions/fh_r1
echo.
"%TOOL%" region path "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" /world/maps/map_world/regions/fh_r1 "30,0,30" "27.57,-1.5,0.70" --water=4
echo.
"%TOOL%" region path-fuzz "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres"
pause
goto MENU

:T73
echo.
echo --- Phase 12-SC-3: mob loot drops vs DS1 retail ---
echo [Pre-SC-3 the equipped weapon (es_weapon_hand) dropped 100%% of kills because]
echo [LootRoller folded Equipped buckets into the drop pile. Post-SC-3 only il_main]
echo [entries roll: krug_grunt drops at ~18%% (15%% chance gate), krug_scout at ~12%%,]
echo [gremal at 0%% (no [inventory][pcontent] in chain). Worn weapon stays on body.]
echo.
"%TOOL%" loot dump "%DS1%\Resources\Logic.dsres" krug_grunt --rolls=200 --seed=42
echo.
"%TOOL%" loot dump "%DS1%\Resources\Logic.dsres" krug_scout --rolls=200 --seed=42
echo.
"%TOOL%" loot dump "%DS1%\Resources\Logic.dsres" gremal --rolls=200 --seed=42
pause
goto MENU

:T74
echo.
echo --- Phase 12-SC-4 + SC-5: Death pose + weapon-class attack chore (VISUAL) ---
echo [SC-4: kill a krug -- the body should fall and HOLD its final chore_die frame]
echo [instead of T-posing or vanishing on the idle. fh_r1 receipt: 179/179 mobs ship]
echo [chore_die. Quicksave (F5) and quickload (F9) -- corpse should still be down.]
echo.
echo [SC-5: pick up the dagger and attack a krug. Player should swing the dagger]
echo [(stance 1, 1H melee), NOT punch with the empty hand. Pre-fix only chore_default]
echo [+ chore_walk swapped on equip; chore_attack stayed at the unarmed stance.]
echo [SC-5 walks the whole chore_dictionary on RefreshMotionClips so attack/magic/]
echo [die/get_hit/fidget all rebind to the equipped weapon's stance.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
echo.
pause
goto MENU

:T75
echo.
echo --- Phase 12-SC-6: PRS TRCR resync ---
echo [Objects.dsres prs fuzz: every shipped .prs (incl. tracer-bearing files) parses.]
echo [Pre-fix the loader threw NotSupportedException on TRCR, so 131 files (incl. fb]
echo [stance-1 attack a_c_gah_fb_fs1_at.prs) bailed and the player swung the unarmed]
echo [chore on dagger equip. Post-fix: scan-forward resync to next valid chunk via]
echo [4-byte-aligned tag scan with chunk-version + KLST bone-index plausibility check.]
echo [Expected: 1962 / 1962 OK, 0 failures, 131 with tracers, 0 legacy-skip.]
echo [Versions OK: 0x3=1855, 0x202=62, 0x302=45 (v3 climbed 1724 -> 1855).]
echo.
"%TOOL%" prs fuzz "%DS1%\Resources\Objects.dsres"
echo.
pause
goto MENU

:T76
echo.
echo --- Phase 17-SC-A1: SpellExpr ** power operator ---
echo [Survey: shows 19 spells use ** (fireball, iceshard, acid_cloud, etc.) and lists]
echo [the placeholder set (#magic + #maxlife/#life/#src_mana/#src_life — last four are]
echo [SC-A2 territory; ternary [[?:]] in healing_hands is SC-A3).]
echo [Show fireball: pre-fix every level rolled dmg=0 because ParseMulDiv ate one star]
echo [of the ** pair. Post-fix L1 [3.85..4.67], L100 [376..399], scaling by ~96x.]
echo [Show iceshard: pre-fix L1 [1.66..2.54] flat. Post-fix L1 [3.54..5.42] -> L100 [284..361].]
echo.
"%TOOL%" spells survey "%DS1%\Resources\Logic.dsres"
echo.
"%TOOL%" spells show "%DS1%\Resources\Logic.dsres" spell_fireball
echo.
"%TOOL%" spells show "%DS1%\Resources\Logic.dsres" spell_iceshard
echo.
pause
goto MENU

:T77
echo.
echo --- Phase 17-SC-A2/A3: SpellExpr placeholders + ternary ---
echo [SC-A2 plumbs #maxlife / #life / #src_mana / #src_life through SpellEvalContext.]
echo [Receipt: spell_freeze with #maxlife=20 -> mana=50.0 (was 0.0 pre-A2).]
echo [SC-A3 adds [[?:]] ternary + comparison ops (^< ^> ^<= ^>= == !=) so leech_life]
echo [drain clamps and healing_hands' nested heal-vs-mana ternary parse cleanly.]
echo [Receipts: leech_life formula evaluates to 0.7 / 0.3 across two src_life cases;]
echo [healing_hands triple-ternary returns 11 / -0.61 / 4 across three context cases.]
echo.
echo -- spell_freeze (#maxlife=20) --
"%TOOL%" spells show "%DS1%\Resources\Logic.dsres" spell_freeze --maxlife=20 5
echo.
echo -- ternary smoke tests --
"%TOOL%" spells eval "(2 ^> 1) ? 5 : 10"
"%TOOL%" spells eval "(1 ^> 2) ? 5 : 10"
"%TOOL%" spells eval "[[ ( #magic ^> 5 ) ?( 100 ): ( 200 ) ]]" --magic=10
"%TOOL%" spells eval "[[ ( #magic ^> 5 ) ?( 100 ): ( 200 ) ]]" --magic=2
echo.
echo -- spell_leech_life clamp formula --
"%TOOL%" spells eval "( ( #src_life ^> (2.0 + #magic ) ) ? (2 + #magic ) : ( ( #src_life ^> 0.0 ) ? #src_life : 0.0 ) )/10.0" --magic=5 --src_life=20
"%TOOL%" spells eval "( ( #src_life ^> (2.0 + #magic ) ) ? (2 + #magic ) : ( ( #src_life ^> 0.0 ) ? #src_life : 0.0 ) )/10.0" --magic=5 --src_life=3
echo.
pause
goto MENU

:T78
echo.
echo --- Phase 17-SC-B: per-element spell projectile/impact VFX ---
echo [SpellTemplate now exposes a SpellElement (Fire/Ice/Lightning/Acid/Death/Holy/]
echo [Generic) classified by template name. RenderHost reads it to tint the bolt +]
echo [impact flash for every cast — fireballs read orange, iceshards cyan, zaps blue.]
echo [Receipt: catalog of 69 offensive instant-hit spells groups cleanly across the]
echo [seven buckets; only 7 land in Generic (kill / killing_fist / leech_life /]
echo [metal_shards / nurture / reconstitution / tremor — no obvious element cue).]
echo.
"%TOOL%" spells elements "%DS1%\Resources\Logic.dsres"
echo.
pause
goto MENU

:T79
echo.
echo --- Phase 17-SC-C: chore_magic plays on every cast (incl. moving casts) ---
echo [The cast site already pinned chore_magic via PlayChoreOnce, but the actor]
echo [draw loop unconditionally swapped to chore_walk while the player was moving]
echo [— masking the cast clip whenever the click-to-cast happened mid-stride.]
echo [Fix: ActorHostBridge.IsOverrideActive gates the walk swap; pinned chores]
echo [(magic / attack / die) now ride through the full override duration.]
echo [Receipt: the farmboy template's chore dictionary ships chore_magic, so the]
echo [override has a real clip to land on. (Visual confirmation: cast spell_zap]
echo [while click-to-moving — the cast pose now reads instead of the walk cycle.)]
echo.
"%TOOL%" templates show "%DS1%\Resources\Logic.dsres" farmboy ^| findstr /R "chore_"
echo.
pause
goto MENU

:T80
echo.
echo --- Phase 17-SC-D: SfxScriptStore inventory + sample bodies ---
echo [DS1's particle / spell-VFX system is driven by [effect_script*] blocks under]
echo [/world/global/effects/. Each block names a script (fireball, smoke_emitter,]
echo [waterfall_froth, ...) and stores a stack-based DSL body in script=[[ ... ]];.]
echo [SC-D parses the lot and exposes them via 'siegefx sfx list / show'.]
echo [Receipt: 1074 scripts across 14 gas files; 'fireball' resolves to offensive.gas,]
echo [smoke_emitter to environmental.gas, waterfall_froth to environmental.gas.]
echo.
echo === sfx list (top of the catalog) ===
"%TOOL%" sfx list "%DS1%\Resources\Logic.dsres" --prefix=fireball
echo.
echo === sfx show smoke_emitter ===
"%TOOL%" sfx show "%DS1%\Resources\Logic.dsres" smoke_emitter
echo.
pause
goto MENU

:T81
echo.
echo --- Phase 17-SC-E: billboard particle backend ---
echo [Loads the farmhouse region (fh_r1) the same way option 50 does, then exposes]
echo [particle backend hotkeys for the visual receipt:]
echo   F11 - spawns a burst of fire + smoke + sparks at the player's feet
echo   F10 - fires a downward lightning bolt onto the player position
echo [Atlas pulled from Objects.dsres at LoadPlayActors:]
echo   slot 0 = b_sfx_fireball-01.raw, slot 1 = b_sfx_smoke.raw,
echo   slot 2 = b_sfx_sparkle01.raw,   slot 3 = b_sfx_002.raw.
echo [Receipt: a smoke column, a fire plume, and bright spark scatter in-window.]
echo [SC-F (script interpreter) and SC-G (region emitter wiring) drive this from data.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
echo.
pause
goto MENU

:T82
echo.
echo --- Phase 17-SC-F-1: sfx_script compiler (text -> SfxProgram IR) ---
echo [SfxScriptCompiler.Compile reads each [effect_script*] body, strips the [[ ]]]
echo [literal markers + // and /* */ comments, tokenizes the DSL, then folds verbs]
echo [into typed StatementKind entries: SfxCreate / SfxStart / Set / SoundPlay /]
echo [Pause / Call / etc. Conditionals (if/else), waitfor, get, worldmsg surface as]
echo [Raw statements so the future VM can log + skip rather than crash on shapes the]
echo [interpreter doesn't yet handle.]
echo.
echo === fireball_emitter (rich script: 34 statements across 11 kinds) ===
"%TOOL%" sfx parse "%DS1%\Resources\Logic.dsres" fireball_emitter
echo.
echo === smoke_emitter (minimal 2-statement emitter pattern) ===
"%TOOL%" sfx parse "%DS1%\Resources\Logic.dsres" smoke_emitter
echo.
pause
goto MENU

:T83
echo.
echo --- Phase 17-SC-F-2: sfx_script VM receipt (TallySink, headless) ---
echo [SfxRuntime executes the compiled IR against an IParticleSink. The VM lives in]
echo [SiegeFX.Core (no GL dep) so this CLI can drive it from a counting stub. Two]
echo [scripts ticked for 3 simulated seconds (60 ticks @ 1/20s):]
echo.
echo   smoke_emitter -> 1 persistent emitter, 60 Maintain calls, ~360 smoke spawns]
echo   fire_emitter  -> 1 persistent emitter, 60 Maintain calls, ~54 fire spawns]
echo.
echo [Same VM (SiegeFX.Core.Sfx.SfxRuntime) drives the live ParticleSystem at runtime.]
echo.
echo === smoke_emitter ===
"%TOOL%" sfx run "%DS1%\Resources\Logic.dsres" smoke_emitter --ticks=60
echo.
echo === fire_emitter ===
"%TOOL%" sfx run "%DS1%\Resources\Logic.dsres" fire_emitter --ticks=60
echo.
pause
goto MENU

:T84
echo.
echo --- Phase 17-SC-G: region emitters wired into world load ---
echo [LoadPlayActors loads emitter.gas placements per region (alongside special.gas)]
echo [and broadcasts we_entered_world to each trigger instance. The trigger matrix's]
echo [call_sfx_script verb invokes SfxRuntime.Spawn at the placement origin, so DS1's]
echo [smoke / fire emitters in fh_r1 produce live billboard columns above chimneys.]
echo.
echo [VISUAL: launch fh_r1, look at the farmhouse roofs. Smoke columns should rise]
echo [from each emitter placement; fireplaces (if any) get fire+smoke pairs.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
echo.
pause
goto MENU

:T85
echo.
echo --- Phase 17-SC-H: spell cast sfx_script binding ---
echo [SpellTemplate.CastSfxScript resolves the per-spell cast effect by walking]
echo [the specializes chain leaf-first looking for either:]
echo.
echo   (1) [common][template_triggers][*] with]
echo       condition* = receive_world_message("we_req_cast")]
echo       action*    = call_sfx_script("&lt;name&gt;");]
echo.
echo   (2) any [spell_*] root block with effect_script = &lt;name&gt;;]
echo.
echo [At cast time RenderHost calls SfxRuntime.Spawn(scriptName, target),]
echo [replacing the legacy SpellBolt dot-trail with the real DS1 fire/smoke/]
echo [lightning effect. Templates that don't bind a script keep the dot trail.]
echo.
echo [Receipt: dump shows the resolved sfx column per spell + a coverage]
echo [summary. Expect ~61/69 offensive spells to resolve a script.]
echo.
"%TOOL%" spells dump "%DS1%\Resources\Logic.dsres"
echo.
pause
goto MENU

:T86
echo.
echo --- Phase 17-SC-I: water UV scroll + waterwheel rotation ---
echo [DS1 ships per-texture TSD .gas sidecars with vshiftpersecond + frame counts.]
echo [SC-I-1 recognises the waterfall texture pattern (b_t_*_rvr_fall-*) and applies]
echo [a 0.5/sec V-shift on its sampling UVs; -static textures (mist + the layered]
echo [wheelfallstatic composite) are excluded so they stay still.]
echo.
echo [SC-I-2 detects chore_default = rotateX?rpm=N on placed templates (mill]
echo [waterwheel = rotatex?rpm=-8.0) and bakes an angular velocity onto the static]
echo [prop. The draw loop spins the prop's local axis before applying placement,]
echo [so the wheel turns in place while the riverfall texture cascades over it.]
echo.
echo [Receipt: in fh_r1, walk to the mill (north of the farmhouse, river side) —]
echo [the waterfall column should flow downward and the wooden wheel should turn.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
echo.
pause
goto MENU

:T87
echo.
echo --- Phase 17-SC-J: per-instance scale_multiplier ---
echo [DS1 lets each placement override aspect.scale_multiplier so the same mesh]
echo [reads with subtle variation across the world. fh_r1 has 1150 placements with]
echo [a non-default scale (most foliage in the 0.9-1.4 range), and the breakable]
echo [farmhouse door (door_grs_farmhouse_breakable, 0x01c00da3) ships with 1.5 so]
echo [the destroyable variant looks visibly larger than the everyday wooden door.]
echo.
echo [Receipt 1 (text): the static-prop load summary now ends with "N with]
echo [non-default scale_multiplier" — expect ~1100+ for fh_r1. Receipt 2 (visual):]
echo [in-window, the breakable farmhouse door sits scaled 1.5x; vegetation reads]
echo [with the per-instance jitter DS1 baked instead of a uniform clone-stamp.]
echo.
echo [Note: the original "burnt door" reading was wrong — DS1 has no separate]
echo [burnt-door mesh and no fire emitters at the door. The breakable variant uses]
echo [the same m_i_grs_door-farmhouse asp; what made it look distinct was scale.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
echo.
pause
goto MENU

:T88
echo.
echo --- Phase 21-SC-SPELL-VFX-AUDIT: visual coverage across every offensive spell ---
echo [Headless audit: walks every offensive SpellTemplate's compiled cast sfx_script]
echo [statically and reports per-spell verdict (COVERED/PARTIAL/UNCOVERED) plus the]
echo [primitive-kind / unhandled-verb / texture roll-ups across the whole catalog.]
echo [Recurses one level into `call <subscript>` so composed scripts audit fully.]
echo.
echo [Receipt: 69 offensive spells, 61 with cast_sfx_script (8 have no we_req_cast),]
echo [9 fully COVERED, 47 PARTIAL (use orbiter/trackball/cylinder/lightsource/etc.),]
echo [5 UNCOVERED (DS1 author left them sound-only — see iceblast_launch.gas TODO).]
echo [Top primitive misses: orbiter (20 spells), trackball (18), cylinder (12),]
echo [lightsource (9), flurry (7), fireb (5), sray (5), curve (4). 18 distinct b_sfx_*]
echo [textures referenced; b_sfx_sparkle01 is the most common (26 spells).]
echo.
"%TOOL%" spells visual-audit "%DS1%\Resources\Logic.dsres"
echo.
echo [--verbose for the per-spell breakdown; --filter=NAME to narrow; --only-uncovered]
echo [to see only the PARTIAL+UNCOVERED rows.]
echo.
pause
goto MENU

:T89
echo.
echo --- Phase 21-SC-SPELL-VFX-AUDIT: visual verify fireball + iceshard ---
echo [Sets SIEGEFX_DEBUG_SPELLS=spell_fireball,spell_iceshard so the player spawns]
echo [with those slotted instead of the default zap+healing_wind. Press Q to cast]
echo [primary (fireball) and W to cast secondary (iceshard) at fh_r1's krug.]
echo.
echo [Post-slice-G: fireball is now COVERED. Q-cast should fly a tracking projectile]
echo [from the caster's hand to the target with a fire trail, then on collision]
echo [(slice G's waitfor + impact gating) fire two cylinder ground rings + an explosion]
echo [burst at the impact point. Pre-G these bursts fired at cast time; post-G they]
echo [wait for the trackball to arrive.]
echo.
echo [Iceshard stays UNCOVERED -- DS1 author stub (ice_shard_launch.gas is sound-only).]
echo [Our SpawnSpellVisual placeholder still over-delivers vs shipped DS1: cyan]
echo [projectile + impact burst.]
echo.
echo [If the visuals don't match these verdicts that's a bug; treat as a finding.]
echo.
set SIEGEFX_DEBUG_SPELLS=spell_fireball,spell_iceshard
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
set SIEGEFX_DEBUG_SPELLS=
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T90
echo.
echo --- Phase 21-SC-SCROLL: full scroll-UI test (16-spell roster) ---
echo [Launches fh_r1 with a 16-spell SIEGEFX_DEBUG_SPELLS roster:]
echo [  Q+W actives: fireball + iceshard]
echo [  Spellbook placed (10 rows): lightning, shock_wave, nurture, bombard,]
echo [    acid_cloud, death_blast, spark, fire_pillar, implosion, starburst]
echo [  Ground pile (~2u in front of player): zap, frigid_armor, heal_bind, leech_life]
echo.
echo [What to test:]
echo [  - Ground pile glitters with element-tinted "pixie dust" until pickup]
echo [    (warm orange for combat magic, green for nature/holy/acid, cyan for ice,]
echo [     purple for death). Walk away, glitter follows the pile. Walk over]
echo [     pile -> auto-routes scrolls to spellbook Placed[]; once Placed full,]
echo [     extras land in the inventory grid for drag-from testing.]
echo [  - Open spellbook with B, click an active or placed slot to pick up]
echo [    onto cursor. Click another slot to drop/swap. Self-drop = restore.]
echo [    ESC or RMB cancels and restores to source.]
echo [  - Open inventory with I. Scroll items render with DS1 b_gui_ig_i_ic_sp_*_inv]
echo [    art. Click a scroll to pick onto cursor; drop on spellbook slot.]
echo [  - With cursor scroll, click outside any UI = world drop with the]
echo [    Phase 9-SC-9 throw arc + new X-axis tumble. Walk over to retrieve.]
echo [  - F5 quicksave / F9 quickload: layout round-trips through schema v6.]
echo [  - Verify fireball regression fix: cast Q -> tracking projectile flies]
echo [    caster -> target with fire trail (was rendering nothing before]
echo [    commit 6d3a58c).]
echo.
set SIEGEFX_DEBUG_SPELLS=fireball,iceshard,lightning,shock_wave,nurture,bombard,acid_cloud,death_blast,spark,fire_pillar,implosion,starburst,zap,frigid_armor,heal_bind,leech_life
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
set SIEGEFX_DEBUG_SPELLS=
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T91
echo.
echo --- Phase 21-SC-SPELL-VISUAL: primitive sweep (slices A-H + sphere) ---
echo [10-spell roster, one spell per shipped primitive kind. Each highlights a]
echo [different slice's deliverable so visual regressions surface fast.]
echo.
echo [Roster:]
echo [  Q  fireball       trackball + fire + lightsource glow + waitfor + cylinder impact]
echo [  W  apprentice_zap straight-bolt lightning]
echo [  Placed slot 1 dragon_fire    fireb directional cone (slice C)]
echo [  Placed slot 2 death_blast    cylinder ground ring + sray radial fan (A + B)]
echo [  Placed slot 3 spark          lightsource Glow halo, color-preserving (slice D)]
echo [  Placed slot 4 firebomb       sphere shells -- omni shell, color-preserving (H + sphere)]
echo [  Placed slot 5 bombard        orbiter + lightsource + sphere + flurry (multi-slice)]
echo [  Placed slot 6 starburst      orbiter + sray]
echo [  Placed slot 7 fire_pillar    cylinder column + fire emitter]
echo [  Placed slot 8 healing_wind   curve + sparkles (motion-handle slice + sparkles)]
echo.
echo [What to look for, organized by slice:]
echo.
echo [  --- Slice A (cylinder ground ring) ---]
echo [  fireball impact: TWO concentric cylinder rings appear at the landing point]
echo [  AFTER the trackball arrives (slice G's waitfor gating). fire_pillar: a]
echo [  textured cylinder column rises at the cast target. Both should look like]
echo [  textured rings/columns on the ground, NOT a thin lightning beam (that was]
echo [  the pre-A placeholder).]
echo.
echo [  --- Slice B (sray streak) ---]
echo [  death_blast: tapered radial streaks fan out from the impact point. starburst:]
echo [  same effect emanating from the orbiting projectile. Should read as long]
echo [  thin tapered rays, NOT a dense spark cloud.]
echo.
echo [  --- Slice C (fireb cone) ---]
echo [  dragon_fire: forward-emitting fire cone in the caster's facing direction.]
echo [  The cone should spread laterally and have noticeable forward velocity, NOT]
echo [  be a static fire column.]
echo.
echo [  --- Slice D (lightsource Glow halo) ---]
echo [  spark: a bright additive halo cluster pulses at the spell origin. Color]
echo [  should match the spell's authored tint -- NOT drift to orange/brown over]
echo [  time (that was the pre-D Steam-as-lightsource bug). Bombard's lightsource]
echo [  should follow the orbiting projectile.]
echo.
echo [  --- Slice E (sfx attach / rat / offset_bone / direction) ---]
echo [  fireball's three layered fire emitters should look DIFFERENT from each other]
echo [  (slice E's sfx-rat random rotation), not stacked plumes pointing the same]
echo [  way. The fire trail should follow the trackball through its flight path,]
echo [  not stay anchored at #SOURCE.]
echo.
echo [  --- Slice F (per-bone resolution) ---]
echo [  fireball, dragon_fire, spark all spawn from the caster's HAND BONE area]
echo [  (weapon_bone -> weapon_grip), not from feet. Watch the cast origin: it]
echo [  should track the hand as the player moves.]
echo.
echo [  --- Slice G (waitfor + collision gate) ---]
echo [  fireball: the impact burst (ring + explosion + sparks) fires WHEN THE]
echo [  TRACKBALL ARRIVES, not at cast time. Pre-G these all fired at cast and]
echo [  the projectile flew through them. bombard same: impact burst follows the]
echo [  orbital flight path's terminus.]
echo.
echo [  --- Slice H + sphere ---]
echo [  firebomb: TWO omni-directional expanding particle shells appear at the]
echo [  impact point. Color should match the authored orange (1, .5, .1) and stay]
echo [  warm through the lifetime, not drift to brown. Shell should be a 3D]
echo [  spherical burst -- particles in ALL directions, not just upward (pre-fold]
echo [  this used the warm-biased SpawnSpark). bombard sphere similar.]
echo.
echo [Things that CAN'T be tested headlessly (the test list above + your eyes):]
echo [  - Color preservation across full lifetime (60+ frames)]
echo [  - Spatial accuracy of bone-anchored emitters as the caster moves]
echo [  - Timing of waitfor's resume vs trackball arrival visual match]
echo [  - Sphere's omni-directionality vs Y-fountain bias]
echo.
echo [Audit CLI receipt before launch (should print 56/0/5):]
"%TOOL%" spells visual-audit "%DS1%\Resources\Logic.dsres" 2^>^&1 ^| findstr /C:"COVERED" /C:"PARTIAL" /C:"UNCOVERED" /C:"MISS"
echo.
set SIEGEFX_DEBUG_SPELLS=fireball,apprentice_zap,dragon_fire,death_blast,spark,firebomb,bombard,starburst,fire_pillar,healing_wind
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
set SIEGEFX_DEBUG_SPELLS=
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T63
echo.
echo --- Phase 21d-2a-viii-c: hero name + variant persistence ---
echo [Same launch path as 62, but verifies the save schema bump (v4 -> v5).]
echo [In-window: type a hero name (e.g. "TestHero"), pick a non-default body/skin,]
echo [Begin to spawn. Hero name banner sits top-center over the 3D scene.]
echo [Press F5 to quicksave, F9 to reload — name banner + variant persist;]
echo [the v5 quicksave.save under %%LOCALAPPDATA%%\SiegeFX\Saves carries HeroName +]
echo [Variant{Gender,BodyTypeIdx,SkinSuffix,PantsSuffix}. v4-and-earlier saves]
echo [load with empty name + null variant (template defaults), no schema break.]
echo.
set SIEGEFX_CREATOR=1
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
set SIEGEFX_CREATOR=
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T62
echo.
echo --- Phase 21d-2a-viii-b: character creator UI panel ---
echo [Reads the gas-authored layout from /ui/interfaces/frontend/character_select/]
echo [character_select.gas (extracted to _scratch_charsel.gas) so button rects +]
echo [name edit_box + 3D preview viewport land at the original DS1 coordinates.]
echo.
echo [Set SIEGEFX_CREATOR=1 so RenderHost gates TrySpawnPlayer behind the panel.]
echo [In-window: cycle Gender/Body/Skin/Pants with the L/R arrow buttons; click]
echo [the name edit_box and type a hero name (max 14 chars); Begin to spawn,]
echo [Cancel to fall through to env-var defaults.]
echo.
set SIEGEFX_CREATOR=1
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
set SIEGEFX_CREATOR=
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
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
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T92
echo.
echo --- Phase 21-SC-BARREL: cursor + spell-break + frag debris + loot ---
echo [Sub-slices A1, B, C, D bundled. fh_r1 ships 13 barrel_glb_fh_r1 +]
echo [5 crate_glb_fh_r1 placements with regional pcontent (35%% gold@2-8,]
echo [5%% potion_mana/health_small, ~62%% empty per the [oneof*] read).]
echo [Plus 2 breakable doors (no pcontent, frags only).]
echo.
echo [What to look for:]
echo   1. Cursor states - hover the mouse over:
echo      - empty terrain  -^> sword (b_gui_c_pointer.raw, 64x64)
echo      - a goblin       -^> red sword (b_gui_c_attack1.raw, 64x64)
echo      - a barrel/crate -^> animated hammer (b_gui_c_smash1.flm, 21 frames)
echo      - a loot pile    -^> animated hand (b_gui_c_grab1.flm, 30 frames)
echo      - Edward (NPC)   -^> talk marker (b_gui_c_talk.raw, 32x32)
echo   2. Melee a barrel: LMB while close. Wood + metal frags fly out, fall,
echo      and settle on the ground. Console logs the drop.
echo   3. Spell a barrel: cast Q/W (zap) at a barrel. Same shatter + frag
echo      burst as melee. Spell damage debits mana per the cast.
echo   4. Loot drop: ~35%% of barrels drop 2-8 gold (auto-credited, "+N gold"
echo      banner); ~5%% drop a potion (lands as a LootPile, click to pickup).
echo      ~60%% drop nothing — that's the authored distribution.
echo   5. Frag debris: each shatter spawns ~6-12 frag instances (frag_glb_wood_*
echo      + frag_glb_metal_*). They tumble, fall under gravity, settle.
echo.
echo [Audit CLI receipts before launch:]
"%TOOL%" region breakable-audit "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1 --top=8
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T93
echo.
echo --- Phase 23-SC-OPTIONS: in-game Options Menu (4 tabs) ---
echo [Slices A-F bundled. Modal dialog over fh_r1, scales by viewport]
echo [height: 968x828 panel at 1080p, 1290x1104 at 1440p ultrawide,]
echo [1935x1656 at 4K. Bitmap font scales at integer steps so the]
echo [12px copperplate stays crisp at every target resolution.]
echo.
echo [How to open:]
echo   F10            - opens / closes the dialog (DS1's [game_options] hotkey)
echo   Esc            - closes as Cancel
echo.
echo [What to look for once it's open:]
echo   1. Tabs at top — Video / Audio / Input / Game. Click a tab to swap
echo      the inner panel content. Active tab gets the inner-panel bg
echo      color (visually attached to the content area).
echo   2. Each row is a label on the left + a control on the right:
echo      - Cycle button (string options): LMB steps forward, RMB steps back
echo      - Slider: click anywhere on the track to jump the thumb;
echo                LMB-drag continues
echo      - Bool toggle: cycle button labeled "Off" / "On"
echo   3. Bottom bar: OK commits, Cancel discards, Defaults resets just
echo      the active tab. NO Apply button (DS1 is OK / Cancel only).
echo.
echo [Per-tab content:]
echo   Video   : Resolution / Shadows / Texture Filtering / Gamma / Object Detail
echo   Audio   : Sound on/off + 5 volume sliders + EAX
echo             - Master / Music / SFX volumes apply LIVE during drag
echo               (drag the master slider down — you should hear it fade)
echo             - Ambient / Voice / EAX persist-only (labels say "(inactive)")
echo   Input   : Invert X/Y, Lock X/Y, Edge Tracking, Camera + Mouse Sensitivity
echo             - "Hotkeys..." button opens a read-only listing sub-screen
echo               (full rebinding pending splinter SC-OPTIONS-REBIND)
echo   Game    : Two pages (More / Back paging)
echo             - Page 1: Framerate, Priority, Text Scroll, Max Text,
echo               Game Speed, Tutorial Tips, Difficulty
echo             - Page 2: Tooltips, Blood Color, Dismemberment
echo.
echo [Status: PARTIAL. The dialog opens, scales, and persists state within
echo the session, but most knobs are still persist-only. Verified working:]
echo   - Audio Master / Music / SFX volumes (live during drag + on OK)
echo   - Sound on/off (Master goes to 0 when off)
echo   - Defaults click on Audio tab applies live so you hear the reset
echo   - Game tab: Show Framerate (top-right FPS HUD on/off) - eyes confirmed
echo.
echo [Wired but reportedly NOT visibly working yet - needs follow-up:]
echo   - Input tab: Invert Camera X/Y, Camera Sensitivity, Mouse Sensitivity
echo     (ApplyOptionsRuntime pushes to Camera but user reports no effect;
echo      may be that the chase-mode handler still routes around it, or
echo      the values aren't reaching the input dispatch path)
echo   - Game tab: Game Speed (claimed wired but not yet validated)
echo.
echo [Persist-only - will need their own splinter to take runtime effect:]
echo   - Video: resolution, shadows, texture filtering, gamma, object detail
echo            (SC-OPTIONS-VIDEO-RUNTIME)
echo   - Input: lock-camera-x/y, screen edge tracking
echo            (SC-OPTIONS-INPUT-RUNTIME)
echo   - Game:  tooltips (no tooltip system shipped yet - SC-TOOLTIP),
echo            blood color, dismemberment, priority, text scroll, max text,
echo            tutorial tips, difficulty (SC-OPTIONS-PERSIST + per-knob splinters)
echo   The menu remembers all of these within the session but resets on relaunch.
echo.
echo [SC-OPTIONS-FOLD2 visible fixes that DID land:]
echo   - Tab labels no longer covered by inner panel (panel Y dropped 80 to 86,
echo     tab font centering now factors in _fontScale)
echo   - Defaults button no longer overlaps Hotkeys (Input) or More (Game) -
echo     RowStride dropped 30 to 24 so 8 rows fit above the Defaults band
echo   - Hotkeys sub-screen layout now scales with _fontScale at 4K
echo   - mood_change trigger flood log dedupes by name (was 100s/frame in fh_r1)
echo.
echo [Reach the menu the cheap way:]
echo   1. Game launches into fh_r1 (skip creator with SIEGEFX_CREATOR=0 — see below)
echo   2. Press F10 once you can move
echo   3. Try every tab + drag the Master Volume slider
echo   4. Hit OK or Cancel to close
echo.
set SIEGEFX_CREATOR=0
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set EXITCODE=%ERRORLEVEL%
set SIEGEFX_CREATOR=
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T94
echo.
echo --- Phase 24-MAINMENU: boot to main menu ---
echo [Default no-args launch. Resolves DS1 install via SIEGEFX_DS1 env var or]
echo [the GOG / Steam / retail-DVD common paths, then runs the splash sequence:]
echo.
echo   1. Microsoft splash (intro_microsoft.gas, 3-panel RAW alpha-anim)
echo   2. GPG splash (intro_gaspowered.gas, same)
echo   3. Bink-stub fade (1s placeholder for SC-MAINMENU-BINK)
echo   4. "Dungeon Siege" sword drop on logo.asp via logo-enter.prs (2.17s)
echo   5. Main menu - 7 buttons (Single Player / Multiplayer / Options / Continue
echo                            / About / Exit / Credits)
echo.
echo [Working actions: Single Player (opens SP submenu via mm2sp transition),
echo  Options (F10 dialog), About (overlay), Exit (close)]
echo [Stubs (log "splinter SC-MAINMENU-X pending" on click): Continue,
echo  Multiplayer, Credits — region launch + sub-screens deferred]
echo.
echo [Phase 27-SP-FLYOUT: Single Player click animates the panel + button
echo  column (mainmenu_mm2sp + menubars_mm2sp PRS) to a 2-button SP screen
echo  (Start New Game / Load Game) with EXIT replaced by BACK. Sword cursor
echo  shows in all menu states. Hover overlays on every button. Back unwinds
echo  via sp2mm clips. New Game / Load Game still log SC-MAINMENU-NEWGAME /
echo  -LOADGAME pending for now.]
echo.
echo [Esc on splash skips ahead to main menu. Esc on main menu quits.]
echo.
echo [Distributable: run publish-alpha.bat [version] at the repo root:]
echo [ single-file SiegeFX.exe + SiegeSmith.exe + zips under publish]
echo [ (see the script for the underlying dotnet publish flags).]
echo.
dotnet "%RUN%"
set EXITCODE=%ERRORLEVEL%
echo.
echo === SiegeFX exited with code %EXITCODE% ===
for %%F in ("%~dp0src\SiegeFX.Runtime\bin\Release\net11.0\siegefx_crash.log") do if exist "%%~F" (
  echo --- crash log ---
  type "%%~F"
  echo ------------------
)
pause
goto MENU

:T95
echo.
echo --- SC-TSD-ANIM: water frame-cycle + waterfall layer-2 ---
echo [DS1 stores per-texture animation in TSD .gas sidecars. Two recipes:]
echo.
echo   1. River surface (b_t_grs01_rvr_water-2a-*.gas): layer1numframes=4,
echo      layer1secondsperframe=0.15, four distinct textures cycled at ~6.7fps,
echo      timesyncanimation=true so all river tiles stay in lockstep.
echo   2. Waterfall (b_t_grs01_wheelfallstatic-01.gas): layer 1 = static
echo      painted-on rock, layer 2 = b_t_grs01_rvr_dynamic with
echo      vshiftpersecond=0.5 and colorop=modulate2x. The visible cascade is
echo      the layer-2 dynamic texture scrolling vertically and modulate2x
echo      blended onto the static base.
echo.
echo [SC-TSD-ANIM-A reads every TSD .gas at terrain-tank open, indexes by
echo  texture name. SC-TSD-ANIM-B extends the mesh fragment shader with a
echo  second sampler (uAlbedo2), independent uv offset, and colorop selector.]
echo.
echo [Receipt: walk to the bridge near the farmhouse (NE of spawn). The river
echo  surface should ripple at 6-7 frames per second. Walk around the bend to
echo  the wheelfall — the cascade should slide downward at ~0.5 unit per second
echo  with the modulate2x brightening on top of the static rock.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
echo.
pause
goto MENU

:T96
echo.
echo --- SC-QUEST-OBJ-A: talk-to-NPC objective receipt ---
echo [QuestCatalog now ships a stub TALK quest "quest_seek_gyorn" with
echo  TalkTargetTemplate=edgaar. Until SC-QUEST-OBJ-F wires dialogue-driven
echo  activate_quest, an env-var pre-activates the quest at region-load.]
echo.
echo [Receipt: console prints "[quest:debug] activated quest_seek_gyorn"
echo  on region-load. Walk to Edgaar (talkable NPC roster lists his world
echo  position), RMB to open dialogue, click through to the close. Console
echo  prints "[quest] talk objective complete: quest_seek_gyorn (spoke to
echo  edgaar)" and the journal entry flips to Completed. Press L to inspect
echo  the quest log overlay.]
echo.
set "SIEGEFX_DEBUG_QUEST=quest_seek_gyorn"
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set "SIEGEFX_DEBUG_QUEST="
echo.
pause
goto MENU

:T97
echo.
echo --- SC-QUEST-OBJ-F: 24-quest catalog + chain receipt ---
echo [QuestCatalog now ships all 24 Kingdom of Ehb quests (Chapters I-IX)
echo  with NextQuestKey chain pointers between main-quest beats.
echo  quest_seek_gyorn -> quest_deliver_gyorn_report -> quest_clear_glitterdelve
echo  -> quest_report_torg_findings -> quest_for_merik -> ... -> quest_vanquish_seck.]
echo.
echo [Receipt: console prints "[quest:debug] activated quest_seek_gyorn"
echo  on region-load. RMB Edgaar in fh_r1 (Quest_for_Gyorn's TALK target
echo  is stubbed to him until Stonebridge streams). Console prints:
echo    [quest] talk objective complete: quest_seek_gyorn (spoke to edgaar)
echo    [quest] follow-up activated: quest_deliver_gyorn_report (from quest_seek_gyorn)
echo  Press L to see both entries in the journal overlay (quest_seek_gyorn
echo  closed, quest_deliver_gyorn_report active).]
echo.
set "SIEGEFX_DEBUG_QUEST=quest_seek_gyorn"
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set "SIEGEFX_DEBUG_QUEST="
echo.
pause
goto MENU

:T98
echo.
echo --- SC-QUEST-OBJ-C: pickup quest objective ---
echo [QuestCatalog now ships quest_grab_fireshot (PickupTargetTemplate=
echo  spell_fireshot, PickupCountGoal=1). Pre-activated via env var; walk
echo  into the fh_r1 basement, pick up the fireshot scroll, console prints:
echo    [quest] pickup objective complete: quest_grab_fireshot (acquired spell_fireshot)]
echo.
set "SIEGEFX_DEBUG_QUEST=quest_grab_fireshot"
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set "SIEGEFX_DEBUG_QUEST="
echo.
pause
goto MENU

:T99
echo.
echo --- SC-QUEST-OBJ-D: deliver quest objective ---
echo [QuestCatalog's quest_merik_staff is a composite C+D entry:
echo  PickupTargetTemplate=merik_staff (auto-completes the pickup leg on grab)
echo  AND TalkTargetTemplate=merik + DeliverItemTemplate=merik_staff (the
echo  hand-off leg gates on holding the staff at talk time). The staff and
echo  Merik are both in Lost Cathedral - fh_r1 alone can't fully exercise
echo  the receipt, but launching here proves the activation + journal entry.
echo  Real receipt needs the player to reach lc_r5 with the staff in hand.]
echo.
set "SIEGEFX_DEBUG_QUEST=quest_merik_staff"
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set "SIEGEFX_DEBUG_QUEST="
echo.
pause
goto MENU

:T100
echo.
echo --- SC-HUD-DATABAR: bottom-row HUD buttons ---
echo [DS1's always-on data_bar at the bottom of the screen. 7 button slots:
echo  pause/play (left), HP potion, MP potion, then on the right:
echo  labels-toggle, mega-map (stub), quest-log (book), menu (door icon).
echo  Click each to verify the right notify fires:
echo    Pause/Play -- toggles world tick (Space key alias)
echo    HP/MP Potion -- drinks lowest-tier potion from inventory
echo    Labels       -- toggles flag (overhead rendering pending SC-HUD-OVERHEAD-BARS)
echo    Mega-Map     -- prints "splinter SC-HUD-MEGAMAP pending"
echo    Quest Log    -- toggles the L/J overlay
echo    Menu (door)  -- opens the Options dialog (F10 alias)
echo.
echo  Quest indicator flash: when a quest activates or objective completes
echo  the red book pulses over the journal button for ~1.5s.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
echo.
pause
goto MENU

:T101
echo.
echo --- SC-HUD-OVERHEAD-BARS: floating HP/MP above heads ---
echo [DS1-authentic floating HP/MP bars (status_bars.gas). PC always
echo  shows; enemies show only when wounded OR in Chase/Attack aggro.
echo  Texture: b_gui_ig_mnu_status_bars; per-bar uvcoords with V-flip
echo  to convert gas's bottom-up convention to screen frame; dynamic_
echo  edge=right clips the fill to currentLife/maxLife.
echo  Receipt: walk to a krug pack, watch each enemy's bar appear when
echo  they engage you, and watch their HP drain on hit.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
echo.
pause
goto MENU

:T102
echo.
echo --- SC-FADE-NODES-LNODE: dungeon-reveal nav test ---
echo [Spawns the PC near the first farmhouse basement entry in fh_r1
echo  (world ~70,-4,-65) so you don't have to walk from the authored
echo  spawn. Walk a few steps down/in toward the basement stairs and
echo  watch for [fade_nodes] lines in the log — those mark the moment
echo  the trigger fires + which (snode,lnode) pairs get hidden. Then
echo  click on the basement floor and verify the path actually
echo  descends instead of routing across the upper floor.
echo  Diag: [fade-trig-diag] lines at startup list each trigger's
echo  resolved world position; if they cluster near origin (0,0,0)
echo  the snode-parent transform isn't resolving for those triggers.]
echo.
set "SIEGEFX_DEBUG_SPAWN=70,-4,-65"
set "SIEGEFX_DEBUG_LOG_FILE=%TEMP%\siegefx_diag\test102.log"
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set "SIEGEFX_DEBUG_SPAWN="
set "SIEGEFX_DEBUG_LOG_FILE="
echo Log written to: %TEMP%\siegefx_diag\test102.log
echo.
pause
goto MENU

:T103
echo.
echo --- Phase 26: Party recruitment (Stonebridge, bt_r1) ---
echo [RMB a companion (Gyorn stands past the Stonebridge gate). His conversation]
echo  opens in the authentic DS1 cpbox dialogue box (top-centre). Gyorn's join]
echo  line ends "...can I come along?" with Accept / Decline buttons.]
echo [Click Accept — he joins (free; paid companions debit [aspect]gold_value),]
echo  his "well met, friend!" accept line plays, console logs "party: recruited".]
echo  Decline shows his polite refusal; the corner X dismisses.]
echo [The recruit drops its wander and TRAILS you in a wedge behind the leader;]
echo  walk around and watch it path-follow. Party cap is 8 (7 followers + leader).]
echo [FOLLOWERS FIGHT: a recruit engages the nearest enemy in range with its]
echo  starting weapon (Gyorn=mace melee, Naidi=bow ranged, Gloern=axe), swings/]
echo  fires, then returns to formation when dead. Kills award party XP + loot.]
echo [PARTY GUI: the field-commands panel (formations / move-attack-target orders /]
echo  select-all / disband / follow) sits BOTTOM-RIGHT and is on screen from game]
echo  start, solo or not. Once Gyorn joins, his portrait cell appears below yours]
echo  (top-left); click it to select, click a formation button to re-shape the wedge.]
echo [Stats read from the gas per companion (Gyorn lvl-2 fighter, Sikra lvl-38 mage).]
echo [ESC MENU: press Esc for DS1's in_game_menu - 5 centred copperplate buttons]
echo  RESUME GAME / OPTIONS / SAVE GAME / LOAD GAME / EXIT GAME. Resume closes it,]
echo  Options opens the tabbed Options dialog, Save/Load use the quicksave slot.]
echo [Not yet: pure casters (lore-book il_main) follow but don't cast; full equipment]
echo  slots + the View/stats popover + pack mule = Phase 27.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/bt_r1
echo.
pause
goto MENU

:T104
echo.
echo --- SC-WEATHER: mood weather + region sound emitters ---
echo [CLI PART - self-verifying receipts:]
echo   mood weather: 232 map_world moods, 232 fog, 9 rain (1 lightning =
echo   fh_r1_3, the opening storm; a 10th [rain] ships commented out in
echo   bt_r1_2), 18 snow, 42 wind, 46 sun tables. Densities 30-225 / 75-500.
"%TOOL%" mood weather "%DS1%\Resources\Logic.dsres" --map=world
echo.
echo   sound-emitters all: 670 emitters/81 regions, 0 wav misses; the single
echo   event miss is the shipped env_rats_sqeakskitter typo (PASS, exit 0).
"%TOOL%" region sound-emitters "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Sound.dsres" all
if errorlevel 1 (echo *** SOUND-EMITTER AUDIT FAILED ***) else (echo sound-emitter audit: PASS)
echo.
echo [EYES TEST - fh_r1 opening storm. Walk out from the farmhouse toward the]
echo  fields/bridge; crossing the mood boxes fires mood_change fh_r1_2/_3:]
echo  - RAIN streaks (200-225/s) lean slightly with the wind; density drifts]
echo    every 15s like retail (mood_manager rates).]
echo  - FOG closes to the authored gray 30-50m; the horizon clears to fog color.]
echo  - LIGHTNING (fh_r1_3 zone): double-pulse white flash + thunder clap]
echo    0.4-2s later; strikes every 3-10s while rain is at storm density.]
echo  - RAIN LOOP audio: the storm trigger we_req_activates the amb_rain_01]
echo    emitters (console: [emitter] 0x01C00B24 activated); leaving the zone]
echo    deactivates them. Wind loops + waterwheel + burning-farmhouse fire]
echo    loop are always-on placed emitters now too.]
echo  - Console shows [weather] mood ... applied lines on each zone cross.]
echo [SNOW check (optional): --play-region nt_r1 or ac_r1 = alpine snowfall.]
echo.
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
echo.
pause
goto MENU

:T105
echo.
echo --- SC-ELEVATOR: farmhouse grate lift ride (hc_r1, closest lift to spawn) ---
echo [Same basement house as the stair/cutaway tests - besides the stairs it
echo  has a METAL GRATE floor section that is a working lift now. Spawn =
echo  top of its shaft (world ~76,-4,-72; hc_r1 streams as fh_r1 neighbor).
echo  SC-ELEVATOR is LIVE: 216 lift gizmos across 32 regions parse and
echo  door-align (CLI receipt: siegefx region elevators ... all = 0 fails).
echo.
echo  EYES TEST - the full ride loop:
echo  - Console at load: [elevator] 0x06A00036 ... parked at stop1 and two
echo    [lever] registration lines.
echo  - Grate platform sits flush at the TOP of the shaft; the wall lever
echo    should read as mounted, not floating (report if still floating).
echo  - CLICK the winch lever ITSELF (ray-picked): player walks to the
echo    authored use point ON THE GRATE, then pulls ([lever] pulled line);
echo    grate descends ~12u over 5s WITH you standing on it; the authored
echo    cutaway fades swap surface/basement sections mid-ride.
echo  - Ordinary move-clicks on the floor NEAR the winch must NOT pull it
echo    (the old bug: elevator left without you + player froze topside).
echo  - If you somehow stand over the open shaft when the car is away, a
echo    [nav-rescue] line nudges you to the landing edge instead of freezing.
echo  - At the bottom: walk OFF onto the cellar floor (nav rebuilds on
echo    arrival - console prints a nav mesh rebuild line).
echo  - Pull the basement lever to ride back UP; fades reverse on arrival.
echo  - Recruits standing on the grate ride along with you.]
echo.
set "SIEGEFX_DEBUG_SPAWN=76,-4,-72"
set "SIEGEFX_DEBUG_LOG_FILE=%TEMP%\siegefx_diag\test105.log"
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/fh_r1
set "SIEGEFX_DEBUG_SPAWN="
set "SIEGEFX_DEBUG_LOG_FILE="
echo Log written to: %TEMP%\siegefx_diag\test105.log
echo.
pause
goto MENU

:T106
echo.
echo --- ALPHA-1: campaign completability sweep (CLI, self-verifying) ---
echo [Sweeps ALL 81 regions in stitch-BFS campaign order: every placed
echo  template's component sections (instance + specializes chain) diffed
echo  against the engine-handled set, plus trigger condition/action verbs
echo  diffed against TriggerRuntime's dispatched sets. The UNHANDLED table
echo  is the alpha blocker list (the elevator gap surfaced exactly this
echo  way); the benign table keeps triaged cosmetics visible. Also runs
echo  the per-region nav component histogram (read with care: single-
echo  region scope over-reports splits that cross-region stitches join).]
echo.
"%TOOL%" world campaign-audit "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Logic.dsres"
echo.
"%TOOL%" region nav-components "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" all --top=5
echo.
pause
goto MENU

:T107
echo.
echo --- CRYPT STAIRS REPRO: spawn AT the cr_r1 stair descent ---
echo [Spawns the PC directly at the top of the crypt stairs (path2crypts
echo  frame 150.1,-4.0,307.5 = the fh-frame spot from the crash logs,
echo  mapped through `world map-point` because the stitch is rotated).
echo  This is the spot where the fade seam was flapping (hide/auto-
echo  reverse/hide on every step): half-revealed lower level, crawling
echo  descent, control wedge.
echo  Fixes to verify this run:
echo  - No stair click-fight: descend in ONE click from top to bottom.
echo  - No [fade_nodes] out/in pairs spamming per step in the console
echo    (seam hysteresis now holds a crossed boundary for +0.35u).
echo  - Lower level reveals fully on descent; going back up restores the
echo    upper level.
echo  - No hijacked movement (patrol chains never target the hero now).
echo  IF the reveal still breaks: press F7 while it looks wrong - the
echo  per-section fade diagnostic prints + writes siegefx_fade_diag.log.]
echo.
set "SIEGEFX_DEBUG_SPAWN=150.1,-4.0,307.5"
set "SIEGEFX_DEBUG_LOG_FILE=%TEMP%\siegefx_diag\test107.log"
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/path2crypts
set "SIEGEFX_DEBUG_SPAWN="
set "SIEGEFX_DEBUG_LOG_FILE="
echo Log written to: %TEMP%\siegefx_diag\test107.log
echo.
pause
goto MENU

:T108
echo.
echo --- ALPHA-2: TOWN + DIALOGUE (Stonebridge, bt_r1) ---
echo [Spawn = town. TEST LIST:
echo  1. DIALOGUE CHROME: right-click Gyorn - dark panel + gold border +
echo     gold text, More... mid-thread, Close on the last line, corner X,
echo     scrollbar arrows page long lines (vs conversation.bmp reference).
echo  2. RECRUIT: Gyorn join offer shows Accept/Decline; Accept = follows.
echo  3. VENDOR: right-click Adwana - shop opens, buy a potion, sell it.
echo  4. SAVE SOAK: F5 quicksave, kill something, open a chest if you see
echo     one, F9 load - gold/kills/chest state should match the save
echo     (console prints "load: world state - N bool(s), ...").]
echo.
set "SIEGEFX_DEBUG_LOG_FILE=%TEMP%\siegefx_diag\test108.log"
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/bt_r1
set "SIEGEFX_DEBUG_LOG_FILE="
echo Log written to: %TEMP%\siegefx_diag\test108.log
echo.
pause
goto MENU

:T109
echo.
echo --- ALPHA-2: CRATES + SHRINE (path2crypts) ---
echo [Spawn = region start. TEST LIST:
echo  1. TRAPPED CRATES: smash crates along the path - some spring traps
echo     (console [trap] ... fires; effect + damage numbers float).
echo  2. CHESTS: click a locker/chest - walk up, it opens once, loot
echo     tumbles out ([chest] 0x... opened in console). No smashing chests.
echo  3. LIFE SHRINE: find the glowing shrine spot - stepping into its
echo     trigger heals to full ([shrine] ... restored) + fx loop pulses.
echo  4. WORLD GOLD: coin piles credit +N gold on pickup, not a ghost item.
echo  5. FLOOR TRAPS: crypt-side fire jets trip on proximity w/ authored
echo     reload pauses ([trap] trp_firetrap fires).]
echo.
set "SIEGEFX_DEBUG_LOG_FILE=%TEMP%\siegefx_diag\test109.log"
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/path2crypts
set "SIEGEFX_DEBUG_LOG_FILE="
echo Log written to: %TEMP%\siegefx_diag\test109.log
echo.
pause
goto MENU

:T110
echo.
echo --- ALPHA-2: DWARVEN GATE (path2sd) ---
echo [Spawn = region start; walk to the big dwarven gate. TEST LIST:
echo  1. Clicking the stuck gate does NOT open it - the authored line
echo     floats ("This door seems to be stuck.") + console [door] refused.
echo  2. Regular doors nearby still click-open normally.
echo  3. If the area quest event fires (clear its trigger), the gate opens
echo     by message ([door] 0x... opened by message).]
echo.
set "SIEGEFX_DEBUG_LOG_FILE=%TEMP%\siegefx_diag\test110.log"
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/path2sd
set "SIEGEFX_DEBUG_LOG_FILE="
echo Log written to: %TEMP%\siegefx_diag\test110.log
echo.
pause
goto MENU

:T111
echo.
echo --- ALPHA-2: STAR DEVICE (gd_a_r1, late-game Glitterdelve) ---
echo [Spawn = region start. TEST LIST:
echo  1. Find the Star Device (console printed [locked] 0x... at load with
echo     its position context). Clicking it without key_glb_star floats
echo     "Locked." - it must NOT unlock.
echo  2. Crypt-style doors here author msg_scid_opening: opening one prints
echo     [door] chaining + downstream trigger lines.
echo  3. Elevators in this region ride like the farmhouse grate did.]
echo.
set "SIEGEFX_DEBUG_LOG_FILE=%TEMP%\siegefx_diag\test111.log"
dotnet "%RUN%" --play-region "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" /world/maps/map_world/regions/gd_a_r1
set "SIEGEFX_DEBUG_LOG_FILE="
echo Log written to: %TEMP%\siegefx_diag\test111.log
echo.
pause
goto MENU

:T112
echo.
echo --- NAV VERTICAL-REBIND REPRO (sd_r1 mine ledge, headless) ---
echo [test-110 field bug: walking the ledge horseshoe around the sealed
echo  t_sd_cap node, the funnel line grazes the ledge edge; the walker used
echo  to re-bind to the path2sd mountain top 27u above and strand there.
echo  Expect: "reached", every tick pos Y=17.00, ~29u walked vs 6.2 straight.
echo  A Y=44.00 line or "blocked" = regression.]
echo.
"%TOOL%" world follow "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" /world/maps/map_world/regions/path2sd,/world/maps/map_world/regions/bt_r1,/world/maps/map_world/regions/path2dm,/world/maps/map_world/regions/path2sd_a,/world/maps/map_world/regions/sd_r1,/world/maps/map_world/regions/sd_r2 "-88.1,17.0,135.8" "-83.6,17.0,140.0" 4.5 600
echo.
echo [Also available: "%%TOOL%%" world probe ^<map^> ^<terrain^> ^<regions^> ^<x,y,z or 0xGUID^>
echo  lists every nav tri in an XZ column / a snode's tri contribution;
echo  "world path ... --full" dumps a corridor with snode + seam provenance;
echo  "sno find ^<terrain-tank^> 0xMESHGUID" names + parses a mesh guid.]
echo.
pause
goto MENU

:T113
echo.
echo --- ENEMY ROAM-SIM (headless wander soak, path2sd..sd_r2 set) ---
echo [Every actor.gas mob gets the game's real wander driver and 90 sim-
echo  seconds at 20 Hz. Receipts: 0 FROZEN; no pile at a user-reported
echo  spot (the 2026-07-10 krug pile at -26.5,32,151 must stay gone).
echo  A handful of BLOCKED-heavy but moving actors is normal (pond fish,
echo  narrow trails). Use --near=x,z,r to reproduce a field report spot.]
echo.
"%TOOL%" region roam-sim "%DS1%\Maps\World.dsmap" "%DS1%\Resources\Terrain.dsres" /world/maps/map_world/regions/path2sd,/world/maps/map_world/regions/bt_r1,/world/maps/map_world/regions/path2dm,/world/maps/map_world/regions/path2sd_a,/world/maps/map_world/regions/sd_r1,/world/maps/map_world/regions/sd_r2 90
echo.
pause
goto MENU

:T114
echo.
echo --- SC-MP-EOS P3: multiplayer net round-trip (headless) ---
echo [Host and client MpSessions over the loopback transport. Asserts:
echo  join -^> world snapshot delivered intact, host-authoritative state
echo  deltas applied on the client, client Input drives the host sim,
echo  chat relays both ways, and the bounds-checked protocol reader
echo  flags a truncated frame instead of throwing. Expect ALL PASS.]
echo.
dotnet "%RUN%" --selftest-net
echo.
pause
goto MENU

:T115
echo.
echo --- FRONTEND CHROME SHOTS: offscreen PNG receipts ---
echo [Renders the FrontendScene chrome at the MainMenu + SinglePlayer
echo  settled states into goldens\frontend-shots\*.png via a hidden
echo  window - no clicking through the boot flow needed. Open the PNGs
echo  and compare against retail screenshots. Chrome layer only: the
echo  per-widget hover swaps + settled-pose labels RenderHost layers on
echo  top are not part of the receipt.]
echo.
dotnet "%RUN%" --frontend-shot "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" MainMenu --t=3.4
dotnet "%RUN%" --frontend-shot "%DS1%\Resources\Logic.dsres" "%DS1%\Resources\Objects.dsres" SinglePlayer --t=3.4
echo.
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
