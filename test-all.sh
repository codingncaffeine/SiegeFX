#!/usr/bin/env bash
# test-all.sh - GENERATED from test-all.bat by tools/test-menu.sh: edit
# test-all.bat and run tools/test-menu.sh (CI checks that the two agree).
#
# The phase-by-phase smoke test menu.
#   ./test-all.sh [--ds1=PATH] [--tool=PATH] [--run=PATH] [--refs=PATH]
set -u
cd "$(dirname "$0")" || exit 1

DS1=${SIEGEFX_DS1:-}
cfg="${XDG_CONFIG_HOME:-$HOME/.config}/siegefx/ds1path.txt"
if [ -z "$DS1" ] && [ -r "$cfg" ]; then
    DS1=$(grep -vE '^[[:space:]]*(#|$)' "$cfg" | head -1 | sed 's/^"//; s/"$//')
fi
TOOL=src/SiegeFX.Tools/bin/Release/net10.0/siegefx
RUN=src/SiegeFX.Runtime/bin/Release/net10.0/SiegeFX
REFS=_ds1refs
for arg in "$@"; do
    case "$arg" in
        --ds1=*) DS1=${arg#--ds1=} ;;
        --tool=*) TOOL=${arg#--tool=} ;;
        --run=*) RUN=${arg#--run=} ;;
        --refs=*) REFS=${arg#--refs=} ;;
        -h|--help)
            echo "Usage: ./test-all.sh [--ds1=PATH] [--tool=PATH] [--run=PATH] [--refs=PATH]"
            echo
            echo "  --ds1=PATH   Dungeon Siege install root (default: SIEGEFX_DS1, else"
            echo "               the first line of ~/.config/siegefx/ds1path.txt: ${DS1:-none})"
            echo "  --tool=PATH  siegefx CLI path           (default: $TOOL)"
            echo "  --run=PATH   SiegeFX engine path        (default: $RUN)"
            echo "  --refs=PATH  local reference assets dir (default: $REFS)"
            exit 0 ;;
        *) echo "unknown argument: $arg"; echo "run \"./test-all.sh --help\" for the parameter list"; exit 1 ;;
    esac
done
# What the tests name with the Windows variables of the same names.
TEMP=${TMPDIR:-/tmp}
LOCALAPPDATA=${XDG_DATA_HOME:-$HOME/.local/share}
dotnet=${DOTNET:-dotnet}
ERRORLEVEL=0

pause() { read -rp "Press Enter to continue . . . " _ || true; }
show_crash_log() {
    local log
    log="$(dirname "$RUN")/siegefx_crash.log"
    if [ -f "$log" ]; then
        echo "--- crash log ---"
        cat "$log"
        echo "------------------"
    fi
}
build() {
    echo
    "$dotnet" build SiegeFX.sln -c Release
    pause
}

if [ ! -x "$TOOL" ] || [ ! -x "$RUN" ]; then
    echo
    echo "Build output missing. Running dotnet build -c Release first..."
    if ! "$dotnet" build SiegeFX.sln -c Release; then
        echo "Build failed. Fix errors and re-run."
        pause
        exit 1
    fi
fi

menu() {
    clear
    printf '%s\n' "============================================================"
    printf '%s\n' "  SiegeFX v0.8.0 phase-by-phase smoke test"
    printf '%s\n' "============================================================"
    printf '%s\n' "  DS1 install : ${DS1}"
    echo
    printf '%s\n' "  1.  Phase 1 - Tank listing (Logic.dsres)"
    printf '%s\n' "  2.  Phase 2 - RAW texture decode (goblin.raw to PNG)"
    printf '%s\n' "  3.  Phase 4 - Static mesh viewer (boot.asp)"
    printf '%s\n' "  4.  Phase 5 - GAS parser (stitch_ds_r2.gas dump)"
    printf '%s\n' "  5.  Phase 6 - Single region viewer (fh_r1)"
    printf '%s\n' "  6.  Phase 6 - World streaming (walk between regions)"
    printf '%s\n' "  7.  Phase 7 - Skeletal animation (goblin walk clip)"
    printf '%s\n' "  8.  Phase 9a - Skrit-driven animation (basic_walk)"
    printf '%s\n' "  9.  Phase 8d - Skrit tick harness (CLI, no viewer)"
    printf '%s\n' "  10. Phase 10a - Template store (goblin grunt archetype resolution)"
    printf '%s\n' "  11. Phase 10b - Region actor instance loader (fh_r1)"
    printf '%s\n' "  12. Phase 10c+d - Spawn 181 actors, tick + broadcast (fh_r1)"
    printf '%s\n' "  13. Phase 10e - Play region (walk into fh_r1, 181 actors idling)"
    printf '%s\n' "  14. Phase 11a - Walkable-surface nav (fh_r1 stats + world-wide fuzz)"
    printf '%s\n' "  15. Phase 11b - A* pathfinding (fh_r1 hand path + world-wide fuzz)"
    printf '%s\n' "  16. Phase 11c - Nav follower (walk fh_r1 corridor tick-by-tick)"
    printf '%s\n' "  17. Phase 11d - Actors wander fh_r1 (181 followers on nav mesh)"
    printf '%s\n' "  18. Phase 12a - Template stats (goblin grunt + all 3W_goblin_* prefix)"
    printf '%s\n' "  19. Phase 12b - Combat sim (1000 duels: grunt vs grunt, guard vs grunt)"
    printf '%s\n' "  20. Phase 12c - Debug attack in fh_r1 (press F to hit nearest goblin)"
    printf '%s\n' "  21. Phase 12d - Loot table (grunt + krug scout, 10000-roll distribution)"
    printf '%s\n' "  22. Phase 13a-e - Farmboy PC + chase cam + LMB move + RMB attack + fair-fight stats (fh_r1)"
    printf '%s\n' "  23. Phase 14a-d - Pickup + equipment + weapon render (fh_r1)"
    printf '%s\n' "  24. Phase 15a   - Text overlay (DS1 copperplate font, fh_r1)"
    printf '%s\n' "  25. Phase 15b   - HP/MP HUD bars (live values, fh_r1)"
    printf '%s\n' "  26. Phase 15c   - Grid inventory panel (press I to toggle, fh_r1)"
    printf '%s\n' "  27. Phase 15d   - Pause menu (Esc to open; Resume / Quit, fh_r1)"
    printf '%s\n' "  28. Phase 16a   - Formulas dump (formulas.gas -> typed values)"
    printf '%s\n' "  29. Phase 16b   - HP/MP regen (~0.25/0.333 per sec at 10/10/10, fh_r1)"
    printf '%s\n' "  30. Phase 16c   - NPC aggro: walk into a krug, watch HP drop + regen (fh_r1)"
    printf '%s\n' "  31. Phase 16d   - XP + level: kill goblins, watch Lv/XP line on HUD (fh_r1)"
    printf '%s\n' "  32. Phase 17a   - Spells: dump catalog + show spell_zap by magic level"
    printf '%s\n' "  33. Phase 17a   - Spells: cast spell_zap with Q (mana 1, dmg 4-7 at L1, fh_r1)"
    printf '%s\n' "  34. Phase 17b   - Spell visuals: cyan bolt + face-snap on Q-cast (fh_r1)"
    printf '%s\n' "  35. Phase 17c   - Heal spell + W slot: spell_healing_wind self-cast (fh_r1)"
    printf '%s\n' "  36. Phase 18a   - Audio: cast SFX (Q = zap_cast.wav, W = healing_wind_cast.wav, fh_r1)"
    printf '%s\n' "  37. Phase 18b   - Audio: melee swing/hit/miss + monster death + level-up SFX (fh_r1)"
    printf '%s\n' "  38. Phase 18c   - Audio: 3D positional pan + falloff (walk away from a kill, listen, fh_r1)"
    printf '%s\n' "  39. Phase 19a   - Save: SaveFile JSON round-trip self-test (no window)"
    printf '%s\n' "  40. Phase 19c   - Save/Load: F5 quicksave + F9 quickload (kill stuff, F5, kill more, F9, fh_r1)"
    printf '%s\n' "  41. Phase 20a   - Dialogue parser self-test + RMB-talk to Edgaar in fh_r1"
    printf '%s\n' "  42. Phase 20b   - Quest log overlay (Accept Edgaar quest, press L, fh_r1)"
    printf '%s\n' "  43. Phase 20c   - Kill objectives + goal markers (kill 5 krug for Edgaar, fh_r1)"
    printf '%s\n' "  44. Phase 25c   - Authored store screen (Stonebridge shopkeepers, buy/sell, bt_r1)"
    printf '%s\n' "  45. Phase 21a-1 - Neighbor terrain preload (fh_r1 + first-ring neighbors visible)"
    printf '%s\n' "  46. Phase 21a-2 - Cross-boundary nav + actors + dialogue (walk into neighbor regions)"
    printf '%s\n' "  47. Phase 21a-3 - Rolling preload (no more invisible wall, walk arbitrarily far)"
    printf '%s\n' "  48. Phase 21b-1 - --diag mode (startup timings + per-second frame histogram)"
    printf '%s\n' "  49. Phase 21c-1 - NPC textures + static props (trees/barrels/fences/crops/candles in fh_r1)"
    printf '%s\n' "  50. Phase 21c-5 - Headless prop-texture audit across all 81 regions (CLI, no window)"
    printf '%s\n' "  51. Phase 21d-1 - Balance curves audit (XP/HP/MP/regen L1..L50, all skills, CLI)"
    printf '%s\n' "  52. Phase 21d-2a-i - ASP subset fuzz (parse all .asp in Objects.dsres, validate subsets)"
    printf '%s\n' "  53. Phase 21d-2a-ii - Per-subset texture render (visually verify farmboy clothing in fh_r1)"
    printf '%s\n' "  54. Phase 21d-2a-iii prep - Actor-coverage audit across all 81 regions (CLI, no window)"
    printf '%s\n' "  55. Phase 21d-2a-iv - BTRI cornerStart fix (visually verify farmboy webbing gone in fh_r1)"
    printf '%s\n' "  56. Phase 21d-2a-v  - Farmboy texture diag (magenta fallback + tex-resolve log)"
    printf '%s\n' "  57. Phase 21d-2a-v  - Subset-tint diag (each ASP subset = solid color: red/grn/blu/yel/mag)"
    printf '%s\n' "  58. Phase 21d-2a-v  - Plain play after uFlipV fix (face/hair detail should render)"
    printf '%s\n' "  59. Phase 21d-2a-vi - Dagger grip (90 deg X prerotation; piercing forward grip vs stab)"
    printf '%s\n' "  60. Phase 21d-2a-vii - Layered equipment (boots + chest texture override on farmboy)"
    printf '%s\n' "  61. Phase 21d-2a-viii-a - Hero variant audit + env-var pick (pos_a3 + skin_07 + pants_015)"
    printf '%s\n' "  62. Phase 21d-2a-viii-b - Character creator UI panel (SIEGEFX_CREATOR=1; Begin to spawn)"
    printf '%s\n' "  63. Phase 21d-2a-viii-c - Hero name + variant persistence through quicksave (F5/F9)"
    printf '%s\n' "  64. Phase 21d-2a-ix    - Audio coverage audit (Sound.dsres histogram + gap report)"
    printf '%s\n' "  65. Phase 21d-2a-xi    - Mood + region ambient bed audit (CLI; play-region for in-game loop)"
    printf '%s\n' "  66. Phase 21d-2a-xii   - SED registry audit (Sound.dsres pitch jitter + cap inventory)"
    printf '%s\n' "  67. Phase 9-SC-10      - Shield render verify (fh_r1 + SIEGEFX_DEBUG_DROP=shield)"
    printf '%s\n' "  68. Phase 9-SC-16 B-1+B-2 - Pcontent tier + wildcards + rarity (#club/2-3, #armor/-rare/..., #*/-unique/...)"
    printf '%s\n' "  69. Phase 10-SC-1 - Trigger matrix parser (fh_r1 special.gas + verb coverage)"
    printf '%s\n' "  70. Phase 10-SC-2 - Full chore dictionary into Actor.Clips (fh_r1 chore coverage)"
    printf '%s\n' "  71. Phase 10-SC-3 - PRS v0x202 + v0x302 loader (Objects.dsres prs fuzz, all 1962 files)"
    printf '%s\n' "  72. Phase 11-SC-7 - Land water seam stitching (fh_r1 nav + amphibious path 30,30 to water)"
    printf '%s\n' "  73. Phase 12-SC-3 - Mob loot frequency vs DS1 retail (krug_grunt/krug_scout/gremal)"
    printf '%s\n' "  74. Phase 12-SC-4/5 - Death pose + weapon-class attack chore (VISUAL, fh_r1)"
    printf '%s\n' "  75. Phase 12-SC-6 - PRS TRCR resync (Objects.dsres prs fuzz, expect 1855 v3 OK + 131 tracers)"
    printf '%s\n' "  76. Phase 17-SC-A1 - SpellExpr ** power op (spells survey + show fireball/iceshard)"
    printf '%s\n' "  77. Phase 17-SC-A2/A3 - SpellExpr placeholders + ternary (spells eval / show freeze)"
    printf '%s\n' "  78. Phase 17-SC-B    - Per-element spell projectile/impact VFX (spells elements)"
    printf '%s\n' "  79. Phase 17-SC-C    - Player chore_magic plays during moving casts"
    printf '%s\n' "  80. Phase 17-SC-D    - SfxScriptStore inventory (1074 effect_script* across 14 gas files)"
    printf '%s\n' "  81. Phase 17-SC-E    - Billboard particle backend (in-window F11 fire+smoke+sparks, F10 lightning)"
    printf '%s\n' "  82. Phase 17-SC-F-1  - sfx_script compiler IR (parser dump for fireball_emitter)"
    printf '%s\n' "  83. Phase 17-SC-F-2  - sfx_script VM receipt (TallySink: smoke_emitter + fire_emitter, 60 ticks)"
    printf '%s\n' "  84. Phase 17-SC-G    - Region emitters wired (in-window fh_r1, smoke columns over chimneys)"
    printf '%s\n' "  85. Phase 17-SC-H    - Spell cast sfx_script binding (spells dump w/ cast_sfx column + coverage)"
    printf '%s\n' "  86. Phase 17-SC-I    - Water UV scroll + waterwheel rotation (in-window fh_r1)"
    printf '%s\n' "  87. Phase 17-SC-J    - Per-instance scale_multiplier (breakable farmhouse door + foliage variation)"
    printf '%s\n' "  88. Phase 21-SC-SPELL-VFX-AUDIT - Visual-coverage audit across every offensive spell (Logic.dsres)"
    printf '%s\n' "  89. Phase 21-SC-SPELL-VFX-AUDIT - Visual verify fireball + iceshard (SIEGEFX_DEBUG_SPELLS launch)"
    printf '%s\n' "  90. Phase 21-SC-SCROLL          - Full scroll-UI test (16-spell roster + ground pile + glitter)"
    printf '%s\n' "  91. Phase 21-SC-SPELL-VISUAL    - Primitive sweep (10 spells, one per slice A-H + sphere)"
    printf '%s\n' "  92. Phase 21-SC-BARREL          - Breakable barrels (cursor + spell + frags + loot, fh_r1)"
    printf '%s\n' "  93. Phase 23-SC-OPTIONS         - Options Menu (F10 in-game; 4 tabs Video/Audio/Input/Game)"
    printf '%s\n' "  94. Phase 24-MAINMENU            - Boot to main menu (no args; splash to logo drop to 7 buttons)"
    printf '%s\n' "  95. SC-TSD-ANIM                  - Water frame-cycle + waterfall layer-2 modulate2x (fh_r1)"
    printf '%s\n' "  96. SC-QUEST-OBJ-A               - Talk-to-NPC quest objective (SIEGEFX_DEBUG_QUEST=quest_seek_gyorn, talk to Edgaar)"
    printf '%s\n' "  97. SC-QUEST-OBJ-F               - Full 24-quest Ehb catalog + chain (SIEGEFX_DEBUG_QUEST=quest_seek_gyorn, watch chain follow-up activate)"
    printf '%s\n' "  98. SC-QUEST-OBJ-C               - Pickup quest objective (SIEGEFX_DEBUG_QUEST=quest_grab_fireshot, basement spell_fireshot)"
    printf '%s\n' "  99. SC-QUEST-OBJ-D               - Deliver quest objective (SIEGEFX_DEBUG_QUEST=quest_merik_staff, hold staff + talk Merik)"
    printf '%s\n' " 100. SC-HUD-DATABAR               - Bottom-row HUD buttons (pause/HP-pot/MP-pot/labels/map/journal/menu) — click each in fh_r1"
    printf '%s\n' " 101. SC-HUD-OVERHEAD-BARS         - Floating HP/MP bars above every combatant (PC always on; enemies on hit/aggro)"
    printf '%s\n' " 102. SC-FADE-NODES-LNODE          - Spawn at fh_r1 farmhouse basement stairs (SIEGEFX_DEBUG_SPAWN=70,-4,-65); walk down + test dungeon reveal + click-pick"
    printf '%s\n' " 103. Phase 26 - PARTY RECRUIT     - Recruit Gyorn at Stonebridge (bt_r1): RMB him, click Accept; he follows + fights"
    printf '%s\n' " 104. SC-WEATHER                   - Mood weather audit (CLI, self-verifying) + fh_r1 storm eyes-test (rain/fog/lightning/thunder + rain loops)"
    printf '%s\n' " 105. SC-ELEVATOR                  - Ride the farmhouse grate lift (hc_r1): lever call, 5s descent with rider carry, fades, nav rebuild"
    printf '%s\n' " 106. ALPHA-1 CAMPAIGN AUDIT       - Completability sweep: unhandled components + undispatched trigger verbs across all 81 regions (CLI, self-verifying)"
    printf '%s\n' " 107. CRYPT STAIRS REPRO           - Spawn AT the cr_r1 stairs (path2crypts root, SIEGEFX_DEBUG_SPAWN); fade-flap / reveal / freeze repro; F7 = fade diag"
    printf '%s\n' " 108. ALPHA-2: TOWN + DIALOGUE     - Stonebridge (bt_r1): retail dialogue chrome w/ Gyorn+Adwana, vendors, recruit, save/load soak (F5/F9)"
    printf '%s\n' " 109. ALPHA-2: CRATES + SHRINE     - path2crypts: trapped crates spring, chests open+loot, life shrine heals, world gold credits"
    printf '%s\n' " 110. ALPHA-2: DWARVEN GATE        - path2sd: stuck use-toggle gate shows authored text on click; opens only via its quest message"
    printf '%s\n' " 111. ALPHA-2: STAR DEVICE         - gd_a_r1: locked usable reports Locked. without key_glb_star; crypt doors chain msg_scid_opening"
    printf '%s\n' " 112. NAV VERTICAL-REBIND REPRO    - sd_r1 mine ledge (headless): horseshoe walk must reach at Y=17, never teleport to the Y=44 mountain top"
    printf '%s\n' " 113. ENEMY ROAM-SIM               - headless 90s wander soak over the path2sd..sd_r2 set; expect 0 FROZEN and no field-report piles"
    printf '%s\n' " 114. SC-MP-EOS P3 NET ROUND-TRIP  - headless host<->client: join+snapshot+state-delta+input-authority+chat+malformed-frame safety (loopback)"
    printf '%s\n' " 115. FRONTEND CHROME SHOTS        - offscreen PNG receipts of the MainMenu + SinglePlayer chrome (goldens/frontend-shots; compare vs retail)"
    printf '%s\n' " 116. ENDGAME: GOM ARENA           - spawn in gom2 with dev console (tilde: god/kill) + debug spells; kill Gom then Super Gom = quest complete + VICTORY card"
    printf '%s\n' " 117. ENEMY CHASE-SIM AUDIT        - headless directed-movement audit (walker-vs-A* divergence, incl. generator children); new-game region set + optional all-region sweep"
    echo
    printf '%s\n' "  B.  Rebuild (dotnet build -c Release)"
    printf '%s\n' "  Q.  Quit"
    echo
}

