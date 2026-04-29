# SiegeFX

![SiegeFX](siegeFX_logo.png)

An open-source reimplementation of **Dungeon Siege** (Gas Powered Games, 2002), written in C# / .NET 8.

The goal is to run Dungeon Siege natively on modern Windows at any resolution, with a clean and moddable architecture — by loading the original game's data files directly rather than redistributing any of them.

> **SiegeFX requires the original game data.** It ships no copyrighted assets. You must own a copy of Dungeon Siege (GOG, Steam, or original discs) to use it.

## About

SiegeFX is a slow-burn reverse-engineering project: every shipped phase ends in something visibly working, and every quirk we hit along the way gets written down. The codebase is C# / .NET 8 with Silk.NET for windowing/GL/audio. It loads `.dsres` tank archives, parses the GAS template language, runs the original Skrit gameplay scripts on a clean VM, and renders ASP meshes with PRS skeletal animation. Spells, leveling, audio, and save/load are all wired against the values DS1 ships in `formulas.gas` rather than reinvented.

It is also a **clean-room** reimplementation. Every claim in the wiki and every fix in the code traces back to running our own headless audits over the bytes the original game ships — `siegefx tank list`, `siegefx raw decode`, `siegefx asp info`, `siegefx region prop-textures`, `siegefx balance curve`, `siegefx asp subset-fuzz` and friends. The 2023 leak of the original DS1 source is off-limits; you don't need it to figure this engine out, you just need patient tooling.

Most of the work that made this tractable is other people's: **Guilherme Lampert** documented the asset formats, **Scott Bilas** (GPG's lead engine programmer) wrote up the engine internals publicly, and the **SiegeTheDay** modding community kept the importer scripts alive. The credits at the bottom of this README aren't an afterthought — without that prior art this project wouldn't exist.

## Status

**`v0.15.0`** — playable across the world. Farmboy spawns at the authored start, click-to-move and click-to-attack route through the live nav mesh, equipped weapons render and animate on the hand bone, the HUD draws DS1 bitmap-font text with HP/MP bars derived from STR/DEX/INT, and `Q` / `W` cast slotted spells with per-element projectile + impact tinting (fireball reads orange, iceshard cyan, zap blue) and 3D positional audio. RMB on an NPC opens a branching dialogue panel; quests journal and persist across saves; vendors stock and trade for gold dropped by enemies. `F5` / `F9` quicksave/quickload restore the world losslessly.

A DS1-faithful character creator built from the shipped `character_select.gas` lets you pick gender / head / face / hair / shirt / pants over a live 3D preview, and the picked hero name + variant axes round-trip through quicksave (schema v5).

Region world events run through the same sfx_script VM as spell casts — chimneys vent smoke, cooking fires + torches throw their billboard cones, and the farmhouse mill turns: animated waterfalls cascade via per-texture UV scroll while the placed waterwheel prop spins on its own X axis from the `chore_default = rotateX?rpm=-8` skrit baked at spawn. Per-instance `aspect.scale_multiplier` is now honored at spawn time, so DS1's baked foliage variation reads correctly (≈1150 placements in fh_r1 alone) and the breakable farmhouse door inflates to its 1.5× footprint instead of reading as a clone of the everyday door.

Phase 21 polish in progress — the always-on HUD now matches DS1's status pane (HP/MP readout above STR/DEX/INT, two-panel dock arrows, four-cell active-ability bar with per-skill XP fills), inventory cells render edge-to-edge in a true grid, and scroll-wheel zoom dollies the chase camera along the view ray instead of arcing flatter as you pull back. Loot drops were over-corrected to zero in 12-SC-3; equipped buckets now roll at a 35% chance gate so corpses leave usable gear behind without becoming weapon-vending machines.

<details>
<summary><strong>Receipts</strong> — 81 regions / 50,680 props / 7,252 NPCs / 2,829 pcontent entries / 16,275 SNO tiles / 2,086 trigger rows / 1962 PRS animations / 69 spells, every claim backed by a headless <code>siegefx</code> audit.</summary>

The clean-room rule: every claim is backed by a headless `siegefx` audit you can run yourself.

