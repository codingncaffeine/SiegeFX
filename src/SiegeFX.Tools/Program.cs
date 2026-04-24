using System.Numerics;
using SiegeFX.Core.Assets;
using SiegeFX.Core.IO;
using SiegeFX.Core.Nav;
using SiegeFX.Core.Skrit;
using SiegeFX.Core.Tank;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "tank" => DispatchTank(args[1..]),
        "raw"  => DispatchRaw(args[1..]),
        "asp"  => DispatchAsp(args[1..]),
        "sno"  => DispatchSno(args[1..]),
        "prs"  => DispatchPrs(args[1..]),
        "gas"  => DispatchGas(args[1..]),
        "region" => DispatchRegion(args[1..]),
        "world"  => DispatchWorld(args[1..]),
        "anim"   => DispatchAnim(args[1..]),
        "skrit"  => DispatchSkrit(args[1..]),
        "templates" => DispatchTemplates(args[1..]),
        _      => UnknownCommand(args[0]),
    };
}
catch (TankException ex)
{
    Console.Error.WriteLine($"tank error: {ex.Message}");
    return 2;
}
catch (InvalidDataException ex)
{
    Console.Error.WriteLine($"invalid data: {ex.Message}");
    return 2;
}
catch (FileNotFoundException ex)
{
    Console.Error.WriteLine($"file not found: {ex.FileName}");
    return 3;
}
catch (FormatException ex)
{
    Console.Error.WriteLine($"bad argument: {ex.Message}");
    return 1;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"bad argument: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("SiegeFX CLI");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  siegefx tank info    <tank>");
    Console.WriteLine("  siegefx tank list    <tank>");
    Console.WriteLine("  siegefx tank extract <tank> <resource-path> [dest-file]");
    Console.WriteLine("  siegefx raw  info    <file.raw>");
    Console.WriteLine("  siegefx raw  decode  <file.raw> [out.png] [--surface N] [--all]");
    Console.WriteLine("  siegefx asp  info    <file.asp>");
    Console.WriteLine("  siegefx sno  info    <file.sno>");
    Console.WriteLine("  siegefx sno  nav     <file.sno>");
    Console.WriteLine("  siegefx prs  info    <file.prs>");
    Console.WriteLine("  siegefx prs  fuzz    <tank>");
    Console.WriteLine("  siegefx gas  info    <file.gas>");
    Console.WriteLine("  siegefx gas  dump    <file.gas>");
    Console.WriteLine("  siegefx gas  fuzz    <tank>");
    Console.WriteLine("  siegefx region info  <map-tank> <region-path>");
    Console.WriteLine("  siegefx region fuzz  <map-tank>");
    Console.WriteLine("  siegefx region layout      <map-tank> <terrain-tank> <region-path>");
    Console.WriteLine("  siegefx region layout-fuzz <map-tank> <terrain-tank>");
    Console.WriteLine("  siegefx world  layout      <map-tank> <terrain-tank> [root-region]");
    Console.WriteLine("  siegefx templates list     <tank> [--prefix=P] [--tag=T]");
    Console.WriteLine("  siegefx templates show     <tank> <name>");
    Console.WriteLine("  siegefx region actors      <map-tank> <region-path>");
    Console.WriteLine("  siegefx region spawn-probe <map-tank> <logic-tank> <objects-tank> <region-path>");
    Console.WriteLine("  siegefx region spawn       <map-tank> <logic-tank> <objects-tank> <region-path> [--ticks=N] [--broadcast=NAME]");
    Console.WriteLine("  siegefx region nav         <map-tank> <terrain-tank> <region-path>");
    Console.WriteLine("  siegefx region nav-fuzz    <map-tank> <terrain-tank>");
    Console.WriteLine("  siegefx region path        <map-tank> <terrain-tank> <region-path> <x1,y1,z1> <x2,y2,z2>");
    Console.WriteLine("  siegefx region path-fuzz   <map-tank> <terrain-tank>");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  siegefx tank info Objects.dsres");
    Console.WriteLine("  siegefx tank extract Objects.dsres /art/bitmaps/gui_logo.raw logo.raw");
    Console.WriteLine("  siegefx raw  decode logo.raw logo.png");
    Console.WriteLine("  siegefx raw  decode logo.raw --all");
    Console.WriteLine("  siegefx asp  info  boot.asp");
    Console.WriteLine("  siegefx sno  info  t_grs01_grs-thick-08.sno");
    Console.WriteLine("  siegefx gas  dump  camera.gas");
    Console.WriteLine("  siegefx gas  fuzz  Logic.dsres");
    Console.WriteLine("  siegefx region info World.dsmap /world/maps/map_world/regions/ac_r1");
    Console.WriteLine("  siegefx region fuzz World.dsmap");
    Console.WriteLine("  siegefx region layout      MpWorld.dsmap Terrain.dsres /world/maps/multiplayer_world/regions/abc_r1");
    Console.WriteLine("  siegefx region layout-fuzz MpWorld.dsmap Terrain.dsres");
    Console.WriteLine("  siegefx world  layout      MpWorld.dsmap Terrain.dsres");
}

static int UnknownCommand(string cmd)
{
    Console.Error.WriteLine($"unknown command: {cmd}");
    PrintUsage();
    return 1;
}

static int DispatchTank(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx tank <info|list|extract> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info"    => CmdTankInfo(a[1..]),
        "list"    => CmdTankList(a[1..]),
        "extract" => CmdTankExtract(a[1..]),
        _         => UnknownCommand("tank " + a[0]),
    };
}

static int DispatchRaw(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx raw <info|decode> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info"   => CmdRawInfo(a[1..]),
        "decode" => CmdRawDecode(a[1..]),
        _        => UnknownCommand("raw " + a[0]),
    };
}

static int DispatchAsp(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx asp <info|skeleton|fuzz> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info"     => CmdAspInfo(a[1..]),
        "skeleton" => CmdAspSkeleton(a[1..]),
        "fuzz"     => CmdAspFuzz(a[1..]),
        _          => UnknownCommand("asp " + a[0]),
    };
}

static int DispatchAnim(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx anim <pose|fuzz> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "pose" => CmdAnimPose(a[1..]),
        "fuzz" => CmdAnimFuzz(a[1..]),
        _      => UnknownCommand("anim " + a[0]),
    };
}

static int DispatchSno(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx sno <info|nav> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info" => CmdSnoInfo(a[1..]),
        "nav"  => CmdSnoNav(a[1..]),
        _      => UnknownCommand("sno " + a[0]),
    };
}

static int DispatchPrs(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx prs <info|fuzz> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info" => CmdPrsInfo(a[1..]),
        "fuzz" => CmdPrsFuzz(a[1..]),
        _      => UnknownCommand("prs " + a[0]),
    };
}

static int CmdPrsInfo(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx prs info <file.prs>"); return 1; }
    var bytes = File.ReadAllBytes(a[0]);
    var prs = PrsAnimation.Load(bytes);
    Console.WriteLine($"File        : {a[0]}");
    Console.WriteLine($"Size        : {bytes.Length:N0} bytes");
    Console.WriteLine($"Anim version: {prs.AnimVersion}");
    Console.WriteLine($"Bones       : {prs.NumBones}");
    Console.WriteLine($"Length      : {prs.AnimLength:F4} s");
    Console.WriteLine($"Root travel : ({prs.RootTravel.X:F2}, {prs.RootTravel.Y:F2}, {prs.RootTravel.Z:F2})");
    Console.WriteLine($"Notes       : {prs.Notes.Count}");
    Console.WriteLine($"Tracers     : {prs.TracerCount}");

    int bonesKeyed = 0, totalRot = 0, totalPos = 0;
    foreach (var k in prs.BoneKeys)
    {
        if (k is null) continue;
        bonesKeyed++;
        totalRot += k.RotKeys.Count;
        totalPos += k.PosKeys.Count;
    }
    Console.WriteLine($"Bones keyed : {bonesKeyed} of {prs.NumBones} (rot keys {totalRot:N0}, pos keys {totalPos:N0})");
    if (prs.RootKeys is not null)
        Console.WriteLine($"Root keys   : rot {prs.RootKeys.RotKeys.Count}, pos {prs.RootKeys.PosKeys.Count}");

    var showBones = Math.Min(8, prs.BoneNames.Count);
    for (var i = 0; i < showBones; i++)
    {
        var k = prs.BoneKeys[i];
        var counts = k is null ? "no keys" : $"rot={k.RotKeys.Count} pos={k.PosKeys.Count}";
        Console.WriteLine($"  [{i,3}] {prs.BoneNames[i],-28} {counts}");
    }
    if (prs.BoneNames.Count > showBones) Console.WriteLine($"  ... {prs.BoneNames.Count - showBones} more bones");

    if (prs.InfoStrings.Count > 0)
    {
        Console.WriteLine($"INFO        :");
        foreach (var s in prs.InfoStrings) Console.WriteLine($"  {s}");
    }
    return 0;
}

static int CmdPrsFuzz(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx prs fuzz <tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);

    int total = 0, failed = 0, tracers = 0, oldVersion = 0;
    long totalBytes = 0;
    var versionsOk = new Dictionary<uint, int>();
    var versionsFail = new Dictionary<uint, int>();
    foreach (var path in reader.ListFiles())
    {
        if (!path.EndsWith(".prs", StringComparison.OrdinalIgnoreCase)) continue;
        total++;
        byte[] bytes;
        try { bytes = reader.ExtractToMemory(path); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [extract-fail] {path}: {ex.Message}");
            failed++;
            continue;
        }
        totalBytes += bytes.Length;
        // Peek anim version so we can histogram successes/failures by format revision.
        uint ver = bytes.Length >= 8 ? BitConverter.ToUInt32(bytes, 4) : 0xFFFFFFFF;
        try
        {
            var prs = PrsAnimation.Load(bytes);
            if (prs.TracerCount > 0) tracers++;
            versionsOk[ver] = versionsOk.GetValueOrDefault(ver) + 1;
        }
        catch (NotSupportedException ex) when (ex.Message.Contains("anim version"))
        {
            // Legacy PRS versions (~5.5% of shipped DS1); explicitly punted. Counted
            // separately so the true failure metric stays focused on v3 regressions.
            oldVersion++;
        }
        catch (NotSupportedException)
        {
            // TRCR chunks — known gap. Counted separately so they don't inflate the
            // failure metric. Don't also bump versionsOk: a TRCR bail is a partial parse,
            // not a successful one, and double-counting muddles the "files handled" signal.
            tracers++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [parse-fail v0x{ver:X}] {path}: {ex.Message}");
            failed++;
            versionsFail[ver] = versionsFail.GetValueOrDefault(ver) + 1;
        }
    }
    Console.WriteLine($"fuzzed {total} .prs file(s), {totalBytes:N0} bytes total; {failed} failure(s), {tracers} with tracers, {oldVersion} legacy-version skipped");
    Console.WriteLine("versions (ok):    " + string.Join(", ", versionsOk.OrderBy(kv => kv.Key).Select(kv => $"0x{kv.Key:X}={kv.Value}")));
    Console.WriteLine("versions (fail):  " + string.Join(", ", versionsFail.OrderBy(kv => kv.Key).Select(kv => $"0x{kv.Key:X}={kv.Value}")));
    return failed == 0 ? 0 : 4;
}

static int DispatchGas(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx gas <info|dump|fuzz> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info" => CmdGasInfo(a[1..]),
        "dump" => CmdGasDump(a[1..]),
        "fuzz" => CmdGasFuzz(a[1..]),
        _      => UnknownCommand("gas " + a[0]),
    };
}

static int CmdGasInfo(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx gas info <file.gas>"); return 1; }
    var bytes = File.ReadAllBytes(a[0]);
    var doc = GasDocument.Load(bytes);
    int blocks = 0, attrs = 0, depth = 0;
    foreach (var r in doc.Roots) Count(r, 1);
    Console.WriteLine($"File     : {a[0]}");
    Console.WriteLine($"Size     : {bytes.Length:N0} bytes");
    Console.WriteLine($"Roots    : {doc.Roots.Count}");
    Console.WriteLine($"Blocks   : {blocks}");
    Console.WriteLine($"Attrs    : {attrs}");
    Console.WriteLine($"Max depth: {depth}");
    foreach (var r in doc.Roots)
        Console.WriteLine($"  [{r.Header}]  {r.Children.Count} child blocks, {r.Attributes.Count} attrs");
    return 0;

    void Count(GasNode n, int d)
    {
        blocks++;
        attrs += n.Attributes.Count;
        if (d > depth) depth = d;
        foreach (var c in n.Children) Count(c, d + 1);
    }
}

