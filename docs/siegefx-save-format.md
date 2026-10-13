# SiegeFX's save format

SiegeFX writes its own save, not the original game's (that format is in
[original-save-format.md](original-save-format.md)). It is JSON, defined by
`src/SiegeFX.Core/Save/SaveFile.cs` and written by `SaveStore.cs`. This page
summarizes it; the code comments stay the authority on each field.

## Files

| File | Where | Written by |
|---|---|---|
| `<name>-YYYYMMDD_HHMMSS.save` | `%LOCALAPPDATA%\SiegeFX\Saves\` (Windows), `~/.local/share/SiegeFX/Saves/` (Linux) | Save Game window; the stem is the typed name, sanitized |
| `quicksave.save` | same folder | F5 (F9 loads it) |
| `autosave.save` | same folder | Scripted autosave points (`cmd_auto_save`), at most once every 30 s |

Writes are atomic: the JSON goes to `<file>.tmp`, then replaces the old file,
so a crash mid-save leaves the previous save intact. The same JSON is the
multiplayer late-join snapshot (`SaveStore.Serialize`).

## Versions

`SchemaVersion` is 13. Every bump so far only added fields with defaults, so
any v1–v12 file loads as v13 with the new fields empty. Bumping the version
needs the migration whitelist in `SaveStore.Load` extended too.

| Version | Added |
|---|---|
| v2 | quests |
| v3 | quest kill progress |
| v4 | gold |
| v5 | hero name and appearance (character creator choices) |
| v6 | spellbook placed rows |
| v7 | quest talk progress |
| v8 | world pickups already taken |
| v9 | quest pickup progress |
| v10 | world state (bools, counters, chests, levers, doors, …) and quest dialogue log |
| v11 | save display name, Adventurer's Handbook progress |
| v12 | load-menu preview: hero name, map, play time, thumbnail |
| v13 | party (companions) |

## What is saved

| Area | Contents |
|---|---|
| File | display name, time saved, play time, difficulty, map, region, thumbnail (PNG), handbook progress |
| Hero (`Player`) | SCID, XP and level, attributes, skill XP, gold, inventory, spellbook (active slots, cooldowns, placed rows), quests (state, kill/talk/pickup progress, dialogue log), appearance, camera, active ability slot, downed state, summon, world pickups already taken |
| Companions (`Party`) | template, position, life and mana, inventory, equipment, XP, spells, standing orders, follow mode, held position, active slot; dismissed companions keep their bundle so re-inviting restores it |
| Actors (`Actors`) | every actor's SCID, template, position, life, mana, dead/hidden, pinned animation |
| World (`World`) | named bools, accumulators, opened chests, unlocked usables, levers, doors, generators, stairwells, auto-traps, active emitters, broken props, cleared blockers, elevators, every trigger's state (fired/held rows, delayed actions), party formation, hired companions, time of day |
| Other | loot piles on the ground, active spell effects |

## Known gaps

- The hero's Field Commands orders (the `fix/field-commands-hero` branch adds
  `PlayerSnapshot.MoveOrder/AtkOrder/TgtOrder`; the original saves them — see
  the orders table in the original-format doc).
- Saved party groups (Ctrl+number).
- Alignment changes made at runtime (`cmd_alignment_changer`): not stored.
- Which SiegeFX build and which game data wrote the save.
- Kill counters and the per-monster to-hit bonus the original's game auditor
  keeps (`ge_player_kill_*`, `chance_to_hit_bonus_versus_*`).