- **81 regions** — all shipped DS1 regions load through the cross-boundary streamer
- **50,680 static-prop placements** — 100% texture via the runtime resolver (`region prop-textures all`)
- **7,252 NPCs / 7,517 texture slots** — every slot resolves (`region actor-coverage all`)
- **2,626 ASP meshes** — 274 multi-subset; the BTRI cornerStart fix landed here (`asp subset-fuzz`)
- **~18,000 hero variants** — 7 bodies × ~32 skins × ~41 pants × 2 genders (`templates hero-variants`)
- **520 mood definitions + 165 SED descriptors** — region ambient beds and per-fire pitch ranges driven by data (`mood list`, `audio sed-list`)
- **626 audio cues** in `Sound.dsres`; 20 wired so far, per-category gaps mapped (`audio coverage`)
- **2,829 pcontent entries** (303 weapons across 10 classes + 435 armor templates) tagged with rarity (normal / rare / unique) — krug-class `#club/2-3` picks a generic power-2/3 club, `#armor/-rare(1)/28-80` rolls only rare-tier armor in band, `#*/-unique(2)/175-286` picks named unique drops the normal roll never sees (`pcontent dump`)
- **16,275 SNO tiles** (World + MpWorld) — every shipped retail tile parses cleanly; the doors/spots disk-order swap and the v6.2/v6.3 `general_connection_section.center` gate landed here (`region nav-fuzz`); funnel-smoothed nav follower hugs corridor corners — fh_r1 (10,10)→(30,30) walks 30.30 units vs 28.28 straight-line over 39 path triangles / 7 funnel waypoints (`region follow`); water tiles ride alongside floor in the mesh (1,435 water / 26,973 floor in fh_r1), gated by a per-actor `NavTraversal` policy — `--water=4` on `region path` swims a 6-tri pond crossing the default land-only policy refuses (`region nav`, `region path --water=N`); A* open-set rewritten from SortedSet to a duplicate-tolerant binary min-heap, **2.0× pathfinder speedup** at fh_r1 scale — 504 µs → 247 µs per probe over 173-tri paths (`region path-bench`); **land↔water seam stitching** wires Floor↔Water boundary edges that share an XZ footprint and a wadeable Y delta (≤0.5u), since DS1 authors water surfaces in their own SNOs whose vertices don't weld to the shoreline floor — fh_r1 climbs from 0/1435 to 37/1435 water tris with a Floor edge, World totals **4,907 cross-kind seams across 81 regions**; biggest-component A* under LandOnly stays at 99.4% (the residual 0.6% reflects newly-revealed floor islands reachable only via water — real topology), and an amphibious actor on (30,0,30) routes a 63-tri / 48.97u path to the (27.57,-1.5,0.70) water sample that LandOnly correctly refuses (`region nav`, `region path-fuzz`)
- **2,086 trigger rows** across the World — `[instance_triggers]` matrix language now parsed and dispatched at 20Hz alongside the message bus; 1,952/3,043 special.gas placements bear matrices, zero parse failures. 8/8 condition verbs (`actor_within_sphere`, `go_within_sphere`, `party_member_within_sphere/_bounding_box/_node`, `party_member_entered/left_trigger_group`, `receive_world_message`) and 5/5 action verbs (`send_world_message`, `fade_nodes`, `mood_change`, `set_interest_radius`, `fade_nodes_global`) dispatched; group-keyed pairing, `single_shot` / `start_active` / `delay` / `reset_duration` honored. Two-pass tick threads named `occupants_group` producers (266 rows authoring 136 distinct groups across 79 regions) into entered/left consumers — synthetic trip-tick at fh_r1 reports `entered=4, left=4`. The `when_false` action prefix lands the falling-edge channel (83 deferred actions across 23 regions) — cr_r1 trip-tick fires `when_false=11` on departure, proving the leave-side dispatch (`region triggers`)
- **Full chore dictionary loads per actor** — every `chore_*` section the template authors (default / walk / attack / die / fidget / magic / misc) is parsed at spawn and addressable by name through `Actor.GetClipIndex("chore_die")`; the chore_misc edge case (`chore_stances=ignore`, full-basename anim_files) is handled too. fh_r1 catalogue grows from 2.0 clips/actor (default + walk only) to **avg 5.99** per actor across 181 spawns (min 3, max 7); chore coverage tally: `chore_default×181, chore_fidget×181, chore_attack×179, chore_die×179, chore_walk×179, chore_magic×144, chore_misc×41` — every actor in the region resolves a chore_default (`region spawn`)
- **Mob loot drops match shipped pacing** — `siegefx loot dump <Logic.dsres> <template>` prints the parsed `[inventory][pcontent]` Equipped/Drops trees and rolls N times. Pre-fix the equipped weapon (`es_weapon_hand`) was folded into the drop pile so every kill tossed the worn axe; post-fix only `il_main` entries roll. **Receipts (200 rolls, seed 42):** `krug_grunt` drops 36/200 (avg 0.18/kill, matching the 0.15 chance gate × ~uniform 4-way pick), distribution `#weapon/12-17` 7.0%, `#armor/6-29` 4.0%, `potion_health_small` 4.0%, `potion_mana_small` 3.0% — worn `ax_d_d_1h1b_avg` stays on the body. `krug_scout` 25/200 (avg 0.12/kill, two stacked oneof* gates 1.0×0.12). `gremal` 0/200 (no `[inventory][pcontent]` in specializes chain — correct, drops nothing). The player click-attack also gates by player→target reach now (was a screen-pick tolerance — 30u-away enemies are no longer instantly hittable; the follower walks up to attack range and the swing fires when in reach), and `chore_attack` (on melee swings) + `chore_magic` (on `Q`/`W` casts) + `chore_die` (on death) actually play on every action via an override layer in `ActorHostBridge` that reads through the skrit-driven blender — 179/179 combatants in fh_r1 carry `chore_attack` and `chore_die`, 144/179 carry `chore_magic`; corpses hold the last frame of `chore_die` (clamp instead of mod) and survive quicksave/quickload via `BeginDeathChore` on restore. Player swings now match the equipped weapon class — `RefreshMotionClips` walks the whole `[chore_dictionary]` on every equip change so attack/magic/die/get_hit/fidget all rebind to the equipped weapon's stance (was idle+walk only — dagger-equipped farmboy played the unarmed punch on swings); chore_magic now rides through moving casts too, since the IsMoving→walk swap respects a pinned `ActorHostBridge.IsOverrideActive` (`siegefx loot dump`)
- **Legacy PRS v0x202 / v0x302 animation loader + TRCR resync** — both older formats decode through the same chunk dispatcher as v3, with the keylist branch synthesizing matching rot/pos arrays from the 32-byte combined `(time, quat, vec3)` keys those revisions use. **1962 / 1962 shipped DS1 .prs files parse with zero failures**: **1855 v3 (94.5%)** + 62 v0x202 + 45 v0x302; 131 carry TRCR tracer chunks (weapon-trail / ammo-hook info, layout undocumented) that the loader skips via 4-byte-aligned tag-scan resync to the next valid chunk, validated by chunk-version + KLST bone-index plausibility checks. Pre-fix every TRCR-bearing file threw `NotSupportedException` and fell through to a stub no-clip — including the farmboy stance-1 attack (`a_c_gah_fb_fs1_at.prs`), which silently demoted dagger swings to the unarmed punch chore. Post-fix the canary parses to 37 keyed bones / 211 rot keys / 0.83s and the dagger swing binds correctly. The 21 phrak / swamp_stinger / etc. actors that previously fell back to no-clip in fh_r1 now own full chore catalogues, and that shrinks the empty-catalogue cohort to **0** across the whole region (`prs fuzz`)
- **69 offensive spell templates parse end-to-end** — `[magic]` block damage / mana-cost / heal expressions evaluate against caster + target stats. SpellExpr learned the `**` power op (19 spells use it — fireball, iceshard, acid_cloud), the `#magic`/`#maxlife`/`#life`/`#src_mana`/`#src_life` placeholders, and the `[[ cond ?: a : b ]]` ternary block. Receipts: fireball L1 jumped from `0..0` (parser ate one star of `**`) to `3.85..4.67`; spell_freeze with `#maxlife=20` charges `mana=50.0` (was 0); leech_life's clamp formula evaluates to `0.7 / 0.3` across two `--src_life` cases; healing_hands' triple-ternary returns `11 / -0.61 / 4` across three context cases (`spells eval`, `spells show`). Element classification groups all 69 spells across seven buckets — Fire 27, Lightning 16, Ice 6, Holy 6, Acid 4, Death 3, Generic 7 — and the renderer tints both bolt and impact-flash from the bucket so a fireball reads orange instead of every spell sharing the same blue zap (`spells elements`)

