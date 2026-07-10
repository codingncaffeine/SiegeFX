# SiegeFX

![SiegeFX](siegeFX_logo.png)

An open-source, clean-room reimplementation of **Dungeon Siege** (Gas Powered Games, 2002) in C# / .NET 11 — loading the original game's data files directly rather than redistributing any of them.

> **SiegeFX requires the original game data.** It ships no copyrighted assets. You must own a copy of Dungeon Siege (GOG, Steam, or original discs) to use it.

## About

SiegeFX loads DS1's `.dsres` tank archives, parses the GAS template language, runs the original Skrit gameplay scripts on a clean VM, and renders ASP meshes with PRS skeletal animation. Spells, leveling, audio, and save/load are wired against the values DS1 ships in `formulas.gas` rather than reinvented.

It is **clean-room**: every claim traces back to headless audits over the bytes the original game ships, and the 2023 leak of the DS1 source is off-limits — you don't need it, just patient tooling. Every claim in the status below is backed by a `siegefx` audit you can run yourself: see [Receipts](https://github.com/codingncaffeine/SiegeFX/wiki/Receipts) on the wiki.

Most of the groundwork is other people's — see [Credits & prior art](#credits--prior-art).

## Current state of development

**Early development.** This is a long-running reverse-engineering project, not finished software — there's still a long way to go before a full Farmhouse → Castle Ehb playthrough. Published builds are **development milestones**, snapshots of the current phase, not a v1.0 candidate.

**Working today:**

- **World & navigation** — every shipped region streams across boundaries; the live nav mesh drives click-to-move / click-to-attack, cross-region descents into cellars, caves, and dungeons, and Sims-style cutaway fades on the layer above you.
- **Party & companions** — conversation-driven recruitment; followers that fight with their own gear and trail you through six authentic DS1 formations; a per-companion character sheet (paper doll + backpack) and the Field Commands panel for orders and formation.
- **Combat & spells** — melee, ranged, and spellcasting enemies with template-driven spawners, pack alerts, and patrol routes; the full authored spell universe firing its own DS1 sfx effects.
- **Presentation & UX** — a DS1-faithful character creator, scripted intro cinematics (a non-interactive-sequence engine + storyteller narration), a rotating compass, a quest journal and HUD tracker, an in-game options menu, in-world vendors, clickable doors, breakable props with authored loot, lossless quicksave/quickload, and streaming mood-driven music.
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
```

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

MIT. See [LICENSE](LICENSE).
