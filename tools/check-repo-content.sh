#!/usr/bin/env bash
# tools/check-repo-content.sh [repo]
#
# SiegeFX ships none of the original game: players point it at their own
# install. This fails when a tracked file looks like game content, so a slip
# is caught before it reaches GitHub:
#   - a game format by extension (tanks, maps, saves, textures, meshes,
#     animations, terrain, scripts, gas data, movies, audio);
#   - a game binary format by its leading bytes, however it is named;
#   - a binary file over 5 MB (source text is never held to the size limit).
# Original work that happens to use one of these formats is listed, one path
# per line, in tools/check-repo-content.allow ('#' starts a comment).
# CI and tools/receipts.sh both run it. Exit 0 when nothing is found.
set -uo pipefail
repo=${1:-$(cd "$(dirname "$0")/.." && pwd)}
allow_file="$repo/tools/check-repo-content.allow"
max_bytes=$((5 * 1024 * 1024))

declare -A allow=()
if [ -f "$allow_file" ]; then
    while IFS= read -r line; do
        line=${line%%#*}; line=${line%"${line##*[![:space:]]}"}
        [ -n "$line" ] && allow[$line]=1
    done < "$allow_file"
fi

found=0
flag() { echo "game content?  $1  ($2)"; found=1; }

# --eol prints "i/-text" for files git treats as binary; the path follows a tab.
while IFS=$'\t' read -r info path; do
    [ -n "${allow[$path]:-}" ] && continue
    full="$repo/$path"
    [ -f "$full" ] || continue   # deleted in the working tree
    ext=${path##*.}; [ "$ext" = "$path" ] && ext=""
    case "${ext,,}" in
        dsres|dsmap|dsmod) flag "$path" "a game archive (.$ext)"; continue ;;
        dssave|dsasave|dsparty) flag "$path" "an original-game save (.$ext)"; continue ;;
        raw|asp|prs|sno|skrit|gas) flag "$path" "a game data file (.$ext)"; continue ;;
        bik|smk) flag "$path" "a game movie (.$ext)"; continue ;;
        mp3|wav|ogg) flag "$path" "audio (.$ext)"; continue ;;
    esac
    case $(head -c 8 -- "$full" | tr -c '[:alnum:]' '.') in
        DSigTank|DSg2Tank) flag "$path" "a game archive, by its signature"; continue ;;
        ipaR*) flag "$path" "a game texture, by its signature"; continue ;;
        SNOD*) flag "$path" "a game terrain node, by its signature"; continue ;;
        ANIM*) flag "$path" "a game animation, by its signature"; continue ;;
        BMSH*) flag "$path" "a game mesh, by its signature"; continue ;;
    esac
    if [[ $info == i/-text* ]]; then
        size=$(stat -c %s -- "$full")
        [ "$size" -gt "$max_bytes" ] && flag "$path" "binary, $((size / 1048576)).$((size % 1048576 * 10 / 1048576)) MB, over the 5 MB limit"
    fi
done < <(git -C "$repo" -c core.quotepath=off ls-files --eol | sed 's/^\([^ ]*\) .*\t/\1\t/')

if [ $found -eq 0 ]; then
    echo "check-repo-content: no game content among $(git -C "$repo" ls-files | wc -l) tracked files"
else
    echo "check-repo-content: remove the files above, or list original work in tools/check-repo-content.allow" >&2
fi
exit $found
