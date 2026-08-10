# SiegeFX

![SiegeFX](siegeFX_logo.png)

An open-source, clean-room reimplementation of **Dungeon Siege** (Gas Powered Games, 2002) in C# / .NET 11 — loading the original game's data files directly rather than redistributing any of them.

> **SiegeFX requires the original game data.** It ships no copyrighted assets. You must own a copy of Dungeon Siege (GOG, Steam, or original discs) to use it.

## About

SiegeFX loads DS1's `.dsres` tank archives, parses the GAS template language, runs the original Skrit gameplay scripts on a clean VM, and renders ASP meshes with PRS skeletal animation. Spells, leveling, audio, and save/load are wired against the values DS1 ships in `formulas.gas` rather than reinvented.

It is **clean-room**: every claim traces back to headless audits over the bytes the original game ships, and the 2023 leak of the DS1 source is off-limits — you don't need it, just patient tooling. Every claim in the status below is backed by a `siegefx` audit you can run yourself: see [Receipts](https://github.com/codingncaffeine/SiegeFX/wiki/Receipts) on the wiki.

Most of the groundwork is other people's — see [Credits & prior art](#credits--prior-art).

## Current state of development

**First playable alpha available:** [**v0.0.1**](https://github.com/codingncaffeine/SiegeFX/releases/tag/v0.0.1) — a self-contained Windows x64 build. Unzip, run `SiegeFX.Runtime.exe` (standard GOG/Steam/Microsoft installs are auto-detected; otherwise set `SIEGEFX_DS1` to your Dungeon Siege folder), and see how far you can take the campaign. Please report what breaks via GitHub issues.

*Versioning note:* earlier tags tracked internal engine milestones and outpaced the project's playable maturity; the scheme was reset at the first playable build and now tracks progress toward **1.0 = a complete Farmhouse → Castle Ehb campaign**. The retired milestone notes live on the wiki's [Release Archive](https://github.com/codingncaffeine/SiegeFX/wiki/Release-Archive).

**Alpha software.** This is a long-running reverse-engineering project, not finished software. The world's interactive mechanics are function-complete by audit, but the point of the alpha is finding out what real playthroughs hit.

**Working today:**

- **World & navigation** — every shipped region streams across boundaries; the live nav mesh drives click-to-move / click-to-attack, cross-region descents into cellars, caves, and dungeons, and Sims-style cutaway fades on the layer above you.
- **Party & companions** — conversation-driven recruitment; followers that fight with their own gear and trail you through six authentic DS1 formations; a per-companion character sheet (paper doll + backpack) and the Field Commands panel for orders and formation.
- **Combat & spells** — melee, ranged, and spellcasting enemies with template-driven spawners, pack alerts, and patrol routes; the full authored spell universe firing its own DS1 sfx effects.
- **Presentation & UX** — a DS1-faithful character creator, scripted intro cinematics (a non-interactive-sequence engine + storyteller narration), a rotating compass, a quest journal and HUD tracker, in-world vendors with retail store chrome, clickable doors, breakable props with authored loot, lossless quicksave/quickload, and streaming mood-driven music.
- **Options & comfort** — a fully wired options menu in authentic DS1 chrome (resolution, fullscreen/windowed with remembered window size, shadows, texture filtering, gamma, object detail) plus a modern Advanced tab (VSync, frame cap, anisotropy, MSAA, point-light budget, UI scale); Shift+drag HUD rearrangement with snapping; everything persists between sessions.
- **Weather & atmosphere** — the full mood system: per-location scripted rain and snow (the opening-farmland storm, the Glacern blizzards) with authored densities that drift like retail, linear mood fog on every region, wind-sheared precipitation, lightning with thunder, and the placed sound-emitter layer (trigger-activated rain loops, wind beds, waterwheels).
- **World mechanics** — the moving-node elevator system (216 lifts across 32 regions, lever- and stand-activated, riding the party between floors); openable chests and trapped containers; life/mana shrines that heal and revive; scripted progression gates (stuck doors that open on quest events, key-locked mechanisms, message-broken rubble); the boolean/counter logic-gizmo network quests gate on; and a campaign-wide completability audit whose "unhandled component" table now reads empty across all 81 regions.

**Under construction:** the end-to-end campaign, time-of-day (mood `[sun]` tables + hour-gated cricket emitters are parsed but parked), interior lighting fidelity, level-up feedback, and combat balance.

The full per-phase development log, roadmap, and what's queued live on the [**wiki**](https://github.com/codingncaffeine/SiegeFX/wiki) — start at [Status](https://github.com/codingncaffeine/SiegeFX/wiki/Status), [Architecture](https://github.com/codingncaffeine/SiegeFX/wiki/Architecture), [Building and Running](https://github.com/codingncaffeine/SiegeFX/wiki/Building-and-Running), or [Engine Quirks and Stumbles](https://github.com/codingncaffeine/SiegeFX/wiki/Engine-Quirks-and-Stumbles).

## Project layout

```
src/
  SiegeFX.Core       class library — file format parsers, no UI
  SiegeFX.Tools      unified `siegefx` CLI (tank info/list/extract, raw info/decode)
  SiegeFX.Browser    WPF asset explorer
  SiegeFX.Runtime    game / engine host (Silk.NET)
  SiegeSmith         modding studio & world builder built on the same parsers
```

## SiegeSmith

<img src="src/SiegeSmith/Assets/logo.png" alt="SiegeSmith" width="220"/>

The repository also ships **SiegeSmith** — the modding studio and world builder built on the same parsers as the engine. One tool covers the whole loop: browse and view every DS1 format (textures, models, **animations with live playback**, scripts with compile diagnostics), edit GAS with live validation, package and install mods — and a full **World Builder** that goes from door-stitched terrain through scatter brushes, patrol routes, triggers, dialogue, quests, weather, and custom imported art, to a one-click playable `.dsmap`. You can mod Dungeon Siege with it, or build an entirely new game.

**Start here: the [SiegeSmith wiki section](https://github.com/codingncaffeine/SiegeFX/wiki/SiegeSmith)** — with the full [World Builder Guide](https://github.com/codingncaffeine/SiegeFX/wiki/SiegeSmith-World-Builder-Guide), the [Making a Game from Scratch](https://github.com/codingncaffeine/SiegeFX/wiki/SiegeSmith-Making-a-Game) walkthrough, and the [Modder's Guide](https://github.com/codingncaffeine/SiegeFX/wiki/SiegeSmith-Modders-Guide).

## Build

```
dotnet build
```

Then from `src/SiegeFX.Runtime`:

```
dotnet run -c Release
```

See [Building and Running](https://github.com/codingncaffeine/SiegeFX/wiki/Building-and-Running) for game-data setup and controls.

## Credits & prior art

- **Guilherme Lampert** — [reverse-engineering-dungeon-siege](https://github.com/glampert/reverse-engineering-dungeon-siege) — canonical documentation of Tank, ASP, SNO, RAW formats (MIT). Most of `SiegeFX.Core` is a port of his work.
- **Scott Bilas** — GPG's lead engine programmer, whose public writings document the Siege engine internals.
- **OpenSiege** — [github.com/OpenSiege/OpenSiege](https://github.com/OpenSiege/OpenSiege) — earlier C++/OpenSceneGraph reimplementation attempt.
- **SiegeTheDay.org** — DS1 modding community and original source of the MaxScript importers we lean on for `.prs` animation.

## License

SiegeFX is licensed under the [GNU General Public License v3.0](LICENSE) (`GPL-3.0-only`).

## Third-party software

SiegeFX's multiplayer is built on the **Epic Online Services (EOS) SDK** — © Epic Games, Inc., used under the [Epic Online Services terms](https://onlineservices.epicgames.com/en-US/services/terms/agreements). Epic, Epic Games, Epic Online Services, and their respective logos are trademarks or registered trademarks of Epic Games, Inc. in the United States and elsewhere. SiegeFX is not affiliated with, endorsed by, or sponsored by Epic Games.

The EOS SDK bundles a number of open-source components; their license notices are reproduced verbatim in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt), which also ships alongside each release build.