T1() {
    echo
    printf '%s\n' "--- Phase 1: first 40 entries of Logic.dsres ---"
    "$TOOL" tank list "${DS1}/Resources/Logic.dsres" | more; ERRORLEVEL=$?
    pause
}
T2() {
    echo
    printf '%s\n' "--- Phase 2: decoding goblin.raw to goblin.png ---"
    "$TOOL" raw decode "${REFS}/goblin.raw" "${REFS}/goblin.png"; ERRORLEVEL=$?
    if [ -e "${REFS}/goblin.png" ]; then xdg-open "${REFS}/goblin.png" > /dev/null 2>&1 & fi
    pause
}
T3() {
    echo
    printf '%s\n' "--- Phase 4: boot.asp in viewer (RMB+WASD to fly, Esc to quit) ---"
    "$RUN" "${REFS}/boot.asp"; ERRORLEVEL=$?
}
T4() {
    echo
    printf '%s\n' "--- Phase 5: dump stitch_ds_r2.gas parse tree ---"
    "$TOOL" gas dump "${REFS}/stitch_ds_r2.gas" | more; ERRORLEVEL=$?
    pause
}
T5() {
    echo
    printf '%s\n' "--- Phase 6: load fh_r1 (Farmhouse region 1) ---"
    "$RUN" --region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
}
T6() {
    echo
    printf '%s\n' "--- Phase 6: world streaming starting at fh_r1 ---"
    "$RUN" --world "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres"; ERRORLEVEL=$?
}
T7() {
    echo
    printf '%s\n' "--- Phase 7: goblin walk animation ---"
    "$RUN" --anim "${REFS}/goblin.asp" "${REFS}/goblin_walk.prs" "${REFS}/goblin.raw"; ERRORLEVEL=$?
}
T8() {
    echo
    printf '%s\n' "--- Phase 9a: skrit-driven goblin animation (basic_walk.skrit) ---"
    "$RUN" --skrit-anim "${REFS}/goblin.asp" "${REFS}/skrit/basic_walk.skrit" "${REFS}/goblin_walk.prs" --texture "${REFS}/goblin.raw"; ERRORLEVEL=$?
}
T9() {
    echo
    printf '%s\n' "--- Phase 8d: tick basic_walk.skrit for 40 logic frames ---"
    "$TOOL" skrit tick "${REFS}/skrit/basic_walk.skrit" --ticks=40 --subanims=1 "--event=OnStartChore\$"; ERRORLEVEL=$?
    pause
}
T10() {
    echo
    printf '%s\n' "--- Phase 10a: template store - resolve 3W_goblin_grunt archetype ---"
    printf '%s\n' "[expect: chain 3W_goblin_grunt -> 3W_base_goblin -> actor_evil -> actor,"
    printf '%s\n' "         aspect.model inherited, 5 chore entries]"
    echo
    "$TOOL" templates show "${DS1}/Resources/Logic.dsres" 3W_goblin_grunt; ERRORLEVEL=$?
    echo
    printf '%s\n' "--- now listing all 3W_goblin_* templates ---"
    "$TOOL" templates list "${DS1}/Resources/Logic.dsres" --prefix=3W_goblin; ERRORLEVEL=$?
    pause
}
T11() {
    echo
    printf '%s\n' "--- Phase 10b: actor instances in fh_r1 (Farmhouse region 1) ---"
    printf '%s\n' "[expect: ~181 actors, templates include krug_scout/phrak/gremal/chicken]"
    echo
    "$TOOL" region actors "${DS1}/Maps/World.dsmap" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    pause
}
T12() {
    echo
    printf '%s\n' "--- Phase 10c+d: spawn 181 actors in fh_r1, broadcast OnStartChore\$ via bus ---"
    printf '%s\n' "[expect: spawned 181/181, all in LoopForever\$, bus posted 1 / delivered 181]"
    echo
    "$TOOL" region spawn "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1 "--broadcast=OnStartChore\$"; ERRORLEVEL=$?
    pause
}
T13() {
    echo
    printf '%s\n' "--- Phase 10e: fh_r1 with terrain + 181 actors, skrit-driven ---"
    printf '%s\n' "[RMB+WASD to fly, Esc to quit]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
}
T14() {
    echo
    printf '%s\n' "--- Phase 11a: fh_r1 walkable-surface nav stats ---"
    printf '%s\n' "[expect: ~1700 snodes placed, ~2400 floor groupings, ~27k floor faces]"
    echo
    "$TOOL" region nav "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    echo
    printf '%s\n' "--- Phase 11a: world-wide nav fuzz (81 regions, ~7400 unique SNOs) ---"
    printf '%s\n' "[expect: 0 region failures, floor ~160k, water ~20k, ignored ~430k]"
    echo
    "$TOOL" region nav-fuzz "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres"; ERRORLEVEL=$?
    pause
}
T15() {
    echo
    printf '%s\n' "--- Phase 11b: hand-picked path in fh_r1 (10,0,10 to 30,0,30) ---"
    printf '%s\n' "[expect: ~30-40 tris, ~35-unit centroid length]"
    echo
    "$TOOL" region path "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" /world/maps/map_world/regions/fh_r1 10,0,10 30,0,30; ERRORLEVEL=$?
    echo
    printf '%s\n' "--- Phase 11b: world-wide A* fuzz (81 regions, 20 samples each) ---"
    printf '%s\n' "[expect: biggest-component A* = 100%; random-pair ~40% reflects topology]"
    echo
    "$TOOL" region path-fuzz "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres"; ERRORLEVEL=$?
    pause
}
T16() {
    echo
    printf '%s\n' "--- Phase 11c: nav follower walks fh_r1 corridor (10,0,10 to 30,0,30 at 6 u/s) ---"
    printf '%s\n' "[expect: reaches goal in ~100 ticks (20 Hz), ~32 units walked vs ~28 straight-line]"
    echo
    "$TOOL" region follow "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" /world/maps/map_world/regions/fh_r1 10,0,10 30,0,30; ERRORLEVEL=$?
    pause
}
T17() {
    echo
    printf '%s\n' "--- Phase 11d: fh_r1 with 181 wandering actors on nav mesh ---"
    printf '%s\n' "[expect: nav mesh ~27k tri / 0 non-manifold; 181 followers wandering]"
    printf '%s\n' "[RMB+WASD to fly, Esc to quit — watch actors pathing around obstacles]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
}
T18() {
    echo
    printf '%s\n' "--- Phase 12a: combat stats for 3W_goblin_grunt ---"
    printf '%s\n' "[expect: life 1162, damage 142-204, defense 554, walk 2.51, combatant=yes]"
    echo
    "$TOOL" templates stats "${DS1}/Resources/Logic.dsres" 3W_goblin_grunt; ERRORLEVEL=$?
    echo
    printf '%s\n' "--- Phase 12a: all 3W_goblin_* variants (3 combatants, 5 inert parts) ---"
    echo
    "$TOOL" templates stats "${DS1}/Resources/Logic.dsres" --prefix=3W_goblin; ERRORLEVEL=$?
    pause
}
T19() {
    echo
    printf '%s\n' "--- Phase 12b: grunt vs grunt, 1000 duels, seed=42 ---"
    printf '%s\n' "[expect: 1000/1000 kills, mean hits ~10, mean damage ~112]"
    echo
    "$TOOL" templates combat "${DS1}/Resources/Logic.dsres" 3W_goblin_grunt 3W_goblin_grunt --duels=1000 --seed=42; ERRORLEVEL=$?
    echo
    printf '%s\n' "--- Phase 12b: guard vs grunt, 1000 duels, seed=42 ---"
    printf '%s\n' "[expect: 1000/1000 kills, mean hits ~4, mean damage ~284]"
    echo
    "$TOOL" templates combat "${DS1}/Resources/Logic.dsres" 3W_goblin_guard 3W_goblin_grunt --duels=1000 --seed=42; ERRORLEVEL=$?
    pause
}
T20() {
    echo
    printf '%s\n' "--- Phase 12c: debug attack in fh_r1 ---"
    printf '%s\n' "[walk up to a goblin, press F — expect \"debug-attack: hit ... for ~N (M/1163)\"]"
    printf '%s\n' "[after ~5-6 hits the actor freezes in place with *** DEAD *** log]"
    printf '%s\n' "[RMB+WASD to fly, F to attack nearest combatant in front, Esc to quit]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
}
T21() {
    echo
    printf '%s\n' "--- Phase 12d: loot table for 3W_goblin_grunt (structure + 10000-roll distribution) ---"
    printf '%s\n' "[expect: 100% equipped hm_g_c_1h1m_low, ~6% common drops, ~0.1% rare/unique]"
    echo
    "$TOOL" templates loot "${DS1}/Resources/Logic.dsres" 3W_goblin_grunt --rolls=10000 --seed=42; ERRORLEVEL=$?
    echo
    printf '%s\n' "--- Phase 12d: loot table for krug_scout (common fh_r1 spawn, 1000 rolls) ---"
    printf '%s\n' "[expect: 100% equipped dg_g_c_1h_fun, ~12% drop one of melee/potion/mana]"
    echo
    "$TOOL" templates loot "${DS1}/Resources/Logic.dsres" krug_scout --rolls=1000 --seed=42; ERRORLEVEL=$?
    pause
}
T22() {
    echo
    printf '%s\n' "--- Phase 13a-e: Farmboy PC + chase cam + LMB move + RMB attack + fair-fight stats (fh_r1) ---"
    printf '%s\n' "[expect: one Farmboy (male human) spawns at the NPC centroid]"
    printf '%s\n' "[LMB on terrain to move; RMB-tap on a goblin to attack]"
    printf '%s\n' "[RMB-drag still orbits the yaw — tap vs drag split by pixel drift]"
    printf '%s\n' "[C toggles chase/fly cam; F still fires the camera-forward debug attack]"
    printf '%s\n' "[13e: NPCs move at template walk_velocity; Farmboy hits with 1-3 dmg (multi-hit kills)]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
}
T23() {
    echo
    printf '%s\n' "--- Phase 14a-d: Pickup + equipment + weapon render (fh_r1) ---"
    printf '%s\n' "[Farmboy spawns visibly wielding dg_g_d_1h_fun (fun dagger, 2-4 dmg)]"
    printf '%s\n' "[kill a goblin, walk onto the beige pile cube to auto-pickup]"
    printf '%s\n' "[upgraded weapons auto-equip and swap the rendered model on the hand]"
    printf '%s\n' "[console logs equipment, pickup, equipped, and weapon-load events]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T24() {
    echo
    printf '%s\n' "--- Phase 15a: Text overlay (DS1 copperplate-light, fh_r1) ---"
    printf '%s\n' "[expect: white \"SiegeFX\" tag in the top-left corner of the window]"
    printf '%s\n' "[a second line shows the Farmboy's live x/y/z as he moves]"
    printf '%s\n' "[text uses DS1's b_gui_fnt_12p_copperplate-light font from Objects.dsres]"
    printf '%s\n' "[console prints \"hud font: ...\" once the atlas decodes]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T25() {
    echo
    printf '%s\n' "--- Phase 15b: HP/MP HUD bars (fh_r1) ---"
    printf '%s\n' "[expect: red HP bar under the SiegeFX/coords text in the top-left]"
    printf '%s\n' "[bar is 200px wide, captioned \"HP 50/50\" (Farmboy starts full)]"
    printf '%s\n' "[no MP bar — Farmboy template has max_mana=0 so it stays hidden]"
    printf '%s\n' "[walk into a krug and let it hit you to see the HP bar drain]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T26() {
    echo
    printf '%s\n' "--- Phase 15c + SC-9/10/13/14: Grid inventory + shield-on-bone (fh_r1) ---"
    printf '%s\n' "[press I to toggle a centered 8x5 inventory grid]"
    printf '%s\n' "[icons land per-template (b_gui_ig_*); multi-cell weapons span 1x2 / 1x3]"
    printf '%s\n' "[LMB-drag an item to a new cell to relocate it (saved across opens)]"
    printf '%s\n' "[LMB-drag out of the panel to drop the item back into the world]"
    printf '%s\n' "[each drop fires the template's [aspect][voice][put_down] *  cue]"
    printf '%s\n' "[SC-10: kill a shield-bearing enemy + pick up the shield -> renders on shield_grip]"
    printf '%s\n' "[press I again or Esc to dismiss the panel]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T27() {
    echo
    printf '%s\n' "--- Phase 15d: Pause menu (fh_r1) ---"
    printf '%s\n' "[press Esc to open a centered \"Paused\" panel with two buttons]"
    printf '%s\n' "[hover a button to highlight; LMB clicks while paused don't retarget]"
    printf '%s\n' "[Resume closes the menu; Quit closes the window]"
    printf '%s\n' "[Esc again from the menu also resumes]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T28() {
    echo
    printf '%s\n' "--- Phase 16a: Formulas dump (Logic.dsres -> formulas.gas) ---"
    printf '%s\n' "[expect: 10/10/10 -> MaxLife=49.0  MaxMana=30.0]"
    printf '%s\n' "[expect: gains rows sum to 1.00; XP table ~151 entries]"
    printf '%s\n' "[expect: lr 1/4 -> 0.250 HP/sec at str=10; mr 1/3 -> 0.333 MP/sec at int=10]"
    echo
    "$TOOL" formulas dump "${DS1}/Resources/Logic.dsres"; ERRORLEVEL=$?
    pause
}
T29() {
    echo
    printf '%s\n' "--- Phase 16b: HP/MP regen (fh_r1) ---"
    printf '%s\n' "[press H to take 5 HP + 5 MP off the player (offline regen check)]"
    printf '%s\n' "[then sit still and watch the bars climb back up]"
    printf '%s\n' "[at 10/10/10 a fresh hero recovers ~0.25 HP/sec and ~0.333 MP/sec]"
    printf '%s\n' "[full HP from 0 takes ~3 min; full MP from 0 takes ~90 sec]"
    printf '%s\n' "[also tests the shutdown-crash fix on Esc->Quit and window X]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T31() {
    echo
    printf '%s\n' "--- Phase 16d: XP + level (fh_r1) ---"
    printf '%s\n' "[HUD shows \"Lv 1  XP 0/N\" under the HP/MP bars on spawn]"
    printf '%s\n' "[click goblins to attack; XP ticks up by damage dealt + kill bonus]"
    printf '%s\n' "[level 2 fires around ~150-200 xp; console prints \"*** LEVEL UP! ***\"]"
    printf '%s\n' "[on level-up: STR/DEX/INT auto-grow by Melee proportional gains (0.64/0.27/0.09)]"
    printf '%s\n' "[HP bar max grows on level-up; current HP unchanged (DS1 doesn't auto-heal)]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T32() {
    echo
    printf '%s\n' "--- Phase 17a: Spells dump + spell_zap evaluated ---"
    "$TOOL" spells dump "${DS1}/Resources/Logic.dsres"; ERRORLEVEL=$?
    echo
    "$TOOL" spells show "${DS1}/Resources/Logic.dsres" spell_zap; ERRORLEVEL=$?
    pause
}
T33() {
    echo
    printf '%s\n' "--- Phase 17a: Cast spell_zap (fh_r1) ---"
    printf '%s\n' "[HUD shows \"spellbook: primary <- spell_zap\" in the launch log]"
    printf '%s\n' "[aim cursor at a krug, press Q to cast: mana drops by 1, target takes 4-7 dmg]"
    printf '%s\n' "[outside 8u range: \"out of range\" floats up; no mana: \"no mana\" floats up]"
    printf '%s\n' "[cooldown is 0.15s so spam-Q just rate-limits to ~6 casts/sec]"
    printf '%s\n' "[kills via spell credit XP under SkillKind.CombatMagic]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T34() {
    echo
    printf '%s\n' "--- Phase 17b: Spell visuals (fh_r1) ---"
    printf '%s\n' "[press Q on a krug; a cyan bolt streaks from your chest to the target]"
    printf '%s\n' "[bolt lasts ~0.3s with a 5-dot fading trail]"
    printf '%s\n' "[PC snaps to face the target on cast (no more shooting out of his back)]"
    printf '%s\n' "[damage popup + mana drain are unchanged from 17a]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T35() {
    echo
    printf '%s\n' "--- Phase 17c: Heal spell + W slot (fh_r1) ---"
    printf '%s\n' "[Q casts spell_zap (offensive instant-hit, primary slot)]"
    printf '%s\n' "[W casts spell_healing_wind (self-target heal, secondary slot)]"
    printf '%s\n' "[W is silent at full HP (\"at full health\"); take damage from a krug first]"
    printf '%s\n' "[L1 heal: ~3.77 HP for 10.3 mana (so a fresh hero gets ~3 casts)]"
    printf '%s\n' "[3-second cooldown on the heal slot, independent from the Q cooldown]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T38() {
    echo
    printf '%s\n' "--- Phase 18c: 3D positional audio (fh_r1) ---"
    printf '%s\n' "[Hit a krug to your LEFT: hit/miss sound pans left in headphones]"
    printf '%s\n' "[Walk ~30 units away from a fight: hits and screams fade out (max=40 units)]"
    printf '%s\n' "[Cast SFX (Q/W) stay player-locked since they're \"your\" sounds]"
    printf '%s\n' "[Console logs: 'audio: ... InverseDistanceClamped attenuation']"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T37() {
    echo
    printf '%s\n' "--- Phase 18b: Combat SFX (fh_r1) ---"
    printf '%s\n' "[RMB on a krug: swing variant + hit (or miss if dealt=0)]"
    printf '%s\n' "[Kill it: matching die_*.wav fires (krug_scout / krug_dog / goblin / gremal)]"
    printf '%s\n' "[Cross an XP threshold: level_up_melee.wav punctuates the LEVEL UP toast]"
    printf '%s\n' "[Console should show:  audio: 'swing_01..04', 'hit_flesh_1..5', 'die_*', 'level_up' loaded]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T36() {
    echo
    printf '%s\n' "--- Phase 18a: Audio engine + cast SFX (fh_r1) ---"
    printf '%s\n' "[Q (zap)  plays /sound/effects/s_e_spell_zap_cast.wav from Sound.dsres]"
    printf '%s\n' "[W (heal) plays /sound/effects/s_e_spell_healing_wind_cast.wav]"
    printf '%s\n' "[Console should show:  audio: OpenAL Soft up (16 voices)]"
    printf '%s\n' "[And:  audio: 'spell_zap_cast' / 'spell_healing_wind_cast' loaded]"
    printf '%s\n' "[SFX-disabled fallback: any OpenAL failure leaves the rest of the game playable]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T39() {
    echo
    printf '%s\n' "--- Phase 19a: SaveFile JSON round-trip (no window) ---"
    printf '%s\n' "[expect: \"[selftest-save] OK - 3 actor(s), player + camera, schema v1 round-tripped at <path>\"]"
    printf '%s\n' "[exits 0 on success, 1 with field-by-field diffs on failure]"
    echo
    "$RUN" --selftest-save; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    pause
}
T40() {
    echo
    printf '%s\n' "--- Phase 19c: F5 quicksave + F9 quickload (fh_r1) ---"
    printf '%s\n' "[Pre-save: kill 1-2 krug or take HP damage to make state interesting]"
    printf '%s\n' "[Press F5: console logs \"save: wrote N actor(s) + M pile(s) -> ...quicksave.save\"]"
    printf '%s\n' "[Continue: kill more stuff, pick up loot, walk around, level up]"
    printf '%s\n' "[Press F9: scene snaps back to F5 state — dead krug revive (if alive at save)]"
    printf '%s\n' "[or stay dead (if dead at save); HP/MP/XP/Level revert; piles return]"
    printf '%s\n' "[Save lives at: ${LOCALAPPDATA}/SiegeFX/Saves/quicksave.save]"
    printf '%s\n' "[Region check: a save from a different region path is refused on F9]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T41() {
    echo
    printf '%s\n' "--- Phase 20a: dialogue parser self-test (no window) ---"
    printf '%s\n' "[expect: \"[selftest-dialogue] OK - edgaar branching tree (3 nodes...) parsed correctly\"]"
    printf '%s\n' "[exits 0 on success, 1 with field diffs on failure]"
    echo
    "$RUN" --selftest-dialogue; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== selftest exited with code ${EXITCODE} ==="
    echo
    printf '%s\n' "--- Phase 20a: visual walkthrough (fh_r1) ---"
    printf '%s\n' "[Walk to Edgaar (the farmer) and right-click him: dialogue panel opens.]"
    printf '%s\n' "[Node 1 has a \"More\" button: click to advance.]"
    printf '%s\n' "[Node 2 is the quest fork with \"Accept\" / \"Decline\":]"
    printf '%s\n' "[  Accept   -> console logs \"talk: quest_edgaar_basement activated\"]"
    printf '%s\n' "[  Decline  -> jumps to the polite-tail node, then \"Continue\" closes.]"
    printf '%s\n' "[Esc while open closes the panel without firing the quest.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T30() {
    echo
    printf '%s\n' "--- Phase 16c: NPC aggro (fh_r1) ---"
    printf '%s\n' "[walk into a krug pen and stop within ~8u; the krug should chase]"
    printf '%s\n' "[once they're adjacent (~1.8u) they swing every 1.5s and chip ~4-8 HP]"
    printf '%s\n' "[step away past ~14u to disengage; HP regen kicks back in]"
    printf '%s\n' "[chickens should still wander uninterested - they have no [attack] block]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T42() {
    echo
    printf '%s\n' "--- Phase 20b: Quest log overlay (fh_r1) ---"
    printf '%s\n' "[RMB Edgaar, click \"More\", click \"Accept\" — console logs activation]"
    printf '%s\n' "[Press L: quest log opens, \"Edgaar Basement\" listed under ACTIVE]"
    printf '%s\n' "[Re-pitch: RMB Edgaar, Accept again — log says \"re-pitched (already in journal)\"]"
    printf '%s\n' "[F5 quicksave + F9 quickload: quest survives the round-trip]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T43() {
    echo
    printf '%s\n' "--- Phase 20c: Kill objectives + goal markers (fh_r1) ---"
    printf '%s\n' "[Accept Edgaar's basement quest — krug-kill objective: 0/5]"
    printf '%s\n' "[Quest log (L) shows \"(0/5)\" beside the objective line]"
    printf '%s\n' "[Yellow chevron paints above the nearest live krug; clamps to screen edge if behind]"
    printf '%s\n' "[Each krug kill increments progress + a \"+gold\" floats off the corpse]"
    printf '%s\n' "[On the 5th kill the entry flips to COMPLETED and the marker disappears]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T44() {
    echo
    printf '%s\n' "--- Phase 25c: Authored store screen (Stonebridge, bt_r1) ---"
    printf '%s\n' "[RMB a shopkeeper: Adwana (spells), Jonn (smith), or Gyorn - store screen opens]"
    printf '%s\n' "[Right-docked authored chrome: cpbox frame, portrait box, name plate, 8x10 shelf grid]"
    printf '%s\n' "[Six tabs ARMOR/WEAPONS/SHIELDS + SPELLS/POTIONS/MISC - active tab shifts down 5px]"
    printf '%s\n' "[Adwana SPELLS tab: starter-band scrolls (zap/flash/fireshot tier, 20-150g range)]"
    printf '%s\n' "[Hover a shelf item - name + price readout; unaffordable reads red]"
    printf '%s\n' "[LMB a shelf item = buy (gold debits); your inventory co-opens - LMB your item = sell]"
    printf '%s\n' "[Previous/Next page when a tab overflows the 8x10 grid; Close button exits]"
    printf '%s\n' "[Insufficient gold: console logs \"trade: cannot afford ...\" and rejects]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/bt_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T45() {
    echo
    printf '%s\n' "--- Phase 21a-1: Neighbor terrain preload (fh_r1) ---"
    printf '%s\n' "[Launch log shows \"neighbor preload: N/M region(s) (X instance(s) ...)\"]"
    printf '%s\n' "[Expect M (declared) ~= 2-4 for fh_r1; N (placed) should equal M]"
    printf '%s\n' "[unresolved + dangling stitch counts should be 0 for shipped fh_r1]"
    printf '%s\n' "[In-game: fly to the south/east edge of fh_r1 — neighbor terrain is visible]"
    printf '%s\n' "[WITHOUT this load, the world would just end at the region boundary]"
    printf '%s\n' "[Actors / nav / dialogue still operate only inside fh_r1 — that's 21a-2]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T46() {
    echo
    printf '%s\n' "--- Phase 21a-2: Cross-boundary nav + actors + dialogue (fh_r1 + neighbors) ---"
    printf '%s\n' "[Builds on 21a-1: neighbor regions are now first-class for gameplay too]"
    printf '%s\n' "[Launch log: \"neighbor preload...\" THEN actor + nav line includes neighbors]"
    printf '%s\n' "[In-game: walk south/east past the old fh_r1 boundary — no more invisible wall]"
    printf '%s\n' "[Nav mesh now spans player region + first-ring neighbors as one graph]"
    printf '%s\n' "[Neighbor actors (krug/goblins beyond the boundary) are alive and aggro you]"
    printf '%s\n' "[RMB an NPC living in a neighbor region — their dialogue tree opens normally]"
    printf '%s\n' "[If you fly past the *outer* edge of the preloaded ring you'll still hit a wall]"
    printf '%s\n' "[That's expected — eviction + rolling preload is 21a-3]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T48() {
    echo
    printf '%s\n' "--- Phase 21b-1: --diag startup timings + frame histogram ---"
    printf '%s\n' "[Boots fh_r1 with --diag; expect a \"diag: startup timings\" table at end of OnLoad]"
    printf '%s\n' "[Then a per-second \"diag: frame avg=... p50=... p99=... max=...\" line during play]"
    printf '%s\n' "[Stages measured: region, neighbor preload, world, play actors, anim, skrit]"
    printf '%s\n' "[Per-frame stats: avg, p50, p99, max in ms; FPS; live actor + region counts]"
    printf '%s\n' "[21b-2/3 will use this output to target the actual hotspots]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1 --diag; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T50() {
    echo
    printf '%s\n' "--- Phase 21c-5: Headless prop-texture audit (all regions) ---"
    printf '%s\n' "[Walks every static-prop placement in EVERY shipped DS1 region through]"
    printf '%s\n' "[the same texset rules the runtime uses (template override -> BMSH]"
    printf '%s\n' "[default -> -01..-08 variant fallback) and prints per-template misses.]"
    printf '%s\n' "[With Terrain.dsres in the resolver, expect 0 untextured + exit code 0.]"
    echo
    "$TOOL" region prop-textures "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" all "--terrain=${DS1}/Resources/Terrain.dsres" --top=10 --list-misses; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== audit exited with code ${EXITCODE} ==="
    pause
}
T49() {
    echo
    printf '%s\n' "--- Phase 21c-1: NPC textures + static props ---"
    printf '%s\n' "[NPCs (Edgaar/Norick/krug/goblin/farmboy) now render with their authored albedo]"
    printf '%s\n' "[Static props from non_interactive/container/inventory/interactive/emitter .gas:]"
    printf '%s\n' "[   trees, bushes, foliage, candles, chairs, tables, baskets, jugs, dishes,]"
    printf '%s\n' "[   barrels, crates, woodboxes, breakable doors, respawn statues, smoke emitters]"
    printf '%s\n' "[Launch log shows: \"static props: N/M placed (K unique mesh(es); skipped ...)\"]"
    printf '%s\n' "[The world should look densely populated, not bare terrain dotted with NPCs]"
    printf '%s\n' "[Cross into a neighbor — log shows another \"static props:\" line for the new region]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T51() {
    echo
    printf '%s\n' "--- Phase 21d-1: Balance curves audit (CLI, no window) ---"
    printf '%s\n' "[Walks each SkillKind from L1 to L50 simulating a player who only earns]"
    printf '%s\n' "[that skill's XP. Prints CumXP / STR / DEX / INT / MaxHP / MaxMP / regen rates]"
    printf '%s\n' "[and time-to-full-HP / MP at every level. Flags monotonicity violations.]"
    printf '%s\n' "[With shipped formulas.gas, expect 0 violations across all 4 skills.]"
    echo
    "$TOOL" balance curve "${DS1}/Resources/Logic.dsres" --max-level=50 --skill=all; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== audit exited with code ${EXITCODE} ==="
    pause
}
T52() {
    echo
    printf '%s\n' "--- Phase 21d-2a-i: ASP subset fuzz (CLI, no window) ---"
    printf '%s\n' "[Parses every .asp in Objects.dsres and validates that the per-submesh]"
    printf '%s\n' "[BSMM (textureIndex, faceSpan) records sum to BTRI's face count.]"
    printf '%s\n' "[Histogram shows how many meshes use 1/2/N subsets — confirms farmboy is]"
    printf '%s\n' "[multi-subset (skin + clothing) while 1-texture creatures stay single-subset.]"
    echo
    "$TOOL" asp subset-fuzz "${DS1}/Resources/Objects.dsres"; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== fuzz exited with code ${EXITCODE} (0 = all clean) ==="
    pause
}
T53() {
    echo
    printf '%s\n' "--- Phase 21d-2a-ii: Per-subset texture render (fh_r1) ---"
    printf '%s\n' "[Farmboy ASP carves into 5 subsets across 2 textures: skin (slot 0)]"
    printf '%s\n' "[covers head/hands/legs flesh, clothing (slot 1 = b_c_pos_a1_015) covers]"
    printf '%s\n' "[shirt+pants strip. Renderer now binds + draws per subset, so the]"
    printf '%s\n' "[clothing strip should NOT inherit the skin texture.]"
    printf '%s\n' "[Visually verify: farmboy's torso/legs show fabric pattern, not skin tone.]"
    printf '%s\n' "[Krug + goblin (single-subset meshes) should render unchanged.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
}
T54() {
    echo
    printf '%s\n' "--- Phase 21d-2a-iii prep: Actor-coverage audit (CLI, no window) ---"
    printf '%s\n' "[Mirror of \`region prop-textures all\` for the NPC layer. Walks every]"
    printf '%s\n' "[actor.gas placement in all 81 shipped regions, resolves the template's]"
    printf '%s\n' "[aspect.model -> .asp, then walks AspMesh.Subsets and probes each]"
    printf '%s\n' "[slot's texture via the same (template-override-by-slot, mesh.TextureNames]"
    printf '%s\n' "[slot) precedence ResolveActorTexture uses at runtime.]"
    printf '%s\n' "[Catches missing meshes, missing slot textures, and parse breakers BEFORE]"
    printf '%s\n' "[the Farmhouse -> Castle Ehb playtest hits them.]"
    echo
    "$TOOL" region actor-coverage "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" all "--terrain=${DS1}/Resources/Terrain.dsres" --top=15; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== audit exited with code ${EXITCODE} (0 = all clean) ==="
    pause
}
T55() {
    echo
    printf '%s\n' "--- Phase 21d-2a-iv: BTRI cornerStart fix (fh_r1) ---"
    printf '%s\n' "[BTRI face indices for ASP version > 2.2 are subtexture-local, not]"
    printf '%s\n' "[submesh-local. Without applying per-subtexture cornerStart, multi-]"
    printf '%s\n' "[subtexture characters (farmboy = 2 subtextures in BSUB[0]) reference]"
    printf '%s\n' "[wrong corners and render as web-like geometry: webbing between forearms,]"
    printf '%s\n' "[hammer pants, partial hair, smeared face. Single-subtexture meshes]"
    printf '%s\n' "[(krug, every monster) are unaffected because cornerStart[0] = 0.]"
    printf '%s\n' "[Visually verify: farmboy is no longer \"web-man\" -- arms are detached,]"
    printf '%s\n' "[body has correct silhouette. Texture coverage on clothing/hair is a]"
    printf '%s\n' "[separate Phase 21d-2a-v issue.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
}
T56() {
    echo
    printf '%s\n' "--- Phase 21d-2a-v diag: farmboy texture binding ---"
    printf '%s\n' "[Two diagnostic switches active in this run:]"
    printf '%s\n' "[  SIEGEFX_DEBUG_FALLBACK=1 -- fragment shader paints uHasTexture=0]"
    printf '%s\n' "[    fragments BRIGHT MAGENTA instead of the sand-toned fallback. If]"
    printf '%s\n' "[    farmboy's clothing strip turns magenta, slot 1 isn't binding a]"
    printf '%s\n' "[    texture. If it stays skin/tan, the texture binds but its actual]"
    printf '%s\n' "[    sampled pixels look skin-colored (texture-content question).]"
    printf '%s\n' "[  SIEGEFX_TEX_RESOLVE_LOG=1 -- prints one line per (template, slot)]"
    printf '%s\n' "[    on first resolve, so we can grep \"tpl=farmboy\" in the log to see]"
    printf '%s\n' "[    OK/MISS + the resolved basename for each subset.]"
    echo
    printf '%s\n' "Please run, eyeball the player, then close the window and copy the]"
    printf '%s\n' "[console lines starting with \"[tex-resolve\" plus a one-line description]"
    printf '%s\n' "[of what farmboy looked like (magenta where? skin tone where?).]"
    echo
    export SIEGEFX_DEBUG_FALLBACK="1"
    export SIEGEFX_TEX_RESOLVE_LOG="1"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    unset SIEGEFX_DEBUG_FALLBACK
    unset SIEGEFX_TEX_RESOLVE_LOG
}
T57() {
    echo
    printf '%s\n' "--- Phase 21d-2a-v diag: subset-tint visualizer ---"
    printf '%s\n' "[Each ASP subset draws as a unique solid color (no texture, no lighting):]"
    printf '%s\n' "[   subset 0 = RED     subset 1 = GREEN    subset 2 = BLUE]"
    printf '%s\n' "[   subset 3 = YELLOW  subset 4 = MAGENTA  (cyan/orange/purple if more)]"
    echo
    printf '%s\n' "[Look at the player's farmboy. Note which body region is which color.]"
    printf '%s\n' "[Expected (if BSMM-to-geometry is correct):]"
    printf '%s\n' "[   subset 0 (RED, 72 tris)    = head/face/hair       -- skin texture]"
    printf '%s\n' "[   subset 1 (GREEN, 300 tris) = torso/arms/legs      -- clothing texture]"
    printf '%s\n' "[   subset 2 (BLUE, 142 tris)  = ?                    -- skin texture]"
    printf '%s\n' "[   subset 3 (YELLOW, 112 tris)= ?                    -- skin texture]"
    printf '%s\n' "[   subset 4 (MAGENTA, 80 tris)= ?                    -- skin texture]"
    echo
    printf '%s\n' "[Also note ALL OTHER NPCs in fh_r1 -- if mismatches show up across many,]"
    printf '%s\n' "[it's a parser issue, not a farmboy-only quirk.]"
    echo
    export SIEGEFX_SUBSET_TINT="1"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    unset SIEGEFX_SUBSET_TINT
}
T58() {
    echo
    printf '%s\n' "--- Phase 21d-2a-v: plain play after uFlipV fix ---"
    printf '%s\n' "[Default skinned uFlipV is now 0 (was 1 for v2.5+). Subset-tint diag in]"
    printf '%s\n' "[option 57 confirmed asp UV V[0.01,0.54] for the head subset must land in]"
    printf '%s\n' "[GL V near 0 to sample the face/hair region of the .raw — the prior flip]"
    printf '%s\n' "[pushed those UVs into the bottom-strip brown gradient, hence smeared face.]"
    echo
    printf '%s\n' "[Look for: face features visible, hair on top of head, brown leather vest]"
    printf '%s\n' "[on the chest, white peasant pants on the legs. Forearms/hands SHOULD be]"
    printf '%s\n' "[skin-toned (sampling the gradient strip in the texture). Boots still]"
    printf '%s\n' "[missing — that is a separate equipment-composition slice, not a tex bug.]"
    echo
    printf '%s\n' "[If other NPCs in fh_r1 (Edgaar, villagers, krug attackers) now look wrong,]"
    printf '%s\n' "[report which and we'll add per-mesh-version override. SIEGEFX_FORCE_FLIPV=1]"
    printf '%s\n' "[forces the old behavior back if you want to A/B compare.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
}
T59() {
    echo
    printf '%s\n' "--- Phase 21d-2a-vi: dagger grip + idle stance + walk anim (RESOLVED) ---"
    printf '%s\n' "[Four fixes layered into this run, all driven by the same farmboy + dagger]"
    printf '%s\n' "[equip slot:]"
    echo
    printf '%s\n' "[  1. Dagger TEXTURE: solid grey blade (was rainbow). Weapon mesh shader]"
    printf '%s\n' "[     uFlipV flipped 1 -> 0 to match every other DS1 .raw bottom-up convention.]"
    echo
    printf '%s\n' "[  2. Dagger GRIP: 180-deg X prerotation on weapon_grip (ASPImport.ms says 90,]"
    printf '%s\n' "[     but our pipeline empirically needs 180 - likely interaction with bind]"
    printf '%s\n' "[     180-X + the FlipUp coord-system handling in the BVA path).]"
    echo
    printf '%s\n' "[  3. Idle STANCE: ActorSpawner picks fs1 (1H melee) idle when the equipped]"
    printf '%s\n' "[     weapon specializes weapon_melee; stance is also tried OUTSIDE the]"
    printf '%s\n' "[     authored chore_stances list so chore_walk picks up fs1 even when its]"
    printf '%s\n' "[     template only authors fs0.]"
    echo
    printf '%s\n' "[  4. WALK animation: weapon-attach loop now mirrors the body's walk-swap]"
    printf '%s\n' "[     so the dagger tracks the wrist through the walk cycle instead of]"
    printf '%s\n' "[     floating at the idle pose while the arm swings through it.]"
    echo
    printf '%s\n' "[F1 in-game cycles 12 grip-prerotation presets (X/Y/Z 180/90/-90 + compounds);]"
    printf '%s\n' "[active preset prints to console. Default = X 180.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T60() {
    echo
    printf '%s\n' "--- Phase 21d-2a-vii: layered equipment composition ---"
    printf '%s\n' "[base_farmboy ships [inventory][equipment]:]"
    printf '%s\n' "[  es_weapon_hand = dg_g_d_1h_fun (handled by 21d-2a-vi weapon attach)]"
    printf '%s\n' "[  es_feet        = bo_bo_le_light]"
    printf '%s\n' "[  es_spellbook   = book_glb_magic_01 (UI-only, no 3D mesh)]"
    echo
    printf '%s\n' "[DS1 layers each equipped item as a separate ASP attached to the body via]"
    printf '%s\n' "[bone names. Boots, helms, gauntlets share the body's biped skeleton; their]"
    printf '%s\n' "[ASPs use IDENTICAL bone names so AnimationRuntime.ComputeSkinMatrices]"
    printf '%s\n' "[name-keyed bone map lets us pose them against the body's clip + time.]"
    echo
    printf '%s\n' "[Boot mesh derivation:]"
    printf '%s\n' "[  body.armor_version=gah_fb + body type=a1 (from aspect.model suffix)]"
    printf '%s\n' "[  + armor_lookup.gas[a1]=(b,b)  + defend.armor_type=type1 (from bo_bo_le_light)]"
    printf '%s\n' "[  -> m_c_gah_fb_boot_type1_b.asp]"
    printf '%s\n' "[Boot texture derivation:]"
    printf '%s\n' "[  defend.armor_style=068 -> b_a_boot_068.raw]"
    echo
    printf '%s\n' "[Pre-flight CLI: equipment-audit dumps the resolved layers per slot]"
    "$TOOL" templates equipment-audit "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" farmboy; ERRORLEVEL=$?
    echo
    printf '%s\n' "[Visual: launch fh_r1 - farmboy spawns with leather boots + dagger + walk anim]"
    printf '%s\n' "[Watch console: \"equip: layered es_feet = bo_bo_le_light mesh=... bones=37 tex=... OK\"]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T61() {
    echo
    printf '%s\n' "--- Phase 21d-2a-viii-a: hero variant resolver (env-var quick-pick) ---"
    printf '%s\n' "[DS1 ships ~18,300 hero variants: 7 body types (pos_a1..a7) x ~32 skin tones]"
    printf '%s\n' "[x ~41 pants colors per body, gendered into farmboy and farmgirl. The shipped]"
    printf '%s\n' "[heroes.gas hardcodes a single (a1, skin_04, pants_008) point in that space.]"
    echo
    printf '%s\n' "[The full character creator UI ships in slice viii-b (built from the authentic]"
    printf '%s\n' "[DS1 character_select.gas layout under /ui/interfaces/frontend/). For now,]"
    printf '%s\n' "[SIEGEFX_HERO_GENDER / _BODY / _SKIN / _PANTS env vars feed the same picker]"
    printf '%s\n' "[the UI will use, exercising the resolver end-to-end without UI work.]"
    echo
    printf '%s\n' "[1/2] Headless audit: enumerate every variant traceable in Objects.dsres"
    "$TOOL" templates hero-variants "${DS1}/Resources/Objects.dsres"; ERRORLEVEL=$?
    echo
    printf '%s\n' "[2/2] Visual: pick boy + body 3 + skin 07 + pants 015, spawn into fh_r1]"
    printf '%s\n' "[Watch console: \"  player: variant pick gender=Boy body=3 skin=07 pants=015\"]"
    printf '%s\n' "[Then: \"  player: 'farmboy' did spawn\" with model=m_c_gah_fb_pos_a3]"
    echo
    export SIEGEFX_HERO_GENDER="boy"
    export SIEGEFX_HERO_BODY="3"
    export SIEGEFX_HERO_SKIN="07"
    export SIEGEFX_HERO_PANTS="015"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    unset SIEGEFX_HERO_GENDER
    unset SIEGEFX_HERO_BODY
    unset SIEGEFX_HERO_SKIN
    unset SIEGEFX_HERO_PANTS
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T64() {
    echo
    printf '%s\n' "--- Phase 21d-2a-ix: audio coverage audit ---"
    printf '%s\n' "[Headless histogram of Sound.dsres: 626 wavs across 46 s_e_<prefix> categories,]"
    printf '%s\n' "[cross-referenced against the static wired-id list in CmdAudioCoverage. Output:]"
    printf '%s\n' "[per-category authored / wired / gap, plus an \"unwired categories\" summary.]"
    printf '%s\n' "[Use --list-unwired=PREFIX to see the full unwired entries for a category.]"
    printf '%s\n' "[Use --list-orphan-categories for first-5 samples of every zero-wired family.]"
    echo
    "$TOOL" audio coverage "${DS1}/Resources/Sound.dsres"; ERRORLEVEL=$?
    echo
    printf '%s\n' "=== audit-only command (no game launch) ==="
    pause
}
T65() {
    echo
    printf '%s\n' "--- Phase 21d-2a-xi: mood + region ambient bed audit ---"
    printf '%s\n' "[Two halves: first a CLI dump of every parsed mood + the per-region default-mood]"
    printf '%s\n' "[picker the runtime applies on region entry; then a play-region launch where you]"
    printf '%s\n' "[can hear the looping bed. fh_r1 is intentionally silent (DS1 used positional]"
    printf '%s\n' "[emitters there, not a mood track) — walk into a region with an audible bed]"
    printf '%s\n' "[(e.g. cr_r1 crypts -> s_e_ambient_crypt) to confirm the swap. Watch the console:]"
    printf '%s\n' "[look for \"ambient: region 'X' -> mood 'Y' -> 'Z'\" lines on each region change.]"
    echo
    "$TOOL" mood list "${DS1}/Resources/Logic.dsres" --map=world --regions; ERRORLEVEL=$?
    echo
    printf '%s\n' "--- launching play-region (Ctrl+C to skip the in-game half) ---"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== xi exited with ${EXITCODE} ==="
    pause
}
T66() {
    echo
    printf '%s\n' "--- Phase 21d-2a-xii: SED registry audit ---"
    printf '%s\n' "[DS1 ships a Sound Effect Descriptor (SED) layer that authors per-fire pitch]"
    printf '%s\n' "[jitter, fixed transposes, and concurrent-voice caps for every sound that wants]"
    printf '%s\n' "[variation. Each SED is a *_sed.gas file in /sound/effects/; 165 ship in DS1.]"
    printf '%s\n' "[The runtime loads them at audio init and applies the rate range when playing.]"
    echo
    printf '%s\n' "[Three views below:]"
    printf '%s\n' "[  1. Default summary - histograms + top-3-category samples]"
    printf '%s\n' "[  2. Cross-aliases   - SEDs whose key name != their actual wav (sound aliasing)]"
    printf '%s\n' "[  3. Filter spell    - all 2 spell SEDs (zap_cast + nova_strike_cast)]"
    echo
    printf '%s\n' "--- summary ---"
    "$TOOL" audio sed-list "${DS1}/Resources/Sound.dsres"; ERRORLEVEL=$?
    echo
    printf '%s\n' "--- cross-aliases ---"
    "$TOOL" audio sed-list "${DS1}/Resources/Sound.dsres" --show-aliases; ERRORLEVEL=$?
    echo
    printf '%s\n' "--- filter spell ---"
    "$TOOL" audio sed-list "${DS1}/Resources/Sound.dsres" --filter=spell; ERRORLEVEL=$?
    echo
    printf '%s\n' "=== xii exited with ${ERRORLEVEL} ==="
    pause
}
T67() {
    echo
    printf '%s\n' "--- Phase 9-SC-10: Shield render verify (fh_r1 + debug-drop) ---"
    printf '%s\n' "[SIEGEFX_DEBUG_DROP injects a sh_m_g_c_r_s_avg loot pile 1.5u in front of]"
    printf '%s\n' "[spawn so the real pickup -> auto-equip -> LoadAttachedItem path fires]"
    printf '%s\n' "[without hunting for a shield-bearing mob to kill.]"
    printf '%s\n' "[Walk forward, the auto-pickup picks up the shield, the [es_shield_hand]]"
    printf '%s\n' "[equip log fires, and the shield should render on the PC's shield_grip]"
    printf '%s\n' "[bone (left forearm) oriented like the weapon (X 180 deg + grip prerot).]"
    echo
    export SIEGEFX_DEBUG_DROP="shield_hand:sh_m_g_c_r_s_avg"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    unset SIEGEFX_DEBUG_DROP
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T68() {
    echo
    printf '%s\n' "--- Phase 9-SC-16 B-1+B-2: Pcontent dump (tier + wildcards + rarity) ---"
    printf '%s\n' "[B-1: #club/2-3 hits power-2/3 generic clubs only, never the unique]"
    printf '%s\n' "[cb_un_2h_troll_rock. B-2: #armor/-rare(1)/28-80 picks rare-tier armor]"
    printf '%s\n' "[in the right defense band (only *_ra_* templates show up); #*/-unique(2)/175-286]"
    printf '%s\n' "[picks unique cross-class items (ax_un_*, sd_un_*, st_un_*, bd_un_*, etc).]"
    echo
    printf '%s\n' "=== B-1 verify: #club/2-3 ==="
    "$TOOL" pcontent dump "${DS1}/Resources/Logic.dsres" --class=club "--spec=#club/2-3" --rolls=20 --seed=42; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== B-2 verify: #armor/-rare(1)/28-80 ==="
    "$TOOL" pcontent dump "${DS1}/Resources/Logic.dsres" --class=NONE "--spec=#armor/-rare(1)/28-80" --rolls=10 --seed=1; ERRORLEVEL=$?
    echo
    printf '%s\n' "=== B-2 verify: #*/-unique(2)/175-286 ==="
    "$TOOL" pcontent dump "${DS1}/Resources/Logic.dsres" --class=NONE "--spec=#*/-unique(2)/175-286" --rolls=10 --seed=2; ERRORLEVEL=$?
    echo
    printf '%s\n' "=== pcontent dump exited with code ${EXITCODE} (last invocation: ${ERRORLEVEL}) ==="
    pause
}
T69() {
    echo
    printf '%s\n' "--- Phase 10-SC-1/b/c: trigger matrix parser + dispatcher ---"
    printf '%s\n' "[fh_r1 expect: 64 placements bear [instance_triggers]; entered/left_trigger_group]"
    printf '%s\n' "[warm via the synthetic trip-tick (entered=4, left=4) — proves SC-1b occupants pass.]"
    printf '%s\n' "[cr_r1 expect: 92 placements, 23 when_false actions deferred to falling edge;]"
    printf '%s\n' "[trip-tick at the @-0.04,-1.2,-0.04 placement reports when_false>0 — proves SC-1c.]"
    echo
    printf '%s\n' "--- fh_r1 (SC-1b: occupants/entered/left) ---"
    "$TOOL" region triggers "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Logic.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    echo
    printf '%s\n' "--- cr_r1 (SC-1c: when_false falling-edge dispatch) ---"
    "$TOOL" region triggers "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Logic.dsres" /world/maps/map_world/regions/cr_r1; ERRORLEVEL=$?
    pause
}
T70() {
    echo
    printf '%s\n' "--- Phase 10-SC-2: full chore dictionary into Actor.Clips ---"
    printf '%s\n' "[fh_r1 expect: 181/181 spawned; clip catalogue avg ~6 per actor (was 2 pre-SC-2);]"
    printf '%s\n' "[chore coverage line lists chore_default / chore_walk / chore_attack / chore_die /]"
    printf '%s\n' "[chore_fidget / chore_magic / chore_misc — each addressable via Actor.GetClipIndex.]"
    printf '%s\n' "[Post-SC-3 expect min=3 (every actor has at least default + fidget + walk).]"
    echo
    "$TOOL" region spawn "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    pause
}
T71() {
    echo
    printf '%s\n' "--- Phase 10-SC-3: PRS v0x202 + v0x302 animation loader ---"
    printf '%s\n' "[Objects.dsres prs fuzz: parse every shipped .prs and tally by version stamp.]"
    printf '%s\n' "[Expected: 1962 / 1962 OK, 0 failures, 131 with TRCR (separate gap), 0 legacy-skip.]"
    printf '%s\n' "[versions (ok): 0x3=1724, 0x202=62, 0x302=45 — full coverage of shipped DS1.]"
    echo
    "$TOOL" prs fuzz "${DS1}/Resources/Objects.dsres"; ERRORLEVEL=$?
    pause
}
T72() {
    echo
    printf '%s\n' "--- Phase 11-SC-7: land-water seam stitching ---"
    printf '%s\n' "[fh_r1 nav: expect \"Water seams: 37/1435 ... (37 stitched cross-kind pair(s))\"]"
    printf '%s\n' "[Pre-SC-7 was 0/1435 — Floor and Water lived on disconnected geometric components.]"
    printf '%s\n' "[World path-fuzz: expect \"Total land-water: ~4,907 cross-kind seam(s)\" across 81]"
    printf '%s\n' "[regions; biggest-component A* should still be ~99-100% (now Floor-only-restricted).]"
    printf '%s\n' "[Amphibious route: 30,0,30 (Floor) -- 27.57,-1.5,0.70 (Water) crosses a stitched seam]"
    printf '%s\n' "[with --water=4. Default LandOnly traversal still refuses water endpoints.]"
    echo
    "$TOOL" region nav "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    echo
    "$TOOL" region path "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" /world/maps/map_world/regions/fh_r1 30,0,30 27.57,-1.5,0.70 --water=4; ERRORLEVEL=$?
    echo
    "$TOOL" region path-fuzz "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres"; ERRORLEVEL=$?
    pause
}
T73() {
    echo
    printf '%s\n' "--- Phase 12-SC-3: mob loot drops vs DS1 retail ---"
    printf '%s\n' "[Pre-SC-3 the equipped weapon (es_weapon_hand) dropped 100% of kills because]"
    printf '%s\n' "[LootRoller folded Equipped buckets into the drop pile. Post-SC-3 only il_main]"
    printf '%s\n' "[entries roll: krug_grunt drops at ~18% (15% chance gate), krug_scout at ~12%,]"
    printf '%s\n' "[gremal at 0% (no [inventory][pcontent] in chain). Worn weapon stays on body.]"
    echo
    "$TOOL" loot dump "${DS1}/Resources/Logic.dsres" krug_grunt --rolls=200 --seed=42; ERRORLEVEL=$?
    echo
    "$TOOL" loot dump "${DS1}/Resources/Logic.dsres" krug_scout --rolls=200 --seed=42; ERRORLEVEL=$?
    echo
    "$TOOL" loot dump "${DS1}/Resources/Logic.dsres" gremal --rolls=200 --seed=42; ERRORLEVEL=$?
    pause
}
T74() {
    echo
    printf '%s\n' "--- Phase 12-SC-4 + SC-5: Death pose + weapon-class attack chore (VISUAL) ---"
    printf '%s\n' "[SC-4: kill a krug -- the body should fall and HOLD its final chore_die frame]"
    printf '%s\n' "[instead of T-posing or vanishing on the idle. fh_r1 receipt: 179/179 mobs ship]"
    printf '%s\n' "[chore_die. Quicksave (F5) and quickload (F9) -- corpse should still be down.]"
    echo
    printf '%s\n' "[SC-5: pick up the dagger and attack a krug. Player should swing the dagger]"
    printf '%s\n' "[(stance 1, 1H melee), NOT punch with the empty hand. Pre-fix only chore_default]"
    printf '%s\n' "[+ chore_walk swapped on equip; chore_attack stayed at the unarmed stance.]"
    printf '%s\n' "[SC-5 walks the whole chore_dictionary on RefreshMotionClips so attack/magic/]"
    printf '%s\n' "[die/get_hit/fidget all rebind to the equipped weapon's stance.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    echo
    pause
}
T75() {
    echo
    printf '%s\n' "--- Phase 12-SC-6: PRS TRCR resync ---"
    printf '%s\n' "[Objects.dsres prs fuzz: every shipped .prs (incl. tracer-bearing files) parses.]"
    printf '%s\n' "[Pre-fix the loader threw NotSupportedException on TRCR, so 131 files (incl. fb]"
    printf '%s\n' "[stance-1 attack a_c_gah_fb_fs1_at.prs) bailed and the player swung the unarmed]"
    printf '%s\n' "[chore on dagger equip. Post-fix: scan-forward resync to next valid chunk via]"
    printf '%s\n' "[4-byte-aligned tag scan with chunk-version + KLST bone-index plausibility check.]"
    printf '%s\n' "[Expected: 1962 / 1962 OK, 0 failures, 131 with tracers, 0 legacy-skip.]"
    printf '%s\n' "[Versions OK: 0x3=1855, 0x202=62, 0x302=45 (v3 climbed 1724 -> 1855).]"
    echo
    "$TOOL" prs fuzz "${DS1}/Resources/Objects.dsres"; ERRORLEVEL=$?
    echo
    pause
}
T76() {
    echo
    printf '%s\n' "--- Phase 17-SC-A1: SpellExpr ** power operator ---"
    printf '%s\n' "[Survey: shows 19 spells use ** (fireball, iceshard, acid_cloud, etc.) and lists]"
    printf '%s\n' "[the placeholder set (#magic + #maxlife/#life/#src_mana/#src_life — last four are]"
    printf '%s\n' "[SC-A2 territory; ternary [[?:]] in healing_hands is SC-A3).]"
    printf '%s\n' "[Show fireball: pre-fix every level rolled dmg=0 because ParseMulDiv ate one star]"
    printf '%s\n' "[of the ** pair. Post-fix L1 [3.85..4.67], L100 [376..399], scaling by ~96x.]"
    printf '%s\n' "[Show iceshard: pre-fix L1 [1.66..2.54] flat. Post-fix L1 [3.54..5.42] -> L100 [284..361].]"
    echo
    "$TOOL" spells survey "${DS1}/Resources/Logic.dsres"; ERRORLEVEL=$?
    echo
    "$TOOL" spells show "${DS1}/Resources/Logic.dsres" spell_fireball; ERRORLEVEL=$?
    echo
    "$TOOL" spells show "${DS1}/Resources/Logic.dsres" spell_iceshard; ERRORLEVEL=$?
    echo
    pause
}
T77() {
    echo
    printf '%s\n' "--- Phase 17-SC-A2/A3: SpellExpr placeholders + ternary ---"
    printf '%s\n' "[SC-A2 plumbs #maxlife / #life / #src_mana / #src_life through SpellEvalContext.]"
    printf '%s\n' "[Receipt: spell_freeze with #maxlife=20 -> mana=50.0 (was 0.0 pre-A2).]"
    printf '%s\n' "[SC-A3 adds [[?:]] ternary + comparison ops (< > <= >= == !=) so leech_life]"
    printf '%s\n' "[drain clamps and healing_hands' nested heal-vs-mana ternary parse cleanly.]"
    printf '%s\n' "[Receipts: leech_life formula evaluates to 0.7 / 0.3 across two src_life cases;]"
    printf '%s\n' "[healing_hands triple-ternary returns 11 / -0.61 / 4 across three context cases.]"
    echo
    printf '%s\n' "-- spell_freeze (#maxlife=20) --"
    "$TOOL" spells show "${DS1}/Resources/Logic.dsres" spell_freeze --maxlife=20 5; ERRORLEVEL=$?
    echo
    printf '%s\n' "-- ternary smoke tests --"
    "$TOOL" spells eval "(2 > 1) ? 5 : 10"; ERRORLEVEL=$?
    "$TOOL" spells eval "(1 > 2) ? 5 : 10"; ERRORLEVEL=$?
    "$TOOL" spells eval "[[ ( #magic > 5 ) ?( 100 ): ( 200 ) ]]" --magic=10; ERRORLEVEL=$?
    "$TOOL" spells eval "[[ ( #magic > 5 ) ?( 100 ): ( 200 ) ]]" --magic=2; ERRORLEVEL=$?
    echo
    printf '%s\n' "-- spell_leech_life clamp formula --"
    "$TOOL" spells eval "( ( #src_life > (2.0 + #magic ) ) ? (2 + #magic ) : ( ( #src_life > 0.0 ) ? #src_life : 0.0 ) )/10.0" --magic=5 --src_life=20; ERRORLEVEL=$?
    "$TOOL" spells eval "( ( #src_life > (2.0 + #magic ) ) ? (2 + #magic ) : ( ( #src_life > 0.0 ) ? #src_life : 0.0 ) )/10.0" --magic=5 --src_life=3; ERRORLEVEL=$?
    echo
    pause
}
T78() {
    echo
    printf '%s\n' "--- Phase 17-SC-B: per-element spell projectile/impact VFX ---"
    printf '%s\n' "[SpellTemplate now exposes a SpellElement (Fire/Ice/Lightning/Acid/Death/Holy/]"
    printf '%s\n' "[Generic) classified by template name. RenderHost reads it to tint the bolt +]"
    printf '%s\n' "[impact flash for every cast — fireballs read orange, iceshards cyan, zaps blue.]"
    printf '%s\n' "[Receipt: catalog of 69 offensive instant-hit spells groups cleanly across the]"
    printf '%s\n' "[seven buckets; only 7 land in Generic (kill / killing_fist / leech_life /]"
    printf '%s\n' "[metal_shards / nurture / reconstitution / tremor — no obvious element cue).]"
    echo
    "$TOOL" spells elements "${DS1}/Resources/Logic.dsres"; ERRORLEVEL=$?
    echo
    pause
}
T79() {
    echo
    printf '%s\n' "--- Phase 17-SC-C: chore_magic plays on every cast (incl. moving casts) ---"
    printf '%s\n' "[The cast site already pinned chore_magic via PlayChoreOnce, but the actor]"
    printf '%s\n' "[draw loop unconditionally swapped to chore_walk while the player was moving]"
    printf '%s\n' "[— masking the cast clip whenever the click-to-cast happened mid-stride.]"
    printf '%s\n' "[Fix: ActorHostBridge.IsOverrideActive gates the walk swap; pinned chores]"
    printf '%s\n' "[(magic / attack / die) now ride through the full override duration.]"
    printf '%s\n' "[Receipt: the farmboy template's chore dictionary ships chore_magic, so the]"
    printf '%s\n' "[override has a real clip to land on. (Visual confirmation: cast spell_zap]"
    printf '%s\n' "[while click-to-moving — the cast pose now reads instead of the walk cycle.)]"
    echo
    "$TOOL" templates show "${DS1}/Resources/Logic.dsres" farmboy | grep -E -e "chore_"; ERRORLEVEL=$?
    echo
    pause
}
T80() {
    echo
    printf '%s\n' "--- Phase 17-SC-D: SfxScriptStore inventory + sample bodies ---"
    printf '%s\n' "[DS1's particle / spell-VFX system is driven by [effect_script*] blocks under]"
    printf '%s\n' "[/world/global/effects/. Each block names a script (fireball, smoke_emitter,]"
    printf '%s\n' "[waterfall_froth, ...) and stores a stack-based DSL body in script=[[ ... ]];.]"
    printf '%s\n' "[SC-D parses the lot and exposes them via 'siegefx sfx list / show'.]"
    printf '%s\n' "[Receipt: 1074 scripts across 14 gas files; 'fireball' resolves to offensive.gas,]"
    printf '%s\n' "[smoke_emitter to environmental.gas, waterfall_froth to environmental.gas.]"
    echo
    printf '%s\n' "=== sfx list (top of the catalog) ==="
    "$TOOL" sfx list "${DS1}/Resources/Logic.dsres" --prefix=fireball; ERRORLEVEL=$?
    echo
    printf '%s\n' "=== sfx show smoke_emitter ==="
    "$TOOL" sfx show "${DS1}/Resources/Logic.dsres" smoke_emitter; ERRORLEVEL=$?
    echo
    pause
}
T81() {
    echo
    printf '%s\n' "--- Phase 17-SC-E: billboard particle backend ---"
    printf '%s\n' "[Loads the farmhouse region (fh_r1) the same way option 50 does, then exposes]"
    printf '%s\n' "[particle backend hotkeys for the visual receipt:]"
    printf '%s\n' "  F11 - spawns a burst of fire + smoke + sparks at the player's feet"
    printf '%s\n' "  F10 - fires a downward lightning bolt onto the player position"
    printf '%s\n' "[Atlas pulled from Objects.dsres at LoadPlayActors:]"
    printf '%s\n' "  slot 0 = b_sfx_fireball-01.raw, slot 1 = b_sfx_smoke.raw,"
    printf '%s\n' "  slot 2 = b_sfx_sparkle01.raw,   slot 3 = b_sfx_002.raw."
    printf '%s\n' "[Receipt: a smoke column, a fire plume, and bright spark scatter in-window.]"
    printf '%s\n' "[SC-F (script interpreter) and SC-G (region emitter wiring) drive this from data.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    echo
    pause
}
T82() {
    echo
    printf '%s\n' "--- Phase 17-SC-F-1: sfx_script compiler (text -> SfxProgram IR) ---"
    printf '%s\n' "[SfxScriptCompiler.Compile reads each [effect_script*] body, strips the [[ ]]]"
    printf '%s\n' "[literal markers + // and /* */ comments, tokenizes the DSL, then folds verbs]"
    printf '%s\n' "[into typed StatementKind entries: SfxCreate / SfxStart / Set / SoundPlay /]"
    printf '%s\n' "[Pause / Call / etc. Conditionals (if/else), waitfor, get, worldmsg surface as]"
    printf '%s\n' "[Raw statements so the future VM can log + skip rather than crash on shapes the]"
    printf '%s\n' "[interpreter doesn't yet handle.]"
    echo
    printf '%s\n' "=== fireball_emitter (rich script: 34 statements across 11 kinds) ==="
    "$TOOL" sfx parse "${DS1}/Resources/Logic.dsres" fireball_emitter; ERRORLEVEL=$?
    echo
    printf '%s\n' "=== smoke_emitter (minimal 2-statement emitter pattern) ==="
    "$TOOL" sfx parse "${DS1}/Resources/Logic.dsres" smoke_emitter; ERRORLEVEL=$?
    echo
    pause
}
T83() {
    echo
    printf '%s\n' "--- Phase 17-SC-F-2: sfx_script VM receipt (TallySink, headless) ---"
    printf '%s\n' "[SfxRuntime executes the compiled IR against an IParticleSink. The VM lives in]"
    printf '%s\n' "[SiegeFX.Core (no GL dep) so this CLI can drive it from a counting stub. Two]"
    printf '%s\n' "[scripts ticked for 3 simulated seconds (60 ticks @ 1/20s):]"
    echo
    printf '%s\n' "  smoke_emitter -> 1 persistent emitter, 60 Maintain calls, ~360 smoke spawns]"
    printf '%s\n' "  fire_emitter  -> 1 persistent emitter, 60 Maintain calls, ~54 fire spawns]"
    echo
    printf '%s\n' "[Same VM (SiegeFX.Core.Sfx.SfxRuntime) drives the live ParticleSystem at runtime.]"
    echo
    printf '%s\n' "=== smoke_emitter ==="
    "$TOOL" sfx run "${DS1}/Resources/Logic.dsres" smoke_emitter --ticks=60; ERRORLEVEL=$?
    echo
    printf '%s\n' "=== fire_emitter ==="
    "$TOOL" sfx run "${DS1}/Resources/Logic.dsres" fire_emitter --ticks=60; ERRORLEVEL=$?
    echo
    pause
}
T84() {
    echo
    printf '%s\n' "--- Phase 17-SC-G: region emitters wired into world load ---"
    printf '%s\n' "[LoadPlayActors loads emitter.gas placements per region (alongside special.gas)]"
    printf '%s\n' "[and broadcasts we_entered_world to each trigger instance. The trigger matrix's]"
    printf '%s\n' "[call_sfx_script verb invokes SfxRuntime.Spawn at the placement origin, so DS1's]"
    printf '%s\n' "[smoke / fire emitters in fh_r1 produce live billboard columns above chimneys.]"
    echo
    printf '%s\n' "[VISUAL: launch fh_r1, look at the farmhouse roofs. Smoke columns should rise]"
    printf '%s\n' "[from each emitter placement; fireplaces (if any) get fire+smoke pairs.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    echo
    pause
}
T85() {
    echo
    printf '%s\n' "--- Phase 17-SC-H: spell cast sfx_script binding ---"
    printf '%s\n' "[SpellTemplate.CastSfxScript resolves the per-spell cast effect by walking]"
    printf '%s\n' "[the specializes chain leaf-first looking for either:]"
    echo
    printf '%s\n' "  (1) [common][template_triggers][*] with]"
    printf '%s\n' "      condition* = receive_world_message(\"we_req_cast\")]"
    printf '%s\n' "      action*    = call_sfx_script(\"&lt;name&gt;\");]"
    echo
    printf '%s\n' "  (2) any [spell_*] root block with effect_script = &lt;name&gt;;]"
    echo
    printf '%s\n' "[At cast time RenderHost calls SfxRuntime.Spawn(scriptName, target),]"
    printf '%s\n' "[replacing the legacy SpellBolt dot-trail with the real DS1 fire/smoke/]"
    printf '%s\n' "[lightning effect. Templates that don't bind a script keep the dot trail.]"
    echo
    printf '%s\n' "[Receipt: dump shows the resolved sfx column per spell + a coverage]"
    printf '%s\n' "[summary. Expect ~61/69 offensive spells to resolve a script.]"
    echo
    "$TOOL" spells dump "${DS1}/Resources/Logic.dsres"; ERRORLEVEL=$?
    echo
    pause
}
T86() {
    echo
    printf '%s\n' "--- Phase 17-SC-I: water UV scroll + waterwheel rotation ---"
    printf '%s\n' "[DS1 ships per-texture TSD .gas sidecars with vshiftpersecond + frame counts.]"
    printf '%s\n' "[SC-I-1 recognises the waterfall texture pattern (b_t_*_rvr_fall-*) and applies]"
    printf '%s\n' "[a 0.5/sec V-shift on its sampling UVs; -static textures (mist + the layered]"
    printf '%s\n' "[wheelfallstatic composite) are excluded so they stay still.]"
    echo
    printf '%s\n' "[SC-I-2 detects chore_default = rotateX?rpm=N on placed templates (mill]"
    printf '%s\n' "[waterwheel = rotatex?rpm=-8.0) and bakes an angular velocity onto the static]"
    printf '%s\n' "[prop. The draw loop spins the prop's local axis before applying placement,]"
    printf '%s\n' "[so the wheel turns in place while the riverfall texture cascades over it.]"
    echo
    printf '%s\n' "[Receipt: in fh_r1, walk to the mill (north of the farmhouse, river side) —]"
    printf '%s\n' "[the waterfall column should flow downward and the wooden wheel should turn.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    echo
    pause
}
T87() {
    echo
    printf '%s\n' "--- Phase 17-SC-J: per-instance scale_multiplier ---"
    printf '%s\n' "[DS1 lets each placement override aspect.scale_multiplier so the same mesh]"
    printf '%s\n' "[reads with subtle variation across the world. fh_r1 has 1150 placements with]"
    printf '%s\n' "[a non-default scale (most foliage in the 0.9-1.4 range), and the breakable]"
    printf '%s\n' "[farmhouse door (door_grs_farmhouse_breakable, 0x01c00da3) ships with 1.5 so]"
    printf '%s\n' "[the destroyable variant looks visibly larger than the everyday wooden door.]"
    echo
    printf '%s\n' "[Receipt 1 (text): the static-prop load summary now ends with \"N with]"
    printf '%s\n' "[non-default scale_multiplier\" — expect ~1100+ for fh_r1. Receipt 2 (visual):]"
    printf '%s\n' "[in-window, the breakable farmhouse door sits scaled 1.5x; vegetation reads]"
    printf '%s\n' "[with the per-instance jitter DS1 baked instead of a uniform clone-stamp.]"
    echo
    printf '%s\n' "[Note: the original \"burnt door\" reading was wrong — DS1 has no separate]"
    printf '%s\n' "[burnt-door mesh and no fire emitters at the door. The breakable variant uses]"
    printf '%s\n' "[the same m_i_grs_door-farmhouse asp; what made it look distinct was scale.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    echo
    pause
}
T88() {
    echo
    printf '%s\n' "--- Phase 21-SC-SPELL-VFX-AUDIT: visual coverage across every offensive spell ---"
    printf '%s\n' "[Headless audit: walks every offensive SpellTemplate's compiled cast sfx_script]"
    printf '%s\n' "[statically and reports per-spell verdict (COVERED/PARTIAL/UNCOVERED) plus the]"
    printf '%s\n' "[primitive-kind / unhandled-verb / texture roll-ups across the whole catalog.]"
    printf '%s\n' "[Recurses one level into \`call <subscript>\` so composed scripts audit fully.]"
    echo
    printf '%s\n' "[Receipt: 69 offensive spells, 61 with cast_sfx_script (8 have no we_req_cast),]"
    printf '%s\n' "[9 fully COVERED, 47 PARTIAL (use orbiter/trackball/cylinder/lightsource/etc.),]"
    printf '%s\n' "[5 UNCOVERED (DS1 author left them sound-only — see iceblast_launch.gas TODO).]"
    printf '%s\n' "[Top primitive misses: orbiter (20 spells), trackball (18), cylinder (12),]"
    printf '%s\n' "[lightsource (9), flurry (7), fireb (5), sray (5), curve (4). 18 distinct b_sfx_*]"
    printf '%s\n' "[textures referenced; b_sfx_sparkle01 is the most common (26 spells).]"
    echo
    "$TOOL" spells visual-audit "${DS1}/Resources/Logic.dsres"; ERRORLEVEL=$?
    echo
    printf '%s\n' "[--verbose for the per-spell breakdown; --filter=NAME to narrow; --only-uncovered]"
    printf '%s\n' "[to see only the PARTIAL+UNCOVERED rows.]"
    echo
    pause
}
T89() {
    echo
    printf '%s\n' "--- Phase 21-SC-SPELL-VFX-AUDIT: visual verify fireball + iceshard ---"
    printf '%s\n' "[Sets SIEGEFX_DEBUG_SPELLS=spell_fireball,spell_iceshard so the player spawns]"
    printf '%s\n' "[with those slotted instead of the default zap+healing_wind. Press Q to cast]"
    printf '%s\n' "[primary (fireball) and W to cast secondary (iceshard) at fh_r1's krug.]"
    echo
    printf '%s\n' "[Post-slice-G: fireball is now COVERED. Q-cast should fly a tracking projectile]"
    printf '%s\n' "[from the caster's hand to the target with a fire trail, then on collision]"
    printf '%s\n' "[(slice G's waitfor + impact gating) fire two cylinder ground rings + an explosion]"
    printf '%s\n' "[burst at the impact point. Pre-G these bursts fired at cast time; post-G they]"
    printf '%s\n' "[wait for the trackball to arrive.]"
    echo
    printf '%s\n' "[Iceshard stays UNCOVERED -- DS1 author stub (ice_shard_launch.gas is sound-only).]"
    printf '%s\n' "[Our SpawnSpellVisual placeholder still over-delivers vs shipped DS1: cyan]"
    printf '%s\n' "[projectile + impact burst.]"
    echo
    printf '%s\n' "[If the visuals don't match these verdicts that's a bug; treat as a finding.]"
    echo
    export SIEGEFX_DEBUG_SPELLS="spell_fireball,spell_iceshard"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    unset SIEGEFX_DEBUG_SPELLS
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T90() {
    echo
    printf '%s\n' "--- Phase 21-SC-SCROLL: full scroll-UI test (16-spell roster) ---"
    printf '%s\n' "[Launches fh_r1 with a 16-spell SIEGEFX_DEBUG_SPELLS roster:]"
    printf '%s\n' "[  Q+W actives: fireball + iceshard]"
    printf '%s\n' "[  Spellbook placed (10 rows): lightning, shock_wave, nurture, bombard,]"
    printf '%s\n' "[    acid_cloud, death_blast, spark, fire_pillar, implosion, starburst]"
    printf '%s\n' "[  Ground pile (~2u in front of player): zap, frigid_armor, heal_bind, leech_life]"
    echo
    printf '%s\n' "[What to test:]"
    printf '%s\n' "[  - Ground pile glitters with element-tinted \"pixie dust\" until pickup]"
    printf '%s\n' "[    (warm orange for combat magic, green for nature/holy/acid, cyan for ice,]"
    printf '%s\n' "[     purple for death). Walk away, glitter follows the pile. Walk over]"
    printf '%s\n' "[     pile -> auto-routes scrolls to spellbook Placed[]; once Placed full,]"
    printf '%s\n' "[     extras land in the inventory grid for drag-from testing.]"
    printf '%s\n' "[  - Open spellbook with B, click an active or placed slot to pick up]"
    printf '%s\n' "[    onto cursor. Click another slot to drop/swap. Self-drop = restore.]"
    printf '%s\n' "[    ESC or RMB cancels and restores to source.]"
    printf '%s\n' "[  - Open inventory with I. Scroll items render with DS1 b_gui_ig_i_ic_sp_*_inv]"
    printf '%s\n' "[    art. Click a scroll to pick onto cursor; drop on spellbook slot.]"
    printf '%s\n' "[  - With cursor scroll, click outside any UI = world drop with the]"
    printf '%s\n' "[    Phase 9-SC-9 throw arc + new X-axis tumble. Walk over to retrieve.]"
    printf '%s\n' "[  - F5 quicksave / F9 quickload: layout round-trips through schema v6.]"
    printf '%s\n' "[  - Verify fireball regression fix: cast Q -> tracking projectile flies]"
    printf '%s\n' "[    caster -> target with fire trail (was rendering nothing before]"
    printf '%s\n' "[    commit 6d3a58c).]"
    echo
    export SIEGEFX_DEBUG_SPELLS="fireball,iceshard,lightning,shock_wave,nurture,bombard,acid_cloud,death_blast,spark,fire_pillar,implosion,starburst,zap,frigid_armor,heal_bind,leech_life"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    unset SIEGEFX_DEBUG_SPELLS
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T91() {
    echo
    printf '%s\n' "--- Phase 21-SC-SPELL-VISUAL: primitive sweep (slices A-H + sphere) ---"
    printf '%s\n' "[10-spell roster, one spell per shipped primitive kind. Each highlights a]"
    printf '%s\n' "[different slice's deliverable so visual regressions surface fast.]"
    echo
    printf '%s\n' "[Roster:]"
    printf '%s\n' "[  Q  fireball       trackball + fire + lightsource glow + waitfor + cylinder impact]"
    printf '%s\n' "[  W  apprentice_zap straight-bolt lightning]"
    printf '%s\n' "[  Placed slot 1 dragon_fire    fireb directional cone (slice C)]"
    printf '%s\n' "[  Placed slot 2 death_blast    cylinder ground ring + sray radial fan (A + B)]"
    printf '%s\n' "[  Placed slot 3 spark          lightsource Glow halo, color-preserving (slice D)]"
    printf '%s\n' "[  Placed slot 4 firebomb       sphere shells -- omni shell, color-preserving (H + sphere)]"
    printf '%s\n' "[  Placed slot 5 bombard        orbiter + lightsource + sphere + flurry (multi-slice)]"
    printf '%s\n' "[  Placed slot 6 starburst      orbiter + sray]"
    printf '%s\n' "[  Placed slot 7 fire_pillar    cylinder column + fire emitter]"
    printf '%s\n' "[  Placed slot 8 healing_wind   curve + sparkles (motion-handle slice + sparkles)]"
    echo
    printf '%s\n' "[What to look for, organized by slice:]"
    echo
    printf '%s\n' "[  --- Slice A (cylinder ground ring) ---]"
    printf '%s\n' "[  fireball impact: TWO concentric cylinder rings appear at the landing point]"
    printf '%s\n' "[  AFTER the trackball arrives (slice G's waitfor gating). fire_pillar: a]"
    printf '%s\n' "[  textured cylinder column rises at the cast target. Both should look like]"
    printf '%s\n' "[  textured rings/columns on the ground, NOT a thin lightning beam (that was]"
    printf '%s\n' "[  the pre-A placeholder).]"
    echo
    printf '%s\n' "[  --- Slice B (sray streak) ---]"
    printf '%s\n' "[  death_blast: tapered radial streaks fan out from the impact point. starburst:]"
    printf '%s\n' "[  same effect emanating from the orbiting projectile. Should read as long]"
    printf '%s\n' "[  thin tapered rays, NOT a dense spark cloud.]"
    echo
    printf '%s\n' "[  --- Slice C (fireb cone) ---]"
    printf '%s\n' "[  dragon_fire: forward-emitting fire cone in the caster's facing direction.]"
    printf '%s\n' "[  The cone should spread laterally and have noticeable forward velocity, NOT]"
    printf '%s\n' "[  be a static fire column.]"
    echo
    printf '%s\n' "[  --- Slice D (lightsource Glow halo) ---]"
    printf '%s\n' "[  spark: a bright additive halo cluster pulses at the spell origin. Color]"
    printf '%s\n' "[  should match the spell's authored tint -- NOT drift to orange/brown over]"
    printf '%s\n' "[  time (that was the pre-D Steam-as-lightsource bug). Bombard's lightsource]"
    printf '%s\n' "[  should follow the orbiting projectile.]"
    echo
    printf '%s\n' "[  --- Slice E (sfx attach / rat / offset_bone / direction) ---]"
    printf '%s\n' "[  fireball's three layered fire emitters should look DIFFERENT from each other]"
    printf '%s\n' "[  (slice E's sfx-rat random rotation), not stacked plumes pointing the same]"
    printf '%s\n' "[  way. The fire trail should follow the trackball through its flight path,]"
    printf '%s\n' "[  not stay anchored at #SOURCE.]"
    echo
    printf '%s\n' "[  --- Slice F (per-bone resolution) ---]"
    printf '%s\n' "[  fireball, dragon_fire, spark all spawn from the caster's HAND BONE area]"
    printf '%s\n' "[  (weapon_bone -> weapon_grip), not from feet. Watch the cast origin: it]"
    printf '%s\n' "[  should track the hand as the player moves.]"
    echo
    printf '%s\n' "[  --- Slice G (waitfor + collision gate) ---]"
    printf '%s\n' "[  fireball: the impact burst (ring + explosion + sparks) fires WHEN THE]"
    printf '%s\n' "[  TRACKBALL ARRIVES, not at cast time. Pre-G these all fired at cast and]"
    printf '%s\n' "[  the projectile flew through them. bombard same: impact burst follows the]"
    printf '%s\n' "[  orbital flight path's terminus.]"
    echo
    printf '%s\n' "[  --- Slice H + sphere ---]"
    printf '%s\n' "[  firebomb: TWO omni-directional expanding particle shells appear at the]"
    printf '%s\n' "[  impact point. Color should match the authored orange (1, .5, .1) and stay]"
    printf '%s\n' "[  warm through the lifetime, not drift to brown. Shell should be a 3D]"
    printf '%s\n' "[  spherical burst -- particles in ALL directions, not just upward (pre-fold]"
    printf '%s\n' "[  this used the warm-biased SpawnSpark). bombard sphere similar.]"
    echo
    printf '%s\n' "[Things that CAN'T be tested headlessly (the test list above + your eyes):]"
    printf '%s\n' "[  - Color preservation across full lifetime (60+ frames)]"
    printf '%s\n' "[  - Spatial accuracy of bone-anchored emitters as the caster moves]"
    printf '%s\n' "[  - Timing of waitfor's resume vs trackball arrival visual match]"
    printf '%s\n' "[  - Sphere's omni-directionality vs Y-fountain bias]"
    echo
    printf '%s\n' "[Audit CLI receipt before launch (should print 240/0/10):]"
    "$TOOL" spells visual-audit "${DS1}/Resources/Logic.dsres" 2>&1 | grep -F -e "COVERED" -e "PARTIAL" -e "UNCOVERED" -e "MISS"; ERRORLEVEL=$?
    echo
    export SIEGEFX_DEBUG_SPELLS="fireball,apprentice_zap,dragon_fire,death_blast,spark,firebomb,bombard,starburst,fire_pillar,healing_wind"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    unset SIEGEFX_DEBUG_SPELLS
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T63() {
    echo
    printf '%s\n' "--- Phase 21d-2a-viii-c: hero name + variant persistence ---"
    printf '%s\n' "[Same launch path as 62, but verifies the save schema bump (v4 -> v5).]"
    printf '%s\n' "[In-window: type a hero name (e.g. \"TestHero\"), pick a non-default body/skin,]"
    printf '%s\n' "[Begin to spawn. Hero name banner sits top-center over the 3D scene.]"
    printf '%s\n' "[Press F5 to quicksave, F9 to reload — name banner + variant persist;]"
    printf '%s\n' "[the v5 quicksave.save under ${LOCALAPPDATA}/SiegeFX/Saves carries HeroName +]"
    printf '%s\n' "[Variant{Gender,BodyTypeIdx,SkinSuffix,PantsSuffix}. v4-and-earlier saves]"
    printf '%s\n' "[load with empty name + null variant (template defaults), no schema break.]"
    echo
    export SIEGEFX_CREATOR="1"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    unset SIEGEFX_CREATOR
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T62() {
    echo
    printf '%s\n' "--- Phase 21d-2a-viii-b: character creator UI panel ---"
    printf '%s\n' "[Reads the gas-authored layout from /ui/interfaces/frontend/character_select/]"
    printf '%s\n' "[character_select.gas (extracted to _scratch_charsel.gas) so button rects +]"
    printf '%s\n' "[name edit_box + 3D preview viewport land at the original DS1 coordinates.]"
    echo
    printf '%s\n' "[Set SIEGEFX_CREATOR=1 so RenderHost gates TrySpawnPlayer behind the panel.]"
    printf '%s\n' "[In-window: cycle Gender/Body/Skin/Pants with the L/R arrow buttons; click]"
    printf '%s\n' "[the name edit_box and type a hero name (max 14 chars); Begin to spawn,]"
    printf '%s\n' "[Cancel to fall through to env-var defaults.]"
    echo
    export SIEGEFX_CREATOR="1"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    unset SIEGEFX_CREATOR
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T47() {
    echo
    printf '%s\n' "--- Phase 21a-3: Rolling preload (no invisible wall) ---"
    printf '%s\n' "[Builds on 21a-2: ring extends as the player crosses into new regions]"
    printf '%s\n' "[Launch log: \"neighbor preload...\" for the initial fh_r1 ring]"
    printf '%s\n' "[Walk south/east past the boundary — log shows \"region change: ... -> ...\"]"
    printf '%s\n' "[Then \"rolling preload: +N region(s)\" + \"rolling spawn: M actor(s) live\"]"
    printf '%s\n' "[The PC's nav follower is reseated onto the new mesh — clicks route into new terrain]"
    printf '%s\n' "[Already-spawned actors (NPCs + player) keep their world coords across re-anchors]"
    printf '%s\n' "[Walk far enough and the ring keeps extending — no fixed outer wall anymore]"
    printf '%s\n' "[Memory grows monotonically (no eviction in MVP) — fine for ~150-region single sessions]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T92() {
    echo
    printf '%s\n' "--- Phase 21-SC-BARREL: cursor + spell-break + frag debris + loot ---"
    printf '%s\n' "[Sub-slices A1, B, C, D bundled. fh_r1 ships 13 barrel_glb_fh_r1 +]"
    printf '%s\n' "[5 crate_glb_fh_r1 placements with regional pcontent (35% gold@2-8,]"
    printf '%s\n' "[5% potion_mana/health_small, ~62% empty per the [oneof*] read).]"
    printf '%s\n' "[Plus 2 breakable doors (no pcontent, frags only).]"
    echo
    printf '%s\n' "[What to look for:]"
    printf '%s\n' "  1. Cursor states - hover the mouse over:"
    printf '%s\n' "     - empty terrain  -> sword (b_gui_c_pointer.raw, 64x64)"
    printf '%s\n' "     - a goblin       -> red sword (b_gui_c_attack1.raw, 64x64)"
    printf '%s\n' "     - a barrel/crate -> animated hammer (b_gui_c_smash1.flm, 21 frames)"
    printf '%s\n' "     - a loot pile    -> animated hand (b_gui_c_grab1.flm, 30 frames)"
    printf '%s\n' "     - Edward (NPC)   -> talk marker (b_gui_c_talk.raw, 32x32)"
    printf '%s\n' "  2. Melee a barrel: LMB while close. Wood + metal frags fly out, fall,"
    printf '%s\n' "     and settle on the ground. Console logs the drop."
    printf '%s\n' "  3. Spell a barrel: cast Q/W (zap) at a barrel. Same shatter + frag"
    printf '%s\n' "     burst as melee. Spell damage debits mana per the cast."
    printf '%s\n' "  4. Loot drop: ~35% of barrels drop 2-8 gold (auto-credited, \"+N gold\""
    printf '%s\n' "     banner); ~5% drop a potion (lands as a LootPile, click to pickup)."
    printf '%s\n' "     ~60% drop nothing — that's the authored distribution."
    printf '%s\n' "  5. Frag debris: each shatter spawns ~6-12 frag instances (frag_glb_wood_*"
    printf '%s\n' "     + frag_glb_metal_*). They tumble, fall under gravity, settle."
    echo
    printf '%s\n' "[Audit CLI receipts before launch:]"
    "$TOOL" region breakable-audit "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1 --top=8; ERRORLEVEL=$?
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T93() {
    echo
    printf '%s\n' "--- Phase 23-SC-OPTIONS: in-game Options Menu (4 tabs) ---"
    printf '%s\n' "[Slices A-F bundled. Modal dialog over fh_r1, scales by viewport]"
    printf '%s\n' "[height: 968x828 panel at 1080p, 1290x1104 at 1440p ultrawide,]"
    printf '%s\n' "[1935x1656 at 4K. Bitmap font scales at integer steps so the]"
    printf '%s\n' "[12px copperplate stays crisp at every target resolution.]"
    echo
    printf '%s\n' "[How to open:]"
    printf '%s\n' "  F10            - opens / closes the dialog (DS1's [game_options] hotkey)"
    printf '%s\n' "  Esc            - closes as Cancel"
    echo
    printf '%s\n' "[What to look for once it's open:]"
    printf '%s\n' "  1. Tabs at top — Video / Audio / Input / Game. Click a tab to swap"
    printf '%s\n' "     the inner panel content. Active tab gets the inner-panel bg"
    printf '%s\n' "     color (visually attached to the content area)."
    printf '%s\n' "  2. Each row is a label on the left + a control on the right:"
    printf '%s\n' "     - Cycle button (string options): LMB steps forward, RMB steps back"
    printf '%s\n' "     - Slider: click anywhere on the track to jump the thumb;"
    printf '%s\n' "               LMB-drag continues"
    printf '%s\n' "     - Bool toggle: cycle button labeled \"Off\" / \"On\""
    printf '%s\n' "  3. Bottom bar: OK commits, Cancel discards, Defaults resets just"
    printf '%s\n' "     the active tab. NO Apply button (DS1 is OK / Cancel only)."
    echo
    printf '%s\n' "[Per-tab content:]"
    printf '%s\n' "  Video   : Resolution / Shadows / Texture Filtering / Gamma / Object Detail"
    printf '%s\n' "  Audio   : Sound on/off + 5 volume sliders + EAX"
    printf '%s\n' "            - Master / Music / SFX volumes apply LIVE during drag"
    printf '%s\n' "              (drag the master slider down — you should hear it fade)"
    printf '%s\n' "            - Ambient / Voice / EAX persist-only (labels say \"(inactive)\")"
    printf '%s\n' "  Input   : Invert X/Y, Lock X/Y, Edge Tracking, Camera + Mouse Sensitivity"
    printf '%s\n' "            - \"Hotkeys...\" button opens a read-only listing sub-screen"
    printf '%s\n' "              (full rebinding pending splinter SC-OPTIONS-REBIND)"
    printf '%s\n' "  Game    : Two pages (More / Back paging)"
    printf '%s\n' "            - Page 1: Framerate, Priority, Text Scroll, Max Text,"
    printf '%s\n' "              Game Speed, Tutorial Tips, Difficulty"
    printf '%s\n' "            - Page 2: Tooltips, Blood Color, Dismemberment"
    echo
    printf '%s\n' "[Status: PARTIAL. The dialog opens, scales, and persists state within"
    printf '%s\n' "the session, but most knobs are still persist-only. Verified working:]"
    printf '%s\n' "  - Audio Master / Music / SFX volumes (live during drag + on OK)"
    printf '%s\n' "  - Sound on/off (Master goes to 0 when off)"
    printf '%s\n' "  - Defaults click on Audio tab applies live so you hear the reset"
    printf '%s\n' "  - Game tab: Show Framerate (top-right FPS HUD on/off) - eyes confirmed"
    echo
    printf '%s\n' "[Wired but reportedly NOT visibly working yet - needs follow-up:]"
    printf '%s\n' "  - Input tab: Invert Camera X/Y, Camera Sensitivity, Mouse Sensitivity"
    printf '%s\n' "    (ApplyOptionsRuntime pushes to Camera but user reports no effect;"
    printf '%s\n' "     may be that the chase-mode handler still routes around it, or"
    printf '%s\n' "     the values aren't reaching the input dispatch path)"
    printf '%s\n' "  - Game tab: Game Speed (claimed wired but not yet validated)"
    echo
    printf '%s\n' "[Persist-only - will need their own splinter to take runtime effect:]"
    printf '%s\n' "  - Video: resolution, shadows, texture filtering, gamma, object detail"
    printf '%s\n' "           (SC-OPTIONS-VIDEO-RUNTIME)"
    printf '%s\n' "  - Input: lock-camera-x/y, screen edge tracking"
    printf '%s\n' "           (SC-OPTIONS-INPUT-RUNTIME)"
    printf '%s\n' "  - Game:  tooltips (no tooltip system shipped yet - SC-TOOLTIP),"
    printf '%s\n' "           blood color, dismemberment, priority, text scroll, max text,"
    printf '%s\n' "           tutorial tips, difficulty (SC-OPTIONS-PERSIST + per-knob splinters)"
    printf '%s\n' "  The menu remembers all of these within the session but resets on relaunch."
    echo
    printf '%s\n' "[SC-OPTIONS-FOLD2 visible fixes that DID land:]"
    printf '%s\n' "  - Tab labels no longer covered by inner panel (panel Y dropped 80 to 86,"
    printf '%s\n' "    tab font centering now factors in _fontScale)"
    printf '%s\n' "  - Defaults button no longer overlaps Hotkeys (Input) or More (Game) -"
    printf '%s\n' "    RowStride dropped 30 to 24 so 8 rows fit above the Defaults band"
    printf '%s\n' "  - Hotkeys sub-screen layout now scales with _fontScale at 4K"
    printf '%s\n' "  - mood_change trigger flood log dedupes by name (was 100s/frame in fh_r1)"
    echo
    printf '%s\n' "[Reach the menu the cheap way:]"
    printf '%s\n' "  1. Game launches into fh_r1 (skip creator with SIEGEFX_CREATOR=0 — see below)"
    printf '%s\n' "  2. Press F10 once you can move"
    printf '%s\n' "  3. Try every tab + drag the Master Volume slider"
    printf '%s\n' "  4. Hit OK or Cancel to close"
    echo
    export SIEGEFX_CREATOR="0"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    unset SIEGEFX_CREATOR
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T94() {
    echo
    printf '%s\n' "--- Phase 24-MAINMENU: boot to main menu ---"
    printf '%s\n' "[Default no-args launch. Resolves DS1 install via SIEGEFX_DS1 env var or]"
    printf '%s\n' "[the GOG / Steam / retail-DVD common paths, then runs the splash sequence:]"
    echo
    printf '%s\n' "  1. Microsoft splash (intro_microsoft.gas, 3-panel RAW alpha-anim)"
    printf '%s\n' "  2. GPG splash (intro_gaspowered.gas, same)"
    printf '%s\n' "  3. Bink-stub fade (1s placeholder for SC-MAINMENU-BINK)"
    printf '%s\n' "  4. \"Dungeon Siege\" sword drop on logo.asp via logo-enter.prs (2.17s)"
    printf '%s\n' "  5. Main menu - 7 buttons (Single Player / Multiplayer / Options / Continue"
    printf '%s\n' "                           / About / Exit / Credits)"
    echo
    printf '%s\n' "[Working actions: Single Player (opens SP submenu via mm2sp transition),"
    printf '%s\n' " Options (F10 dialog), About (overlay), Exit (close)]"
    printf '%s\n' "[Stubs (log \"splinter SC-MAINMENU-X pending\" on click): Continue,"
    printf '%s\n' " Multiplayer, Credits — region launch + sub-screens deferred]"
    echo
    printf '%s\n' "[Phase 27-SP-FLYOUT: Single Player click animates the panel + button"
    printf '%s\n' " column (mainmenu_mm2sp + menubars_mm2sp PRS) to a 2-button SP screen"
    printf '%s\n' " (Start New Game / Load Game) with EXIT replaced by BACK. Sword cursor"
    printf '%s\n' " shows in all menu states. Hover overlays on every button. Back unwinds"
    printf '%s\n' " via sp2mm clips. New Game / Load Game still log SC-MAINMENU-NEWGAME /"
    printf '%s\n' " -LOADGAME pending for now.]"
    echo
    printf '%s\n' "[Esc on splash skips ahead to main menu. Esc on main menu quits.]"
    echo
    printf '%s\n' "[Distributable: run publish-alpha.bat [version] at the repo root:]"
    printf '%s\n' "[ single-file SiegeFX.exe + SiegeSmith.exe + zips under publish]"
    printf '%s\n' "[ (see the script for the underlying dotnet publish flags).]"
    echo
    "$RUN"; ERRORLEVEL=$?
    export EXITCODE="${ERRORLEVEL}"
    echo
    printf '%s\n' "=== SiegeFX exited with code ${EXITCODE} ==="
    show_crash_log
    pause
}
T95() {
    echo
    printf '%s\n' "--- SC-TSD-ANIM: water frame-cycle + waterfall layer-2 ---"
    printf '%s\n' "[DS1 stores per-texture animation in TSD .gas sidecars. Two recipes:]"
    echo
    printf '%s\n' "  1. River surface (b_t_grs01_rvr_water-2a-*.gas): layer1numframes=4,"
    printf '%s\n' "     layer1secondsperframe=0.15, four distinct textures cycled at ~6.7fps,"
    printf '%s\n' "     timesyncanimation=true so all river tiles stay in lockstep."
    printf '%s\n' "  2. Waterfall (b_t_grs01_wheelfallstatic-01.gas): layer 1 = static"
    printf '%s\n' "     painted-on rock, layer 2 = b_t_grs01_rvr_dynamic with"
    printf '%s\n' "     vshiftpersecond=0.5 and colorop=modulate2x. The visible cascade is"
    printf '%s\n' "     the layer-2 dynamic texture scrolling vertically and modulate2x"
    printf '%s\n' "     blended onto the static base."
    echo
    printf '%s\n' "[SC-TSD-ANIM-A reads every TSD .gas at terrain-tank open, indexes by"
    printf '%s\n' " texture name. SC-TSD-ANIM-B extends the mesh fragment shader with a"
    printf '%s\n' " second sampler (uAlbedo2), independent uv offset, and colorop selector.]"
    echo
    printf '%s\n' "[Receipt: walk to the bridge near the farmhouse (NE of spawn). The river"
    printf '%s\n' " surface should ripple at 6-7 frames per second. Walk around the bend to"
    printf '%s\n' " the wheelfall — the cascade should slide downward at ~0.5 unit per second"
    printf '%s\n' " with the modulate2x brightening on top of the static rock.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    echo
    pause
}
T96() {
    echo
    printf '%s\n' "--- SC-QUEST-OBJ-A: talk-to-NPC objective receipt ---"
    printf '%s\n' "[QuestCatalog now ships a stub TALK quest \"quest_seek_gyorn\" with"
    printf '%s\n' " TalkTargetTemplate=edgaar. Until SC-QUEST-OBJ-F wires dialogue-driven"
    printf '%s\n' " activate_quest, an env-var pre-activates the quest at region-load.]"
    echo
    printf '%s\n' "[Receipt: console prints \"[quest:debug] activated quest_seek_gyorn\""
    printf '%s\n' " on region-load. Walk to Edgaar (talkable NPC roster lists his world"
    printf '%s\n' " position), RMB to open dialogue, click through to the close. Console"
    printf '%s\n' " prints \"[quest] talk objective complete: quest_seek_gyorn (spoke to"
    printf '%s\n' " edgaar)\" and the journal entry flips to Completed. Press L to inspect"
    printf '%s\n' " the quest log overlay.]"
    echo
    export SIEGEFX_DEBUG_QUEST="quest_seek_gyorn"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    unset SIEGEFX_DEBUG_QUEST
    echo
    pause
}
T97() {
    echo
    printf '%s\n' "--- SC-QUEST-OBJ-F: 24-quest catalog + chain receipt ---"
    printf '%s\n' "[QuestCatalog now ships all 24 Kingdom of Ehb quests (Chapters I-IX)"
    printf '%s\n' " with NextQuestKey chain pointers between main-quest beats."
    printf '%s\n' " quest_seek_gyorn -> quest_deliver_gyorn_report -> quest_clear_glitterdelve"
    printf '%s\n' " -> quest_report_torg_findings -> quest_for_merik -> ... -> quest_vanquish_seck.]"
    echo
    printf '%s\n' "[Receipt: console prints \"[quest:debug] activated quest_seek_gyorn\""
    printf '%s\n' " on region-load. RMB Edgaar in fh_r1 (Quest_for_Gyorn's TALK target"
    printf '%s\n' " is stubbed to him until Stonebridge streams). Console prints:"
    printf '%s\n' "   [quest] talk objective complete: quest_seek_gyorn (spoke to edgaar)"
    printf '%s\n' "   [quest] follow-up activated: quest_deliver_gyorn_report (from quest_seek_gyorn)"
    printf '%s\n' " Press L to see both entries in the journal overlay (quest_seek_gyorn"
    printf '%s\n' " closed, quest_deliver_gyorn_report active).]"
    echo
    export SIEGEFX_DEBUG_QUEST="quest_seek_gyorn"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    unset SIEGEFX_DEBUG_QUEST
    echo
    pause
}
T98() {
    echo
    printf '%s\n' "--- SC-QUEST-OBJ-C: pickup quest objective ---"
    printf '%s\n' "[QuestCatalog now ships quest_grab_fireshot (PickupTargetTemplate="
    printf '%s\n' " spell_fireshot, PickupCountGoal=1). Pre-activated via env var; walk"
    printf '%s\n' " into the fh_r1 basement, pick up the fireshot scroll, console prints:"
    printf '%s\n' "   [quest] pickup objective complete: quest_grab_fireshot (acquired spell_fireshot)]"
    echo
    export SIEGEFX_DEBUG_QUEST="quest_grab_fireshot"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    unset SIEGEFX_DEBUG_QUEST
    echo
    pause
}
T99() {
    echo
    printf '%s\n' "--- SC-QUEST-OBJ-D: deliver quest objective ---"
    printf '%s\n' "[QuestCatalog's quest_merik_staff is a composite C+D entry:"
    printf '%s\n' " PickupTargetTemplate=merik_staff (auto-completes the pickup leg on grab)"
    printf '%s\n' " AND TalkTargetTemplate=merik + DeliverItemTemplate=merik_staff (the"
    printf '%s\n' " hand-off leg gates on holding the staff at talk time). The staff and"
    printf '%s\n' " Merik are both in Lost Cathedral - fh_r1 alone can't fully exercise"
    printf '%s\n' " the receipt, but launching here proves the activation + journal entry."
    printf '%s\n' " Real receipt needs the player to reach lc_r5 with the staff in hand.]"
    echo
    export SIEGEFX_DEBUG_QUEST="quest_merik_staff"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    unset SIEGEFX_DEBUG_QUEST
    echo
    pause
}
T100() {
    echo
    printf '%s\n' "--- SC-HUD-DATABAR: bottom-row HUD buttons ---"
    printf '%s\n' "[DS1's always-on data_bar at the bottom of the screen. 7 button slots:"
    printf '%s\n' " pause/play (left), HP potion, MP potion, then on the right:"
    printf '%s\n' " labels-toggle, mega-map (stub), quest-log (book), menu (door icon)."
    printf '%s\n' " Click each to verify the right notify fires:"
    printf '%s\n' "   Pause/Play -- toggles world tick (Space key alias)"
    printf '%s\n' "   HP/MP Potion -- drinks lowest-tier potion from inventory"
    printf '%s\n' "   Labels       -- toggles flag (overhead rendering pending SC-HUD-OVERHEAD-BARS)"
    printf '%s\n' "   Mega-Map     -- prints \"splinter SC-HUD-MEGAMAP pending\""
    printf '%s\n' "   Quest Log    -- toggles the L/J overlay"
    printf '%s\n' "   Menu (door)  -- opens the Options dialog (F10 alias)"
    echo
    printf '%s\n' " Quest indicator flash: when a quest activates or objective completes"
    printf '%s\n' " the red book pulses over the journal button for ~1.5s.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    echo
    pause
}
T101() {
    echo
    printf '%s\n' "--- SC-HUD-OVERHEAD-BARS: floating HP/MP above heads ---"
    printf '%s\n' "[DS1-authentic floating HP/MP bars (status_bars.gas). PC always"
    printf '%s\n' " shows; enemies show only when wounded OR in Chase/Attack aggro."
    printf '%s\n' " Texture: b_gui_ig_mnu_status_bars; per-bar uvcoords with V-flip"
    printf '%s\n' " to convert gas's bottom-up convention to screen frame; dynamic_"
    printf '%s\n' " edge=right clips the fill to currentLife/maxLife."
    printf '%s\n' " Receipt: walk to a krug pack, watch each enemy's bar appear when"
    printf '%s\n' " they engage you, and watch their HP drain on hit.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    echo
    pause
}
T102() {
    echo
    printf '%s\n' "--- SC-FADE-NODES-LNODE: dungeon-reveal nav test ---"
    printf '%s\n' "[Spawns the PC near the first farmhouse basement entry in fh_r1"
    printf '%s\n' " (world ~70,-4,-65) so you don't have to walk from the authored"
    printf '%s\n' " spawn. Walk a few steps down/in toward the basement stairs and"
    printf '%s\n' " watch for [fade_nodes] lines in the log — those mark the moment"
    printf '%s\n' " the trigger fires + which (snode,lnode) pairs get hidden. Then"
    printf '%s\n' " click on the basement floor and verify the path actually"
    printf '%s\n' " descends instead of routing across the upper floor."
    printf '%s\n' " Diag: [fade-trig-diag] lines at startup list each trigger's"
    printf '%s\n' " resolved world position; if they cluster near origin (0,0,0)"
    printf '%s\n' " the snode-parent transform isn't resolving for those triggers.]"
    echo
    export SIEGEFX_DEBUG_SPAWN="70,-4,-65"
    export SIEGEFX_DEBUG_LOG_FILE="${TEMP}/siegefx_diag/test102.log"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    unset SIEGEFX_DEBUG_SPAWN
    unset SIEGEFX_DEBUG_LOG_FILE
    printf '%s\n' "Log written to: ${TEMP}/siegefx_diag/test102.log"
    echo
    pause
}
T103() {
    echo
    printf '%s\n' "--- Phase 26: Party recruitment (Stonebridge, bt_r1) ---"
    printf '%s\n' "[RMB a companion (Gyorn stands past the Stonebridge gate). His conversation]"
    printf '%s\n' " opens in the authentic DS1 cpbox dialogue box (top-centre). Gyorn's join]"
    printf '%s\n' " line ends \"...can I come along?\" with Accept / Decline buttons.]"
    printf '%s\n' "[Click Accept — he joins (free; paid companions debit [aspect]gold_value),]"
    printf '%s\n' " his \"well met, friend!\" accept line plays, console logs \"party: recruited\".]"
    printf '%s\n' " Decline shows his polite refusal; the corner X dismisses.]"
    printf '%s\n' "[The recruit drops its wander and TRAILS you in a wedge behind the leader;]"
    printf '%s\n' " walk around and watch it path-follow. Party cap is 8 (7 followers + leader).]"
    printf '%s\n' "[FOLLOWERS FIGHT: a recruit engages the nearest enemy in range with its]"
    printf '%s\n' " starting weapon (Gyorn=mace melee, Naidi=bow ranged, Gloern=axe), swings/]"
    printf '%s\n' " fires, then returns to formation when dead. Kills award party XP + loot.]"
    printf '%s\n' "[PARTY GUI: the field-commands panel (formations / move-attack-target orders /]"
    printf '%s\n' " select-all / disband / follow) sits BOTTOM-RIGHT and is on screen from game]"
    printf '%s\n' " start, solo or not. Once Gyorn joins, his portrait cell appears below yours]"
    printf '%s\n' " (top-left); click it to select, click a formation button to re-shape the wedge.]"
    printf '%s\n' "[Stats read from the gas per companion (Gyorn lvl-2 fighter, Sikra lvl-38 mage).]"
    printf '%s\n' "[ESC MENU: press Esc for DS1's in_game_menu - 5 centred copperplate buttons]"
    printf '%s\n' " RESUME GAME / OPTIONS / SAVE GAME / LOAD GAME / EXIT GAME. Resume closes it,]"
    printf '%s\n' " Options opens the tabbed Options dialog, Save/Load use the quicksave slot.]"
    printf '%s\n' "[Not yet: pure casters (lore-book il_main) follow but don't cast; full equipment]"
    printf '%s\n' " slots + the View/stats popover + pack mule = Phase 27.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/bt_r1; ERRORLEVEL=$?
    echo
    pause
}
T104() {
    echo
    printf '%s\n' "--- SC-WEATHER: mood weather + region sound emitters ---"
    printf '%s\n' "[CLI PART - self-verifying receipts:]"
    printf '%s\n' "  mood weather: 232 map_world moods, 232 fog, 9 rain (1 lightning ="
    printf '%s\n' "  fh_r1_3, the opening storm; a 10th [rain] ships commented out in"
    printf '%s\n' "  bt_r1_2), 18 snow, 42 wind, 46 sun tables. Densities 30-225 / 75-500."
    "$TOOL" mood weather "${DS1}/Resources/Logic.dsres" --map=world; ERRORLEVEL=$?
    echo
    printf '%s\n' "  sound-emitters all: 670 emitters/81 regions, 0 wav misses; the single"
    printf '%s\n' "  event miss is the shipped env_rats_sqeakskitter typo (PASS, exit 0)."
    "$TOOL" region sound-emitters "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Sound.dsres" all; ERRORLEVEL=$?
    if [ "$ERRORLEVEL" -ge 1 ]; then printf '%s\n' "*** SOUND-EMITTER AUDIT FAILED ***"; else printf '%s\n' "sound-emitter audit: PASS"; fi
    echo
    printf '%s\n' "[EYES TEST - fh_r1 opening storm. Walk out from the farmhouse toward the]"
    printf '%s\n' " fields/bridge; crossing the mood boxes fires mood_change fh_r1_2/_3:]"
    printf '%s\n' " - RAIN streaks (200-225/s) lean slightly with the wind; density drifts]"
    printf '%s\n' "   every 15s like retail (mood_manager rates).]"
    printf '%s\n' " - FOG closes to the authored gray 30-50m; the horizon clears to fog color.]"
    printf '%s\n' " - LIGHTNING (fh_r1_3 zone): double-pulse white flash + thunder clap]"
    printf '%s\n' "   0.4-2s later; strikes every 3-10s while rain is at storm density.]"
    printf '%s\n' " - RAIN LOOP audio: the storm trigger we_req_activates the amb_rain_01]"
    printf '%s\n' "   emitters (console: [emitter] 0x01C00B24 activated); leaving the zone]"
    printf '%s\n' "   deactivates them. Wind loops + waterwheel + burning-farmhouse fire]"
    printf '%s\n' "   loop are always-on placed emitters now too.]"
    printf '%s\n' " - Console shows [weather] mood ... applied lines on each zone cross.]"
    printf '%s\n' "[SNOW check (optional): --play-region nt_r1 or ac_r1 = alpine snowfall.]"
    echo
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    echo
    pause
}
T105() {
    echo
    printf '%s\n' "--- SC-ELEVATOR: farmhouse grate lift ride (hc_r1, closest lift to spawn) ---"
    printf '%s\n' "[Same basement house as the stair/cutaway tests - besides the stairs it"
    printf '%s\n' " has a METAL GRATE floor section that is a working lift now. Spawn ="
    printf '%s\n' " top of its shaft (world ~76,-4,-72; hc_r1 streams as fh_r1 neighbor)."
    printf '%s\n' " SC-ELEVATOR is LIVE: 216 lift gizmos across 32 regions parse and"
    printf '%s\n' " door-align (CLI receipt: siegefx region elevators ... all = 0 fails)."
    echo
    printf '%s\n' " EYES TEST - the full ride loop:"
    printf '%s\n' " - Console at load: [elevator] 0x06A00036 ... parked at stop1 and two"
    printf '%s\n' "   [lever] registration lines."
    printf '%s\n' " - Grate platform sits flush at the TOP of the shaft; the wall lever"
    printf '%s\n' "   should read as mounted, not floating (report if still floating)."
    printf '%s\n' " - CLICK the winch lever ITSELF (ray-picked): player walks to the"
    printf '%s\n' "   authored use point ON THE GRATE, then pulls ([lever] pulled line);"
    printf '%s\n' "   grate descends ~12u over 5s WITH you standing on it; the authored"
    printf '%s\n' "   cutaway fades swap surface/basement sections mid-ride."
    printf '%s\n' " - Ordinary move-clicks on the floor NEAR the winch must NOT pull it"
    printf '%s\n' "   (the old bug: elevator left without you + player froze topside)."
    printf '%s\n' " - If you somehow stand over the open shaft when the car is away, a"
    printf '%s\n' "   [nav-rescue] line nudges you to the landing edge instead of freezing."
    printf '%s\n' " - At the bottom: walk OFF onto the cellar floor (nav rebuilds on"
    printf '%s\n' "   arrival - console prints a nav mesh rebuild line)."
    printf '%s\n' " - Pull the basement lever to ride back UP; fades reverse on arrival."
    printf '%s\n' " - Recruits standing on the grate ride along with you.]"
    echo
    export SIEGEFX_DEBUG_SPAWN="76,-4,-72"
    export SIEGEFX_DEBUG_LOG_FILE="${TEMP}/siegefx_diag/test105.log"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/fh_r1; ERRORLEVEL=$?
    unset SIEGEFX_DEBUG_SPAWN
    unset SIEGEFX_DEBUG_LOG_FILE
    printf '%s\n' "Log written to: ${TEMP}/siegefx_diag/test105.log"
    echo
    pause
}
T106() {
    echo
    printf '%s\n' "--- ALPHA-1: campaign completability sweep (CLI, self-verifying) ---"
    printf '%s\n' "[Sweeps ALL 81 regions in stitch-BFS campaign order: every placed"
    printf '%s\n' " template's component sections (instance + specializes chain) diffed"
    printf '%s\n' " against the engine-handled set, plus trigger condition/action verbs"
    printf '%s\n' " diffed against TriggerRuntime's dispatched sets. The UNHANDLED table"
    printf '%s\n' " is the alpha blocker list (the elevator gap surfaced exactly this"
    printf '%s\n' " way); the benign table keeps triaged cosmetics visible. Also runs"
    printf '%s\n' " the per-region nav component histogram (read with care: single-"
    printf '%s\n' " region scope over-reports splits that cross-region stitches join).]"
    echo
    "$TOOL" world campaign-audit "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Logic.dsres"; ERRORLEVEL=$?
    echo
    "$TOOL" region nav-components "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" all --top=5; ERRORLEVEL=$?
    echo
    pause
}
T107() {
    echo
    printf '%s\n' "--- CRYPT STAIRS REPRO: spawn AT the cr_r1 stair descent ---"
    printf '%s\n' "[Spawns the PC directly at the top of the crypt stairs (path2crypts"
    printf '%s\n' " frame 150.1,-4.0,307.5 = the fh-frame spot from the crash logs,"
    printf '%s\n' " mapped through \`world map-point\` because the stitch is rotated)."
    printf '%s\n' " This is the spot where the fade seam was flapping (hide/auto-"
    printf '%s\n' " reverse/hide on every step): half-revealed lower level, crawling"
    printf '%s\n' " descent, control wedge."
    printf '%s\n' " Fixes to verify this run:"
    printf '%s\n' " - No stair click-fight: descend in ONE click from top to bottom."
    printf '%s\n' " - No [fade_nodes] out/in pairs spamming per step in the console"
    printf '%s\n' "   (seam hysteresis now holds a crossed boundary for +0.35u)."
    printf '%s\n' " - Lower level reveals fully on descent; going back up restores the"
    printf '%s\n' "   upper level."
    printf '%s\n' " - No hijacked movement (patrol chains never target the hero now)."
    printf '%s\n' " IF the reveal still breaks: press F7 while it looks wrong - the"
    printf '%s\n' " per-section fade diagnostic prints + writes siegefx_fade_diag.log.]"
    echo
    export SIEGEFX_DEBUG_SPAWN="150.1,-4.0,307.5"
    export SIEGEFX_DEBUG_LOG_FILE="${TEMP}/siegefx_diag/test107.log"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/path2crypts; ERRORLEVEL=$?
    unset SIEGEFX_DEBUG_SPAWN
    unset SIEGEFX_DEBUG_LOG_FILE
    printf '%s\n' "Log written to: ${TEMP}/siegefx_diag/test107.log"
    echo
    pause
}
T108() {
    echo
    printf '%s\n' "--- ALPHA-2: TOWN + DIALOGUE (Stonebridge, bt_r1) ---"
    printf '%s\n' "[Spawn = town. TEST LIST:"
    printf '%s\n' " 1. DIALOGUE CHROME: right-click Gyorn - dark panel + gold border +"
    printf '%s\n' "    gold text, More... mid-thread, Close on the last line, corner X,"
    printf '%s\n' "    scrollbar arrows page long lines (vs conversation.bmp reference)."
    printf '%s\n' " 2. RECRUIT: Gyorn join offer shows Accept/Decline; Accept = follows."
    printf '%s\n' " 3. VENDOR: right-click Adwana - shop opens, buy a potion, sell it."
    printf '%s\n' " 4. SAVE SOAK: F5 quicksave, kill something, open a chest if you see"
    printf '%s\n' "    one, F9 load - gold/kills/chest state should match the save"
    printf '%s\n' "    (console prints \"load: world state - N bool(s), ...\").]"
    echo
    export SIEGEFX_DEBUG_LOG_FILE="${TEMP}/siegefx_diag/test108.log"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/bt_r1; ERRORLEVEL=$?
    unset SIEGEFX_DEBUG_LOG_FILE
    printf '%s\n' "Log written to: ${TEMP}/siegefx_diag/test108.log"
    echo
    pause
}
T109() {
    echo
    printf '%s\n' "--- ALPHA-2: CRATES + SHRINE (path2crypts) ---"
    printf '%s\n' "[Spawn = region start. TEST LIST:"
    printf '%s\n' " 1. TRAPPED CRATES: smash crates along the path - some spring traps"
    printf '%s\n' "    (console [trap] ... fires; effect + damage numbers float)."
    printf '%s\n' " 2. CHESTS: click a locker/chest - walk up, it opens once, loot"
    printf '%s\n' "    tumbles out ([chest] 0x... opened in console). No smashing chests."
    printf '%s\n' " 3. LIFE SHRINE: find the glowing shrine spot - stepping into its"
    printf '%s\n' "    trigger heals to full ([shrine] ... restored) + fx loop pulses."
    printf '%s\n' " 4. WORLD GOLD: coin piles credit +N gold on pickup, not a ghost item."
    printf '%s\n' " 5. FLOOR TRAPS: crypt-side fire jets trip on proximity w/ authored"
    printf '%s\n' "    reload pauses ([trap] trp_firetrap fires).]"
    echo
    export SIEGEFX_DEBUG_LOG_FILE="${TEMP}/siegefx_diag/test109.log"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/path2crypts; ERRORLEVEL=$?
    unset SIEGEFX_DEBUG_LOG_FILE
    printf '%s\n' "Log written to: ${TEMP}/siegefx_diag/test109.log"
    echo
    pause
}
T110() {
    echo
    printf '%s\n' "--- ALPHA-2: DWARVEN GATE (path2sd) ---"
    printf '%s\n' "[Spawn = region start; walk to the big dwarven gate. TEST LIST:"
    printf '%s\n' " 1. Clicking the stuck gate does NOT open it - the authored line"
    printf '%s\n' "    floats (\"This door seems to be stuck.\") + console [door] refused."
    printf '%s\n' " 2. Regular doors nearby still click-open normally."
    printf '%s\n' " 3. If the area quest event fires (clear its trigger), the gate opens"
    printf '%s\n' "    by message ([door] 0x... opened by message).]"
    echo
    export SIEGEFX_DEBUG_LOG_FILE="${TEMP}/siegefx_diag/test110.log"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/path2sd; ERRORLEVEL=$?
    unset SIEGEFX_DEBUG_LOG_FILE
    printf '%s\n' "Log written to: ${TEMP}/siegefx_diag/test110.log"
    echo
    pause
}
T111() {
    echo
    printf '%s\n' "--- ALPHA-2: STAR DEVICE (gd_a_r1, late-game Glitterdelve) ---"
    printf '%s\n' "[Spawn = region start. TEST LIST:"
    printf '%s\n' " 1. Find the Star Device (console printed [locked] 0x... at load with"
    printf '%s\n' "    its position context). Clicking it without key_glb_star floats"
    printf '%s\n' "    \"Locked.\" - it must NOT unlock."
    printf '%s\n' " 2. Crypt-style doors here author msg_scid_opening: opening one prints"
    printf '%s\n' "    [door] chaining + downstream trigger lines."
    printf '%s\n' " 3. Elevators in this region ride like the farmhouse grate did.]"
    echo
    export SIEGEFX_DEBUG_LOG_FILE="${TEMP}/siegefx_diag/test111.log"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/gd_a_r1; ERRORLEVEL=$?
    unset SIEGEFX_DEBUG_LOG_FILE
    printf '%s\n' "Log written to: ${TEMP}/siegefx_diag/test111.log"
    echo
    pause
}
T112() {
    echo
    printf '%s\n' "--- NAV VERTICAL-REBIND REPRO (sd_r1 mine ledge, headless) ---"
    printf '%s\n' "[test-110 field bug: walking the ledge horseshoe around the sealed"
    printf '%s\n' " t_sd_cap node, the funnel line grazes the ledge edge; the walker used"
    printf '%s\n' " to re-bind to the path2sd mountain top 27u above and strand there."
    printf '%s\n' " Expect: \"reached\", every tick pos Y=17.00, ~29u walked vs 6.2 straight."
    printf '%s\n' " A Y=44.00 line or \"blocked\" = regression.]"
    echo
    "$TOOL" world follow "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" /world/maps/map_world/regions/path2sd,/world/maps/map_world/regions/bt_r1,/world/maps/map_world/regions/path2dm,/world/maps/map_world/regions/path2sd_a,/world/maps/map_world/regions/sd_r1,/world/maps/map_world/regions/sd_r2 -88.1,17.0,135.8 -83.6,17.0,140.0 4.5 600; ERRORLEVEL=$?
    echo
    printf '%s\n' "[Also available: \"%TOOL%\" world probe <map> <terrain> <regions> <x,y,z or 0xGUID>"
    printf '%s\n' " lists every nav tri in an XZ column / a snode's tri contribution;"
    printf '%s\n' " \"world path ... --full\" dumps a corridor with snode + seam provenance;"
    printf '%s\n' " \"sno find <terrain-tank> 0xMESHGUID\" names + parses a mesh guid.]"
    echo
    pause
}
T113() {
    echo
    printf '%s\n' "--- ENEMY ROAM-SIM (headless wander soak, path2sd..sd_r2 set) ---"
    printf '%s\n' "[Every actor.gas mob gets the game's real wander driver and 90 sim-"
    printf '%s\n' " seconds at 20 Hz. Receipts: 0 FROZEN; no pile at a user-reported"
    printf '%s\n' " spot (the 2026-07-10 krug pile at -26.5,32,151 must stay gone)."
    printf '%s\n' " A handful of BLOCKED-heavy but moving actors is normal (pond fish,"
    printf '%s\n' " narrow trails). Use --near=x,z,r to reproduce a field report spot.]"
    echo
    "$TOOL" region roam-sim "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" /world/maps/map_world/regions/path2sd,/world/maps/map_world/regions/bt_r1,/world/maps/map_world/regions/path2dm,/world/maps/map_world/regions/path2sd_a,/world/maps/map_world/regions/sd_r1,/world/maps/map_world/regions/sd_r2 90; ERRORLEVEL=$?
    echo
    pause
}
T114() {
    echo
    printf '%s\n' "--- SC-MP-EOS P3: multiplayer net round-trip (headless) ---"
    printf '%s\n' "[Host and client MpSessions over the loopback transport. Asserts:"
    printf '%s\n' " join -> world snapshot delivered intact, host-authoritative state"
    printf '%s\n' " deltas applied on the client, client Input drives the host sim,"
    printf '%s\n' " chat relays both ways, and the bounds-checked protocol reader"
    printf '%s\n' " flags a truncated frame instead of throwing. Expect ALL PASS.]"
    echo
    "$RUN" --selftest-net; ERRORLEVEL=$?
    echo
    pause
}
T115() {
    echo
    printf '%s\n' "--- FRONTEND CHROME SHOTS: offscreen PNG receipts ---"
    printf '%s\n' "[Renders the FrontendScene chrome at the MainMenu + SinglePlayer"
    printf '%s\n' " settled states into goldens/frontend-shots/*.png via a hidden"
    printf '%s\n' " window - no clicking through the boot flow needed. Open the PNGs"
    printf '%s\n' " and compare against retail screenshots. Chrome layer only: the"
    printf '%s\n' " per-widget hover swaps + settled-pose labels RenderHost layers on"
    printf '%s\n' " top are not part of the receipt.]"
    echo
    "$RUN" --frontend-shot "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" MainMenu --t=3.4; ERRORLEVEL=$?
    "$RUN" --frontend-shot "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" SinglePlayer --t=3.4; ERRORLEVEL=$?
    echo
    pause
}
T116() {
    echo
    printf '%s\n' "--- ENDGAME: GOM ARENA (gom2) ---"
    printf '%s\n' "[Spawns in the final arena. Dev tools are live: press tilde for the"
    printf '%s\n' " console, click GOD for invulnerability and KILL to slay nearby foes."
    printf '%s\n' " Fight (or KILL) Gom - his death spawns Super Gom after the authored"
    printf '%s\n' " delay - then bring down Super Gom. Expected: \"Vanquish the Seck\""
    printf '%s\n' " completes in the journal, the arena-exit chain fires, and the"
    printf '%s\n' " VICTORY card appears (click it after it settles to keep playing).]"
    echo
    export SIEGEFX_DEV="1"
    export SIEGEFX_DEBUG_SPELLS="fireball,iceshard,lightning"
    # gom2's authored start node isn't in the play-region layout (centroid
    # fallback lands on an unreachable ledge at y=1.8); spawn on the ARENA
    # floor ~15u from Gom himself (he stands at 167.7,-159.1,-66.0).
    export SIEGEFX_DEBUG_SPAWN="155,-158,-70"
    "$RUN" --play-region "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" "${DS1}/Resources/Logic.dsres" "${DS1}/Resources/Objects.dsres" /world/maps/map_world/regions/gom2; ERRORLEVEL=$?
    unset SIEGEFX_DEV
    unset SIEGEFX_DEBUG_SPELLS
    unset SIEGEFX_DEBUG_SPAWN
    echo
    pause
}
T117() {
    echo
    printf '%s\n' "--- ENEMY CHASE-SIM AUDIT (headless directed-movement, new-game set) ---"
    printf '%s\n' "[For every actor.gas placement AND generator child in the fh_r1 set,"
    printf '%s\n' " samples quarry points 6-14u away that A* declares reachable, then"
    printf '%s\n' " physically walks the follower there via the game's TickDriven chase"
    printf '%s\n' " path. Any non-arrival is a walker-vs-A* divergence (nav pin) with a"
    printf '%s\n' " clustered hotspot list. Receipts: failure count should only go DOWN"
    printf '%s\n' " vs the last recorded run (2026-07-19: 49 FAILED / 1122 attempts)."
    printf '%s\n' " Append --diag for per-cluster gate-level replays. Region arg \"all\""
    printf '%s\n' " sweeps every region one mesh at a time (2026-07-19: 57/81 regions"
    printf '%s\n' " with failures - the standing burn-down list).]"
    echo
    "$TOOL" region chase-sim "${DS1}/Maps/World.dsmap" "${DS1}/Resources/Terrain.dsres" /world/maps/map_world/regions/fh_r1,/world/maps/map_world/regions/bc_r1,/world/maps/map_world/regions/fh_r1a,/world/maps/map_world/regions/hc_r1,/world/maps/map_world/regions/path2crypts; ERRORLEVEL=$?
    echo
    pause
}

while true; do
    menu
    read -rp "Choose: " CHOICE || exit 0
    case "${CHOICE,,}" in
        1) T1 ;;
        2) T2 ;;
        3) T3 ;;
        4) T4 ;;
        5) T5 ;;
        6) T6 ;;
        7) T7 ;;
        8) T8 ;;
        9) T9 ;;
        10) T10 ;;
        11) T11 ;;
        12) T12 ;;
        13) T13 ;;
        14) T14 ;;
        15) T15 ;;
        16) T16 ;;
        17) T17 ;;
        18) T18 ;;
        19) T19 ;;
        20) T20 ;;
        21) T21 ;;
        22) T22 ;;
        23) T23 ;;
        24) T24 ;;
        25) T25 ;;
        26) T26 ;;
        27) T27 ;;
        28) T28 ;;
        29) T29 ;;
        30) T30 ;;
        31) T31 ;;
        32) T32 ;;
        33) T33 ;;
        34) T34 ;;
        35) T35 ;;
        36) T36 ;;
        37) T37 ;;
        38) T38 ;;
        39) T39 ;;
        40) T40 ;;
        41) T41 ;;
        42) T42 ;;
        43) T43 ;;
        44) T44 ;;
        45) T45 ;;
        46) T46 ;;
        47) T47 ;;
        48) T48 ;;
        49) T49 ;;
        50) T50 ;;
        51) T51 ;;
        52) T52 ;;
        53) T53 ;;
        54) T54 ;;
        55) T55 ;;
        56) T56 ;;
        57) T57 ;;
        58) T58 ;;
        59) T59 ;;
        60) T60 ;;
        61) T61 ;;
        62) T62 ;;
        63) T63 ;;
        64) T64 ;;
        65) T65 ;;
        66) T66 ;;
        67) T67 ;;
        68) T68 ;;
        69) T69 ;;
        70) T70 ;;
        71) T71 ;;
        72) T72 ;;
        73) T73 ;;
        74) T74 ;;
        75) T75 ;;
        76) T76 ;;
        77) T77 ;;
        78) T78 ;;
        79) T79 ;;
        80) T80 ;;
        81) T81 ;;
        82) T82 ;;
        83) T83 ;;
        84) T84 ;;
        85) T85 ;;
        86) T86 ;;
        87) T87 ;;
        88) T88 ;;
        89) T89 ;;
        90) T90 ;;
        91) T91 ;;
        92) T92 ;;
        93) T93 ;;
        94) T94 ;;
        95) T95 ;;
        96) T96 ;;
        97) T97 ;;
        98) T98 ;;
        99) T99 ;;
        100) T100 ;;
        101) T101 ;;
        102) T102 ;;
        103) T103 ;;
        104) T104 ;;
        105) T105 ;;
        106) T106 ;;
        107) T107 ;;
        108) T108 ;;
        109) T109 ;;
        110) T110 ;;
        111) T111 ;;
        112) T112 ;;
        113) T113 ;;
        114) T114 ;;
        115) T115 ;;
        116) T116 ;;
        117) T117 ;;
        b) build ;;
        q) exit 0 ;;
    esac
done