static int CmdGasDump(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx gas dump <file.gas>"); return 1; }
    var bytes = File.ReadAllBytes(a[0]);
    var doc = GasDocument.Load(bytes);
    foreach (var r in doc.Roots) Dump(r, 0);
    return 0;

    static void Dump(GasNode n, int indent)
    {
        var pad = new string(' ', indent * 2);
        Console.WriteLine($"{pad}[{n.Header}]");
        foreach (var a in n.Attributes)
        {
            var tag = a.TypeTag is null ? "" : $"{a.TypeTag} ";
            Console.WriteLine($"{pad}  {tag}{a.Name} = {a.Value}");
        }
        foreach (var c in n.Children) Dump(c, indent + 1);
    }
}

static int DispatchRegion(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx region <info|fuzz|layout|layout-fuzz|actors|spawn|nav|path> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info"        => CmdRegionInfo(a[1..]),
        "fuzz"        => CmdRegionFuzz(a[1..]),
        "layout"      => CmdRegionLayout(a[1..]),
        "layout-fuzz" => CmdRegionLayoutFuzz(a[1..]),
        "layout-diag" => CmdRegionLayoutDiag(a[1..]),
        "actors"      => CmdRegionActors(a[1..]),
        "spawn-probe" => CmdRegionSpawnProbe(a[1..]),
        "spawn"       => CmdRegionSpawn(a[1..]),
        "nav"         => CmdRegionNav(a[1..]),
        "nav-fuzz"    => CmdRegionNavFuzz(a[1..]),
        "path"        => CmdRegionPath(a[1..]),
        "path-fuzz"   => CmdRegionPathFuzz(a[1..]),
        _             => UnknownCommand("region " + a[0]),
    };
}

static int DispatchWorld(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx world <layout> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "layout" => CmdWorldLayout(a[1..]),
        _        => UnknownCommand("world " + a[0]),
    };
}

static int DispatchSkrit(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx skrit <tokens|parse|bind|compile|run|tick|fuzz> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "tokens"  => CmdSkritTokens(a[1..]),
        "parse"   => CmdSkritParse(a[1..]),
        "bind"    => CmdSkritBind(a[1..]),
        "compile" => CmdSkritCompile(a[1..]),
        "run"     => CmdSkritRun(a[1..]),
        "tick"    => CmdSkritTick(a[1..]),
        "fuzz"    => CmdSkritFuzz(a[1..]),
        _         => UnknownCommand("skrit " + a[0]),
    };
}

static int CmdSkritTokens(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx skrit tokens <file.skrit>"); return 1; }
    var src = File.ReadAllText(a[0]);
    var toks = SkritLexer.Tokenize(src);
    foreach (var t in toks) Console.WriteLine(t);
    Console.WriteLine($"--- {toks.Count} tokens");
    return 0;
}

static int CmdSkritParse(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx skrit parse <file.skrit>"); return 1; }
    var src = File.ReadAllText(a[0]);
    var script = SkritParser.Parse(src);
    Console.WriteLine($"script: {script.TopLevels.Count} top-levels");
    foreach (var top in script.TopLevels)
    {
        switch (top)
        {
            case SkritPreprocessorDecl p: Console.WriteLine($"  preprocessor: {p.Directive.Trim()}"); break;
            case SkritPropertyDecl p: Console.WriteLine($"  property {p.Type} {p.Name}"); break;
            case SkritOwnerDecl o: Console.WriteLine($"  owner = {o.Owner}"); break;
            case SkritFieldDecl f: Console.WriteLine($"  field {f.Type} {f.Name}"); break;
            case SkritFunctionDecl fn: Console.WriteLine($"  fn {fn.ReturnType ?? "<void>"} {fn.Name}({fn.Params.Count} params) body={fn.Body.Statements.Count} stmts"); break;
            case SkritStateDecl s: Console.WriteLine($"  state{(s.IsStartup ? "[startup]" : "")} {s.Name} ({s.Body.Count} members)"); break;
        }
    }
    return 0;
}

static int CmdSkritBind(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx skrit bind <file.skrit>"); return 1; }
    var src = File.ReadAllText(a[0]);
    var script = SkritParser.Parse(src);
    var bind = new SkritBinder(script).Bind();

    Console.WriteLine($"script: {script.TopLevels.Count} top-levels");
    Console.WriteLine($"  globals   : {bind.Globals.Count}");
    Console.WriteLine($"  states    : {bind.States.Count}");
    Console.WriteLine($"  externs   : {bind.Externs.Count}");
    Console.WriteLine($"  diagnostics: {bind.Diagnostics.Count}");
    foreach (var g in bind.Globals.Values) Console.WriteLine($"    global  {g}");
    foreach (var s in bind.States.Values)  Console.WriteLine($"    state   {s}");
    foreach (var e in bind.Externs)        Console.WriteLine($"    extern  {e}");
    foreach (var d in bind.Diagnostics)    Console.WriteLine($"    !! {d}");
    return bind.HasErrors ? 4 : 0;
}

static int CmdSkritCompile(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx skrit compile <file.skrit>"); return 1; }
    var src = File.ReadAllText(a[0]);
    var script = SkritParser.Parse(src);
    var bind = new SkritBinder(script).Bind();
    var program = new SkritCompiler(script, bind).Compile();
    Console.WriteLine($"program: {program.Chunks.Count} chunks, {program.Externs.Count} externs");
    foreach (var c in program.Chunks)
        Console.Write(SkritDisassembler.Dump(c));
    return 0;
}

static int CmdSkritRun(string[] a)
{
    if (a.Length < 2) { Console.Error.WriteLine("usage: siegefx skrit run <file.skrit> <chunk> [globalName=intVal ...]"); return 1; }
    var src = File.ReadAllText(a[0]);
    var script = SkritParser.Parse(src);
    var bind = new SkritBinder(script).Bind();
    var program = new SkritCompiler(script, bind).Compile();
    var vm = new SkritVm(program, new SiegeFX.Tools.TracingHostBridge());

    for (int i = 2; i < a.Length; i++)
    {
        var eq = a[i].IndexOf('=');
        if (eq <= 0) continue;
        var k = a[i][..eq];
        var raw = a[i][(eq + 1)..];
        SkritValue v;
        if (long.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var lv)) v = SkritValue.FromInt(lv);
        else if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv)) v = SkritValue.FromFloat(dv);
        else if (raw == "true") v = SkritValue.True;
        else if (raw == "false") v = SkritValue.False;
        else v = SkritValue.FromString(raw);
        vm.SetGlobal(k, v);
    }
    var result = vm.Run(a[1]);
    Console.WriteLine($"result: {result}");
    return 0;
}

static int CmdSkritTick(string[] a)
{
    int ticks = 60;
    int seed = 1;
    int subAnims = 4;
    string? file = null;
    var startEvents = new List<string>();
    foreach (var x in a)
    {
        if (x.StartsWith("--ticks=", StringComparison.Ordinal)) ticks = int.Parse(x["--ticks=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        else if (x.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(x["--seed=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        else if (x.StartsWith("--subanims=", StringComparison.Ordinal)) subAnims = int.Parse(x["--subanims=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        else if (x.StartsWith("--event=", StringComparison.Ordinal)) startEvents.Add(x["--event=".Length..]);
        else if (!x.StartsWith("--", StringComparison.Ordinal)) file = x;
    }
    if (file is null)
    {
        Console.Error.WriteLine("usage: siegefx skrit tick <file.skrit> [--ticks=N] [--seed=S] [--subanims=N] [--event=Name ...]");
        return 1;
    }

    var src = File.ReadAllText(file);
    var script = SkritParser.Parse(src);
    var bind = new SkritBinder(script).Bind();
    if (bind.Diagnostics.Count > 0)
    {
        // Shipped DS1 skrits carry real bugs (duplicate states, unknown transitions).
        // Surface them but keep running — the binder still produces a usable scope
        // table by keeping the first of each duplicate.
        Console.WriteLine($"bind: {bind.Diagnostics.Count} diagnostic(s) (continuing):");
        foreach (var d in bind.Diagnostics) Console.WriteLine($"  !! {d}");
    }
    var program = new SkritCompiler(script, bind).Compile();

    var host = new ActorHostBridge(seed) { NumSubAnims = subAnims };
    var runtime = new SkritRuntime();
    var inst = runtime.Add(new SkritInstance(program, host));
    host.Instance = inst;
    inst.Start();
    Console.WriteLine($"start: state={inst.CurrentState ?? "<none>"}, chores={inst.Chores.Count}");
    foreach (var ev in startEvents)
    {
        // Anim skrits conventionally enter via OnStartChore$( subanim, flags ).
        bool fired = inst.Dispatch(ev, SkritValue.FromInt(0), SkritValue.FromInt(0));
        Console.WriteLine($"dispatch {ev}: {(fired ? "ran" : "no handler")}");
    }

    // Fixed-step tick at 20 Hz (SkritInstance.FramesPerSecond) so `frames` units map 1:1.
    double dt = 1.0 / SkritInstance.FramesPerSecond;
    string? lastState = inst.CurrentState;
    int firedChores = 0;
    for (int t = 0; t < ticks; t++)
    {
        int beforeChoreCount = inst.Chores.Count;
        runtime.Tick(dt);
        int afterChoreCount = inst.Chores.Count;
        if (afterChoreCount < beforeChoreCount) firedChores += beforeChoreCount - afterChoreCount;
        if (inst.CurrentState != lastState)
        {
            Console.WriteLine($"tick {t,3}: state {lastState ?? "<none>"} -> {inst.CurrentState ?? "<none>"}, chores={afterChoreCount}");
            lastState = inst.CurrentState;
        }
    }
    Console.WriteLine($"end: state={inst.CurrentState ?? "<none>"}, chores-remaining={inst.Chores.Count}, chores-fired≥{firedChores}");
    Console.WriteLine($"blender: anim={host.CurrentAnimIndex}, log={host.BlenderLog.Count} call(s)");
    foreach (var line in host.BlenderLog) Console.WriteLine($"  {line}");
    return 0;
}

static int CmdSkritFuzz(string[] a)
{
    // Optional --stage={lex|parse|bind|compile} flag (default: parse, which implies lex).
    string stage = "parse";
    var rest = new List<string>();
    foreach (var x in a)
    {
        if (x.StartsWith("--stage=", StringComparison.Ordinal)) stage = x["--stage=".Length..].ToLowerInvariant();
        else rest.Add(x);
    }
    if (rest.Count != 1) { Console.Error.WriteLine("usage: siegefx skrit fuzz [--stage=lex|parse|bind|compile] <tank.dsres>"); return 1; }
    using var tank = TankFile.Open(rest[0]);
    var reader = new TankReader(tank);

    int total = 0, ok = 0, fail = 0;
    long totalTokens = 0;
    long totalDiags = 0;
    int filesWithDiags = 0;
    var failures = new List<(string Path, string Msg)>();
    var seenHashes = new HashSet<string>(StringComparer.Ordinal);
    int deduped = 0;

    foreach (var path in reader.ListFiles())
    {
        if (!path.EndsWith(".skrit", StringComparison.OrdinalIgnoreCase)) continue;
        total++;
        byte[] bytes;
        try { bytes = reader.ExtractToMemory(path); }
        catch (Exception ex) { fail++; failures.Add((path, "extract: " + ex.Message)); continue; }

        // Dedup by content hash so the 333 animation skrits duplicated across Logic/Objects
        // only fuzz once.
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes));
        if (!seenHashes.Add(hash)) { deduped++; continue; }

        try
        {
            var src = System.Text.Encoding.UTF8.GetString(bytes);
            var toks = SkritLexer.Tokenize(src);
            totalTokens += toks.Count;
            if (stage == "parse")
            {
                _ = new SkritParser(toks).ParseScript();
            }
            else if (stage == "bind")
            {
                var script = new SkritParser(toks).ParseScript();
                var bind = new SkritBinder(script).Bind();
                // Diagnostics are informational — shipped skrits have real bugs (duplicate
                // states, unknown transition targets). The fuzz counts them separately
                // rather than treating them as binder failures.
                if (bind.Diagnostics.Count > 0)
                {
                    filesWithDiags++;
                    totalDiags += bind.Diagnostics.Count;
                }
            }
            else if (stage == "compile")
            {
                var script = new SkritParser(toks).ParseScript();
                var bind = new SkritBinder(script).Bind();
                _ = new SkritCompiler(script, bind).Compile();
            }
            ok++;
        }
        catch (Exception ex)
        {
            fail++;
            failures.Add((path, ex.Message));
        }
    }

    Console.WriteLine($"skrit fuzz [{stage}]: {total} found / {deduped} dedup / {ok} OK / {fail} FAIL (total tokens: {totalTokens:N0})");
    if (stage == "bind")
        Console.WriteLine($"  bind diagnostics: {totalDiags} across {filesWithDiags} file(s) (informational)");
    foreach (var (p, m) in failures.Take(20)) Console.WriteLine($"  FAIL {p}: {m}");
    if (failures.Count > 20) Console.WriteLine($"  (+{failures.Count - 20} more)");
    return fail == 0 ? 0 : 3;
}

static int CmdRegionInfo(string[] a)
{
    if (a.Length != 2)
    {
        Console.Error.WriteLine("usage: siegefx region info <map-tank> <region-path>");
        return 1;
    }

    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);

    var regionPath = a[1].Replace('\\', '/');
    if (!regionPath.StartsWith('/')) regionPath = "/" + regionPath;
    if (regionPath.EndsWith('/')) regionPath = regionPath[..^1];
    var nodesPath = regionPath + "/terrain_nodes/nodes.gas";

    var bytes = reader.ExtractToMemory(nodesPath);
    var region = RegionGraph.Load(bytes);

    var unconnected = 0;
    var crossRegion = new HashSet<uint>();
    var within = new HashSet<uint>();
    foreach (var n in region.Nodes) within.Add(n.Guid);
    foreach (var n in region.Nodes)
    foreach (var d in n.Doors)
    {
        if (d.FarGuid == 0) unconnected++;
        else if (!within.Contains(d.FarGuid)) crossRegion.Add(d.FarGuid);
    }

    Console.WriteLine($"Region        : {regionPath}");
    Console.WriteLine($"Target node   : 0x{region.TargetNodeGuid:X8}");
    Console.WriteLine($"Snode count   : {region.Nodes.Count}");
    var totalDoors = 0;
    foreach (var n in region.Nodes) totalDoors += n.Doors.Count;
    Console.WriteLine($"Door edges    : {totalDoors}");
    Console.WriteLine($"Cross-region  : {crossRegion.Count} neighbor-region node(s)");
    Console.WriteLine($"Unconnected   : {unconnected} door(s)");
    Console.WriteLine($"Target present: {(within.Contains(region.TargetNodeGuid) ? "yes" : "no (cross-region anchor)")}");

    var show = Math.Min(5, region.Nodes.Count);
    for (var i = 0; i < show; i++)
    {
        var n = region.Nodes[i];
        Console.WriteLine($"  [{i}] guid=0x{n.Guid:X8} mesh=0x{n.MeshGuid:X8} texset='{n.TexsetAbbr}' doors={n.Doors.Count}");
    }
    if (region.Nodes.Count > show)
        Console.WriteLine($"  ... {region.Nodes.Count - show} more");
    return 0;
}

static int CmdRegionFuzz(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx region fuzz <map-tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);

    var total = 0;
    var failed = 0;
    var totalNodes = 0;
    var totalDoors = 0;
    foreach (var path in reader.ListFiles())
    {
        if (!path.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase)) continue;
        total++;
        try
        {
            var bytes = reader.ExtractToMemory(path);
            var region = RegionGraph.Load(bytes);
            totalNodes += region.Nodes.Count;
            foreach (var n in region.Nodes) totalDoors += n.Doors.Count;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [fail] {path}: {ex.Message}");
            failed++;
        }
    }
    Console.WriteLine($"fuzzed {total} region(s); {totalNodes:N0} snodes, {totalDoors:N0} doors; {failed} failure(s)");
    return failed == 0 ? 0 : 4;
}

