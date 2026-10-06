# SiegeFX

![SiegeFX](siegeFX_logo.png)

An open-source, clean-room reimplementation of **Dungeon Siege** (Gas Powered Games, 2002) in C# / .NET 10, for Windows and Linux — loading the original game's data files directly rather than redistributing any of them.

> **SiegeFX requires the original game data.** It ships no copyrighted assets. You must own a copy of Dungeon Siege (GOG, Steam, or original discs) to use it.

## About

SiegeFX loads DS1's `.dsres` tank archives, parses the GAS template language, runs the original Skrit gameplay scripts on a clean VM, and renders ASP meshes with PRS skeletal animation. Spells, leveling, audio, and save/load are wired against the values DS1 ships in `formulas.gas` rather than reinvented.

It is **clean-room**: every claim traces back to headless audits over the bytes the original game ships, and the 2023 leak of the DS1 source is off-limits — you don't need it, just patient tooling. Every claim in the status below is backed by a `siegefx` audit you can run yourself: see [Receipts](https://github.com/codingncaffeine/SiegeFX/wiki/Receipts) on the wiki.

Most of the groundwork is other people's — see [Credits & prior art](#credits--prior-art).

## Current state of development

**Latest alpha: [v0.5.0](https://github.com/codingncaffeine/SiegeFX/releases/tag/v0.5.0)**, self-contained builds for Windows x64 and Linux x64; every earlier build, back to the first playable v0.0.1, is on the [Releases](https://github.com/codingncaffeine/SiegeFX/releases) page. On Windows, unzip it and run `SiegeFX.exe`; on Linux, unpack the tarball and run `./SiegeFX`, or install the AUR package `siegefx-bin` on Arch. GOG and Steam installs are found by themselves (on Linux also through Heroic, Lutris, Bottles and Wine prefixes), or set `SIEGEFX_DS1` to your Dungeon Siege folder. Please report what breaks via GitHub issues.

*Versioning note:* earlier tags tracked internal engine milestones and outpaced the project's playable maturity; the scheme was reset at the first playable build and now tracks progress toward **1.0 = a complete Farmhouse → Castle Ehb campaign**. The retired milestone notes live on the wiki's [Release Archive](https://github.com/codingncaffeine/SiegeFX/wiki/Release-Archive).

**Alpha software.** This is a long-running reverse-engineering project, not finished software. The world's interactive mechanics are function-complete by audit, but the point of the alpha is finding out what real playthroughs hit.

**Working today:**

- **World & navigation** — every shipped region streams across boundaries; the live nav mesh drives click-to-move / click-to-attack, cross-region descents into cellars, caves, and dungeons, and Sims-style cutaway fades on the layer above you.
- **Party & companions** — conversation-driven recruitment; followers that fight with their own gear and trail you through six authentic DS1 formations; a per-companion character sheet (paper doll + backpack) and the Field Commands panel for orders and formation.
- **Combat & spells** — melee, ranged, and spellcasting enemies with template-driven spawners, pack alerts, and patrol routes; the full authored spell universe firing its own DS1 sfx effects.
- **Presentation & UX** — a DS1-faithful character creator, scripted intro cinematics (a non-interactive-sequence engine + storyteller narration), a rotating compass, a quest journal and HUD tracker, in-world vendors with retail store chrome, clickable doors, breakable props with authored loot, lossless quicksave/quickload, and streaming mood-driven music.
- **Options & comfort** — a fully wired options menu in authentic DS1 chrome (resolution, fullscreen/windowed with remembered window size, shadows, texture filtering, gamma, object detail) plus a modern Advanced tab (VSync, frame cap, anisotropy, MSAA, point-light budget, UI scale); Shift+drag HUD rearrangement with snapping; everything persists between sessions.
- **Weather & atmosphere** — a world clock that runs the authored day: the sun takes the hourly colours DS1 ships, and night-only life such as the crickets sounds only after dark; and the full mood system: per-location scripted rain and snow (the opening-farmland storm, the Glacern blizzards) with authored densities that drift like retail, linear mood fog on every region, wind-sheared precipitation, lightning with thunder, and the placed sound-emitter layer (trigger-activated rain loops, wind beds, waterwheels).
- **World mechanics** — the moving-node elevator system (216 lifts across 32 regions, lever- and stand-activated, riding the party between floors); openable chests and trapped containers; life/mana shrines that heal and revive; scripted progression gates (stuck doors that open on quest events, key-locked mechanisms, message-broken rubble); the boolean/counter logic-gizmo network quests gate on; and a campaign-wide completability audit whose "unhandled component" table now reads empty across all 81 regions.

**Under construction:** since v0.4.0 the campaign can be played through to the final boss; the work now is making every part of it behave as the original does, measured row by row on the wiki's [Parity](https://github.com/codingncaffeine/SiegeFX/wiki/Parity) page. Still open there: the intro and credits movies (Bink), interior lighting fidelity, and combat balance.

The full per-phase development log, roadmap, and what's queued live on the [**wiki**](https://github.com/codingncaffeine/SiegeFX/wiki) — start at [Status](https://github.com/codingncaffeine/SiegeFX/wiki/Status), [Architecture](https://github.com/codingncaffeine/SiegeFX/wiki/Architecture), [Building and Running](https://github.com/codingncaffeine/SiegeFX/wiki/Building-and-Running), or [Engine Quirks and Stumbles](https://github.com/codingncaffeine/SiegeFX/wiki/Engine-Quirks-and-Stumbles).

## Project layout

```
src/
  SiegeFX.Core       class library: file formats, gameplay rules, the parity audits; no UI
  SiegeFX.Audio      OpenAL Soft audio engine and streaming music
  SiegeFX.Runtime    the game (Silk.NET: GLFW + OpenGL 3.3)
  SiegeFX.Tools      the `siegefx` command line: format tools and the headless audits
  SiegeFX.Net.Eos    Epic Online Services multiplayer (needs Epic's SDK; not in the solution)
  SiegeSmith         modding studio & world builder on the same parsers (WPF, Windows)
tests/               data-free unit tests (CI runs them on Linux and Windows)
tools/               receipts.sh (the pre-push gate), the test-menu generator, ds1/ (the original game under Proton)
packaging/           Linux desktop entry, smoke test, AUR package
```

## SiegeSmith

<img src="src/SiegeSmith/Assets/logo.png" alt="SiegeSmith" width="220"/>

The repository also ships **SiegeSmith** — the modding studio and world builder built on the same parsers as the engine. One tool covers the whole loop: browse and view every DS1 format (textures, models, **animations with live playback**, scripts with compile diagnostics), edit GAS with live validation, package and install mods — and a full **World Builder** that goes from door-stitched terrain through scatter brushes, patrol routes, triggers, dialogue, quests, weather, and custom imported art, to a one-click playable `.dsmap`. You can mod Dungeon Siege with it, or build an entirely new game.

**Start here: the [SiegeSmith wiki section](https://github.com/codingncaffeine/SiegeFX/wiki/SiegeSmith)** — with the full [World Builder Guide](https://github.com/codingncaffeine/SiegeFX/wiki/SiegeSmith-World-Builder-Guide), the [Making a Game from Scratch](https://github.com/codingncaffeine/SiegeFX/wiki/SiegeSmith-Making-a-Game) walkthrough, and the [Modder's Guide](https://github.com/codingncaffeine/SiegeFX/wiki/SiegeSmith-Modders-Guide).

## Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`global.json` pins the band) and your own Dungeon Siege install. The whole solution builds on Linux and on Windows:

```
dotnet build SiegeFX.sln -c Release
```

(or `./build-release.sh` / `build-release.bat`). Then run the game:

- Linux: `src/SiegeFX.Runtime/bin/Release/net10.0/SiegeFX`
- Windows: `src\SiegeFX.Runtime\bin\Release\net10.0-windows10.0.22621.0\SiegeFX.exe` (the Windows build adds the in-game video recorder)

SiegeFX finds a GOG or Steam install by itself: on Windows through the registry, on Linux where Steam (native, Flatpak or Snap), Heroic, Lutris, Bottles or a Wine prefix put it. Otherwise write your Dungeon Siege folder on the first line of `~/.config/siegefx/ds1path.txt` (Windows: `%APPDATA%\siegefx\ds1path.txt`), or set `SIEGEFX_DS1`. On Linux the game needs OpenGL 3.3 and, for multiplayer, OpenSSL 3; GLFW and OpenAL Soft come with the build.

`./publish.sh vX.Y.Z` builds the release artifacts on Linux: the Linux tarball (self-contained, with a desktop entry), the Windows game zip and the SiegeSmith zip. `publish-alpha.bat` builds the Windows two on Windows.

See [Building and Running](https://github.com/codingncaffeine/SiegeFX/wiki/Building-and-Running) for controls and more.

## Checks

- **CI** builds the solution on Linux and Windows with warnings as errors, runs the unit tests, builds all three release artifacts and launches the Linux one, on every push.
- **`tools/receipts.sh`** is the gate before a push; it needs a game install: the build, the unit tests, the engine's self-tests, the [parity ledger](https://github.com/codingncaffeine/SiegeFX/wiki/Parity) against its committed baseline, and every spell's effect trace against `goldens/sfx-timelines`.
- **`siegefx parity ledger <install>`** measures how much of the original content the engine handles (commands, quests, sounds, template fields, effects…) and lists each gap.
- **`test-all.bat` / `test-all.sh`**: the phase-by-phase test menu, one entry per feature. Edit the .bat; `tools/test-menu.sh` generates the .sh from it.

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
