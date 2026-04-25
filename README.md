# SiegeFX

![SiegeFX](siegeFX_logo.png)

An open-source reimplementation of **Dungeon Siege** (Gas Powered Games, 2002), written in C# / .NET 8.

The goal is to run Dungeon Siege natively on modern Windows at any resolution, with a clean and moddable architecture — by loading the original game's data files directly rather than redistributing any of them.

> **SiegeFX requires the original game data.** It ships no copyrighted assets. You must own a copy of Dungeon Siege (GOG, Steam, or original discs) to use it.

## About

SiegeFX is a slow-burn reverse-engineering project: every shipped phase ends in something visibly working, and every quirk we hit along the way gets written down. The codebase is C# / .NET 8 with Silk.NET for windowing/GL/audio. It loads `.dsres` tank archives, parses the GAS template language, runs the original Skrit gameplay scripts on a clean VM, and renders ASP meshes with PRS skeletal animation. Spells, leveling, audio, and save/load are all wired against the values DS1 ships in `formulas.gas` rather than reinvented.

Most of the work that made this tractable is other people's: **Guilherme Lampert** documented the asset formats, **Scott Bilas** (GPG's lead engine programmer) wrote up the engine internals publicly, and the **SiegeTheDay** modding community kept the importer scripts alive. The credits at the bottom of this README aren't an afterthought — without that prior art this project wouldn't exist.

## Status

**`v0.14.0`** — Playable. Farmboy spawns in `fh_r1`, click-to-move and click-to-attack route through the live nav mesh, equipped weapons render on the hand bone, the HUD draws DS1 bitmap-font text with HP/MP bars derived from STR/DEX/INT, an 8×5 grid inventory toggles with `I`, the pause menu opens with `Esc`, `Q` and `W` cast the slotted spells, audio plays positionally, and `F5` / `F9` quicksave/quickload restore the world losslessly.

181 NPCs wander the region on the live nav mesh. Spells, leveling, audio, and save/load are all in.

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
