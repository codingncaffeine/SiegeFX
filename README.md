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

**`v0.15.0`** — playable across the world. Farmboy spawns at the authored start, click-to-move and click-to-attack route through the live nav mesh, equipped weapons render and animate on the hand bone, the HUD draws DS1 bitmap-font text with HP/MP bars derived from STR/DEX/INT, and `Q` / `W` cast slotted spells with 3D positional audio. RMB on an NPC opens a branching dialogue panel; quests journal and persist across saves; vendors stock and trade for gold dropped by enemies. `F5` / `F9` quicksave/quickload restore the world losslessly.

A DS1-faithful character creator built from the shipped `character_select.gas` lets you pick gender / head / face / hair / shirt / pants over a live 3D preview, and the picked hero name + variant axes round-trip through quicksave (schema v5).

### Receipts

The clean-room rule: every claim is backed by a headless `siegefx` audit you can run yourself.

- **81 regions** — all shipped DS1 regions load through the cross-boundary streamer
- **50,680 static-prop placements** — 100% texture via the runtime resolver (`region prop-textures all`)
- **7,252 NPCs / 7,517 texture slots** — every slot resolves (`region actor-coverage all`)
- **2,626 ASP meshes** — 274 multi-subset; the BTRI cornerStart fix landed here (`asp subset-fuzz`)
- **~18,000 hero variants** — 7 bodies × ~32 skins × ~41 pants × 2 genders (`templates hero-variants`)
- **520 mood definitions + 165 SED descriptors** — region ambient beds and per-fire pitch ranges driven by data (`mood list`, `audio sed-list`)
- **626 audio cues** in `Sound.dsres`; 20 wired so far, per-category gaps mapped (`audio coverage`)
- **10 weapon class buckets** (axe / bow / club / hammer / mace / minigun / staff / sword / combat_magic / beastfu) indexed for tier-correct pcontent rolls — `#club/2-3` resolves to a power-2/3 club, never the unique `cb_un_2h_troll_rock` (`pcontent dump`)

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
