using SiegeFX.Runtime.Render;

// Crash logger — dumps any unhandled exception (including ones the CLR would
// otherwise report via its own stderr banner) to siegefx_crash.log next to the
// DLL. test-all.bat's T23 prints this file after the process exits so the user
// doesn't lose the stack trace when the console window closes.
var crashLogPath = System.IO.Path.Combine(AppContext.BaseDirectory, "siegefx_crash.log");
try { System.IO.File.Delete(crashLogPath); } catch { }
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try
    {
        System.IO.File.WriteAllText(crashLogPath,
            "UnhandledException at " + DateTime.Now.ToString("o") + Environment.NewLine +
            (e.ExceptionObject?.ToString() ?? "<no exception object>") + Environment.NewLine);
    }
    catch { }
};

// Console tee — ALWAYS mirror Console.Out into a rotating session log at
// %LOCALAPPDATA%\SiegeFX\logs\session-<stamp>.log (alpha-plan Piece 3; until
// now a closed window took its console output with it, so live bug reports
// had no evidence trail). Newest 8 sessions are kept. SIEGEFX_DEBUG_LOG_FILE
// still overrides the target path for directed diagnosis. Auto-flush on every
// write so a forced quit still leaves a valid log.
var teePath = System.Environment.GetEnvironmentVariable("SIEGEFX_DEBUG_LOG_FILE");
if (string.IsNullOrWhiteSpace(teePath))
{
    var logDir = System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "SiegeFX", "logs");
    teePath = System.IO.Path.Combine(logDir, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    try
    {
        System.IO.Directory.CreateDirectory(logDir);
        foreach (var f in new System.IO.DirectoryInfo(logDir).GetFiles("session-*.log")
                     .OrderByDescending(f => f.LastWriteTimeUtc).Skip(7))
        {
            try { f.Delete(); } catch { }
        }
    }
    catch { }
}
try
{
    var teeDir = System.IO.Path.GetDirectoryName(teePath);
    if (!string.IsNullOrEmpty(teeDir)) System.IO.Directory.CreateDirectory(teeDir);
    var fs = new System.IO.FileStream(teePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read);
    var fileWriter = new System.IO.StreamWriter(fs) { AutoFlush = true };
    Console.SetOut(new SiegeFX.Runtime.TeeTextWriter(Console.Out, fileWriter));
    // ALPHA-PACKAGING — the bug-snapshot hotkey (F11) bundles this file.
    SiegeFX.Runtime.Render.RenderHost.SessionLogPath = teePath;
    Console.WriteLine($"[log] session log: {teePath}");
}
catch (Exception ex) { Console.WriteLine($"  tee log: failed to open '{teePath}': {ex.Message}"); }

// Invocation shapes:
//   SiegeFX.Runtime                                          → boot to main menu (Phase 24)
//   SiegeFX.Runtime [mesh.sno|mesh.asp] [texture.raw | tank.dsres]
//   SiegeFX.Runtime --region <map-tank> <terrain-tank> <region-path>
//   SiegeFX.Runtime --world  <map-tank> <terrain-tank> [root-region]
//   SiegeFX.Runtime --anim   <rigged.asp> <clip.prs> [texture.raw]
//   SiegeFX.Runtime --skrit-anim <rigged.asp> <skrit> <clip0.prs> [clip1.prs ...] [--texture <raw>]
//   SiegeFX.Runtime --play-region <map-tank> <terrain-tank> <logic-tank> <objects-tank> <region-path>
//
// Phase 24-MAINMENU step 1+2 — when invoked with no positional args (or with
// only the top-level --diag / --noVideo flags), the runtime enters boot mode:
// resolves a DS1 install via env var SIEGEFX_DS1 or a list of common paths,
// opens Logic.dsres + Objects.dsres, and runs the splash → main menu sequence.
// All --<verb> shapes above remain available for dev use; test-all.bat keeps
// working unchanged.
string? meshPath = null;
string? texturePath = null;
string? regionMap = null;
string? regionTerrain = null;
string? regionPath = null;
string? worldMap = null;
string? worldTerrain = null;
string? worldRoot = null;
string? animAsp = null;
string? animPrs = null;
string? animTexture = null;
string? skritPath = null;
List<string>? skritClips = null;
string? playLogic = null;
string? playObjects = null;
bool diagMode = false;
bool noVideo = false;
bool bootMode = false;
string? ds1Resources = null;

