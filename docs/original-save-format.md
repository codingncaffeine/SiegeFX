# The original game's save format

What Dungeon Siege 1 and *Legends of Aranna* write when you save, as far as it
has been worked out, and what is still open. Everything here was learned from
saves made for this purpose, one change apart, and read with SiegeFX's own
tools; nothing comes from the leaked source. When you solve one of the
[open questions](#open-questions), move it up into the known sections with the
evidence.

The reader is the `src/SiegeFX.OriginalSaves` library (kept out of the engine assemblies the parity ledger scans, since its field names come from save files, not game data); the CLI is
`siegefx save inspect | tree | diff` (see [Tools](#tools)).

## Files

| Extension | Written for | Contents |
|---|---|---|
| `.dssave` | Save Game | `info.gas`, `party.gas`, `portrait-0.bmp`, `thumb.bmp`, `world.xidx`, `world.xdat` |
| `.dsasave` | Autosave | Same files; `info.gas` says `is_auto_save = true`, screen name `[Auto-Save]` |
| `.dsparty` | Multiplayer character | `info.gas`, `party.gas`, `portrait-0.bmp` only — no world |

Saves sit in `Documents\Dungeon Siege\Save\` (DS1) and
`Documents\Dungeon Siege LOA\Save\` (LoA). There is also an `is_quick_save`
flag in `info.gas`; no quicksave has been examined yet.

Each file is a **tank archive** — the same container as the game's `.dsres`
data — with zlib-compressed entries, one directory, product id `DSig`,
priority `User`, flag `AllowMultiplayerXfer`, creator `!GPG`. The tank's title
is the save's screen name and its author the player's name.

**Tank "product version" is a build stamp.** The header's three version
words hold 16-bit halves: word 2 = month (high) and year (low), word 3 =
build (high) and day (low). `0x000707D3, 0x00020002` → `2003.07.0202`, which
is exactly the `msqa:2003.07.0202` in `info.gas`. Word 1 was `0x00050000` in
every file seen; its meaning is unknown. `siegefx tank info` prints the raw
words; `OriginalSave.BuildStamp` decodes them.

## info.gas

Plain GAS text, one `[info]` block. What the original's load menu shows.

| Field | Example | Notes |
|---|---|---|
| `screen_name` | `"D (0-02-25)"` | Name in the load list; the time is play time |
| `hero_name`, `player_name` | `"D"` | |
| `map_name`, `map_screen_name` | `map_world`, `"Kingdom of Ehb"` | `map_expansion` / `"Legends of Aranna"` for LoA; `multiplayer_world` / `"Utraean Peninsula"` in a `.dsparty` |
| `region` | `fh_r1` | Region the hero is in |
| `teleport` | `map_world:n:-0.98,0.91,-0.09,0x5353287c` | Hero position: x, y, z inside one terrain node, then the node id |
| `time_elapsed`, `time_elapsed_text` | `145.808601`, `"0:02:25"` | Play time in seconds |
| `save_time`, `save_time_text` | `134363309084250000` | Windows FILETIME (100 ns ticks since 1601, UTC) |
| `save_duration` | `0.115552` | Seconds the save took |
| `is_auto_save`, `is_quick_save` | `false` | |
| `is_dsx` | `true` | Present (true) in LoA files only |
| `is_retail_content`, `single_player`, `product_compile_mode` | `true`, `true`, `"Retail"` | |
| `product_version` | `1.11.1.1486 (msqa:2003.07.0202)` | DS1 1.11; LoA writes `1.50.0.0056 (msqa:2003.10.1601)` |
| `file_name` | full path of the save | |
| `[fubi_manifest]` | `digest`, and `crc32` of `DungeonSiege.exe` | Ties the save to the game executable |
| `[tank_manifest]` | one block per loaded data tank | `file_name`, `tank_type`, `guid`, `data_crc32`, `index_crc32`, `utc_build_time`, `version` |

The tank manifest identifies the exact game data a save was made with: the
same install produced the same CRCs in every save, and a 2019 save from an
earlier install lists different `Logic.dsres`/`Objects.dsres` CRCs.

## party.gas

Plain GAS text. `[party] { [members] { [0] { [member] {…} } … } [party] { gold; hero_index; } }`.
Every party member — hero, companions, pack mule — has the same shape.

| Field | Example | Notes |
|---|---|---|
| `screen_name`, `template_name`, `actor_class` | `"D"`, `farmgirl`, `"Farmer"` | `actor_class` is the title DS1 shows (Farmer, Squire, Champion…); empty for the pack mule |
| `gold` | `30` | Also in the trailing `[party]` block; both changed together |
| `movement_orders`, `combat_orders`, `focus_orders`, `disposition_orders` | see [Orders](#field-commands-orders) | Per member |
| `selected_active_location` | `il_active_melee_weapon` | Active weapon slot |
| `custom_head`, `pack_only`, `grid_x_offset`, `grid_y_offset`, `m_clientcachedengagedobject` | | Not yet interpreted |
| `[inventory]` | `[N] { template_name; inv_location; equip_slot; grid_x; grid_y; gold_value; }` | See below |
| `[skills]` | `<skill>_level`, `<skill>_xp` | strength, dexterity, intelligence, melee, ranged, nature_magic, combat_magic, and `uber` (the overall character level) |
| `[skins]` | `0 = b_c_gah_fg_skin_010,b_c_gah_fg_hair_007;` | Appearance textures |

**Inventory.** Row order is not meaningful — two saves seconds apart listed the
same 18 items in a different order. Equipped items carry `equip_slot`
(`es_feet`, `es_chest`, `es_spellbook`, `es_weapon_hand`, `es_shield_hand`…)
or an active `inv_location` (`il_active_melee_weapon`,
`il_active_ranged_weapon`) and no grid position. Pack items have `grid_x`,
`grid_y` in the inventory window's pixels: 32-pixel cells, the first at
(255, 30).

**Skills and XP.** Levels are fractional (`melee_level = 1.00195` is just past
level 1). Killing a krug scavenger gave 4 melee XP, twice, and both times the
attributes rose by 64 % / 27 % / 9 % of it (strength / dexterity /
intelligence: 2.56 / 1.08 / 0.36). `uber_xp` tracks the total.

### Field Commands orders

Each value below was observed in a save written right after setting that
button in the original game.

| Panel row | Field | Value → button | SiegeFX encoding |
|---|---|---|---|
| Movement | `movement_orders` | `mo_free` Move Freely · `mo_limited` Engage · `mo_hold` Hold Ground | 0 · 1 · 2 |
| Attack | `combat_orders` | `co_free` Attack Freely · `co_limited` Defend · `co_hold` Hold Fire | 0 · 1 · 2 |
| Targeting | `focus_orders` | `fo_closest` Closest · `fo_strongest` Strongest · `fo_weakest` Weakest | 0 · 1 · 2 |
| (none) | `disposition_orders` | `ad_defensive` everywhere, except `ad_offensive` on LoA's Kraal | — |

The hero has orders like every companion, which is the parity evidence behind
the "Give the hero the Field Commands orders" fix.

## The world: world.xidx + world.xdat

A tree of named values. Both files start with an 8-byte magic and are
little-endian.

### world.xdat (`DSigDXfr`)

16-byte header (`DSigDXfr`, `u32` version `0x00010000`, `u32` 0), then entries.
An entry is a **NUL-terminated name followed by the raw value bytes**. A child
node is just its name (no value bytes). Nothing marks types or lengths.

### world.xidx (`DSigIXfr`)

```
"DSigIXfr"  u32 version (0x00010000)  u32 0  u32 nodeCount
then nodeCount records, node 0 = root:
    u32 valueCount, valueCount × u32  data offset of a value entry's name
    u32 childCount, childCount × (u32 data offset of the child's name, u32 child node number)
```

Verified on 14 saves (5 k – 98 k nodes): the records consume the index
exactly, every node but the root is referenced exactly once (a tree), and
every offset lands on a name.

### Values

A value runs from the end of its name to the next indexed entry in the data
file, so its **size is the only type evidence**:

| Size | Reading | Examples |
|---|---|---|
| 1 | bool | `tutorial_tips`, `bactive`, `m_constructed` |
| 4 | int, float or id | `m_goid`, `m_scid`, `m_currentstate`; floats look like `1065353216` = 1.0 |
| 8 | double | `m_currenttime` |
| 12 | three floats (a vector) | `m_upvector` = (0, 1, 0) |
| 16 | position: three floats + terrain node id | `m_cameraposition` |
| NUL-terminated | text | `m_code` = `"success"`, `sname` = `"quest_edgaar_basement"` |
| ends in two zero bytes | UTF-16 text | `sscreenname` = `"Clear Edgaar's Basement"`, `sspeaker` |

**Node ids in positions are stored byte-reversed** relative to how `info.gas`
prints them: `teleport … 0x5353287c` is stored `53 53 28 7c`, so read the last
four bytes big-endian.

Name prefixes help: `b…` bool, `s…` string, `m_p…` pointer, `m_…` member;
names ending in `$` belong to skrit (script) property stores.

### Order in the data file

The data file is a depth-first walk of the tree, with a node's values and
children **interleaved in write order** (the index files them into two
lists), with one exception: **0.3–0.4 % of entries** (201 of 46 041 in a
fresh save, 1 805 of 603 085 in the big LoA save) are values written *after* a
later sibling's whole subtree. They are always the same few members —
`m_pblendgroup`, `orientation`, `m_skrit`, `jat_use`,
`m_screenplayervisiblecount`, `m_wantsdraws`, `m_bforceget` — which looks
like a second pass that fills in references once their targets are written.
The reader doesn't care (offsets come from the index) and keeps each node's
entries in file order.

### Top-level sections

`skritengine`, `m_pworldterrain`, `world`, `services`, `godb`, `m_pmcp`,
`m_pflamethrower`, `m_psim`, `screenplayer`, `tattoogame`, `uicamera`,
`uigame`, `gridboxes`, `misc` — and in LoA also `m_ptransformationmanager`
(after `m_psim`).

### Known paths

| What | Path | Fields |
|---|---|---|
| Quests | `/world/m_pvictory/m_quests/N/_value` | `sname`, `sscreenname` (UTF-16), `sspeaker` (UTF-16), `bactive`, `bcompleted`, `bviewed`, `id`, `order`, `currentorder`, `events/…` |
| Script flags and counters | `/world/m_pgameauditor/m_entries/N` | `m_key` plus `m_bool`, `m_int`, `m_float`, `m_double`, `m_string`, `m_tag` (one slot used) |
| "Show tips" option | `/uigame` → `tutorial_tips` | bool |
| Camera | `/m_pworldterrain/siegeengine/m_pcamera` | `m_cameraposition`, `m_targetposition` (positions), `m_upvector`, `m_fieldofview` |
| Game objects | `/godb/m_globaldb/N` | `m_goid`, `m_scid`, `m_datatemplate` (template name), `m_regionsource`, `m_parent`, flags (`m_isselected`, `m_isinsideinventory`, …), `m_componentcoll/…` (components: skills, conversations, enchantments, triggers…), `m_childcoll` |
| Triggers | inside an object's `m_componentcoll/…/m_instancetriggers/m_triggers` | conditions and actions with parameters, e.g. `change_quest_state quest_edgaar_basement completed` |
| Loaded area | `/m_pworldterrain/siegeengine/m_frustumcoll/N` | `id` (a bit per party member), `m_frustumpos`, `m_frustumwidth`, `m_prevownednodes/N/_value` (node GUIDs) |
| Objects made at run time | `/godb/m_localdb/N` | same layout as `m_globaldb`, `m_scid = 0xfffffffe` (debris, leaves) |
| Placements never to create again | `/godb/m_retiredscidcoll/N` | `_value` (SCID), sorted |
| Per-SCID bits | `/godb/m_scidbitscoll/N` | `_key` (SCID), `m_bits`, `m_mustrpc` |
| Saved trigger state | `/world/m_ptriggersys/m_trigbitsdb/N` | `_key/m_ownscid`, `_key/m_trignum`, `_value` |

### Objects are keyed by the map's SCID

A game object's `m_scid` is the SCID of the map placement it came from — the
`n:` in `[t:<template>,n:<scid>]` in `regions/<region>/objects/*.gas`, which is
also the SCID SiegeFX gives the actor. Evidence: Edgaar is
`m_scid = 0x01c00b13`, `m_datatemplate = "edgaar"` in the save, and
`siegefx save locate World.dsmap 0x01c00b13` and `… edgaar` both return
`fh_r1  actor.gas  [t:edgaar,n:0x01c00b13]` against the same install. World
import can therefore match saved objects to SiegeFX actors by SCID.

### Life and death, and areas left behind

A creature's `aspect` component (one of its `m_componentcoll` entries, whose
`_name` is `"aspect"`) holds `life_state` (`"ls_dead_normal"` once killed),
`life` and `max_life` as floats.

**What is loaded.** Each party member has a frustum, the area loaded around
them: `/m_pworldterrain/siegeengine/m_frustumcoll/N` with `id` (one bit
each: 1, 2, 4 … 128 in an 8-member party), `m_frustumwidth` 45 (metres) and
`m_prevownednodes`, the terrain nodes it holds. A node GUID there is stored
in the same byte order as a position's node, so read it big-endian to get
the `0x…` the map and `info.gas` print. An object's `m_frustummembership` is
the OR of the frustums it is in. In all six saves checked (C, C2, A2, D7,
the 2019 save, LoA) it is non-zero exactly for the objects on a loaded node,
apart from 19 aspect-less objects in the LoA save that sit on a loaded node
but are in no frustum.

**What is saved.** `godb/m_globaldb` holds game objects, not decor: in C2,
659 trees, bushes, cornstalks, reeds and pieces of furniture placed on
loaded nodes are missing (across all the saves, only a lightable lamp stand
in D7 and two chairs in the LoA save are of that kind), so the map supplies
them every time. Of the
rest it holds:

- every object on a loaded node, untouched barrels included;
- off the loaded nodes (`m_frustummembership = 0`), only creatures (alive
  or dead), items, triggers and generators, and props whose state changed:
  the sarcophagus opened on the way to the crypt (A2), elevators, levers,
  doors and chests (the LoA save).

Untouched props off the loaded nodes are dropped. In new game "C" the five
start-area barrels (`0x01c00001`, `…06`, `…0a`, `…6b6`, `…6b8`) are saved;
in "C2", after running to the next farmhouse, all five are gone although
nobody touched them. So a missing object means "as the map placed it",
unless its SCID is retired.

**Retired SCIDs.** `/godb/m_retiredscidcoll/N/_value` lists the map
placements the game must not create again, for the whole game and whether or
not their area is loaded. Evidence:

| Saves | Added to the list |
|---|---|
| C → C2 (the hero and a krug each smashed a barrel) | `0x01c00008`, `0x01c00427`: both `barrel_glb_fh_r1`, nothing else |
| A → A2 | `0x01c00427` again (the krug smashes the same barrel every new game), plus 7 others along the way |
| Fire Shot → kill | `0x01c005ca`, the krug killed, which is also still saved as a corpse |
| kill → D2 (hit a barrel) | `0x01c006b6`, a start-area barrel |

A new game already has 44 entries, mostly its opening cutscene. All 47 in C2
are placements in `World.dsmap`: 36 `cmd_camera_command`, 4
`trigger_generic`, `cmd_enter_nis`, `dog_sleeping`,
`door_grs_farmhouse_breakable`, a `spider` (`0x08d000b7`), Norick
(`0x01c007c0`, dies in the opening; his corpse is still saved) and the two
smashed barrels. The 2019 save has 7 007 entries and the LoA save 3 005.
SCIDs are unique only within one map: LoA's `map_expansion`
(`Expansion.dsmap`) reuses DS1's numbers, so a save's retired SCIDs must be
looked up in the map its `info.gas` names. Against `Expansion.dsmap`, the LoA
save's 3 005 retired SCIDs matched 9 015 placements, about three each, with
templates repeated as `2W_…` and `3W_…`. That fits a copy of every placement
per difficulty world (regular, veteran, elite) sharing one SCID, presumably
in a folder per world under `objects/`. The save names its world in
`/m_pworldterrain/m_pmap/m_mpworldname`: `"normal"` in every DS1 save,
`"regular"` in the LoA save. That LoA list is mostly monsters killed, crates
smashed, doors and triggers.

Which tanks a save was played with is in its `[tank_manifest]`. The LoA save
lists `Expansion.dsmap` as its only map, and its own `Logic.dsres` and
`Objects.dsres` (CRCs differ from DS1's); `Voices`, `Sound` and `Terrain`
have the same CRCs as DS1's. In a LoA install, `Expansion.dsmap`,
`Expansion.dsres` and `ExpVoices.dsres` are in `DSLOA/`, not `Maps/`. The
list is sorted by SCID. A smashed barrel leaves nothing else by SCID; its
debris is in `/godb/m_localdb`, which holds objects made at run time
(`m_scid = 0xfffffffe`). C2 holds `frag_glb_metal_01` ×4 and
`frag_glb_wood_01`–`04` ×2 each, the hoops and staves of two barrels, and
`/m_psim/m_objects` their physics state. `siegefx save retired` lists a
save's retired placements by template.

Monsters killed are kept with `ls_dead_normal` wherever they are. In A→A2 the
fence-smashing krug `0x01c005ca` and Norick (`0x01c007c0`, who dies in the
scripted bridge collapse) are still saved, dead, at the crypt; in the twelve
D saves every one of the 39 monsters that died stays in every later save.

`/godb/m_scidbitscoll/N` (`_key` = SCID, `m_bits`, `m_mustrpc`) records a
few objects only: `generator_bush_01` krug-spawning bushes get `m_bits =
0x10000` once spent, sound emitters `1`, and a handful of fixed entries
present from the start of the game.

### What the auditor records

`/world/m_pgameauditor/m_entries` holds more than script flags:

- `talked_with_edgaar_once`, `talked_with_edgaar_twice` — conversation flags
  (SiegeFX's `World.Bools` holds the same kind of thing).
- `ge_player_kill_<template>` — kills per monster type (e.g.
  `ge_player_kill_krug_scavenger = 24`).
- `chance_to_hit_bonus_versus_<template>` — a to-hit bonus against monster
  types you have killed many of (`0.09` at 18 krug scavenger kills, `0.15` at
  24). **SiegeFX has no such mechanic** — a parity gap.
- `was_party_member_<goid>`.

## What each experiment showed

| Saves (one change apart) | Change made in game | Where it showed |
|---|---|---|
| base → moved | walked a few steps | `info.gas` `teleport` |
| moved → no tips | turned tips off | `/uigame` `tutorial_tips` 1 → 0 |
| no tips → picked up Fire Shot | picked up a spell | `party.gas` inventory `spell_fireshot` with grid slot |
| Fire Shot → kill | killed a krug scavenger | `party.gas` skills (+4 melee XP, 64/27/9 split); the corpse stays in `godb` |
| kill → D2 | second kill, bow, barrel, 6 gold | gold 30 → 36 twice; `spell_zap` and a club dropped; bow in `il_active_ranged_weapon`; +4 XP again |
| D3 → D4 | Defend → Hold Fire | `combat_orders` `co_limited` → `co_hold` |
| D5, D6, D7 | the remaining order buttons | completes the [orders table](#field-commands-orders) |
| D6 → D7 | got Edgaar's quest | quest `bactive` 0 → 1, `talked_with_edgaar_*` flags, kill counters |
| autosave | — | same format, `is_auto_save = true`, `.dsasave` |
| D7 + the map | `save locate` for Edgaar's `m_scid` | the save's SCID is the map placement's SCID |
| new game "A" → "A2" | killed the fence krug, ran to the crypt, opened sarcophagi | creatures (dead and alive), items and the opened sarcophagus are still saved at the crypt; untouched farm props are not |
| new game "C" → "C2" | ran to the next farmhouse; the hero and a krug each smashed a barrel | the two barrels join `m_retiredscidcoll`; untouched start barrels drop out of `m_globaldb`; loaded nodes = `m_frustumcoll` |
| 2019 DS1 save | 8-member party, 13:56 played | every companion stored like the hero; only the hero gets a portrait file |
| LoA save, `.dsparty` | — | `is_dsx`, expansion tanks in the manifest, `m_ptransformationmanager` section; `.dsparty` has no world |

## Tools

```
siegefx save inspect <file>            # save info, party (orders, skills, gear), quests, flags
siegefx save inspect <file> --all      # every pack item, quest and flag
siegefx save tree <file> --path=/world/m_pvictory --depth=2
siegefx save tree <file> --find=edgaar # every node and value mentioning a name
siegefx save diff <before> <after>     # what changed, clocks and timers hidden
siegefx save diff <a> <b> --all        # including clocks and timers
siegefx save locate <World.dsmap> 0x01c00b13   # where a save object was placed in the map
siegefx save locate <World.dsmap> edgaar       # placements by template name
siegefx save retired <file> <World.dsmap>      # placements the game will not create again
```

`retired` looks up every SCID in `m_retiredscidcoll` in the save's own map
(it refuses a map tank without that map's regions and names the right file),
keeps only the save's world when the map has a folder per world (and prints
the folders it found), and lists them by template; `*` marks the ones still saved (corpses, mostly).

`diff` reports the readable facts first (info, party, orders, items, quests,
flags, tips), then counts value changes per world section. `skritengine` and
`godb` are positional lists — one new object shifts every later index — so
they always show churn and are listed last.

## Open questions

Roughly in order of how much they block importing and writing saves.

1. **What else retires a SCID.** Smashing and killing do; the list also
   grows with one-time gizmos as the game runs (D2 → D3 added 26). Which
   commands and triggers retire themselves, and whether picking up a
   map-placed item does, is not yet sorted out. Also: what
   `m_scidbitscoll` bits other than `0x10000` (a spent spawner bush) mean.
2. **The write order.** 0.3–0.4 % of entries (a handful of member names) are
   written after a later sibling's subtree. A writer needs the rule for when, or
   proof that the original loads a save laid out in plain tree order (it reads
   through the index, so it may not care).
3. **What the original checks on load.** Does it verify `fubi_manifest`,
   the tank manifest CRCs, or the tank's own CRCs? Test by changing one byte of
   a copy (e.g. gold in `party.gas`) and loading it.
4. **Unread `party.gas` fields:** `custom_head`, `pack_only`,
   `grid_x_offset`/`grid_y_offset`, `m_clientcachedengagedobject`,
   `disposition_orders` (seen: `ad_defensive`, `ad_offensive`).
5. **Value types without a size hint:** 4-byte values are int, float or id —
   the reader only guesses. A table of known field types would end the guessing.
6. **Quicksave:** the extension and whether anything else differs.
7. **Tank header word 1** (`0x00050000` in every file) and the `[fubi_manifest]`
   `digest`.
8. **The to-hit-bonus formula** behind `chance_to_hit_bonus_versus_*` (it is
   not a flat amount per kill: 18 kills → 0.09, 24 → 0.15).
