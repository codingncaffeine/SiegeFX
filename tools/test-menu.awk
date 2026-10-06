# Translates test-all.bat into the menu and test functions of test-all.sh;
# tools/test-menu.sh adds the parameters and helpers around them. The rules
# are cmd's own: %VAR% is expanded (%% is a percent sign), a caret escapes the
# next character outside double quotes and is an ordinary character inside
# them, and spaces outside quotes separate arguments, whose quotes the program
# never sees. Windows paths become Linux ones. A line of any other shape
# stops the translation, so a new construct in test-all.bat is never
# translated by guess. POSIX awk.

BEGIN {
    VARS["DS1"] = "DS1"; VARS["REFS"] = "REFS"; VARS["TOOL"] = "TOOL"; VARS["RUN"] = "RUN"
    VARS["TEMP"] = "TEMP"; VARS["LOCALAPPDATA"] = "LOCALAPPDATA"; VARS["EXITCODE"] = "EXITCODE"
    # cmd's ERRORLEVEL is changed by programs, not by echo or set: every tool
    # and engine call records it, and the tests read it from there.
    VARS["ERRORLEVEL"] = "ERRORLEVEL"
    section = "header"; ind = "    "; failures = 0; ncase = 0
}

function fail(msg) {
    printf "test-menu: test-all.bat line %d: %s\n    %s\n", NR, msg, line > "/dev/stderr"
    failures++
}

# One character as it is written inside a bash double-quoted string.
function dq(c) { return (c == "\\" || c == "\"" || c == "$" || c == "`") ? "\\" c : c }

# cmd text as the inside of a bash double-quoted string. keepq: an echo shows
# its quotes; an argument loses them.
function conv(s, keepq,    out, i, c, inq, j, name) {
    gsub(/%%LOCALAPPDATA%%/, "%LOCALAPPDATA%", s)
    gsub(/\\/, "/", s)
    out = ""; inq = 0
    for (i = 1; i <= length(s); i++) {
        c = substr(s, i, 1)
        if (c == "%") {
            if (substr(s, i + 1, 1) == "%") { out = out "%"; i++; continue }
            j = index(substr(s, i + 1), "%")
            name = substr(s, i + 1, j - 1)
            if (j == 0 || !(name in VARS)) { fail("unknown variable in: " s); return out }
            out = out "${" VARS[name] "}"; i += j
            continue
        }
        if (c == "\"") { inq = !inq; if (keepq) out = out "\\\""; continue }
        if (c == "^" && !inq) { i++; c = substr(s, i, 1) }
        out = out dq(c)
    }
    return out
}

function say(text) { return "printf '%s\\n' \"" conv(text, 1) "\"" }

# Splits a command line into W[1..n]: spaces outside quotes separate words,
# and an unescaped | outside quotes is a word of its own.
function words(s,    n, i, c, inq, w, have) {
    n = 0; w = ""; inq = 0; have = 0
    for (i = 1; i <= length(s); i++) {
        c = substr(s, i, 1)
        if (c == "\"") inq = !inq
        else if (c == "^" && !inq) { w = w c substr(s, i + 1, 1); i++; have = 1; continue }
        else if (!inq && (c == " " || c == "\t")) { if (have) { W[++n] = w; w = ""; have = 0 } continue }
        else if (!inq && c == "|") { if (have) { W[++n] = w; w = ""; have = 0 } W[++n] = "|"; continue }
        w = w c; have = 1
    }
    if (have) W[++n] = w
    return n
}

# An argument as bash writes it: bare when nothing in it is special.
function word(a) { return (a ~ /^[A-Za-z0-9_.\/=:,+@%-]+$/) ? a : "\"" a "\"" }

function command(s,    n, k, out, fixed, pats) {
    n = words(s)
    if (W[1] == "\"%TOOL%\"") { out = "\"$TOOL\""; k = 2 }
    else if (W[1] == "dotnet" && W[2] == "\"%RUN%\"") { out = "\"$RUN\""; k = 3 }
    else { fail("a command other than the tool or the engine"); return "" }
    for (; k <= n; k++) {
        if (W[k] == "2>&1") { out = out " 2>&1"; continue }
        if (W[k] != "|") { out = out " " word(conv(W[k], 0)); continue }
        k++
        if (tolower(W[k]) == "more") { out = out " | more"; continue }
        if (tolower(W[k]) != "findstr") { fail("a pipe into " W[k]); return out }
        fixed = 1; pats = ""
        for (k++; k <= n; k++) {
            if (W[k] == "|") { k--; break }
            if (toupper(W[k]) == "/R") { fixed = 0; continue }
            if (toupper(substr(W[k], 1, 3)) == "/C:") { pats = pats " -e \"" conv(substr(W[k], 4), 0) "\""; continue }
            if (substr(W[k], 1, 1) == "/" || index(W[k], " ")) { fail("findstr argument " W[k]); continue }
            pats = pats " -e \"" conv(W[k], 0) "\""
        }
        out = out " | grep " (fixed ? "-F" : "-E") pats
    }
    return out
}