static int CmdRegionLayout(string[] a)
{
    if (a.Length != 3)
    {
        Console.Error.WriteLine("usage: siegefx region layout <map-tank> <terrain-tank> <region-path>");
        return 1;
    }

    using var mapTank = TankFile.Open(a[0]);
    var mapReader = new TankReader(mapTank);
    using var terrainTank = TankFile.Open(a[1]);
    var terrainReader = new TankReader(terrainTank);

    var regionPath = a[2].Replace('\\', '/');
    if (!regionPath.StartsWith('/')) regionPath = "/" + regionPath;
    if (regionPath.EndsWith('/')) regionPath = regionPath[..^1];

    var graph = RegionGraph.Load(mapReader.ExtractToMemory(regionPath + "/terrain_nodes/nodes.gas"));

    var meshIndex = SnoMeshIndex.Build(terrainReader);
    var snoCache = new Dictionary<uint, SnoModel?>();
    SnoModel? Resolve(uint meshGuid)
    {
        if (snoCache.TryGetValue(meshGuid, out var cached)) return cached;
        SnoModel? sno = null;
        if (meshIndex.TryResolve(meshGuid, out var path))
        {
            try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
            catch { sno = null; }
        }
        snoCache[meshGuid] = sno;
        return sno;
    }

    var layout = RegionLayout.Build(graph, Resolve);

    var missingMeshes = 0;
    foreach (var n in graph.Nodes)
        if (!meshIndex.TryResolve(n.MeshGuid, out _)) missingMeshes++;

    Console.WriteLine($"Region            : {regionPath}");
    Console.WriteLine($"Anchor            : 0x{layout.AnchorGuid:X8}" +
                      (layout.AnchorGuid == graph.TargetNodeGuid ? " (target)" : " (fallback; target cross-region)"));
    Console.WriteLine($"Placed snodes     : {layout.Transforms.Count} of {graph.Nodes.Count}");
    Console.WriteLine($"Unreachable       : {layout.UnreachableNodeCount}");
    Console.WriteLine($"Cross-region doors: {layout.CrossRegionDoorCount}");
    Console.WriteLine($"Unresolved doors  : {layout.UnresolvedDoorCount}");
    Console.WriteLine($"Missing meshes    : {missingMeshes}");
    Console.WriteLine($"MeshIndex         : {meshIndex.GuidCount} guid(s), {meshIndex.SnoCount} sno(s)");

    var show = 0;
    foreach (var n in graph.Nodes)
    {
        if (show >= 5) break;
        if (!layout.TryGetTransform(n.Guid, out var w)) continue;
        Console.WriteLine($"  0x{n.Guid:X8}  t=({w.M41,8:F2}, {w.M42,8:F2}, {w.M43,8:F2})");
        show++;
    }
    return 0;
}

static int CmdRegionNav(string[] a)
{
    if (a.Length != 3)
    {
        Console.Error.WriteLine("usage: siegefx region nav <map-tank> <terrain-tank> <region-path>");
        return 1;
    }

    using var mapTank = TankFile.Open(a[0]);
    var mapReader = new TankReader(mapTank);
    using var terrainTank = TankFile.Open(a[1]);
    var terrainReader = new TankReader(terrainTank);

    var regionPath = a[2].Replace('\\', '/');
    if (!regionPath.StartsWith('/')) regionPath = "/" + regionPath;
    if (regionPath.EndsWith('/')) regionPath = regionPath[..^1];

    var graph = RegionGraph.Load(mapReader.ExtractToMemory(regionPath + "/terrain_nodes/nodes.gas"));
    var meshIndex = SnoMeshIndex.Build(terrainReader);
    var snoCache = new Dictionary<uint, SnoModel?>();
    SnoModel? Resolve(uint meshGuid)
    {
        if (snoCache.TryGetValue(meshGuid, out var cached)) return cached;
        SnoModel? sno = null;
        if (meshIndex.TryResolve(meshGuid, out var path))
        {
            try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
            catch { sno = null; }
        }
        snoCache[meshGuid] = sno;
        return sno;
    }

    var layout = RegionLayout.Build(graph, Resolve);

    int placedSnodes = 0, snosWithNav = 0, snosNoNav = 0;
    int groupTotal = 0, floorGroups = 0, waterGroups = 0, ignoredGroups = 0;
    long floorFaces = 0, waterFaces = 0, ignoredFaces = 0;

    foreach (var n in graph.Nodes)
    {
        if (!layout.TryGetTransform(n.Guid, out _)) continue;
        placedSnodes++;
        var sno = Resolve(n.MeshGuid);
        if (sno is null) continue;
        if (sno.LogicalGroupings.Length == 0) { snosNoNav++; continue; }
        snosWithNav++;
        foreach (var g in sno.LogicalGroupings)
        {
            groupTotal++;
            switch (g.Kind)
            {
                case SnoModel.FloorKind.Floor:   floorGroups++;   floorFaces   += g.Faces.Length; break;
                case SnoModel.FloorKind.Water:   waterGroups++;   waterFaces   += g.Faces.Length; break;
                case SnoModel.FloorKind.Ignored: ignoredGroups++; ignoredFaces += g.Faces.Length; break;
            }
        }
    }

    Console.WriteLine($"Region         : {regionPath}");
    Console.WriteLine($"Snodes placed  : {placedSnodes} of {graph.Nodes.Count}");
    Console.WriteLine($"Snodes w/  nav : {snosWithNav}");
    Console.WriteLine($"Snodes w/o nav : {snosNoNav}");
    Console.WriteLine($"Nav groupings  : {groupTotal}  (floor={floorGroups}, water={waterGroups}, ignored={ignoredGroups})");
    Console.WriteLine($"Nav faces      : floor={floorFaces:N0}  water={waterFaces:N0}  ignored={ignoredFaces:N0}");
    if (floorFaces == 0)
        Console.WriteLine("WARNING: no walkable floor faces — region is nav-empty");
    return 0;
}