</details>

End-to-end Farmhouse → Castle Ehb playtest is next.

Full status, roadmap, and per-phase development journal live on the [**wiki**](https://github.com/codingncaffeine/SiegeFX/wiki).

## Project layout

```
src/
  SiegeFX.Core       class library — file format parsers, no UI
  SiegeFX.Tools      unified `siegefx` CLI (tank info/list/extract, raw info/decode)
  SiegeFX.Browser    WPF asset explorer
  SiegeFX.Runtime    game / engine host (Silk.NET)
```

See the wiki's [Architecture](https://github.com/codingncaffeine/SiegeFX/wiki/Architecture) page for the subsystem map.

## Build

```
dotnet build
```

Then from `src/SiegeFX.Runtime`:

```
dotnet run -c Release
```

See the wiki's [Building and Running](https://github.com/codingncaffeine/SiegeFX/wiki/Building-and-Running) page for game-data setup and controls.

## Credits & prior art

- **Guilherme Lampert** — [reverse-engineering-dungeon-siege](https://github.com/glampert/reverse-engineering-dungeon-siege) — canonical documentation of Tank, ASP, SNO, RAW formats (MIT). Most of `SiegeFX.Core` is a port of his work.
- **Scott Bilas** — GPG's lead engine programmer, whose public writings document the Siege engine internals.
- **OpenSiege** — [github.com/OpenSiege/OpenSiege](https://github.com/OpenSiege/OpenSiege) — earlier C++/OpenSceneGraph reimplementation attempt.
- **SiegeTheDay.org** — DS1 modding community and original source of the MaxScript importers we lean on for `.prs` animation.

## License

MIT. See [LICENSE](LICENSE).