{ sub(/\r$/, ""); line = $0 }

section == "header" {
    if (line == ":MENU") { section = "menu"; print "menu() {" }
    next
}

section == "menu" {
    if (line == "cls") print ind "clear"
    else if (line == "echo.") print ind "echo"
    else if (substr(line, 1, 5) == "echo ") print ind say(substr(line, 6))
    else if (line == "set /p CHOICE=Choose:") { print "}"; print ""; section = "dispatch" }
    else if (line != "") fail("unknown menu line")
    next
}

section == "dispatch" {
    if (line ~ /^if \/i "%CHOICE%"=="[^"]*" goto [A-Za-z0-9]+$/) {
        split(line, q, "\""); key = tolower(q[4]); label = line; sub(/.* goto /, "", label)
        if (label == "BUILD") label = "build"; else if (label == "END") label = "exit 0"
        else if (label != "T" key) fail("choice " key " goes to " label)
        CASEKEY[++ncase] = key; CASELABEL[ncase] = label
    } else if (line == "goto MENU") section = "between"
    else if (line != "") fail("unknown dispatch line")
    next
}

section == "between" || section == "body" {
    if (skip) {
        if (line == ")") skip = 0
        else if (line != "  echo --- crash log ---" && line != "  type \"%%~F\"" && line != "  echo ------------------")
            fail("unexpected line in the crash-log block")
        next
    }
    if (line ~ /^:T[0-9]+$/) {
        if (section == "body") fail("a test without goto MENU")
        print substr(line, 2) "() {"; section = "body"; DEFINED[substr(line, 2)] = 1; next
    }
    if (line == ":BUILD") { if (section == "body") fail("a test without goto MENU"); section = "tail"; next }
    if (section == "between") { if (line != "") fail("a line outside any test"); next }
    if (line == "") { print ""; next }
    if (line == "goto MENU") { print "}"; section = "between"; next }
    if (line == "echo.") { print ind "echo"; next }
    if (line == "pause") { print ind "pause"; next }
    if (substr(line, 1, 5) == "echo ") { print ind say(substr(line, 6)); next }
    if (substr(line, 1, 4) == "rem ") { print ind "# " substr(line, 5); next }
    if (line ~ /^for %%F in \("%~dp0src\\SiegeFX\.Runtime\\bin\\Release\\[^"]*\\siegefx_crash\.log"\) do if exist "%%~F" \($/) {
        print ind "show_crash_log"; skip = 1; next
    }
    if (substr(line, 1, 4) == "set ") {
        rest = substr(line, 5)
        if (substr(rest, 1, 1) == "\"") { rest = substr(rest, 2); if (!sub(/"$/, "", rest)) fail("an unclosed set") }
        eq = index(rest, "="); name = substr(rest, 1, eq - 1); value = substr(rest, eq + 1)
        if (eq < 2 || name !~ /^[A-Za-z_][A-Za-z_0-9]*$/) fail("an unknown set")
        else if (value == "") print ind "unset " name
        else print ind "export " name "=\"" conv(value, 0) "\""
        next
    }
    if (line ~ /^if errorlevel 1 \(echo .*\) else \(echo .*\)$/) {
        a = substr(line, 23); p = index(a, ") else (echo ")
        print ind "if [ \"$ERRORLEVEL\" -ge 1 ]; then " say(substr(a, 1, p - 1)) "; else " say(substr(a, p + 13, length(a) - p - 13)) "; fi"
        next
    }
    if (line ~ /^if exist "[^"]*" start "" "[^"]*"$/) {
        split(line, q, "\"")
        print ind "if [ -e \"" conv(q[2], 0) "\" ]; then xdg-open \"" conv(q[6], 0) "\" > /dev/null 2>&1 & fi"
        next
    }
    if (substr(line, 1, 8) == "\"%TOOL%\"" || substr(line, 1, 14) == "dotnet \"%RUN%\"") {
        print ind command(line) "; ERRORLEVEL=$?"; next
    }
    fail("a line of unknown shape")
    next
}

END {
    if (section != "tail") fail("the file ended before :BUILD")
    print ""
    print "while true; do"
    print ind "menu"
    print ind "read -rp \"Choose: \" CHOICE || exit 0"
    print ind "case \"${CHOICE,,}\" in"
    for (k = 1; k <= ncase; k++) {
        if (CASELABEL[k] ~ /^T/ && !(CASELABEL[k] in DEFINED)) { line = ""; fail("choice " CASEKEY[k] " has no test") }
        print ind ind CASEKEY[k] ") " CASELABEL[k] " ;;"
    }
    print ind "esac"
    print "done"
    if (failures) exit 1
}