static int CmdRegionNavFuzz(string[] a)
{
    if (a.Length != 2)
    {
        Console.Error.WriteLine("usage: siegefx region nav-fuzz <map-tank> <terrain-tank>");
        return 1;
    }

    using var mapTank = TankFile.Open(a[0]);
    var mapReader = new TankReader(mapTank);
    using var terrainTank = TankFile.Open(a[1]);
    var terrainReader = new TankReader(terrainTank);

    var meshIndex = SnoMeshIndex.Build(terrainReader);
    var snoCache = new Dictionary<uint, SnoModel?>();
    var snoFails = new List<string>();
    SnoModel? Resolve(uint meshGuid)
    {
        if (snoCache.TryGetValue(meshGuid, out var cached)) return cached;
        SnoModel? sno = null;
        if (meshIndex.TryResolve(meshGuid, out var path))
        {
            try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
            catch (Exception ex) { snoFails.Add($"{path}: {ex.Message}"); sno = null; }
        }
        snoCache[meshGuid] = sno;
        return sno;
    }

    int regionCount = 0, regionFails = 0;
    int snosScanned = 0, snosWithNav = 0, snosNoNav = 0, snosBad = 0;
    long floorFaces = 0, waterFaces = 0, ignoredFaces = 0;
    foreach (var path in mapReader.ListFiles())
    {
        if (!path.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase)) continue;
        regionCount++;
        try
        {
            var graph = RegionGraph.Load(mapReader.ExtractToMemory(path));
            var seen = new HashSet<uint>();
            foreach (var n in graph.Nodes)
            {
                if (!seen.Add(n.MeshGuid)) continue;
                var sno = Resolve(n.MeshGuid);
                snosScanned++;
                if (sno is null) { snosBad++; continue; }
                if (sno.LogicalGroupings.Length == 0) { snosNoNav++; continue; }
                snosWithNav++;
                foreach (var g in sno.LogicalGroupings)
                {
                    switch (g.Kind)
                    {
                        case SnoModel.FloorKind.Floor:   floorFaces   += g.Faces.Length; break;
                        case SnoModel.FloorKind.Water:   waterFaces   += g.Faces.Length; break;
                        case SnoModel.FloorKind.Ignored: ignoredFaces += g.Faces.Length; break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [region fail] {path}: {ex.Message}");
            regionFails++;
        }
    }
    Console.WriteLine($"Regions scanned: {regionCount}  ({regionFails} region failure(s))");
    Console.WriteLine($"Unique SNOs    : {snosScanned}  (with-nav={snosWithNav}, no-nav={snosNoNav}, bad={snosBad})");
    Console.WriteLine($"Nav faces      : floor={floorFaces:N0}  water={waterFaces:N0}  ignored={ignoredFaces:N0}");
    if (snoFails.Count > 0)
    {
        Console.WriteLine($"SNO parse failures ({snoFails.Count}):");
        foreach (var msg in snoFails.Take(20)) Console.WriteLine($"  {msg}");
        if (snoFails.Count > 20) Console.WriteLine($"  ... {snoFails.Count - 20} more");
    }
    return (regionFails == 0 && snoFails.Count == 0) ? 0 : 4;
}

static int CmdRegionPath(string[] a)
{
    if (a.Length != 5)
    {
        Console.Error.WriteLine("usage: siegefx region path <map-tank> <terrain-tank> <region-path> <x1,y1,z1> <x2,y2,z2>");
        return 1;
    }
    if (!TryParseVec3(a[3], out var start)) { Console.Error.WriteLine($"bad start vector: '{a[3]}'"); return 1; }
    if (!TryParseVec3(a[4], out var goal))  { Console.Error.WriteLine($"bad goal vector: '{a[4]}'");  return 1; }

    var mesh = LoadRegionNavMesh(a[0], a[1], a[2]);
    Console.WriteLine($"NavMesh    : {mesh.TriangleCount:N0} tris, {mesh.Vertices.Length:N0} welded verts, {mesh.SourceSnodeCount} source snode(s), {mesh.DegenerateFaceCount} degen");

    if (!mesh.TryFindTriangle(start, out var startTri)) { Console.Error.WriteLine("start point is not over any walkable triangle"); return 2; }
    if (!mesh.TryFindTriangle(goal,  out var goalTri))  { Console.Error.WriteLine("goal point is not over any walkable triangle"); return 2; }
    Console.WriteLine($"Start tri  : {startTri}  centroid={FormatVec(mesh.Centroids[startTri])}");
    Console.WriteLine($"Goal  tri  : {goalTri}  centroid={FormatVec(mesh.Centroids[goalTri])}");

    if (!NavPathfinder.TryFindPath(mesh, startTri, goalTri, out var path))
    {
        Console.Error.WriteLine("no path — triangles are in disconnected components");
        return 3;
    }
    float length = 0f;
    for (int i = 1; i < path.Count; i++)
        length += Vector3.Distance(mesh.Centroids[path[i - 1]], mesh.Centroids[path[i]]);
    Console.WriteLine($"Path       : {path.Count} tris, centroid length {length:F2} units");
    int show = Math.Min(8, path.Count);
    for (int i = 0; i < show; i++)
        Console.WriteLine($"  [{i,3}] tri={path[i]}  {FormatVec(mesh.Centroids[path[i]])}");
    if (path.Count > show) Console.WriteLine($"  ... {path.Count - show} more");
    return 0;
}

static int CmdRegionPathFuzz(string[] a)
{
    if (a.Length != 2)
    {
        Console.Error.WriteLine("usage: siegefx region path-fuzz <map-tank> <terrain-tank>");
        return 1;
    }

    using var mapTank = TankFile.Open(a[0]);
    var mapReader = new TankReader(mapTank);
    using var terrainTank = TankFile.Open(a[1]);
    var terrainReader = new TankReader(terrainTank);
    var meshIndex = SnoMeshIndex.Build(terrainReader);
    var snoCache = new Dictionary<uint, SnoModel?>();
    SnoModel? Resolve(uint meshGuid)
    {
        if (snoCache.TryGetValue(meshGuid, out var cached)) return cached;
        SnoModel? sno = null;
        if (meshIndex.TryResolve(meshGuid, out var path))
        {
            try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
            catch { sno = null; }
        }
        snoCache[meshGuid] = sno;
        return sno;
    }

    const int SamplesPerRegion = 20;
    var rng = new Random(unchecked((int)0xDEADBEEF));
    int regions = 0, emptyMeshes = 0, failedRegions = 0, probes = 0, solved = 0, intraBigProbes = 0, intraBigSolved = 0;
    long totalTris = 0, totalComponents = 0, totalBiggest = 0, totalNonManifold = 0;
    foreach (var rnodePath in mapReader.ListFiles())
    {
        if (!rnodePath.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase)) continue;
        regions++;
        try
        {
            var graph = RegionGraph.Load(mapReader.ExtractToMemory(rnodePath));
            var layout = RegionLayout.Build(graph, Resolve);
            var mesh = NavMesh.BuildForRegion(graph, layout, Resolve);
            if (mesh.TriangleCount == 0) { emptyMeshes++; continue; }
            totalTris += mesh.TriangleCount;
            totalNonManifold += mesh.NonManifoldEdgeCount;
            var (components, bigComponent, bigSize) = AnalyzeComponents(mesh);
            totalComponents += components;
            totalBiggest += bigSize;
            for (int s = 0; s < SamplesPerRegion; s++)
            {
                probes++;
                int a0 = rng.Next(mesh.TriangleCount);
                int b0 = rng.Next(mesh.TriangleCount);
                if (NavPathfinder.TryFindPath(mesh, a0, b0, out _)) solved++;
                // Control probe: both endpoints forced into the biggest component.
                // Exercises the pathfinder on the mesh's "real" walkable surface rather
                // than measuring topology disconnectedness. Expect ~100%.
                int ia = bigComponent[rng.Next(bigComponent.Count)];
                int ib = bigComponent[rng.Next(bigComponent.Count)];
                intraBigProbes++;
                if (NavPathfinder.TryFindPath(mesh, ia, ib, out _)) intraBigSolved++;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [region fail] {rnodePath}: {ex.Message}");
            failedRegions++;
        }
    }
    int meshed = regions - emptyMeshes - failedRegions;
    double solveRate = probes == 0 ? 0 : 100.0 * solved / probes;
    double bigSolveRate = intraBigProbes == 0 ? 0 : 100.0 * intraBigSolved / intraBigProbes;
    Console.WriteLine($"Regions              : {regions} ({emptyMeshes} empty, {failedRegions} failed)");
    Console.WriteLine($"Avg tris / mesh      : {(meshed == 0 ? 0 : totalTris / meshed):N0}");
    Console.WriteLine($"Avg comps / mesh     : {(meshed == 0 ? 0 : totalComponents / meshed)}");
    Console.WriteLine($"Avg biggest / mesh   : {(meshed == 0 ? 0 : totalBiggest / meshed):N0} ({(totalTris == 0 ? 0 : 100.0 * totalBiggest / totalTris):F1}% of all tris)");
    Console.WriteLine($"Total non-manifold   : {totalNonManifold:N0} edge(s) across all regions");
    Console.WriteLine($"Random-pair A*       : {probes} probes, {solved} solved = {solveRate:F1}%  (measures topology, not pathfinder health)");
    Console.WriteLine($"Biggest-component A* : {intraBigProbes} probes, {intraBigSolved} solved = {bigSolveRate:F1}%  (should be ~100%)");
    return failedRegions == 0 ? 0 : 4;
}

// Flood-fills connected components over triangle adjacency. Returns the total
// component count, the largest component's triangle indices, and its size. Used
// by the fuzz probe to separate pathfinder correctness (biggest-component A*
// should be ~100%) from mesh topology (random-pair A* is bounded by the
// largest-component fraction squared).
static (int components, List<int> bigComponent, int bigSize) AnalyzeComponents(NavMesh mesh)
{
    var visited = new bool[mesh.TriangleCount];
    var stack = new Stack<int>();
    var current = new List<int>();
    var biggest = new List<int>();
    int components = 0;
    for (int seed = 0; seed < mesh.TriangleCount; seed++)
    {
        if (visited[seed]) continue;
        components++;
        current.Clear();
        stack.Push(seed);
        while (stack.Count > 0)
        {
            int t = stack.Pop();
            if (visited[t]) continue;
            visited[t] = true;
            current.Add(t);
            for (int s = 0; s < 3; s++)
            {
                int nb = mesh.Neighbors[3 * t + s];
                if (nb >= 0 && !visited[nb]) stack.Push(nb);
            }
        }
        if (current.Count > biggest.Count)
        {
            // Swap-then-clear avoids copying the component list. `current` will be
            // re-used for the next seed; whatever was in `biggest` gets scrubbed below.
            (biggest, current) = (current, biggest);
        }
    }
    return (components, biggest, biggest.Count);
}

static NavMesh LoadRegionNavMesh(string mapTankPath, string terrainTankPath, string regionPathRaw)
{
    using var mapTank = TankFile.Open(mapTankPath);
    var mapReader = new TankReader(mapTank);
    using var terrainTank = TankFile.Open(terrainTankPath);
    var terrainReader = new TankReader(terrainTank);

    var regionPath = regionPathRaw.Replace('\\', '/');
    if (!regionPath.StartsWith('/')) regionPath = "/" + regionPath;
    if (regionPath.EndsWith('/')) regionPath = regionPath[..^1];

    var graph = RegionGraph.Load(mapReader.ExtractToMemory(regionPath + "/terrain_nodes/nodes.gas"));
    var meshIndex = SnoMeshIndex.Build(terrainReader);
    var snoCache = new Dictionary<uint, SnoModel?>();
    SnoModel? Resolve(uint meshGuid)
    {
        if (snoCache.TryGetValue(meshGuid, out var cached)) return cached;
        SnoModel? sno = null;
        if (meshIndex.TryResolve(meshGuid, out var path))
        {
            try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
            catch { sno = null; }
        }
        snoCache[meshGuid] = sno;
        return sno;
    }
    var layout = RegionLayout.Build(graph, Resolve);
    return NavMesh.BuildForRegion(graph, layout, Resolve);
}

static bool TryParseVec3(string s, out Vector3 v)
{
    v = default;
    var parts = s.Split(',');
    if (parts.Length != 3) return false;
    if (!float.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var x)) return false;
    if (!float.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var y)) return false;
    if (!float.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var z)) return false;
    v = new Vector3(x, y, z);
    return true;
}

static string FormatVec(Vector3 v) => $"({v.X,8:F2},{v.Y,7:F2},{v.Z,8:F2})";

static int CmdRegionLayoutFuzz(string[] a)
{
    if (a.Length != 2)
    {
        Console.Error.WriteLine("usage: siegefx region layout-fuzz <map-tank> <terrain-tank>");
        return 1;
    }

    using var mapTank = TankFile.Open(a[0]);
    var mapReader = new TankReader(mapTank);
    using var terrainTank = TankFile.Open(a[1]);
    var terrainReader = new TankReader(terrainTank);

    var meshIndex = SnoMeshIndex.Build(terrainReader);
    var snoCache = new Dictionary<uint, SnoModel?>();
    SnoModel? Resolve(uint meshGuid)
    {
        if (snoCache.TryGetValue(meshGuid, out var cached)) return cached;
        SnoModel? sno = null;
        if (meshIndex.TryResolve(meshGuid, out var path))
        {
            try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
            catch { sno = null; }
        }
        snoCache[meshGuid] = sno;
        return sno;
    }

    var regionCount = 0;
    var placed = 0;
    var unplaced = 0;
    var crossRegionDoors = 0;
    var unresolvedDoors = 0;
    var missingMeshes = 0;
    var failed = 0;
    foreach (var path in mapReader.ListFiles())
    {
        if (!path.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase)) continue;
        regionCount++;
        try
        {
            var graph = RegionGraph.Load(mapReader.ExtractToMemory(path));
            var layout = RegionLayout.Build(graph, Resolve);
            placed += layout.Transforms.Count;
            unplaced += layout.UnreachableNodeCount;
            crossRegionDoors += layout.CrossRegionDoorCount;
            unresolvedDoors += layout.UnresolvedDoorCount;
            foreach (var n in graph.Nodes)
                if (!meshIndex.TryResolve(n.MeshGuid, out _)) missingMeshes++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [fail] {path}: {ex.Message}");
            failed++;
        }
    }
    Console.WriteLine($"layout-fuzzed {regionCount} region(s): {placed:N0} placed, {unplaced:N0} unreachable, " +
                      $"{crossRegionDoors:N0} cross-region door(s), {unresolvedDoors:N0} unresolved door(s), " +
                      $"{missingMeshes:N0} missing mesh(es); {failed} failure(s)");
    return failed == 0 ? 0 : 4;
}

