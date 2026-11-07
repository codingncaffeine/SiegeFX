SiegeFX for macOS - quick start
===============================

SiegeFX is an open-source, clean-room reimplementation of Dungeon Siege
(2002). It ships no game data: it plays the files of your own copy of
Dungeon Siege (GOG, Steam or the original discs).

This is the self-contained build for Apple Silicon Macs (M1 and later):
the .NET runtime, GLFW and OpenAL Soft are bundled. It is a preview: the
Mac build is new, so please report what breaks.

First run
---------
The build is not notarized by Apple, so macOS blocks a downloaded copy
from starting. Clear the download flag once, from this folder:

    xattr -dr com.apple.quarantine .

Running
-------
From this folder, in Terminal:

    ./SiegeFX

Tell it where your Dungeon Siege folder is (the one that holds
Resources/Logic.dsres) by writing that path on the first line of a file
named ds1path.txt next to SiegeFX, or in

    ~/.config/siegefx/ds1path.txt

or start it with the path in SIEGEFX_DS1:

    SIEGEFX_DS1="$HOME/Games/Dungeon Siege" ./SiegeFX

A Windows install copied over from a PC (or from a GOG/Steam download)
works as it is.

Multiplayer
-----------
Multiplayer > Internet lists the games other players host, on any
system (Windows, Linux or Mac), and HOST GAME lists yours; nobody needs
an IP address. Multiplayer > Network finds games on your own network.
Internet play signs in to Epic anonymously (no account) and Epic's
library keeps that sign-in in your keychain, so macOS may ask to let
SiegeFX use the keychain: allow it.

Each player's co-op character is saved on their own Mac, every half
minute when it changed and when the game closes, and comes back in
every co-op game they host or join.

Where your data lives
---------------------
Saves, settings, screenshots and session logs:
    ~/Library/Application Support/SiegeFX

Reporting a problem
-------------------
Attach the newest session log from
~/Library/Application Support/SiegeFX/logs/ to an issue at
https://github.com/codingncaffeine/SiegeFX/issues - its second line names
the build you ran.

Project: https://github.com/codingncaffeine/SiegeFX
