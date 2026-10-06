SiegeFX for Linux - quick start
===============================

SiegeFX is an open-source, clean-room reimplementation of Dungeon Siege
(2002). It ships no game data: it plays the files of your own copy of
Dungeon Siege (GOG, Steam or the original discs).

This is the self-contained Linux build: the .NET runtime, GLFW and OpenAL
Soft are bundled, and a system OpenAL is used instead when one is
installed. It needs a 64-bit distribution with glibc 2.34 or newer
(Ubuntu 22.04 and Debian 12 or later, Arch), OpenGL 3.3, and OpenSSL 3
for multiplayer (libssl3 on Debian and Ubuntu; every desktop install has
it).

Running
-------
From this folder:

    ./SiegeFX

SiegeFX looks for the game where Steam (native, Flatpak or Snap), Heroic,
Lutris, Bottles or a plain Wine prefix installed it. If it does not find
yours, write the path of your Dungeon Siege folder (the one that holds
Resources/Logic.dsres) on the first line of

    ~/.config/siegefx/ds1path.txt

or start it with the path in SIEGEFX_DS1:

    SIEGEFX_DS1="$HOME/Games/Dungeon Siege" ./SiegeFX

Adding it to your applications menu
-----------------------------------
From this folder:

    mkdir -p ~/.local/share/applications
    sed "s|^Exec=.*|Exec=\"$PWD/SiegeFX\"|" share/applications/siegefx.desktop \
        > ~/.local/share/applications/siegefx.desktop
    cp -r share/icons ~/.local/share/

On Arch Linux the AUR package siegefx-bin installs it for every user.

Where your data lives
---------------------
Saves, settings, screenshots and session logs:
    ~/.local/share/SiegeFX   (or $XDG_DATA_HOME/SiegeFX)

Reporting a problem
-------------------
Attach the newest session log from ~/.local/share/SiegeFX/logs/ to an
issue at https://github.com/codingncaffeine/SiegeFX/issues - its second
line names the build you ran.

Project: https://github.com/codingncaffeine/SiegeFX
Licensed under the GNU GPL v3 (see LICENSE). The bundled components and
their licenses are listed in THIRD-PARTY-NOTICES.txt.