static int CmdWorldLayout(string[] a)
{
    if (a.Length < 2 || a.Length > 3)
    {
        Console.Error.WriteLine("usage: siegefx world layout <map-tank> <terrain-tank> [root-region]");
        return 1;
    }

    using var mapTank = TankFile.Open(a[0]);
    var mapReader = new TankReader(mapTank);
    using var terrainTank = TankFile.Open(a[1]);
    var terrainReader = new TankReader(terrainTank);

    string? rootHint = null;
    if (a.Length == 3)
    {
        rootHint = a[2].Replace('\\', '/');
        if (!rootHint.StartsWith('/')) rootHint = "/" + rootHint;
        if (rootHint.EndsWith('/')) rootHint = rootHint[..^1];
    }

    // Shared mesh index + SNO cache across every region: many tiles repeat, so
    // parsing each SNO once and reusing it keeps the whole-map layout cheap.
    var meshIndex = SnoMeshIndex.Build(terrainReader);
    var snoCache = new Dictionary<uint, SnoModel?>();
    SnoModel? Resolve(uint meshGuid)
    {
        if (snoCache.TryGetValue(meshGuid, out var cached)) return cached;
        SnoModel? sno = null;
        if (meshIndex.TryResolve(meshGuid, out var path))
        {
            try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
            catch { sno = null; }
        }
        snoCache[meshGuid] = sno;
        return sno;
    }

    var entries = new List<WorldLayout.RegionEntry>();
    var parseFailures = 0;
    var regionsWithStitch = 0;
    foreach (var path in mapReader.ListFiles())
    {
        if (!path.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase)) continue;
        // Strip the nodes.gas suffix so Path is the region root — makes the output
        // directly copy-pasteable into `region layout` or the viewer .bat.
        var regionPath = path[..^"/terrain_nodes/nodes.gas".Length];
        try
        {
            var graph = RegionGraph.Load(mapReader.ExtractToMemory(path));
            var layout = RegionLayout.Build(graph, Resolve);

            // Stitch helper is optional — some regions (isolated demo levels) ship without one.
            RegionStitchHelper? stitches = null;
            var stitchPath = regionPath + "/editor/stitch_helper.gas";
            if (mapReader.TryGetFile(stitchPath, out _))
            {
                try
                {
                    stitches = RegionStitchHelper.Load(mapReader.ExtractToMemory(stitchPath));
                    regionsWithStitch++;
                }
                catch (Exception sex)
                {
                    Console.Error.WriteLine($"  [stitch-fail] {stitchPath}: {sex.Message}");
                }
            }

            entries.Add(new WorldLayout.RegionEntry(regionPath, graph, layout, stitches));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [fail] {path}: {ex.Message}");
            parseFailures++;
        }
    }

    var world = WorldLayout.Build(entries, Resolve, rootHint);

    Console.WriteLine($"Map tank          : {a[0]}");
    Console.WriteLine($"Terrain tank      : {a[1]}");
    Console.WriteLine($"Regions discovered: {entries.Count}" + (parseFailures > 0 ? $" ({parseFailures} parse failure(s))" : ""));
    Console.WriteLine($"Regions w/ stitch : {regionsWithStitch}");
    Console.WriteLine($"Root region       : {world.RootRegion}");
    Console.WriteLine($"Placed regions    : {world.PlacedRegionCount} of {entries.Count}");
    Console.WriteLine($"Unreachable       : {world.UnreachableRegionCount}");
    Console.WriteLine($"Unresolved stitch : {world.UnresolvedStitchCount}");
    Console.WriteLine($"Dangling stitch   : {world.DanglingStitchCount}");
    Console.WriteLine($"Global snodes     : {world.Transforms.Count:N0}");

    // Spatial extent of the placed world, handy for sanity-checking that regions
    // fan out rather than all stacking at the origin.
    if (world.Transforms.Count > 0)
    {
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        foreach (var t in world.Transforms.Values)
        {
            if (t.M41 < minX) minX = t.M41;
            if (t.M42 < minY) minY = t.M42;
            if (t.M43 < minZ) minZ = t.M43;
            if (t.M41 > maxX) maxX = t.M41;
            if (t.M42 > maxY) maxY = t.M42;
            if (t.M43 > maxZ) maxZ = t.M43;
        }
        Console.WriteLine($"World extent      : ({minX,8:F1}, {minY,8:F1}, {minZ,8:F1}) .. ({maxX,8:F1}, {maxY,8:F1}, {maxZ,8:F1})");
    }

    var showOffsets = Math.Min(5, world.RegionOffsets.Count);
    if (showOffsets > 0) Console.WriteLine("Region offsets (first few):");
    var shown = 0;
    foreach (var (region, offset) in world.RegionOffsets)
    {
        if (shown >= showOffsets) break;
        Console.WriteLine($"  t=({offset.M41,8:F1},{offset.M42,8:F1},{offset.M43,8:F1})  {region}");
        shown++;
    }

    return parseFailures == 0 ? 0 : 4;
}

// Re-walks the door graph with verbose per-edge logging. The idea is to surface edges
// where the composed far transform lands somewhere wildly different from the parent
// (dY, distance, handedness flip, non-unit-scale), so we can pick one to solve by hand.
static int CmdRegionLayoutDiag(string[] a)
{
    if (a.Length < 3 || a.Length > 4)
    {
        Console.Error.WriteLine("usage: siegefx region layout-diag <map-tank> <terrain-tank> <region-path> [--all]");
        return 1;
    }
    var showAll = a.Length == 4 && a[3] == "--all";

    using var mapTank = TankFile.Open(a[0]);
    var mapReader = new TankReader(mapTank);
    using var terrainTank = TankFile.Open(a[1]);
    var terrainReader = new TankReader(terrainTank);

    var regionPath = a[2].Replace('\\', '/');
    if (!regionPath.StartsWith('/')) regionPath = "/" + regionPath;
    if (regionPath.EndsWith('/')) regionPath = regionPath[..^1];

    var graph = RegionGraph.Load(mapReader.ExtractToMemory(regionPath + "/terrain_nodes/nodes.gas"));

    var meshIndex = SnoMeshIndex.Build(terrainReader);
    var snoCache = new Dictionary<uint, SnoModel?>();
    SnoModel? Resolve(uint meshGuid)
    {
        if (snoCache.TryGetValue(meshGuid, out var cached)) return cached;
        SnoModel? sno = null;
        if (meshIndex.TryResolve(meshGuid, out var path))
        {
            try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
            catch { sno = null; }
        }
        snoCache[meshGuid] = sno;
        return sno;
    }

    var anchor = graph.TargetNodeGuid;
    if (!graph.TryGetNode(anchor, out _)) anchor = graph.Nodes[0].Guid;

    var transforms = new Dictionary<uint, System.Numerics.Matrix4x4> { [anchor] = System.Numerics.Matrix4x4.Identity };
    var parentOf = new Dictionary<uint, uint>();
    var queue = new Queue<uint>();
    queue.Enqueue(anchor);

    int edgeCount = 0;
    int suspicious = 0;
    Console.WriteLine($"Region : {regionPath}");
    Console.WriteLine($"Anchor : 0x{anchor:X8}");
    Console.WriteLine($"Nodes  : {graph.Nodes.Count}");
    Console.WriteLine();
    Console.WriteLine("edge# parent→far       dY     dist   hand fromDet  farDet  (localDoorId→farDoorId)");

    while (queue.Count > 0)
    {
        var curGuid = queue.Dequeue();
        if (!graph.TryGetNode(curGuid, out var curNode)) continue;
        var curSno = Resolve(curNode.MeshGuid);
        if (curSno is null) continue;
        var wCur = transforms[curGuid];

        foreach (var door in curNode.Doors)
        {
            if (!graph.TryGetNode(door.FarGuid, out var far)) continue;
            if (transforms.ContainsKey(far.Guid)) continue;
            var farSno = Resolve(far.MeshGuid);
            if (farSno is null) continue;
            var localDoor = FindDoorXf(curSno, door.LocalId);
            var farDoor = FindDoorXf(farSno, door.FarDoorId);
            if (localDoor is null || farDoor is null) continue;
            if (!System.Numerics.Matrix4x4.Invert(farDoor.Value, out var invFar)) continue;

            var flip = System.Numerics.Matrix4x4.CreateRotationY(MathF.PI);
            var wFar = wCur * localDoor.Value * flip * invFar;

            transforms[far.Guid] = wFar;
            parentOf[far.Guid] = curGuid;
            queue.Enqueue(far.Guid);

            var dY = wFar.M42 - wCur.M42;
            var dx = wFar.M41 - wCur.M41;
            var dz = wFar.M43 - wCur.M43;
            var dist = MathF.Sqrt(dx * dx + dY * dY + dz * dz);
            var locDet = Det3(localDoor.Value);
            var farDet = Det3(farDoor.Value);
            var hand = (locDet * farDet) < 0f ? "FLIP" : "ok  ";
            var isSuspicious = MathF.Abs(dY) > 20f || dist > 100f || MathF.Abs(locDet) < 0.95f || MathF.Abs(farDet) < 0.95f || hand == "FLIP";
            if (isSuspicious) suspicious++;
            if (showAll || isSuspicious)
            {
                Console.WriteLine($"{edgeCount,5} 0x{curGuid:X8}→0x{far.Guid:X8} {dY,7:F2} {dist,7:F2} {hand} {locDet,7:F3} {farDet,7:F3}  ({door.LocalId}→{door.FarDoorId}){(isSuspicious ? "  <-- SUS" : "")}");
            }
            edgeCount++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Walked {edgeCount} edge(s); {suspicious} suspicious; placed {transforms.Count}/{graph.Nodes.Count}");

    // Dump the FIRST suspicious edge in full detail.
    var firstSus = FirstSuspicious(graph, parentOf, transforms, Resolve);
    if (firstSus.HasValue)
    {
        var (parentGuid, childGuid, localDoorId, farDoorId) = firstSus.Value;
        Console.WriteLine();
        Console.WriteLine($"FIRST SUSPICIOUS EDGE: 0x{parentGuid:X8} → 0x{childGuid:X8}  (door {localDoorId}→{farDoorId})");
        DumpEdge(graph, Resolve, transforms, parentGuid, childGuid, localDoorId, farDoorId);
    }

    return 0;
}

static float Det3(System.Numerics.Matrix4x4 m) =>
    m.M11 * (m.M22 * m.M33 - m.M23 * m.M32)
  - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31)
  + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);

static System.Numerics.Matrix4x4? FindDoorXf(SnoModel sno, int doorId)
{
    foreach (var d in sno.Doors)
    {
        if (d.Id == (uint)doorId)
        {
            var xf = d.Transform;
            return new System.Numerics.Matrix4x4(
                xf.Row0.X, xf.Row0.Y, xf.Row0.Z, 0f,
                xf.Row1.X, xf.Row1.Y, xf.Row1.Z, 0f,
                xf.Row2.X, xf.Row2.Y, xf.Row2.Z, 0f,
                xf.Translation.X, xf.Translation.Y, xf.Translation.Z, 1f);
        }
    }
    return null;
}

static (uint parent, uint child, int localDoor, int farDoor)? FirstSuspicious(
    RegionGraph graph,
    Dictionary<uint, uint> parentOf,
    Dictionary<uint, System.Numerics.Matrix4x4> transforms,
    Func<uint, SnoModel?> resolve)
{
    foreach (var kv in parentOf)
    {
        var child = kv.Key;
        var parent = kv.Value;
        var wCur = transforms[parent];
        var wFar = transforms[child];
        var dY = wFar.M42 - wCur.M42;
        if (MathF.Abs(dY) <= 20f) continue;
        if (!graph.TryGetNode(parent, out var parentNode)) continue;
        foreach (var d in parentNode.Doors)
        {
            if (d.FarGuid == child)
                return (parent, child, d.LocalId, d.FarDoorId);
        }
    }
    return null;
}

static void DumpEdge(
    RegionGraph graph,
    Func<uint, SnoModel?> resolve,
    Dictionary<uint, System.Numerics.Matrix4x4> transforms,
    uint parentGuid, uint childGuid, int localDoorId, int farDoorId)
{
    if (!graph.TryGetNode(parentGuid, out var parent) || !graph.TryGetNode(childGuid, out var child)) return;
    var parentSno = resolve(parent.MeshGuid);
    var childSno = resolve(child.MeshGuid);
    if (parentSno is null || childSno is null) return;

    var wCur = transforms[parentGuid];
    var wFar = transforms[childGuid];
    var localDoor = FindDoorXf(parentSno, localDoorId)!.Value;
    var farDoor = FindDoorXf(childSno, farDoorId)!.Value;

    Console.WriteLine($"  parent mesh=0x{parent.MeshGuid:X8}  world t=({wCur.M41,8:F2},{wCur.M42,8:F2},{wCur.M43,8:F2})");
    Console.WriteLine($"  child  mesh=0x{child.MeshGuid:X8}  world t=({wFar.M41,8:F2},{wFar.M42,8:F2},{wFar.M43,8:F2})");
    Console.WriteLine();
    Console.WriteLine($"  localDoor (parent's door {localDoorId}) det={Det3(localDoor):F4}");
    PrintMat(localDoor, "    ");
    Console.WriteLine($"  farDoor (child's door {farDoorId}) det={Det3(farDoor):F4}");
    PrintMat(farDoor, "    ");

    System.Numerics.Matrix4x4.Invert(farDoor, out var invFar);
    var flipY = System.Numerics.Matrix4x4.CreateRotationY(MathF.PI);
    var composed = wCur * localDoor * flipY * invFar;
    Console.WriteLine($"  wCur * localDoor * flipY * invFar  (current formula)");
    PrintMat(composed, "    ");
}

static void PrintMat(System.Numerics.Matrix4x4 m, string indent)
{
    Console.WriteLine($"{indent}[{m.M11,7:F3} {m.M12,7:F3} {m.M13,7:F3}]");
    Console.WriteLine($"{indent}[{m.M21,7:F3} {m.M22,7:F3} {m.M23,7:F3}]");
    Console.WriteLine($"{indent}[{m.M31,7:F3} {m.M32,7:F3} {m.M33,7:F3}]");
    Console.WriteLine($"{indent}t ({m.M41,7:F3} {m.M42,7:F3} {m.M43,7:F3})");
}

static int CmdGasFuzz(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx gas fuzz <tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);

    var total = 0;
    var failed = 0;
    var totalBytes = 0L;
    foreach (var path in reader.ListFiles())
    {
        if (!path.EndsWith(".gas", StringComparison.OrdinalIgnoreCase)) continue;
        total++;
        byte[] bytes;
        try { bytes = reader.ExtractToMemory(path); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [extract-fail] {path}: {ex.Message}");
            failed++;
            continue;
        }
        totalBytes += bytes.Length;
        try { _ = GasDocument.Load(bytes); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [parse-fail]   {path}: {ex.Message}");
            failed++;
        }
    }
    Console.WriteLine($"fuzzed {total} .gas file(s), {totalBytes:N0} bytes total; {failed} failure(s)");
    return failed == 0 ? 0 : 4;
}

