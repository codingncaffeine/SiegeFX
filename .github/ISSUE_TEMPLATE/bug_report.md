---
name: Bug report
about: Something in SiegeFX didn't behave like Dungeon Siege (or crashed)
title: ""
labels: bug
assignees: ""
---

**What happened?**
A clear description of what went wrong.

**What did you expect?**
How the original game behaves here, if you know it.

**Where were you?**
Region / landmark (e.g. "farmlands, bridge by the stream"), and roughly what you were doing.

**Version and system**
The second line of the session log names both, e.g. `[log] SiegeFX 0.4.0+69ed842… on Ubuntu 24.04.1 LTS (linux-x64)`. Without a log, the name of the release file you run.

**Your DS1 data source**
GOG / Steam / original discs.

**Session log**
Please attach the newest file from the logs folder — it records what the engine was doing and usually pins the cause immediately:
- Windows: `%LOCALAPPDATA%\SiegeFX\logs\`
- Linux: `~/.local/share/SiegeFX/logs/`

In game, **Ctrl+F11** writes one zip with the session log, the game state and the newest save into the `bugreports` folder beside `logs`; attaching that covers this section and the next.

**Save file (optional but very helpful)**
From the `Saves` folder beside `logs`, if the problem is reproducible from a save.

**Screenshot (optional)**
For anything visual.
