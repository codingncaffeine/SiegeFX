using System.Numerics;
using SiegeFX.Core.Actors;
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
        "formulas"  => DispatchFormulas(args[1..]),
        "spells"    => DispatchSpells(args[1..]),
        "balance"   => DispatchBalance(args[1..]),
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
    Console.WriteLine("  siegefx templates stats    <tank> <name | --prefix=P>");
    Console.WriteLine("  siegefx templates combat   <tank> <attacker> <target> [--duels=N] [--seed=K]");
    Console.WriteLine("  siegefx templates loot     <tank> <name> [--rolls=N] [--seed=K]");
    Console.WriteLine("  siegefx region actors      <map-tank> <region-path>");
    Console.WriteLine("  siegefx region spawn-probe <map-tank> <logic-tank> <objects-tank> <region-path>");
    Console.WriteLine("  siegefx region spawn       <map-tank> <logic-tank> <objects-tank> <region-path> [--ticks=N] [--broadcast=NAME]");
    Console.WriteLine("  siegefx region prop-textures <map-tank> <logic-tank> <objects-tank> <region-path|all> [--terrain=PATH] [--top=N] [--list-misses]");
    Console.WriteLine("  siegefx region nav         <map-tank> <terrain-tank> <region-path>");
    Console.WriteLine("  siegefx region nav-fuzz    <map-tank> <terrain-tank>");
    Console.WriteLine("  siegefx region path        <map-tank> <terrain-tank> <region-path> <x1,y1,z1> <x2,y2,z2>");
    Console.WriteLine("  siegefx region path-fuzz   <map-tank> <terrain-tank>");
    Console.WriteLine("  siegefx region follow      <map-tank> <terrain-tank> <region-path> <x1,y1,z1> <x2,y2,z2> [speed] [ticks]");
    Console.WriteLine("  siegefx formulas dump      <Logic.dsres>");
    Console.WriteLine("  siegefx spells dump        <Logic.dsres>");
    Console.WriteLine("  siegefx spells show        <Logic.dsres> <spell_name> [magic_level]");
    Console.WriteLine("  siegefx balance curve      <Logic.dsres> [--max-level=N] [--skill=melee|ranged|nature|combat|all] [--start=str,dex,int]");
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
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx region <info|fuzz|layout|layout-fuzz|load-fuzz|neighbors|actors|spawn|nav|path|follow> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info"        => CmdRegionInfo(a[1..]),
        "fuzz"        => CmdRegionFuzz(a[1..]),
        "layout"      => CmdRegionLayout(a[1..]),
        "layout-fuzz" => CmdRegionLayoutFuzz(a[1..]),
        "layout-diag" => CmdRegionLayoutDiag(a[1..]),
        "load-fuzz"   => CmdRegionLoadFuzz(a[1..]),
        "neighbors"   => CmdRegionNeighbors(a[1..]),
        "actors"      => CmdRegionActors(a[1..]),
        "spawn-probe" => CmdRegionSpawnProbe(a[1..]),
        "spawn"       => CmdRegionSpawn(a[1..]),
        "prop-textures" => CmdRegionPropTextures(a[1..]),
        "nav"         => CmdRegionNav(a[1..]),
        "nav-fuzz"    => CmdRegionNavFuzz(a[1..]),
        "path"        => CmdRegionPath(a[1..]),
        "path-fuzz"   => CmdRegionPathFuzz(a[1..]),
        "follow"      => CmdRegionFollow(a[1..]),
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

    var path = new List<int>();
    if (!NavPathfinder.TryFindPath(mesh, startTri, goalTri, path))
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

