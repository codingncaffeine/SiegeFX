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

// Console tee — when SIEGEFX_DEBUG_LOG_FILE is set, mirror all Console.Out to
// that file so I can read the diag log directly instead of asking the user to
// paste it. Auto-flush on every write so a forced quit still leaves a valid
// log. test-all.bat sets this for the slices where I'm actively diagnosing.
var teePath = System.Environment.GetEnvironmentVariable("SIEGEFX_DEBUG_LOG_FILE");
if (!string.IsNullOrWhiteSpace(teePath))
{
    try
    {
        var dir = System.IO.Path.GetDirectoryName(teePath);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        var fs = new System.IO.FileStream(teePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read);
        var fileWriter = new System.IO.StreamWriter(fs) { AutoFlush = true };
        Console.SetOut(new SiegeFX.Runtime.TeeTextWriter(Console.Out, fileWriter));
    }
    catch (Exception ex) { Console.WriteLine($"  tee log: failed to open '{teePath}': {ex.Message}"); }
}

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