// Phase 21b-1 — `--diag` is a top-level flag that pairs with any other
// invocation. It enables: per-stage Stopwatch timing inside OnLoad and a
// rolling frame-time histogram printed once per second. We strip it from
// argv before the per-mode parser runs so existing positional layouts
// (--region MAP TERRAIN PATH, etc.) keep working without `--diag` shifting
// indices.
{
    var filtered = new List<string>(args.Length);
    foreach (var a in args)
    {
        if (string.Equals(a, "--diag", StringComparison.OrdinalIgnoreCase)) diagMode = true;
        // Phase 24-MAINMENU — DS1's nointro=true equivalent. Skips the
        // Microsoft + GPG splashes and the logo drop, going straight to
        // the main menu state. Pairs cleanly with boot mode but is also
        // honored on --play-region paths that route through the frontend.
        else if (string.Equals(a, "--noVideo", StringComparison.OrdinalIgnoreCase)) noVideo = true;
        else filtered.Add(a);
    }
    args = filtered.ToArray();
}

if (args.Length >= 1 && args[0] == "--region")
{
    if (args.Length < 4)
    {
        Console.Error.WriteLine("usage: SiegeFX.Runtime --region <map-tank> <terrain-tank> <region-path>");
        return 1;
    }
    regionMap = args[1];
    regionTerrain = args[2];
    regionPath = args[3];
}
else if (args.Length >= 1 && args[0] == "--world")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: SiegeFX.Runtime --world <map-tank> <terrain-tank> [root-region]");
        return 1;
    }
    worldMap = args[1];
    worldTerrain = args[2];
    worldRoot = args.Length >= 4 ? args[3] : null;
}
else if (args.Length >= 1 && args[0] == "--anim")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: SiegeFX.Runtime --anim <rigged.asp> <clip.prs> [texture.raw]");
        return 1;
    }
    animAsp = args[1];
    animPrs = args[2];
    animTexture = args.Length >= 4 ? args[3] : null;
}
else if (args.Length >= 1 && args[0] == "--play-region")
{
    // Phase 10e. Full region scene: terrain + every shipped actor placed at its gas-authored
    // position, each driven by its own skrit. Same map+terrain tanks as --region; adds
    // logic + objects tanks so template + skrit + model + clip resolution can find assets.
    if (args.Length < 6)
    {
        Console.Error.WriteLine("usage: SiegeFX.Runtime --play-region <map-tank> <terrain-tank> <logic-tank> <objects-tank> <region-path>");
        return 1;
    }
    regionMap     = args[1];
    regionTerrain = args[2];
    playLogic     = args[3];
    playObjects   = args[4];
    regionPath    = args[5];
}
else if (args.Length >= 1 && args[0] == "--sfx-filmstrip")
{
    // Phase 23c — offscreen spell filmstrip renders. Hidden window, real
    // ParticleSystem, FBO readback to PNG strips + one contact sheet.
    // usage: --sfx-filmstrip <Logic.dsres> <Objects.dsres> <spell|--all>
    //        [--out=DIR] [--frames=N] [--strip=N] [--seed=N] [--size=N]
    if (args.Length < 4)
    {
        Console.Error.WriteLine("usage: SiegeFX.Runtime --sfx-filmstrip <Logic.dsres> <Objects.dsres> <spell|--all> [--out=DIR] [--frames=N] [--strip=N] [--seed=N] [--size=N]");
        return 1;
    }
    string fsOut = System.IO.Path.Combine("goldens", "sfx-filmstrips");
    int fsFrames = 40, fsStrip = 8, fsSeed = 1, fsSize = 256;
    float fsTargetDist = 4f;   // caster→target distance; longer exposes moving-projectile trails
    for (int i = 4; i < args.Length; i++)
    {
        if (args[i].StartsWith("--out=", StringComparison.Ordinal)) fsOut = args[i]["--out=".Length..];
        else if (args[i].StartsWith("--frames=", StringComparison.Ordinal) && int.TryParse(args[i]["--frames=".Length..], out var v1)) fsFrames = v1;
        else if (args[i].StartsWith("--strip=", StringComparison.Ordinal) && int.TryParse(args[i]["--strip=".Length..], out var v2)) fsStrip = v2;
        else if (args[i].StartsWith("--seed=", StringComparison.Ordinal) && int.TryParse(args[i]["--seed=".Length..], out var v3)) fsSeed = v3;
        else if (args[i].StartsWith("--size=", StringComparison.Ordinal) && int.TryParse(args[i]["--size=".Length..], out var v4)) fsSize = v4;
        else if (args[i].StartsWith("--target-dist=", StringComparison.Ordinal) && float.TryParse(args[i]["--target-dist=".Length..], System.Globalization.CultureInfo.InvariantCulture, out var v5)) fsTargetDist = v5;
    }
    return SfxFilmstripHost.Run(args[1], args[2], args[3], fsOut, fsFrames, fsStrip, fsSeed, fsSize, fsTargetDist);
}
else if (args.Length >= 1 && args[0] == "--selftest-save")
{
    // Phase 19a self-test. Builds a synthetic SaveFile, writes it through
    // SaveStore.Save (atomic temp+replace), reads it back through Load,
    // and asserts every field round-tripped. No window, no GL — pure
    // JSON-correctness check, suitable for test-all.bat.
    return SiegeFX.Runtime.SaveSelfTest.Run() ? 0 : 1;
}
else if (args.Length >= 1 && args[0] == "--selftest-dialogue")
{
    // Phase 20a self-test. Parses a synthetic conversations.gas (mirroring
    // fh_r1's edgaar branching shape) and asserts ConversationStore picks
    // up choice=more / quest_dialog / no-order-tail correctly.
    return SiegeFX.Runtime.DialogueSelfTest.Run() ? 0 : 1;
}
else if (args.Length >= 1 && args[0] == "--skrit-anim")
{
    // Phase 9a. Rigged ASP + skrit that decides which clip plays. Optional trailing
    // `--texture <raw>` binds albedo (keeps the positional list of clips unambiguous).
    if (args.Length < 4)
    {
        Console.Error.WriteLine("usage: SiegeFX.Runtime --skrit-anim <rigged.asp> <skrit> <clip0.prs> [clip1.prs ...] [--texture <raw>]");
        return 1;
    }
    animAsp = args[1];
    skritPath = args[2];
    skritClips = new List<string>();
    for (int i = 3; i < args.Length; i++)
    {
        if (args[i] == "--texture")
        {
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--texture needs a value"); return 1; }
            animTexture = args[i + 1];
            i++;
        }
        else skritClips.Add(args[i]);
    }
    if (skritClips.Count == 0) { Console.Error.WriteLine("--skrit-anim requires at least one clip"); return 1; }
}
else if (args.Length == 0)
{
    // Phase 24-MAINMENU step 1+2 — no-args = boot to main menu.
    // Resolve a DS1 install: env var SIEGEFX_DS1 wins, then probe the
    // common GOG / Steam / retail-DVD paths. If nothing resolves, the
    // user gets a friendly hint instead of a silent black window.
    bootMode = true;
    ds1Resources = ResolveDs1Resources();
    // ALPHA-PACKAGING — tank byte-size integrity check (warn-only). Known-
    // good sizes are the GOG 1.11 set; Steam/disc editions may differ and
    // still work, but a truncated download or a modded tank is the #1
    // "engine acts weird" cause a tester can self-diagnose from this line.
    if (ds1Resources is not null)
    {
        var known = new (string Name, long Size)[]
        {
            ("Logic.dsres",   4_206_896),
            ("Objects.dsres", 304_438_568),
            ("Sound.dsres",   185_343_092),
            ("Terrain.dsres", 410_230_240),
            ("Voices.dsres",  45_951_736),
        };
        foreach (var (name, size) in known)
        {
            var p = System.IO.Path.Combine(ds1Resources, name);
            if (!System.IO.File.Exists(p))
                Console.WriteLine($"[data] warning: {name} missing from '{ds1Resources}'");
            else if (new System.IO.FileInfo(p).Length != size)
                Console.WriteLine($"[data] note: {name} is {new System.IO.FileInfo(p).Length:N0} bytes " +
                                  $"(known-good GOG: {size:N0}) — other editions/mods may work but are untested");
        }
    }
    if (ds1Resources is null)
    {
        // Persist the failure to siegefx_crash.log too — the .exe is
        // typically double-launched without a console attached, so stderr
        // alone is invisible. Same pattern the host.Run crash net uses.
        var msg = new System.Text.StringBuilder();
        msg.AppendLine("siegefx: couldn't find a Dungeon Siege install. Tried:");
        foreach (var p in CandidateDs1Paths()) msg.AppendLine($"   {p}");
        msg.AppendLine("Set the SIEGEFX_DS1 env var to your install path (the folder");
        msg.AppendLine("containing Resources\\Logic.dsres) and re-launch.");
        Console.Error.Write(msg.ToString());
        try { System.IO.File.WriteAllText(crashLogPath, msg.ToString()); } catch { }
        return 1;
    }
}
else
{
    meshPath    = args.Length > 0 ? args[0] : null;
    texturePath = args.Length > 1 ? args[1] : null;
}