static int CmdRegionFollow(string[] a)
{
    if (a.Length < 5 || a.Length > 7)
    {
        Console.Error.WriteLine("usage: siegefx region follow <map-tank> <terrain-tank> <region-path> <x1,y1,z1> <x2,y2,z2> [speed] [ticks]");
        return 1;
    }
    if (!TryParseVec3(a[3], out var start)) { Console.Error.WriteLine($"bad start vector: '{a[3]}'"); return 1; }
    if (!TryParseVec3(a[4], out var goal))  { Console.Error.WriteLine($"bad goal vector: '{a[4]}'");  return 1; }
    float speed = 6f;
    if (a.Length >= 6 && !float.TryParse(a[5], System.Globalization.CultureInfo.InvariantCulture, out speed))
    { Console.Error.WriteLine($"bad speed: '{a[5]}'"); return 1; }
    int maxTicks = 400;
    if (a.Length >= 7 && !int.TryParse(a[6], out maxTicks))
    { Console.Error.WriteLine($"bad tick count: '{a[6]}'"); return 1; }

    var mesh = LoadRegionNavMesh(a[0], a[1], a[2]);
    Console.WriteLine($"NavMesh    : {mesh.TriangleCount:N0} tris, {mesh.Vertices.Length:N0} welded verts, {mesh.NonManifoldEdgeCount} non-manifold edge(s)");

    var follower = new SiegeFX.Core.Nav.NavFollower(mesh, start, speed);
    follower.SetTarget(goal);
    if (follower.PathBlocked)
    {
        Console.Error.WriteLine("path blocked — start or goal off-mesh, or endpoints in disconnected components");
        return 2;
    }
    Console.WriteLine($"Start      : {FormatVec(follower.Position)}  tri={follower.CurrentTriangle}");
    Console.WriteLine($"Goal       : {FormatVec(follower.Target)}   path-tris={follower.RemainingPath.Count}");

    const float tickDt = 1f / 20f; // Match the 20 Hz simulation tick used by actors.
    float totalDist = 0f;
    var prev = follower.Position;
    int lastLogTri = -999;
    int ticks = 0;
    for (; ticks < maxTicks; ticks++)
    {
        follower.Tick(tickDt);
        totalDist += Vector3.Distance(prev, follower.Position);
        prev = follower.Position;
        if (follower.CurrentTriangle != lastLogTri)
        {
            Console.WriteLine($"  t={ticks,3}  pos={FormatVec(follower.Position)}  tri={follower.CurrentTriangle}");
            lastLogTri = follower.CurrentTriangle;
        }
        if (follower.ReachedGoal) { ticks++; break; }
        if (follower.PathBlocked) break;
    }
    Console.WriteLine($"Ticks      : {ticks} ({(follower.ReachedGoal ? "reached" : follower.PathBlocked ? "blocked" : "timed out")})");
    Console.WriteLine($"Distance   : {totalDist:F2} units walked vs {Vector3.Distance(start, goal):F2} units straight-line");
    return follower.ReachedGoal ? 0 : 3;
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
            var ws = new NavPathfinder.Workspace();
            var pathBuf = new List<int>();
            for (int s = 0; s < SamplesPerRegion; s++)
            {
                probes++;
                int a0 = rng.Next(mesh.TriangleCount);
                int b0 = rng.Next(mesh.TriangleCount);
                if (NavPathfinder.TryFindPath(mesh, a0, b0, pathBuf, ws)) solved++;
                // Control probe: both endpoints forced into the biggest component.
                // Exercises the pathfinder on the mesh's "real" walkable surface rather
                // than measuring topology disconnectedness. Expect ~100%.
                int ia = bigComponent[rng.Next(bigComponent.Count)];
                int ib = bigComponent[rng.Next(bigComponent.Count)];
                intraBigProbes++;
                if (NavPathfinder.TryFindPath(mesh, ia, ib, pathBuf, ws)) intraBigSolved++;
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

// Phase 21a — full-asset region load fuzzer. Walks every region in the map
// tank and runs the same loader stack the play-region path uses (graph +
// layout + actor.gas + conversation pool), so an asset variant that only
// chokes mid-play surfaces here in CI instead. Aggregates per-region failure
// counts plus per-error-class counts so the worst offenders rise to the top
// of the report rather than getting buried in a wall of stack traces.
static int CmdRegionLoadFuzz(string[] a)
{
    if (a.Length != 2)
    {
        Console.Error.WriteLine("usage: siegefx region load-fuzz <map-tank> <terrain-tank>");
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

    int regionCount = 0, fullySucceeded = 0;
    int graphFails = 0, layoutFails = 0, actorFails = 0, convFails = 0;
    int totalActors = 0, totalConvs = 0, totalDiags = 0;
    var errorBuckets = new Dictionary<string, int>(StringComparer.Ordinal);

    void Bucket(string stage, Exception ex)
    {
        var key = $"{stage}: {ex.GetType().Name}: {Truncate(ex.Message, 80)}";
        errorBuckets[key] = errorBuckets.GetValueOrDefault(key) + 1;
    }

    foreach (var path in mapReader.ListFiles())
    {
        if (!path.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase)) continue;
        regionCount++;
        var regionPath = path[..^"/terrain_nodes/nodes.gas".Length];
        bool ok = true;

        SiegeFX.Core.Assets.RegionGraph? graph = null;
        try { graph = SiegeFX.Core.Assets.RegionGraph.Load(mapReader.ExtractToMemory(path)); }
        catch (Exception ex) { graphFails++; Bucket("graph", ex); ok = false; }

        if (graph is not null)
        {
            try { _ = SiegeFX.Core.Assets.RegionLayout.Build(graph, Resolve); }
            catch (Exception ex) { layoutFails++; Bucket("layout", ex); ok = false; }
        }

        try
        {
            var (actors, diags) = SiegeFX.Core.Assets.RegionObjects.LoadActors(mapReader, regionPath);
            totalActors += actors.Count;
            totalDiags  += diags.Count;
        }
        catch (Exception ex) { actorFails++; Bucket("actors", ex); ok = false; }

        try
        {
            var (convs, diags) = SiegeFX.Core.Assets.ConversationStore.Load(mapReader, regionPath);
            totalConvs += convs.Count;
            totalDiags += diags.Count;
        }
        catch (Exception ex) { convFails++; Bucket("conversations", ex); ok = false; }

        if (ok) fullySucceeded++;
    }

    Console.WriteLine($"load-fuzzed {regionCount} region(s): {fullySucceeded} clean, " +
                      $"{regionCount - fullySucceeded} with at least one stage failure");
    Console.WriteLine($"  totals: {totalActors:N0} actor instance(s), {totalConvs:N0} conversation tree(s), " +
                      $"{totalDiags:N0} per-region diagnostic(s)");
    Console.WriteLine($"  per-stage hard failures: graph={graphFails} layout={layoutFails} " +
                      $"actors={actorFails} conversations={convFails}");

    if (errorBuckets.Count > 0)
    {
        Console.WriteLine("  top error classes:");
        foreach (var kv in errorBuckets.OrderByDescending(kv => kv.Value).Take(8))
            Console.WriteLine($"    {kv.Value,4}x  {kv.Key}");
    }

    return (graphFails + layoutFails + actorFails + convFails) == 0 ? 0 : 4;

    static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";
}

// Phase 21a-1 — discovery + composition probe for neighbor preload. Mirrors
// RenderHost.LoadNeighborTerrain without touching GL: parses the player
// region's stitch helper, resolves each declared dest_region to its tank
// path, builds a partial WorldLayout rooted at the player, and prints
// per-neighbor placement + a per-region instance count. Lets the runtime
// path be self-verified in CI before the user opens a window.
static int CmdRegionNeighbors(string[] a)
{
    if (a.Length != 3)
    {
        Console.Error.WriteLine("usage: siegefx region neighbors <map-tank> <terrain-tank> <region-path>");
        return 1;
    }

    using var mapTank = TankFile.Open(a[0]);
    var mapReader = new TankReader(mapTank);
    using var terrainTank = TankFile.Open(a[1]);
    var terrainReader = new TankReader(terrainTank);

    var normalized = a[2].Replace('\\', '/');
    if (!normalized.StartsWith('/')) normalized = "/" + normalized;
    if (normalized.EndsWith('/')) normalized = normalized[..^1];

    var stitchPath = normalized + "/editor/stitch_helper.gas";
    if (!mapReader.TryGetFile(stitchPath, out _))
    {
        Console.WriteLine($"region '{normalized}': no stitch_helper.gas (standalone region)");
        return 0;
    }

    SiegeFX.Core.Assets.RegionStitchHelper stitches;
    try { stitches = SiegeFX.Core.Assets.RegionStitchHelper.Load(mapReader.ExtractToMemory(stitchPath)); }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"stitch parse failed: {ex.Message}");
        return 2;
    }

    var pathByLeaf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var p in mapReader.ListFiles())
    {
        if (!p.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase)) continue;
        var rp = p[..^"/terrain_nodes/nodes.gas".Length];
        var leaf = rp[(rp.LastIndexOf('/') + 1)..];
        pathByLeaf[leaf] = rp;
    }

    var declared = stitches.ByDestination.Keys.ToList();
    var resolvedPaths = new List<string>();
    var unresolved = new List<string>();
    foreach (var d in declared)
    {
        if (pathByLeaf.TryGetValue(d, out var np) && np != normalized) resolvedPaths.Add(np);
        else if (!pathByLeaf.ContainsKey(d)) unresolved.Add(d);
    }

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
    bool TryEntry(string rp, out WorldLayout.RegionEntry entry)
    {
        entry = default;
        try
        {
            var graph = SiegeFX.Core.Assets.RegionGraph.Load(mapReader.ExtractToMemory(rp + "/terrain_nodes/nodes.gas"));
            var layout = SiegeFX.Core.Assets.RegionLayout.Build(graph, Resolve);
            SiegeFX.Core.Assets.RegionStitchHelper? stitch = null;
            var sp = rp + "/editor/stitch_helper.gas";
            if (mapReader.TryGetFile(sp, out _))
            {
                try { stitch = SiegeFX.Core.Assets.RegionStitchHelper.Load(mapReader.ExtractToMemory(sp)); }
                catch { }
            }
            entry = new WorldLayout.RegionEntry(rp, graph, layout, stitch);
            return true;
        }
        catch { return false; }
    }

    if (!TryEntry(normalized, out var playerEntry))
    {
        Console.Error.WriteLine($"player region '{normalized}' failed to load — cannot probe neighbors");
        return 3;
    }
    entries.Add(playerEntry);
    foreach (var rp in resolvedPaths)
        if (TryEntry(rp, out var ne)) entries.Add(ne);

    var world = WorldLayout.Build(entries, Resolve, rootHint: normalized);

    int placed = 0;
    long totalInstances = 0;
    Console.WriteLine($"region '{normalized}': {declared.Count} declared, {resolvedPaths.Count} resolved");
    foreach (var entry in entries)
    {
        if (entry.Path == normalized) continue;
        bool ok = world.RegionOffsets.ContainsKey(entry.Path);
        int instCount = entry.Graph.Nodes.Count(n => world.Transforms.ContainsKey(n.Guid));
        if (ok) { placed++; totalInstances += instCount; }
        Console.WriteLine($"  {(ok ? "[ok]" : "[--]")}  {entry.Path}  ({instCount} instance(s))");
    }
    foreach (var u in unresolved)
        Console.WriteLine($"  [??]  {u}  (declared but no nodes.gas in tank)");

    Console.WriteLine($"summary: placed {placed}/{resolvedPaths.Count} neighbor(s), " +
                      $"{totalInstances:N0} instance(s); " +
                      $"unresolved={world.UnresolvedStitchCount} dangling={world.DanglingStitchCount}");
    return placed == resolvedPaths.Count && unresolved.Count == 0 ? 0 : 5;
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

    if (mesh.Positions.Length > 0)
    {
        var min = mesh.Positions[0]; var max = mesh.Positions[0];
        for (var i = 1; i < mesh.Positions.Length; i++)
        {
            min = Vector3.Min(min, mesh.Positions[i]);
            max = Vector3.Max(max, mesh.Positions[i]);
        }
        var size = max - min;
        Console.WriteLine($"Extents   : min=({min.X:F2},{min.Y:F2},{min.Z:F2}) max=({max.X:F2},{max.Y:F2},{max.Z:F2}) size=({size.X:F2},{size.Y:F2},{size.Z:F2})");
    }
    if (mesh.BindPose.Length > 0)
    {
        for (var i = 0; i < Math.Min(4, mesh.BindPose.Length); i++)
        {
            var bp = mesh.BindPose[i];
            Console.WriteLine($"BindPose[{i}] : rot=({bp.Rotation.X:F3},{bp.Rotation.Y:F3},{bp.Rotation.Z:F3},{bp.Rotation.W:F3}) tr=({bp.Translation.X:F2},{bp.Translation.Y:F2},{bp.Translation.Z:F2})");
        }
    }
    if (mesh.Corners.Length > 0)
    {
        var umin = mesh.Corners[0].Uv.X; var umax = umin;
        var vmin = mesh.Corners[0].Uv.Y; var vmax = vmin;
        for (var i = 1; i < mesh.Corners.Length; i++)
        {
            var uv = mesh.Corners[i].Uv;
            if (uv.X < umin) umin = uv.X; if (uv.X > umax) umax = uv.X;
            if (uv.Y < vmin) vmin = uv.Y; if (uv.Y > vmax) vmax = uv.Y;
        }
        Console.WriteLine($"UV extents: U=[{umin:F3},{umax:F3}] V=[{vmin:F3},{vmax:F3}]");
        if (Environment.GetEnvironmentVariable("SIEGEFX_DUMP_UVS") == "1")
        {
            for (var i = 0; i < mesh.Corners.Length; i++)
            {
                var c = mesh.Corners[i]; var p = mesh.Positions[c.VertexIndex];
                Console.WriteLine($"  c[{i,2}] v={c.VertexIndex,2}  uv=({c.Uv.X:F3},{c.Uv.Y:F3})  pos=({p.X:F2},{p.Y:F2},{p.Z:F2})");
            }
        }
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
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx templates <list|show|stats|combat|loot> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "list"   => CmdTemplatesList(a[1..]),
        "show"   => CmdTemplatesShow(a[1..]),
        "stats"  => CmdTemplatesStats(a[1..]),
        "combat" => CmdTemplatesCombat(a[1..]),
        "loot"   => CmdTemplatesLoot(a[1..]),
        _        => UnknownCommand("templates " + a[0]),
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

// Phase 12a: resolve combat stats from a template's specializes chain.
// Accepts either a single --prefix filter to dump a group, or a bare name to show
// one archetype. The chain-walk means stats defined on 3W_base_goblin bleed down
// into every descendant that doesn't override, which is how DS1 authors it.
static int CmdTemplatesStats(string[] a)
{
    string? prefix = null;
    var rest = new List<string>();
    foreach (var x in a)
    {
        if (x.StartsWith("--prefix=", StringComparison.Ordinal)) prefix = x["--prefix=".Length..];
        else rest.Add(x);
    }
    // Modes are exclusive: either one specific name or one prefix. The two bare
    // positionals in prefix mode would be ambiguous (the second could be a name
    // that shadows the prefix), so reject rather than silently ignoring one.
    bool wantsName   = prefix is null && rest.Count == 2;
    bool wantsPrefix = prefix is not null && rest.Count == 1;
    if (!wantsName && !wantsPrefix)
    {
        Console.Error.WriteLine("usage: siegefx templates stats <tank> <name>");
        Console.Error.WriteLine("   or: siegefx templates stats <tank> --prefix=P");
        if (prefix is not null && rest.Count == 2)
            Console.Error.WriteLine("(cannot combine a bare <name> with --prefix=)");
        return 1;
    }

    using var tank = TankFile.Open(rest[0]);
    var reader = new TankReader(tank);
    var (store, _) = TemplateStore.LoadFromTank(reader);

    if (prefix is not null)
    {
        int combatants = 0, inert = 0, shown = 0;
        Console.WriteLine($"{"template",-32}  {"life",8} {"dmg",-12} {"def",6} {"rng",5} {"spd",5} {"xp",8}");
        Console.WriteLine(new string('-', 80));
        foreach (var t in store.All.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var s = ActorStats.FromTemplate(store, t);
            if (s.IsCombatant) combatants++; else inert++;
            if (shown++ < 40)
                Console.WriteLine(
                    $"{t.Name,-32}  {s.MaxLife,8:F1} {s.DamageMin,5:F0}-{s.DamageMax,-5:F0} {s.Defense,6:F1} {s.AttackRange,5:F1} {s.WalkSpeed,5:F2} {s.ExperienceValue,8}");
        }
        if (shown > 40) Console.WriteLine($"... (showed 40 of {shown} matching; prefix='{prefix}')");
        Console.WriteLine($"summary: {combatants} combatant(s), {inert} inert (max_life<=0 or damage_max<=0)");
        return 0;
    }

    if (!store.TryGet(rest[1], out var tpl))
    {
        Console.Error.WriteLine($"template '{rest[1]}' not found (store has {store.Count})");
        return 1;
    }
    var stats = ActorStats.FromTemplate(store, tpl);
    Console.WriteLine($"name              : {tpl.Name}");
    Console.Write    ("chain             :");
    for (var cur = tpl; cur is not null; cur = cur.Specializes) Console.Write($" {cur.Name}");
    Console.WriteLine();
    Console.WriteLine($"max life          : {stats.MaxLife:F2}");
    Console.WriteLine($"max mana          : {stats.MaxMana:F2}");
    Console.WriteLine($"damage            : {stats.DamageMin:F1} – {stats.DamageMax:F1}");
    Console.WriteLine($"defense           : {stats.Defense:F2}");
    Console.WriteLine($"attack range      : {stats.AttackRange:F2}");
    Console.WriteLine($"walk speed        : {stats.WalkSpeed:F2} u/s");
    Console.WriteLine($"experience value  : {stats.ExperienceValue:N0}");
    Console.WriteLine($"combatant         : {(stats.IsCombatant ? "yes" : "no (non-combat archetype)")}");
    return 0;
}

// Phase 12b: simulate a melee fight between two named templates. Sanity check that
// the stats + resolver combine into DS1-feeling combat: not a one-shot, not 100+
// rolls, consistent across RNG runs. Rolls until one side dies; reports hits,
// mean damage dealt per hit, and remaining life on the winner.
static int CmdTemplatesCombat(string[] a)
{
    int duels = 1;
    int? rngSeed = null;
    var rest = new List<string>();
    foreach (var x in a)
    {
        if (x.StartsWith("--duels=", StringComparison.Ordinal)) int.TryParse(x["--duels=".Length..], out duels);
        else if (x.StartsWith("--seed=", StringComparison.Ordinal)) { if (int.TryParse(x["--seed=".Length..], out var s)) rngSeed = s; }
        else rest.Add(x);
    }
    if (rest.Count != 3)
    {
        Console.Error.WriteLine("usage: siegefx templates combat <tank> <attacker> <target> [--duels=N] [--seed=K]");
        return 1;
    }

    using var tank = TankFile.Open(rest[0]);
    var reader = new TankReader(tank);
    var (store, _) = TemplateStore.LoadFromTank(reader);
    if (!store.TryGet(rest[1], out var attackerT)) { Console.Error.WriteLine($"template '{rest[1]}' not found"); return 1; }
    if (!store.TryGet(rest[2], out var targetT))   { Console.Error.WriteLine($"template '{rest[2]}' not found"); return 1; }

    var atkStats = ActorStats.FromTemplate(store, attackerT);
    var tgtStats = ActorStats.FromTemplate(store, targetT);
    if (!atkStats.IsCombatant) { Console.Error.WriteLine($"{attackerT.Name}: not a combatant (no life/damage)"); return 1; }

    Console.WriteLine($"attacker  : {attackerT.Name}  life={atkStats.MaxLife:F0} dmg={atkStats.DamageMin:F0}-{atkStats.DamageMax:F0} def={atkStats.Defense:F0}");
    Console.WriteLine($"target    : {targetT.Name}  life={tgtStats.MaxLife:F0} def={tgtStats.Defense:F0}");
    Console.WriteLine($"duels     : {duels}{(rngSeed is null ? "" : $"  seed={rngSeed}")}");
    Console.WriteLine();

    var rng = new Random(rngSeed ?? Environment.TickCount);
    int totalHits = 0;
    double totalDmg = 0;
    int kills = 0;
    int capHits = 200; // safety cap — a zero-damage resolver would otherwise loop forever
    for (int d = 0; d < duels; d++)
    {
        var target = new ActorCombatState(tgtStats);
        int hits = 0;
        while (!target.IsDead && hits < capHits)
        {
            float dmg = CombatResolver.RollMeleeDamage(atkStats, tgtStats, rng);
            float actual = target.ApplyDamage(dmg);
            totalDmg += actual;
            hits++;
        }
        totalHits += hits;
        if (target.IsDead) kills++;
    }

    double meanHits = totalHits / (double)duels;
    double meanDmg  = totalDmg / totalHits;
    Console.WriteLine($"result    : {kills}/{duels} duel(s) reached a kill");
    Console.WriteLine($"  mean hits to kill : {meanHits:F1}");
    Console.WriteLine($"  mean damage / hit : {meanDmg:F1}");
    return 0;
}

static int CmdTemplatesLoot(string[] a)
{
    int rolls = 0;
    int? rngSeed = null;
    var rest = new List<string>();
    foreach (var x in a)
    {
        if (x.StartsWith("--rolls=", StringComparison.Ordinal)) int.TryParse(x["--rolls=".Length..], out rolls);
        else if (x.StartsWith("--seed=", StringComparison.Ordinal)) { if (int.TryParse(x["--seed=".Length..], out var s)) rngSeed = s; }
        else rest.Add(x);
    }
    if (rest.Count != 2)
    {
        Console.Error.WriteLine("usage: siegefx templates loot <tank> <name> [--rolls=N] [--seed=K]");
        return 1;
    }

    using var tank = TankFile.Open(rest[0]);
    var reader = new TankReader(tank);
    var (store, _) = TemplateStore.LoadFromTank(reader);
    if (!store.TryGet(rest[1], out var template)) { Console.Error.WriteLine($"template '{rest[1]}' not found"); return 1; }

    var table = LootTable.FromTemplate(store, template);
    Console.WriteLine($"template  : {template.Name}");
    if (table.IsEmpty) { Console.WriteLine("(no inventory.pcontent in chain)"); return 0; }

    Console.WriteLine($"equipped  : {table.Equipped.Count} bucket(s)");
    for (int i = 0; i < table.Equipped.Count; i++) PrintBucket(table.Equipped[i], "  ");
    Console.WriteLine($"drops     : {table.Drops.Count} bucket(s)");
    for (int i = 0; i < table.Drops.Count; i++) PrintBucket(table.Drops[i], "  ");

    if (rolls <= 0) return 0;

    Console.WriteLine();
    Console.WriteLine($"rolling {rolls} kill(s){(rngSeed is null ? "" : $", seed={rngSeed}")}:");
    var rng = new Random(rngSeed ?? Environment.TickCount);
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    int totalDrops = 0;
    int emptyRolls = 0;
    for (int i = 0; i < rolls; i++)
    {
        var drops = LootRoller.Roll(table, rng);
        if (drops.Count == 0) emptyRolls++;
        totalDrops += drops.Count;
        foreach (var d in drops)
        {
            var key = d.IsEquipped ? $"[{d.Slot}] {d.Reference}" : d.Reference;
            counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
        }
    }
    Console.WriteLine($"  total drops        : {totalDrops}");
    Console.WriteLine($"  empty-handed rolls : {emptyRolls}/{rolls}");
    Console.WriteLine($"  distinct outputs   : {counts.Count}");
    foreach (var kv in counts.OrderByDescending(k => k.Value).Take(20))
        Console.WriteLine($"    {kv.Value,5}  {kv.Key}");
    if (counts.Count > 20) Console.WriteLine($"    ... +{counts.Count - 20} more");
    return 0;
}

static void PrintBucket(LootBucket bucket, string indent)
{
    var chanceStr = bucket.Chance < 1f ? $" chance={bucket.Chance:P1}" : "";
    Console.WriteLine($"{indent}[oneof*]{chanceStr}");
    foreach (var e in bucket.Entries)
    {
        var tag = e.IsEquipped ? $"es_{e.Slot}" : "il_main";
        Console.WriteLine($"{indent}  {tag} = {e.Reference}");
    }
    foreach (var c in bucket.Children) PrintBucket(c, indent + "  ");
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

// Phase 21c-5 — headless static-prop texture audit. Walks every static-prop
// placement in <region-path> and counts how many have a resolvable .raw via
// the same texset rules the runtime uses (template [aspect][textures]{0=...}
// override → BMSH default → -01..-08 variant fallback). Prints an unresolved
// table sorted by miss count so we can see which templates' textures we
// can't find — same logic that lights up the in-game --diag summary, but
// runnable in CI / SSH without bringing up a window.
static int CmdRegionPropTextures(string[] a)
{
    if (a.Length < 4)
    {
        Console.Error.WriteLine("usage: siegefx region prop-textures <map-tank> <logic-tank> <objects-tank> <region-path> [--terrain=PATH] [--top=N] [--list-misses]");
        return 1;
    }
    int top = 25;
    bool listMisses = false;
    string? terrainPath = null;
    for (int i = 4; i < a.Length; i++)
    {
        const string topPrefix = "--top=";
        const string terrPrefix = "--terrain=";
        if (a[i].StartsWith(topPrefix) && int.TryParse(a[i][topPrefix.Length..], out var n)) top = n;
        else if (a[i].StartsWith(terrPrefix)) terrainPath = a[i][terrPrefix.Length..];
        else if (a[i] == "--list-misses") listMisses = true;
        else { Console.Error.WriteLine($"unknown option: {a[i]}"); return 1; }
    }

    using var mapTank     = TankFile.Open(a[0]);
    using var logicTank   = TankFile.Open(a[1]);
    using var objectsTank = TankFile.Open(a[2]);
    SiegeFX.Core.Tank.TankFile? terrainTank = terrainPath is not null ? TankFile.Open(terrainPath) : null;
    var mapReader     = new TankReader(mapTank);
    var logicReader   = new TankReader(logicTank);
    var objectsReader = new TankReader(objectsTank);

    var (store, _) = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);
    var resolver = new SiegeFX.Core.Assets.AssetResolver();
    if (terrainTank is not null) resolver.Add(new TankReader(terrainTank), "Terrain.dsres");
    resolver.Add(objectsReader, "Objects.dsres");
    resolver.Add(logicReader,   "Logic.dsres");

    // "all" as the region arg auto-discovers every /world/maps/.../regions/<name>
    // path in the map tank and audits each in turn, then prints one summary.
    // Useful for CI / regression checks: 0 unresolved across all shipped regions.
    var regionPaths = new List<string>();
    if (a[3].Equals("all", StringComparison.OrdinalIgnoreCase))
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in mapReader.ListFiles())
        {
            var idx = path.IndexOf("/regions/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var rest = path[(idx + "/regions/".Length)..];
            var slash = rest.IndexOf('/');
            if (slash < 0) continue;
            var regionPath = path[..(idx + "/regions/".Length + slash)];
            if (seen.Add(regionPath)) regionPaths.Add(regionPath);
        }
        regionPaths.Sort(StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"auditing {regionPaths.Count} regions...");
    }
    else
    {
        regionPaths.Add(a[3]);
    }

    int considered = 0, withMesh = 0, resolved = 0, unresolved = 0;
    int noTemplate = 0, noModel = 0, noMesh = 0;
    var perTpl = new SortedDictionary<string, (int textured, int untextured, string? expected, string? mesh)>(StringComparer.OrdinalIgnoreCase);
    var unresolvedNames = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var perRegion = new List<(string path, int placements, int untextured)>();

    foreach (var regionPath in regionPaths)
    {
        int regPlacements = 0, regUntex = 0;
        foreach (var fileName in SiegeFX.Core.Assets.RegionObjects.StaticPropFiles)
        {
            var (placements, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, regionPath, fileName);
            foreach (var p in placements)
            {
                considered++;
                regPlacements++;
                if (!store.TryGet(p.TemplateName, out var template)) { noTemplate++; continue; }
                var modelName = store.GetAttribute(template, "aspect", "model");
                if (string.IsNullOrEmpty(modelName)) { noModel++; continue; }
                if (!resolver.TryLoadModel(modelName, out var aspBytes)) { noMesh++; continue; }
                SiegeFX.Core.Assets.AspMesh asp;
                try { asp = SiegeFX.Core.Assets.AspMesh.Load(aspBytes); }
                catch { noMesh++; continue; }
                withMesh++;

                // Same precedence as ResolveActorTexture: template override slot 0 wins,
                // then BMSH-stored texture name. Static-prop templates rarely override but
                // some interactives (chests, doors with state variants) do.
                var baseName = store.GetAttribute(template, "aspect", "textures", "0");
                if (string.IsNullOrEmpty(baseName) && asp.TextureNames.Count > 0)
                    baseName = asp.TextureNames[0];

                bool hit = false;
                if (!string.IsNullOrEmpty(baseName))
                {
                    if (resolver.TryLoadByBasename(baseName + ".raw", out _)) hit = true;
                    else
                    {
                        for (int i = 1; i <= 8 && !hit; i++)
                            if (resolver.TryLoadByBasename($"{baseName}-{i:D2}.raw", out _)) hit = true;
                    }
                }

                perTpl.TryGetValue(p.TemplateName, out var entry);
                perTpl[p.TemplateName] = (
                    entry.textured + (hit ? 1 : 0),
                    entry.untextured + (hit ? 0 : 1),
                    baseName ?? entry.expected,
                    modelName);
                if (hit) resolved++;
                else
                {
                    unresolved++;
                    regUntex++;
                    if (!string.IsNullOrEmpty(baseName))
                    {
                        unresolvedNames.TryGetValue(baseName, out var nn);
                        unresolvedNames[baseName] = nn + 1;
                    }
                }
            }
        }
        perRegion.Add((regionPath, regPlacements, regUntex));
    }

    bool batch = regionPaths.Count > 1;
    Console.WriteLine();
    if (batch)
    {
        Console.WriteLine($"regions       : {regionPaths.Count}");
    }
    else
    {
        Console.WriteLine($"region        : {a[3]}");
    }
    Console.WriteLine($"placements    : {considered}");
    Console.WriteLine($"  no template : {noTemplate}");
    Console.WriteLine($"  no aspect.model: {noModel}");
    Console.WriteLine($"  no .asp / parse fail: {noMesh}");
    Console.WriteLine($"  with mesh   : {withMesh}");
    Console.WriteLine($"    textured  : {resolved}");
    Console.WriteLine($"    untextured: {unresolved}");
    Console.WriteLine();
    Console.WriteLine($"templates with un-resolved .raw (top {top} by miss count):");
    foreach (var kv in perTpl.Where(x => x.Value.untextured > 0)
                              .OrderByDescending(x => x.Value.untextured)
                              .Take(top))
    {
        var v = kv.Value;
        Console.WriteLine($"  {v.untextured,4}x untextured ({v.textured} textured)  template={kv.Key}  mesh={v.mesh}  tex={v.expected ?? "(none)"}");
    }
    if (listMisses && unresolvedNames.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("distinct unresolved texset basenames (count):");
        foreach (var kv in unresolvedNames.OrderByDescending(x => x.Value))
            Console.WriteLine($"  {kv.Value,4}x  {kv.Key}");
    }
    if (batch)
    {
        var dirty = perRegion.Where(r => r.untextured > 0).ToList();
        Console.WriteLine();
        Console.WriteLine($"per-region misses ({dirty.Count} dirty / {perRegion.Count} total):");
        if (dirty.Count == 0) Console.WriteLine("  (all regions clean)");
        foreach (var r in dirty.OrderByDescending(r => r.untextured))
            Console.WriteLine($"  {r.untextured,4}x untextured / {r.placements,5} placements  {r.path}");
    }
    terrainTank?.Dispose();
    return unresolved == 0 ? 0 : 4;
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

static int DispatchFormulas(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx formulas <dump> <Logic.dsres>"); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "dump" => CmdFormulasDump(a[1..]),
        _      => UnknownCommand("formulas " + a[0]),
    };
}

