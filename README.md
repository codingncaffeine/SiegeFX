# SiegeFX

An open-source reimplementation of **Dungeon Siege** (Gas Powered Games, 2002), written in C# / .NET 8.

The goal is to run Dungeon Siege natively on modern Windows at any resolution, with clean, moddable architecture — the same thing KeeperFX did for Dungeon Keeper 1.

> **SiegeFX requires the original game data.** It ships no copyrighted assets. You must own a copy of Dungeon Siege (GOG, Steam, or original discs) to use it.

## Status

**v0.8.0** — Skrit VM drives rigged actors. You can fly a first-person camera across streamed SNO regions, view skeletal `.asp` meshes animating from `.prs` clips, and run shipped `.skrit` scripts against a live rig via `--skrit-anim`. Actor system, combat, UI, and gameplay are next.

## Roadmap

| Phase | Goal | Ship criterion | Status |
|---|---|---|---|
| 0 | Bootstrap | Solution scaffolded, repo live | ✅ |
| 1 | Tank reader | List + extract `.dsres` / `.dsmap` contents | ✅ |
| 2 | Texture pipeline | `.raw` → PNG in asset browser | ✅ |
| 3 | Renderer foundation | Silk.NET window, first-person camera | ✅ |
| 4 | Static meshes | `.sno` terrain + `.asp` mesh on screen | ✅ |
| 5 | GAS parser | Load hierarchical `.gas` templates / configs | ✅ |
| 6 | World streaming | Walk across connected SNO nodes | ✅ |
| 7 | Skeletal animation | Animate `.asp` with `.prs` keyframes | ✅ |
| 8 | Skrit VM | Interpret `.skrit` gameplay scripts | ✅ |
| 9a | Skrit-driven viewer | `--skrit-anim` runs shipped scripts against a rig | ✅ (v0.8.0) |
| 10 | Actor system + GAS spawn | Walk into `fh_r1` and see goblins idling per-skrit | → v0.9.0 |
| 11 | Pathfinding + nav | Actors path around obstacles on SNO walkable surfaces | |
| 12 | Combat + stats | Melee kills goblin; corpse drops loot pile | → v0.10.0 |
| 13 | Player character + input | Playable Farmboy engaging goblins (RMB target / LMB move) | → v0.11.0 |
| 14 | Inventory + items | Grid inventory, equip slots, pickup from drops | → v0.12.0 |
| 15 | UI / HUD | Bitmap font, HUD, inventory/options/main-menu screens | → v0.13.0 |
| 16 | Spells + effect scripts | Fire-bolt casts, hits goblin for spell damage | → v0.14.0 |
| 17 | Audio | 3D positional SFX, music streaming, anim-event hooks | → v0.15.0 |
| 18 | Save / load | Quit-and-resume in mid-dungeon | → v0.16.0 |
| 19 | NPCs / dialogue / quests | Talk to Edward, accept + complete opening quest | → v0.17.0 |
| 20 | Content integration + polish | Farmhouse → Castle Ehb main-quest end-to-end | → v1.0 |

Deferred past v1.0: multiplayer, Legends of Aranna, mod loader, launcher integration.

## Project layout

```
src/
  SiegeFX.Core       class library — file format parsers, no UI
  SiegeFX.Tools      unified siegefx CLI (tank info/list/extract, raw info/decode)
  SiegeFX.Browser    WPF asset explorer
  SiegeFX.Runtime    game / engine host (Silk.NET)
```

## Build

```
dotnet build
```

## Credits & prior art

- **Guilherme Lampert** — [reverse-engineering-dungeon-siege](https://github.com/glampert/reverse-engineering-dungeon-siege) — canonical documentation of Tank, ASP, SNO, RAW formats (MIT). Most of SiegeFX.Core is a port of his work.
- **Scott Bilas** — GPG's lead engine programmer, whose public writings document the Siege engine internals.
- **OpenSiege** — [github.com/OpenSiege/OpenSiege](https://github.com/OpenSiege/OpenSiege) — earlier C++/OpenSceneGraph reimplementation attempt.
- **SiegeTheDay.org** — DS1 modding community and original source of the MaxScript importers we lean on for `.prs` animation.

## License

MIT. See [LICENSE](LICENSE).