static IEnumerable<string> CandidateDs1Paths()
{
    var env = Environment.GetEnvironmentVariable("SIEGEFX_DS1");
    if (!string.IsNullOrEmpty(env)) yield return env;
    yield return @"D:\GOG Games\Dungeon Siege";
    yield return @"C:\GOG Games\Dungeon Siege";
    yield return @"C:\Program Files (x86)\GOG Galaxy\Games\Dungeon Siege";
    yield return @"C:\Program Files (x86)\Steam\steamapps\common\Dungeon Siege 1";
    yield return @"C:\Program Files\Steam\steamapps\common\Dungeon Siege 1";
    yield return @"C:\Program Files (x86)\Microsoft Games\Dungeon Siege";
    yield return @"C:\Program Files\Microsoft Games\Dungeon Siege";
}

static string? ResolveDs1Resources()
{
    // Honor SIEGEFX_DS1 strictly — if the user set it, treat it as a hard
    // override. Falling silently through to the autodetect candidates
    // when the env var points to a missing path swallows a configuration
    // error the user is actively trying to make visible.
    var env = Environment.GetEnvironmentVariable("SIEGEFX_DS1");
    if (!string.IsNullOrEmpty(env))
    {
        var resolved = TryResolveOne(env);
        if (resolved is not null) return resolved;
        Console.Error.WriteLine($"siegefx: SIEGEFX_DS1='{env}' set but no Logic.dsres found there.");
        return null;
    }
    foreach (var root in CandidateDs1Paths())
    {
        var resolved = TryResolveOne(root);
        if (resolved is not null) return resolved;
    }
    return null;

    static string? TryResolveOne(string root)
    {
        if (string.IsNullOrEmpty(root)) return null;
        // Honor either the install root (containing Resources\) or a
        // direct pointer to the Resources folder so power-users can
        // skip the install-root layer entirely.
        var withResources = System.IO.Path.Combine(root, "Resources");
        if (System.IO.File.Exists(System.IO.Path.Combine(withResources, "Logic.dsres")))
            return withResources;
        if (System.IO.File.Exists(System.IO.Path.Combine(root, "Logic.dsres")))
            return root;
        return null;
    }
}