static int CmdSnoInfo(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx sno info <file.sno>"); return 1; }
    var data = File.ReadAllBytes(a[0]);
    Console.WriteLine($"File      : {a[0]}");
    Console.WriteLine($"Size      : {data.Length:N0} bytes");

    var sno = SnoModel.Load(data);
    Console.WriteLine($"Magic     : {sno.Magic}");
    Console.WriteLine($"Version   : {sno.Version}");
    Console.WriteLine($"Bounds    : {sno.MinBounds} .. {sno.MaxBounds}");
    Console.WriteLine($"Data CRC32: 0x{sno.DataCrc32:X8}");
    Console.WriteLine($"Spots     : {sno.Spots.Length}");
    Console.WriteLine($"Doors     : {sno.Doors.Length}");
    Console.WriteLine($"Corners   : {sno.Corners.Length}");
    Console.WriteLine($"Surfaces  : {sno.Surfaces.Length} (total {sno.TotalTriangleCount} triangles)");
    for (var i = 0; i < sno.Surfaces.Length; i++)
    {
        var s = sno.Surfaces[i];
        Console.WriteLine($"  [{i}] '{s.TextureName}'  start={s.StartCorner} span={s.SpanCorner} corners={s.CornerCount} tris={s.TriangleCount}");
    }
    for (var i = 0; i < sno.Doors.Length; i++)
    {
        var d = sno.Doors[i];
        Console.WriteLine($"  door[{i}] id={d.Id} hotSpots={d.HotSpots.Length}");
    }
    for (var i = 0; i < sno.Spots.Length; i++)
    {
        Console.WriteLine($"  spot[{i}] name='{sno.Spots[i].Name}'");
    }
    if (sno.LogicalGroupings.Length > 0)
    {
        int floor = 0, water = 0, ignored = 0, navFaces = 0;
        foreach (var g in sno.LogicalGroupings)
        {
            if (g.Kind == SnoModel.FloorKind.Floor) floor++;
            else if (g.Kind == SnoModel.FloorKind.Water) water++;
            else if (g.Kind == SnoModel.FloorKind.Ignored) ignored++;
            navFaces += g.Faces.Length;
        }
        Console.WriteLine($"Nav groups: {sno.LogicalGroupings.Length} (floor={floor} water={water} ignored={ignored}), {navFaces} nav faces");
    }
    return 0;
}

static int CmdSnoNav(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx sno nav <file.sno>"); return 1; }
    var data = File.ReadAllBytes(a[0]);
    var sno = SnoModel.Load(data);
    Console.WriteLine($"File    : {a[0]}");
    Console.WriteLine($"Bounds  : {sno.MinBounds} .. {sno.MaxBounds}");
    Console.WriteLine($"Groups  : {sno.LogicalGroupings.Length}");
    if (sno.LogicalGroupings.Length == 0)
    {
        Console.WriteLine("(no logical-grouping nav section — SNO predates nav bake or was stripped)");
        return 0;
    }
    int totalFloor = 0, totalWater = 0, totalIgnored = 0;
    for (var i = 0; i < sno.LogicalGroupings.Length; i++)
    {
        var g = sno.LogicalGroupings[i];
        var tag = g.Kind switch
        {
            SnoModel.FloorKind.Floor   => "FLOOR  ",
            SnoModel.FloorKind.Water   => "WATER  ",
            SnoModel.FloorKind.Ignored => "IGNORED",
            _                          => $"0x{(uint)g.Kind:X8}",
        };
        if (g.Kind == SnoModel.FloorKind.Floor) totalFloor += g.Faces.Length;
        else if (g.Kind == SnoModel.FloorKind.Water) totalWater += g.Faces.Length;
        else if (g.Kind == SnoModel.FloorKind.Ignored) totalIgnored += g.Faces.Length;
        Console.WriteLine($"  [{i,3}] id={g.Id,3} {tag}  bbox=({g.BoundsMin.X,7:F1},{g.BoundsMin.Y,7:F1},{g.BoundsMin.Z,7:F1}) .. ({g.BoundsMax.X,7:F1},{g.BoundsMax.Y,7:F1},{g.BoundsMax.Z,7:F1})  faces={g.Faces.Length}");
    }
    Console.WriteLine($"Totals  : floor={totalFloor}, water={totalWater}, ignored={totalIgnored}");
    return 0;
}

static int CmdAspInfo(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx asp info <file.asp>"); return 1; }
    var data = File.ReadAllBytes(a[0]);
    Console.WriteLine($"File      : {a[0]}");
    Console.WriteLine($"Size      : {data.Length:N0} bytes");

    var mesh = AspMesh.Load(data);
    Console.WriteLine($"Version   : {mesh.AspVersionMajor}.{mesh.AspVersionMinor}");
    Console.WriteLine($"Mesh      : {mesh.MeshName}");
    Console.WriteLine($"Textures  : {mesh.TextureNames.Count}" + (mesh.TextureNames.Count > 0 ? $"  ({string.Join(", ", mesh.TextureNames)})" : ""));
    Console.WriteLine($"Bones     : {mesh.BoneCount}");
    Console.WriteLine($"Vertices  : {mesh.Positions.Length}");
    Console.WriteLine($"Corners   : {mesh.Corners.Length}");
    Console.WriteLine($"Triangles : {mesh.TriangleCount}");
    if (mesh.HasSkin)
    {
        // Average active (non-zero-weight) influences per corner, and the max single bone
        // index observed across all corners — cheap sanity probe for downstream skinning.
        int activeTotal = 0, maxBone = -1;
        for (var i = 0; i < mesh.SkinWeights.Length; i++)
        {
            var w = mesh.SkinWeights[i]; var b = mesh.SkinBones[i];
            if (w.X > 0) { activeTotal++; maxBone = Math.Max(maxBone, (int)(b        & 0xFF)); }
            if (w.Y > 0) { activeTotal++; maxBone = Math.Max(maxBone, (int)((b >> 8)  & 0xFF)); }
            if (w.Z > 0) { activeTotal++; maxBone = Math.Max(maxBone, (int)((b >> 16) & 0xFF)); }
            if (w.W > 0) { activeTotal++; maxBone = Math.Max(maxBone, (int)((b >> 24) & 0xFF)); }
        }
        var avg = mesh.SkinWeights.Length > 0 ? (double)activeTotal / mesh.SkinWeights.Length : 0.0;
        Console.WriteLine($"Skin      : {mesh.SkinWeights.Length} weighted corner(s), avg {avg:F2} influences/corner, max bone={maxBone}");
    }

    var chunks = AspScanner.Scan(data);
    Console.WriteLine($"Chunks    : {chunks.Count}");
    foreach (var c in chunks)
        Console.WriteLine($"  0x{c.Offset:X8}  {c.Id}  v{c.Version}");
    return 0;
}

static int CmdAspSkeleton(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx asp skeleton <file.asp>"); return 1; }
    var mesh = AspMesh.Load(File.ReadAllBytes(a[0]));
    Console.WriteLine($"File   : {a[0]}");
    Console.WriteLine($"Mesh   : {mesh.MeshName}");
    Console.WriteLine($"Bones  : {mesh.BoneCount}");
    if (mesh.BoneCount == 0) return 0;

    // Print the hierarchy as an indented tree. Orphans (parent out-of-range, pointing at
    // a bone we somehow haven't emitted, cycles) fall through to an "orphans" section
    // so asset bugs surface instead of silently missing bones.
    var children = new List<int>[mesh.BoneCount];
    for (var i = 0; i < mesh.BoneCount; i++) children[i] = new List<int>();
    var roots = new List<int>();
    for (var i = 0; i < mesh.BoneCount; i++)
    {
        var p = mesh.BoneParents[i];
        if (p < 0 || p >= mesh.BoneCount || p == i) roots.Add(i);
        else children[p].Add(i);
    }
    var emitted = new bool[mesh.BoneCount];
    void Walk(int idx, int depth)
    {
        if (emitted[idx]) return;
        emitted[idx] = true;
        var t = mesh.BindPose[idx];
        var tr = t.Translation;
        Console.WriteLine($"  {new string(' ', depth * 2)}[{idx,3}] {mesh.BoneNames[idx]}  pos=({tr.X:F2},{tr.Y:F2},{tr.Z:F2})");
        foreach (var c in children[idx]) Walk(c, depth + 1);
    }
    foreach (var r in roots) Walk(r, 0);
    var orphans = Enumerable.Range(0, mesh.BoneCount).Where(i => !emitted[i]).ToArray();
    if (orphans.Length > 0)
    {
        Console.WriteLine($"Orphans: {orphans.Length}");
        foreach (var o in orphans)
            Console.WriteLine($"  [{o,3}] {mesh.BoneNames[o]}  parent={mesh.BoneParents[o]}");
    }
    return 0;
}

static int CmdAspFuzz(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx asp fuzz <tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    int total = 0, failed = 0, skeletal = 0, skinned = 0;
    long totalBytes = 0;
    foreach (var path in reader.ListFiles())
    {
        if (!path.EndsWith(".asp", StringComparison.OrdinalIgnoreCase)) continue;
        total++;
        byte[] bytes;
        try { bytes = reader.ExtractToMemory(path); }
        catch (Exception ex) { Console.Error.WriteLine($"  [extract-fail] {path}: {ex.Message}"); failed++; continue; }
        totalBytes += bytes.Length;
        try
        {
            var mesh = AspMesh.Load(bytes);
            if (mesh.BoneCount > 0) skeletal++;
            if (mesh.HasSkin) skinned++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [parse-fail] {path}: {ex.Message}");
            failed++;
        }
    }
    Console.WriteLine($"fuzzed {total} .asp file(s), {totalBytes:N0} bytes; {failed} failure(s), {skeletal} w/skeleton, {skinned} w/skin");
    return failed == 0 ? 0 : 4;
}

static int CmdAnimPose(string[] a)
{
    if (a.Length is < 2 or > 3)
    {
        Console.Error.WriteLine("usage: siegefx anim pose <file.asp> <file.prs> [time-seconds]");
        return 1;
    }
    var mesh = AspMesh.Load(File.ReadAllBytes(a[0]));
    var anim = PrsAnimation.Load(File.ReadAllBytes(a[1]));
    var t    = a.Length == 3 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : anim.AnimLength * 0.5f;

    Console.WriteLine($"Mesh   : {a[0]}  bones={mesh.BoneCount} corners={mesh.Corners.Length} skin={mesh.HasSkin}");
    Console.WriteLine($"Clip   : {a[1]}  bones={anim.NumBones} length={anim.AnimLength:F3}s");
    Console.WriteLine($"Time   : {t:F3}s ({(anim.AnimLength > 0 ? t / anim.AnimLength : 0):P0} of clip)");

    // Bone-name overlap is the canonical test for a clip vs mesh match: shared bones get
    // animated, the rest hold their bind pose. A near-zero overlap usually means a wrong
    // pairing (a clip targeted at a different rig).
    var meshNames = new HashSet<string>(mesh.BoneNames);
    var matched = anim.BoneNames.Count(n => meshNames.Contains(n));
    Console.WriteLine($"Match  : {matched} of {anim.NumBones} clip bones map to mesh bones");

    var skin = AnimationRuntime.ComputeSkinMatrices(mesh, anim, t);
    if (skin.Length == 0)
    {
        Console.WriteLine("(static mesh — nothing to pose)");
        return 0;
    }
    var posed = AnimationRuntime.SkinCorners(mesh, skin);
    var bindAabb  = AabbOf(IndexedPositions(mesh));
    var posedAabb = AabbOf(posed);
    Console.WriteLine($"Bind AABB  : min=({bindAabb.min.X:F2},{bindAabb.min.Y:F2},{bindAabb.min.Z:F2}) max=({bindAabb.max.X:F2},{bindAabb.max.Y:F2},{bindAabb.max.Z:F2})");
    Console.WriteLine($"Posed AABB : min=({posedAabb.min.X:F2},{posedAabb.min.Y:F2},{posedAabb.min.Z:F2}) max=({posedAabb.max.X:F2},{posedAabb.max.Y:F2},{posedAabb.max.Z:F2})");

    // Spot-check the first few bones' world translations, which is the easiest visual
    // sanity that the pose composition isn't mirrored or scaled.
    Console.WriteLine("First bones:");
    for (var i = 0; i < Math.Min(8, mesh.BoneCount); i++)
    {
        var m = skin[i];
        Console.WriteLine($"  [{i,3}] {mesh.BoneNames[i],-24}  skinT=({m.M41:F3},{m.M42:F3},{m.M43:F3})");
    }
    return 0;
}

static System.Numerics.Vector3[] IndexedPositions(AspMesh mesh)
{
    var v = new System.Numerics.Vector3[mesh.Corners.Length];
    for (var i = 0; i < mesh.Corners.Length; i++) v[i] = mesh.Positions[mesh.Corners[i].VertexIndex];
    return v;
}

