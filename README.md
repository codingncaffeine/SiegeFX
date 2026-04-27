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

**`v0.15.0`** — Playable across the world. Farmboy spawns at the authored start position, click-to-move and click-to-attack route through the live nav mesh, equipped weapons render on the hand bone, the HUD draws DS1 bitmap-font text with HP/MP bars derived from STR/DEX/INT, the inventory and pause menu work, `Q` and `W` cast slotted spells, audio plays positionally, and `F5` / `F9` quicksave/quickload restore the world losslessly. RMB on an NPC opens a branching dialogue panel; quests are journaled and persist across saves; vendors stock and trade for gold dropped by enemies.

All **81 shipped DS1 regions** load through the cross-boundary streamer, with **50,680 static-prop placements** texturing 100% via the runtime resolver. NPCs, dialogue, quests, vendors, spells, leveling, audio, and save/load are all in. The render hot path is zero-alloc per actor per frame.

A growing set of headless `siegefx` audit commands replaces eyeball debugging with measurable claims: `siegefx region prop-textures all` confirmed all 50,680 prop placements resolve a texture; `siegefx balance curve` walks every SkillKind L1→L50 against the shipped `formulas.gas` and verifies HP/MP/XP grow monotonically; `siegefx asp subset-fuzz` parses every .asp in a tank, validates BSMM `(textureIndex, faceSpan)` records sum to BTRI's face count, and prints a subset-count histogram; `siegefx region actor-coverage all` walks every actor.gas placement in every shipped region — **7,252 NPCs across 81 regions, 7,517 texture slots, every one resolves**, with 909 of those NPCs being multi-subset characters; and `siegefx templates equipment-audit` walks a player template's `[inventory][equipment]` block and characterises each slot against shipped content — for `farmboy` it confirms es_weapon_hand attaches to weapon_grip, es_feet derives a per-hero boot mesh via `body.armor_version` + `armor_lookup.gas`, and the boot ASP shares the body's 37-bone skeleton so layered skinning re-uses the body's animation pose. The subset audit cracked a long-standing player-render bug — the corpus is 274 / 2,626 multi-subset meshes, with farmboy resolving as 5 subsets across 2 textures (skin + clothing). BTRI face indices are subtexture-local (not submesh-local) for ASP versions > 2.2; without the per-subtexture cornerStart offset, multi-subtexture characters render with the right textures bound to the wrong triangle ranges, producing webbed-arms / hammer-pants geometry. The actor renderer now binds + draws per subset and applies the cornerStart offset, so multi-texture characters render with correct geometry and the right .raw on every body slot. The PC's equipped weapon now picks the matching idle/walk stance (`weapon_melee` → fs1, `weapon_ranged` → fs5) so the wrist holds the dagger correctly, with a 180° X grip prerotation matching the SiegeMax importer's "grips must be prerotated" bind hack, and the weapon attach tracks the same animated bone-world the body is being skinned with — the dagger swings with the wrist through walk cycles instead of floating at the idle pose. The PC's leather boots now layer on the body via name-keyed bone-map skinning (the boot ASP shares the biped skeleton, so the body's per-frame skin matrices drive the boots in lockstep), with chest armor textures swapping into body subset 1 as a slot-1 override. The character creator's resolver-side plumbing is wired: a `TemplateOverride` threaded through `ActorSpawner.Spawn` lets the picked body type substitute `aspect.model` (so `pos_a1..a7` all resolve), with skin and pants picks layering into the player's slot-0 and slot-1 texture overrides through the same path the chest override uses; `siegefx templates hero-variants` enumerates every shipped axis (7 body meshes × ~32 skin tones × ~41 pants colors × 2 genders ≈ 18,000 variants) so the upcoming UI menu can be built from real bytes. The creator UI itself is built from the shipped DS1 frontend gas — `/ui/interfaces/frontend/character_select/character_select.gas` is the source of truth for the 14 ◄► axis buttons (gender / head / face / hair / shirt / pants), the name edit box, the 3D preview viewport, and the Begin / Cancel buttons; rects and pixel sizes scale linearly from the DS1 reference 800×600 to the live window. With `SIEGEFX_CREATOR=1` set, the panel gates `TrySpawnPlayer` until the player picks a variant and clicks Begin (or Cancel to fall through to env-var defaults). End-to-end Farmhouse → Castle Ehb playtest is next.

Full status, roadmap, and development journal live on the [**wiki**](https://github.com/codingncaffeine/SiegeFX/wiki).

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
