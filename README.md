# SiegeFX

An open-source reimplementation of **Dungeon Siege** (Gas Powered Games, 2002), written in C# / .NET 8.

The goal is to run Dungeon Siege natively on modern Windows at any resolution, with clean, moddable architecture — the same thing KeeperFX did for Dungeon Keeper 1.

> **SiegeFX requires the original game data.** It ships no copyrighted assets. You must own a copy of Dungeon Siege (GOG, Steam, or original discs) to use it.

## Status

Tank archive listing + extraction works. RAW textures decode to PNG. Renderer not started.

## Roadmap

| Phase | Goal | Ship criterion |
|---|---|---|
| 0 | Bootstrap | Solution scaffolded, repo live |
| 1 | Tank reader | List + extract `.dsres` / `.dsmap` contents |
| 2 | Texture pipeline | `.raw` → PNG in asset browser |
| 3 | Renderer foundation | Silk.NET window, first-person camera |
| 4 | Static meshes | `.sno` terrain + `.asp` mesh on screen |
| 5 | GAS parser | Load hierarchical `.gas` templates / configs |
| 6 | World streaming | Walk across connected SNO nodes |
| 7 | Skeletal animation | Animate `.asp` with `.prs` keyframes |
| 8 | Skrit VM | Interpret `.skrit` gameplay scripts |
| 9+ | Gameplay | Combat, AI, inventory, pathfinding |

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