static (System.Numerics.Vector3 min, System.Numerics.Vector3 max) AabbOf(System.Numerics.Vector3[] pts)
{
    if (pts.Length == 0) return (System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero);
    var min = pts[0]; var max = pts[0];
    for (var i = 1; i < pts.Length; i++)
    {
        min = System.Numerics.Vector3.Min(min, pts[i]);
        max = System.Numerics.Vector3.Max(max, pts[i]);
    }
    return (min, max);
}

static int CmdAnimFuzz(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx anim fuzz <tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    int total = 0, failed = 0, ok = 0, skipped = 0;
    foreach (var path in reader.ListFiles())
    {
        if (!path.EndsWith(".prs", StringComparison.OrdinalIgnoreCase)) continue;
        total++;
        byte[] bytes;
        try { bytes = reader.ExtractToMemory(path); }
        catch (Exception ex) { Console.Error.WriteLine($"  [extract-fail] {path}: {ex.Message}"); failed++; continue; }
        PrsAnimation anim;
        try { anim = PrsAnimation.Load(bytes); }
        catch (NotSupportedException) { skipped++; continue; } // legacy v0x202 / v0x302 formats — covered by prs fuzz
        catch (Exception ex) { Console.Error.WriteLine($"  [parse-fail] {path}: {ex.Message}"); failed++; continue; }

        // Sample every keyed bone at start, mid, and end of the clip; if any quaternion
        // slerp or vector lerp produces NaN/Inf we want to know about it before runtime.
        var times = new[] { 0f, anim.AnimLength * 0.5f, anim.AnimLength };
        var bad = false;
        foreach (var t in times)
        {
            for (var b = 0; b < anim.NumBones; b++)
            {
                var (r, p) = AnimationRuntime.EvaluateBone(anim, b, t);
                if (r is { } rv && (!float.IsFinite(rv.X) || !float.IsFinite(rv.Y) || !float.IsFinite(rv.Z) || !float.IsFinite(rv.W)))
                { Console.Error.WriteLine($"  [pose-fail] {path}: bone {b} rot at t={t:F3} not finite"); bad = true; break; }
                if (p is { } pv && (!float.IsFinite(pv.X) || !float.IsFinite(pv.Y) || !float.IsFinite(pv.Z)))
                { Console.Error.WriteLine($"  [pose-fail] {path}: bone {b} pos at t={t:F3} not finite"); bad = true; break; }
            }
            if (bad) break;
        }
        if (bad) failed++; else ok++;
    }
    Console.WriteLine($"anim fuzz: {total} .prs file(s), {ok} sampled OK, {skipped} legacy-version skipped, {failed} failure(s)");
    return failed == 0 ? 0 : 4;
}

static int CmdTankInfo(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx tank info <tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var h = tank.Header;
    Console.WriteLine($"File         : {tank.Path}");
    Console.WriteLine($"Size         : {tank.SizeBytes:N0} bytes");
    Console.WriteLine($"Product      : {h.ProductId} ({(h.IsDs1 ? "Dungeon Siege 1/LoA" : h.IsDs2 ? "Dungeon Siege 2" : "unknown")})");
    Console.WriteLine($"Tank id      : {h.TankId}");
    Console.WriteLine($"Header ver   : {TankVersion.ToString(h.HeaderVersion)}");
    Console.WriteLine($"Creator      : {h.CreatorId}");
    Console.WriteLine($"Priority     : {h.Priority}");
    Console.WriteLine($"Flags        : {h.Flags}");
    Console.WriteLine($"Product ver  : {h.ProductVersion}");
    Console.WriteLine($"Minimum ver  : {h.MinimumVersion}");
    Console.WriteLine($"GUID         : {h.Guid}");
    Console.WriteLine($"Index CRC32  : 0x{h.IndexCrc32:X8}");
    Console.WriteLine($"Data  CRC32  : 0x{h.DataCrc32:X8}");
    Console.WriteLine($"Build time   : {h.UtcBuildTime}");
    Console.WriteLine($"DirSet  @    : 0x{h.DirSetOffset:X8}");
    Console.WriteLine($"FileSet @    : 0x{h.FileSetOffset:X8}");
    Console.WriteLine($"Data    @    : 0x{h.DataOffset:X8}");
    Console.WriteLine($"Title        : {h.TitleText}");
    Console.WriteLine($"Author       : {h.AuthorText}");
    Console.WriteLine($"Copyright    : {h.CopyrightText}");
    Console.WriteLine($"Build text   : {h.BuildText}");
    Console.WriteLine($"Description  : {h.DescriptionText}");

    var reader = new TankReader(tank);
    Console.WriteLine($"Directories  : {reader.DirCount}");
    Console.WriteLine($"Files        : {reader.FileCount}");
    return 0;
}

static int CmdTankList(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx tank list <tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);

    foreach (var path in reader.ListFiles().OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
    {
        if (!reader.TryGetFile(path, out var file)) continue;
        var tag = file.Format switch
        {
            TankDataFormat.Raw  => "raw ",
            TankDataFormat.Zlib => "zlib",
            TankDataFormat.Lzo  => "lzo ",
            _                   => "?   ",
        };
        var invalid = file.IsInvalid ? " [INVALID]" : "";
        Console.WriteLine($"  {tag}  {file.Size,10:N0}  {path}{invalid}");
    }

    Console.WriteLine();
    Console.WriteLine($"{reader.FileCount} file(s) across {reader.DirCount} dir(s)");
    return 0;
}

static int CmdTankExtract(string[] a)
{
    if (a.Length < 2 || a.Length > 3)
    {
        Console.Error.WriteLine("usage: siegefx tank extract <tank> <resource-path> [dest-file]");
        return 1;
    }

    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var resource = a[1].Replace('\\', '/');
    if (!resource.StartsWith('/')) resource = "/" + resource;

    var dest = a.Length == 3 ? a[2] : Path.GetFileName(resource);
    reader.ExtractToFile(resource, dest);
    Console.WriteLine($"wrote {new FileInfo(dest).Length:N0} bytes -> {dest}");
    return 0;
}

static int CmdRawInfo(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx raw info <file.raw>"); return 1; }
    var data = File.ReadAllBytes(a[0]);
    var img = RawImage.Load(data);
    Console.WriteLine($"File         : {a[0]}");
    Console.WriteLine($"Size         : {data.Length:N0} bytes");
    Console.WriteLine($"Dimensions   : {img.Width} x {img.Height}");
    Console.WriteLine($"Surfaces     : {img.SurfaceCount}");
    Console.WriteLine($"Pixel bytes  : {img.Pixels.Length:N0}");
    for (var i = 0; i < img.SurfaceCount; i++)
    {
        var w = img.GetSurfaceWidth(i);
        var h = img.GetSurfaceHeight(i);
        Console.WriteLine($"  surface {i,2}: {w,5} x {h,-5}  @ offset 0x{img.GetSurfaceOffset(i):X8}");
    }
    return 0;
}

static int CmdRawDecode(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("usage: siegefx raw decode <file.raw> [out.png] [--surface N] [--all]");
        return 1;
    }

    var src = a[0];
    string? explicitDest = null;
    var surfaceIndex = 0;
    var allSurfaces = false;

    for (var i = 1; i < a.Length; i++)
    {
        var tok = a[i];
        if (tok == "--surface")
        {
            if (i + 1 >= a.Length) { Console.Error.WriteLine("--surface needs a value"); return 1; }
            surfaceIndex = int.Parse(a[++i]);
        }
        else if (tok == "--all")
        {
            allSurfaces = true;
        }
        else if (!tok.StartsWith("--"))
        {
            explicitDest = tok;
        }
        else
        {
            Console.Error.WriteLine($"unknown option: {tok}");
            return 1;
        }
    }

    var data = File.ReadAllBytes(src);
    var img = RawImage.Load(data);

    if (allSurfaces)
    {
        var baseDir = Path.GetDirectoryName(src) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(src);
        for (var i = 0; i < img.SurfaceCount; i++)
        {
            var outPath = Path.Combine(baseDir, $"{stem}.{i}.png");
            WritePng(img, i, outPath);
            Console.WriteLine($"surface {i}: {img.GetSurfaceWidth(i)}x{img.GetSurfaceHeight(i)} -> {outPath}");
        }
        return 0;
    }

    if (surfaceIndex < 0 || surfaceIndex >= img.SurfaceCount)
    {
        Console.Error.WriteLine($"surface {surfaceIndex} out of range (0..{img.SurfaceCount - 1})");
        return 1;
    }

    var dest = explicitDest ?? Path.ChangeExtension(src, ".png");
    WritePng(img, surfaceIndex, dest);
    Console.WriteLine($"surface {surfaceIndex}: {img.GetSurfaceWidth(surfaceIndex)}x{img.GetSurfaceHeight(surfaceIndex)} -> {dest}");
    return 0;
}

static void WritePng(RawImage img, int surfaceIndex, string destPath)
{
    var rgba = img.GetSurfaceRgba(surfaceIndex);
    var w = img.GetSurfaceWidth(surfaceIndex);
    var h = img.GetSurfaceHeight(surfaceIndex);

    var dir = Path.GetDirectoryName(destPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

    using var fs = File.Create(destPath);
    Png.EncodeRgba(fs, rgba, w, h);
}

static int DispatchTemplates(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx templates <list|show> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "list" => CmdTemplatesList(a[1..]),
        "show" => CmdTemplatesShow(a[1..]),
        _      => UnknownCommand("templates " + a[0]),
    };
}