static int CmdFormulasDump(string[] a)
{
    if (a.Length < 1) { Console.Error.WriteLine("usage: siegefx formulas dump <Logic.dsres>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var f = FormulasStore.LoadFromTank(reader);

    Console.WriteLine("recalculation_constants:");
    Console.WriteLine($"  max_life   = base {f.MaxLifeBase}, const {f.MaxLifeConstant}, str% {f.MaxLifeStrPct}, dex% {f.MaxLifeDexPct}, int% {f.MaxLifeIntPct}");
    Console.WriteLine($"  max_mana   = base {f.MaxManaBase}, const {f.MaxManaConstant}, str% {f.MaxManaStrPct}, dex% {f.MaxManaDexPct}, int% {f.MaxManaIntPct}");
    Console.WriteLine($"  10/10/10   -> MaxLife={f.MaxLife(10,10,10),5:0.0}  MaxMana={f.MaxMana(10,10,10),5:0.0}");
    Console.WriteLine($"  death_threshold = {f.DeathThreshold}");
    Console.WriteLine();

    Console.WriteLine("recovery rates (HP/sec, MP/sec):");
    Console.WriteLine($"  lr unit/period = {f.LifeRecoveryUnit}/{f.LifeRecoveryPeriod}  ->  rate@str=10 = {f.LifeRecoveryRate(10),5:0.000}, str=20 = {f.LifeRecoveryRate(20),5:0.000}");
    Console.WriteLine($"  mr unit/period = {f.ManaRecoveryUnit}/{f.ManaRecoveryPeriod}  ->  rate@int=10 = {f.ManaRecoveryRate(10),5:0.000}, int=20 = {f.ManaRecoveryRate(20),5:0.000}");
    Console.WriteLine();

    Console.WriteLine("proportional gains (str / dex / int):");
    foreach (SkillKind k in Enum.GetValues<SkillKind>())
    {
        var g = f.ProportionalGains(k);
        Console.WriteLine($"  {k,-12} {g.Str,5:0.00} / {g.Dex,5:0.00} / {g.Int,5:0.00}  (sum {g.Str + g.Dex + g.Int:0.00})");
    }
    Console.WriteLine();

    Console.WriteLine($"experience_table: {f.XpTable.Count} entries  (lvl 1: {f.XpForLevel(1):N0}, lvl 10: {f.XpForLevel(10):N0}, lvl 50: {f.XpForLevel(50):N0}, lvl 100: {f.XpForLevel(100):N0}, lvl 160: {f.XpForLevel(160):N0})");
    Console.WriteLine($"  reverse: 1000 xp -> level {f.LevelForXp(1000)}, 50000 -> {f.LevelForXp(50000)}, 1000000 -> {f.LevelForXp(1000000)}");
    return 0;
}

static int DispatchBalance(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx balance <curve> <Logic.dsres> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "curve" => CmdBalanceCurve(a[1..]),
        _       => UnknownCommand("balance " + a[0]),
    };
}

static int CmdBalanceCurve(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("usage: siegefx balance curve <Logic.dsres> [--max-level=N] [--skill=melee|ranged|nature|combat|all] [--start=str,dex,int]");
        return 1;
    }

    int maxLevel = 50;
    string skillSel = "all";
    float startStr = 10f, startDex = 10f, startInt = 10f;
    string? logicPath = null;

    foreach (var arg in a)
    {
        if (arg.StartsWith("--max-level=", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(arg.AsSpan("--max-level=".Length), out maxLevel) || maxLevel < 1)
                throw new FormatException("--max-level must be a positive integer");
        }
        else if (arg.StartsWith("--skill=", StringComparison.OrdinalIgnoreCase))
        {
            skillSel = arg["--skill=".Length..].Trim().ToLowerInvariant();
        }
        else if (arg.StartsWith("--start=", StringComparison.OrdinalIgnoreCase))
        {
            var parts = arg["--start=".Length..].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3
                || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out startStr)
                || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out startDex)
                || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out startInt))
                throw new FormatException("--start must be 'str,dex,int' (3 floats)");
        }
        else if (arg.StartsWith("--", StringComparison.Ordinal))
        {
            throw new FormatException($"unknown flag '{arg}'");
        }
        else
        {
            if (logicPath is not null) throw new FormatException("only one Logic.dsres path expected");
            logicPath = arg;
        }
    }

    if (logicPath is null) { Console.Error.WriteLine("missing <Logic.dsres>"); return 1; }

    using var tank = TankFile.Open(logicPath);
    var reader = new TankReader(tank);
    var f = FormulasStore.LoadFromTank(reader);

    SkillKind[] skills;
    switch (skillSel)
    {
        case "all":     skills = Enum.GetValues<SkillKind>(); break;
        case "melee":   skills = new[] { SkillKind.Melee }; break;
        case "ranged":  skills = new[] { SkillKind.Ranged }; break;
        case "nature":  skills = new[] { SkillKind.NatureMagic }; break;
        case "combat":  skills = new[] { SkillKind.CombatMagic }; break;
        default: throw new FormatException($"--skill must be melee|ranged|nature|combat|all (got '{skillSel}')");
    }

    int xpCap = f.XpTable.Count;
    if (maxLevel > xpCap)
    {
        Console.WriteLine($"note: --max-level={maxLevel} exceeds xp table cap ({xpCap}); clamping.");
        maxLevel = xpCap;
    }

    int totalWarnings = 0;
    foreach (var skill in skills)
    {
        var gains = f.ProportionalGains(skill);
        Console.WriteLine();
        Console.WriteLine($"=== skill: {skill} ===  proportional gains  str {gains.Str:0.00}  dex {gains.Dex:0.00}  int {gains.Int:0.00}  (sum {gains.Str + gains.Dex + gains.Int:0.00})");
        Console.WriteLine($"starting attrs   str {startStr:0.00}   dex {startDex:0.00}   int {startInt:0.00}");
        Console.WriteLine();
        Console.WriteLine($"  Lvl  CumXP            STR     DEX     INT     MaxHP    MaxMP    HP/s    MP/s   T->FullHP T->FullMP");
        Console.WriteLine($"  ---  ---------------- ------  ------  ------  -------  -------  ------  ------ --------- ---------");

        int warnings = 0;
        float prevHp = -1f, prevMp = -1f;
        long prevXp = -1;

        for (int lvl = 1; lvl <= maxLevel; lvl++)
        {
            int dlvl = lvl - 1;
            float str = startStr + gains.Str * dlvl;
            float dex = startDex + gains.Dex * dlvl;
            float intl = startInt + gains.Int * dlvl;
            float hp = f.MaxLife(str, dex, intl);
            float mp = f.MaxMana(str, dex, intl);
            float hpRate = f.LifeRecoveryRate(str);
            float mpRate = f.ManaRecoveryRate(intl);
            float tFullHp = hpRate > 0 ? hp / hpRate : float.PositiveInfinity;
            float tFullMp = mp > 0 && mpRate > 0 ? mp / mpRate : (mp <= 0 ? 0f : float.PositiveInfinity);
            long cumXp = f.XpForLevel(lvl);

            string warn = "";
            if (prevHp >= 0 && hp < prevHp) { warn += " HP-DROP"; warnings++; }
            if (prevMp >= 0 && mp < prevMp) { warn += " MP-DROP"; warnings++; }
            if (prevXp >= 0 && cumXp < prevXp) { warn += " XP-DROP"; warnings++; }

            string mpStr = mp > 0 ? mp.ToString("0.0").PadLeft(7) : "    -- ";
            string mpRateStr = mp > 0 ? mpRate.ToString("0.000").PadLeft(6) : "   -- ";
            string tMpStr;
            if (mp <= 0) tMpStr = "      --";
            else if (float.IsPositiveInfinity(tFullMp)) tMpStr = "     inf";
            else tMpStr = tFullMp.ToString("0.0").PadLeft(8);

            string tHpStr = float.IsPositiveInfinity(tFullHp) ? "     inf" : tFullHp.ToString("0.0").PadLeft(8);

            Console.WriteLine(
                $"  {lvl,3}  {cumXp,16:N0} {str,6:0.00}  {dex,6:0.00}  {intl,6:0.00}  {hp,7:0.0}  {mpStr}  {hpRate,6:0.000}  {mpRateStr} {tHpStr} {tMpStr}{warn}");

            prevHp = hp; prevMp = mp; prevXp = cumXp;
        }

        Console.WriteLine();
        Console.WriteLine(warnings == 0
            ? $"  warnings: none ({skill} curve is monotonic L1..L{maxLevel})"
            : $"  warnings: {warnings} monotonicity violation(s) on {skill} — see HP-DROP / MP-DROP / XP-DROP rows above");
        totalWarnings += warnings;
    }

    Console.WriteLine();
    Console.WriteLine(totalWarnings == 0
        ? $"OK — all {skills.Length} skill(s) walk L1..L{maxLevel} with monotonic HP/MP/XP."
        : $"FAIL — {totalWarnings} monotonicity violation(s) across {skills.Length} skill(s).");
    return totalWarnings == 0 ? 0 : 4;
}