using var host = new RenderHost(
    bootMode ? "Dungeon Siege" : "SiegeFX  —  RMB+WASD to fly, Shift to sprint, Esc to quit",
    meshPath: meshPath,
    texturePath: texturePath,
    regionMapTankPath: regionMap,
    regionTerrainTankPath: regionTerrain,
    regionPath: regionPath,
    worldMapTankPath: worldMap,
    worldTerrainTankPath: worldTerrain,
    worldRootHint: worldRoot,
    animAspPath: animAsp,
    animPrsPath: animPrs,
    animTexturePath: animTexture,
    skritPath: skritPath,
    skritClipPaths: skritClips,
    playLogicTankPath: playLogic,
    playObjectsTankPath: playObjects,
    diagMode: diagMode,
    bootMode: bootMode,
    ds1ResourcesDir: ds1Resources,
    noVideo: noVideo);
try
{
    host.Run();
}
catch (Exception ex)
{
    // Top-level net. Without this, an unhandled exception in the render/logic
    // tick closes the console window before the stack prints. Also persist to
    // siegefx_crash.log so test-all.bat can surface the trace even if the
    // console scrolled past it or was closed too fast.
    Console.Error.WriteLine();
    Console.Error.WriteLine("!! SiegeFX.Runtime crashed:");
    Console.Error.WriteLine(ex.ToString());
    try
    {
        System.IO.File.WriteAllText(crashLogPath,
            "host.Run crashed at " + DateTime.Now.ToString("o") + Environment.NewLine +
            ex.ToString() + Environment.NewLine);
    }
    catch { }
    return 1;
}
return 0;