static int CmdTemplatesList(string[] a)
{
    string? prefix = null;
    string? tagFilter = null;
    var rest = new List<string>();
    foreach (var x in a)
    {
        if (x.StartsWith("--prefix=", StringComparison.Ordinal)) prefix = x["--prefix=".Length..];
        else if (x.StartsWith("--tag=", StringComparison.Ordinal)) tagFilter = x["--tag=".Length..];
        else rest.Add(x);
    }
    if (rest.Count != 1) { Console.Error.WriteLine("usage: siegefx templates list <tank> [--prefix=P] [--tag=T]"); return 1; }

    using var tank = TankFile.Open(rest[0]);
    var reader = new TankReader(tank);
    var (store, diags) = TemplateStore.LoadFromTank(reader);

    Console.WriteLine($"loaded {store.Count} templates from {rest[0]}");
    if (diags.Count > 0)
    {
        Console.WriteLine($"{diags.Count} diagnostic(s):");
        foreach (var d in diags.Take(8)) Console.WriteLine($"  !! {d}");
        if (diags.Count > 8) Console.WriteLine($"  ... +{diags.Count - 8} more");
    }

    var shown = 0;
    foreach (var t in store.All.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
    {
        if (tagFilter is not null && !string.Equals(t.TypeTag, tagFilter, StringComparison.OrdinalIgnoreCase)) continue;
        if (prefix is not null && !t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
        var parent = t.SpecializesName is null ? "" : $"  : {t.SpecializesName}";
        Console.WriteLine($"  [{t.TypeTag}] {t.Name}{parent}");
        shown++;
        if (shown >= 60) { Console.WriteLine($"  ... (showing first 60; {store.Count - shown}+ more)"); break; }
    }
    return 0;
}

static int CmdTemplatesShow(string[] a)
{
    if (a.Length != 2) { Console.Error.WriteLine("usage: siegefx templates show <tank> <name>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var (store, diags) = TemplateStore.LoadFromTank(reader);
    if (!store.TryGet(a[1], out var t))
    {
        Console.Error.WriteLine($"template '{a[1]}' not found (store has {store.Count} templates; {diags.Count} diagnostic[s])");
        return 1;
    }

    Console.WriteLine($"name       : {t.Name}");
    Console.WriteLine($"tag        : [t:{t.TypeTag}]");
    Console.WriteLine($"source     : {t.SourcePath}");
    // Chain is useful because DS1 uses inheritance heavily — e.g. 3W_goblin_grunt →
    // 3W_base_goblin → actor_evil → actor — and callers will walk it to find fields.
    Console.Write("chain      :");
    for (var cur = t; cur is not null; cur = cur.Specializes) Console.Write($" {cur.Name}");
    Console.WriteLine();

    // Quick peek at commonly-queried resolved fields so we can tell visually that the
    // chain walker actually found an ancestor-defined attribute.
    Console.WriteLine();
    Console.WriteLine("resolved (chain-walked):");
    Print("  aspect.model        =", store.GetAttribute(t, "aspect", "model"));
    Print("  aspect.life         =", store.GetAttribute(t, "aspect", "life"));
    Print("  common.screen_name  =", store.GetAttribute(t, "common", "screen_name"));
    Print("  body.chore_dictionary.chore_prefix =",
        store.GetAttribute(t, "body", "chore_dictionary", "chore_prefix"));

    var chores = store.GetSection(t, "body", "chore_dictionary");
    if (chores is not null)
    {
        Console.WriteLine();
        Console.WriteLine($"chore dictionary ({chores.Children.Count} entries):");
        foreach (var c in chores.Children)
        {
            var skrit = TemplateStore.FindAttr(c, "skrit") ?? "?";
            Console.WriteLine($"  [{c.Header}] skrit={skrit}");
        }
    }
    return 0;

    static void Print(string label, string? val) =>
        Console.WriteLine(val is null ? $"{label} <none>" : $"{label} {val}");
}

static int CmdRegionActors(string[] a)
{
    if (a.Length != 2) { Console.Error.WriteLine("usage: siegefx region actors <map-tank> <region-path>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var (actors, diags) = RegionObjects.LoadActors(reader, a[1]);

    Console.WriteLine($"region    : {a[1]}");
    Console.WriteLine($"actors    : {actors.Count}");
    if (diags.Count > 0)
    {
        Console.WriteLine($"diagnostics: {diags.Count}");
        foreach (var d in diags.Take(5)) Console.WriteLine($"  !! {d}");
        if (diags.Count > 5) Console.WriteLine($"  ... +{diags.Count - 5} more");
    }

    // Tally by template so we can see the archetype distribution at a glance.
    var byTemplate = actors
        .GroupBy(x => x.TemplateName, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(g => g.Count())
        .ToList();
    Console.WriteLine();
    Console.WriteLine($"by template ({byTemplate.Count} distinct):");
    foreach (var g in byTemplate.Take(15)) Console.WriteLine($"  {g.Count(),4}  {g.Key}");
    if (byTemplate.Count > 15) Console.WriteLine($"  ... +{byTemplate.Count - 15} more templates");

    Console.WriteLine();
    Console.WriteLine("first 8 placements:");
    foreach (var act in actors.Take(8))
    {
        var p = act.Placement;
        Console.WriteLine($"  {act}  pos=({p.LocalPosition.X:F2},{p.LocalPosition.Y:F2},{p.LocalPosition.Z:F2}) node=0x{p.NodeGuid:x8}");
    }
    return 0;
}

static int CmdRegionSpawnProbe(string[] a)
{
    if (a.Length != 4) { Console.Error.WriteLine("usage: siegefx region spawn-probe <map-tank> <logic-tank> <objects-tank> <region-path>"); return 1; }
    using var mapTank     = TankFile.Open(a[0]);
    using var logicTank   = TankFile.Open(a[1]);
    using var objectsTank = TankFile.Open(a[2]);
    var mapReader     = new TankReader(mapTank);
    var logicReader   = new TankReader(logicTank);
    var objectsReader = new TankReader(objectsTank);

    // Template store reads from Logic.dsres (where all templates live).
    var (store, tdiags) = TemplateStore.LoadFromTank(logicReader);
    Console.WriteLine($"templates : {store.Count} (diagnostics: {tdiags.Count})");

    var (actors, adiags) = RegionObjects.LoadActors(mapReader, a[3]);
    Console.WriteLine($"actors    : {actors.Count} (diagnostics: {adiags.Count})");

    // Resolver spans Objects.dsres (models + anims) and Logic.dsres (skrits).
    var resolver = new AssetResolver();
    resolver.Add(objectsReader, "Objects.dsres");
    resolver.Add(logicReader,   "Logic.dsres");

    // Walk actor list, try to resolve (template, model, chore_default skrit, chore_default
    // anim) for each. Tally hits vs misses so we can see if the conventions hold up.
    int tResolved = 0, tMissing = 0;
    int mHit = 0, mMiss = 0;
    int sHit = 0, sMiss = 0;
    int aHit = 0, aMiss = 0;
    var missTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var perTemplate = actors.GroupBy(x => x.TemplateName, StringComparer.OrdinalIgnoreCase);
    foreach (var g in perTemplate)
    {
        if (!store.TryGet(g.Key, out var t))
        {
            tMissing += g.Count();
            missTemplates.Add(g.Key);
            continue;
        }
        tResolved += g.Count();

        var model = store.GetAttribute(t, "aspect", "model");
        if (model is not null && resolver.TryLoadModel(model, out _)) mHit += g.Count(); else mMiss += g.Count();

        var chorePrefix = store.GetAttribute(t, "body", "chore_dictionary", "chore_prefix");
        var defaultSection = store.GetSection(t, "body", "chore_dictionary", "chore_default");
        var skritName = defaultSection is null ? null : TemplateStore.FindAttr(defaultSection, "skrit");
        var animFiles = defaultSection is null ? null : TemplateStore.FindChild(defaultSection, "anim_files");
        var animSuffix = animFiles?.Attributes.FirstOrDefault().Value;
        var stances = ParseChoreStances(defaultSection is null ? null : TemplateStore.FindAttr(defaultSection, "chore_stances"));

        if (skritName is not null && resolver.TryLoadSkrit(skritName, out _)) sHit += g.Count(); else sMiss += g.Count();
        if (chorePrefix is not null && animSuffix is not null
            && TryResolveAnyStance(resolver, chorePrefix, stances, animSuffix)) aHit += g.Count(); else aMiss += g.Count();
    }

    Console.WriteLine();
    Console.WriteLine("resolution tallies (per actor, weighted by count):");
    Console.WriteLine($"  template resolved  : {tResolved}/{actors.Count}");
    if (tMissing > 0) Console.WriteLine($"    missing templates: {string.Join(", ", missTemplates.Take(6))}{(missTemplates.Count > 6 ? ", ..." : "")}");
    Console.WriteLine($"  model .asp found   : {mHit}/{actors.Count}  (miss {mMiss})");
    Console.WriteLine($"  chore_default skrit: {sHit}/{actors.Count}  (miss {sMiss})");
    Console.WriteLine($"  chore_default anim : {aHit}/{actors.Count}  (miss {aMiss})");

    // Pick the three most popular templates and fully resolve them so we can eyeball
    // the final asset paths the spawner will use.
    Console.WriteLine();
    Console.WriteLine("sample resolutions (top 3 templates in region):");
    foreach (var g in actors.GroupBy(x => x.TemplateName).OrderByDescending(x => x.Count()).Take(3))
    {
        Console.WriteLine($"  [{g.Key}] x{g.Count()}");
        if (!store.TryGet(g.Key, out var t)) { Console.WriteLine("    <template not in store>"); continue; }

        var model = store.GetAttribute(t, "aspect", "model") ?? "<none>";
        var chorePrefix = store.GetAttribute(t, "body", "chore_dictionary", "chore_prefix") ?? "<none>";
        var defaultSection = store.GetSection(t, "body", "chore_dictionary", "chore_default");
        var skritName = defaultSection is null ? "<none>" : TemplateStore.FindAttr(defaultSection, "skrit") ?? "<none>";
        var animFiles = defaultSection is null ? null : TemplateStore.FindChild(defaultSection, "anim_files");
        var animSuffix = animFiles?.Attributes.FirstOrDefault().Value ?? "<none>";
        var stancesAttr = defaultSection is null ? "<none>" : TemplateStore.FindAttr(defaultSection, "chore_stances") ?? "<none>";

        Console.WriteLine($"    model = {model}.asp   prefix = {chorePrefix}   stances = {stancesAttr}   skrit = {skritName}   anim_suffix = {animSuffix}");
    }
    return 0;
}

// DS1 template chore sections carry `chore_stances = 0,1,3,6;` — a comma-separated list
// of stance numbers (weapon/combat postures). For each listed stance, a corresponding
// animation file {prefix}{stance}_{suffix}.prs exists. We parse the list and drop any
// tokens that don't parse as ints; empty/null yields {0} so the legacy probe still runs.
static int[] ParseChoreStances(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return new[] { 0 };
    var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var list = new List<int>(parts.Length);
    foreach (var p in parts) if (int.TryParse(p, out var n)) list.Add(n);
    return list.Count == 0 ? new[] { 0 } : list.ToArray();
}

// Tries each candidate stance in order; first hit wins. Template lists stances from
// most-common to less, so first-hit approximates what DS1 would pick for the default
// idle state before combat changes stance.
static bool TryResolveAnyStance(AssetResolver resolver, string prefix, int[] stances, string suffix)
{
    foreach (var s in stances)
        if (resolver.TryLoadChoreAnim(prefix, s, suffix, out _)) return true;
    return false;
}

// End-to-end spawn harness: resolve everything, build real Actors, tick the shared
// SkritRuntime for N logic frames, and print summary state. Proves the full pipeline
// (template → mesh + clip + skrit → per-actor VM driving a blender) runs headless
// before the viewer wires into it in Phase 10e.
static int CmdRegionSpawn(string[] a)
{
    if (a.Length < 4)
    {
        Console.Error.WriteLine("usage: siegefx region spawn <map-tank> <logic-tank> <objects-tank> <region-path> [--ticks=N] [--broadcast=NAME]");
        return 1;
    }
    int ticks = 40;
    string? broadcast = null;
    for (int i = 4; i < a.Length; i++)
    {
        const string ticksPrefix = "--ticks=";
        const string bcastPrefix = "--broadcast=";
        if (a[i].StartsWith(ticksPrefix) && int.TryParse(a[i][ticksPrefix.Length..], out var t)) ticks = t;
        else if (a[i].StartsWith(bcastPrefix)) broadcast = a[i][bcastPrefix.Length..];
        else { Console.Error.WriteLine($"unknown option: {a[i]}"); return 1; }
    }

    using var mapTank     = TankFile.Open(a[0]);
    using var logicTank   = TankFile.Open(a[1]);
    using var objectsTank = TankFile.Open(a[2]);
    var mapReader     = new TankReader(mapTank);
    var logicReader   = new TankReader(logicTank);
    var objectsReader = new TankReader(objectsTank);

    var (store, _) = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);
    var (instances, _) = SiegeFX.Core.Assets.RegionObjects.LoadActors(mapReader, a[3]);

    var resolver = new SiegeFX.Core.Assets.AssetResolver();
    resolver.Add(objectsReader, "Objects.dsres");
    resolver.Add(logicReader,   "Logic.dsres");

    var spawner = new SiegeFX.Core.Actors.ActorSpawner(store, resolver);
    var actors = spawner.Spawn(instances);

    Console.WriteLine($"templates : {store.Count}");
    Console.WriteLine($"instances : {instances.Count}");
    Console.WriteLine($"spawned   : {actors.Count}  (diagnostics {spawner.Diagnostics.Count})");
    foreach (var d in spawner.Diagnostics.Take(5)) Console.WriteLine($"  !! {d}");
    if (spawner.Diagnostics.Count > 5) Console.WriteLine($"  ... ({spawner.Diagnostics.Count - 5} more)");

    Console.WriteLine();
    Console.WriteLine($"ticking runtime for {ticks} frames ({ticks / 20.0:0.0}s logical)...");
    const double stepSec = 1.0 / SiegeFX.Core.Skrit.SkritInstance.FramesPerSecond;
    int delivered = 0;
    if (broadcast is not null)
    {
        // Post at t=0 so it drains on the very first deliver pass. Usage is diagnostic:
        // `--broadcast=WE_ENTERED_WORLD` exercises the fan-out path and counts how many
        // actors actually have a handler for it. Most shipped skrits don't, which is fine
        // — we're measuring the bus, not the handlers.
        spawner.MessageBus.Post(broadcast, fromScid: 0, toScid: 0, arg1: 0, arg2: 0);
        Console.WriteLine($"  (broadcasting {broadcast} to all {actors.Count} actors)");
    }
    for (int i = 0; i < ticks; i++)
    {
        spawner.Runtime.Tick(stepSec);
        delivered += spawner.MessageBus.Deliver();
    }

    int withAnim = 0, withState = 0;
    var clipPicks = new Dictionary<int, int>();
    foreach (var actor in actors)
    {
        if (actor.Host.CurrentAnimIndex >= 0) withAnim++;
        if (actor.Skrit.CurrentState is not null) withState++;
        clipPicks[actor.CurrentClipIndex] = clipPicks.GetValueOrDefault(actor.CurrentClipIndex) + 1;
    }

    Console.WriteLine();
    Console.WriteLine($"after tick:");
    Console.WriteLine($"  in a skrit state   : {withState}/{actors.Count}");
    Console.WriteLine($"  picked a clip      : {withAnim}/{actors.Count}");
    Console.WriteLine($"  clip-index tally   : {string.Join(", ", clipPicks.OrderBy(kv => kv.Key).Select(kv => $"#{kv.Key}×{kv.Value}"))}");
    Console.WriteLine($"  bus posted         : {spawner.MessageBus.PostedCount}");
    Console.WriteLine($"  bus delivered      : {delivered}   (undelivered: {spawner.MessageBus.UndeliveredCount})");

    Console.WriteLine();
    Console.WriteLine("sample actors (first 5):");
    foreach (var actor in actors.Take(5))
    {
        var t = actor.WorldTransform.Translation;
        Console.WriteLine($"  {actor.Template.Name,-22} scid=0x{actor.Instance.Scid:x8}  pos=({t.X,8:0.00},{t.Y,6:0.00},{t.Z,8:0.00})  state={actor.Skrit.CurrentState}  clip=#{actor.CurrentClipIndex}");
    }
    return 0;
}