static int DispatchSpells(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx spells <dump|show> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "dump" => CmdSpellsDump(a[1..]),
        "show" => CmdSpellsShow(a[1..]),
        _      => UnknownCommand("spells " + a[0]),
    };
}

static int CmdSpellsDump(string[] a)
{
    if (a.Length < 1) { Console.Error.WriteLine("usage: siegefx spells dump <Logic.dsres>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var (store, diags) = TemplateStore.LoadFromTank(reader);
    var cat = SpellCatalog.Build(store);
    Console.WriteLine($"templates: {store.Count} loaded ({diags.Count} diagnostics)");
    Console.WriteLine($"spells (instant-hit, parsed [magic] block): {cat.Count}");
    foreach (var s in cat.All.OrderBy(s => s.Name).Take(40))
    {
        Console.WriteLine($"  {s.Name,-32} \"{s.ScreenName,-22}\"  range={s.CastRange,5:0.0}  cd={s.CastReloadDelay,4:0.00}  cost={s.BaseManaCost,4:0.0}");
    }
    if (cat.Count > 40) Console.WriteLine($"  ... ({cat.Count - 40} more)");
    return 0;
}

static int CmdSpellsShow(string[] a)
{
    if (a.Length < 2) { Console.Error.WriteLine("usage: siegefx spells show <Logic.dsres> <spell_name> [magic_level]"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var (store, _) = TemplateStore.LoadFromTank(reader);
    var cat = SpellCatalog.Build(store);
    if (!cat.TryGet(a[1], out var s)) { Console.Error.WriteLine($"no spell named '{a[1]}' in catalog"); return 4; }
    Console.WriteLine($"{s.Name}  \"{s.ScreenName}\"  (kind={s.Kind})");
    Console.WriteLine($"  cast_range          = {s.CastRange}");
    Console.WriteLine($"  cast_reload_delay   = {s.CastReloadDelay}");
    Console.WriteLine($"  base mana_cost      = {s.BaseManaCost}");
    Console.WriteLine($"  mana_cost_modifier  = {s.ManaCostModifierExpr}");
    if (s.Kind == SpellKind.SelfHeal)
        Console.WriteLine($"  heal_amount expr    = {s.HealAmountExpr}");
    else
    {
        Console.WriteLine($"  damage_min expr     = {s.AttackDamageMinExpr}");
        Console.WriteLine($"  damage_max expr     = {s.AttackDamageMaxExpr}");
    }
    Console.WriteLine();
    int[] levels = a.Length >= 3 && int.TryParse(a[2], out var only) ? new[] { only } : new[] { 1, 5, 10, 25, 50, 100 };
    var rng = new Random(1);
    if (s.Kind == SpellKind.SelfHeal)
    {
        Console.WriteLine("evaluated by magic level (heal amount, mana cost):");
        foreach (var lv in levels)
        {
            float heal = s.HealAmount(lv);
            float cost = s.ManaCost(lv);
            Console.WriteLine($"  L{lv,-3}  heal={heal,6:0.00}  mana={cost,5:0.0}");
        }
    }
    else
    {
        Console.WriteLine("evaluated by magic level (lo / hi damage, mana cost):");
        foreach (var lv in levels)
        {
            float lo = SpellExpr.Eval(s.AttackDamageMinExpr, lv);
            float hi = SpellExpr.Eval(s.AttackDamageMaxExpr, lv);
            float cost = s.ManaCost(lv);
            float sample = s.RollDamage(lv, rng);
            Console.WriteLine($"  L{lv,-3}  dmg [{lo,7:0.00} .. {hi,7:0.00}]  sample={sample,6:0.00}  mana={cost,5:0.0}");
        }
    }
    return 0;
}
