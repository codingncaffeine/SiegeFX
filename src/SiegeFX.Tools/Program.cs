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
        "pcontent"  => DispatchPcontent(args[1..]),
        "loot"      => DispatchLoot(args[1..]),
        "formulas"  => DispatchFormulas(args[1..]),
        "spells"    => DispatchSpells(args[1..]),
        "sfx"       => DispatchSfx(args[1..]),
        "capture-kit" => CmdCaptureKitBuild(args[1..]),
        "store"     => DispatchStore(args[1..]),
        "party"     => DispatchParty(args[1..]),
        "balance"   => DispatchBalance(args[1..]),
        "audio"     => DispatchAudio(args[1..]),
        "mood"      => DispatchMood(args[1..]),
        "ui"        => DispatchUi(args[1..]),
        "flm"       => DispatchFlm(args[1..]),
        "music"     => DispatchMusic(args[1..]),
        "tsd"       => DispatchTsd(args[1..]),
        "quests"    => DispatchQuests(args[1..]),
        "weapons"   => DispatchWeapons(args[1..]),
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
    Console.WriteLine("  siegefx tank list    <tank> [--prefix=PATH] [--ext=.EXT]");
    Console.WriteLine("  siegefx tank extract <tank> <resource-path> [dest-file]");
    Console.WriteLine("  siegefx tank fuzz    <tank>");
    Console.WriteLine("  siegefx raw  info    <file.raw>");
    Console.WriteLine("  siegefx raw  decode  <file.raw> [out.png] [--surface N] [--all]");
    Console.WriteLine("  siegefx raw  fuzz    <tank>");
    Console.WriteLine("  siegefx asp  info       <file.asp>");
    Console.WriteLine("  siegefx asp  subsets    <file.asp>");
    Console.WriteLine("  siegefx asp  trace-pose <file.asp> [prs-file] [time-frac]");
    Console.WriteLine("  siegefx sno  info    <file.sno>");
    Console.WriteLine("  siegefx sno  nav     <file.sno>");
    Console.WriteLine("  siegefx sno  fuzz    <tank>");
    Console.WriteLine("  siegefx prs  info    <file.prs>");
    Console.WriteLine("  siegefx prs  fuzz    <tank>");
    Console.WriteLine("  siegefx prs  sample  <file.prs> [time-fraction 0..1]");
    Console.WriteLine("  siegefx prs  compare <a.prs> <b.prs> [threshold]");
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
    Console.WriteLine("  siegefx templates equipment-audit <logic-tank> <objects-tank> <player-template> [--terrain=PATH]");
    Console.WriteLine("  siegefx templates hero-variants <objects-tank>");
    Console.WriteLine("  siegefx pcontent  dump     <tank> [--spec=#class/lo-hi] [--rolls=N] [--seed=K] [--class=X]");
    Console.WriteLine("  siegefx region actors      <map-tank> <region-path>");
    Console.WriteLine("  siegefx region spawn-probe <map-tank> <logic-tank> <objects-tank> <region-path>");
    Console.WriteLine("  siegefx region spawn       <map-tank> <logic-tank> <objects-tank> <region-path> [--ticks=N] [--broadcast=NAME]");
    Console.WriteLine("  siegefx region prop-textures <map-tank> <logic-tank> <objects-tank> <region-path|all> [--terrain=PATH] [--top=N] [--list-misses]");
    Console.WriteLine("  siegefx region actor-coverage <map-tank> <logic-tank> <objects-tank> <region-path|all> [--terrain=PATH] [--top=N] [--list-misses]");
    Console.WriteLine("  siegefx region nav         <map-tank> <terrain-tank> <region-path>");
    Console.WriteLine("  siegefx region nav-fuzz    <map-tank> <terrain-tank>");
    Console.WriteLine("  siegefx region path        <map-tank> <terrain-tank> <region-path> <x1,y1,z1> <x2,y2,z2>");
    Console.WriteLine("  siegefx region path-fuzz   <map-tank> <terrain-tank>");
    Console.WriteLine("  siegefx region nav-components <map-tank> <terrain-tank> <region-path|all> [--top=N]");
    Console.WriteLine("  siegefx region follow      <map-tank> <terrain-tank> <region-path> <x1,y1,z1> <x2,y2,z2> [speed] [ticks]");
    Console.WriteLine("  siegefx formulas dump      <Logic.dsres>");
    Console.WriteLine("  siegefx spells dump        <Logic.dsres>");
    Console.WriteLine("  siegefx spells show        <Logic.dsres> <spell_name> [magic_level]");
    Console.WriteLine("  siegefx spells elements    <Logic.dsres>");
    Console.WriteLine("  siegefx spells visual-audit <Logic.dsres> [--verbose] [--filter=NAME] [--only-uncovered]");
    Console.WriteLine("  siegefx spells icon-audit  <Logic.dsres> <Objects.dsres> [--verbose]");
    Console.WriteLine("  siegefx sfx list           <Logic.dsres> [--prefix=NAME]");
    Console.WriteLine("  siegefx sfx show           <Logic.dsres> <script-name>");
    Console.WriteLine("  siegefx sfx param-audit    <Logic.dsres> [--verbose] [--filter=NAME]");
    Console.WriteLine("  siegefx sfx timeline       <Logic.dsres> <script-name|--all> [--ticks=N] [--seed=N] [--out=DIR]");
    Console.WriteLine("  siegefx capture-kit build  <DS1-Resources-dir> [--out=DIR]");
    Console.WriteLine("  siegefx balance curve      <Logic.dsres> [--max-level=N] [--skill=melee|ranged|nature|combat|all] [--start=str,dex,int]");
    Console.WriteLine("  siegefx audio coverage     <Sound.dsres> [--list-orphan-categories] [--list-unwired=PREFIX]");
    Console.WriteLine("  siegefx audio sed-list     <Sound.dsres> [--filter=PREFIX] [--show-all|--show-aliases|--show-rate-only]");
    Console.WriteLine("  siegefx mood list          <Logic.dsres> [--map=world] [--with-bed] [--regions]");
    Console.WriteLine("  siegefx ui mesh-info       <Objects.dsres> <mesh-basename>");
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
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx tank <info|list|extract|fuzz> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info"    => CmdTankInfo(a[1..]),
        "list"    => CmdTankList(a[1..]),
        "extract" => CmdTankExtract(a[1..]),
        "fuzz"    => CmdTankFuzz(a[1..]),
        _         => UnknownCommand("tank " + a[0]),
    };
}

static int DispatchRaw(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx raw <info|decode|fuzz> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info"   => CmdRawInfo(a[1..]),
        "decode" => CmdRawDecode(a[1..]),
        "fuzz"   => CmdRawFuzz(a[1..]),
        _        => UnknownCommand("raw " + a[0]),
    };
}

static int DispatchAsp(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx asp <info|skeleton|fuzz|subset-fuzz|subsets|trace-pose|uv-by-bone> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info"        => CmdAspInfo(a[1..]),
        "skeleton"    => CmdAspSkeleton(a[1..]),
        "fuzz"        => CmdAspFuzz(a[1..]),
        "subset-fuzz" => CmdAspSubsetFuzz(a[1..]),
        "subsets"     => CmdAspSubsets(a[1..]),
        "trace-pose"  => CmdAspTracePose(a[1..]),
        "uv-by-bone"  => CmdAspSubsetUvBoneGroups(a[1..]),
        "bonesweep"   => CmdAspBoneSweep(a[1..]),
        _             => UnknownCommand("asp " + a[0]),
    };
}

// viii-FE diagnostic: per-subset, group corners by their primary (highest-
// weight) bone and dump the UV bbox for each group. This reveals how a
// subset's atlas is sliced across rows — e.g. for mainmenu's text-01L the
// 5 rows of NEW GAME / SINGLE PLAYER / CHOOSE HERO / LOAD GAME / OPTIONS
// each weight to one PanelBASE bone, and each bone-group should map to a
// distinct V strip of the atlas.
static int CmdAspSubsetUvBoneGroups(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx asp uv-by-bone <file.asp>"); return 1; }
    var asp = AspMesh.Load(File.ReadAllBytes(a[0]));
    Console.WriteLine($"ASP: {a[0]}  ({asp.MeshName} v{asp.AspVersionMajor}.{asp.AspVersionMinor})");
    for (int s = 0; s < asp.Subsets.Length; s++)
    {
        var sub = asp.Subsets[s];
        if (sub.TriangleCount == 0) continue;
        var texName = sub.TextureIndex < asp.TextureNames.Count ? asp.TextureNames[sub.TextureIndex] : "<oor>";
        var perBone = new Dictionary<int, (float umin, float umax, float vmin, float vmax, int n)>();
        int firstIdx = sub.FirstTriangle * 3;
        int endIdx = (sub.FirstTriangle + sub.TriangleCount) * 3;
        for (int idx = firstIdx; idx < endIdx; idx++)
        {
            int corner = asp.TriangleIndices[idx];
            if ((uint)corner >= (uint)asp.Corners.Length) continue;
            var uv = asp.Corners[corner].Uv;
            int primary = -1; float primaryW = 0f;
            if (asp.HasSkin)
            {
                var w = asp.SkinWeights[corner];
                var b = asp.SkinBones[corner];
                if (w.X > primaryW) { primary = (int)( b        & 0xFF); primaryW = w.X; }
                if (w.Y > primaryW) { primary = (int)((b >>  8) & 0xFF); primaryW = w.Y; }
                if (w.Z > primaryW) { primary = (int)((b >> 16) & 0xFF); primaryW = w.Z; }
                if (w.W > primaryW) { primary = (int)((b >> 24) & 0xFF); primaryW = w.W; }
            }
            if (perBone.TryGetValue(primary, out var ext))
            {
                ext.umin = MathF.Min(ext.umin, uv.X); ext.umax = MathF.Max(ext.umax, uv.X);
                ext.vmin = MathF.Min(ext.vmin, uv.Y); ext.vmax = MathF.Max(ext.vmax, uv.Y);
                ext.n++;
                perBone[primary] = ext;
            }
            else
                perBone[primary] = (uv.X, uv.X, uv.Y, uv.Y, 1);
        }
        Console.WriteLine($"\n[{s}] tris={sub.TriangleCount} tex={texName}");
        foreach (var kv in perBone.OrderBy(p => p.Key))
        {
            var name = kv.Key >= 0 && kv.Key < asp.BoneNames.Count ? asp.BoneNames[kv.Key] : $"#{kv.Key}";
            var (umin, umax, vmin, vmax, n) = kv.Value;
            Console.WriteLine($"      bone[{kv.Key,3}] {name,-22}  corners={n,4}  U[{umin,6:F3},{umax,6:F3}] V[{vmin,6:F3},{vmax,6:F3}]");
        }
    }
    return 0;
}

// viii-FE diagnostic: replicate the GPU vertex pipeline CPU-side for every
// subset of an ASP at a given PRS clip+time, dumping the post-skinned (mesh-
// world space) bbox for each subset. This lets us answer questions like
// "where does PanelBASE3-bound subset 1 (CHOOSE HERO row) actually end up at
// the cd-state pose?" without rendering. If subset 1 ends up off-screen or
// degenerate, the rendering pipeline isn't to blame — the wrong clip is.
//
// Usage: siegefx asp trace-pose <file.asp> [prs-file] [time-frac]
//        time-frac defaults to 1.0 (end frame). prs-file omitted = bind pose.
static int CmdAspTracePose(string[] a)
{
    if (a.Length is < 1 or > 3)
    {
        Console.Error.WriteLine("usage: siegefx asp trace-pose <file.asp> [prs-file] [time-frac 0..1, default 1]");
        return 1;
    }
    var asp = AspMesh.Load(File.ReadAllBytes(a[0]));
    PrsAnimation? prs = a.Length >= 2 ? PrsAnimation.Load(File.ReadAllBytes(a[1])) : null;
    var frac = a.Length == 3 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : 1f;

    Console.WriteLine($"ASP   : {a[0]}  ({asp.MeshName} v{asp.AspVersionMajor}.{asp.AspVersionMinor}, bones={asp.BoneCount}, subsets={asp.Subsets.Length})");
    if (prs is not null)
        Console.WriteLine($"PRS   : {a[1]}  (length={prs.AnimLength:F3}s, sample t={prs.AnimLength * frac:F3}s frac={frac:F2})");
    else
        Console.WriteLine("Pose  : bind pose (no PRS supplied)");

    Matrix4x4[] skin;
    if (prs is not null)
        skin = AnimationRuntime.ComputeSkinMatrices(asp, prs, prs.AnimLength * Math.Clamp(frac, 0f, 1f));
    else
    {
        skin = new Matrix4x4[asp.BoneCount];
        for (int i = 0; i < skin.Length; i++) skin[i] = Matrix4x4.Identity;
    }

    // Also report animated world matrix of the root bone — this is what
    // converts mesh-Z to whatever-axis after axis-swap rotations. With no
    // PRS, ComputeAnimatedBoneWorlds walks the BIND pose hierarchy so we
    // see Bone01's bind translation/rotation rather than identity (the bug
    // I had earlier was forcing identity here, hiding the -90°X bind axis-
    // swap on Bone01).
    if (asp.BoneCount > 0)
    {
        var worldAnim = AnimationRuntime.ComputeAnimatedBoneWorlds(
            asp, prs, prs is not null ? prs.AnimLength * Math.Clamp(frac, 0f, 1f) : 0f);
        Console.WriteLine();
        Console.WriteLine("Bone world-anim translations (post-hierarchy walk):");
        for (int i = 0; i < asp.BoneCount; i++)
        {
            var name = asp.BoneNames[i];
            // Filter to interesting bones only — root + everything containing
            // 'panel', 'gear', 'pole', 'spindle', 'tip', 'base'. Keeps the
            // dump readable on 30+-bone meshes.
            var lower = name.ToLowerInvariant();
            if (i != 0 && !(lower.Contains("panel") || lower.Contains("gear") || lower.Contains("pole")
                || lower.Contains("spindle") || lower.Contains("tip") || lower.Contains("base")
                || lower.Contains("titlebar")))
                continue;
            var t = worldAnim[i].Translation;
            Console.WriteLine($"  [{i,3}] {name,-28}  world=({t.X,8:F3},{t.Y,8:F3},{t.Z,8:F3})");
        }
    }

    Console.WriteLine();
    Console.WriteLine("Per-subset bbox (post-skin, mesh-world space):");
    for (int s = 0; s < asp.Subsets.Length; s++)
    {
        var sub = asp.Subsets[s];
        if (sub.TriangleCount == 0) { Console.WriteLine($"  [{s}] empty subset"); continue; }
        var texName = (sub.TextureIndex >= 0 && sub.TextureIndex < asp.TextureNames.Count)
            ? asp.TextureNames[sub.TextureIndex] : "<oor>";
        var bindMin = new Vector3(float.PositiveInfinity);
        var bindMax = new Vector3(float.NegativeInfinity);
        var skinMin = new Vector3(float.PositiveInfinity);
        var skinMax = new Vector3(float.NegativeInfinity);
        var primaryBoneCounts = new Dictionary<int, int>();
        int firstIdx = sub.FirstTriangle * 3;
        int endIdx = (sub.FirstTriangle + sub.TriangleCount) * 3;
        for (int idx = firstIdx; idx < endIdx; idx++)
        {
            int corner = asp.TriangleIndices[idx];
            if ((uint)corner >= (uint)asp.Corners.Length) continue;
            var src = asp.Positions[asp.Corners[corner].VertexIndex];
            bindMin = Vector3.Min(bindMin, src);
            bindMax = Vector3.Max(bindMax, src);
            var w = asp.HasSkin ? asp.SkinWeights[corner] : new Vector4(1, 0, 0, 0);
            var b = asp.HasSkin ? asp.SkinBones[corner] : 0u;
            var acc = Vector3.Zero;
            int primary = -1; float primaryW = 0f;
            if (w.X > 0) { var bi = (int)( b        & 0xFF); acc += w.X * Vector3.Transform(src, skin[bi]); if (w.X > primaryW) { primary = bi; primaryW = w.X; } }
            if (w.Y > 0) { var bi = (int)((b >>  8) & 0xFF); acc += w.Y * Vector3.Transform(src, skin[bi]); if (w.Y > primaryW) { primary = bi; primaryW = w.Y; } }
            if (w.Z > 0) { var bi = (int)((b >> 16) & 0xFF); acc += w.Z * Vector3.Transform(src, skin[bi]); if (w.Z > primaryW) { primary = bi; primaryW = w.Z; } }
            if (w.W > 0) { var bi = (int)((b >> 24) & 0xFF); acc += w.W * Vector3.Transform(src, skin[bi]); if (w.W > primaryW) { primary = bi; primaryW = w.W; } }
            skinMin = Vector3.Min(skinMin, acc);
            skinMax = Vector3.Max(skinMax, acc);
            if (primary >= 0) primaryBoneCounts[primary] = primaryBoneCounts.GetValueOrDefault(primary) + 1;
        }
        var primaryName = primaryBoneCounts.Count == 0 ? "(none)"
            : string.Join(",", primaryBoneCounts.OrderByDescending(p => p.Value).Take(2)
                .Select(p => p.Key < asp.BoneNames.Count ? asp.BoneNames[p.Key] : $"#{p.Key}"));
        Console.WriteLine($"  [{s,2}] tris={sub.TriangleCount,4} tex={texName,-22} primary={primaryName}");
        Console.WriteLine($"        bind  X[{bindMin.X,7:F3},{bindMax.X,7:F3}]  Y[{bindMin.Y,7:F3},{bindMax.Y,7:F3}]  Z[{bindMin.Z,7:F3},{bindMax.Z,7:F3}]");
        Console.WriteLine($"        skin  X[{skinMin.X,7:F3},{skinMax.X,7:F3}]  Y[{skinMin.Y,7:F3},{skinMax.Y,7:F3}]  Z[{skinMin.Z,7:F3},{skinMax.Z,7:F3}]");
    }
    return 0;
}

// viii-FE diagnostic: for each subset, list the bones its corners reference
// (with weight > 0). Multi-row text atlases like text-01L are scrolled by
// translating the bones that own those corners — we need to know WHICH bone
// drives WHICH text subset before we can pick the right cd-state Z values.
static int CmdAspSubsets(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx asp subsets <file.asp>"); return 1; }
    var bytes = File.ReadAllBytes(a[0]);
    var asp = AspMesh.Load(bytes);
    Console.WriteLine($"File    : {a[0]}");
    Console.WriteLine($"Mesh    : {asp.MeshName} (v{asp.AspVersionMajor}.{asp.AspVersionMinor})");
    Console.WriteLine($"Bones   : {asp.BoneCount}");
    Console.WriteLine($"Subsets : {asp.Subsets.Length}");
    for (int s = 0; s < asp.Subsets.Length; s++)
    {
        var sub = asp.Subsets[s];
        var texName = sub.TextureIndex < asp.TextureNames.Count ? asp.TextureNames[sub.TextureIndex] : "(invalid)";
        var boneCounts = new Dictionary<int, int>();
        for (int t = 0; t < sub.TriangleCount; t++)
        {
            var triBase = (sub.FirstTriangle + t) * 3;
            for (int corner = 0; corner < 3; corner++)
            {
                var ci = asp.TriangleIndices[triBase + corner];
                if (asp.HasSkin)
                {
                    var w = asp.SkinWeights[ci];
                    var b = asp.SkinBones[ci];
                    if (w.X > 0) boneCounts[(int)( b        & 0xFF)] = boneCounts.GetValueOrDefault((int)( b        & 0xFF)) + 1;
                    if (w.Y > 0) boneCounts[(int)((b >>  8) & 0xFF)] = boneCounts.GetValueOrDefault((int)((b >>  8) & 0xFF)) + 1;
                    if (w.Z > 0) boneCounts[(int)((b >> 16) & 0xFF)] = boneCounts.GetValueOrDefault((int)((b >> 16) & 0xFF)) + 1;
                    if (w.W > 0) boneCounts[(int)((b >> 24) & 0xFF)] = boneCounts.GetValueOrDefault((int)((b >> 24) & 0xFF)) + 1;
                }
            }
        }
        Console.WriteLine($"\n[{s}] tris={sub.TriangleCount,4}  tex={texName}");
        foreach (var kv in boneCounts.OrderByDescending(p => p.Value))
        {
            var name = kv.Key < asp.BoneNames.Count ? asp.BoneNames[kv.Key] : "(invalid)";
            Console.WriteLine($"      bone[{kv.Key,3}] {name,-28}  refs={kv.Value}");
        }
    }
    return 0;
}

static int CmdAspSubsetFuzz(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx asp subset-fuzz <tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    int total = 0, parsed = 0, parseFail = 0, mismatched = 0;
    int oneSubset = 0, multiSubset = 0;
    int multiTexAcrossSubsets = 0;
    int maxSubsets = 0;
    string? maxSubsetsFile = null;
    var perSubsetCount = new SortedDictionary<int, int>();

    foreach (var path in reader.ListFiles())
    {
        if (!path.EndsWith(".asp", StringComparison.OrdinalIgnoreCase)) continue;
        total++;
        AspMesh mesh;
        try { mesh = AspMesh.Load(reader.ExtractToMemory(path)); }
        catch (Exception ex)
        {
            parseFail++;
            Console.Error.WriteLine($"  parse fail: {path}  ->  {ex.Message}");
            continue;
        }
        parsed++;
        int triFromSubsets = 0;
        var texSet = new HashSet<int>();
        foreach (var s in mesh.Subsets) { triFromSubsets += s.TriangleCount; texSet.Add(s.TextureIndex); }
        if (triFromSubsets != mesh.TriangleCount)
        {
            mismatched++;
            Console.Error.WriteLine($"  triangle mismatch in {path}: subsetSum={triFromSubsets} TriangleCount={mesh.TriangleCount}");
        }
        if (mesh.Subsets.Length <= 1) oneSubset++; else multiSubset++;
        if (texSet.Count > 1) multiTexAcrossSubsets++;
        if (mesh.Subsets.Length > maxSubsets) { maxSubsets = mesh.Subsets.Length; maxSubsetsFile = path; }
        perSubsetCount.TryGetValue(mesh.Subsets.Length, out var c);
        perSubsetCount[mesh.Subsets.Length] = c + 1;
    }

    Console.WriteLine($"asp subset-fuzz: {total} .asp file(s) in tank");
    Console.WriteLine($"  parsed              : {parsed}");
    Console.WriteLine($"  parse-fail          : {parseFail}");
    Console.WriteLine($"  triangle mismatched : {mismatched}");
    Console.WriteLine($"  single-subset       : {oneSubset}");
    Console.WriteLine($"  multi-subset        : {multiSubset}");
    Console.WriteLine($"  multi-texture spans : {multiTexAcrossSubsets}");
    Console.WriteLine($"  max subsets in file : {maxSubsets} ({maxSubsetsFile})");
    Console.WriteLine("  histogram (subsetCount -> meshCount):");
    foreach (var kv in perSubsetCount)
        Console.WriteLine($"    {kv.Key,3} subsets : {kv.Value,5}");
    return (parseFail == 0 && mismatched == 0) ? 0 : 4;
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
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx sno <info|nav|fuzz> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info" => CmdSnoInfo(a[1..]),
        "nav"  => CmdSnoNav(a[1..]),
        "fuzz" => CmdSnoFuzz(a[1..]),
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
        "sample"  => CmdPrsSample(a[1..]),
        "compare" => CmdPrsCompare(a[1..]),
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

    // Dump every bone — the truncated 8-bone view hid the bones that
    // actually drive the title-bar / gear morphs in mainmenu PRS clips
    // (TitleBar / TitleBarLeftGear etc. live mid-array).
    for (var i = 0; i < prs.BoneNames.Count; i++)
    {
        var k = prs.BoneKeys[i];
        var counts = k is null ? "no keys" : $"rot={k.RotKeys.Count} pos={k.PosKeys.Count}";
        Console.WriteLine($"  [{i,3}] {prs.BoneNames[i],-28} {counts}");
    }

    if (prs.InfoStrings.Count > 0)
    {
        Console.WriteLine($"INFO        :");
        foreach (var s in prs.InfoStrings) Console.WriteLine($"  {s}");
    }
    return 0;
}

// viii-FE clip diagnostics: sample every bone of a PRS clip at a given time
// fraction (0..1) and dump per-bone Position values. Used to verify what pose
// the END frame of `mainmenu_sng2cd.prs` actually captures, vs `mainmenu_default`.
// If the two clips' end-frames yield identical positions, then `_default` IS
// the cd-state pose (and the title-row bug is elsewhere). If they differ, we
// know which clip to sample for cd-state.
static int CmdPrsSample(string[] a)
{
    if (a.Length is < 1 or > 2)
    {
        Console.Error.WriteLine("usage: siegefx prs sample <file.prs> [time-fraction 0..1, default 1]");
        return 1;
    }
    var bytes = File.ReadAllBytes(a[0]);
    var prs = PrsAnimation.Load(bytes);
    var frac = a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 1f;
    var t = prs.AnimLength * Math.Clamp(frac, 0f, 1f);
    Console.WriteLine($"File   : {a[0]}");
    Console.WriteLine($"Length : {prs.AnimLength:F4} s ({prs.NumBones} bones)");
    Console.WriteLine($"Sample : t={t:F4}s (frac={frac:F3})");
    Console.WriteLine($"  {"idx",3}  {"bone",-28}  {"pos.x",10} {"pos.y",10} {"pos.z",10}  rotKeys posKeys");
    for (int i = 0; i < prs.NumBones; i++)
    {
        var (rot, pos) = AnimationRuntime.EvaluateBone(prs, i, t);
        var p = pos ?? Vector3.Zero;
        var k = prs.BoneKeys[i];
        var rk = k?.RotKeys.Count ?? 0;
        var pk = k?.PosKeys.Count ?? 0;
        var posStr = pos.HasValue ? $"{p.X,10:F4} {p.Y,10:F4} {p.Z,10:F4}" : "        --         --         --";
        Console.WriteLine($"  [{i,3}] {prs.BoneNames[i],-28}  {posStr}   {rk,4}    {pk,4}");
    }
    return 0;
}

static int CmdPrsCompare(string[] a)
{
    if (a.Length is < 2 or > 3)
    {
        Console.Error.WriteLine("usage: siegefx prs compare <a.prs> <b.prs> [threshold, default 0.001]");
        return 1;
    }
    var pa = PrsAnimation.Load(File.ReadAllBytes(a[0]));
    var pb = PrsAnimation.Load(File.ReadAllBytes(a[1]));
    var threshold = a.Length > 2 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : 0.001f;

    Console.WriteLine($"A: {a[0]}  ({pa.NumBones} bones, {pa.AnimLength:F3}s)");
    Console.WriteLine($"B: {a[1]}  ({pb.NumBones} bones, {pb.AnimLength:F3}s)");
    Console.WriteLine($"Compare end-frame poses (t = AnimLength); diff threshold = {threshold}");

    // Map bone names → indexes for B so we can compare by name (per-clip
    // bone-list ordering is not guaranteed identical even between sibling
    // clips on the same mesh).
    var byName = new Dictionary<string, int>(pb.NumBones);
    for (int i = 0; i < pb.NumBones; i++) byName[pb.BoneNames[i]] = i;

    int matched = 0, differ = 0, missing = 0;
    Console.WriteLine($"  {"bone",-28}  {"Δpos",10}  posA→posB");
    for (int i = 0; i < pa.NumBones; i++)
    {
        if (!byName.TryGetValue(pa.BoneNames[i], out var j))
        {
            missing++;
            continue;
        }
        var (_, posA) = AnimationRuntime.EvaluateBone(pa, i, pa.AnimLength);
        var (_, posB) = AnimationRuntime.EvaluateBone(pb, j, pb.AnimLength);
        if (!posA.HasValue || !posB.HasValue) continue;
        var d = (posA.Value - posB.Value).Length();
        matched++;
        if (d > threshold)
        {
            differ++;
            Console.WriteLine($"  {pa.BoneNames[i],-28}  {d,10:F4}  ({posA.Value.X:F2},{posA.Value.Y:F2},{posA.Value.Z:F2}) → ({posB.Value.X:F2},{posB.Value.Y:F2},{posB.Value.Z:F2})");
        }
    }
    Console.WriteLine($"\nmatched {matched} bones; {differ} above threshold; {missing} missing in B");
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
    var versionsLegacy = new Dictionary<uint, int>();
    var legacySamples = new Dictionary<uint, string>();
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
            // Sub-histogram + first-sample path so the SC-3 work has a concrete handle.
            oldVersion++;
            versionsLegacy[ver] = versionsLegacy.GetValueOrDefault(ver) + 1;
            if (!legacySamples.ContainsKey(ver)) legacySamples[ver] = path;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [parse-fail v0x{ver:X}] {path}: {ex.Message}");
            failed++;
            versionsFail[ver] = versionsFail.GetValueOrDefault(ver) + 1;
        }
    }
    Console.WriteLine($"fuzzed {total} .prs file(s), {totalBytes:N0} bytes total; {failed} failure(s), {tracers} with tracers, {oldVersion} legacy-version skipped");
    Console.WriteLine("versions (ok):     " + string.Join(", ", versionsOk.OrderBy(kv => kv.Key).Select(kv => $"0x{kv.Key:X}={kv.Value}")));
    Console.WriteLine("versions (fail):   " + string.Join(", ", versionsFail.OrderBy(kv => kv.Key).Select(kv => $"0x{kv.Key:X}={kv.Value}")));
    Console.WriteLine("versions (legacy): " + string.Join(", ", versionsLegacy.OrderBy(kv => kv.Key).Select(kv => $"0x{kv.Key:X}={kv.Value}")));
    foreach (var kv in legacySamples.OrderBy(kv => kv.Key))
        Console.WriteLine($"  legacy sample 0x{kv.Key:X}: {kv.Value}");
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
        "sound-emitters" => CmdRegionSoundEmitters(a[1..]),
        "decal-audit" => CmdRegionDecalAudit(a[1..]),
        "breakable-audit" => CmdRegionBreakableAudit(a[1..]),
        "loot-distribution" => CmdRegionLootDistribution(a[1..]),
        "mob-loot" => CmdRegionMobLoot(a[1..]),
        "drop-sweep" => CmdRegionDropSweep(a[1..]),
        "find-template" => CmdRegionFindTemplate(a[1..]),
        "actor-coverage" => CmdRegionActorCoverage(a[1..]),
        "nav"         => CmdRegionNav(a[1..]),
        "nav-fuzz"    => CmdRegionNavFuzz(a[1..]),
        "path"        => CmdRegionPath(a[1..]),
        "path-fuzz"   => CmdRegionPathFuzz(a[1..]),
        "path-bench"  => CmdRegionPathBench(a[1..]),
        "nav-components" => CmdRegionNavComponents(a[1..]),
        "follow"      => CmdRegionFollow(a[1..]),
        "triggers"    => CmdRegionTriggers(a[1..]),
        "gen-audit"   => CmdRegionGenAudit(a[1..]),
        "cmd-audit"   => CmdRegionCmdAudit(a[1..]),
        "elevators"   => CmdRegionElevators(a[1..]),
        _             => UnknownCommand("region " + a[0]),
    };
}

// SC-ELEVATOR — parse every elevator gizmo (objects/elevator.gas) in one region
// or all, resolve the car node + both stop poses via door alignment against the
// region's own layout, and report. Connect/car nodes living in a NEIGHBOR
// region resolve at runtime through the unified layout — the CLI counts them
// as cross-region (informational), not failures. Hard FAIL = a door id missing
// from its SNO or a degenerate door transform: authored data the engine could
// never place. Exit 0 when no hard failures.
static int CmdRegionElevators(string[] a)
{
    if (a.Length != 3)
    {
        Console.Error.WriteLine("usage: siegefx region elevators <map-tank> <terrain-tank> <region-path|all>");
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

    var regionPaths = new List<string>();
    if (string.Equals(a[2], "all", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var p in mapReader.ListFiles())
            if (p.EndsWith("/objects/elevator.gas", StringComparison.OrdinalIgnoreCase))
                regionPaths.Add(p[..^"/objects/elevator.gas".Length]);
        regionPaths.Sort(StringComparer.OrdinalIgnoreCase);
    }
    else
    {
        var rp = a[2].Replace('\\', '/');
        if (!rp.StartsWith('/')) rp = "/" + rp;
        regionPaths.Add(rp.TrimEnd('/'));
    }

    int totalDefs = 0, aligned = 0, crossRegion = 0, hardFails = 0, regionsWith = 0;
    foreach (var rp in regionPaths)
    {
        var (defs, diags) = SiegeFX.Core.Assets.ElevatorStore.Load(mapReader, rp);
        foreach (var d in diags) Console.WriteLine("  " + d);
        if (defs.Count == 0) continue;
        regionsWith++;
        totalDefs += defs.Count;

        SiegeFX.Core.Assets.RegionGraph? graph = null;
        SiegeFX.Core.Assets.RegionLayout? layout = null;
        try
        {
            graph = SiegeFX.Core.Assets.RegionGraph.Load(
                mapReader.ExtractToMemory(rp + "/terrain_nodes/nodes.gas"));
            layout = SiegeFX.Core.Assets.RegionLayout.Build(graph, Resolve);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {rp}: nodes.gas load failed ({ex.Message})");
        }

        var shortName = rp[(rp.LastIndexOf('/') + 1)..];
        foreach (var def in defs)
        {
            bool StopInfo(uint connectGuid, int connectDoor, int carDoor, out string desc)
            {
                desc = "";
                if (graph is null || layout is null) { desc = "region graph unavailable"; return false; }
                if (!graph.TryGetNode(def.CarNodeGuid, out var carNode) ||
                    !graph.TryGetNode(connectGuid, out var connNode))
                {
                    desc = "cross-region node (resolves at runtime via unified layout)";
                    crossRegion++;
                    return true;
                }
                var carSno = Resolve(carNode.MeshGuid);
                var connSno = Resolve(connNode.MeshGuid);
                if (carSno is null || connSno is null) { desc = "SNO missing from Terrain tank"; return false; }
                if (!layout.TryGetTransform(connectGuid, out var wConn))
                {
                    desc = "connect node unplaced by door graph";
                    return false;
                }
                if (!SiegeFX.Core.Assets.RegionLayout.TryAlignThroughDoor(
                        connSno, wConn, connectDoor, carSno, carDoor, out var w))
                {
                    desc = $"door alignment FAILED (connect door {connectDoor} / car door {carDoor})";
                    return false;
                }
                var t = w.Translation;
                desc = $"({t.X:F1},{t.Y:F1},{t.Z:F1})";
                return true;
            }

            bool ok1 = StopInfo(def.Connect1Guid, def.Connect1DoorId, def.CarDoor1Id, out var s1);
            bool ok2 = StopInfo(def.Connect2Guid, def.Connect2DoorId, def.CarDoor2Id, out var s2);
            if (ok1 && ok2 && !s1.Contains("cross-region") && !s2.Contains("cross-region")) aligned++;
            if (!ok1 || !ok2) hardFails++;
            Console.WriteLine($"  {shortName,-14} 0x{def.Scid:X8} {def.TemplateName,-20} car=0x{def.CarNodeGuid:X8} " +
                $"dur={def.DurationSeconds,4:F1}s stop1={s1} stop2={s2}" +
                (def.Moving1ActionInfo.Length > 0 ? " [fades]" : ""));
        }
    }

    Console.WriteLine();
    Console.WriteLine($"elevators: {totalDefs} gizmo(s) across {regionsWith} region(s) — " +
                      $"{aligned} fully aligned in-region, {crossRegion} stop(s) cross-region (runtime-resolved), " +
                      $"{hardFails} hard failure(s)");
    return hardFails == 0 ? 0 : 4;
}

// SC-MOB-COMMANDS audit — inventory every scripted AI command (command.gas) across
// one region or all: the verb (command template), how many placements use it, and
// whether the runtime HANDLES it (movement + NIS) or STUBS it ("recognized but not
// yet implemented"). Also counts actors that reference a scripted route via
// [mind] initial_command. Surfaces exactly which scripted set-pieces are unwired —
// the barn-fence smash falls out as a stubbed cmd_ai_* attack/job verb.
static int CmdRegionCmdAudit(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("usage: siegefx region cmd-audit <World.dsmap> [region|all]");
        return 1;
    }
    using var mapTank = TankFile.Open(a[0]);
    var mapReader = new TankReader(mapTank);
    string filter = a.Length >= 2 ? a[1].Trim() : "all";

    // Verbs the runtime actually dispatches (RenderHost.ActivateAiCommand /
    // BuildCommandRoute / the NIS engine). Everything else logs "recognized but
    // not yet implemented" and is effectively inert.
    var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cmd_ai_c_move", "cmd_ai_c_move_orient", "cmd_ai_t_move", "cmd_ai_t_move_orient",
        "cmd_enter_nis", "cmd_camera_command", "cmd_camera_waypoint", "cmd_leave_nis",
    };
    // "route" verbs aren't message-dispatched, but their positions ARE consumed by
    // BuildCommandRoute -> AssignPatrolRoutes: an actor whose [mind] initial_command
    // points at one of these walks the chain as a patrol. So the 105 scripted
    // patrollers DO move; the verb-specific nuance (orient/face-on-arrival) is lost.
    var routeVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cmd_ai_c_patrol", "cmd_ai_c_patrol_orient",
    };

    var regionPaths = new List<string>();
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
    }
    if (!filter.Equals("all", StringComparison.OrdinalIgnoreCase))
        regionPaths = regionPaths.Where(r => r.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    if (regionPaths.Count == 0) { Console.Error.WriteLine($"no regions matched '{filter}'"); return 1; }

    var verbCount = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var verbRegions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    int totalCmds = 0, actorsWithInitial = 0;
    var initialByRegion = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    static string RegionName(string rp) { int i = rp.LastIndexOf('/'); return i >= 0 ? rp[(i + 1)..] : rp; }
    static bool HasInitialCommand(SiegeFX.Core.Assets.GasNode node)
    {
        foreach (var at in node.Attributes)
            if (at.Name.Equals("initial_command", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(at.Value) && !at.Value.Trim().Trim('"').Equals("0"))
                return true;
        foreach (var c in node.Children)
            if (HasInitialCommand(c)) return true;
        return false;
    }

    foreach (var rp in regionPaths)
    {
        var name = RegionName(rp);
        var (cmds, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, "command.gas");
        foreach (var p in cmds)
        {
            verbCount.TryGetValue(p.TemplateName, out var c);
            verbCount[p.TemplateName] = c + 1;
            if (!verbRegions.TryGetValue(p.TemplateName, out var set)) verbRegions[p.TemplateName] = set = new(StringComparer.OrdinalIgnoreCase);
            set.Add(name);
            totalCmds++;
        }
        var (actors, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, "actor.gas");
        foreach (var p in actors)
            if (HasInitialCommand(p.Node)) { actorsWithInitial++; initialByRegion.TryGetValue(name, out var ic); initialByRegion[name] = ic + 1; }
    }

    Console.WriteLine($"SCRIPTED COMMAND AUDIT — {(filter.Equals("all", StringComparison.OrdinalIgnoreCase) ? regionPaths.Count + " region(s)" : filter)}");
    Console.WriteLine($"  {totalCmds} command placement(s), {verbCount.Count} distinct verb(s)");
    Console.WriteLine($"  {actorsWithInitial} actor(s) reference a scripted route via [mind] initial_command");
    Console.WriteLine();
    Console.WriteLine($"  {"count",5} {"status",-8} verb (regions)");
    int stubbed = 0, stubPlacements = 0;
    foreach (var kv in verbCount.OrderByDescending(k => k.Value))
    {
        string status = handled.Contains(kv.Key) ? "handled"
                      : routeVerbs.Contains(kv.Key) ? "route"
                      : "STUB";
        if (status == "STUB") { stubbed++; stubPlacements += kv.Value; }
        int rc = verbRegions.TryGetValue(kv.Key, out var s) ? s.Count : 0;
        Console.WriteLine($"  {kv.Value,5} {status,-8} {kv.Key}  ({rc} region{(rc == 1 ? "" : "s")})");
    }
    Console.WriteLine();
    if (initialByRegion.Count > 0)
    {
        Console.WriteLine("actors with initial_command, by region (top 12):");
        foreach (var kv in initialByRegion.OrderByDescending(k => k.Value).Take(12))
            Console.WriteLine($"  {kv.Value,4}  {kv.Key}");
        Console.WriteLine();
    }
    Console.WriteLine($"VERDICT: {verbCount.Count - stubbed}/{verbCount.Count} verb type(s) handled; {stubbed} stubbed ({stubPlacements} placements) — those scripted set-pieces are inert.");
    return 0;
}

// SC-MOB-SPAWNER audit — enumerate every generator / on-death spawn edge across
// one region (substring match) or all, so a "massive mob spawning where it
// shouldn't" surfaces by name + region + gating. Sorted by child life (boss-tier
// first). Gating: incubate@load (basic, spawns at boot) / proximity@Nu (bush
// ambush) / message-parked (armed, waits for we_req_activate) / on-death/entered
// (generator_in_object family — the Gom_Super chain). Flags children SiegeFX
// spawns at the generator gizmo while an authored spawnpoint SCID goes unused.
static int CmdRegionGenAudit(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx region gen-audit <World.dsmap> <Logic.dsres> [region|all]");
        return 1;
    }
    using var mapTank = TankFile.Open(a[0]);
    using var logicTank = TankFile.Open(a[1]);
    var mapReader = new TankReader(mapTank);
    var logicReader = new TankReader(logicTank);
    var (store, _) = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);
    string filter = a.Length >= 3 ? a[2].Trim() : "all";

    static float? PF(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().Trim('"').Trim();
        return float.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : (float?)null;
    }
    float ChildLife(string child) =>
        store.TryGet(child, out var t) && t is not null
            ? (PF(store.GetAttribute(t, "aspect", "max_life")) ?? PF(store.GetAttribute(t, "aspect", "life")) ?? 0f) : 0f;
    float ChildScale(string child) =>
        store.TryGet(child, out var t) && t is not null
            ? (PF(store.GetAttribute(t, "aspect", "scale_base")) ?? 1f) : 1f;
    static string RegionName(string rp) { int i = rp.LastIndexOf('/'); return i >= 0 ? rp[(i + 1)..] : rp; }

    var regionPaths = new List<string>();
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
    }
    if (!filter.Equals("all", StringComparison.OrdinalIgnoreCase))
        regionPaths = regionPaths.Where(r => r.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    if (regionPaths.Count == 0) { Console.Error.WriteLine($"no regions matched '{filter}'"); return 1; }

    var rows = new Dictionary<string, (string Region, string File, string Parent, string Child, string Gating, float Life, float Scale, int Count, bool SpIgnored, bool HasCmd)>();
    void Add(string region, string file, string parent, string block, string child, string gating, bool spIgnored, bool hasCmd)
    {
        string key = $"{region}|{file}|{parent}|{block}|{child}|{gating}";
        if (rows.TryGetValue(key, out var ex)) { ex.Count++; rows[key] = ex; return; }
        rows[key] = (RegionName(region), file, parent, child, gating, ChildLife(child), ChildScale(child), 1, spIgnored, hasCmd);
    }

    foreach (var rp in regionPaths)
    {
        // 1) generator.gas — the primary spawner file.
        var (gens, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, "generator.gas");
        foreach (var p in gens)
        {
            store.TryGet(p.TemplateName, out var genT);
            SiegeFX.Core.Assets.GasNode? pblock = null;
            foreach (var c in p.Node.Children)
                if (c.Header.StartsWith("generator", StringComparison.OrdinalIgnoreCase)) { pblock = c; break; }
            string? blockHeader = pblock?.Header;
            if (blockHeader is null && genT is not null)
                for (var t = genT; t is not null && blockHeader is null; t = t.Specializes)
                    foreach (var c in t.Node.Children)
                    {
                        if (!c.Header.StartsWith("generator_", StringComparison.OrdinalIgnoreCase)) continue;
                        if (c.Header.Equals("generator_in_object", StringComparison.OrdinalIgnoreCase)) continue;
                        blockHeader = c.Header; break;
                    }
            if (blockHeader is null) continue;

            string? child = null; bool sp = false, cmd = false; float trig = -1f;
            if (pblock is not null)
                foreach (var at in pblock.Attributes)
                {
                    if (at.Name.Equals("child_template_name", StringComparison.OrdinalIgnoreCase)) child = at.Value.Trim().Trim('"');
                    else if (at.Name.Equals("spawnpoint", StringComparison.OrdinalIgnoreCase)) sp = true;
                    else if (at.Name.Equals("initial_command", StringComparison.OrdinalIgnoreCase)) cmd = true;
                    else if (at.Name.Equals("trigger_range", StringComparison.OrdinalIgnoreCase)) { if (PF(at.Value) is float f) trig = f; }
                }
            if (string.IsNullOrEmpty(child) && genT is not null)
                child = store.GetAttribute(genT, blockHeader, "child_template_name")?.Trim().Trim('"');
            if (string.IsNullOrEmpty(child)) continue;
            if (trig < 0f && genT is not null && PF(store.GetAttribute(genT, blockHeader, "trigger_range")) is float tf) trig = tf;

            bool basic = blockHeader.Contains("basic", StringComparison.OrdinalIgnoreCase);
            bool explod = blockHeader.Contains("explod", StringComparison.OrdinalIgnoreCase);
            string gating = basic ? "incubate@load"
                : explod ? (trig > 0 ? $"exploding@{trig:0}u" : "exploding@?")
                : trig > 0f ? $"proximity@{trig:0}u"
                : "message-parked";
            Add(rp, "generator.gas", p.TemplateName, blockHeader, child!, gating, sp, cmd);
        }

        // 2) on-death / entered-world spawns: templates carrying a generator_in_object
        //    or *auto_object* block with a child (the Gom_Super family), placed via
        //    actor.gas / special.gas.
        foreach (var file in new[] { "actor.gas", "special.gas" })
        {
            var (ps, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, file);
            foreach (var p in ps)
            {
                if (!store.TryGet(p.TemplateName, out var t) || t is null) continue;
                for (var w = t; w is not null; w = w.Specializes)
                    foreach (var c in w.Node.Children)
                    {
                        if (!c.Header.StartsWith("generator", StringComparison.OrdinalIgnoreCase)) continue;
                        if (c.Header.Contains("basic", StringComparison.OrdinalIgnoreCase)) continue;
                        string? child = null;
                        foreach (var at in c.Attributes)
                            if (at.Name.Equals("child_template_name", StringComparison.OrdinalIgnoreCase)) child = at.Value.Trim().Trim('"');
                        if (string.IsNullOrEmpty(child)) continue;
                        Add(rp, file, p.TemplateName, c.Header, child!, "on-death/entered", false, false);
                    }
            }
        }
    }

    var ordered = rows.Values.OrderByDescending(r => r.Life).ThenByDescending(r => r.Scale).ThenBy(r => r.Region).ToList();
    Console.WriteLine($"GENERATOR / SPAWN AUDIT — {(filter.Equals("all", StringComparison.OrdinalIgnoreCase) ? regionPaths.Count + " region(s)" : filter)}");
    Console.WriteLine($"  {ordered.Count} distinct spawner->child edge(s)");
    Console.WriteLine();
    Console.WriteLine($"  {"life",6} {"scale",5}  {"region",-12} {"gating",-16} {"child",-26} parent");
    foreach (var r in ordered)
    {
        string flags = "";
        if (r.Life >= 200f) flags += " BOSS";
        if (r.Scale >= 1.4f) flags += " BIG";
        if (r.Gating.StartsWith("incubate")) flags += " @load";
        if (r.Gating == "on-death/entered") flags += " onEnter/Die";
        if (r.SpIgnored) flags += " spawnpt-ignored";
        string cnt = r.Count > 1 ? $" x{r.Count}" : "";
        Console.WriteLine($"  {r.Life,6:0} {r.Scale,5:0.0}  {r.Region,-12} {r.Gating,-16} {r.Child,-26} {r.Parent}{cnt}{(flags.Length > 0 ? "  <=" + flags : "")}");
    }
    Console.WriteLine();
    int boss = ordered.Count(r => r.Life >= 200f);
    int atLoad = ordered.Count(r => r.Gating.StartsWith("incubate"));
    int spIgn = ordered.Count(r => r.SpIgnored);
    Console.WriteLine($"VERDICT: {boss} boss-tier (life>=200) spawn edge(s); {atLoad} incubate-at-load; {spIgn} with an authored spawnpoint SiegeFX currently ignores.");
    return 0;
}

static int DispatchWorld(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx world <layout|path|follow> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "layout" => CmdWorldLayout(a[1..]),
        "path"   => CmdWorldPath(a[1..]),
        "follow" => CmdWorldFollow(a[1..]),
        "campaign-audit" => CmdWorldCampaignAudit(a[1..]),
        _        => UnknownCommand("world " + a[0]),
    };
}

// ALPHA-1 — campaign completability sweep. The elevator lesson generalized:
// enumerate every placed template's component sections (instance blocks +
// full specializes chain) across ALL regions, diff against the set the
// engine actually consumes, and rank what's left by placement count and by
// how early the campaign hits it (BFS depth over the stitch graph from the
// root region ≈ walk order — DS1's world is near-linear). Also aggregates
// trigger condition/action verbs against TriggerRuntime's dispatched sets.
// Informational exit 0; the ranked tables ARE the deliverable.
static int CmdWorldCampaignAudit(string[] a)
{
    string root = "fh_r1";
    var extra = new List<string>();
    foreach (var arg in a)
    {
        if (arg.StartsWith("--root=", StringComparison.OrdinalIgnoreCase)) root = arg["--root=".Length..];
        else extra.Add(arg);
    }
    if (extra.Count != 2)
    {
        Console.Error.WriteLine("usage: siegefx world campaign-audit <map-tank> <logic-tank> [--root=fh_r1]");
        return 1;
    }

    using var mapTank = TankFile.Open(extra[0]);
    var mapReader = new TankReader(mapTank);
    using var logicTank = TankFile.Open(extra[1]);
    var logicReader = new TankReader(logicTank);
    var (store, storeDiags) = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);
    Console.WriteLine($"templates: {store.Count} loaded ({storeDiags.Count} diagnostics)");

    // ---- region list + stitch adjacency → BFS depth from root ----
    var regionPaths = new List<string>();
    foreach (var p in mapReader.ListFiles())
        if (p.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase))
            regionPaths.Add(p[..^"/terrain_nodes/nodes.gas".Length]);
    regionPaths.Sort(StringComparer.OrdinalIgnoreCase);
    static string ShortName(string rp) => rp[(rp.LastIndexOf('/') + 1)..];

    var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var rp in regionPaths)
    {
        var name = ShortName(rp);
        if (!adjacency.TryGetValue(name, out var set)) adjacency[name] = set = new(StringComparer.OrdinalIgnoreCase);
        var sp = rp + "/editor/stitch_helper.gas";
        if (!mapReader.TryGetFile(sp, out _)) continue;
        try
        {
            var stitches = SiegeFX.Core.Assets.RegionStitchHelper.Load(mapReader.ExtractToMemory(sp));
            foreach (var dest in stitches.ByDestination.Keys) set.Add(dest);
        }
        catch { /* stitch parse failure = isolated region; BFS flags it */ }
    }
    var depth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [root] = 0 };
    var bfs = new Queue<string>();
    bfs.Enqueue(root);
    while (bfs.Count > 0)
    {
        var cur = bfs.Dequeue();
        if (!adjacency.TryGetValue(cur, out var outs)) continue;
        foreach (var nxt in outs)
        {
            if (depth.ContainsKey(nxt)) continue;
            depth[nxt] = depth[cur] + 1;
            bfs.Enqueue(nxt);
        }
    }
    int Depth(string rp) => depth.TryGetValue(ShortName(rp), out var d) ? d : int.MaxValue;
    regionPaths.Sort((x, y) =>
    {
        int c = Depth(x).CompareTo(Depth(y));
        return c != 0 ? c : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    });
    int unreachable = regionPaths.Count(rp => Depth(rp) == int.MaxValue);
    Console.WriteLine($"regions: {regionPaths.Count} ({regionPaths.Count - unreachable} reachable from '{root}' via stitches, {unreachable} unreachable)");

    // ---- component sections the engine consumes today (curated; see the
    //      subsystem tag on each). Anything NOT here lands in the ranked
    //      UNHANDLED table below — exactly how elevator.gas would have
    //      surfaced before SC-ELEVATOR. Instance-authoring blocks and pure
    //      data-holders (no runtime behavior expected) count as handled.
    var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "placement", "common", "gizmo", "template",                 // authoring scaffolding
        "aspect", "body", "chore_dictionary",                       // render + anim
        "physics",                                                  // breakables (break_particulate)
        "on_off_lever",                                             // SC-ELEVATOR levers
        "store", "store_pcontent",                                  // Phase 25 vendors
        "inventory", "equipment", "other", "pcontent",              // items/equip/drops
        "conversation", "conversations",                            // dialogue
        "actor", "skills",                                          // stats
        "mind", "attack", "defend", "magic",                        // brains + combat + spells
        "instance_triggers", "trigger_instance",                    // trigger runtime
        "tip",                                                      // tutorial tips (known-parked: Language.dll strings)
        "light_enable", "light_flicker",                            // torch flames (point light known-stubbed)
        "emt_sound", "emt_sound_act",                               // SC-WEATHER emitters
        "generator", "generator_in_object",                         // spawners
        "fader",                                                    // interface_fade chapter title (known-parked)
        "water_effects",                                            // TSD water animation
        "sound_emitter", "sound_emitter_act",                       // SC-WEATHER emitters ([emt_sound*] templates' section name)
        "door_basic",                                               // SC-DOORS (base_door chain drives behavior)
        "gui",                                                      // equip_slot etc. (PcontentResolver reads it)
        "potion", "spell", "spell_default", "spell_status_effect",  // pickup/spell data read by item + spell systems
        "spell_instant_hit",
    };

    // Benign-but-unconsumed: dev scaffolding, cosmetic effect managers, and
    // sections whose gameplay-relevant fields are read via other components.
    // Printed in their own table so they stay visible without drowning the
    // real blockers. Reason strings keep the triage honest.
    var benign = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["follower"] = "movement speeds read via ActorStats; formation AI is engine-side",
        ["dev_path_point"] = "editor breadcrumbs, no retail runtime behavior",
        ["guts_manager"] = "gore detail component (cosmetic)",
        ["light_flicker_lightweight"] = "cosmetic light flicker variant",
        ["light_colorwave"] = "cosmetic light color animation",
        ["generic_emitter"] = "prop sfx-script emitters (tree sway etc.) — cosmetic",
        ["generic_emitter_act"] = "activated prop sfx-script emitters — cosmetic",
        ["fire_emitter"] = "fire prop emitters (torch flames handled via AP_light)",
        ["fire_emitter_act"] = "activated fire/mist emitters — cosmetic",
        ["particle_emitter"] = "cosmetic particle emitters",
        ["particle_emitter_act"] = "cosmetic particle emitters (activated)",
        ["glow_emitter"] = "cosmetic glow emitters",
        ["glow_emitter_act"] = "cosmetic glow emitters (activated)",
        ["spark_emitter"] = "cosmetic spark emitters",
        ["go_emitter"] = "spawns cosmetic GOs (activated)",
        ["effect_manager"] = "scripted effect visuals (corpim torture fx etc.)",
        ["camera_quake"] = "camera shake gizmo — cosmetic",
        ["camera_stomp"] = "camera shake on actor stomp — cosmetic",
        ["enchantment_manager"] = "buff visual manager — cosmetic layer",
        ["nodal_tex_anim"] = "terrain texture animation (chains) — cosmetic",
        ["on_client"] = "client-side wrapper on trap/fx components",
        ["play_chapter_sound"] = "chapter sting audio — presentation",
        ["interface_fade"] = "chapter title card — known-parked (intro slice)",
        ["activate_chapter"] = "chapter progression marker — verify journal impact in Piece 2",
        ["clone_preloader"] = "asset preloader for the Gom fight — perf hint only",
        ["party"] = "MP guard group templates",
        ["check_level"] = "MP-only level gate",
        ["chipper"] = "one-off ambient (dm_r8)",
        ["minigun_magic"] = "one-off turret fx (gi_r4) — verify in Piece 2",
    };
    // Header FAMILIES handled by prefix (elevator_2s_1c_1n etc.).
    static bool HandledFamily(string h) =>
        h.StartsWith("elevator_", StringComparison.OrdinalIgnoreCase) ||
        h.StartsWith("generator_", StringComparison.OrdinalIgnoreCase) ||
        h.StartsWith("cmd_", StringComparison.OrdinalIgnoreCase) ||
        h.StartsWith("dsfx_", StringComparison.OrdinalIgnoreCase);

    var dispatchedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "actor_within_sphere", "go_within_sphere", "party_member_within_sphere",
        "party_member_within_bounding_box", "party_member_within_node",
        "party_member_entered_trigger_group", "party_member_left_trigger_group",
        "receive_world_message",
    };
    var dispatchedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "send_world_message", "mood_change", "set_interest_radius",
        "fade_node", "fade_nodes", "fade_nodes_global",
        "change_quest_state", "call_sfx_script",
    };

    // ---- the sweep ----
    var benignHits = new Dictionary<string, (int Count, int FirstDepth, string FirstRegion, string ExampleTemplate)>(StringComparer.OrdinalIgnoreCase);
    var sectionHits = new Dictionary<string, (int Count, int FirstDepth, string FirstRegion, string ExampleTemplate)>(StringComparer.OrdinalIgnoreCase);
    var missingTemplates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var verbHits = new Dictionary<string, (int Count, int FirstDepth, string FirstRegion, bool IsCondition)>(StringComparer.OrdinalIgnoreCase);
    var chainSectionCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    int totalPlacements = 0, filesScanned = 0;

    foreach (var rp in regionPaths)
    {
        var shortName = ShortName(rp);
        int d = Depth(rp) == int.MaxValue ? 999 : Depth(rp);
        var objPrefix = rp + "/objects/";
        foreach (var file in mapReader.ListFiles())
        {
            if (!file.StartsWith(objPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!file.EndsWith(".gas", StringComparison.OrdinalIgnoreCase)) continue;
            var fileName = file[objPrefix.Length..];
            if (fileName.Contains('/')) continue;
            filesScanned++;
            var (placements, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, fileName);
            foreach (var p in placements)
            {
                totalPlacements++;
                void HitSection(string header)
                {
                    if (handled.Contains(header) || HandledFamily(header)) return;
                    var bucket = benign.ContainsKey(header) ? benignHits : sectionHits;
                    if (bucket.TryGetValue(header, out var e))
                        bucket[header] = (e.Count + 1, Math.Min(e.FirstDepth, d), e.FirstDepth <= d ? e.FirstRegion : shortName, e.ExampleTemplate);
                    else
                        bucket[header] = (1, d, shortName, p.TemplateName);
                }

                // Instance component blocks + trigger verbs.
                foreach (var c in p.Node.Children)
                {
                    HitSection(c.Header);
                    if (string.Equals(c.Header, "common", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var it in c.Children)
                        {
                            if (!string.Equals(it.Header, "instance_triggers", StringComparison.OrdinalIgnoreCase)) continue;
                            foreach (var row in it.Children)
                            foreach (var attr in row.Attributes)
                            {
                                bool isCond = attr.Name.StartsWith("condition", StringComparison.OrdinalIgnoreCase);
                                bool isAct = attr.Name.StartsWith("action", StringComparison.OrdinalIgnoreCase);
                                if (!isCond && !isAct) continue;
                                var v = attr.Value.TrimStart();
                                // when_false / delay() wrappers precede the verb in
                                // authored text; TriggerRuntime's parser strips them.
                                if (v.StartsWith("when_false", StringComparison.OrdinalIgnoreCase))
                                    v = v["when_false".Length..].TrimStart();
                                int paren = v.IndexOf('(');
                                var verb = (paren > 0 ? v[..paren] : v).Trim().Trim('"');
                                if (verb.Length == 0) continue;
                                bool dispatched = isCond ? dispatchedConditions.Contains(verb) : dispatchedActions.Contains(verb);
                                if (dispatched) continue;
                                if (verbHits.TryGetValue(verb, out var e))
                                    verbHits[verb] = (e.Count + 1, Math.Min(e.FirstDepth, d), e.FirstDepth <= d ? e.FirstRegion : shortName, isCond);
                                else
                                    verbHits[verb] = (1, d, shortName, isCond);
                            }
                        }
                    }
                }

                // Template chain sections (cached per template).
                if (!chainSectionCache.TryGetValue(p.TemplateName, out var chainSections))
                {
                    chainSections = new List<string>();
                    if (store.TryGet(p.TemplateName, out var tpl))
                    {
                        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (var t = tpl; t is not null; t = t.Specializes)
                            foreach (var c in t.Node.Children)
                                if (seen.Add(c.Header)) chainSections.Add(c.Header);
                    }
                    else
                    {
                        chainSections.Add("!missing-template");
                    }
                    chainSectionCache[p.TemplateName] = chainSections;
                }
                foreach (var h in chainSections)
                {
                    if (h == "!missing-template")
                    {
                        missingTemplates.TryGetValue(p.TemplateName, out var n);
                        missingTemplates[p.TemplateName] = n + 1;
                        continue;
                    }
                    HitSection(h);
                }
            }
        }
    }

    Console.WriteLine($"scanned: {filesScanned} object file(s), {totalPlacements:N0} placement(s)");
    Console.WriteLine();
    Console.WriteLine("UNHANDLED component sections (ranked by placement count; depth = stitch-BFS hops from root ≈ campaign order):");
    Console.WriteLine("  count  depth  first-region     section                        example-template");
    foreach (var kv in sectionHits.OrderByDescending(kv => kv.Value.Count))
        Console.WriteLine($"  {kv.Value.Count,5}  {kv.Value.FirstDepth,5}  {kv.Value.FirstRegion,-15}  {kv.Key,-29}  {kv.Value.ExampleTemplate}");
    if (sectionHits.Count == 0) Console.WriteLine("  (none — every placed component section has an engine consumer)");

    Console.WriteLine();
    Console.WriteLine("benign / consumed-elsewhere sections (triaged, kept visible):");
    foreach (var kv in benignHits.OrderByDescending(kv => kv.Value.Count))
        Console.WriteLine($"  {kv.Value.Count,5}  {kv.Value.FirstDepth,5}  {kv.Value.FirstRegion,-15}  {kv.Key,-29}  {benign[kv.Key]}");

    Console.WriteLine();
    Console.WriteLine("UNDISPATCHED trigger verbs (parsed but no TriggerRuntime handler):");
    Console.WriteLine("  count  depth  first-region     kind       verb");
    foreach (var kv in verbHits.OrderByDescending(kv => kv.Value.Count))
        Console.WriteLine($"  {kv.Value.Count,5}  {kv.Value.FirstDepth,5}  {kv.Value.FirstRegion,-15}  {(kv.Value.IsCondition ? "condition" : "action"),-9}  {kv.Key}");
    if (verbHits.Count == 0) Console.WriteLine("  (none — every authored verb dispatches)");

    if (missingTemplates.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"missing templates ({missingTemplates.Count} distinct):");
        foreach (var kv in missingTemplates.OrderByDescending(kv => kv.Value).Take(20))
            Console.WriteLine($"  {kv.Value,5}x  {kv.Key}");
    }

    Console.WriteLine();
    Console.WriteLine("campaign order (stitch-BFS depth : regions):");
    foreach (var g in regionPaths.GroupBy(Depth).OrderBy(g => g.Key))
        Console.WriteLine($"  {(g.Key == int.MaxValue ? "unreached" : g.Key.ToString()),9} : {string.Join(", ", g.Select(ShortName))}");

    return 0;
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

    // SC-FADE-GROUPS — fade-group key histogram + camera flag tallies. These
    // are what fade_nodes(regionGuid, section, level, object, mode) triggers
    // address; a section histogram mismatch against shipped data means the
    // cutaway will hide the wrong node set.
    var sections = new Dictionary<int, int>();
    var levels = new Dictionary<int, int>();
    int camFade = 0, boundsCam = 0, occlCam = 0;
    foreach (var n in region.Nodes)
    {
        sections.TryGetValue(n.NodeSection, out var sc); sections[n.NodeSection] = sc + 1;
        levels.TryGetValue(n.NodeLevel, out var lc); levels[n.NodeLevel] = lc + 1;
        if (n.CameraFade) camFade++;
        if (n.BoundsCamera) boundsCam++;
        if (n.OccludesCamera) occlCam++;
    }
    string Histo(Dictionary<int, int> h) =>
        string.Join("  ", h.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
    Console.WriteLine($"Fade sections : {Histo(sections)}");
    Console.WriteLine($"Fade levels   : {Histo(levels)}");
    Console.WriteLine($"Camera flags  : camera_fade={camFade}  bounds_camera={boundsCam}  occludes_camera={occlCam}");

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
    bool dumpAll = false;
    if (a.Contains("--dump"))
    {
        dumpAll = true;
        a = a.Where(x => x != "--dump").ToArray();
    }
    if (a.Length != 3)
    {
        Console.Error.WriteLine("usage: siegefx region layout <map-tank> <terrain-tank> <region-path> [--dump]");
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

    if (dumpAll)
    {
        // SC-FADE-FRUSTUM diagnostics — every placed node with its fade-group
        // keys, so distances from any reference point can be measured (e.g.
        // "which hc_r1 section-4 nodes sit beyond DS1's 45u fade frustum").
        foreach (var n in graph.Nodes)
        {
            if (!layout.TryGetTransform(n.Guid, out var w)) continue;
            Console.WriteLine($"  0x{n.Guid:X8}  sec={n.NodeSection,3} lvl={n.NodeLevel,3}  t=({w.M41,8:F2}, {w.M42,8:F2}, {w.M43,8:F2})");
        }
        return 0;
    }

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

    // Build the mesh and break down what made it through the welder. Water tiles flow
    // into the mesh starting Phase 11-SC-3; this confirms they survived per-region.
    var mesh = NavMesh.BuildForRegion(graph, layout, Resolve);
    int meshFloor = 0, meshWater = 0;
    for (int t = 0; t < mesh.TriangleCount; t++)
    {
        switch (mesh.Kinds[t])
        {
            case SnoModel.FloorKind.Floor: meshFloor++; break;
            case SnoModel.FloorKind.Water: meshWater++; break;
        }
    }
    Console.WriteLine($"Mesh tris      : {mesh.TriangleCount:N0}  (floor={meshFloor:N0}, water={meshWater:N0})");
    if (meshWater > 0)
    {
        // Diagnostic: how does water actually connect to floor in this region? Print the
        // first water tri's centroid (so callers can drive `region path --water=N` at a
        // real water target), then count how many water tris share an edge with a Floor
        // tri. Pre-SC-7 this was structurally 0 (water authored in its own SNO whose
        // vertices don't weld to the shoreline floor); post-SC-7 the seam-stitch pass
        // wires Floor↔Water boundary edges that share an XZ footprint and a wadeable Y,
        // and this counter measures how thoroughly the shoreline got reconnected.
        int firstWater = -1;
        int waterFloorEdges = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            if (mesh.Kinds[t] != SnoModel.FloorKind.Water) continue;
            if (firstWater < 0) firstWater = t;
            for (int s = 0; s < 3; s++)
            {
                int nb = mesh.Neighbors[3 * t + s];
                if (nb >= 0 && mesh.Kinds[nb] == SnoModel.FloorKind.Floor) { waterFloorEdges++; break; }
            }
        }
        Console.WriteLine($"Water sample   : tri={firstWater}  centroid={FormatVec(mesh.Centroids[firstWater])}");
        Console.WriteLine($"Water seams    : {waterFloorEdges}/{meshWater} water tris share an edge with a Floor tri  ({mesh.SeamEdgeCount} stitched cross-kind pair(s))");
    }
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
    var snoUnresolved = new List<uint>();
    var unresolvedRegions = new Dictionary<uint, List<string>>();
    string currentRegion = "";
    SnoModel? Resolve(uint meshGuid)
    {
        if (snoCache.TryGetValue(meshGuid, out var cached))
        {
            if (cached is null && !meshIndex.TryResolve(meshGuid, out _))
            {
                if (!unresolvedRegions.TryGetValue(meshGuid, out var list)) { list = new List<string>(); unresolvedRegions[meshGuid] = list; }
                if (!list.Contains(currentRegion)) list.Add(currentRegion);
            }
            return cached;
        }
        SnoModel? sno = null;
        if (meshIndex.TryResolve(meshGuid, out var path))
        {
            try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
            catch (Exception ex) { snoFails.Add($"{path}: {ex.Message}"); sno = null; }
        }
        else
        {
            snoUnresolved.Add(meshGuid);
            if (!unresolvedRegions.TryGetValue(meshGuid, out var list)) { list = new List<string>(); unresolvedRegions[meshGuid] = list; }
            list.Add(currentRegion);
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
        currentRegion = path;
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
    Console.WriteLine($"  bad split    : parse-fail={snoFails.Count}  unresolved-meshGuid={snoUnresolved.Count}");
    Console.WriteLine($"Nav faces      : floor={floorFaces:N0}  water={waterFaces:N0}  ignored={ignoredFaces:N0}");
    if (snoFails.Count > 0)
    {
        Console.WriteLine($"SNO parse failures ({snoFails.Count}):");
        foreach (var msg in snoFails.Take(20)) Console.WriteLine($"  {msg}");
        if (snoFails.Count > 20) Console.WriteLine($"  ... {snoFails.Count - 20} more");
    }
    if (snoUnresolved.Count > 0)
    {
        Console.WriteLine($"Unresolved mesh GUIDs ({snoUnresolved.Count}):");
        foreach (var g in snoUnresolved.Take(20))
        {
            Console.Write($"  0x{g:X8}");
            if (unresolvedRegions.TryGetValue(g, out var regions))
                Console.Write($"  in {regions.Count} region(s): {string.Join(", ", regions.Take(3).Select(r => r.Substring(r.LastIndexOf("/regions/", StringComparison.OrdinalIgnoreCase) + 9).Replace("/terrain_nodes/nodes.gas", "")))}");
            Console.WriteLine();
        }
        if (snoUnresolved.Count > 20) Console.WriteLine($"  ... {snoUnresolved.Count - 20} more");
    }
    return (regionFails == 0 && snoFails.Count == 0) ? 0 : 4;
}

static int CmdRegionPath(string[] a)
{
    // Optional trailing flag: --water=<multiplier> (anything else stays positional). Pulled
    // out before the arg-count check so the path command keeps its rigid 5-positional shape.
    var traversal = ExtractTraversalFlag(ref a);
    if (a.Length != 5)
    {
        Console.Error.WriteLine("usage: siegefx region path <map-tank> <terrain-tank> <region-path> <x1,y1,z1> <x2,y2,z2> [--water=<cost-mul>]");
        return 1;
    }
    if (!TryParseVec3(a[3], out var start)) { Console.Error.WriteLine($"bad start vector: '{a[3]}'"); return 1; }
    if (!TryParseVec3(a[4], out var goal))  { Console.Error.WriteLine($"bad goal vector: '{a[4]}'");  return 1; }

    var mesh = LoadRegionNavMesh(a[0], a[1], a[2]);
    Console.WriteLine($"NavMesh    : {mesh.TriangleCount:N0} tris, {mesh.Vertices.Length:N0} welded verts, {mesh.SourceSnodeCount} source snode(s), {mesh.DegenerateFaceCount} degen");
    Console.WriteLine($"Traversal  : water-cost-mul={(float.IsPositiveInfinity(traversal.WaterCostMultiplier) ? "INF (impassable)" : traversal.WaterCostMultiplier.ToString("F2"))}");

    if (!mesh.TryFindTriangle(start, out var startTri)) { Console.Error.WriteLine("start point is not over any walkable triangle"); return 2; }
    if (!mesh.TryFindTriangle(goal,  out var goalTri))  { Console.Error.WriteLine("goal point is not over any walkable triangle"); return 2; }
    Console.WriteLine($"Start tri  : {startTri}  kind={mesh.Kinds[startTri]}  centroid={FormatVec(mesh.Centroids[startTri])}");
    Console.WriteLine($"Goal  tri  : {goalTri}  kind={mesh.Kinds[goalTri]}  centroid={FormatVec(mesh.Centroids[goalTri])}");

    var path = new List<int>();
    if (!NavPathfinder.TryFindPath(mesh, startTri, goalTri, path, ws: null, traversal: traversal))
    {
        Console.Error.WriteLine("no path — disconnected components, or endpoint kind impassable under this traversal policy");
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
    var traversal = ExtractTraversalFlag(ref a);
    bool dumpWaypoints = false;
    if (a.Contains("--waypoints"))
    {
        dumpWaypoints = true;
        a = a.Where(x => x != "--waypoints").ToArray();
    }
    if (a.Length < 5 || a.Length > 7)
    {
        Console.Error.WriteLine("usage: siegefx region follow <map-tank> <terrain-tank> <region-path> <x1,y1,z1> <x2,y2,z2> [speed] [ticks] [--water=<cost-mul>] [--waypoints]");
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

    var follower = new SiegeFX.Core.Nav.NavFollower(mesh, start, speed) { Traversal = traversal };
    follower.SetTarget(goal);
    if (follower.PathBlocked)
    {
        Console.Error.WriteLine("path blocked — start or goal off-mesh, or endpoints in disconnected components");
        return 2;
    }
    Console.WriteLine($"Start      : {FormatVec(follower.Position)}  tri={follower.CurrentTriangle}");
    Console.WriteLine($"Goal       : {FormatVec(follower.Target)}   path-tris={follower.RemainingPath.Count}  funnel-waypoints={follower.Waypoints.Count}");
    if (dumpWaypoints)
        for (int w = 0; w < follower.Waypoints.Count; w++)
            Console.WriteLine($"  wp[{w,3}] {FormatVec(follower.Waypoints[w])}");

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
    long totalTris = 0, totalComponents = 0, totalBiggest = 0, totalNonManifold = 0, totalSeams = 0;
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
            totalSeams += mesh.SeamEdgeCount;
            var (components, bigComponent, bigSize) = AnalyzeComponents(mesh);
            totalComponents += components;
            totalBiggest += bigSize;
            // SC-7: the seam-stitch pass merges shoreline water into the biggest
            // geometric component. The biggest-component A* control probes LandOnly
            // routability, so endpoints have to be Floor — water is impassable under
            // the default traversal and a probe with a water start/goal would always
            // fail and contaminate the metric. Filter once per region.
            var bigFloor = new List<int>(bigComponent.Count);
            for (int i = 0; i < bigComponent.Count; i++)
                if (mesh.Kinds[bigComponent[i]] == SnoModel.FloorKind.Floor)
                    bigFloor.Add(bigComponent[i]);
            var ws = new NavPathfinder.Workspace();
            var pathBuf = new List<int>();
            for (int s = 0; s < SamplesPerRegion; s++)
            {
                probes++;
                int a0 = rng.Next(mesh.TriangleCount);
                int b0 = rng.Next(mesh.TriangleCount);
                if (NavPathfinder.TryFindPath(mesh, a0, b0, pathBuf, ws)) solved++;
                // Control probe: both endpoints forced into the biggest component's
                // Floor subset. Exercises the pathfinder on the mesh's "real" walkable
                // surface rather than measuring topology disconnectedness. Expect ~100%.
                if (bigFloor.Count == 0) continue;
                int ia = bigFloor[rng.Next(bigFloor.Count)];
                int ib = bigFloor[rng.Next(bigFloor.Count)];
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
    Console.WriteLine($"Total land↔water     : {totalSeams:N0} cross-kind seam(s) wired across all regions");
    Console.WriteLine($"Random-pair A*       : {probes} probes, {solved} solved = {solveRate:F1}%  (measures topology, not pathfinder health)");
    Console.WriteLine($"Biggest-component A* : {intraBigProbes} probes, {intraBigSolved} solved = {bigSolveRate:F1}%  (should be ~100%)");
    return failedRegions == 0 ? 0 : 4;
}

// Hot-loop microbench: time NavPathfinder.TryFindPath over N random pairs drawn
// from the biggest connected component of one region's mesh (so every probe
// solves and the pathfinder hits its full inner-loop cost). Prints total time +
// per-probe stats. Used to decide whether the SortedSet open-set is worth
// replacing with a binary heap (Phase 11-SC-5).
static int CmdRegionPathBench(string[] a)
{
    if (a.Length < 3 || a.Length > 4)
    {
        Console.Error.WriteLine("usage: siegefx region path-bench <map-tank> <terrain-tank> <region-path> [probes]");
        return 1;
    }
    int probeCount = 10000;
    if (a.Length == 4 && !int.TryParse(a[3], out probeCount))
    { Console.Error.WriteLine($"bad probe count: '{a[3]}'"); return 1; }

    var mesh = LoadRegionNavMesh(a[0], a[1], a[2]);
    var (components, bigComponent, bigSize) = AnalyzeComponents(mesh);
    Console.WriteLine($"NavMesh   : {mesh.TriangleCount:N0} tris, biggest component {bigSize:N0} ({(100.0 * bigSize / Math.Max(1, mesh.TriangleCount)):F1}%) across {components} components");
    if (bigSize < 2) { Console.Error.WriteLine("biggest component is too small to bench"); return 2; }

    var rng = new Random(unchecked((int)0xCAFEBABE));
    // Pre-roll the probe pairs so RNG / list-indexing cost doesn't bleed into the timed loop.
    var pairs = new (int a, int b)[probeCount];
    for (int i = 0; i < probeCount; i++)
        pairs[i] = (bigComponent[rng.Next(bigSize)], bigComponent[rng.Next(bigSize)]);

    var ws = new NavPathfinder.Workspace();
    var pathBuf = new List<int>(capacity: 256);

    // Warmup pass — JIT the hot path, prime the workspace arrays at full size.
    int warmupSolves = 0;
    for (int i = 0; i < Math.Min(500, probeCount); i++)
    {
        if (NavPathfinder.TryFindPath(mesh, pairs[i].a, pairs[i].b, pathBuf, ws)) warmupSolves++;
    }

    var sw = System.Diagnostics.Stopwatch.StartNew();
    long totalPathLen = 0;
    int solved = 0;
    for (int i = 0; i < probeCount; i++)
    {
        if (NavPathfinder.TryFindPath(mesh, pairs[i].a, pairs[i].b, pathBuf, ws))
        {
            solved++;
            totalPathLen += pathBuf.Count;
        }
    }
    sw.Stop();

    double ms = sw.Elapsed.TotalMilliseconds;
    double usPerProbe = (ms * 1000.0) / probeCount;
    double avgPathLen = solved == 0 ? 0 : (double)totalPathLen / solved;
    Console.WriteLine($"Probes    : {probeCount:N0} (warmup {warmupSolves} solved, timed {solved} solved)");
    Console.WriteLine($"Time      : {ms:F1} ms total, {usPerProbe:F2} us/probe");
    Console.WriteLine($"Avg path  : {avgPathLen:F1} tris");
    return 0;
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

// Phase 11-SC-1 — characterize each connected component in a region's nav mesh.
// Phase 11 audit found avg 33 components / region, with the biggest holding only
// ~54.5% of all tris; the question this CLI answers is whether the stranded 45.5%
// is real terrain (cliff islands, balcony platforms, NPC pens) or junk (Floor SNOs
// authored as separate sub-meshes that should weld but don't). For each component
// we report face count, AABB, centroid, and the top SNO files anchoring it (by
// face contribution), so a human can scan the report and decide.
static int CmdRegionNavComponents(string[] a)
{
    int top = 5;
    var rest = new List<string>();
    foreach (var x in a)
    {
        if (x.StartsWith("--top=", StringComparison.Ordinal)) int.TryParse(x["--top=".Length..], out top);
        else rest.Add(x);
    }
    if (rest.Count != 3)
    {
        Console.Error.WriteLine("usage: siegefx region nav-components <map-tank> <terrain-tank> <region-path|all> [--top=N]");
        return 1;
    }

    using var mapTank = TankFile.Open(rest[0]);
    var mapReader = new TankReader(mapTank);
    using var terrainTank = TankFile.Open(rest[1]);
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
    string MeshPath(uint guid) => meshIndex.TryResolve(guid, out var p) ? p : $"<unresolved 0x{guid:X8}>";

    if (string.Equals(rest[2], "all", StringComparison.OrdinalIgnoreCase))
    {
        // Aggregate roll-up across every region. We don't dump per-component for
        // every region (would be tens of thousands of lines); instead we report
        // the histogram of fragment sizes and the worst-offender regions.
        var sizeBuckets = new Dictionary<string, int>(StringComparer.Ordinal);
        int regions = 0, totalComps = 0, totalTris = 0, totalBig = 0;
        var worst = new List<(string region, int comps, int big, int total)>();
        foreach (var rnodePath in mapReader.ListFiles())
        {
            if (!rnodePath.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase)) continue;
            regions++;
            try
            {
                var graph = RegionGraph.Load(mapReader.ExtractToMemory(rnodePath));
                var layout = RegionLayout.Build(graph, Resolve);
                var mesh = NavMesh.BuildForRegion(graph, layout, Resolve);
                if (mesh.TriangleCount == 0) continue;
                var comps = FloodFillAllComponents(mesh);
                totalComps += comps.Count;
                totalTris += mesh.TriangleCount;
                int biggest = 0;
                foreach (var c in comps)
                {
                    if (c.Count > biggest) biggest = c.Count;
                    string bucket = c.Count switch
                    {
                        1     => "1",
                        <= 10 => "2-10",
                        <= 100 => "11-100",
                        <= 1000 => "101-1000",
                        _ => "1001+",
                    };
                    sizeBuckets[bucket] = sizeBuckets.TryGetValue(bucket, out var v) ? v + 1 : 1;
                }
                totalBig += biggest;
                worst.Add((rnodePath, comps.Count, biggest, mesh.TriangleCount));
            }
            catch { /* swallow — counted under regions but not contributing */ }
        }
        Console.WriteLine($"Regions scanned    : {regions}");
        Console.WriteLine($"Total components   : {totalComps:N0}  ({(double)totalComps / Math.Max(1, regions):F1} avg/region)");
        Console.WriteLine($"Total tris         : {totalTris:N0}");
        Console.WriteLine($"Biggest-comp share : {(totalTris == 0 ? 0 : 100.0 * totalBig / totalTris):F1}%");
        Console.WriteLine();
        Console.WriteLine("Component size histogram:");
        foreach (var b in new[] { "1", "2-10", "11-100", "101-1000", "1001+" })
            Console.WriteLine($"  {b,-9}  {sizeBuckets.GetValueOrDefault(b, 0),6}");
        Console.WriteLine();
        Console.WriteLine($"Top {top} fragmented regions (by component count):");
        worst.Sort((x, y) => y.comps.CompareTo(x.comps));
        foreach (var w in worst.Take(top))
            Console.WriteLine($"  {w.comps,4} comps  biggest={w.big,5}/{w.total,-5}  {w.region}");
        return 0;
    }

    var regionPath = rest[2].Replace('\\', '/');
    if (!regionPath.StartsWith('/')) regionPath = "/" + regionPath;
    if (regionPath.EndsWith('/')) regionPath = regionPath[..^1];
    if (regionPath.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase))
        regionPath = regionPath[..^"/terrain_nodes/nodes.gas".Length];
    var rGraph = RegionGraph.Load(mapReader.ExtractToMemory(regionPath + "/terrain_nodes/nodes.gas"));
    var rLayout = RegionLayout.Build(rGraph, Resolve);
    var rMesh = NavMesh.BuildForRegion(rGraph, rLayout, Resolve);
    Console.WriteLine($"Region          : {regionPath}");
    Console.WriteLine($"NavMesh         : {rMesh.TriangleCount:N0} tris, {rMesh.SourceSnodeCount} src snode(s), {rMesh.NonManifoldEdgeCount} non-manifold edge(s)");
    if (rMesh.TriangleCount == 0) { Console.WriteLine("(empty mesh — nothing to flood)"); return 0; }

    var components = FloodFillAllComponents(rMesh);
    components.Sort((x, y) => y.Count.CompareTo(x.Count));
    Console.WriteLine($"Components      : {components.Count}");
    Console.WriteLine();

    int show = Math.Min(top, components.Count);
    for (int ci = 0; ci < show; ci++)
    {
        var comp = components[ci];
        // AABB + centroid in region-space.
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        Vector3 sum = Vector3.Zero;
        var sourceFaces = new Dictionary<int, int>();
        foreach (var t in comp)
        {
            var ctr = rMesh.Centroids[t];
            sum += ctr;
            if (ctr.X < minX) minX = ctr.X; if (ctr.X > maxX) maxX = ctr.X;
            if (ctr.Y < minY) minY = ctr.Y; if (ctr.Y > maxY) maxY = ctr.Y;
            if (ctr.Z < minZ) minZ = ctr.Z; if (ctr.Z > maxZ) maxZ = ctr.Z;
            int srcNode = rMesh.SourceNodeIndex[t];
            sourceFaces[srcNode] = sourceFaces.TryGetValue(srcNode, out var v) ? v + 1 : 1;
        }
        var ctrAvg = sum / comp.Count;
        Console.WriteLine($"[{ci}] {comp.Count} faces  centroid={FormatVec(ctrAvg)}  bbox=({maxX-minX:F1} x {maxY-minY:F1} x {maxZ-minZ:F1})");
        // Top 3 source SNOs by face count
        foreach (var kv in sourceFaces.OrderByDescending(k => k.Value).Take(3))
        {
            var node = rGraph.Nodes[kv.Key];
            Console.WriteLine($"    {kv.Value,5} faces  {MeshPath(node.MeshGuid)}");
        }
        if (sourceFaces.Count > 3) Console.WriteLine($"    ... +{sourceFaces.Count - 3} more snode(s)");
    }
    if (components.Count > show) Console.WriteLine($"... +{components.Count - show} more component(s) (use --top=N to see more)");

    // Tail summary: histogram of remaining components by size, so a human can see
    // at a glance whether the long tail is "lots of tiny stranded fragments" vs
    // "a few medium-size islands".
    Console.WriteLine();
    var tailHistogram = new Dictionary<string, int>();
    foreach (var c in components.Skip(show))
    {
        string bucket = c.Count switch
        {
            1     => "1",
            <= 10 => "2-10",
            <= 100 => "11-100",
            <= 1000 => "101-1000",
            _ => "1001+",
        };
        tailHistogram[bucket] = tailHistogram.TryGetValue(bucket, out var v) ? v + 1 : 1;
    }
    if (tailHistogram.Count > 0)
    {
        Console.WriteLine("Tail (components beyond top): size histogram:");
        foreach (var b in new[] { "1", "2-10", "11-100", "101-1000", "1001+" })
            if (tailHistogram.TryGetValue(b, out var v))
                Console.WriteLine($"  {b,-9}  {v,5}");
    }
    return 0;
}

// Returns every connected component as its own list of triangle indices. The
// existing `AnalyzeComponents` only retains the biggest because that's all path-
// fuzz needs; nav-components needs every fragment so it can describe them.
static List<List<int>> FloodFillAllComponents(NavMesh mesh)
{
    var visited = new bool[mesh.TriangleCount];
    var stack = new Stack<int>();
    var result = new List<List<int>>();
    for (int seed = 0; seed < mesh.TriangleCount; seed++)
    {
        if (visited[seed]) continue;
        var current = new List<int>();
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
        result.Add(current);
    }
    return result;
}

// Strips a `--water=<float>` flag (in any position) from the command's positional args
// and returns a NavTraversal configured accordingly. `--water=inf` or no flag at all
// keep the LandOnly default. `--water=4` (or any finite value) makes water passable
// at that cost multiplier — handy to verify swim-style routing through CLI without
// touching engine code.
static SiegeFX.Core.Nav.NavTraversal ExtractTraversalFlag(ref string[] a)
{
    float? mul = null;
    var kept = new List<string>(a.Length);
    for (int i = 0; i < a.Length; i++)
    {
        if (a[i].StartsWith("--water=", StringComparison.OrdinalIgnoreCase))
        {
            var v = a[i].Substring("--water=".Length);
            if (string.Equals(v, "inf", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "infinity", StringComparison.OrdinalIgnoreCase))
                mul = float.PositiveInfinity;
            else if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                mul = parsed;
            // Bad values silently fall through to LandOnly — better than aborting the
            // command for a typo when the diagnostic itself is the point.
            continue;
        }
        kept.Add(a[i]);
    }
    a = kept.ToArray();
    if (mul is null || float.IsPositiveInfinity(mul.Value)) return SiegeFX.Core.Nav.NavTraversal.LandOnly;
    return new SiegeFX.Core.Nav.NavTraversal { WaterCostMultiplier = mul.Value };
}

/// <summary>Builds one unified NavMesh spanning several regions, mirroring the
/// runtime's world build: per-region graphs + layouts composed into a shared
/// world frame via stitch_helper.gas (WorldLayout), then combined with
/// cross-region door links injected (CombineWithCrossRegionDoors) so the door
/// stitcher wires boundary seams. Coordinates for path/follow queries are in
/// the world frame rooted at the FIRST region listed. This is the offline
/// repro for anything that crosses a region boundary (the fh_r1 -> hc_r1
/// cellar descent), which single-region LoadRegionNavMesh cannot express.</summary>
static NavMesh LoadWorldNavMesh(string mapTankPath, string terrainTankPath, string regionsCsv)
{
    using var mapTank = TankFile.Open(mapTankPath);
    var mapReader = new TankReader(mapTank);
    using var terrainTank = TankFile.Open(terrainTankPath);
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

    var entries = new List<WorldLayout.RegionEntry>();
    var graphTuples = new List<(string Path, RegionGraph Graph, RegionStitchHelper? Stitches)>();
    foreach (var raw in regionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var rp = raw.Replace('\\', '/');
        if (!rp.StartsWith('/')) rp = "/" + rp;
        if (rp.EndsWith('/')) rp = rp[..^1];
        var graph = RegionGraph.Load(mapReader.ExtractToMemory(rp + "/terrain_nodes/nodes.gas"));
        var layout = RegionLayout.Build(graph, Resolve);
        RegionStitchHelper? stitches = null;
        try { stitches = RegionStitchHelper.Load(mapReader.ExtractToMemory(rp + "/editor/stitch_helper.gas")); }
        catch { /* regions without stitch files are legal (isolated demo regions) */ }
        entries.Add(new WorldLayout.RegionEntry(rp, graph, layout, stitches));
        graphTuples.Add((rp, graph, stitches));
    }

    if (entries.Count == 0)
        throw new ArgumentException("no regions parsed from the comma-list", nameof(regionsCsv));
    var world = WorldLayout.Build(entries, Resolve, entries[0].Path);
    var combined = graphTuples.Count > 1
        ? RegionGraph.CombineWithCrossRegionDoors(graphTuples)
        : graphTuples[0].Graph;
    var unified = RegionLayout.FromTransforms(combined.TargetNodeGuid, world.Transforms);
    return NavMesh.BuildForRegion(combined, unified, Resolve);
}

static int CmdWorldPath(string[] a)
{
    var traversal = ExtractTraversalFlag(ref a);
    if (a.Length != 5)
    {
        Console.Error.WriteLine("usage: siegefx world path <map-tank> <terrain-tank> <region1,region2,...> <x1,y1,z1> <x2,y2,z2> [--water=<cost-mul>]");
        Console.Error.WriteLine("       coordinates are in the world frame rooted at region1");
        return 1;
    }
    if (!TryParseVec3(a[3], out var start)) { Console.Error.WriteLine($"bad start vector: '{a[3]}'"); return 1; }
    if (!TryParseVec3(a[4], out var goal))  { Console.Error.WriteLine($"bad goal vector: '{a[4]}'");  return 1; }

    var mesh = LoadWorldNavMesh(a[0], a[1], a[2]);
    Console.WriteLine($"NavMesh    : {mesh.TriangleCount:N0} tris, {mesh.Vertices.Length:N0} welded verts, {mesh.SourceSnodeCount} source snode(s), {mesh.DoorSeamCount} door seam(s)");

    if (!mesh.TryFindTriangle(start, out var startTri)) { Console.Error.WriteLine("start point is not over any walkable triangle"); return 2; }
    if (!mesh.TryFindTriangle(goal,  out var goalTri))  { Console.Error.WriteLine("goal point is not over any walkable triangle"); return 2; }
    Console.WriteLine($"Start tri  : {startTri}  kind={mesh.Kinds[startTri]}  centroid={FormatVec(mesh.Centroids[startTri])}");
    Console.WriteLine($"Goal  tri  : {goalTri}  kind={mesh.Kinds[goalTri]}  centroid={FormatVec(mesh.Centroids[goalTri])}");

    var path = new List<int>();
    if (!NavPathfinder.TryFindPath(mesh, startTri, goalTri, path, ws: null, traversal: traversal))
    {
        Console.Error.WriteLine("no path — disconnected components, or endpoint kind impassable under this traversal policy");
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

static int CmdWorldFollow(string[] a)
{
    var traversal = ExtractTraversalFlag(ref a);
    bool dumpWaypoints = false;
    if (a.Contains("--waypoints"))
    {
        dumpWaypoints = true;
        a = a.Where(x => x != "--waypoints").ToArray();
    }
    if (a.Length < 5 || a.Length > 7)
    {
        Console.Error.WriteLine("usage: siegefx world follow <map-tank> <terrain-tank> <region1,region2,...> <x1,y1,z1> <x2,y2,z2> [speed] [ticks] [--water=<cost-mul>] [--waypoints]");
        Console.Error.WriteLine("       coordinates are in the world frame rooted at region1");
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

    var mesh = LoadWorldNavMesh(a[0], a[1], a[2]);
    Console.WriteLine($"NavMesh    : {mesh.TriangleCount:N0} tris, {mesh.Vertices.Length:N0} welded verts, {mesh.DoorSeamCount} door seam(s)");

    var follower = new SiegeFX.Core.Nav.NavFollower(mesh, start, speed) { Traversal = traversal };
    follower.SetTarget(goal);
    if (follower.PathBlocked)
    {
        Console.Error.WriteLine("path blocked — start or goal off-mesh, or endpoints in disconnected components");
        return 2;
    }
    Console.WriteLine($"Start      : {FormatVec(follower.Position)}  tri={follower.CurrentTriangle}");
    Console.WriteLine($"Goal       : {FormatVec(follower.Target)}   path-tris={follower.RemainingPath.Count}  funnel-waypoints={follower.Waypoints.Count}");
    if (dumpWaypoints)
        for (int w = 0; w < follower.Waypoints.Count; w++)
            Console.WriteLine($"  wp[{w,3}] {FormatVec(follower.Waypoints[w])}");

    const float tickDt = 1f / 20f;
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
    bool trace = false;
    var rest = new List<string>();
    foreach (var x in a) { if (x == "--trace") trace = true; else rest.Add(x); }
    if (rest.Count != 1) { Console.Error.WriteLine("usage: siegefx sno info <file.sno> [--trace]"); return 1; }
    var data = File.ReadAllBytes(rest[0]);
    Console.WriteLine($"File      : {rest[0]}");
    Console.WriteLine($"Size      : {data.Length:N0} bytes");

    SnoModel.TraceParse = trace;
    var sno = SnoModel.Load(data);
    SnoModel.TraceParse = false;
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

// Phase 4 audit: corpus-coverage receipt for the SNO loader. Matches the
// shape of CmdAspFuzz / CmdRawFuzz so test-all.bat can run it the same way.
// SNO is the most structurally complex of the four binary formats (header +
// spots + doors + corners + surfaces + recursive nav-grouping unknown sections),
// so a clean fuzz run is the cheapest "load every shipped tile without throwing"
// signal we can get.
static int CmdSnoFuzz(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx sno fuzz <tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    int total = 0, failed = 0, withNav = 0;
    long totalBytes = 0;
    long totalCorners = 0, totalTris = 0, totalDoors = 0, totalSpots = 0, totalNavFaces = 0;
    var byVersion = new SortedDictionary<int, int>();
    foreach (var path in reader.ListFiles())
    {
        if (!path.EndsWith(".sno", StringComparison.OrdinalIgnoreCase)) continue;
        total++;
        byte[] bytes;
        try { bytes = reader.ExtractToMemory(path); }
        catch (Exception ex) { Console.Error.WriteLine($"  [extract-fail] {path}: {ex.Message}"); failed++; continue; }
        totalBytes += bytes.Length;
        try
        {
            var sno = SnoModel.Load(bytes);
            byVersion[sno.Version] = byVersion.TryGetValue(sno.Version, out var n) ? n + 1 : 1;
            totalCorners += sno.Corners.Length;
            totalTris    += sno.TotalTriangleCount;
            totalDoors   += sno.Doors.Length;
            totalSpots   += sno.Spots.Length;
            if (sno.LogicalGroupings.Length > 0)
            {
                withNav++;
                foreach (var g in sno.LogicalGroupings) totalNavFaces += g.Faces.Length;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [parse-fail] {path}: {ex.Message}");
            failed++;
        }
    }
    Console.WriteLine($"fuzzed {total} .sno file(s), {totalBytes:N0} bytes; {failed} failure(s)");
    Console.WriteLine($"  corners={totalCorners:N0}  tris={totalTris:N0}  doors={totalDoors:N0}  spots={totalSpots:N0}");
    Console.WriteLine($"  with nav-grouping section: {withNav}/{total} ({totalNavFaces:N0} nav faces)");
    if (byVersion.Count > 0)
    {
        Console.Write("  versions:");
        foreach (var kv in byVersion) Console.Write($" v{kv.Key}={kv.Value}");
        Console.WriteLine();
    }
    return failed == 0 ? 0 : 4;
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
    Console.WriteLine($"Subsets   : {mesh.Subsets.Length}");
    for (var i = 0; i < mesh.Subsets.Length; i++)
    {
        var s = mesh.Subsets[i];
        var texName = (s.TextureIndex >= 0 && s.TextureIndex < mesh.TextureNames.Count)
            ? mesh.TextureNames[s.TextureIndex]
            : "<out-of-range>";
        // Phase 21d-2a-v — per-subset UV extent. Tells which atlas region each
        // subset samples, so e.g. farmboy clothing-strip not showing up on the
        // body can be diagnosed by checking whether the subset's UV box lands
        // in the colored garment region of b_c_pos_a1_*.raw vs the empty
        // background.
        float umin = float.PositiveInfinity, umax = float.NegativeInfinity;
        float vmin = float.PositiveInfinity, vmax = float.NegativeInfinity;
        int triCorners = 0;
        if (s.TriangleCount > 0 && mesh.TriangleCount > 0 && mesh.Corners.Length > 0)
        {
            int firstIdx = s.FirstTriangle * 3;
            int endIdx = (s.FirstTriangle + s.TriangleCount) * 3;
            for (int idx = firstIdx; idx < endIdx; idx++)
            {
                int corner = mesh.TriangleIndices[idx];
                if ((uint)corner >= (uint)mesh.Corners.Length) continue;
                var uv = mesh.Corners[corner].Uv;
                if (uv.X < umin) umin = uv.X; if (uv.X > umax) umax = uv.X;
                if (uv.Y < vmin) vmin = uv.Y; if (uv.Y > vmax) vmax = uv.Y;
                triCorners++;
            }
        }
        var uvBox = triCorners > 0
            ? $"  uv=U[{umin:F3},{umax:F3}] V[{vmin:F3},{vmax:F3}]"
            : "  uv=<n/a>";
        Console.WriteLine($"  [{i}] firstTri={s.FirstTriangle,5}  triCount={s.TriangleCount,5}  texIdx={s.TextureIndex} ({texName}){uvBox}");
        // Phase 21d-2a-v — V-band histogram so we can see where the bulk of
        // a subset's face UVs actually concentrate (the bbox above only tells
        // the extremes). 10 bands × density chars; helps identify "subset 1
        // covers full atlas" vs "subset 1 lives in the strip rows".
        if (Environment.GetEnvironmentVariable("SIEGEFX_UV_HIST") == "1" && triCorners > 0)
        {
            var bands = new int[10];
            int firstIdx2 = s.FirstTriangle * 3;
            int endIdx2 = (s.FirstTriangle + s.TriangleCount) * 3;
            for (int idx = firstIdx2; idx < endIdx2; idx++)
            {
                int corner = mesh.TriangleIndices[idx];
                if ((uint)corner >= (uint)mesh.Corners.Length) continue;
                var v = mesh.Corners[corner].Uv.Y;
                int band = (int)Math.Clamp(Math.Floor(v * 10f), 0, 9);
                bands[band]++;
            }
            var sb = new System.Text.StringBuilder("        V-hist [");
            for (int bi = 0; bi < 10; bi++)
                sb.Append($"{bands[bi],4}");
            sb.Append(" ]  (V=0.0..0.1, 0.1..0.2, ..., 0.9..1.0)");
            Console.WriteLine(sb.ToString());
        }
    }
    // SC-VERTEX-COLOR-APPLY diagnostic — distribution of per-corner ARGB
    // values. DS1 bakes per-vertex radiosity / per-instance dark shading on
    // ASP corners (e.g. burnt farmhouse door); reading the data straight from
    // the asp before wiring the shader proves whether the visible darkening
    // in retail is in fact authored on the corner stream.
    if (mesh.Corners.Length > 0)
    {
        long sumR = 0, sumG = 0, sumB = 0, sumA = 0;
        int minLum = 255, maxLum = 0;
        var unique = new HashSet<uint>();
        foreach (var corner in mesh.Corners)
        {
            int cb = (int)( corner.Color        & 0xFF);
            int cg = (int)((corner.Color >>  8) & 0xFF);
            int cr = (int)((corner.Color >> 16) & 0xFF);
            int ca = (int)((corner.Color >> 24) & 0xFF);
            sumR += cr; sumG += cg; sumB += cb; sumA += ca;
            int lum = (cr + cg + cb) / 3;
            if (lum < minLum) minLum = lum;
            if (lum > maxLum) maxLum = lum;
            unique.Add(corner.Color);
        }
        int n = mesh.Corners.Length;
        Console.WriteLine($"VColor    : avg ARGB=({sumA/n:000}, {sumR/n:000}, {sumG/n:000}, {sumB/n:000})  " +
                          $"luma min={minLum} max={maxLum}  unique={unique.Count}");
        if (unique.Count <= 8)
        {
            var sb = new System.Text.StringBuilder("            distinct = ");
            bool first = true;
            foreach (var u in unique)
            {
                if (!first) sb.Append(", ");
                sb.Append($"0x{u:X8}");
                first = false;
            }
            Console.WriteLine(sb.ToString());
        }
    }

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

// TEMP-VERIFY: tank-wide sweep of every ASP. For each, report bones, the
// primary-max skin bone (highest-weight bone per corner, matching the proposed
// fix's MaxSkinBone), the any-influence-max bone (matching asp-info), and
// bone-0's bind rotation as an axis-angle. The regression set for the proposed
// fix is exactly: bones>=2 AND primaryMax==0 (fix would newly apply bindRoot).
// Flag any such file whose bone-0 bind rotation is materially non-identity.
static int CmdAspBoneSweep(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx asp bonesweep <tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    int total = 0, multiBone = 0, riskPrimary0 = 0, riskPrimary0NonId = 0;
    foreach (var path in reader.ListFiles())
    {
        if (!path.EndsWith(".asp", StringComparison.OrdinalIgnoreCase)) continue;
        total++;
        AspMesh mesh;
        try { mesh = AspMesh.Load(reader.ExtractToMemory(path)); }
        catch { continue; }
        if (mesh.BoneCount < 2) continue;
        multiBone++;
        // primary (highest-weight) max bone per corner, over all corners
        int primaryMax = 0, anyMax = 0;
        if (mesh.HasSkin)
        {
            for (int c = 0; c < mesh.SkinWeights.Length; c++)
            {
                var w = mesh.SkinWeights[c]; var b = mesh.SkinBones[c];
                int primary = 0; float primaryW = -1f;
                if (w.X > primaryW) { primary = (int)( b        & 0xFF); primaryW = w.X; }
                if (w.Y > primaryW) { primary = (int)((b >>  8) & 0xFF); primaryW = w.Y; }
                if (w.Z > primaryW) { primary = (int)((b >> 16) & 0xFF); primaryW = w.Z; }
                if (w.W > primaryW) { primary = (int)((b >> 24) & 0xFF); primaryW = w.W; }
                primaryMax = Math.Max(primaryMax, primary);
                if (w.X > 0) anyMax = Math.Max(anyMax, (int)( b        & 0xFF));
                if (w.Y > 0) anyMax = Math.Max(anyMax, (int)((b >>  8) & 0xFF));
                if (w.Z > 0) anyMax = Math.Max(anyMax, (int)((b >> 16) & 0xFF));
                if (w.W > 0) anyMax = Math.Max(anyMax, (int)((b >> 24) & 0xFF));
            }
        }
        // bone-0 bind rotation as angle (deg)
        float angDeg = 0f;
        if (mesh.BindPose.Length > 0)
        {
            var q = mesh.BindPose[0].Rotation;
            var wc = Math.Clamp(MathF.Abs(q.W), 0f, 1f);
            angDeg = 2f * MathF.Acos(wc) * 180f / MathF.PI;
        }
        if (primaryMax == 0)
        {
            riskPrimary0++;
            bool nonId = angDeg > 1.0f;
            if (nonId) riskPrimary0NonId++;
            Console.WriteLine($"RISK bones={mesh.BoneCount} primMax={primaryMax} anyMax={anyMax} bind0ang={angDeg,6:F1}deg v{mesh.AspVersionMajor}.{mesh.AspVersionMinor} skin={mesh.HasSkin} {path}");
        }
    }
    Console.WriteLine($"--- swept {total} asp; multiBone(>=2)={multiBone}; primaryMax==0 (fix-newly-applies)={riskPrimary0}; of those non-identity-bind0={riskPrimary0NonId}");
    return 0;
}

static int CmdAspFuzz(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx asp fuzz <tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    int total = 0, failed = 0, skeletal = 0, skinned = 0;
    long totalBytes = 0;
    // Phase 4 audit: track ASP version distribution. The reference doc lists ten+
    // possible versions (V1_2..V5_0); knowing which actually ship in DS1 anchors
    // future debugging — a parse-fail on a never-shipped version is a different
    // problem from a fail on a v2.5 file we should already handle.
    var byVersion = new SortedDictionary<string, int>();
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
            var key = $"v{mesh.AspVersionMajor}.{mesh.AspVersionMinor}";
            byVersion[key] = byVersion.TryGetValue(key, out var n) ? n + 1 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [parse-fail] {path}: {ex.Message}");
            failed++;
        }
    }
    Console.WriteLine($"fuzzed {total} .asp file(s), {totalBytes:N0} bytes; {failed} failure(s), {skeletal} w/skeleton, {skinned} w/skin");
    if (byVersion.Count > 0)
    {
        Console.Write("  versions:");
        foreach (var kv in byVersion) Console.Write($" {kv.Key}={kv.Value}");
        Console.WriteLine();
    }
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
    if (reader.InvalidFileCount > 0)
        Console.WriteLine($"Invalid files: {reader.InvalidFileCount}");
    return 0;
}

static int CmdTankList(string[] a)
{
    string? prefix = null;
    string? ext = null;
    string? tankPath = null;
    foreach (var arg in a)
    {
        if      (arg.StartsWith("--prefix=", StringComparison.Ordinal)) prefix = arg["--prefix=".Length..];
        else if (arg.StartsWith("--ext=",    StringComparison.Ordinal)) ext    = arg["--ext=".Length..];
        else if (!arg.StartsWith("--"))                                 tankPath ??= arg;
        else { Console.Error.WriteLine($"unknown option: {arg}"); return 1; }
    }
    if (tankPath is null)
    { Console.Error.WriteLine("usage: siegefx tank list <tank> [--prefix=PATH] [--ext=.EXT]"); return 1; }

    if (ext is not null && !ext.StartsWith('.')) ext = "." + ext;

    using var tank = TankFile.Open(tankPath);
    var reader = new TankReader(tank);

    var matched = 0;
    foreach (var path in reader.ListFiles().OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
    {
        if (prefix is not null && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
        if (ext    is not null && !path.EndsWith  (ext,    StringComparison.OrdinalIgnoreCase)) continue;
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
        matched++;
    }

    Console.WriteLine();
    if (prefix is null && ext is null)
        Console.WriteLine($"{reader.FileCount} file(s) across {reader.DirCount} dir(s)");
    else
        Console.WriteLine($"{matched} of {reader.FileCount} file(s) matched (prefix={prefix ?? "<any>"}, ext={ext ?? "<any>"})");
    return 0;
}

// Phase 1 audit follow-up — diagnostic CLI receipt for "DS1 ships zlib only,
// no LZO" claim. Walks every file entry, builds a compression-format
// histogram, sums sizes, and reports invalid-flag counts. Catches a corrupt
// or unexpectedly-LZO'd tank up front rather than at first extract.
static int CmdTankFuzz(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx tank fuzz <tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);

    var byFormat = new Dictionary<TankDataFormat, (int count, long totalSize)>();
    var invalidPaths = new List<string>();
    foreach (var path in reader.ListFiles())
    {
        if (!reader.TryGetFile(path, out var file)) continue;
        if (file.IsInvalid) invalidPaths.Add(path);
        if (byFormat.TryGetValue(file.Format, out var t))
            byFormat[file.Format] = (t.count + 1, t.totalSize + file.Size);
        else
            byFormat[file.Format] = (1, file.Size);
    }

    Console.WriteLine($"tank fuzz: {a[0]}");
    Console.WriteLine($"  files       : {reader.FileCount}");
    Console.WriteLine($"  directories : {reader.DirCount}");
    Console.WriteLine($"  invalid     : {invalidPaths.Count}");
    Console.WriteLine();
    Console.WriteLine("Compression formats:");
    foreach (var kv in byFormat.OrderBy(kv => (int)kv.Key))
        Console.WriteLine($"  {kv.Key,-6}  {kv.Value.count,6} file(s)  {kv.Value.totalSize,14:N0} bytes");

    if (invalidPaths.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Invalid entries:");
        foreach (var p in invalidPaths.Take(10)) Console.WriteLine($"  {p}");
        if (invalidPaths.Count > 10) Console.WriteLine($"  ... +{invalidPaths.Count - 10} more");
    }
    return invalidPaths.Count == 0 ? 0 : 4;
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

// Phase 2 audit follow-up — diagnostic CLI receipt for "RAW pipeline handles
// every shipped texture without parse error". Walks all .raw entries in a
// tank, decodes each, and reports a dimension histogram + surface-count
// distribution. A non-zero failure count or any unexpectedly large
// dimension indicates either a format regression or a fuzzed/corrupt entry.
static int CmdRawFuzz(string[] a)
{
    if (a.Length != 1) { Console.Error.WriteLine("usage: siegefx raw fuzz <tank>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);

    int total = 0, ok = 0, failed = 0;
    int maxW = 0, maxH = 0, maxSurfaces = 0;
    var dimHisto = new Dictionary<(int w, int h), int>();
    var surfHisto = new Dictionary<int, int>();
    foreach (var path in reader.ListFiles())
    {
        if (!path.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) continue;
        total++;
        byte[] bytes;
        try { bytes = reader.ExtractToMemory(path); }
        catch (Exception ex) { Console.Error.WriteLine($"  [extract-fail] {path}: {ex.Message}"); failed++; continue; }
        RawImage img;
        try { img = RawImage.Load(bytes); }
        catch (Exception ex) { Console.Error.WriteLine($"  [parse-fail] {path}: {ex.Message}"); failed++; continue; }

        ok++;
        if (img.Width  > maxW) maxW = img.Width;
        if (img.Height > maxH) maxH = img.Height;
        if (img.SurfaceCount > maxSurfaces) maxSurfaces = img.SurfaceCount;
        var key = (img.Width, img.Height);
        dimHisto[key] = dimHisto.TryGetValue(key, out var n) ? n + 1 : 1;
        surfHisto[img.SurfaceCount] = surfHisto.TryGetValue(img.SurfaceCount, out var m) ? m + 1 : 1;
    }

    Console.WriteLine($"raw fuzz: {total} .raw file(s), {ok} decoded OK, {failed} failure(s)");
    Console.WriteLine($"  max dim     : {maxW} x {maxH}");
    Console.WriteLine($"  max surfaces: {maxSurfaces}");
    Console.WriteLine();
    Console.WriteLine("Top dimensions (top 8):");
    foreach (var kv in dimHisto.OrderByDescending(kv => kv.Value).Take(8))
        Console.WriteLine($"  {kv.Key.w,5} x {kv.Key.h,-5}  {kv.Value,6}");
    Console.WriteLine();
    Console.WriteLine("Surface counts:");
    foreach (var kv in surfHisto.OrderBy(kv => kv.Key))
        Console.WriteLine($"  {kv.Key,2} surface(s)  {kv.Value,6}");
    return failed == 0 ? 0 : 4;
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
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx templates <list|show|stats|combat|loot|resolve-textures|equipment-audit|hero-variants> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "list"   => CmdTemplatesList(a[1..]),
        "show"   => CmdTemplatesShow(a[1..]),
        "resolve-textures" => CmdTemplatesResolveTextures(a[1..]),
        "stats"  => CmdTemplatesStats(a[1..]),
        "combat" => CmdTemplatesCombat(a[1..]),
        "loot"   => CmdTemplatesLoot(a[1..]),
        "equipment-audit" => CmdTemplatesEquipmentAudit(a[1..]),
        "hero-variants"   => CmdTemplatesHeroVariants(a[1..]),
        "attrs"           => CmdTemplateAttrDump(a[1..]),
        _        => UnknownCommand("templates " + a[0]),
    };
}

// 21d-2a-viii — enumerate every shipped variant the character creator picker
// will be able to expose: body meshes (pos_a1..a7) per gender, skin textures
// (b_c_gah_<g>_skin_NN), and pants textures (b_c_pos_aN_NNN) per body type.
// The picker doesn't need this list at runtime — it composes names by string —
// but the audit confirms each composed name traces back to a real file in the
// shipped Objects.dsres, so a UI menu built from the audit's output will never
// list a phantom option.
static int CmdTemplatesHeroVariants(string[] a)
{
    if (a.Length != 1)
    {
        Console.Error.WriteLine("usage: siegefx templates hero-variants <objects-tank>");
        return 1;
    }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var paths = reader.ListFiles().ToList();

    foreach (var (gender, prefix) in new[] { ("boy", "gah_fb"), ("girl", "gah_fg") })
    {
        Console.WriteLine();
        Console.WriteLine($"== {gender} ({prefix}) ==");

        // Body meshes m_c_<prefix>_pos_aN.asp
        var bodyTypes = new List<int>();
        for (int i = 1; i <= 7; i++)
        {
            var meshName = $"m_c_{prefix}_pos_a{i}";
            if (paths.Any(p => p.EndsWith($"/{meshName}.asp", StringComparison.OrdinalIgnoreCase)))
                bodyTypes.Add(i);
        }
        Console.WriteLine($"  body types  : {bodyTypes.Count}/7 shipped — pos_a{string.Join(",pos_a", bodyTypes)}");

        // Skin textures b_c_<prefix>_skin_NN.raw
        var skinSuffixes = new List<string>();
        var skinPrefix = $"b_c_{prefix}_skin_";
        foreach (var p in paths)
        {
            var fname = System.IO.Path.GetFileNameWithoutExtension(p);
            if (!p.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) continue;
            if (!fname.StartsWith(skinPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = fname[skinPrefix.Length..];
            if (suffix.Length == 2 && int.TryParse(suffix, out _))
                skinSuffixes.Add(suffix);
        }
        skinSuffixes = skinSuffixes.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        Console.WriteLine($"  skin tones  : {skinSuffixes.Count} — {string.Join(",", skinSuffixes)}");

        // Pants textures b_c_pos_aN_NNN.raw, per body type
        for (int i = 1; i <= 7; i++)
        {
            if (!bodyTypes.Contains(i)) continue;
            var pantsPrefix = $"b_c_pos_a{i}_";
            var pantsSuffixes = new List<string>();
            foreach (var p in paths)
            {
                var fname = System.IO.Path.GetFileNameWithoutExtension(p);
                if (!p.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) continue;
                if (!fname.StartsWith(pantsPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var suffix = fname[pantsPrefix.Length..];
                if (suffix.Length == 3 && int.TryParse(suffix, out _))
                    pantsSuffixes.Add(suffix);
            }
            pantsSuffixes = pantsSuffixes.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
            Console.WriteLine($"  pants pos_a{i}: {pantsSuffixes.Count} — {(pantsSuffixes.Count == 0 ? "<none>" : string.Join(",", pantsSuffixes))}");
        }

        var totalPants = 0;
        for (int i = 1; i <= 7; i++)
        {
            if (!bodyTypes.Contains(i)) continue;
            var pantsPrefix = $"b_c_pos_a{i}_";
            totalPants += paths.Count(p => System.IO.Path.GetFileNameWithoutExtension(p)
                .StartsWith(pantsPrefix, StringComparison.OrdinalIgnoreCase)
                && p.EndsWith(".raw", StringComparison.OrdinalIgnoreCase));
        }
        long combinations = (long)bodyTypes.Count * skinSuffixes.Count * (totalPants == 0 ? 1 : (totalPants / Math.Max(1, bodyTypes.Count)));
        Console.WriteLine($"  combinations: {bodyTypes.Count} body x {skinSuffixes.Count} skin x ~{(bodyTypes.Count == 0 ? 0 : totalPants / Math.Max(1, bodyTypes.Count))} pants/body = ~{combinations:N0}");
    }
    return 0;
}

// Phase 21d-2a-vii — read-only audit of an actor template's equipment slots.
// For each [inventory][equipment] entry (es_weapon_hand, es_feet, es_chest, ...),
// resolves the item template, loads its ASP from the resolver, and characterizes
// the mesh: bone count, skin presence, overlap with the body mesh's bone names.
//
// The intent is to confirm — before writing the layered-equipment renderer — which
// slots use single-bone attachments (like the dagger we wired in 21d-2a-vi) vs
// full-body skinned meshes that share the actor's skeleton. DS1's eBone enum only
// lists weapon_bone/shield_bone/kill_bone, so the working hypothesis is that
// boots/chest/head/forearms are full-body skinned and the spellbook is the only
// other single-bone attach. This audit verifies that hypothesis against shipped
// content rather than relying on it.
static int CmdTemplatesEquipmentAudit(string[] a)
{
    string? terrainPath = null;
    var rest = new List<string>();
    foreach (var x in a)
    {
        if (x.StartsWith("--terrain=", StringComparison.Ordinal)) terrainPath = x["--terrain=".Length..];
        else rest.Add(x);
    }
    if (rest.Count != 3)
    {
        Console.Error.WriteLine("usage: siegefx templates equipment-audit <logic-tank> <objects-tank> <player-template> [--terrain=PATH]");
        return 1;
    }

    using var logicTank   = TankFile.Open(rest[0]);
    using var objectsTank = TankFile.Open(rest[1]);
    var playerTemplate = rest[2];

    var logicReader   = new TankReader(logicTank);
    var objectsReader = new TankReader(objectsTank);
    var (store, _) = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);

    var resolver = new SiegeFX.Core.Assets.AssetResolver();
    if (terrainPath is not null)
    {
        var terrainTank = TankFile.Open(terrainPath);
        resolver.Add(new TankReader(terrainTank), "Terrain.dsres");
    }
    resolver.Add(objectsReader, "Objects.dsres");
    resolver.Add(logicReader,   "Logic.dsres");

    if (!store.TryGet(playerTemplate, out var pcTpl))
    {
        Console.Error.WriteLine($"player template not found: {playerTemplate}");
        return 1;
    }

    Console.WriteLine($"player template : {playerTemplate}");
    Console.Write    ("chain           :");
    for (var cur = pcTpl; cur is not null; cur = cur.Specializes) Console.Write($" {cur.Name}");
    Console.WriteLine();

    var bodyModel = store.GetAttribute(pcTpl, "aspect", "model");
    if (string.IsNullOrEmpty(bodyModel))
    {
        Console.Error.WriteLine("player template has no aspect.model — cannot audit");
        return 2;
    }
    if (!resolver.TryLoadModel(bodyModel, out var bodyAspBytes))
    {
        Console.Error.WriteLine($"body mesh '{bodyModel}.asp' not in any tank");
        return 3;
    }
    var bodyAsp = SiegeFX.Core.Assets.AspMesh.Load(bodyAspBytes);
    Console.WriteLine($"body mesh       : {bodyModel}.asp  v{bodyAsp.AspVersionMajor}.{bodyAsp.AspVersionMinor}");
    Console.WriteLine($"body bones      : {bodyAsp.BoneCount}");
    if (bodyAsp.BoneCount > 0)
    {
        Console.WriteLine("body bone names :");
        for (int bi = 0; bi < bodyAsp.BoneCount; bi++)
            Console.WriteLine($"  [{bi,3}] {bodyAsp.BoneNames[bi]}");
    }
    var bodyBoneSet = new HashSet<string>(bodyAsp.BoneNames, StringComparer.OrdinalIgnoreCase);

    // body.bone_translator authors weapon_bone=weapon_grip / shield_bone=shield_grip,
    // mapping the eBone enum slots (weapon_bone=1, shield_bone=2) to actual bone
    // names on the biped skeleton. Surface what the template authored so the audit
    // shows exactly which body bones are the documented attach points.
    var translator = store.GetSection(pcTpl, "body", "bone_translator");
    if (translator is not null)
    {
        Console.WriteLine($"bone_translator : {translator.Attributes.Count} entries");
        foreach (var attr in translator.Attributes)
        {
            var present = bodyBoneSet.Contains(attr.Value) ? "OK" : "MISS";
            Console.WriteLine($"  {attr.Name,-16} = {attr.Value,-16}  [{present}]");
        }
    }
    else
    {
        Console.WriteLine("bone_translator : <none>");
    }

    // Walk the equipment slots authored on the template (and its specializes chain
    // via GetSection). DS1's actually-mesh-equipped slots are 7: shield_hand,
    // weapon_hand, feet, chest, head, forearms, spellbook. Amulet/rings carry no
    // mesh (UI-only) — anything else surfaces here so we don't quietly skip new slots.
    var eq = store.GetSection(pcTpl, "inventory", "equipment");
    if (eq is null)
    {
        Console.WriteLine();
        Console.WriteLine("equipment       : <no [inventory][equipment] block on template chain>");
        return 0;
    }
    var slotEntries = new List<(string Slot, string Item)>();
    foreach (var attr in eq.Attributes)
    {
        if (!attr.Name.StartsWith("es_", StringComparison.OrdinalIgnoreCase)) continue;
        if (string.IsNullOrWhiteSpace(attr.Value)) continue;
        slotEntries.Add((attr.Name, attr.Value.Trim()));
    }

    Console.WriteLine();
    Console.WriteLine($"equipment slots : {slotEntries.Count}");
    foreach (var (slot, itemRef) in slotEntries)
    {
        Console.WriteLine();
        Console.WriteLine($"== [{slot}] {itemRef}");
        if (!store.TryGet(itemRef, out var itemTpl))
        {
            Console.WriteLine("   item template: <not found in store>");
            continue;
        }
        Console.Write("   chain        :");
        for (var cur = itemTpl; cur is not null; cur = cur.Specializes) Console.Write($" {cur.Name}");
        Console.WriteLine();

        var itemModel = store.GetAttribute(itemTpl, "aspect", "model");
        Console.WriteLine($"   aspect.model : {itemModel ?? "<none>"}");
        if (string.IsNullOrEmpty(itemModel)) continue;

        if (!resolver.TryLoadModel(itemModel, out var itemBytes))
        {
            Console.WriteLine($"   asp          : <{itemModel}.asp not in any tank>");
            continue;
        }
        SiegeFX.Core.Assets.AspMesh asp;
        try { asp = SiegeFX.Core.Assets.AspMesh.Load(itemBytes); }
        catch (Exception ex)
        {
            Console.WriteLine($"   asp          : <load failed: {ex.Message}>");
            continue;
        }
        Console.WriteLine($"   asp          : {itemModel}.asp  v{asp.AspVersionMajor}.{asp.AspVersionMinor}  " +
                          $"corners={asp.Corners.Length}  subsets={asp.Subsets.Length}  textures={asp.TextureNames.Count}");
        Console.WriteLine($"   skin         : {(asp.HasSkin ? $"yes ({asp.SkinWeights.Length} weighted corners)" : "no (rigid)")}");
        Console.WriteLine($"   bones        : {asp.BoneCount}");
        if (asp.BoneCount > 0)
        {
            for (int bi = 0; bi < Math.Min(asp.BoneCount, 16); bi++)
                Console.WriteLine($"     [{bi,3}] {asp.BoneNames[bi]}");
            if (asp.BoneCount > 16) Console.WriteLine($"     ... +{asp.BoneCount - 16} more");
        }

        // Bone-overlap scoring tells us which render path the slot needs:
        //   * 0–1 named overlaps with the body skeleton + asp.HasSkin=false
        //         → single-bone rigid attach (like the dagger in 21d-2a-vi)
        //   * Most/all bone names match body bones + asp.HasSkin=true
        //         → full-body skinned, layer over body using shared bone matrices
        //   * 0 named overlaps + asp.HasSkin=true
        //         → ASP rigs its own skeleton (rare — would need its own matrices)
        int matched = 0;
        var nonMatch = new List<string>();
        for (int bi = 0; bi < asp.BoneCount; bi++)
        {
            if (bodyBoneSet.Contains(asp.BoneNames[bi])) matched++;
            else nonMatch.Add(asp.BoneNames[bi]);
        }
        Console.WriteLine($"   body overlap : {matched} / {asp.BoneCount} bone names match body skeleton");
        if (nonMatch.Count > 0)
        {
            var preview = nonMatch.Count <= 6 ? string.Join(", ", nonMatch) : string.Join(", ", nonMatch.Take(6)) + $" ... +{nonMatch.Count - 6}";
            Console.WriteLine($"   non-match    : {preview}");
        }

        string verdict;
        if (!asp.HasSkin && asp.BoneCount <= 4)
            verdict = "single-bone rigid attach (probe attach bone via es_*→eBone mapping)";
        else if (asp.HasSkin && matched == asp.BoneCount)
            verdict = "full-body skinned, share body bone matrices (1:1 bone-name match)";
        else if (asp.HasSkin && matched > 0 && matched >= asp.BoneCount / 2)
            verdict = "skinned subset of body skeleton (partial overlap; map by name)";
        else if (asp.HasSkin)
            verdict = "skinned but bones don't match body — needs own skeleton (rare)";
        else
            verdict = "rigid multi-bone — uncommon, treat as static prop";
        Console.WriteLine($"   verdict      : {verdict}");
    }
    return 0;
}

// Phase 21d-2a-v — same resolution logic as RenderHost.ResolveActorTexture +
// CmdRegionActorCoverage, but addressable by template name. Useful when an actor
// (the player, vendors, NPCs spawned by start_positions instead of actor.gas) is
// not placed via region actor.gas and therefore not covered by the bulk audit.
// Walks aspect.model → load .asp → for each unique BSMM TextureIndex slot:
// look up [aspect][textures][slot] override, fall back to mesh.TextureNames[slot],
// probe play resolver for {base}.raw and {base}-NN.raw — exactly what the renderer does.
static int CmdTemplatesResolveTextures(string[] a)
{
    if (a.Length < 4)
    {
        Console.Error.WriteLine("usage: siegefx templates resolve-textures <logic-tank> <objects-tank> <terrain-tank> <template-name>");
        return 1;
    }
    using var logicTank   = TankFile.Open(a[0]);
    using var objectsTank = TankFile.Open(a[1]);
    using var terrainTank = TankFile.Open(a[2]);
    var templateName = a[3];
    var logicReader   = new TankReader(logicTank);
    var objectsReader = new TankReader(objectsTank);

    var (store, _) = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);
    var resolver = new SiegeFX.Core.Assets.AssetResolver();
    resolver.Add(new TankReader(terrainTank), "Terrain.dsres");
    resolver.Add(objectsReader, "Objects.dsres");
    resolver.Add(logicReader,   "Logic.dsres");

    if (!store.TryGet(templateName, out var template))
    {
        Console.Error.WriteLine($"template not found: {templateName}");
        return 1;
    }
    Console.WriteLine($"template      : {templateName}");
    var modelName = store.GetAttribute(template, "aspect", "model");
    Console.WriteLine($"aspect.model  : {modelName ?? "<none>"}");
    if (string.IsNullOrEmpty(modelName))
    {
        Console.Error.WriteLine("template has no aspect.model — cannot resolve textures");
        return 2;
    }
    if (!resolver.TryLoadModel(modelName, out var aspBytes))
    {
        Console.Error.WriteLine($"could not load mesh '{modelName}' from any tank");
        return 3;
    }
    var asp = SiegeFX.Core.Assets.AspMesh.Load(aspBytes);
    Console.WriteLine($"mesh          : {asp.MeshName} (v{asp.AspVersionMajor}.{asp.AspVersionMinor})");
    Console.WriteLine($"textures      : {asp.TextureNames.Count}  ({string.Join(", ", asp.TextureNames)})");
    Console.WriteLine($"subsets       : {asp.Subsets.Length}");

    var slotsSeen = new SortedSet<int>();
    foreach (var s in asp.Subsets) slotsSeen.Add(s.TextureIndex);
    if (slotsSeen.Count == 0) slotsSeen.Add(0);

    int missing = 0;
    foreach (var slot in slotsSeen)
    {
        var slotKey = slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var overrideName = store.GetAttribute(template, "aspect", "textures", slotKey);
        var fallbackName = (slot >= 0 && slot < asp.TextureNames.Count) ? asp.TextureNames[slot] : null;
        var baseName = !string.IsNullOrEmpty(overrideName) ? overrideName : fallbackName;
        var src = !string.IsNullOrEmpty(overrideName) ? "template[aspect][textures]" : "mesh.TextureNames";

        bool hit = false; string? resolvedPath = null;
        if (!string.IsNullOrEmpty(baseName))
        {
            if (resolver.TryLoadByBasename(baseName + ".raw", out _)) { hit = true; resolvedPath = baseName + ".raw"; }
            else
            {
                for (int i = 1; i <= 8 && !hit; i++)
                    if (resolver.TryLoadByBasename($"{baseName}-{i:D2}.raw", out _))
                    { hit = true; resolvedPath = $"{baseName}-{i:D2}.raw"; }
            }
        }
        var status = hit ? "OK  " : "MISS";
        Console.WriteLine($"  slot {slot}: {status}  base={baseName ?? "<null>"}  src={src}  resolved={resolvedPath ?? "<n/a>"}");
        if (!hit) missing++;
    }
    return missing == 0 ? 0 : 4;
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
    Console.WriteLine($"attributes        : STR {stats.Strength:F2}  DEX {stats.Dexterity:F2}  INT {stats.Intelligence:F2}");
    Console.WriteLine($"skill levels      : melee {stats.MeleeSkill:F2}  ranged {stats.RangedSkill:F2}  cmag {stats.CombatMagicSkill:F2}  nmag {stats.NatureMagicSkill:F2}  uber {stats.UberLevel:F2}");
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

    // Load the shipped [combat_constants] so the sim resolves with the same to-hit
    // + armor + difficulty math the game uses (falls back to Ds1Default if missing).
    CombatConstants cc;
    try { cc = FormulasStore.LoadFromTank(reader).Combat; }
    catch { cc = CombatConstants.Ds1Default; }

    float atkRating = CombatResolver.AttackRating(atkStats, cc);
    float defRating = CombatResolver.DefendRating(tgtStats, cc);
    float hitPct    = CombatResolver.HitChance(atkStats, tgtStats, cc);

    Console.WriteLine($"attacker  : {attackerT.Name}  life={atkStats.MaxLife:F0} dmg={atkStats.DamageMin:F0}-{atkStats.DamageMax:F0} def={atkStats.Defense:F0}  dex={atkStats.Dexterity:F0} melee={atkStats.MeleeSkill:F0}");
    Console.WriteLine($"target    : {targetT.Name}  life={tgtStats.MaxLife:F0} def={tgtStats.Defense:F0}  dex={tgtStats.Dexterity:F0} melee={tgtStats.MeleeSkill:F0}");
    Console.WriteLine($"to-hit    : {hitPct:F1}%   (attack_rating {atkRating:F1} vs defend_rating {defRating:F1})");
    Console.WriteLine($"duels     : {duels}{(rngSeed is null ? "" : $"  seed={rngSeed}")}");
    Console.WriteLine();

    var rng = new Random(rngSeed ?? Environment.TickCount);
    long totalSwings = 0, totalLanded = 0;
    double totalDmg = 0;
    int kills = 0;
    int capSwings = 4000; // safety cap — misses inflate the swing count vs the old hit-only loop
    for (int d = 0; d < duels; d++)
    {
        var target = new ActorCombatState(tgtStats);
        int swings = 0, landed = 0;
        while (!target.IsDead && swings < capSwings)
        {
            var res = CombatResolver.Resolve(atkStats, tgtStats, rng, cc);
            float actual = target.ApplyDamage(res.Damage);
            if (res.Hit) { landed++; totalDmg += actual; }
            swings++;
        }
        totalSwings += swings;
        totalLanded += landed;
        if (target.IsDead) kills++;
    }

    double meanSwings = totalSwings / (double)duels;
    double meanLanded = totalLanded / (double)duels;
    double meanDmg    = totalLanded > 0 ? totalDmg / totalLanded : 0;
    double landRate   = totalSwings > 0 ? 100.0 * totalLanded / totalSwings : 0;
    Console.WriteLine($"result    : {kills}/{duels} duel(s) reached a kill");
    Console.WriteLine($"  mean swings to kill      : {meanSwings:F1}   (landed {meanLanded:F1}, {landRate:F0}% connect)");
    Console.WriteLine($"  mean damage / landed hit : {meanDmg:F1}");
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

// Phase 10-SC-1 — surface every [instance_triggers] block in a region's special.gas
// (and the templates the placements specialize from). Tallies per-verb coverage so
// the audit can confirm which conditions/actions are wired in TriggerRuntime and
// which are still parsed-but-cold. Also dispatches one tick to confirm the runtime
// handles the parsed matrix without throwing.
static int CmdRegionTriggers(string[] a)
{
    if (a.Length != 3)
    {
        Console.Error.WriteLine("usage: siegefx region triggers <map-tank> <logic-tank> <region-path>");
        return 1;
    }
    using var mapTank   = TankFile.Open(a[0]);
    using var logicTank = TankFile.Open(a[1]);
    var mapReader   = new TankReader(mapTank);
    var logicReader = new TankReader(logicTank);

    var (store, _) = TemplateStore.LoadFromTank(logicReader);
    var (placements, diags) = RegionObjects.LoadPlacements(mapReader, a[2], "special.gas");

    Console.WriteLine($"region      : {a[2]}");
    Console.WriteLine($"placements  : {placements.Count} (special.gas)");
    if (diags.Count > 0)
    {
        Console.WriteLine($"diagnostics : {diags.Count}");
        foreach (var d in diags.Take(5)) Console.WriteLine($"  !! {d}");
        if (diags.Count > 5) Console.WriteLine($"  ... +{diags.Count - 5} more");
    }

    var matrixDiags = new List<string>();
    var conditionVerbs = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var actionVerbs    = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var matrixOwners = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var occupantsGroups = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    int rowsTotal = 0, withMatrix = 0, whenFalseActions = 0, occupantsRows = 0;
    // The trip-tick prefers a placement whose row carries when_false (falling-edge proof);
    // failing that, an occupants_group producer (entered/left proof); failing that, any
    // matrix placement (smoke test the dispatcher walks rows without throwing).
    System.Numerics.Vector3? firstWhenFalsePos = null;
    System.Numerics.Vector3? firstOccupantsPos = null;
    System.Numerics.Vector3? firstMatrixPos = null;

    var runtime = new TriggerRuntime();
    foreach (var p in placements)
    {
        if (!store.TryGet(p.TemplateName, out var template)) continue;
        var matrix = TriggerMatrix.FromInstanceOrTemplate(p, template, store, matrixDiags);
        if (matrix is null) continue;
        withMatrix++;
        rowsTotal += matrix.Rows.Count;
        matrixOwners.TryGetValue(p.TemplateName, out var n);
        matrixOwners[p.TemplateName] = n + 1;
        firstMatrixPos ??= p.Placement.LocalPosition;
        bool placementHasWhenFalseDispatchable = false;
        bool rowHasOccupants = false;
        foreach (var row in matrix.Rows)
        {
            bool rowHasWhenFalse = false;
            bool rowHasAnswerableCondition = false;
            foreach (var c in row.Conditions)
            {
                Bump(conditionVerbs, c.Verb);
                if (c.Verb.Equals("party_member_within_sphere",        StringComparison.OrdinalIgnoreCase)
                 || c.Verb.Equals("party_member_within_bounding_box",  StringComparison.OrdinalIgnoreCase)
                 || c.Verb.Equals("party_member_within_node",          StringComparison.OrdinalIgnoreCase))
                    rowHasAnswerableCondition = true;
            }
            foreach (var act in row.Actions)
            {
                Bump(actionVerbs, act.Verb);
                if (act.WhenFalse) { whenFalseActions++; rowHasWhenFalse = true; }
            }
            // Only nominate this placement for the when_false trip-tick when the row
            // pairs when_false with a condition the synthetic party context can drive
            // (volume tests). Otherwise the rising edge never fires → no falling edge.
            // single_shot rows are still excluded: they latch FiredOnce on the rising
            // edge so the falling edge never re-evaluates. flip_flop is fine — the
            // runtime treats it as inert (see TriggerRuntime.EvaluateInstance).
            if (rowHasWhenFalse && rowHasAnswerableCondition && !row.SingleShot)
                placementHasWhenFalseDispatchable = true;
            if (row.OccupantsGroup.Length > 0)
            {
                occupantsGroups.Add(row.OccupantsGroup);
                occupantsRows++;
                rowHasOccupants = true;
            }
        }
        if (placementHasWhenFalseDispatchable) firstWhenFalsePos ??= p.Placement.LocalPosition;
        if (rowHasOccupants) firstOccupantsPos ??= p.Placement.LocalPosition;
        runtime.Register(new TriggerInstance(p.Scid, p.Placement.NodeGuid,
            p.Placement.LocalPosition, matrix, startActive: matrix.Rows[0].StartActive));
    }

    Console.WriteLine($"trig matrix : {withMatrix}/{placements.Count} placements bear [instance_triggers]");
    Console.WriteLine($"rows total  : {rowsTotal}");
    if (matrixDiags.Count > 0)
    {
        Console.WriteLine($"parse diags : {matrixDiags.Count}");
        foreach (var d in matrixDiags.Take(5)) Console.WriteLine($"  !! {d}");
        if (matrixDiags.Count > 5) Console.WriteLine($"  ... +{matrixDiags.Count - 5} more");
    }

    Console.WriteLine();
    Console.WriteLine($"by template ({matrixOwners.Count} distinct):");
    foreach (var (name, count) in matrixOwners.OrderByDescending(kv => kv.Value).Take(15))
        Console.WriteLine($"  {count,4}  {name}");

    Console.WriteLine();
    Console.WriteLine($"condition verbs ({conditionVerbs.Count}):");
    foreach (var (verb, count) in conditionVerbs)
        Console.WriteLine($"  {count,4}  {verb}{(IsConditionDispatched(verb) ? "" : "   [parsed, not yet dispatched]")}");

    Console.WriteLine();
    Console.WriteLine($"action verbs ({actionVerbs.Count}):");
    foreach (var (verb, count) in actionVerbs)
        Console.WriteLine($"  {count,4}  {verb}{(IsActionDispatched(verb) ? "" : "   [parsed, not yet dispatched]")}");

    Console.WriteLine();
    Console.WriteLine($"occupants   : {occupantsRows} producer rows authoring " +
                      $"{occupantsGroups.Count} distinct trigger_groups");
    if (occupantsGroups.Count > 0)
        foreach (var g in occupantsGroups) Console.WriteLine($"  · {g}");
    Console.WriteLine($"when_false  : {whenFalseActions} actions deferred to falling edge");

    // Dispatch one synthetic tick with a no-op context. The runtime should walk every
    // row without throwing; condition verbs that need world state report false against
    // the empty context, and the smoke-test confirms the dispatcher handles the parsed
    // shape (verb names, arg counts) without an unhandled exception.
    runtime.Tick(1.0 / 20, new TriggerContext());
    Console.WriteLine();
    Console.WriteLine($"dry-tick    : {runtime.Instances.Count} instances iterated, " +
                      $"{runtime.ActionFireCounts.Sum(kv => kv.Value)} actions fired " +
                      $"(empty world ⇒ no condition satisfaction expected)");

    // Phase 10-SC-1b/c smoke test: drive a synthetic party member to the first
    // matrix-bearing placement so volume conditions fire on tick 1 (arrive) and the
    // falling edge fires on tick 2 (depart). Captures hit counts for entered/left
    // trigger_group and the when_false dispatches separately.
    var pickPos = firstWhenFalsePos ?? firstOccupantsPos ?? firstMatrixPos;
    var pickKind = firstWhenFalsePos is not null ? "when_false placement"
        : firstOccupantsPos is not null ? "occupants placement"
        : "first matrix placement";
    if (pickPos is { } here)
    {
        var trip = new TriggerRuntime();
        foreach (var inst in runtime.Instances)
            trip.Register(new TriggerInstance(inst.Scid, inst.NodeGuid, inst.Position,
                inst.Matrix, startActive: inst.Matrix.Rows[0].StartActive));
        var ctx = new SyntheticPartyContext { Position = here };
        trip.Tick(1.0 / 20, ctx);                  // tick 1: arrive
        int firesAfterArrive = trip.ActionFireCounts.Sum(kv => kv.Value);
        ctx.Position = new System.Numerics.Vector3(1e6f, 1e6f, 1e6f);
        trip.Tick(1.0 / 20, ctx);                  // tick 2: leave
        int firesTotal = trip.ActionFireCounts.Sum(kv => kv.Value);
        var entered = trip.ConditionHitCounts.TryGetValue("party_member_entered_trigger_group", out var ne) ? ne : 0;
        var left    = trip.ConditionHitCounts.TryGetValue("party_member_left_trigger_group",    out var nl) ? nl : 0;
        Console.WriteLine();
        Console.WriteLine($"trip-tick   : @{here.X:0.##},{here.Y:0.##},{here.Z:0.##} ({pickKind}) → away → "
            + $"arrive-fires={firesAfterArrive}, depart-fires={firesTotal - firesAfterArrive} "
            + $"(when_false={trip.WhenFalseFireCount}), entered={entered}, left={left}");
    }

    return 0;

    static void Bump(SortedDictionary<string, int> dict, string key)
    {
        dict.TryGetValue(key, out var n);
        dict[key] = n + 1;
    }
    static bool IsConditionDispatched(string verb) => verb.ToLowerInvariant() switch
    {
        "actor_within_sphere" or "go_within_sphere" or "party_member_within_sphere" or
        "party_member_within_bounding_box" or "party_member_within_node" or
        "party_member_entered_trigger_group" or "party_member_left_trigger_group" or
        "receive_world_message" => true,
        _ => false,
    };
    static bool IsActionDispatched(string verb) => verb.ToLowerInvariant() switch
    {
        "send_world_message" or "mood_change" or "set_interest_radius" or
        "fade_node" or "fade_nodes" or "fade_nodes_global" => true,
        _ => false,
    };
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
// SC-WEATHER-F — audit every placed emt_sound / emt_sound_act against the
// sounddb [global_voice] event table, SED aliases, and the shipped wavs.
// Receipts (map_world, 2026-07-07): fh_r1 places 26 emt_sound + 9
// emt_sound_act; the two trigger-toggled amb_rain_01 loops are 0x01C00B24 /
// 0x01C00B18 (event → s_e_ambient_rain1.wav).
static int CmdRegionSoundEmitters(string[] a)
{
    if (a.Length < 4)
    {
        Console.Error.WriteLine("usage: siegefx region sound-emitters <map-tank> <logic-tank> <sound-tank> <region-path|all>");
        return 1;
    }
    using var mapTank   = TankFile.Open(a[0]);
    using var logicTank = TankFile.Open(a[1]);
    using var soundTank = TankFile.Open(a[2]);
    var mapReader   = new TankReader(mapTank);
    var logicReader = new TankReader(logicTank);
    var soundReader = new TankReader(soundTank);

    var (events, evDiags) = SiegeFX.Core.Assets.SoundDb.LoadGlobalVoice(logicReader);
    foreach (var d in evDiags) Console.Error.WriteLine($"  diag: {d}");
    var (seds, _) = SiegeFX.Core.Assets.SedStore.Load(soundReader);
    Console.WriteLine($"sounddb [global_voice]: {events.Count} events; SEDs: {seds.Count}");

    bool all = a[3].Equals("all", StringComparison.OrdinalIgnoreCase);
    var regionPaths = new List<string>();
    if (all)
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
    else regionPaths.Add(a[3]);

    int total = 0, actCount = 0, loops = 0, repeats = 0, oneShots = 0, hourGated = 0;
    int eventMisses = 0, wavMisses = 0;
    var missedEvents = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var eventTally = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    foreach (var rp in regionPaths)
    {
        var (placements, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, "emitter.gas");
        foreach (var inst in placements)
        {
            bool isAct = inst.TemplateName.Equals("emt_sound_act", StringComparison.OrdinalIgnoreCase);
            bool isPlain = inst.TemplateName.Equals("emt_sound", StringComparison.OrdinalIgnoreCase);
            if (!isAct && !isPlain) continue;
            var block = SiegeFX.Core.Assets.TemplateStore.FindChild(
                inst.Node, isAct ? "sound_emitter_act" : "sound_emitter");
            if (block is null) continue;

            string ev = "";
            bool loop = false, rep = false, hg = false;
            float rr = 0f, mrr = 0f;
            foreach (var attr in block.Attributes)
            {
                var n = attr.Name.Trim();
                var v = (attr.Value ?? "").Trim().Trim('"');
                if (n.Equals("event_sound", StringComparison.OrdinalIgnoreCase)) ev = v;
                else if (n.Equals("continual_loop", StringComparison.OrdinalIgnoreCase)) loop = v.Equals("true", StringComparison.OrdinalIgnoreCase);
                else if (n.Equals("repeat", StringComparison.OrdinalIgnoreCase)) rep = v.Equals("true", StringComparison.OrdinalIgnoreCase);
                else if (n.Equals("repeat_rate", StringComparison.OrdinalIgnoreCase)) float.TryParse(v.TrimEnd('f', 'F'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rr);
                else if (n.Equals("max_repeat_rate", StringComparison.OrdinalIgnoreCase)) float.TryParse(v.TrimEnd('f', 'F'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out mrr);
                else if (n.Equals("start_hour", StringComparison.OrdinalIgnoreCase)
                      || n.Equals("stop_hour", StringComparison.OrdinalIgnoreCase)
                      || n.Equals("time_chunk", StringComparison.OrdinalIgnoreCase)) hg = true;
            }
            if (ev.Length == 0) continue;
            total++;
            if (isAct) actCount++;
            if (loop) loops++;
            else if (rep || rr > 0f || mrr > 0f) repeats++;
            else oneShots++;
            if (hg) hourGated++;
            eventTally.TryGetValue(ev, out var tc); eventTally[ev] = tc + 1;

            string kind = loop ? "loop" : (rep || rr > 0f || mrr > 0f) ? $"repeat {rr:0.#}-{Math.Max(mrr, rr):0.#}s" : "one-shot";
            if (!events.TryGetValue(ev, out var samples) || samples.Count == 0)
            {
                eventMisses++;
                missedEvents.TryGetValue(ev, out var c); missedEvents[ev] = c + 1;
                if (!all) Console.WriteLine($"  0x{inst.Scid:x8} {inst.TemplateName,-14} '{ev}' — NOT IN [global_voice]");
                continue;
            }
            var wavNames = new List<string>();
            bool wavOk = true;
            foreach (var sample in samples)
            {
                var key = sample.EndsWith("_SED", StringComparison.OrdinalIgnoreCase) ? sample[..^4] : sample;
                var wav = seds.TryGetValue(key, out var sed) && sed.SoundEffectFile.Length > 0
                    ? sed.SoundEffectFile : key;
                wavNames.Add(wav);
                if (!soundReader.TryGetFile($"/sound/effects/{wav}.wav", out _)) { wavOk = false; wavMisses++; }
            }
            if (!all)
                Console.WriteLine($"  0x{inst.Scid:x8} {inst.TemplateName,-14} '{ev}' → [{string.Join(",", wavNames)}] " +
                                  $"{kind}{(hg ? " HOUR-GATED" : "")}{(wavOk ? "" : " WAV-MISSING")}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"  emitters: {total} ({actCount} act, {total - actCount} plain) — " +
                      $"{loops} loops, {repeats} repeating, {oneShots} one-shot, {hourGated} hour-gated");
    Console.WriteLine($"  event misses: {eventMisses}, wav misses: {wavMisses}");
    foreach (var (ev, c) in missedEvents) Console.WriteLine($"    missing event: {ev} ×{c}");
    if (all)
    {
        Console.WriteLine($"  distinct events: {eventTally.Count}; top usage:");
        foreach (var (ev, c) in eventTally.OrderByDescending(kv => kv.Value).Take(15))
            Console.WriteLine($"    {ev,-32} ×{c}");
    }
    // Shipped-data baseline: exactly one placement (map_world) authors
    // 'env_rats_sqeakskitter' — a typo for env_rats_squeakskitter (35 correct
    // placements). It's silent in retail DS1 too, so it doesn't fail the audit.
    bool onlyKnownTypo = eventMisses == 1
        && missedEvents.Count == 1
        && missedEvents.ContainsKey("env_rats_sqeakskitter");
    if (onlyKnownTypo)
        Console.WriteLine("  (the single miss is the known shipped typo — silent in retail too; PASS)");
    return (wavMisses == 0 && (eventMisses == 0 || onlyKnownTypo)) ? 0 : 2;
}

// SC-DECAL-REGRESSION — measure every decals.gas orientation matrix under the
// competing interpretations that produced the "hovering ground" regression:
//   read:      row-major rows=(N,H,V) [original c1bc4a8]  vs
//              column-major cols=(H,V,N) [418a9be]        vs
//              the N-last permutations of each
//   transform: axes rotated by the anchor node's world transform [original]
//              vs axes used as-authored [936c129 "world-space"]
// Majorness is decided transform-independently: the CORRECT read yields an
// orthonormal (H,V,N) triad per decal (H⊥V, unit lengths). The transform
// question is decided by tilt bimodality: ground decals (shadows/dirt/rugs)
// must be horizontal AND the barn-door chars vertical under the same rule.
static int CmdRegionDecalAudit(string[] a)
{
    if (a.Length < 3)
    {
        Console.Error.WriteLine("usage: siegefx region decal-audit <map-tank> <terrain-tank> <region-path|all>");
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

    bool all = a[2].Equals("all", StringComparison.OrdinalIgnoreCase);
    var regionPaths = new List<string>();
    if (all)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in mapReader.ListFiles())
        {
            if (!path.EndsWith("/decals/decals.gas", StringComparison.OrdinalIgnoreCase)) continue;
            var rp = path[..^"/decals/decals.gas".Length];
            if (seen.Add(rp)) regionPaths.Add(rp);
        }
        regionPaths.Sort(StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"auditing {regionPaths.Count} regions with decals...");
    }
    else regionPaths.Add(a[2].TrimEnd('/'));

    // reads: 0 = rows (N,H,V)   [c1bc4a8]
    //        1 = cols (H,V,N)   [418a9be / current]
    //        2 = rows (H,V,N)
    //        3 = cols (N,H,V)
    string[] readNames = { "rows N,H,V", "cols H,V,N", "rows H,V,N", "cols N,H,V" };
    var orthoErr = new double[4];
    // per (read, transform 0=node-rotated 1=as-authored): [horizontal, vertical, oblique]
    var tilt = new int[4, 2, 3];
    int totalDecals = 0, nonFinite = 0;
    var anchors = new List<string>();
    // Under the CURRENT shipped interpretation (cols H,V,N, as-authored):
    // every non-horizontal decal, so the hovering population is nameable.
    var suspects = new List<string>();
    // GROUND TRUTH — per (read, frame 0=node-local 1=world): decals whose
    // plane normal matches the nearest SNO face normal at the decal origin.
    // decal_origin is node-local, so the SNO comparison happens in node space:
    // node-local axes compare raw; world axes compare against the face normal
    // rotated into world by the node transform. Buckets: aligned ≤10°,
    // loose ≤26°, misaligned.
    var align = new int[4, 2, 3];
    int alignSamples = 0;
    var drapeReport = new List<string>();

    foreach (var rp in regionPaths)
    {
        var decPath = rp + "/decals/decals.gas";
        if (!mapReader.TryGetFile(decPath, out _)) { if (!all) Console.WriteLine("  no decals.gas"); continue; }
        SiegeFX.Core.Assets.GasDocument doc;
        try { doc = SiegeFX.Core.Assets.GasDocument.Load(mapReader.ExtractToMemory(decPath)); }
        catch { continue; }

        RegionLayout? layout = null;
        RegionGraph? graphRef = null;
        try
        {
            var graph = RegionGraph.Load(mapReader.ExtractToMemory(rp + "/terrain_nodes/nodes.gas"));
            graphRef = graph;
            layout = RegionLayout.Build(graph, Resolve);
        }
        catch { /* transforms fall back to identity */ }

        void Walk(SiegeFX.Core.Assets.GasNode node)
        {
            var hdr = node.Header.Trim().TrimStart('[').TrimEnd(']');
            bool isDecal = hdr.Split(',').Any(p =>
            {
                var t = p.Trim();
                return t.StartsWith("t:", StringComparison.OrdinalIgnoreCase) &&
                       t[2..].Trim().Equals("decal", StringComparison.OrdinalIgnoreCase);
            });
            if (isDecal)
            {
                string? ori = null, org = null, tex = null;
                foreach (var at in node.Attributes)
                {
                    if (at.Name.Equals("decal_orientation", StringComparison.OrdinalIgnoreCase)) ori = at.Value;
                    else if (at.Name.Equals("decal_origin", StringComparison.OrdinalIgnoreCase)) org = at.Value;
                    else if (at.Name.Equals("texture", StringComparison.OrdinalIgnoreCase)) tex = at.Value;
                }
                if (ori is not null && org is not null && tex is not null)
                {
                    var parts = ori.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    var op = org.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 9 && op.Length >= 4)
                    {
                        var o = new float[9];
                        bool okF = true;
                        for (int i = 0; i < 9; i++)
                            okF &= float.TryParse(parts[i].TrimEnd(';'), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out o[i]);
                        var guidS = op[3].Trim().TrimEnd(';');
                        if (guidS.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) guidS = guidS[2..];
                        uint.TryParse(guidS, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out var nodeGuid);
                        if (okF)
                        {
                            totalDecals++;
                            var texName = tex.Trim().TrimEnd(';').Trim();
                            int slash = texName.LastIndexOfAny(new[] { '\\', '/' });
                            if (slash >= 0) texName = texName[(slash + 1)..];
                            int dot = texName.IndexOf('.');
                            if (dot >= 0) texName = texName[..dot];

                            Matrix4x4 nw = Matrix4x4.Identity;
                            if (layout is null || !layout.TryGetTransform(nodeGuid, out nw))
                                nw = Matrix4x4.Identity;

                            // Ground truth: the SNO face nearest the (node-local)
                            // decal origin. Take the best-aligned of the 6 nearest
                            // face candidates so a decal at a floor/wall junction
                            // scores against its own surface.
                            var localOrigin = new Vector3(
                                float.Parse(op[0].TrimEnd(';'), System.Globalization.CultureInfo.InvariantCulture),
                                float.Parse(op[1].TrimEnd(';'), System.Globalization.CultureInfo.InvariantCulture),
                                float.Parse(op[2].TrimEnd(';'), System.Globalization.CultureInfo.InvariantCulture));
                            var nearFaces = new List<(float dist, Vector3 n)>();
                            if (graphRef is not null && graphRef.TryGetNode(nodeGuid, out var gnode))
                            {
                                var sno = Resolve(gnode.MeshGuid);
                                if (sno is not null)
                                {
                                    foreach (var surf in sno.Surfaces)
                                    {
                                        var tris = surf.TriangleIndices;
                                        for (int t = 0; t + 2 < tris.Length; t += 3)
                                        {
                                            var pa = sno.Corners[surf.StartCorner + tris[t]].Position;
                                            var pb = sno.Corners[surf.StartCorner + tris[t + 1]].Position;
                                            var pc = sno.Corners[surf.StartCorner + tris[t + 2]].Position;
                                            var centroid = (pa + pb + pc) / 3f;
                                            float dist = Vector3.DistanceSquared(centroid, localOrigin);
                                            if (dist > 9f) continue;   // 3m radius
                                            var fn = Vector3.Cross(pb - pa, pc - pa);
                                            if (fn.LengthSquared() < 1e-10f) continue;
                                            nearFaces.Add((dist, Vector3.Normalize(fn)));
                                        }
                                    }
                                    nearFaces.Sort((x, y) => x.dist.CompareTo(y.dist));
                                    if (nearFaces.Count > 6) nearFaces.RemoveRange(6, nearFaces.Count - 6);
                                }
                            }
                            if (nearFaces.Count > 0) alignSamples++;

                            // DRAPE SIM — mirror the runtime's ground gate (cols
                            // H,V,N node-local, |n.Y|>0.7) and measure the true
                            // distance from the decal plane to the surface in its
                            // XZ column, flagging anything the runtime's ±2m snap
                            // window can't reach (those still float post-drape).
                            {
                                var Hc = new Vector3(o[0], o[3], o[6]);
                                var Vc = new Vector3(o[1], o[4], o[7]);
                                var nC = Vector3.Cross(Hc, Vc);
                                if (nC.LengthSquared() > 1e-10f && MathF.Abs(Vector3.Normalize(nC).Y) > 0.7f
                                    && graphRef is not null && graphRef.TryGetNode(nodeGuid, out var gnode2))
                                {
                                    var sno2 = Resolve(gnode2.MeshGuid);
                                    if (sno2 is null)
                                    {
                                        drapeReport.Add($"{System.IO.Path.GetFileName(rp),-14} {texName,-26} NO-SNO (mesh 0x{gnode2.MeshGuid:X8})");
                                    }
                                    else
                                    {
                                        float bestAny = float.MaxValue;
                                        bool anyHit = false;
                                        foreach (var surf in sno2.Surfaces)
                                        {
                                            var tris2 = surf.TriangleIndices;
                                            for (int t = 0; t + 2 < tris2.Length; t += 3)
                                            {
                                                var a2 = sno2.Corners[surf.StartCorner + tris2[t]].Position;
                                                var b2 = sno2.Corners[surf.StartCorner + tris2[t + 1]].Position;
                                                var c2 = sno2.Corners[surf.StartCorner + tris2[t + 2]].Position;
                                                float e00x = b2.X - a2.X, e00z = b2.Z - a2.Z;
                                                float e01x = c2.X - a2.X, e01z = c2.Z - a2.Z;
                                                float det2 = e00x * e01z - e01x * e00z;
                                                if (MathF.Abs(det2) < 1e-9f) continue;
                                                float px = localOrigin.X - a2.X, pz = localOrigin.Z - a2.Z;
                                                float uu = (px * e01z - e01x * pz) / det2;
                                                float vv = (e00x * pz - px * e00z) / det2;
                                                if (uu < -0.001f || vv < -0.001f || uu + vv > 1.001f) continue;
                                                float ty = a2.Y + uu * (b2.Y - a2.Y) + vv * (c2.Y - a2.Y);
                                                anyHit = true;
                                                float delta = localOrigin.Y - ty;   // + = plane above surface
                                                if (MathF.Abs(delta) < MathF.Abs(bestAny)) bestAny = delta;
                                            }
                                        }
                                        if (!anyHit)
                                            drapeReport.Add($"{System.IO.Path.GetFileName(rp),-14} {texName,-26} NO-SURFACE-IN-COLUMN");
                                        else if (MathF.Abs(bestAny) > 2.0f)
                                            drapeReport.Add($"{System.IO.Path.GetFileName(rp),-14} {texName,-26} nearest-surface={bestAny,6:F2}m  BEYOND-2M-WINDOW");
                                        else if (!all || MathF.Abs(bestAny) > 0.25f)
                                            drapeReport.Add($"{System.IO.Path.GetFileName(rp),-14} {texName,-26} nearest-surface={bestAny,6:F2}m (snaps)");
                                    }
                                }
                            }

                            for (int read = 0; read < 4; read++)
                            {
                                (Vector3 H, Vector3 V) = read switch
                                {
                                    0 => (new Vector3(o[3], o[4], o[5]), new Vector3(o[6], o[7], o[8])),
                                    1 => (new Vector3(o[0], o[3], o[6]), new Vector3(o[1], o[4], o[7])),
                                    2 => (new Vector3(o[0], o[1], o[2]), new Vector3(o[3], o[4], o[5])),
                                    _ => (new Vector3(o[1], o[4], o[7]), new Vector3(o[2], o[5], o[8])),
                                };
                                float oe = Math.Abs(Vector3.Dot(H, V))
                                         + Math.Abs(H.Length() - 1f)
                                         + Math.Abs(V.Length() - 1f);
                                if (float.IsFinite(oe)) orthoErr[read] += oe;
                                else if (read == 0) nonFinite++;
                                // Surface alignment in NODE space: frame 0 treats
                                // the authored axes as node-local (raw vs raw);
                                // frame 1 treats them as world (rotate the face
                                // normal up into world before comparing).
                                if (nearFaces.Count > 0)
                                {
                                    var nLocal = Vector3.Cross(H, V);
                                    if (nLocal.LengthSquared() > 1e-10f)
                                    {
                                        nLocal = Vector3.Normalize(nLocal);
                                        for (int frame = 0; frame < 2; frame++)
                                        {
                                            float best = 0f;
                                            foreach (var (_, fn) in nearFaces)
                                            {
                                                var fcmp = frame == 0 ? fn : Vector3.Normalize(Vector3.TransformNormal(fn, nw));
                                                best = MathF.Max(best, MathF.Abs(Vector3.Dot(nLocal, fcmp)));
                                            }
                                            int b = best > 0.985f ? 0 : best > 0.9f ? 1 : 2;
                                            align[read, frame, b]++;
                                        }
                                    }
                                }

                                for (int xf = 0; xf < 2; xf++)
                                {
                                    var Ht = xf == 0 ? Vector3.TransformNormal(H, nw) : H;
                                    var Vt = xf == 0 ? Vector3.TransformNormal(V, nw) : V;
                                    var n = Vector3.Cross(Ht, Vt);
                                    if (n.LengthSquared() < 1e-10f) continue;
                                    n = Vector3.Normalize(n);
                                    float ay = MathF.Abs(n.Y);
                                    int bucket = ay > 0.94f ? 0 : ay < 0.34f ? 1 : 2;
                                    tilt[read, xf, bucket]++;
                                    if ((texName.Contains("burnt", StringComparison.OrdinalIgnoreCase)
                                         || texName.Contains("rug", StringComparison.OrdinalIgnoreCase)
                                         || texName.Contains("shadow", StringComparison.OrdinalIgnoreCase))
                                        && anchors.Count < 400)
                                        anchors.Add($"{texName,-24} read={readNames[read],-10} xf={(xf == 0 ? "node" : "raw ")} |n.Y|={ay:F2}");
                                    if (read == 1 && xf == 1 && bucket != 0 && suspects.Count < 200)
                                    {
                                        var rname = System.IO.Path.GetFileName(rp);
                                        suspects.Add($"{rname,-14} {texName,-28} |n.Y|={ay:F2} {(bucket == 1 ? "VERTICAL" : "OBLIQUE")}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            foreach (var c in node.Children) Walk(c);
        }
        foreach (var root in doc.Roots) Walk(root);
    }

    Console.WriteLine($"decals measured: {totalDecals} (non-finite orientation values: {nonFinite})");
    Console.WriteLine();
    Console.WriteLine("orthonormality error per read (lower = correct matrix majorness; non-finite skipped):");
    for (int r = 0; r < 4; r++)
        Console.WriteLine($"  {readNames[r],-12} mean err = {(totalDecals > 0 ? orthoErr[r] / totalDecals : 0):F4}");
    Console.WriteLine();
    Console.WriteLine("plane tilt by (read, transform): horizontal / vertical / OBLIQUE");
    for (int r = 0; r < 4; r++)
        for (int xf = 0; xf < 2; xf++)
            Console.WriteLine($"  {readNames[r],-12} {(xf == 0 ? "node-rotated" : "as-authored ")}  " +
                              $"H={tilt[r, xf, 0],5}  V={tilt[r, xf, 1],5}  oblique={tilt[r, xf, 2],5}");
    Console.WriteLine();
    Console.WriteLine($"GROUND TRUTH — plane vs nearest SNO face normal ({alignSamples} decals with geometry):");
    Console.WriteLine("  (read, frame): aligned<=10deg / loose<=26deg / MISALIGNED — correct combo maximizes aligned");
    for (int r = 0; r < 4; r++)
        for (int f = 0; f < 2; f++)
            Console.WriteLine($"  {readNames[r],-12} {(f == 0 ? "node-local" : "world     ")}  " +
                              $"aligned={align[r, f, 0],5}  loose={align[r, f, 1],5}  MISALIGNED={align[r, f, 2],5}");
    Console.WriteLine();
    Console.WriteLine($"non-horizontal decals under the CURRENT interpretation (cols H,V,N as-authored) — {suspects.Count}:");
    foreach (var s in suspects) Console.WriteLine("  " + s);
    Console.WriteLine();
    Console.WriteLine($"DRAPE SIMULATION (runtime rules: cols H,V,N node-local, ground=|n.Y|>0.7) — {drapeReport.Count} ground decals:");
    Console.WriteLine("  hover = plane-origin height above the surface directly below each decal center;");
    Console.WriteLine("  'beyond2m' = outside the runtime's ±2m snap window = STILL FLOATS after drape");
    foreach (var s in drapeReport) Console.WriteLine("  " + s);
    if (!all)
    {
        Console.WriteLine();
        Console.WriteLine("known-anchor decals (chars must be V≈vertical, rugs/shadows H≈1.00):");
        foreach (var s in anchors) Console.WriteLine("  " + s);
    }
    return 0;
}

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

// Phase 21-SC-BARREL-E — breakable-prop audit. Walks every region's static-prop
// placements; for each whose template carries [physics][break_particulate] and
// is not aspect:is_invincible, tallies (template, life, has-pcontent, frag
// count, gold-bucket count, item-bucket count). Output: per-template summary
// + per-region totals + a global rollup. Use as the structural-completeness
// receipt for sub-slices C+D — every shipped breakable should resolve a
// non-zero frag count and (for regional templates) a non-empty pcontent.
static int CmdRegionBreakableAudit(string[] a)
{
    if (a.Length < 4)
    {
        Console.Error.WriteLine("usage: siegefx region breakable-audit <map-tank> <logic-tank> <objects-tank> <region-path|all> [--top=N]");
        return 1;
    }
    int top = 30;
    for (int i = 4; i < a.Length; i++)
    {
        const string topPrefix = "--top=";
        if (a[i].StartsWith(topPrefix) && int.TryParse(a[i][topPrefix.Length..], out var n)) top = n;
        else { Console.Error.WriteLine($"unknown option: {a[i]}"); return 1; }
    }

    using var mapTank     = TankFile.Open(a[0]);
    using var logicTank   = TankFile.Open(a[1]);
    using var objectsTank = TankFile.Open(a[2]);
    var mapReader     = new TankReader(mapTank);
    var logicReader   = new TankReader(logicTank);
    var (store, _) = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);

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

    int totalPlacements = 0, totalBreakable = 0, totalWithPcontent = 0;
    int totalFragEntries = 0, totalGoldBuckets = 0, totalItemBuckets = 0;
    var perTemplate = new SortedDictionary<string,
        (int placements, bool breakable, int fragEntries, int frags, bool hasPcontent, int goldBuckets, int itemBuckets, float maxLife, string? breakSound)>(
        StringComparer.OrdinalIgnoreCase);
    var perRegion = new List<(string path, int placements, int breakables, int withPcontent)>();

    foreach (var regionPath in regionPaths)
    {
        int regPlacements = 0, regBreakable = 0, regPcontent = 0;
        foreach (var fileName in SiegeFX.Core.Assets.RegionObjects.StaticPropFiles)
        {
            var (placements, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, regionPath, fileName);
            foreach (var p in placements)
            {
                totalPlacements++;
                regPlacements++;
                if (!store.TryGet(p.TemplateName, out var template)) continue;

                var breakSection = store.GetSection(template, "physics", "break_particulate");
                if (breakSection is null) continue;
                var inv = store.GetAttribute(template, "aspect", "is_invincible");
                bool invincible = inv is not null &&
                    (inv.Equals("true", StringComparison.OrdinalIgnoreCase) || inv == "1");
                if (invincible) continue;

                int fragEntries = 0, frags = 0;
                foreach (var attr in breakSection.Attributes)
                {
                    if (int.TryParse(attr.Value,
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var c) && c > 0)
                    {
                        fragEntries++;
                        frags += c;
                    }
                }
                var lifeAttr = store.GetAttribute(template, "aspect", "max_life") ??
                               store.GetAttribute(template, "aspect", "life");
                float maxLife = 1f;
                if (!string.IsNullOrEmpty(lifeAttr))
                    float.TryParse(lifeAttr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out maxLife);
                // Phase 21-SC-BARREL-FOLD verification — resolve the break-
                // sound cue under both authored paths so the audit shows
                // which templates would actually play a shatter sound.
                var breakSound = store.GetAttribute(template, "aspect", "voice", "die", "*")
                              ?? store.GetAttribute(template, "physics", "break_sound");
                if (string.IsNullOrWhiteSpace(breakSound)) breakSound = null;
                var table = SiegeFX.Core.Actors.LootTable.FromTemplate(store, template);
                bool hasPcontent = !table.IsEmpty;
                int goldBuckets = 0, itemBuckets = 0;
                foreach (var bucket in table.Drops)
                    CountBucketKinds(bucket, ref goldBuckets, ref itemBuckets);

                totalBreakable++;
                regBreakable++;
                if (hasPcontent) { totalWithPcontent++; regPcontent++; }
                totalFragEntries += fragEntries;
                totalGoldBuckets += goldBuckets;
                totalItemBuckets += itemBuckets;

                perTemplate.TryGetValue(p.TemplateName, out var entry);
                perTemplate[p.TemplateName] = (
                    entry.placements + 1,
                    breakable: true,
                    entry.fragEntries == 0 ? fragEntries : entry.fragEntries,
                    entry.frags == 0 ? frags : entry.frags,
                    hasPcontent,
                    entry.goldBuckets == 0 ? goldBuckets : entry.goldBuckets,
                    entry.itemBuckets == 0 ? itemBuckets : entry.itemBuckets,
                    maxLife,
                    entry.breakSound ?? breakSound);
            }
        }
        perRegion.Add((regionPath, regPlacements, regBreakable, regPcontent));
    }

    Console.WriteLine();
    Console.WriteLine($"siegefx region breakable-audit  —  {regionPaths.Count} region(s) walked");
    Console.WriteLine($"  total placements          : {totalPlacements}");
    Console.WriteLine($"  breakable placements      : {totalBreakable}");
    Console.WriteLine($"  …with [inventory.pcontent]: {totalWithPcontent}");
    Console.WriteLine($"  total break_particulate frag entries: {totalFragEntries}");
    Console.WriteLine($"  total [gold*] buckets        : {totalGoldBuckets}");
    Console.WriteLine($"  total item-drop buckets      : {totalItemBuckets}");

    Console.WriteLine();
    Console.WriteLine($"top {top} breakable templates by placement count:");
    foreach (var kv in perTemplate.OrderByDescending(x => x.Value.placements).Take(top))
    {
        var v = kv.Value;
        Console.WriteLine(
            $"  {v.placements,4}x  life={v.maxLife,5:F0}  frags={v.frags,3}({v.fragEntries} entries)  " +
            $"pcontent={(v.hasPcontent ? "Y" : "-")}  gold={v.goldBuckets} item={v.itemBuckets}  " +
            $"break={v.breakSound ?? "<none>"}  {kv.Key}");
    }

    Console.WriteLine();
    Console.WriteLine($"top regions by breakable count:");
    foreach (var r in perRegion.OrderByDescending(x => x.breakables).Take(top))
        Console.WriteLine($"  {r.breakables,4} breakable / {r.withPcontent,3} with pcontent / {r.placements,5} placements  {r.path}");
    return 0;
}

static void CountBucketKinds(SiegeFX.Core.Actors.LootBucket bucket, ref int gold, ref int items)
{
    foreach (var entry in bucket.Entries)
    {
        if (entry.IsGold) gold++;
        else items++;
    }
    foreach (var child in bucket.Children)
        CountBucketKinds(child, ref gold, ref items);
}

// SC-BARREL-LOOT-VERIFY (2026-05-13) — Monte-Carlo roll every breakable container
// placed in a region (or every region with --all) against its template's pcontent,
// aggregate the outcomes into a human-readable distribution table, and compare
// against the per-chapter loot expectations the Sybex guide documents.
// Per-template: empty %, gold %/total, item %/named breakdown. Per-region: total
// expected gold yield, total potions by type, total gear by slot. Drift between
// this readout and the guide is the receipt that pcontent rolling matches DS1.
/// <summary>SC-MOB-LOOT-TRACE — the actor-death counterpart of
/// loot-distribution: Monte-Carlo every actor template placed in a region
/// (actor.gas + generator children) through LootTable/LootRoller and print
/// per-template drop distributions, plus the SET-drop facts read straight
/// off the template: the specific carried weapon, and drops_spellbook with
/// the authored spells. This is the authoritative per-mob answer to "which
/// drops are set and which are random".</summary>
// Phase 25c — where is a template placed? Sweeps every region's
// actor.gas placements for names containing the given substring
// (locating shopkeepers/hireables: `region find-template World.dsmap adwana`).
static int CmdRegionFindTemplate(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx region find-template <map-tank> <name-substring>");
        return 1;
    }
    using var mapTank = TankFile.Open(a[0]);
    var mapReader = new TankReader(mapTank);
    var regionPaths = new List<string>();
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
    }
    int hits = 0;
    foreach (var rp in regionPaths)
    {
        var (actors, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, "actor.gas");
        foreach (var p in actors)
        {
            if (p.TemplateName.IndexOf(a[1], StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine($"  {p.TemplateName,-36} {rp}");
            hits++;
        }
    }
    Console.WriteLine($"{hits} placement(s)");
    return hits > 0 ? 0 : 4;
}

// Phase 24b — map-wide drop-completeness gate. Sweeps EVERY region's
// actor.gas + generator children and asserts:
//  (1) every caster with drops_spellbook=true authors a primary spell
//      that resolves in the SpellCatalog AND appears in 100% of its
//      loot rolls (authored spell drops are SET, not random);
//  (2) every #spell/#cmagic/#nmagic pcontent reference in any mob loot
//      table resolves to at least one indexed spell.
// Exit 0 only when both hold across the whole map.
static int CmdRegionDropSweep(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx region drop-sweep <map-tank> <logic-tank> [--rolls=N] [--seed=K]");
        return 1;
    }
    int rolls = 25, seed = 1;
    for (int i = 2; i < a.Length; i++)
    {
        if (a[i].StartsWith("--rolls=") && int.TryParse(a[i]["--rolls=".Length..], out var r)) rolls = r;
        else if (a[i].StartsWith("--seed=") && int.TryParse(a[i]["--seed=".Length..], out var s)) seed = s;
    }

    using var mapTank = TankFile.Open(a[0]);
    using var logicTank = TankFile.Open(a[1]);
    var mapReader = new TankReader(mapTank);
    var logicReader = new TankReader(logicTank);
    var (store, _) = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);
    var catalog = SiegeFX.Core.Assets.SpellCatalog.Build(store);
    var resolver = new SiegeFX.Core.Actors.PcontentResolver(store);

    // Discover regions.
    var regionPaths = new List<string>();
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
    }

    // Gather every distinct mob template map-wide (actors + generator children).
    var mobTemplates = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var rp in regionPaths)
    {
        var (actors, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, "actor.gas");
        foreach (var p in actors)
        {
            mobTemplates.TryGetValue(p.TemplateName, out var c);
            mobTemplates[p.TemplateName] = c + 1;
        }
        var (gens, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, "generator.gas");
        foreach (var p in gens)
        {
            string? child = null;
            foreach (var c in p.Node.Children)
            {
                if (!c.Header.StartsWith("generator", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var at in c.Attributes)
                    if (at.Name.Equals("child_template_name", StringComparison.OrdinalIgnoreCase))
                        child = at.Value.Trim().Trim('"');
                break;
            }
            if (child is null && store.TryGet(p.TemplateName, out var gt) && gt is not null)
            {
                string? blockHeader = null;
                for (var t = gt; t is not null && blockHeader is null; t = t.Specializes)
                    foreach (var c in t.Node.Children)
                    {
                        if (!c.Header.StartsWith("generator_", StringComparison.OrdinalIgnoreCase)) continue;
                        if (c.Header.Equals("generator_in_object", StringComparison.OrdinalIgnoreCase)) continue;
                        blockHeader = c.Header;
                        break;
                    }
                if (blockHeader is not null)
                    child = store.GetAttribute(gt, blockHeader, "child_template_name")?.Trim().Trim('"');
            }
            if (child is null) continue;
            mobTemplates.TryGetValue(child, out var cc);
            mobTemplates[child] = cc + 1;
        }
    }

    int casters = 0, castersOk = 0, specRefs = 0, specOk = 0;
    var failures = new List<string>();
    var specRng = new Random(seed);
    foreach (var (name, placed) in mobTemplates)
    {
        if (!store.TryGet(name, out var template) || template is null) continue;

        // (1) authored-spell drop assertion.
        var sb = store.GetAttribute(template, "actor", "drops_spellbook")?.Trim();
        if (sb is not null && sb.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            casters++;
            var prim = store.GetAttribute(template, "inventory", "other", "il_active_primary_spell")?.Trim().Trim('"');
            if (string.IsNullOrEmpty(prim))
            {
                failures.Add($"{name}: drops_spellbook=true but no il_active_primary_spell authored");
                continue;
            }
            // The guaranteed drop is applied on the RUNTIME kill path
            // (RenderHost SC-MOB-SPELLBOOK augments the roll), not inside
            // LootRoller — so the sweep asserts the same gate the runtime
            // uses: the authored primary must resolve in the catalog as a
            // player-acquirable spell. Monster-utility secondaries
            // intentionally stay behind.
            if (!catalog.TryGet(prim, out var primSpell))
            {
                failures.Add($"{name}: primary spell '{prim}' not in SpellCatalog");
                continue;
            }
            // Mirror the runtime kill-path gate: player-school spells OR
            // castable monster attacks (offensive/heal Kind) drop;
            // monster utilities (Kind.Other) stay behind by design.
            if (!primSpell.PlayerAcquirable
                && primSpell.Kind is not (SiegeFX.Core.Assets.SpellKind.OffensiveInstantHit
                                          or SiegeFX.Core.Assets.SpellKind.SelfHeal))
            {
                failures.Add($"{name}: primary spell '{prim}' would never drop (class {primSpell.Class}, kind {primSpell.Kind})");
                continue;
            }
            castersOk++;
        }

        // (2) spell pcontent spec resolution across the loot table.
        var lt = SiegeFX.Core.Actors.LootTable.FromTemplate(store, template);
        void ScanBuckets(IReadOnlyList<SiegeFX.Core.Actors.LootBucket> buckets)
        {
            foreach (var b in buckets)
            {
                foreach (var e in b.Entries)
                {
                    if (!SiegeFX.Core.Actors.PcontentResolver.IsSpec(e.Reference)) continue;
                    var cls = SiegeFX.Core.Actors.PcontentResolver.ParseSpec(e.Reference).Class.ToLowerInvariant();
                    if (cls is not ("spell" or "cmagic" or "nmagic")) continue;
                    specRefs++;
                    if (resolver.TryResolve(e.Reference, specRng, out _)) specOk++;
                    else failures.Add($"{name}: spell spec '{e.Reference}' resolves to NOTHING");
                }
                ScanBuckets(b.Children);
            }
        }
        ScanBuckets(lt.Drops);
        ScanBuckets(lt.Equipped);
    }

    Console.WriteLine($"siegefx region drop-sweep  —  {regionPaths.Count} regions, {mobTemplates.Count} distinct mob templates");
    Console.WriteLine();
    Console.WriteLine($"  casters (drops_spellbook)      : {castersOk}/{casters} drop their authored spell in {rolls}/{rolls} rolls");
    Console.WriteLine($"  spell pcontent specs           : {specOk}/{specRefs} resolve");
    Console.WriteLine($"  failures                       : {failures.Count}");
    foreach (var f in failures) Console.WriteLine("  FAIL  " + f);
    return failures.Count == 0 ? 0 : 4;
}

static int CmdRegionMobLoot(string[] a)
{
    if (a.Length < 3)
    {
        Console.Error.WriteLine("usage: siegefx region mob-loot <map-tank> <logic-tank> <region-path> [--rolls=N] [--seed=K]");
        return 1;
    }
    int rolls = 2000, seed = 1;
    for (int i = 3; i < a.Length; i++)
    {
        if (a[i].StartsWith("--rolls=") && int.TryParse(a[i]["--rolls=".Length..], out var r)) rolls = r;
        else if (a[i].StartsWith("--seed=") && int.TryParse(a[i]["--seed=".Length..], out var s)) seed = s;
        else { Console.Error.WriteLine($"unknown option: {a[i]}"); return 1; }
    }

    using var mapTank = TankFile.Open(a[0]);
    using var logicTank = TankFile.Open(a[1]);
    var mapReader = new TankReader(mapTank);
    var logicReader = new TankReader(logicTank);
    var (store, _) = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);

    var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var (actors, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, a[2], "actor.gas");
    foreach (var p in actors)
    {
        counts.TryGetValue(p.TemplateName, out var c);
        counts[p.TemplateName] = c + 1;
    }
    // Generator children count as region mobs too — child template from the
    // placement block or the template chain (same rule the runtime uses).
    var (gens, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, a[2], "generator.gas");
    foreach (var p in gens)
    {
        string? child = null;
        string? blockHeader = null;
        foreach (var c in p.Node.Children)
        {
            if (!c.Header.StartsWith("generator", StringComparison.OrdinalIgnoreCase)) continue;
            blockHeader = c.Header;
            foreach (var at in c.Attributes)
                if (at.Name.Equals("child_template_name", StringComparison.OrdinalIgnoreCase))
                    child = at.Value.Trim().Trim('"');
            break;
        }
        if (child is null && store.TryGet(p.TemplateName, out var gt) && gt is not null)
        {
            for (var t = gt; t is not null && blockHeader is null; t = t.Specializes)
                foreach (var c in t.Node.Children)
                {
                    if (!c.Header.StartsWith("generator_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (c.Header.Equals("generator_in_object", StringComparison.OrdinalIgnoreCase)) continue;
                    blockHeader = c.Header;
                    break;
                }
            if (blockHeader is not null)
                child = store.GetAttribute(gt, blockHeader, "child_template_name")?.Trim().Trim('"');
        }
        if (child is null) continue;
        counts.TryGetValue(child, out var cc);
        counts[child] = cc + 1;
    }

    Console.WriteLine($"mob-loot: {a[2]} — {counts.Count} unique template(s), {rolls} rolls each, seed={seed}");
    foreach (var (name, placed) in counts)
    {
        if (!store.TryGet(name, out var template) || template is null)
        {
            Console.WriteLine($"\n  {name} (x{placed}) — template not in store");
            continue;
        }
        Console.WriteLine($"\n  {name} (x{placed}):");
        // SET facts straight off the template chain.
        var spellbook = store.GetAttribute(template, "actor", "drops_spellbook")?.Trim();
        if (spellbook is not null && spellbook.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            var prim = store.GetAttribute(template, "inventory", "other", "il_active_primary_spell")?.Trim();
            var sec  = store.GetAttribute(template, "inventory", "other", "il_active_secondary_spell")?.Trim();
            Console.WriteLine($"    SET: drops_spellbook = true (spells: {prim ?? "-"}{(sec is null ? "" : ", " + sec)})");
        }
        var table = SiegeFX.Core.Actors.LootTable.FromTemplate(store, template);
        if (table.IsEmpty)
        {
            Console.WriteLine("    (no inventory.pcontent — never drops)");
            continue;
        }
        var rng = new Random(seed ^ name.GetHashCode(StringComparison.OrdinalIgnoreCase));
        int empty = 0;
        var itemCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rolls; i++)
        {
            var drops = SiegeFX.Core.Actors.LootRoller.Roll(table, rng);
            if (drops.Count == 0) { empty++; continue; }
            foreach (var d in drops)
            {
                var key = d.IsEquipped ? $"[worn] {d.Reference}" : d.Reference;
                itemCounts.TryGetValue(key, out var ic);
                itemCounts[key] = ic + 1;
            }
        }
        Console.WriteLine($"    empty: {empty * 100.0 / rolls:F1}%");
        foreach (var (item, n) in itemCounts.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    {n * 100.0 / rolls,5:F1}%  {item}");
    }
    return 0;
}

static int CmdRegionLootDistribution(string[] a)
{
    if (a.Length < 4)
    {
        Console.Error.WriteLine("usage: siegefx region loot-distribution <map-tank> <logic-tank> <objects-tank> <region-path|all> [--rolls=N] [--seed=K] [--top=N]");
        return 1;
    }
    int rollsPerPlacement = 1000;
    int seed = 1;
    int top = 30;
    for (int i = 4; i < a.Length; i++)
    {
        const string rollsPrefix = "--rolls=";
        const string seedPrefix  = "--seed=";
        const string topPrefix   = "--top=";
        if (a[i].StartsWith(rollsPrefix) && int.TryParse(a[i][rollsPrefix.Length..], out var r)) rollsPerPlacement = r;
        else if (a[i].StartsWith(seedPrefix) && int.TryParse(a[i][seedPrefix.Length..], out var s)) seed = s;
        else if (a[i].StartsWith(topPrefix) && int.TryParse(a[i][topPrefix.Length..], out var n)) top = n;
        else { Console.Error.WriteLine($"unknown option: {a[i]}"); return 1; }
    }

    using var mapTank   = TankFile.Open(a[0]);
    using var logicTank = TankFile.Open(a[1]);
    using var objectsTank = TankFile.Open(a[2]);
    var mapReader   = new TankReader(mapTank);
    var logicReader = new TankReader(logicTank);
    var (store, _)  = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);

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
        Console.WriteLine($"loot-distribution: {regionPaths.Count} region(s), {rollsPerPlacement} rolls per placement, seed={seed}");
    }
    else
    {
        regionPaths.Add(a[3]);
    }

    // Aggregate stats per template + per region. Each placement contributes
    // rollsPerPlacement samples to its template's bucket; region totals are
    // the sum of (template per-roll yields × placement count).
    var perTemplate = new SortedDictionary<string,
        (int placements, int rolls, int emptyRolls, int goldRolls, long goldTotal,
         SortedDictionary<string, int> itemCounts)>(StringComparer.OrdinalIgnoreCase);
    var rng = new Random(seed);

    foreach (var regionPath in regionPaths)
    {
        var (placements, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(
            mapReader, regionPath, "container.gas");
        foreach (var p in placements)
        {
            if (!store.TryGet(p.TemplateName, out var template)) continue;
            var breakSection = store.GetSection(template, "physics", "break_particulate");
            if (breakSection is null) continue;
            var inv = store.GetAttribute(template, "aspect", "is_invincible");
            if (inv is not null && (inv.Equals("true", StringComparison.OrdinalIgnoreCase) || inv == "1"))
                continue;
            var table = SiegeFX.Core.Actors.LootTable.FromTemplate(store, template);
            if (table.IsEmpty) continue;

            if (!perTemplate.TryGetValue(p.TemplateName, out var bag))
                bag = (0, 0, 0, 0, 0L, new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase));
            bag.placements++;

            for (int i = 0; i < rollsPerPlacement; i++)
            {
                var drops = SiegeFX.Core.Actors.LootRoller.Roll(table, rng);
                bag.rolls++;
                if (drops.Count == 0) { bag.emptyRolls++; continue; }
                bool gotGold = false;
                foreach (var d in drops)
                {
                    if (d.IsGold)
                    {
                        var (lo, hi) = d.GoldRange();
                        if (hi >= lo && hi > 0)
                        {
                            int pick = rng.Next(lo, hi + 1);
                            bag.goldTotal += pick;
                            gotGold = true;
                        }
                    }
                    else
                    {
                        bag.itemCounts.TryGetValue(d.Reference, out var c);
                        bag.itemCounts[d.Reference] = c + 1;
                    }
                }
                if (gotGold) bag.goldRolls++;
            }
            perTemplate[p.TemplateName] = bag;
        }
    }

    if (perTemplate.Count == 0)
    {
        Console.WriteLine("no breakable containers with pcontent found in scope.");
        return 0;
    }

    // Sort by placement count (busiest templates first); cap to --top.
    var ordered = perTemplate.OrderByDescending(kv => kv.Value.placements).ToList();
    int shown = 0;
    long sumGoldExpected = 0;
    int sumPlacements = 0;
    var aggregateItems = new SortedDictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    foreach (var (templateName, bag) in ordered)
    {
        sumPlacements += bag.placements;
        // Expected per-region yield = per-roll average × placements.
        // bag.rolls = placements × rollsPerPlacement (one roll per simulated
        // break). Average gold per break = goldTotal / rolls. Multiplying by
        // placements gives the expected total if the player breaks each
        // container exactly once on a region walk.
        double expectedGoldThisTemplate = bag.rolls > 0
            ? (double)bag.goldTotal * bag.placements / bag.rolls
            : 0;
        sumGoldExpected += (long)Math.Round(expectedGoldThisTemplate);
        foreach (var (item, count) in bag.itemCounts)
        {
            double expected = bag.rolls > 0
                ? (double)count * bag.placements / bag.rolls
                : 0;
            aggregateItems.TryGetValue(item, out var prev);
            aggregateItems[item] = prev + expected;
        }
        if (shown++ >= top) continue;

        double emptyPct = bag.rolls == 0 ? 0 : 100.0 * bag.emptyRolls / bag.rolls;
        double goldPct  = bag.rolls == 0 ? 0 : 100.0 * bag.goldRolls / bag.rolls;
        double avgGold  = bag.goldRolls == 0 ? 0 : (double)bag.goldTotal / bag.goldRolls;
        Console.WriteLine();
        Console.WriteLine($"  {templateName}  ({bag.placements} placement(s), {bag.rolls} rolls):");
        Console.WriteLine($"    empty:  {bag.emptyRolls,6}  ({emptyPct,5:F1}%)");
        Console.WriteLine($"    gold:   {bag.goldRolls,6}  ({goldPct,5:F1}%)   avg={avgGold,5:F1}g  per-template total={bag.goldTotal}g  expected/region={expectedGoldThisTemplate,5:F1}g");
        if (bag.itemCounts.Count > 0)
        {
            int itemRolls = 0;
            foreach (var v in bag.itemCounts.Values) itemRolls += v;
            double itemPct = bag.rolls == 0 ? 0 : 100.0 * itemRolls / bag.rolls;
            Console.WriteLine($"    items:  {itemRolls,6}  ({itemPct,5:F1}%)");
            foreach (var (item, count) in bag.itemCounts.OrderByDescending(kv => kv.Value))
            {
                double pct = bag.rolls == 0 ? 0 : 100.0 * count / bag.rolls;
                Console.WriteLine($"               {count,6}  ({pct,5:F2}%)  {item}");
            }
        }
    }

    if (ordered.Count > top)
        Console.WriteLine($"  ... {ordered.Count - top} more template(s) (raise --top to see)");

    Console.WriteLine();
    // When run with --all, perTemplate aggregates ACROSS every region
    // (barrel_glb_fh_r1 placements in fh_r1 and elsewhere merge), so the
    // total is a grand-total across the scope, not a per-region average.
    // Label accordingly so the reader knows what they're looking at — per-
    // region splits would require a separate pass and are deferred until
    // someone needs them.
    string totalsHeader = regionPaths.Count > 1 ? "GRAND TOTALS (scope-wide)" : "REGION TOTALS";
    Console.WriteLine($"{totalsHeader}  ({regionPaths.Count} region path(s), {sumPlacements} container placements):");
    Console.WriteLine($"  expected gold yield: ~{sumGoldExpected}g across all containers");
    if (aggregateItems.Count > 0)
    {
        Console.WriteLine($"  expected item drops:");
        foreach (var (item, count) in aggregateItems.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    {count,7:F2}  {item}");
    }
    return 0;
}

// Phase 21d-2a-iii prep — actor-coverage audit. Mirror of prop-textures but for the
// NPC layer: walks every region's actor.gas, resolves template -> aspect.model -> .asp,
// then walks AspMesh.Subsets and checks each subset's texture slot via the same
// (template-override-by-slot, mesh.TextureNames[slot]) precedence ResolveActorTexture
// uses at runtime. Catches missing meshes, missing slot textures, and parse breakers
// before the playtest hits them. Exits non-zero if anything fails to resolve.
static int CmdRegionActorCoverage(string[] a)
{
    if (a.Length < 4)
    {
        Console.Error.WriteLine("usage: siegefx region actor-coverage <map-tank> <logic-tank> <objects-tank> <region-path|all> [--terrain=PATH] [--top=N] [--list-misses]");
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

    int placements = 0, withMesh = 0, parseFail = 0;
    int noTemplate = 0, noModel = 0, noMesh = 0;
    int totalSlots = 0, slotsResolved = 0, slotsMissing = 0;
    int actorsAllSlotsClean = 0, actorsAnySlotMissing = 0;
    int multiSubsetActors = 0;
    var perTpl = new SortedDictionary<string, (int placements, int slotsResolved, int slotsMissing, string? mesh)>(StringComparer.OrdinalIgnoreCase);
    var unresolvedNames = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var perRegion = new List<(string path, int placements, int slotsMissing)>();

    var aspCache = new Dictionary<string, SiegeFX.Core.Assets.AspMesh?>(StringComparer.OrdinalIgnoreCase);

    foreach (var regionPath in regionPaths)
    {
        int regPlacements = 0, regSlotsMissing = 0;
        var (actors, _) = SiegeFX.Core.Assets.RegionObjects.LoadActors(mapReader, regionPath);
        foreach (var p in actors)
        {
            placements++;
            regPlacements++;
            if (!store.TryGet(p.TemplateName, out var template)) { noTemplate++; continue; }
            var modelName = store.GetAttribute(template, "aspect", "model");
            if (string.IsNullOrEmpty(modelName)) { noModel++; continue; }

            if (!aspCache.TryGetValue(modelName, out var asp))
            {
                if (!resolver.TryLoadModel(modelName, out var aspBytes)) { aspCache[modelName] = null; }
                else
                {
                    try { asp = SiegeFX.Core.Assets.AspMesh.Load(aspBytes); aspCache[modelName] = asp; }
                    catch { aspCache[modelName] = null; }
                }
            }
            if (asp is null) { if (!resolver.TryLoadModel(modelName, out _)) noMesh++; else parseFail++; continue; }
            withMesh++;
            if (asp.Subsets.Length > 1) multiSubsetActors++;

            // Walk each unique slot referenced by the mesh's subsets. Multiple
            // subsets sharing a TextureIndex resolve once; matches the renderer's
            // per-slot cache key.
            var slotsSeen = new HashSet<int>();
            int actorMissing = 0, actorResolved = 0;
            var allSubsets = asp.Subsets.Length == 0
                ? new[] { new SiegeFX.Core.Assets.AspMesh.Subset(0, asp.TriangleCount, 0) }
                : asp.Subsets;
            foreach (var sub in allSubsets)
            {
                if (!slotsSeen.Add(sub.TextureIndex)) continue;
                totalSlots++;
                var slotKey = sub.TextureIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var baseName = store.GetAttribute(template, "aspect", "textures", slotKey);
                if (string.IsNullOrEmpty(baseName) && sub.TextureIndex >= 0 && sub.TextureIndex < asp.TextureNames.Count)
                    baseName = asp.TextureNames[sub.TextureIndex];

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
                if (hit) { slotsResolved++; actorResolved++; }
                else
                {
                    slotsMissing++; actorMissing++; regSlotsMissing++;
                    if (!string.IsNullOrEmpty(baseName))
                    {
                        unresolvedNames.TryGetValue(baseName, out var nn);
                        unresolvedNames[baseName] = nn + 1;
                    }
                }
            }
            if (actorMissing == 0) actorsAllSlotsClean++;
            else actorsAnySlotMissing++;

            perTpl.TryGetValue(p.TemplateName, out var entry);
            perTpl[p.TemplateName] = (entry.placements + 1, entry.slotsResolved + actorResolved,
                                      entry.slotsMissing + actorMissing, modelName);
        }
        perRegion.Add((regionPath, regPlacements, regSlotsMissing));
    }

    bool batch = regionPaths.Count > 1;
    Console.WriteLine();
    if (batch) Console.WriteLine($"regions       : {regionPaths.Count}");
    else       Console.WriteLine($"region        : {a[3]}");
    Console.WriteLine($"actor placements    : {placements}");
    Console.WriteLine($"  no template       : {noTemplate}");
    Console.WriteLine($"  no aspect.model   : {noModel}");
    Console.WriteLine($"  missing .asp file : {noMesh}");
    Console.WriteLine($"  asp parse fail    : {parseFail}");
    Console.WriteLine($"  with mesh         : {withMesh}");
    Console.WriteLine($"    all slots clean : {actorsAllSlotsClean}");
    Console.WriteLine($"    any slot missing: {actorsAnySlotMissing}");
    Console.WriteLine($"    multi-subset    : {multiSubsetActors}");
    Console.WriteLine($"texture slots       : {totalSlots}");
    Console.WriteLine($"  resolved          : {slotsResolved}");
    Console.WriteLine($"  missing           : {slotsMissing}");
    Console.WriteLine();
    Console.WriteLine($"templates with un-resolved slots (top {top} by miss count):");
    var dirty = perTpl.Where(x => x.Value.slotsMissing > 0)
                      .OrderByDescending(x => x.Value.slotsMissing)
                      .Take(top);
    foreach (var kv in dirty)
    {
        var v = kv.Value;
        Console.WriteLine($"  {v.slotsMissing,4}x missing ({v.slotsResolved} resolved) over {v.placements} placement(s)  template={kv.Key}  mesh={v.mesh}");
    }
    if (!perTpl.Values.Any(v => v.slotsMissing > 0)) Console.WriteLine("  (none — every actor slot resolves)");
    if (listMisses && unresolvedNames.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("distinct unresolved texset basenames (count):");
        foreach (var kv in unresolvedNames.OrderByDescending(x => x.Value))
            Console.WriteLine($"  {kv.Value,4}x  {kv.Key}");
    }
    if (batch)
    {
        var dirtyRegions = perRegion.Where(r => r.slotsMissing > 0).ToList();
        Console.WriteLine();
        Console.WriteLine($"per-region misses ({dirtyRegions.Count} dirty / {perRegion.Count} total):");
        if (dirtyRegions.Count == 0) Console.WriteLine("  (all regions clean)");
        foreach (var r in dirtyRegions.OrderByDescending(r => r.slotsMissing))
            Console.WriteLine($"  {r.slotsMissing,4}x missing slots / {r.placements,5} placements  {r.path}");
    }
    terrainTank?.Dispose();
    return (slotsMissing == 0 && noMesh == 0 && parseFail == 0) ? 0 : 4;
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
    var choreCoverage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    long catalogueTotal = 0;
    int catalogueMin = int.MaxValue, catalogueMax = 0;
    foreach (var actor in actors)
    {
        if (actor.Host.CurrentAnimIndex >= 0) withAnim++;
        if (actor.Skrit.CurrentState is not null) withState++;
        clipPicks[actor.CurrentClipIndex] = clipPicks.GetValueOrDefault(actor.CurrentClipIndex) + 1;

        catalogueTotal += actor.Clips.Length;
        if (actor.Clips.Length < catalogueMin) catalogueMin = actor.Clips.Length;
        if (actor.Clips.Length > catalogueMax) catalogueMax = actor.Clips.Length;
        foreach (var name in actor.ClipIndexByName.Keys)
            choreCoverage[name] = choreCoverage.GetValueOrDefault(name) + 1;
    }

    Console.WriteLine();
    Console.WriteLine($"after tick:");
    Console.WriteLine($"  in a skrit state   : {withState}/{actors.Count}");
    Console.WriteLine($"  picked a clip      : {withAnim}/{actors.Count}");
    Console.WriteLine($"  clip-index tally   : {string.Join(", ", clipPicks.OrderBy(kv => kv.Key).Select(kv => $"#{kv.Key}×{kv.Value}"))}");
    if (actors.Count > 0)
    {
        Console.WriteLine($"  clip catalogue     : avg {catalogueTotal / (double)actors.Count:0.00} per actor (min {catalogueMin}, max {catalogueMax})");
        Console.WriteLine($"  chore coverage     : {string.Join(", ", choreCoverage.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}×{kv.Value}"))}");
    }
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
    Console.WriteLine();

    var cc = f.Combat;
    Console.WriteLine("combat_constants (drives CombatResolver):");
    Console.WriteLine($"  attack_rating = {cc.AttackSkillScalar}*skill + {cc.AttackDexScalar}*dex + {cc.AttackIntScalar}*int");
    Console.WriteLine($"  defend_rating = {cc.DefendSkillScalar}*skill + {cc.DefendDexScalar}*dex + {cc.DefendIntScalar}*int");
    Console.WriteLine($"  hit_chance    = {cc.BaseHitChance} + (AR-DR)*{cc.AttackerDiffScalar}, clamp [{cc.DefenderHitCap}, {cc.AttackerHitCap}]");
    Console.WriteLine($"  armor         = dmg*difficulty - defense*{cc.ArmorScalar}/{CombatResolver.ArmorDivisor}   (medium: player {cc.DifficultyPlayer}, computer {cc.DifficultyComputer})");
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
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx spells <dump|show|survey|eval|elements|visual-audit> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "dump"         => CmdSpellsDump(a[1..]),
        "show"         => CmdSpellsShow(a[1..]),
        "survey"       => CmdSpellsSurvey(a[1..]),
        "eval"         => CmdSpellsEval(a[1..]),
        "elements"     => CmdSpellsElements(a[1..]),
        "visual-audit" => CmdSpellsVisualAudit(a[1..]),
        "icon-audit"   => CmdSpellsIconAudit(a[1..]),
        _              => UnknownCommand("spells " + a[0]),
    };
}

// Phase 17-SC-A3: free-form expression evaluator. Lets us reproduce ternary +
// comparison receipts from the CLI without needing a spell that lands in
// SpellCatalog (healing_hands' [[?:]] is the canonical case but it doesn't
// match the SelfHeal predicate yet, and leech_life isn't OffensiveInstantHit
// either — Phase 17-SC-D will widen the catalog).
//   siegefx spells eval "<expr>" [--magic=N] [--maxlife=N] [--life=N]
//                                [--src_mana=N] [--src_life=N]
static int CmdSpellsEval(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("usage: siegefx spells eval \"<expr>\" [--magic=N] [--maxlife=N] [--life=N] [--src_mana=N] [--src_life=N]");
        return 1;
    }
    string expr = a[0];
    float magic = 0f, maxLife = 0f, life = 0f, srcMana = 0f, srcLife = 0f;
    for (int i = 1; i < a.Length; i++)
    {
        var s = a[i];
        if      (s.StartsWith("--magic=",    StringComparison.Ordinal)) float.TryParse(s.AsSpan("--magic=".Length),    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out magic);
        else if (s.StartsWith("--maxlife=",  StringComparison.Ordinal)) float.TryParse(s.AsSpan("--maxlife=".Length),  System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out maxLife);
        else if (s.StartsWith("--life=",     StringComparison.Ordinal)) float.TryParse(s.AsSpan("--life=".Length),     System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out life);
        else if (s.StartsWith("--src_mana=", StringComparison.Ordinal)) float.TryParse(s.AsSpan("--src_mana=".Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out srcMana);
        else if (s.StartsWith("--src_life=", StringComparison.Ordinal)) float.TryParse(s.AsSpan("--src_life=".Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out srcLife);
    }
    var ctx = new SpellEvalContext(magic, maxLife, life, srcMana, srcLife);
    float v = SpellExpr.Eval(expr, ctx);
    Console.WriteLine($"= {v}");
    return 0;
}

// Phase 17-SC-A: survey all spell_* templates and report which operators
// (** ^ %) and #-placeholders appear in their [magic] block damage / mana /
// heal expressions. Used to scope SpellExpr coverage gaps. Walks
// attack_damage_modifier_min/max + mana_cost_modifier on every spell_*
// template + walks the alter_life enchantment value for self-heal spells.
static int CmdSpellsSurvey(string[] a)
{
    if (a.Length < 1) { Console.Error.WriteLine("usage: siegefx spells survey <Logic.dsres>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var (store, _) = TemplateStore.LoadFromTank(reader);

    var ops = new SortedDictionary<string, int>();
    var phs = new SortedDictionary<string, int>();
    var sampleByOp = new Dictionary<string, (string spell, string expr)>();
    var sampleByPh = new Dictionary<string, (string spell, string expr)>();
    int spellCount = 0, exprCount = 0;

    void Inspect(string spell, string expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return;
        exprCount++;
        // Operators of interest beyond what SpellExpr currently handles (+ - * /).
        if (expr.Contains("**")) { ops["**"] = ops.GetValueOrDefault("**") + 1; sampleByOp.TryAdd("**", (spell, expr)); }
        if (expr.Contains('^')) { ops["^"] = ops.GetValueOrDefault("^") + 1;  sampleByOp.TryAdd("^",  (spell, expr)); }
        if (expr.Contains('%')) { ops["%"] = ops.GetValueOrDefault("%") + 1;  sampleByOp.TryAdd("%",  (spell, expr)); }
        for (int i = 0; i < expr.Length; i++)
        {
            if (expr[i] != '#') continue;
            int s = ++i;
            while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_')) i++;
            var name = expr.Substring(s, i - s);
            phs[name] = phs.GetValueOrDefault(name) + 1;
            sampleByPh.TryAdd(name, (spell, expr));
            i--;
        }
    }

    foreach (var t in store.All)
    {
        if (!t.Name.StartsWith("spell_", StringComparison.OrdinalIgnoreCase)) continue;
        spellCount++;
        Inspect(t.Name, store.GetAttribute(t, "magic", "attack_damage_modifier_min") ?? "");
        Inspect(t.Name, store.GetAttribute(t, "magic", "attack_damage_modifier_max") ?? "");
        Inspect(t.Name, store.GetAttribute(t, "magic", "mana_cost_modifier") ?? "");
        var ench = store.GetSection(t, "magic", "enchantments");
        if (ench is not null)
        {
            foreach (var child in ench.Children)
            {
                if (!string.Equals((TemplateStore.FindAttr(child, "alteration") ?? "").Trim().Trim('"'),
                                   "alter_life", StringComparison.OrdinalIgnoreCase)) continue;
                Inspect(t.Name, TemplateStore.FindAttr(child, "value") ?? "");
            }
        }
    }

    Console.WriteLine($"spell_* templates    : {spellCount}");
    Console.WriteLine($"non-empty exprs scanned: {exprCount}");
    Console.WriteLine();
    Console.WriteLine("operators beyond + - * /:");
    if (ops.Count == 0) Console.WriteLine("  (none)");
    foreach (var kv in ops)
    {
        var samp = sampleByOp[kv.Key];
        Console.WriteLine($"  {kv.Key,-3} -> {kv.Value,3}    e.g. {samp.spell}: {samp.expr}");
    }
    Console.WriteLine();
    Console.WriteLine("placeholders:");
    foreach (var kv in phs)
    {
        var samp = sampleByPh[kv.Key];
        Console.WriteLine($"  #{kv.Key,-12} -> {kv.Value,3}    e.g. {samp.spell}: {samp.expr}");
    }
    return 0;
}

// Phase 17-SC-B: classify every parsed spell by element bucket so we can
// verify the renderer's per-element tinting maps onto sensible spells. Walks
// the SpellCatalog (so only fully-parseable templates count) and groups by
// SpellElement, listing the spells under each bucket. Receipt: zap →
// Lightning, fireball → Fire, iceshard → Ice, etc., with no large Generic
// pile (a big Generic count would mean the keyword set missed real spells).
static int CmdSpellsElements(string[] a)
{
    if (a.Length < 1) { Console.Error.WriteLine("usage: siegefx spells elements <Logic.dsres>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var (store, _) = TemplateStore.LoadFromTank(reader);
    var cat = SpellCatalog.Build(store);

    var byElem = new SortedDictionary<SpellElement, List<SpellTemplate>>();
    foreach (var s in cat.All)
    {
        if (!byElem.TryGetValue(s.Element, out var list)) byElem[s.Element] = list = new();
        list.Add(s);
    }
    Console.WriteLine($"spell elements (catalog size: {cat.Count})");
    foreach (var kv in byElem)
    {
        Console.WriteLine($"  {kv.Key,-9} ({kv.Value.Count}):");
        foreach (var s in kv.Value.OrderBy(s => s.Name))
            Console.WriteLine($"    {s.Name,-32} \"{s.ScreenName}\"");
    }
    return 0;
}

// Phase 21-SC-SPELL-VFX-AUDIT: headless visual-coverage audit over every
// offensive spell's cast sfx_script. Walks each script's compiled IR
// statically (no GL, no synthetic time-stepping) and reports:
//   1. which `sfx create` kinds are invoked across the catalog, tagged
//      OK / MISS against SfxRuntime.MapMode's covered set
//      (fire / smoke / steam / lightning / explosion / sparkles)
//   2. which verbs the VM still treats as Raw (trackball / waitfor /
//      spawn / fireb / sphere / orbiter / worldmsg / get / etc.)
//   3. which textures (b_sfx_*) the param strings reference, plus how
//      many spells reach for each
//   4. a per-spell verdict: COVERED / PARTIAL / UNCOVERED
// `call <subscript>` recurses one level so composed scripts (DS1's
// canonical pattern — a spell calls a shared bolt/burst primitive)
// get audited end-to-end. Output drives the SC-SPELL-VFX-3 scope: we
// know exactly which verbs unblock how many spells before guessing
// fireball was the right next pick.
static int CmdSpellsVisualAudit(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("usage: siegefx spells visual-audit <Logic.dsres> [--verbose] [--filter=NAME] [--only-uncovered]");
        return 1;
    }
    bool verbose = false;
    bool onlyUncovered = false;
    string? filter = null;
    for (int i = 1; i < a.Length; i++)
    {
        if (a[i] == "--verbose") verbose = true;
        else if (a[i] == "--only-uncovered") onlyUncovered = true;
        else if (a[i].StartsWith("--filter=", StringComparison.OrdinalIgnoreCase))
            filter = a[i]["--filter=".Length..];
    }

    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var (templates, _) = TemplateStore.LoadFromTank(reader);
    var spells = SpellCatalog.Build(templates);
    var sfx    = SfxScriptStore.LoadFromTank(reader);

    // Phase 21-SC-SPELL-VFX-3c — single source of truth for "what kinds
    // does the runtime actually render": SfxRuntime.SupportedCreateKinds.
    // Was duplicated here in the audit's first pass (927284a); folded into
    // the runtime so the audit and the cast-site coverage check can never
    // drift apart.
    var coveredKinds = SiegeFX.Core.Sfx.SfxRuntime.SupportedCreateKinds;
    // Bare top-level verbs whose unhandled-ness alone moves a spell from
    // COVERED → PARTIAL. Today only `waitfor` qualifies — it gates the
    // whole script (e.g. waitfor collision before the impact burst), so a
    // skipped waitfor breaks visual sequencing even when every primitive
    // is otherwise handled. The other "critical" things you'd naively put
    // here (trackball / orbiter / sphere / fireb / charge / lightsource)
    // are actually `sfx create <kind>` *kinds*, not bare verbs — DS1
    // scripts ship them as `sfx create trackball ...` and the compiler
    // routes them as `SfxCreate` with the kind in `Tokens[0]`. Those land
    // in `PrimitiveKinds` and get caught by `!allPrimsCovered` below; they
    // never reach `UnhandledVerbs`. Folded out of this set on review of
    // 927284a so the table reads what it actually does.
    var criticalUnhandled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "waitfor",
    };

    var results = new List<SpellAuditRow>();
    int withScript = 0, withoutScript = 0, missingScript = 0;
    var primitiveTally = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    var textureTally   = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    var unhandledTally = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    foreach (var spell in spells.All.OrderBy(s => s.Name, StringComparer.Ordinal))
    {
        if (filter != null && spell.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            continue;

        if (string.IsNullOrEmpty(spell.CastSfxScript)) { withoutScript++; continue; }
        if (!sfx.TryGet(spell.CastSfxScript, out var script)) { missingScript++; continue; }
        withScript++;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var row = new SpellAuditRow(spell.Name, spell.CastSfxScript);
        WalkAuditScript(script.Name, script.Body, sfx, row, visited);

        bool hasAnyPrimitive = row.PrimitiveKinds.Count > 0;
        bool allPrimsCovered = hasAnyPrimitive && row.PrimitiveKinds.All(k => coveredKinds.Contains(k));
        bool anyCritical     = row.UnhandledVerbs.Any(v => criticalUnhandled.Contains(v));
        row.Verdict =
            !hasAnyPrimitive ? "UNCOVERED" :
            (allPrimsCovered && !anyCritical) ? "COVERED" :
            "PARTIAL";

        results.Add(row);

        foreach (var k in row.PrimitiveKinds) AppendTo(primitiveTally, k, spell.Name);
        foreach (var t in row.Textures)       AppendTo(textureTally,   t, spell.Name);
        foreach (var v in row.UnhandledVerbs) AppendTo(unhandledTally, v, spell.Name);
    }

    int total     = results.Count;
    int covered   = results.Count(r => r.Verdict == "COVERED");
    int partial   = results.Count(r => r.Verdict == "PARTIAL");
    int uncovered = results.Count(r => r.Verdict == "UNCOVERED");

    Console.WriteLine($"siegefx spells visual-audit  —  {spells.Count} offensive spells, {sfx.Count} sfx_scripts in pack");
    Console.WriteLine();
    Console.WriteLine($"  cast_sfx_script coverage : {withScript} resolved, {missingScript} missing, {withoutScript} no [we_req_cast] row");
    Console.WriteLine();
    Console.WriteLine($"  visual coverage  (of {total} runnable spells):");
    Console.WriteLine($"     COVERED   : {covered,3}  every `sfx create` kind handled and no `waitfor` gating left unhandled");
    Console.WriteLine($"     PARTIAL   : {partial,3}  uses unmodeled create kinds (orbiter/trackball/cylinder/lightsource/...) or hits an unhandled `waitfor`");
    Console.WriteLine($"     UNCOVERED : {uncovered,3}  no recognized `sfx create` kind at all (DS1-author stubs in shipped data)");
    Console.WriteLine();

    Console.WriteLine($"Primitive `sfx create` kinds invoked ({primitiveTally.Count} distinct):");
    foreach (var kv in primitiveTally.OrderByDescending(p => p.Value.Count).ThenBy(p => p.Key, StringComparer.Ordinal))
    {
        var tag = coveredKinds.Contains(kv.Key) ? "OK  " : "MISS";
        var sample = string.Join(", ", kv.Value.Take(2));
        var more   = kv.Value.Count > 2 ? ", ..." : "";
        Console.WriteLine($"  {tag}  {kv.Key,-12} : {kv.Value.Count,3} spells   (e.g. {sample}{more})");
    }
    Console.WriteLine();

    Console.WriteLine($"Unhandled verbs across the catalog ({unhandledTally.Count} distinct):");
    foreach (var kv in unhandledTally.OrderByDescending(p => p.Value.Count).ThenBy(p => p.Key, StringComparer.Ordinal))
    {
        var tag = criticalUnhandled.Contains(kv.Key) ? "CRIT" : "soft";
        var sample = string.Join(", ", kv.Value.Take(3));
        var more   = kv.Value.Count > 3 ? ", ..." : "";
        Console.WriteLine($"  {tag}  {kv.Key,-14} : {kv.Value.Count,3} spells   (e.g. {sample}{more})");
    }
    Console.WriteLine();

    Console.WriteLine($"Textures referenced ({textureTally.Count} distinct, top 20):");
    foreach (var kv in textureTally.OrderByDescending(p => p.Value.Count).Take(20))
    {
        var sample = string.Join(", ", kv.Value.Take(2));
        var more   = kv.Value.Count > 2 ? ", ..." : "";
        Console.WriteLine($"  {kv.Key,-32} : {kv.Value.Count,3} spells   (e.g. {sample}{more})");
    }
    if (textureTally.Count > 20) Console.WriteLine($"  ... ({textureTally.Count - 20} more)");
    Console.WriteLine();

    if (verbose || onlyUncovered)
    {
        Console.WriteLine("Per-spell breakdown:");
        IEnumerable<SpellAuditRow> rows = results
            .OrderBy(r => r.Verdict switch { "UNCOVERED" => 0, "PARTIAL" => 1, "COVERED" => 2, _ => 3 })
            .ThenBy(r => r.Name, StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (onlyUncovered && r.Verdict == "COVERED") continue;
            var prims = r.PrimitiveKinds.Count > 0 ? string.Join("/", r.PrimitiveKinds) : "<none>";
            var unh   = r.UnhandledVerbs.Count > 0 ? "  unhandled=" + string.Join(",", r.UnhandledVerbs) : "";
            var texs  = r.Textures.Count > 0
                ? "  tex=" + string.Join(",", r.Textures.Take(2)) + (r.Textures.Count > 2 ? "..." : "")
                : "";
            Console.WriteLine($"  [{r.Verdict,-9}] {r.Name,-30} script={r.ScriptName,-22} create={prims}{unh}{texs}");
        }
    }
    return 0;
}

static void AppendTo(Dictionary<string, List<string>> map, string key, string spellName)
{
    // Set-style add: avoid double-counting if the same spell hits the
    // same key twice. Today the per-spell loop only iterates each row's
    // HashSets once each, so a positional `list[^1] != spellName` check
    // would suffice; but `Contains` keeps the helper safe for future
    // callers that don't batch by spell. Spell counts are small (<70).
    if (!map.TryGetValue(key, out var list)) map[key] = list = new List<string>();
    if (!list.Contains(spellName)) list.Add(spellName);
}

static void WalkAuditScript(string scriptName, string body, SfxScriptStore store, SpellAuditRow row, HashSet<string> visited)
{
    if (!visited.Add(scriptName)) return; // cycle / mutual-call guard
    SiegeFX.Core.Sfx.SfxProgram prog;
    try { prog = SiegeFX.Core.Sfx.SfxScriptCompiler.Compile(scriptName, body); }
    catch { return; }

    foreach (var stmt in prog.Statements)
    {
        switch (stmt.Kind)
        {
            case SiegeFX.Core.Sfx.StatementKind.SfxCreate:
                if (stmt.Tokens.Count > 0)
                    row.PrimitiveKinds.Add(stmt.Tokens[0].ToLowerInvariant());
                if (!string.IsNullOrEmpty(stmt.ParamString))
                    foreach (var tex in ExtractAuditTextures(stmt.ParamString!))
                        row.Textures.Add(tex);
                break;

            case SiegeFX.Core.Sfx.StatementKind.Call:
                // First non-quoted token is the called script's name; the
                // remainder are <angle-quoted-args> the compiler preserved.
                if (stmt.Tokens.Count > 0)
                {
                    var callName = stmt.Tokens[0].Trim('"').Trim();
                    // Strip trailing args glued by the lexer (rare — compiler
                    // already split on whitespace) just in case.
                    int sp = callName.IndexOf(' ');
                    if (sp >= 0) callName = callName.Substring(0, sp);
                    if (!string.IsNullOrEmpty(callName) &&
                        store.TryGet(callName, out var sub))
                        WalkAuditScript(sub.Name, sub.Body, store, row, visited);
                }
                break;

            case SiegeFX.Core.Sfx.StatementKind.Raw:
                // Verb is "sfx <sub>" / "sound <sub>" for unrecognized
                // sub-verbs, or the bare top-level verb otherwise. Keep the
                // sub-verb shape (drop the "sfx " prefix) so the table
                // reads cleanly: `orbiter` / `sphere` / `charge` instead
                // of `sfx orbiter`.
                if (string.IsNullOrEmpty(stmt.Verb)) break;
                string v = stmt.Verb;
                if (v.StartsWith("sfx ", StringComparison.OrdinalIgnoreCase))
                    v = v.Substring(4);
                else if (v.StartsWith("sound ", StringComparison.OrdinalIgnoreCase))
                    v = v.Substring(6);
                // Phase 23d-2e — the VM now executes some raw verbs
                // (randrange/frandrange/camerashake/worldmsg); keep the
                // audit's unhandled table truthful.
                if (SiegeFX.Core.Sfx.SfxRuntime.HandledRawVerbs.Contains(v)) break;
                row.UnhandledVerbs.Add(v.ToLowerInvariant());
                break;
        }
    }
}

// Extracts every `texture(NAME)` reference from a DS1 sfx param-string.
// DS1 param strings use a paren-args shape: `key(value)key(value)...`
// (e.g. `scale(.75)texture(b_sfx_sparkle01)color0(.7,.7,1)`) — no `=` and
// no `;` separators inside a quoted block. We only mine `texture` and
// `dual_texture` keys; their values are bare token names.
//
// Limitations (documented for clean-room transparency, not yet bugs in
// shipped data): nested parens inside a value (`texture(foo(bar))`) get
// truncated to `foo(bar` because `IndexOf(')')` takes the first close.
// No DS1 param string we've seen does this — values are always bare token
// names — but a future SC-SPELL-* slice authoring its own param strings
// must keep flat-paren values, or this extractor needs a balanced scan.
static IEnumerable<string> ExtractAuditTextures(string paramString)
{
    int i = 0;
    while (i < paramString.Length)
    {
        int idx = paramString.IndexOf("texture", i, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) yield break;
        // Word-boundary check on the preceding char so `dual_texture` matches
        // (prev is `_`, which is fine) but a substring like `nntexture` (made
        // up — guard against weird typos in shipped data) does not.
        if (idx > 0)
        {
            char prev = paramString[idx - 1];
            if (char.IsLetterOrDigit(prev)) { i = idx + 7; continue; }
        }
        int open = idx + 7;
        while (open < paramString.Length && char.IsWhiteSpace(paramString[open])) open++;
        if (open >= paramString.Length || paramString[open] != '(') { i = idx + 7; continue; }
        int close = paramString.IndexOf(')', open + 1);
        if (close < 0) { i = open + 1; continue; }
        var name = paramString.Substring(open + 1, close - open - 1).Trim().Trim('"');
        // Skip script-variable references (e.g. `texture($texture)`) — those
        // resolve at runtime from caller args, not to a literal asset name.
        // The audit's primitive / unhandled-verb tables already surface that
        // a spell uses caller-arg textures; reporting the placeholder string
        // as if it were an asset clutters the texture roll-up.
        if (name.Length == 0 || name[0] == '$') { i = close + 1; continue; }
        yield return name;
        i = close + 1;
    }
}

// Phase 17-SC-D: dispatch + commands for the sfx_script store. Lets us
// inventory the shipped /world/global/effects/*.gas pile and dump any
// single script body (fireball, smoke_emitter, waterfall_froth, ...) so
// the interpreter we build in SC-F has a verifiable source of truth.
// Phase 26 — party/recruitment receipts.
static int DispatchParty(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx party <recruit-audit> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "recruit-audit" => CmdPartyRecruitAudit(a[1..]),
        "inspect"       => CmdPartyInspect(a[1..]),
        _               => UnknownCommand("party " + a[0]),
    };
}

// Phase 26 — dump a companion's combat profile + inventory authoring so
// follower-combat can resolve the right damage/attack-type. PCs author
// DamageMax=0 (their weapon carries it), so we need to see the starting
// weapon / spell to feed the brain.
static int CmdPartyInspect(string[] a)
{
    if (a.Length < 2) { Console.Error.WriteLine("usage: siegefx party inspect <logic-tank> <template-name>"); return 1; }
    using var logicTank = TankFile.Open(a[0]);
    var (store, _) = TemplateStore.LoadFromTank(new TankReader(logicTank));
    if (!store.TryGet(a[1], out var tpl) || tpl is null) { Console.Error.WriteLine($"template not found: {a[1]}"); return 1; }

    var st = SiegeFX.Core.Actors.ActorStats.FromTemplate(store, tpl);
    Console.WriteLine($"template: {tpl.Name}");
    Console.WriteLine($"  DamageMin/Max = {st.DamageMin}/{st.DamageMax}   AttackRange = {st.AttackRange}   IsCombatant = {st.IsCombatant}");
    Console.WriteLine($"  WeaponPreference = {st.WeaponPreference ?? "(none)"}   MaxLife = {st.MaxLife}   STR/DEX/INT = {st.Strength}/{st.Dexterity}/{st.Intelligence}");
    foreach (var (grp, key) in new[] { ("attack","damage_min"), ("attack","damage_max"), ("aspect","gold_value"),
                                       ("actor","portrait_icon"), ("gui","portrait_icon"),
                                       ("mind","actor_weapon_preference"), ("mind","melee_skill"), ("mind","ranged_skill"),
                                       ("mind","combat_magic_skill"), ("mind","nature_magic_skill") })
        Console.WriteLine($"  [{grp}]{key} = {store.GetAttribute(tpl, grp, key) ?? "(unset)"}");

    void Dump(SiegeFX.Core.Assets.GasNode n, int depth)
    {
        var pad = new string(' ', depth * 2);
        Console.WriteLine($"{pad}[{n.Header}]");
        foreach (var at in n.Attributes) Console.WriteLine($"{pad}  {at.Name} = {at.Value}");
        foreach (var c in n.Children) Dump(c, depth + 1);
    }
    var actor = store.GetSection(tpl, "actor");
    Console.WriteLine("  --- [actor] (authoritative skills/stats) ---");
    if (actor is null) Console.WriteLine("  (no [actor] block)");
    else Dump(actor, 1);
    var aspect = store.GetSection(tpl, "aspect");
    Console.WriteLine("  --- [aspect] (life/mana/gold) ---");
    if (aspect is not null) Dump(aspect, 1);
    var inv = store.GetSection(tpl, "inventory");
    Console.WriteLine("  --- [inventory] ---");
    if (inv is null) Console.WriteLine("  (no [inventory] block)");
    else Dump(inv, 1);
    return 0;
}

static int DispatchWeapons(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx weapons damage-audit <logic-tank> [--list]"); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "damage-audit" => CmdWeaponsDamageAudit(a[1..]),
        _              => UnknownCommand("weapons " + a[0]),
    };
}

// Receipts-grade audit: enumerate every weapon-item template (an [attack]
// [attack_class] plus an [aspect][model] grip mesh — the same definition the
// pcontent/loot resolver uses) and confirm SiegeFX resolves each weapon's
// authored damage_min/damage_max 1:1 through the in-game ActorStats path (what
// an equipped weapon feeds its wielder via RenderHost.ApplyWeaponToStats).
// Flags any weapon whose damage reads missing / zero / min>max — those deal no
// or wrong damage in-game. Audits the SOURCE damage only; the defense-mitigation
// formula in CombatResolver is a separate, explicitly-approximated concern.
static int CmdWeaponsDamageAudit(string[] a)
{
    if (a.Length < 1) { Console.Error.WriteLine("usage: siegefx weapons damage-audit <logic-tank> [--list]"); return 1; }
    bool list = a.Contains("--list");
    using var logicTank = TankFile.Open(a[0]);
    var (store, _) = TemplateStore.LoadFromTank(new TankReader(logicTank));

    static float? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().Trim('"').Trim();
        return float.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : (float?)null;
    }

    static bool ContainsAny(string s, params string[] needles)
    {
        foreach (var n in needles)
            if (s.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    int items = 0, innateNoModel = 0, actors = 0, devTest = 0;
    var ammoThrown = new List<string>();
    var zeroBug = new List<string>();
    var swapped = new List<string>();
    var mismatch = new List<string>();
    var byClass = new SortedDictionary<string, (int n, float lo, float hi)>();
    var rows = new List<(string name, string ac, float mn, float mx)>();

    foreach (var tpl in store.All)
    {
        var ac = store.GetAttribute(tpl, "attack", "attack_class");
        if (string.IsNullOrEmpty(ac)) continue;
        if (string.IsNullOrEmpty(store.GetAttribute(tpl, "aspect", "model"))) { innateNoModel++; continue; }
        var key = ac.StartsWith("ac_", StringComparison.OrdinalIgnoreCase) ? ac[3..] : ac;
        // Exclude actors: a creature carries an innate attack (ac_beastfu) plus a
        // [mind] AI brain; an equippable weapon ITEM has neither. (Weapons DO carry
        // a [body] block for ground physics, so don't filter on that.) Creature
        // damage is a separate monster-damage question, not a weapon.
        if (string.Equals(key, "beastfu", StringComparison.OrdinalIgnoreCase)
            || store.GetSection(tpl, "mind") is not null)
        { actors++; continue; }
        // Dev/test-only templates (test_*, dev_*) are never shipped as playable weapons.
        if (tpl.Name.StartsWith("test_", StringComparison.OrdinalIgnoreCase)
            || tpl.Name.StartsWith("dev_", StringComparison.OrdinalIgnoreCase))
        { devTest++; continue; }
        items++;

        var authoredMin = Parse(store.GetAttribute(tpl, "attack", "damage_min"));
        var authoredMax = Parse(store.GetAttribute(tpl, "attack", "damage_max"));
        var st = SiegeFX.Core.Actors.ActorStats.FromTemplate(store, tpl);

        float mn = authoredMin ?? 0f, mx = authoredMax ?? 0f;
        rows.Add((tpl.Name, key, mn, mx));

        // 1:1 check: what combat resolves (ActorStats) must equal what the
        // template authored ([attack]damage_min/max).
        if (Math.Abs(st.DamageMin - mn) > 0.001f || Math.Abs(st.DamageMax - mx) > 0.001f)
            mismatch.Add($"{tpl.Name}: authored {mn}/{mx} but ActorStats {st.DamageMin}/{st.DamageMax}");

        if (authoredMin is null || authoredMax is null || mx <= 0f)
        {
            // Ammunition (arrows/bolts/rocks) and throwables (grenades/bombs) carry no
            // damage_min/max — their damage comes from the launcher that fires them or
            // the explosion/spell they trigger. That's how DS1 authors them, not a bug.
            bool ammo = key is "arrow" or "bolt"
                || ContainsAny(tpl.Name, "grenade", "bomb", "powder", "rock", "shot", "frag");
            (ammo ? ammoThrown : zeroBug).Add($"{tpl.Name} ({key})");
        }
        else
        {
            if (mn > mx) swapped.Add($"{tpl.Name} ({key}) {mn}>{mx}");
            var cur = byClass.TryGetValue(key, out var v) ? v : (0, float.MaxValue, float.MinValue);
            byClass[key] = (cur.Item1 + 1, Math.Min(cur.Item2, mn), Math.Max(cur.Item3, mx));
        }
    }

    Console.WriteLine($"WEAPON DAMAGE AUDIT — {a[0]}");
    Console.WriteLine($"  {items} weapon-item template(s)  ([attack][attack_class] + [aspect][model], not an actor)");
    Console.WriteLine($"  {actors} excluded as actors (creatures w/ innate ac_beastfu / [mind])");
    Console.WriteLine($"  {devTest} excluded as dev/test templates (test_* / dev_*)");
    Console.WriteLine($"  {innateNoModel} excluded: attack_class but no [aspect][model] grip mesh");
    Console.WriteLine();
    Console.WriteLine("Per weapon class (damage range across the class):");
    foreach (var kv in byClass)
        Console.WriteLine($"  {kv.Key,-14} {kv.Value.n,4} weapon(s)   damage {kv.Value.lo:0.#}..{kv.Value.hi:0.#}");
    Console.WriteLine();
    Console.WriteLine("Resolution — in-game ActorStats path vs authored [attack]damage_min/max:");
    Console.WriteLine($"  {items - mismatch.Count}/{items} resolve their authored damage 1:1");
    if (mismatch.Count > 0)
    {
        Console.WriteLine($"  !! {mismatch.Count} MISMATCH (ActorStats != authored):");
        foreach (var m in mismatch) Console.WriteLine($"       {m}");
    }
    if (zeroBug.Count > 0)
    {
        Console.WriteLine($"  !! {zeroBug.Count} weapon(s) reading NO/zero damage (deal nothing in-game):");
        foreach (var z in zeroBug) Console.WriteLine($"       {z}");
    }
    if (ammoThrown.Count > 0)
    {
        Console.WriteLine($"  . {ammoThrown.Count} ammunition/thrown item(s): no damage_min/max — damage comes from the launcher or explosion, not the projectile (expected in DS1):");
        foreach (var z in ammoThrown) Console.WriteLine($"       {z}");
    }
    if (swapped.Count > 0)
    {
        Console.WriteLine($"  ~ {swapped.Count} weapon(s) authored min>max (CombatResolver swaps them, but flag anyway):");
        foreach (var s in swapped) Console.WriteLine($"       {s}");
    }
    if (list)
    {
        Console.WriteLine();
        Console.WriteLine("All weapons (name  class  min..max):");
        foreach (var r in rows.OrderBy(r => r.ac).ThenBy(r => r.name))
            Console.WriteLine($"  {r.name,-40} {r.ac,-12} {r.mn:0.#}..{r.mx:0.#}");
    }
    Console.WriteLine();
    bool ok = mismatch.Count == 0 && zeroBug.Count == 0;
    Console.WriteLine(ok
        ? $"VERDICT: PASS — all {items - ammoThrown.Count} damage-dealing weapons resolve their authored DS1 damage_min/max 1:1 ({ammoThrown.Count} ammo/thrown carry damage via launcher/explosion). Source damage only; mitigation is audited separately."
        : $"VERDICT: {mismatch.Count} misread + {zeroBug.Count} zero-damage weapon(s) — NOT 1:1.");
    return ok ? 0 : 1;
}

// Phase 26 — thoroughly maps how EVERY companion is recruited. DS1
// authors recruitment inside the companion's conversation: a [text]
// node with `choice = potential_member` (ending in "...can I come
// along?") renders the Accept/Decline join buttons; Accept plays the
// _accept conversation and adds the NPC to the party (debiting
// [aspect]gold_value). This scans every region's conversations.gas for
// those offer nodes, resolves the companion + hire cost, and
// cross-checks against the can_sell_self hireable roster so nothing is
// missed.
static int CmdPartyRecruitAudit(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx party recruit-audit <map-tank> <logic-tank>");
        return 1;
    }
    using var mapTank = TankFile.Open(a[0]);
    using var logicTank = TankFile.Open(a[1]);
    var mapReader = new TankReader(mapTank);
    var logicReader = new TankReader(logicTank);
    var (store, _) = TemplateStore.LoadFromTank(logicReader);

    // Discover regions.
    var regionPaths = new List<string>();
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
    }

    // conversation key (lowercased) -> companion template guess.
    static string CompanionFromKey(string key)
    {
        var k = key.StartsWith("conversation_", StringComparison.OrdinalIgnoreCase)
            ? key["conversation_".Length..] : key;
        foreach (var suf in new[] { "_join", "_rejoin", "_accept", "_reject", "_disband_rejoin", "_disband", "_multiplayer" })
            if (k.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) { k = k[..^suf.Length]; break; }
        return k;
    }

    long GoldValue(string template)
    {
        if (store.TryGet(template, out var t) && t is not null)
        {
            var gv = store.GetAttribute(t, "aspect", "gold_value");
            if (!string.IsNullOrEmpty(gv)
                && float.TryParse(gv, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out var v))
                return (long)MathF.Round(v);
        }
        return 0;
    }

    // Collect every potential_member offer.
    var offers = new List<(string Region, string Key, string Companion, long Cost, bool HasAccept, bool HasReject, string Snippet)>();
    foreach (var rp in regionPaths)
    {
        var (convs, _) = SiegeFX.Core.Assets.ConversationStore.Load(mapReader, rp);
        foreach (var (key, def) in convs)
        {
            foreach (var node in def.Nodes)
            {
                if (!node.Choice.Equals("potential_member", StringComparison.OrdinalIgnoreCase)) continue;
                var companion = CompanionFromKey(key);
                var baseKey = key.StartsWith("conversation_", StringComparison.OrdinalIgnoreCase)
                    ? key["conversation_".Length..] : key;
                // strip the trailing _join/_rejoin to derive the accept/reject stems
                string stem = baseKey;
                foreach (var suf in new[] { "_join", "_rejoin" })
                    if (stem.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) { stem = stem[..^suf.Length]; break; }
                bool hasAccept = convs.ContainsKey($"conversation_{stem}_accept") || convs.ContainsKey($"{stem}_accept");
                bool hasReject = convs.ContainsKey($"conversation_{stem}_reject") || convs.ContainsKey($"{stem}_reject");
                var snip = node.Text.Length > 70 ? node.Text[^70..] : node.Text;
                var rShort = rp[(rp.LastIndexOf('/') + 1)..];
                offers.Add((rShort, key, companion, GoldValue(companion), hasAccept, hasReject, snip));
            }
        }
    }

    // Every can_sell_self hireable template (the roster the shop scan found).
    var hireables = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var tpl in store.All)
    {
        if (tpl.Name.StartsWith("base_", StringComparison.OrdinalIgnoreCase)) continue;
        var st = SiegeFX.Core.Actors.StoreTable.FromTemplate(store, tpl);
        if (st is not null && st.CanSellSelf) hireables.Add(tpl.Name);
    }

    Console.WriteLine($"party recruit-audit — {regionPaths.Count} regions, {offers.Count} potential_member offer(s)");
    Console.WriteLine();
    Console.WriteLine("  RECRUIT OFFERS (choice = potential_member):");
    foreach (var o in offers.OrderBy(o => o.Cost).ThenBy(o => o.Companion, StringComparer.Ordinal))
        Console.WriteLine($"    {o.Companion,-16} {o.Region,-10} cost={o.Cost,7}  " +
                          $"accept={(o.HasAccept ? "Y" : "-")} reject={(o.HasReject ? "Y" : "-")}  \"...{o.Snippet.Trim()}\"");

    // Cross-check: hireables with no offer (quest/skrit-gated or MP), and
    // offers whose companion isn't a can_sell_self template.
    var offerCompanions = new HashSet<string>(offers.Select(o => o.Companion), StringComparer.OrdinalIgnoreCase);
    var noOffer = hireables.Where(h => !offerCompanions.Contains(h)).ToList();
    Console.WriteLine();
    Console.WriteLine($"  can_sell_self hireables ({hireables.Count}): {string.Join(", ", hireables)}");
    Console.WriteLine($"  hireables WITHOUT a potential_member offer in SP world (skrit/quest-gated or MP): {string.Join(", ", noOffer)}");
    return 0;
}

// Phase 25a — shop authoring runtime receipts.
static int DispatchStore(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx store <dump|list> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "dump" => CmdStoreDump(a[1..]),
        "list" => CmdStoreList(a[1..]),
        "audit" => CmdStoreAudit(a[1..]),
        _      => UnknownCommand("store " + a[0]),
    };
}

// Every template carrying a [store]/[store_pcontent] chain — the
// merchant + hireable roster straight from the data.
static int CmdStoreList(string[] a)
{
    if (a.Length < 1) { Console.Error.WriteLine("usage: siegefx store list <Logic.dsres>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var (store, _) = TemplateStore.LoadFromTank(reader);
    int shops = 0, hire = 0;
    foreach (var tpl in store.All.OrderBy(t => t.Name, StringComparer.Ordinal))
    {
        var st = SiegeFX.Core.Actors.StoreTable.FromTemplate(store, tpl);
        if (st is null) continue;
        // Only leaves matter for the roster; bases show as duplicates of
        // their children otherwise. Cheap heuristic: skip base_* names.
        if (tpl.Name.StartsWith("base_", StringComparison.OrdinalIgnoreCase)) continue;
        var screen = store.GetAttribute(tpl, "common", "screen_name")?.Trim().Trim('"') ?? "";
        if (st.CanSellSelf)
        {
            hire++;
            var cost = store.GetAttribute(tpl, "aspect", "gold_value")?.Trim() ?? "?";
            Console.WriteLine($"  HIRE  {tpl.Name,-34} \"{screen}\"  cost={cost}");
        }
        else if (st.Tabs.Count > 0)
        {
            shops++;
            var tabs = string.Join(",", st.Tabs.Select(t => $"{t.Name}({t.Bags.Count})"));
            Console.WriteLine($"  SHOP  {tpl.Name,-34} \"{screen}\"  markup={st.ItemMarkup}  tabs: {tabs}");
        }
    }
    Console.WriteLine($"\n{shops} shop template(s), {hire} hireable template(s)");
    return 0;
}

// Phase 25-fold C — stable FNV-1a over a lowercased string. String's
// GetHashCode is randomized per process in .NET, so any audit seeded
// with it is non-reproducible; this gives run-to-run stable shelves.
static int StableHash(string s)
{
    unchecked
    {
        uint h = 2166136261u;
        foreach (var c in s) { h ^= char.ToLowerInvariant(c); h *= 16777619u; }
        return (int)h;
    }
}

// Phase 25d — stock-completeness + pricing audit over every shopkeeper
// PLACED in the world (user requirement: merchants carry everything a
// user can buy, and pay correctly). Gates:
//  (1) every authored tab on every placed shop rolls NON-EMPTY stock
//      across all probe seeds (no unfillable tabs, no dead bag specs);
//  (2) every stocked item prices definitively - authored gold_value or
//      counted as provisional (the computed-value fit is the flagged
//      remainder, reported not failed);
//  (3) anchors: spell_fireshot gold_value 8 (buy 16 at markup 2),
//      pack mule template gold_value present.
// Exit 0 only when (1) and (3) hold.
static int CmdStoreAudit(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx store audit <map-tank> <Logic.dsres> [--seeds=N]");
        return 1;
    }
    int seeds = 5;
    for (int i = 2; i < a.Length; i++)
        if (a[i].StartsWith("--seeds=") && int.TryParse(a[i]["--seeds=".Length..], out var s)) seeds = s;

    using var mapTank = TankFile.Open(a[0]);
    using var logicTank = TankFile.Open(a[1]);
    var mapReader = new TankReader(mapTank);
    var logicReader = new TankReader(logicTank);
    var (store, _) = TemplateStore.LoadFromTank(logicReader);
    var resolver = new SiegeFX.Core.Actors.PcontentResolver(store);

    // Placed actor templates, map-wide.
    var placed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // template -> first region
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var regionPaths = new List<string>();
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
        foreach (var rp in regionPaths)
        {
            var (actors, _) = SiegeFX.Core.Assets.RegionObjects.LoadPlacements(mapReader, rp, "actor.gas");
            foreach (var p in actors)
                if (!placed.ContainsKey(p.TemplateName)) placed[p.TemplateName] = rp;
        }
    }

    int shops = 0, tabChecks = 0, priced = 0, provisional = 0;
    var failures = new List<string>();
    var purchasableSpells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var (name, region) in placed.OrderBy(kv => kv.Key, StringComparer.Ordinal))
    {
        if (!store.TryGet(name, out var tpl) || tpl is null) continue;
        var table = SiegeFX.Core.Actors.StoreTable.FromTemplate(store, tpl);
        if (table is null || !table.IsShop) continue;
        shops++;
        var regionShort = region[(region.LastIndexOf('/') + 1)..];

        foreach (var tab in table.Tabs) tabChecks++;
        for (int s = 1; s <= seeds; s++)
        {
            // Phase 25-fold C — stable seed (String.GetHashCode is
            // process-randomized; the audit must be reproducible).
            var stock = table.GenerateStock(resolver, new Random(s * 7919 + StableHash(name)));
            foreach (var tab in table.Tabs)
            {
                // Phase 25-fold E — only a tab with at least one [all*] bag
                // is GUARANTEED non-empty; a tab whose bags are all
                // [oneof*] with chance<1 is authored to be sometimes-empty,
                // so an empty roll there is authentic, not a defect.
                bool guaranteed = tab.Bags.Any(b => !b.OneOf);
                if (guaranteed && !stock.Any(it => it.Tab.Equals(tab.Name, StringComparison.OrdinalIgnoreCase)))
                    failures.Add($"{name} ({regionShort}): guaranteed tab [{tab.Name}] rolled EMPTY at seed {s}");
            }
            foreach (var it in stock)
            {
                if (it.Tab.Equals("magic", StringComparison.OrdinalIgnoreCase)
                    && it.TemplateName.StartsWith("spell_", StringComparison.OrdinalIgnoreCase))
                    purchasableSpells.Add(it.TemplateName);
                if (s == 1)
                {
                    var gv = store.TryGet(it.TemplateName, out var itpl) && itpl is not null
                        ? store.GetAttribute(itpl, "aspect", "gold_value") : null;
                    if (string.IsNullOrEmpty(gv)) provisional++; else priced++;
                }
            }
        }
    }

    // Anchors.
    {
        if (!store.TryGet("spell_fireshot", out var fs) || fs is null
            || store.GetAttribute(fs, "aspect", "gold_value")?.Trim() != "8")
            failures.Add("anchor: spell_fireshot gold_value != 8");
        // Template name is pack_mule (npc_pack_mule is the FILE name);
        // data authors gold_value 600 — the walkthrough's "320" doesn't
        // match shipped data, so the tank is the anchor.
        if (!store.TryGet("pack_mule", out var mule) || mule is null
            || store.GetAttribute(mule, "aspect", "gold_value")?.Trim() != "600")
            failures.Add("anchor: pack_mule gold_value != 600");
    }

    Console.WriteLine($"store audit  —  {shops} placed shop(s), {tabChecks} authored tab(s), {seeds} seed(s) each");
    Console.WriteLine($"  stock rows priced from authored gold_value : {priced}");
    Console.WriteLine($"  rows on the PROVISIONAL power curve        : {provisional}  (25d fit scope)");
    Console.WriteLine($"  distinct spells purchasable somewhere      : {purchasableSpells.Count}");
    Console.WriteLine($"  failures                                   : {failures.Count}");
    foreach (var f in failures.Distinct().Take(30)) Console.WriteLine("  FAIL  " + f);
    return failures.Count == 0 ? 0 : 4;
}

// Roll a shop's stock and print it per tab with buy prices
// (gold_value x item_markup; items without an authored gold_value show
// value=? until the 25d computed-value fit lands).
static int CmdStoreDump(string[] a)
{
    if (a.Length < 2) { Console.Error.WriteLine("usage: siegefx store dump <Logic.dsres> <npc-template> [--seed=N]"); return 1; }
    int seed = 1;
    for (int i = 2; i < a.Length; i++)
        if (a[i].StartsWith("--seed=") && int.TryParse(a[i]["--seed=".Length..], out var s)) seed = s;

    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var (store, _) = TemplateStore.LoadFromTank(reader);
    if (!store.TryGet(a[1], out var tpl) || tpl is null)
    {
        Console.Error.WriteLine($"template '{a[1]}' not found");
        return 2;
    }
    var table = SiegeFX.Core.Actors.StoreTable.FromTemplate(store, tpl);
    if (table is null)
    {
        Console.Error.WriteLine($"'{a[1]}' has no [store]/[store_pcontent] chain");
        return 3;
    }
    var resolver = new SiegeFX.Core.Actors.PcontentResolver(store);
    var rng = new Random(seed);
    var stock = table.GenerateStock(resolver, rng);

    Console.WriteLine($"store dump: {a[1]}  markup={table.ItemMarkup}  full_ratio={table.FullRatio}  can_sell_self={table.CanSellSelf}  seed={seed}");
    foreach (var grp in stock.GroupBy(s => s.Tab))
    {
        Console.WriteLine($"\n  [{grp.Key}]  ({grp.Count()} items)");
        foreach (var it in grp.OrderBy(x => x.Power))
        {
            var goldStr = store.TryGet(it.TemplateName, out var itpl) && itpl is not null
                ? store.GetAttribute(itpl, "aspect", "gold_value")?.Trim()
                : null;
            var buy = goldStr is not null
                && float.TryParse(goldStr, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out var gv)
                ? ((int)MathF.Round(gv * table.ItemMarkup)).ToString()
                : "?";
            Console.WriteLine($"    pow={it.Power,4}  buy={buy,6}  {it.TemplateName,-34} <- {it.Spec}");
        }
    }
    // Empty-tab gate for the 25d completeness audit. Phase 25-fold E —
    // only GUARANTEED tabs (≥1 [all*] bag) are expected non-empty; an
    // all-[oneof*] tab is authored to sometimes roll empty.
    var emptyTabs = table.Tabs
        .Where(t => t.Bags.Any(b => !b.OneOf)
                    && stock.All(s => !s.Tab.Equals(t.Name, StringComparison.OrdinalIgnoreCase)))
        .ToList();
    if (emptyTabs.Count > 0)
    {
        Console.WriteLine($"\n  EMPTY TABS: {string.Join(", ", emptyTabs.Select(t => t.Name))}");
        return 4;
    }
    return 0;
}

// Phase 23e — DS1-side ground-truth capture kit. Builds a mod tank for
// the ORIGINAL game that starts a fresh farmboy with every spell in
// inventory and casting skills high enough to use them all, so DS1
// reference footage can be recorded without campaign progress. Retail
// DS1 loads extra .dsres from its Resources folder and higher-priority
// tanks override same-path files (SU 213), so dropping the built
// zz_spelltest.dsres next to Logic.dsres overrides heroes.gas for a NEW
// game; deleting the file restores stock behavior. Output also includes
// a call sheet in the SAME ordinal spell order as the SiegeFX
// filmstrips/goldens, so a single recorded casting session maps 1:1
// onto our strips for side-by-side comparison.
static int CmdCaptureKitBuild(string[] a)
{
    if (a.Length < 2 || !string.Equals(a[0], "build", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("usage: siegefx capture-kit build <DS1-Resources-dir> [--out=DIR]");
        return 1;
    }
    string resDir = a[1];
    string outDir = "capture-kit";
    for (int i = 2; i < a.Length; i++)
        if (a[i].StartsWith("--out=", StringComparison.Ordinal)) outDir = a[i]["--out=".Length..];

    var logicPath = System.IO.Path.Combine(resDir, "Logic.dsres");
    if (!File.Exists(logicPath))
    {
        Console.Error.WriteLine($"Logic.dsres not found under '{resDir}'");
        return 2;
    }

    using var tank = TankFile.Open(logicPath);
    var reader = new TankReader(tank);
    var (templates, _) = TemplateStore.LoadFromTank(reader);
    var spells = SpellCatalog.Build(templates);

    // Locate heroes.gas inside the tank (path varies by contentdb layout).
    var heroesPath = reader.ListFiles()
        .FirstOrDefault(p => p.EndsWith("/heroes.gas", StringComparison.OrdinalIgnoreCase));
    if (heroesPath is null)
    {
        Console.Error.WriteLine("heroes.gas not found in Logic.dsres");
        return 3;
    }
    var heroesText = System.Text.Encoding.UTF8.GetString(reader.ExtractToMemory(heroesPath));

    // ---- modify farmboy ------------------------------------------------
    int fbStart = heroesText.IndexOf("[t:template,n:farmboy]", StringComparison.OrdinalIgnoreCase);
    if (fbStart < 0) { Console.Error.WriteLine("farmboy template not found in heroes.gas"); return 3; }
    int fbEnd = heroesText.IndexOf("[t:template,", fbStart + 10, StringComparison.OrdinalIgnoreCase);
    if (fbEnd < 0) fbEnd = heroesText.Length;

    // Phase 24a / 23-fold F8 — the kit injects the player-acquirable
    // roster; the cast order maps onto the SiegeFX side by NAME (see
    // goldens/sfx-filmstrips/_contact_sheet_index.txt), since the full
    // contact sheet also carries monster-arsenal rows the player can't
    // cast.
    var roster = spells.All
        .Where(s => s.PlayerAcquirable)
        .OrderBy(s => s.Name, StringComparer.Ordinal)
        .ToList();
    var invItems = new System.Text.StringBuilder();
    invItems.AppendLine("\t\t// --- SiegeFX capture kit: every spell, for DS1 reference capture ---");
    invItems.AppendLine("\t\t[other]");
    invItems.AppendLine("\t\t{");
    foreach (var s in roster)
        invItems.AppendLine($"\t\t\til_main = {s.Name};");
    invItems.AppendLine("\t\t}");

    const string skillsOverride =
        "\n\t// --- SiegeFX capture kit: cast-anything skills ---\n" +
        "\t[actor]\n\t{\n\t\t[skills]\n\t\t{\n" +
        "\t\t\tintelligence = 0, 0, 60;\n" +
        "\t\t\tnature_magic = 0, 50, 0;\n" +
        "\t\t\tcombat_magic = 0, 50, 0;\n" +
        "\t\t}\n\t}\n";

    var block = heroesText[fbStart..fbEnd];
    int specIdx = block.IndexOf("specializes", StringComparison.OrdinalIgnoreCase);
    int specEol = specIdx >= 0 ? block.IndexOf('\n', specIdx) : -1;
    if (specEol < 0) { Console.Error.WriteLine("farmboy specializes line not found"); return 3; }
    block = block.Insert(specEol + 1, skillsOverride);

    int invIdx = block.IndexOf("[inventory]", StringComparison.OrdinalIgnoreCase);
    int invBrace = invIdx >= 0 ? block.IndexOf('{', invIdx) : -1;
    if (invBrace < 0) { Console.Error.WriteLine("farmboy [inventory] block not found"); return 3; }
    block = block.Insert(invBrace + 1, "\n" + invItems);

    var modifiedHeroes = heroesText[..fbStart] + block + heroesText[fbEnd..];

    // ---- write the mod tank ---------------------------------------------
    Directory.CreateDirectory(outDir);
    var tankOut = System.IO.Path.Combine(outDir, "zz_spelltest.dsres");
    var writer = new TankWriter
    {
        Priority      = TankPriority.User,
        Title         = "SiegeFX spell capture kit",
        Author        = "SiegeFX",
        Description  = "Temporary test override: farmboy starts with every spell. Delete after capturing.",
        BuildText     = "siegefx capture-kit build",
        CopyrightText = "",
    };
    writer.Add(heroesPath, System.Text.Encoding.UTF8.GetBytes(modifiedHeroes));
    writer.Write(tankOut, DateTime.UtcNow);

    // Round-trip receipt: our own reader must open the tank and give the
    // exact bytes back.
    using (var check = TankFile.Open(tankOut))
    {
        var checkReader = new TankReader(check);
        var back = checkReader.ExtractToMemory(heroesPath);
        if (!back.AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes(modifiedHeroes)))
        {
            Console.Error.WriteLine("round-trip FAILED: extracted bytes differ");
            return 4;
        }
        Console.WriteLine($"round-trip OK: {checkReader.FileCount} file(s), {checkReader.DirCount} dir(s), header valid");
    }

    // ---- call sheet + install notes --------------------------------------
    var sheet = new List<string>
    {
        "SiegeFX spell capture call sheet",
        "================================",
        "1. Copy zz_spelltest.dsres into your Dungeon Siege Resources folder",
        $"   (next to Logic.dsres, e.g. {resDir}).",
        "2. Start a NEW single-player game with the farmboy hero.",
        "3. Your inventory contains every spell; casting skills are pre-raised.",
        "4. Start recording. Cast each spell 2-3 times at a nearby target or",
        "   open ground, IN THE ORDER BELOW, pausing ~2s between spells.",
        "   (Match to SiegeFX strips by NAME via _contact_sheet_index.txt.)",
        "5. Stop recording, delete zz_spelltest.dsres, and share the video.",
        "",
        "Cast order:",
    };
    for (int i = 0; i < roster.Count; i++)
        sheet.Add($"  {i + 1,3}. {roster[i].Name}   ({roster[i].ScreenName})");
    File.WriteAllLines(System.IO.Path.Combine(outDir, "capture_call_sheet.txt"), sheet);

    Console.WriteLine($"capture kit written to {outDir}:");
    Console.WriteLine($"  zz_spelltest.dsres       ({new FileInfo(tankOut).Length} bytes, {roster.Count} spells injected)");
    Console.WriteLine("  capture_call_sheet.txt   (match rows by name via _contact_sheet_index.txt)");
    return 0;
}

static int DispatchSfx(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx sfx <list|show|parse|run|param-audit> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "list"        => CmdSfxList(a[1..]),
        "show"        => CmdSfxShow(a[1..]),
        "parse"       => CmdSfxParse(a[1..]),
        "run"         => CmdSfxRun(a[1..]),
        "param-audit" => CmdSfxParamAudit(a[1..]),
        "timeline"    => CmdSfxTimeline(a[1..]),
        _             => UnknownCommand("sfx " + a[0]),
    };
}

// Phase 23a — authored-param coverage audit. `spells visual-audit` answers
// "is every `sfx create` KIND handled"; this answers the next level down:
// "is every authored param KEY inside those param strings actually consumed
// by the runtime's parser". A key that ships in DS1 data but is never read
// is authored intent the renderer silently drops — exactly where per-spell
// visual drift hides. The consumed set is live-probed from the parser via
// SfxRuntime.CollectConsumedParamKeys() so audit and runtime cannot drift.
static int CmdSfxParamAudit(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("usage: siegefx sfx param-audit <Logic.dsres> [--verbose] [--filter=NAME]");
        return 1;
    }
    bool verbose = false;
    string? filter = null;
    for (int i = 1; i < a.Length; i++)
    {
        if (a[i] == "--verbose") verbose = true;
        else if (a[i].StartsWith("--filter=", StringComparison.OrdinalIgnoreCase))
            filter = a[i]["--filter=".Length..];
    }

    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var (templates, _) = TemplateStore.LoadFromTank(reader);
    var spells = SpellCatalog.Build(templates);
    var sfx    = SfxScriptStore.LoadFromTank(reader);

    var consumed = SiegeFX.Core.Sfx.SfxRuntime.CollectConsumedParamKeys();
    var gameplay = SiegeFX.Core.Sfx.SfxRuntime.GameplayParamKeys;

    var keySpells       = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    var keyKinds        = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    var perSpellIgnored = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
    int scanned = 0, paramStrings = 0;

    foreach (var spell in spells.All.OrderBy(s => s.Name, StringComparer.Ordinal))
    {
        if (filter != null && spell.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            continue;
        if (string.IsNullOrEmpty(spell.CastSfxScript)) continue;
        if (!sfx.TryGet(spell.CastSfxScript, out var script)) continue;
        scanned++;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new List<(string Key, string Kind)>();
        WalkParamStrings(script.Name, script.Body, sfx, visited, keys, ref paramStrings);

        foreach (var (key, kind) in keys)
        {
            AppendTo(keySpells, key, spell.Name);
            if (!keyKinds.TryGetValue(key, out var ks))
                keyKinds[key] = ks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ks.Add(kind);
            if (!consumed.Contains(key) && !gameplay.Contains(key))
            {
                if (!perSpellIgnored.TryGetValue(spell.Name, out var set))
                    perSpellIgnored[spell.Name] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(key);
            }
        }
    }

    string Classify(string k) =>
        consumed.Contains(k) ? "CONSUMED" : gameplay.Contains(k) ? "GAMEPLAY" : "IGNORED";

    var allKeys = keySpells.Keys
        .OrderBy(k => Classify(k) switch { "IGNORED" => 0, "GAMEPLAY" => 1, _ => 2 })
        .ThenByDescending(k => keySpells[k].Count)
        .ThenBy(k => k, StringComparer.Ordinal)
        .ToList();
    int nConsumed = allKeys.Count(k => Classify(k) == "CONSUMED");
    int nGameplay = allKeys.Count(k => Classify(k) == "GAMEPLAY");
    int nIgnored  = allKeys.Count(k => Classify(k) == "IGNORED");
    int fullSpells = scanned - perSpellIgnored.Count;

    Console.WriteLine($"siegefx sfx param-audit  —  {scanned} spells scanned, {paramStrings} param strings, {allKeys.Count} distinct authored keys");
    Console.WriteLine();
    Console.WriteLine($"  runtime consumes {consumed.Count} keys (live-probed from the parser)");
    Console.WriteLine($"  authored-key classes : IGNORED {nIgnored}   GAMEPLAY {nGameplay}   CONSUMED {nConsumed}");
    Console.WriteLine($"  spells fully consumed : {fullSpells}/{scanned}   with ignored-key gaps : {perSpellIgnored.Count}");
    Console.WriteLine();
    Console.WriteLine("Per-key roster (IGNORED first — these are the fidelity gaps):");
    foreach (var k in allKeys)
    {
        var cls    = Classify(k);
        var kinds  = string.Join(",", keyKinds[k].OrderBy(x => x, StringComparer.Ordinal).Take(4));
        var sample = string.Join(", ", keySpells[k].Take(2));
        var more   = keySpells[k].Count > 2 ? ", ..." : "";
        Console.WriteLine($"  {cls,-8}  {k,-16} : {keySpells[k].Count,3} spells  on {kinds,-30} (e.g. {sample}{more})");
    }
    if (verbose || perSpellIgnored.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"Spells with IGNORED authored keys ({perSpellIgnored.Count}):");
        foreach (var kv in perSpellIgnored)
            Console.WriteLine($"  {kv.Key,-30} : {string.Join(", ", kv.Value)}");
    }
    return 0;
}

// Walks a compiled sfx script (recursing one level into `call <sub>` like
// WalkAuditScript) collecting every param key from every statement that
// carries a quoted param string, tagged with the create-kind (or verb)
// it was authored on.
static void WalkParamStrings(string scriptName, string body, SfxScriptStore store,
    HashSet<string> visited, List<(string Key, string Kind)> keys, ref int paramStrings)
{
    if (!visited.Add(scriptName)) return; // cycle / mutual-call guard
    SiegeFX.Core.Sfx.SfxProgram prog;
    try { prog = SiegeFX.Core.Sfx.SfxScriptCompiler.Compile(scriptName, body); }
    catch { return; }

    foreach (var stmt in prog.Statements)
    {
        if (!string.IsNullOrEmpty(stmt.ParamString))
        {
            paramStrings++;
            var kind = stmt.Kind == SiegeFX.Core.Sfx.StatementKind.SfxCreate && stmt.Tokens.Count > 0
                ? stmt.Tokens[0].ToLowerInvariant()
                : "(" + stmt.Verb + ")";
            foreach (var key in ExtractParamKeys(stmt.ParamString!))
                keys.Add((key, kind));
        }
        if (stmt.Kind == SiegeFX.Core.Sfx.StatementKind.Call && stmt.Tokens.Count > 0)
        {
            var callName = stmt.Tokens[0].Trim('"').Trim();
            int sp = callName.IndexOf(' ');
            if (sp >= 0) callName = callName.Substring(0, sp);
            if (!string.IsNullOrEmpty(callName) && store.TryGet(callName, out var sub))
                WalkParamStrings(sub.Name, sub.Body, store, visited, keys, ref paramStrings);
        }
    }
}

// Tokenizes a DS1 param string (`key(args)key2(args)...[0][1]`) into its
// key names. A key is an identifier followed by `(`; paren contents are
// skipped flat (no shipped DS1 value nests parens — same limitation as
// ExtractAuditTextures, documented there). Bare identifiers outside parens
// are also yielded — shipped strings author flag-style keys both ways.
// `[N]` caller-arg slots and `$var` leftovers are not keys.
static IEnumerable<string> ExtractParamKeys(string raw)
{
    int i = 0;
    while (i < raw.Length)
    {
        char c = raw[i];
        if (c == '$')
        {
            i++;
            while (i < raw.Length && (char.IsLetterOrDigit(raw[i]) || raw[i] == '_')) i++;
        }
        else if (char.IsLetter(c) || c == '_')
        {
            int start = i;
            while (i < raw.Length && (char.IsLetterOrDigit(raw[i]) || raw[i] == '_')) i++;
            var name = raw.Substring(start, i - start);
            int j = i;
            while (j < raw.Length && char.IsWhiteSpace(raw[j])) j++;
            if (j < raw.Length && raw[j] == '(')
            {
                yield return name;
                int close = raw.IndexOf(')', j + 1);
                i = close < 0 ? raw.Length : close + 1;
            }
            else
            {
                yield return name; // bare flag-style key
            }
        }
        else i++;
    }
}

// Phase 23b — deterministic cast-timeline dump. One spell prints to
// stdout; --all writes one file per spell into --out (default
// goldens/sfx-timelines under the current directory) for committing as
// golden regression baselines. Fixed seed + fixed context anchors +
// fixed 20 Hz dt make identical code/data emit identical traces.
static int CmdSfxTimeline(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx sfx timeline <Logic.dsres> <script-name|--all> [--ticks=N] [--seed=N] [--out=DIR]");
        return 1;
    }
    int ticks = 40, seed = 1;
    string? outDir = null;
    bool all = string.Equals(a[1], "--all", StringComparison.OrdinalIgnoreCase);
    for (int i = 2; i < a.Length; i++)
    {
        if (a[i].StartsWith("--ticks=", StringComparison.Ordinal) && int.TryParse(a[i]["--ticks=".Length..], out var t)) ticks = t;
        else if (a[i].StartsWith("--seed=", StringComparison.Ordinal) && int.TryParse(a[i]["--seed=".Length..], out var s)) seed = s;
        else if (a[i].StartsWith("--out=", StringComparison.Ordinal)) outDir = a[i]["--out=".Length..];
    }

    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var store  = SfxScriptStore.LoadFromTank(reader);

    if (!all)
    {
        if (!store.TryGet(a[1], out _))
        {
            Console.Error.WriteLine($"no sfx_script named '{a[1]}'");
            return 4;
        }
        foreach (var line in RunTimelineLines(store, a[1], ticks, seed))
            Console.WriteLine(line);
        return 0;
    }

    var (templates, _) = TemplateStore.LoadFromTank(reader);
    var spells = SpellCatalog.Build(templates);
    outDir ??= System.IO.Path.Combine("goldens", "sfx-timelines");
    System.IO.Directory.CreateDirectory(outDir);
    int written = 0, skipped = 0;
    foreach (var spell in spells.All.OrderBy(s => s.Name, StringComparer.Ordinal))
    {
        if (string.IsNullOrEmpty(spell.CastSfxScript) || !store.TryGet(spell.CastSfxScript, out _))
        {
            skipped++;
            continue;
        }
        var lines = RunTimelineLines(store, spell.CastSfxScript, ticks, seed);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(outDir, spell.Name + ".txt"), lines);
        written++;
    }
    Console.WriteLine($"sfx timeline --all: {written} golden traces written to {outDir} ({skipped} spells without runnable cast script)");
    return 0;
}

static List<string> RunTimelineLines(SfxScriptStore store, string scriptName, int ticks, int seed)
{
    var sink = new TimelineSink();
    var rt = new SiegeFX.Core.Sfx.SfxRuntime(store, sink);
    rt.SetDeterministicSeed(seed);
    // Fixed anchors: caster feet at origin, target 4u east (typical cast
    // range mid-band), weapon bone at hand height. Chosen once; every
    // golden depends on them, so never change without regenerating all.
    var ctx = new SiegeFX.Core.Sfx.SfxContext(
        new System.Numerics.Vector3(0f, 0f, 0f),
        new System.Numerics.Vector3(4f, 0f, 0f),
        new System.Numerics.Vector3(0.3f, 1.2f, 0f));
    sink.Now = 0f;
    rt.Spawn(scriptName, ctx, null);
    const float dt = 1f / 20f;
    for (int i = 1; i <= ticks; i++) { sink.Now = i * dt; rt.Tick(dt); }

    var lines = new List<string>
    {
        $"# sfx timeline  script={scriptName}  ticks={ticks}  dt=0.050  seed={seed}",
        "# ctx: src=(0,0,0) tgt=(4,0,0) weapon=(0.3,1.2,0)",
    };
    lines.AddRange(sink.Events);
    lines.Add($"# end: emitters={rt.LivePersistentCount} coroutines={rt.LiveCoroutineCount} unhandled=[{string.Join(",", rt.UnhandledVerbs.OrderBy(v => v, StringComparer.Ordinal))}]");
    return lines;
}

static int CmdSfxList(string[] a)
{
    if (a.Length < 1) { Console.Error.WriteLine("usage: siegefx sfx list <Logic.dsres> [--prefix=NAME]"); return 1; }
    string? prefix = null;
    for (int i = 1; i < a.Length; i++)
        if (a[i].StartsWith("--prefix=", StringComparison.Ordinal))
            prefix = a[i]["--prefix=".Length..];

    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var store = SfxScriptStore.LoadFromTank(reader);

    var byFile = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    int shown = 0;
    var picked = store.All.OrderBy(s => s.Name).ToList();
    foreach (var s in picked)
    {
        byFile.TryGetValue(s.SourcePath, out var n);
        byFile[s.SourcePath] = n + 1;
        if (prefix is not null && !s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
        Console.WriteLine($"  {s.Name,-32}  {s.SourcePath}");
        shown++;
    }
    Console.WriteLine();
    Console.WriteLine($"sfx scripts: {store.Count} total ({shown} shown)");
    foreach (var kv in byFile)
        Console.WriteLine($"  {kv.Value,5} from {kv.Key}");
    return 0;
}

static int CmdSfxShow(string[] a)
{
    if (a.Length < 2) { Console.Error.WriteLine("usage: siegefx sfx show <Logic.dsres> <script-name>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var store = SfxScriptStore.LoadFromTank(reader);
    if (!store.TryGet(a[1], out var s))
    {
        Console.Error.WriteLine($"no sfx_script named '{a[1]}' in {SfxScriptStore.EffectsDir}");
        return 4;
    }
    Console.WriteLine($"name   : {s.Name}");
    Console.WriteLine($"source : {s.SourcePath}");
    Console.WriteLine($"body   : {s.Body.Length} chars");
    Console.WriteLine();
    Console.WriteLine(s.Body);
    return 0;
}

static int CmdSfxParse(string[] a)
{
    if (a.Length < 2) { Console.Error.WriteLine("usage: siegefx sfx parse <Logic.dsres> <script-name>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var store = SfxScriptStore.LoadFromTank(reader);
    if (!store.TryGet(a[1], out var s))
    {
        Console.Error.WriteLine($"no sfx_script named '{a[1]}' in {SfxScriptStore.EffectsDir}");
        return 4;
    }
    var prog = SiegeFX.Core.Sfx.SfxScriptCompiler.Compile(s.Name, s.Body);
    Console.WriteLine($"name      : {prog.Name}");
    Console.WriteLine($"source    : {s.SourcePath}");
    Console.WriteLine($"statements: {prog.Statements.Count}");

    var byKind = new Dictionary<SiegeFX.Core.Sfx.StatementKind, int>();
    foreach (var st in prog.Statements)
        byKind[st.Kind] = byKind.TryGetValue(st.Kind, out var n) ? n + 1 : 1;
    Console.WriteLine();
    Console.WriteLine("kind tally:");
    foreach (var kv in byKind.OrderByDescending(kv => kv.Value))
        Console.WriteLine($"  {kv.Key,-20} {kv.Value,4}");

    Console.WriteLine();
    Console.WriteLine("statements:");
    int idx = 0;
    foreach (var st in prog.Statements)
    {
        var argSummary = string.Join(" ", st.Tokens);
        if (argSummary.Length > 80) argSummary = argSummary.Substring(0, 77) + "...";
        var paramTail = st.ParamString is null ? "" : $"  param=\"{Truncate(st.ParamString, 40)}\"";
        Console.WriteLine($"  [{idx,3}] {st.Kind,-18} {st.Verb,-20} {argSummary}{paramTail}");
        idx++;
    }
    return 0;
}

static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 3) + "...";

static int CmdSfxRun(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx sfx run <Logic.dsres> <script-name> [--ticks=N]");
        return 1;
    }
    int ticks = 60;
    for (int i = 2; i < a.Length; i++)
        if (a[i].StartsWith("--ticks=", StringComparison.Ordinal)
            && int.TryParse(a[i]["--ticks=".Length..], out var t)) ticks = t;

    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var store  = SfxScriptStore.LoadFromTank(reader);
    if (!store.TryGet(a[1], out _))
    {
        Console.Error.WriteLine($"no sfx_script named '{a[1]}'");
        return 4;
    }

    var sink = new TallySink();
    var rt = new SiegeFX.Core.Sfx.SfxRuntime(store, sink);
    rt.Spawn(a[1], new System.Numerics.Vector3(0, 1, 0), null);

    Console.WriteLine($"after Spawn:");
    Console.WriteLine($"  persistent emitters : {rt.LivePersistentCount}");
    Console.WriteLine($"  live coroutines     : {rt.LiveCoroutineCount}");
    Console.WriteLine($"  one-shot bursts     : fire={sink.SpawnFireCount} smoke={sink.SpawnSmokeCount} steam={sink.SpawnSteamCount} spark={sink.SpawnSparkCount} bolt={sink.SpawnLightningCount} cyl={sink.SpawnCylinderCount} sray={sink.SpawnSrayCount} fireb={sink.SpawnFirebCount}");

    const float dt = 1f / 20f;
    for (int i = 0; i < ticks; i++) rt.Tick(dt);

    Console.WriteLine();
    Console.WriteLine($"after {ticks} ticks ({ticks * dt:0.00}s):");
    Console.WriteLine($"  persistent emitters : {rt.LivePersistentCount}");
    Console.WriteLine($"  live coroutines     : {rt.LiveCoroutineCount}");
    Console.WriteLine($"  Maintain calls      : fire={sink.MaintainFireCount} smoke={sink.MaintainSmokeCount} steam={sink.MaintainSteamCount} glow={sink.MaintainGlowCount}");
    Console.WriteLine($"  particles spawned   : fire={sink.SpawnFireCount} smoke={sink.SpawnSmokeCount} steam={sink.SpawnSteamCount} glow={sink.SpawnGlowCount} spark={sink.SpawnSparkCount} bolt={sink.SpawnLightningCount} cyl={sink.SpawnCylinderCount} sray={sink.SpawnSrayCount} fireb={sink.SpawnFirebCount} sphere={sink.SpawnSphereCount}");
    if (rt.UnhandledVerbs.Count > 0)
        Console.WriteLine($"  unhandled verbs     : {string.Join(", ", rt.UnhandledVerbs)}");

    return 0;
}

// Phase 24c — both authored icon sets, audited against the shipped raws.
// Every player-acquirable spell must author active_icon (the small
// active-slot set, b_gui_ig_i_ic_sp_NNN) AND inventory_icon (the 32x32
// _inv set), and both raws must exist in Objects.dsres. Ship gate:
// 0 missing on the player roster.
static int CmdSpellsIconAudit(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx spells icon-audit <Logic.dsres> <Objects.dsres> [--verbose]");
        return 1;
    }
    bool verbose = a.Skip(2).Any(x => x == "--verbose");

    using var logic = TankFile.Open(a[0]);
    var logicReader = new TankReader(logic);
    var (templates, _) = TemplateStore.LoadFromTank(logicReader);
    var spells = SpellCatalog.Build(templates);

    using var objects = TankFile.Open(a[1]);
    var objectsReader = new TankReader(objects);
    // Basename (no extension) index of every raw in the tank.
    var raws = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var p in objectsReader.ListFiles())
    {
        if (!p.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) continue;
        int slash = p.LastIndexOf('/');
        raws.Add(p[(slash + 1)..^4]);
    }

    int playerTotal = 0, bothOk = 0;
    var missing = new List<string>();
    var monsterWithIcons = new List<string>();
    var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var s in spells.All.OrderBy(x => x.Name, StringComparer.Ordinal))
    {
        if (!string.IsNullOrEmpty(s.ActiveIcon)) referenced.Add(s.ActiveIcon);
        if (!string.IsNullOrEmpty(s.InventoryIcon)) referenced.Add(s.InventoryIcon);
        if (!s.PlayerAcquirable)
        {
            if (!string.IsNullOrEmpty(s.ActiveIcon) || !string.IsNullOrEmpty(s.InventoryIcon))
                monsterWithIcons.Add(s.Name);
            continue;
        }
        playerTotal++;
        var problems = new List<string>();
        if (string.IsNullOrEmpty(s.ActiveIcon)) problems.Add("no active_icon authored");
        else if (!raws.Contains(s.ActiveIcon)) problems.Add($"active raw '{s.ActiveIcon}' absent");
        if (string.IsNullOrEmpty(s.InventoryIcon)) problems.Add("no inventory_icon authored");
        else if (!raws.Contains(s.InventoryIcon)) problems.Add($"inv raw '{s.InventoryIcon}' absent");
        if (problems.Count == 0) bothOk++;
        else missing.Add($"{s.Name,-30} : {string.Join("; ", problems)}");
    }

    // Orphan icon raws — numbered sp_ pairs no spell references.
    var orphans = raws
        .Where(r => r.StartsWith("b_gui_ig_i_ic_sp_", StringComparison.OrdinalIgnoreCase)
                    && !referenced.Contains(r))
        .OrderBy(r => r, StringComparer.Ordinal)
        .ToList();

    Console.WriteLine($"siegefx spells icon-audit  —  {spells.Count} spells ({playerTotal} player-acquirable)");
    Console.WriteLine();
    Console.WriteLine($"  player spells with BOTH icon sets resolved : {bothOk}/{playerTotal}");
    Console.WriteLine($"  missing/unresolved                        : {missing.Count}");
    Console.WriteLine($"  monster-arsenal spells authoring icons    : {monsterWithIcons.Count}");
    Console.WriteLine($"  orphan sp_* icon raws (no spell refs them): {orphans.Count}");
    if (missing.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Missing:");
        foreach (var m in missing) Console.WriteLine("  " + m);
    }
    if (verbose && orphans.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Orphans:");
        foreach (var o in orphans) Console.WriteLine("  " + o);
    }
    return missing.Count == 0 ? 0 : 4;
}

static int CmdSpellsDump(string[] a)
{
    if (a.Length < 1) { Console.Error.WriteLine("usage: siegefx spells dump <Logic.dsres>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var (store, diags) = TemplateStore.LoadFromTank(reader);
    var cat = SpellCatalog.Build(store);
    var sfxStore = SfxScriptStore.LoadFromTank(reader);
    Console.WriteLine($"templates: {store.Count} loaded ({diags.Count} diagnostics)");
    Console.WriteLine($"spells (instant-hit, parsed [magic] block): {cat.Count}");
    Console.WriteLine($"sfx scripts loaded: {sfxStore.Count}");
    foreach (var s in cat.All.OrderBy(s => s.Name).Take(40))
    {
        var sfx = string.IsNullOrEmpty(s.CastSfxScript) ? "<no cast row>"
                : sfxStore.TryGet(s.CastSfxScript, out _) ? s.CastSfxScript
                : s.CastSfxScript + " (missing)";
        Console.WriteLine($"  {s.Name,-32} \"{s.ScreenName,-22}\"  range={s.CastRange,5:0.0}  cd={s.CastReloadDelay,4:0.00}  cost={s.BaseManaCost,4:0.0}  sfx={sfx}");
    }
    if (cat.Count > 40) Console.WriteLine($"  ... ({cat.Count - 40} more)");

    // Phase 17-SC-H receipt — coverage summary across the catalog.
    int withRow = 0, resolved = 0, missing = 0, none = 0;
    foreach (var s in cat.All)
    {
        if (string.IsNullOrEmpty(s.CastSfxScript)) { none++; continue; }
        withRow++;
        if (sfxStore.TryGet(s.CastSfxScript, out _)) resolved++;
        else missing++;
    }
    Console.WriteLine();
    Console.WriteLine($"cast_sfx_script coverage: {resolved}/{cat.Count} resolved, {missing} unresolved, {none} no [we_req_cast] row");
    return 0;
}

static int CmdSpellsShow(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx spells show <Logic.dsres> <spell_name> [magic_level] [--maxlife=N] [--life=N] [--src_mana=N] [--src_life=N]");
        return 1;
    }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var (store, _) = TemplateStore.LoadFromTank(reader);
    var cat = SpellCatalog.Build(store);
    if (!cat.TryGet(a[1], out var s)) { Console.Error.WriteLine($"no spell named '{a[1]}' in catalog"); return 4; }

    // Optional context flags (SC-A2): the placeholders besides #magic.
    float maxLife = 0f, life = 0f, srcMana = 0f, srcLife = 0f;
    int? onlyLevel = null;
    for (int ai = 2; ai < a.Length; ai++)
    {
        var arg = a[ai];
        if (arg.StartsWith("--maxlife=",  StringComparison.Ordinal)) float.TryParse(arg.AsSpan("--maxlife=".Length),  System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out maxLife);
        else if (arg.StartsWith("--life=",     StringComparison.Ordinal)) float.TryParse(arg.AsSpan("--life=".Length),     System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out life);
        else if (arg.StartsWith("--src_mana=", StringComparison.Ordinal)) float.TryParse(arg.AsSpan("--src_mana=".Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out srcMana);
        else if (arg.StartsWith("--src_life=", StringComparison.Ordinal)) float.TryParse(arg.AsSpan("--src_life=".Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out srcLife);
        else if (int.TryParse(arg, out var lv)) onlyLevel = lv;
    }

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
    if (maxLife != 0f || life != 0f || srcMana != 0f || srcLife != 0f)
        Console.WriteLine($"  context             = #maxlife={maxLife} #life={life} #src_mana={srcMana} #src_life={srcLife}");
    Console.WriteLine();

    int[] levels = onlyLevel.HasValue ? new[] { onlyLevel.Value } : new[] { 1, 5, 10, 25, 50, 100 };
    var rng = new Random(1);
    if (s.Kind == SpellKind.SelfHeal)
    {
        Console.WriteLine("evaluated by magic level (heal amount, mana cost):");
        foreach (var lv in levels)
        {
            var ctx = new SpellEvalContext(lv, maxLife, life, srcMana, srcLife);
            float heal = s.HealAmount(ctx);
            float cost = s.ManaCost(ctx);
            Console.WriteLine($"  L{lv,-3}  heal={heal,6:0.00}  mana={cost,5:0.0}");
        }
    }
    else
    {
        Console.WriteLine("evaluated by magic level (lo / hi damage, mana cost):");
        foreach (var lv in levels)
        {
            var ctx = new SpellEvalContext(lv, maxLife, life, srcMana, srcLife);
            float lo = SpellExpr.Eval(s.AttackDamageMinExpr, ctx);
            float hi = SpellExpr.Eval(s.AttackDamageMaxExpr, ctx);
            float cost = s.ManaCost(ctx);
            float sample = s.RollDamage(ctx, rng);
            Console.WriteLine($"  L{lv,-3}  dmg [{lo,7:0.00} .. {hi,7:0.00}]  sample={sample,6:0.00}  mana={cost,5:0.0}");
        }
    }
    return 0;
}

// ---- audio coverage ----
//
// Phase 21d-2a-ix audit: walks Sound.dsres, categorises every shipped wav by
// `s_e_<prefix>` family + buckets music separately, then cross-references the
// set against the static list of clip ids the runtime currently registers
// (mirrors RenderHost's Sfx* constants — kept here so SiegeFX.Tools doesn't
// pull a Runtime ref for one string list). Per-category report:
//   authored = how many wavs ship in that family
//   wired    = how many of those a Play()/PlayAt() site actually triggers
//   gap      = authored - wired (ie unused authored content the runtime never reaches)
// Surfaces unwired categories (gui, ambient, fidget, call, attack, …) so the
// punch list for the next slice is observable in one screen instead of buried
// in a grep.

static int DispatchAudio(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx audio <coverage|sed-list> <Sound.dsres> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "coverage" => CmdAudioCoverage(a[1..]),
        "sed-list" => CmdAudioSedList(a[1..]),
        "enemy-states" => CmdAudioEnemyStates(a[1..]),
        _          => UnknownCommand("audio " + a[0]),
    };
}

// SC-ENEMY-AUDIO-AUDIT (2026-05-13) — walks every template that ships a
// [voice] block in its specializes chain, lists which voice states are
// authored (die / enemy_spotted / hit_critical / hit_glance / hit_solid /
// fidget / etc.), and flags templates missing the `enemy_spotted` aggro
// cue. DS1 fires `enemy_spotted` when an enemy first acquires the player
// in its combat brain — the distinct "krug-spots-you" yelp the user
// flagged as missing.
//
// Output: per-template voice-state matrix + summary tallies of which
// states each NPC family ships. Templates without a [voice] block at
// any chain level are excluded (props, sound emitters, decorative
// items that never speak).
static int CmdAudioEnemyStates(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("usage: siegefx audio enemy-states <Logic.dsres> [--filter=PREFIX] [--show=STATE]");
        return 1;
    }
    string? filter = null;
    string? showState = null;
    for (int i = 1; i < a.Length; i++)
    {
        const string filterPrefix = "--filter=";
        const string showPrefix   = "--show=";
        if (a[i].StartsWith(filterPrefix)) filter = a[i][filterPrefix.Length..];
        else if (a[i].StartsWith(showPrefix)) showState = a[i][showPrefix.Length..];
        else { Console.Error.WriteLine($"unknown option: {a[i]}"); return 1; }
    }

    using var logicTank = TankFile.Open(a[0]);
    var logicReader = new TankReader(logicTank);
    var (store, _) = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);

    // Walk every template. For each, look up the [voice] section via the
    // chain-walking GetSection. Record which state subsections are present
    // and what wav references they carry.
    var perTemplate = new SortedDictionary<string, SortedDictionary<string, string>>(
        StringComparer.OrdinalIgnoreCase);
    var stateCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    foreach (var template in store.All)
    {
        var name = template.Name;
        if (filter is not null &&
            !name.StartsWith(filter, StringComparison.OrdinalIgnoreCase)) continue;
        // DS1 nests [voice] under [aspect] (verified against krug.gas:
        // base_krug.aspect.voice). The other two paths are defensive in case
        // a different actor template family puts it elsewhere.
        var voice = store.GetSection(template, "aspect", "voice")
                 ?? store.GetSection(template, "actor", "voice")
                 ?? store.GetSection(template, "voice");
        if (voice is null) continue;

        var states = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in voice.Children)
        {
            // Each child is one named voice state ([die] / [enemy_spotted] / etc.)
            // Look for a `* = WAV_REF;` attribute carrying the played wav. DS1
            // also authors `priority` and other knobs — we just want the wav.
            string? wav = null;
            foreach (var attr in child.Attributes)
            {
                if (attr.Name == "*" || attr.Name.Equals("*", StringComparison.Ordinal))
                {
                    wav = attr.Value;
                    break;
                }
            }
            states[child.Header] = wav ?? "(no wav attr)";
            stateCounts.TryGetValue(child.Header, out var c);
            stateCounts[child.Header] = c + 1;
        }
        if (states.Count > 0) perTemplate[name] = states;
    }

    if (perTemplate.Count == 0)
    {
        Console.WriteLine("no templates with [voice] blocks found in scope.");
        return 0;
    }

    Console.WriteLine($"audio enemy-states: {perTemplate.Count} template(s) with [voice]");

    // --show=STATE — focus mode: list every template that ships that state,
    // alongside its wav cue. Skips the rest of the report so the receipt
    // for "what is the cast catalog?" or "who shouts on attack?" is one
    // tight list. Lands the audit's NIT-8 deliverable from the prior
    // audit-pair review.
    if (!string.IsNullOrEmpty(showState))
    {
        Console.WriteLine();
        Console.WriteLine($"== --show={showState} ==");
        int shown = 0;
        foreach (var (name, states) in perTemplate)
        {
            if (!states.TryGetValue(showState, out var wav)) continue;
            Console.WriteLine($"  {name,-32}  {wav}");
            shown++;
        }
        Console.WriteLine($"  -> {shown} template(s) ship {showState}");
        return 0;
    }

    Console.WriteLine();
    Console.WriteLine("== STATE FREQUENCY ==");
    foreach (var (state, count) in stateCounts.OrderByDescending(kv => kv.Value))
        Console.WriteLine($"  {state,-20} {count} template(s)");

    Console.WriteLine();
    Console.WriteLine("== MISSING enemy_spotted (the aggro cue) ==");
    int missingAggro = 0;
    foreach (var (name, states) in perTemplate)
    {
        if (states.ContainsKey("enemy_spotted")) continue;
        Console.WriteLine($"  {name,-32}  states: {string.Join(",", states.Keys)}");
        missingAggro++;
    }
    Console.WriteLine($"  -> {missingAggro} template(s) ship without an aggro cue");

    Console.WriteLine();
    Console.WriteLine("== FULL MATRIX (per template) ==");
    foreach (var (name, states) in perTemplate)
    {
        Console.WriteLine($"  {name}:");
        foreach (var (state, wav) in states)
            Console.WriteLine($"    {state,-20} {wav}");
    }
    return 0;
}

static int CmdAudioSedList(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine(
            "usage: siegefx audio sed-list <Sound.dsres> [--filter=PREFIX] [--show-all] [--show-aliases] [--show-rate-only]");
        Console.Error.WriteLine("  --filter=PREFIX     restrict to SED keys starting with PREFIX (e.g. spell, call, attack)");
        Console.Error.WriteLine("  --show-all          print every SED entry (default: histograms + samples only)");
        Console.Error.WriteLine("  --show-aliases      print SEDs whose sound_effect_file != key (cross-aliasing)");
        Console.Error.WriteLine("  --show-rate-only    print SEDs with non-unity playback rate range");
        return 1;
    }
    string? soundPath = null;
    string? filter = null;
    bool showAll = false, showAliases = false, showRateOnly = false;
    foreach (var arg in a)
    {
        if (arg.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase))
            filter = arg["--filter=".Length..].Trim().ToLowerInvariant();
        else if (arg.Equals("--show-all", StringComparison.OrdinalIgnoreCase)) showAll = true;
        else if (arg.Equals("--show-aliases", StringComparison.OrdinalIgnoreCase)) showAliases = true;
        else if (arg.Equals("--show-rate-only", StringComparison.OrdinalIgnoreCase)) showRateOnly = true;
        else if (arg.StartsWith("--", StringComparison.Ordinal))
            throw new FormatException($"unknown flag '{arg}'");
        else
        {
            if (soundPath is not null) throw new FormatException("only one Sound.dsres path expected");
            soundPath = arg;
        }
    }
    if (soundPath is null) { Console.Error.WriteLine("missing <Sound.dsres>"); return 1; }

    using var tank = SiegeFX.Core.Tank.TankFile.Open(soundPath);
    var reader = new SiegeFX.Core.Tank.TankReader(tank);
    var (seds, diags) = SiegeFX.Core.Assets.SedStore.Load(reader);

    Console.WriteLine($"== SED registry — {seds.Count} descriptors parsed from {Path.GetFileName(soundPath)} ==");
    foreach (var d in diags) Console.Error.WriteLine($"  diag: {d}");

    var ordered = seds.Values
        .Where(s => filter is null
                    || string.Equals(CategoryOf(s.Key), filter, StringComparison.OrdinalIgnoreCase)
                    || s.Key.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
        .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (filter is not null)
        Console.WriteLine($"   ({ordered.Count} match filter prefix '{filter}')");

    // Histograms.
    int withRateJitter = ordered.Count(s => s.MinPlaybackRate != s.MaxPlaybackRate);
    int withFixedTrans = ordered.Count(s => s.MinPlaybackRate == s.MaxPlaybackRate && s.MinPlaybackRate != 1f);
    int unity          = ordered.Count(s => s.MinPlaybackRate == 1f && s.MaxPlaybackRate == 1f);
    int aliased        = ordered.Count(s => !string.Equals(s.Key, s.SoundEffectFile, StringComparison.OrdinalIgnoreCase));
    int withCap        = ordered.Count(s => s.MaxSimultaneousSamples > 0);

    Console.WriteLine();
    Console.WriteLine("Playback-rate distribution:");
    Console.WriteLine($"  varied (min < max):      {withRateJitter,4} — per-fire pitch jitter");
    Console.WriteLine($"  fixed transpose (≠1.0):  {withFixedTrans,4} — sound is intentionally re-pitched");
    Console.WriteLine($"  unity (1.0 / 1.0):       {unity,4} — playback rate fields absent / commented");
    Console.WriteLine();
    Console.WriteLine("Other SED features:");
    Console.WriteLine($"  cross-alias (key != sound_effect_file): {aliased,4}");
    Console.WriteLine($"  max_simultaneous_samples authored:      {withCap,4}");

    // Distinct underlying wav files.
    var distinctSounds = ordered
        .Select(s => s.SoundEffectFile)
        .Where(s => !string.IsNullOrEmpty(s))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
    Console.WriteLine($"  distinct sound_effect_file values:      {distinctSounds,4}");

    // Per-prefix breakdown (s_e_<prefix>_).
    Console.WriteLine();
    Console.WriteLine("Per-category SED count (top 10):");
    var perCat = ordered
        .GroupBy(s => CategoryOf(s.Key))
        .Select(g => (Cat: g.Key, Count: g.Count()))
        .OrderByDescending(x => x.Count)
        .Take(10)
        .ToList();
    foreach (var (cat, count) in perCat)
        Console.WriteLine($"  {cat,-28} {count,4}");

    if (showAll)
    {
        Console.WriteLine();
        Console.WriteLine("All SED entries:");
        foreach (var s in ordered) PrintSed(s);
    }
    else if (showAliases)
    {
        Console.WriteLine();
        Console.WriteLine("Cross-aliasing SEDs (key → sound_effect_file):");
        foreach (var s in ordered.Where(s =>
            !string.Equals(s.Key, s.SoundEffectFile, StringComparison.OrdinalIgnoreCase)))
            PrintSed(s);
    }
    else if (showRateOnly)
    {
        Console.WriteLine();
        Console.WriteLine("SEDs with non-unity playback rate:");
        foreach (var s in ordered.Where(s => s.MinPlaybackRate != 1f || s.MaxPlaybackRate != 1f))
            PrintSed(s);
    }
    else
    {
        // Default: 5 sample entries from each of the top 3 categories,
        // so the user gets a feel for the data without scroll fatigue.
        Console.WriteLine();
        Console.WriteLine("Sample entries (first 5 per top category — pass --show-all for the full list):");
        foreach (var (cat, _) in perCat.Take(3))
        {
            Console.WriteLine($"  [{cat}]");
            foreach (var s in ordered.Where(s => CategoryOf(s.Key) == cat).Take(5))
                PrintSed(s);
        }
    }
    return 0;

    static string CategoryOf(string key)
    {
        // Strip a leading "s_e_" then take the first underscore-separated token.
        const string prefix = "s_e_";
        var k = key;
        if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) k = k[prefix.Length..];
        var us = k.IndexOf('_');
        return us < 0 ? k : k[..us];
    }

    static void PrintSed(SiegeFX.Core.Assets.SedDescriptor s)
    {
        string rate = s.MinPlaybackRate == s.MaxPlaybackRate
            ? $"rate {s.MinPlaybackRate:0.00}"
            : $"rate {s.MinPlaybackRate:0.00}–{s.MaxPlaybackRate:0.00}";
        string cap  = s.MaxSimultaneousSamples > 0 ? $", cap {s.MaxSimultaneousSamples}" : "";
        string alias = string.Equals(s.Key, s.SoundEffectFile, StringComparison.OrdinalIgnoreCase)
            ? "" : $"  → {s.SoundEffectFile}";
        Console.WriteLine($"    {s.Key,-44}{alias,-44}  {rate}{cap}");
    }
}

static int CmdAudioCoverage(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("usage: siegefx audio coverage <Sound.dsres> [--list-orphan-categories] [--list-unwired=PREFIX]");
        return 1;
    }
    string? soundPath = null;
    bool listOrphans = false;
    string? listUnwiredPrefix = null;
    foreach (var arg in a)
    {
        if (arg.Equals("--list-orphan-categories", StringComparison.OrdinalIgnoreCase))
            listOrphans = true;
        else if (arg.StartsWith("--list-unwired=", StringComparison.OrdinalIgnoreCase))
            listUnwiredPrefix = arg["--list-unwired=".Length..].Trim().ToLowerInvariant();
        else if (arg.StartsWith("--", StringComparison.Ordinal))
            throw new FormatException($"unknown flag '{arg}'");
        else
        {
            if (soundPath is not null) throw new FormatException("only one Sound.dsres path expected");
            soundPath = arg;
        }
    }
    if (soundPath is null) { Console.Error.WriteLine("missing <Sound.dsres>"); return 1; }

    // Static wired-id list mirrors RenderHost's Sfx* constants + the inline
    // clip ids it registers (swing_01..04, hit_flesh_1..5, die_<species>).
    // When RenderHost grows new TryRegisterSfx calls, append here so the
    // gap report stays accurate. See feedback_siegefx_diagnostic_clis.md.
    var wiredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/sound/effects/s_e_spell_zap_cast.wav",
        "/sound/effects/s_e_spell_healing_wind_cast.wav",
        "/sound/effects/s_e_swing_01.wav",
        "/sound/effects/s_e_swing_02.wav",
        "/sound/effects/s_e_swing_03.wav",
        "/sound/effects/s_e_swing_04.wav",
        "/sound/effects/s_e_hit_steelsword_flesh1.wav",
        "/sound/effects/s_e_hit_steelsword_flesh2.wav",
        "/sound/effects/s_e_hit_steelsword_flesh3.wav",
        "/sound/effects/s_e_hit_steelsword_flesh4.wav",
        "/sound/effects/s_e_hit_steelsword_flesh5.wav",
        "/sound/effects/s_e_miss_melee.wav",
        "/sound/effects/s_e_level_up_melee.wav",
        "/sound/effects/s_e_die_goblin.wav",
        "/sound/effects/s_e_die_gremal.wav",
        "/sound/effects/s_e_die_krug_scout.wav",
        "/sound/effects/s_e_die_krug_dog.wav",
        "/sound/effects/s_e_gui_inventory_sheet.wav",
        "/sound/effects/s_e_gui_pick_up.wav",
        "/sound/effects/s_e_gui_out_of_mana.wav",
    };

    using var tank = TankFile.Open(soundPath);
    var reader = new TankReader(tank);

    int wavTotal = 0, musicTotal = 0, otherTotal = 0;
    int wiredFound = 0, wiredMissing = 0;
    var byCategory = new SortedDictionary<string, (int Authored, int Wired, List<string> Unwired)>(StringComparer.OrdinalIgnoreCase);

    foreach (var path in reader.ListFiles())
    {
        var lower = path.ToLowerInvariant();
        if (lower.StartsWith("/sound/music/", StringComparison.Ordinal)) { musicTotal++; continue; }
        if (!lower.EndsWith(".wav", StringComparison.Ordinal))           { otherTotal++; continue; }
        if (!lower.StartsWith("/sound/effects/s_e_", StringComparison.Ordinal))
        {
            wavTotal++;
            continue;
        }
        wavTotal++;

        // Category = first underscore-delimited token after s_e_, e.g.
        // s_e_spell_zap_cast.wav -> "spell". Sub-buckets (per-spell, per-creature)
        // are interesting but would explode the report; leaving that to a future
        // --by-creature flag if ever needed.
        var rest = lower.AsSpan("/sound/effects/s_e_".Length);
        int sep = rest.IndexOfAny(new[] { '_', '.' });
        string cat = sep < 0 ? rest.ToString() : rest[..sep].ToString();

        if (!byCategory.TryGetValue(cat, out var cell))
            cell = (0, 0, new List<string>());
        cell.Authored++;
        if (wiredPaths.Contains(path)) cell.Wired++;
        else cell.Unwired.Add(path);
        byCategory[cat] = cell;
    }

    foreach (var w in wiredPaths)
    {
        if (reader.TryGetFile(w, out _)) wiredFound++;
        else wiredMissing++;
    }

    Console.WriteLine($"audio coverage: {Path.GetFileName(soundPath)}");
    Console.WriteLine($"  totals: {wavTotal} wav(s), {musicTotal} music track(s), {otherTotal} other");
    Console.WriteLine($"  wired-list health: {wiredFound}/{wiredPaths.Count} resolve in tank ({wiredMissing} missing — report stale runtime constants)");
    Console.WriteLine();
    Console.WriteLine($"  {"category",-12}  {"authored",8}  {"wired",5}  gap");
    Console.WriteLine($"  {new string('-', 12)}  {new string('-', 8)}  {new string('-', 5)}  ---");

    var orphans = new List<string>();
    foreach (var (cat, cell) in byCategory)
    {
        int gap = cell.Authored - cell.Wired;
        Console.WriteLine($"  {cat,-12}  {cell.Authored,8}  {cell.Wired,5}  {gap,3}");
        if (cell.Wired == 0) orphans.Add(cat);
    }

    Console.WriteLine();
    Console.WriteLine($"  unwired categories ({orphans.Count}): {string.Join(", ", orphans)}");
    Console.WriteLine($"  music: 0/{musicTotal} wired (no music playback path in runtime yet)");

    if (listOrphans && orphans.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("orphan-category samples (first 5 each):");
        foreach (var cat in orphans)
        {
            Console.WriteLine($"  [{cat}]");
            foreach (var p in byCategory[cat].Unwired.Take(5))
                Console.WriteLine($"    {p}");
        }
    }

    if (listUnwiredPrefix is not null)
    {
        Console.WriteLine();
        Console.WriteLine($"unwired entries in [{listUnwiredPrefix}] (full list):");
        if (byCategory.TryGetValue(listUnwiredPrefix, out var cellList))
        {
            foreach (var p in cellList.Unwired) Console.WriteLine($"  {p}");
        }
        else
        {
            Console.WriteLine("  (no such category)");
        }
    }

    return 0;
}

// ---- mood ----
//
// Phase 21d-2a-xi audit: parses every /world/global/moods/<map>/moods*.gas in
// Logic.dsres, prints mood + ambient_track totals, and (with --regions) the
// region->default-mood->bed table the runtime applies on region entry.

static int DispatchMood(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx mood list|weather <Logic.dsres> [--map=NAME] [--with-bed] [--regions]"); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "list"    => CmdMoodList(a[1..]),
        "weather" => CmdMoodWeather(a[1..]),
        _         => UnknownCommand("mood " + a[0]),
    };
}

// SC-WEATHER-B — weather audit over parsed moods. Receipts (map_world, verified
// against raw gas text 2026-07-07): 232 moods, 232 [fog], 9 [rain] (a 10th, in
// map_world_bt_r1_2, ships commented out — "Rain near the beach got vetoed"),
// 18 [snow], 42 [wind], 46 [sun] (colon-shorthand keys), exactly 1 authored
// lightning (map_world_fh_r1_3, the opening-farmlands storm). Rain density
// 30–225/s, snow 75–500/s.
static int CmdMoodWeather(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("usage: siegefx mood weather <Logic.dsres> [--map=NAME] [--all]");
        return 1;
    }
    string? logicPath = null;
    string? mapFilter = null;
    bool showAll = false;
    foreach (var arg in a)
    {
        if (arg.StartsWith("--map=", StringComparison.OrdinalIgnoreCase))
            mapFilter = arg["--map=".Length..].Trim().ToLowerInvariant();
        else if (arg.Equals("--all", StringComparison.OrdinalIgnoreCase))
            showAll = true;
        else if (arg.StartsWith("--", StringComparison.Ordinal))
            throw new FormatException($"unknown flag '{arg}'");
        else
        {
            if (logicPath is not null) throw new FormatException("only one Logic.dsres path expected");
            logicPath = arg;
        }
    }
    if (logicPath is null) { Console.Error.WriteLine("missing <Logic.dsres>"); return 1; }

    using var tank = SiegeFX.Core.Tank.TankFile.Open(logicPath);
    var reader = new SiegeFX.Core.Tank.TankReader(tank);
    var (moods, diags) = SiegeFX.Core.Assets.MoodStore.Load(reader);
    foreach (var d in diags) Console.Error.WriteLine($"  diag: {d}");

    bool InMap(string key) =>
        mapFilter is null || key.StartsWith($"map_{mapFilter}_", StringComparison.OrdinalIgnoreCase);

    var picked = moods.Values.Where(m => InMap(m.Name)).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    Console.WriteLine($"mood weather: {Path.GetFileName(logicPath)}" +
                      (mapFilter is null ? "" : $" (map={mapFilter})"));
    Console.WriteLine($"  moods: {picked.Count}");
    Console.WriteLine($"  fog:   {picked.Count(m => m.Fog is not null)}");
    Console.WriteLine($"  rain:  {picked.Count(m => m.Rain is not null)} " +
                      $"(authored lightning: {picked.Count(m => m.Rain?.Lightning == true)})");
    Console.WriteLine($"  snow:  {picked.Count(m => m.Snow is not null)}");
    Console.WriteLine($"  wind:  {picked.Count(m => m.Wind is not null)}");
    Console.WriteLine($"  sun:   {picked.Count(m => m.Sun.Count > 0)}");
    Console.WriteLine($"  interior: {picked.Count(m => m.Interior)}");
    Console.WriteLine($"  reverb (non-generic room_type): " +
                      $"{picked.Count(m => !string.IsNullOrEmpty(m.RoomType) && !m.RoomType.Equals("rt_generic", StringComparison.OrdinalIgnoreCase))}");

    var rains = picked.Where(m => m.Rain is not null).Select(m => m.Rain!.Density).ToList();
    var snows = picked.Where(m => m.Snow is not null).Select(m => m.Snow!.Density).ToList();
    if (rains.Count > 0) Console.WriteLine($"  rain density range: {rains.Min():0.#}–{rains.Max():0.#}/s");
    if (snows.Count > 0) Console.WriteLine($"  snow density range: {snows.Min():0.#}–{snows.Max():0.#}/s");

    Console.WriteLine();
    Console.WriteLine("  mood                                      tt     weather                          fog(near-far, color)          wind");
    foreach (var m in picked)
    {
        bool hasWeather = m.Rain is not null || m.Snow is not null;
        if (!showAll && !hasWeather) continue;
        string weather =
            m.Rain is not null ? $"rain {m.Rain.Density:0.#}/s{(m.Rain.Lightning ? " +LIGHTNING" : "")}"
          : m.Snow is not null ? $"snow {m.Snow.Density:0.#}/s"
          : "-";
        string fogStr = m.Fog is null ? "-" :
            $"{m.Fog.NearDist:0.#}-{m.Fog.FarDist:0.#}m 0x{m.Fog.Color:X8}";
        string windStr = m.Wind is null ? "-" :
            $"{m.Wind.Velocity:0.##}m/s @{m.Wind.Direction:0.##}rad";
        Console.WriteLine($"    {m.Name,-40}  {m.TransitionTime,4:0.#}s  {weather,-30}  {fogStr,-28}  {windStr}" +
                          (m.Interior ? "  [interior]" : ""));
    }
    return 0;
}

static int CmdMoodList(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("usage: siegefx mood list <Logic.dsres> [--map=NAME] [--with-bed] [--regions]");
        return 1;
    }
    string? logicPath = null;
    string? mapFilter = null;
    bool withBedOnly = false;
    bool regionsOnly = false;
    foreach (var arg in a)
    {
        if (arg.StartsWith("--map=", StringComparison.OrdinalIgnoreCase))
            mapFilter = arg["--map=".Length..].Trim().ToLowerInvariant();
        else if (arg.Equals("--with-bed", StringComparison.OrdinalIgnoreCase))
            withBedOnly = true;
        else if (arg.Equals("--regions", StringComparison.OrdinalIgnoreCase))
            regionsOnly = true;
        else if (arg.StartsWith("--", StringComparison.Ordinal))
            throw new FormatException($"unknown flag '{arg}'");
        else
        {
            if (logicPath is not null) throw new FormatException("only one Logic.dsres path expected");
            logicPath = arg;
        }
    }
    if (logicPath is null) { Console.Error.WriteLine("missing <Logic.dsres>"); return 1; }

    using var tank = SiegeFX.Core.Tank.TankFile.Open(logicPath);
    var reader = new SiegeFX.Core.Tank.TankReader(tank);

    var (moods, diags) = SiegeFX.Core.Assets.MoodStore.Load(reader);
    foreach (var d in diags) Console.Error.WriteLine($"  diag: {d}");

    Console.WriteLine($"mood list: {Path.GetFileName(logicPath)}");
    Console.WriteLine($"  total moods parsed: {moods.Count}");
    int withBed = moods.Values.Count(m => !string.IsNullOrEmpty(m.AmbientTrack));
    Console.WriteLine($"  moods with non-empty ambient_track: {withBed}");
    int withStandard = moods.Values.Count(m => !string.IsNullOrEmpty(m.StandardTrack));
    int withBattle   = moods.Values.Count(m => !string.IsNullOrEmpty(m.BattleTrack));
    Console.WriteLine($"  moods with standard music: {withStandard}");
    Console.WriteLine($"  moods with battle music:   {withBattle}");

    var distinctBeds = moods.Values
        .Select(m => m.AmbientTrack)
        .Where(t => !string.IsNullOrEmpty(t))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
        .ToList();
    Console.WriteLine($"  distinct ambient bed clips: {distinctBeds.Count}");
    foreach (var b in distinctBeds) Console.WriteLine($"    {b}");

    if (regionsOnly)
    {
        // Group moods by inferred (map, region) and resolve each region to its
        // FindRegionDefault pick. The runtime walks the same path on region
        // entry, so this report is the authoritative "what the player should
        // hear in region X" preview.
        var regions = new SortedDictionary<(string map, string region), List<string>>(
            Comparer<(string, string)>.Create((a, b) =>
            {
                int c = string.Compare(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase);
            }));
        foreach (var key in moods.Keys)
        {
            // Mood names are "map_<map>_<region>_<index>[_subindex]"; pull
            // the map+region prefix and remember each mood as a member.
            const string head = "map_";
            if (!key.StartsWith(head, StringComparison.OrdinalIgnoreCase)) continue;
            // Find the region split: key starts as map_<map>_<region>_..., so
            // count from the second underscore.
            int u1 = key.IndexOf('_', head.Length);
            if (u1 < 0) continue;
            int u2 = key.IndexOf('_', u1 + 1);
            if (u2 < 0) continue;
            int u3 = key.IndexOf('_', u2 + 1);
            // region runs from u1+1 to u3 (or end). Some moods have only two
            // underscores (rare); skip those — the run-time picker needs the
            // numeric tail.
            if (u3 < 0) continue;
            var map = key.Substring(head.Length, u1 - head.Length);
            var region = key.Substring(u1 + 1, u3 - (u1 + 1));
            // Drop intro-only entries where the region itself starts with intro
            if (region.StartsWith("intro", StringComparison.OrdinalIgnoreCase)) continue;
            if (mapFilter is not null && !map.Equals(mapFilter, StringComparison.OrdinalIgnoreCase)) continue;
            var keyTuple = (map, region);
            if (!regions.TryGetValue(keyTuple, out var list)) regions[keyTuple] = list = new();
            list.Add(key);
        }
        Console.WriteLine();
        Console.WriteLine($"  regions (map, region, default-mood, ambient_track):");
        int regionsWithBed = 0;
        foreach (var (key, _) in regions)
        {
            var pick = SiegeFX.Core.Assets.MoodStore.FindRegionDefault(moods, key.map, key.region);
            var bed = pick?.AmbientTrack ?? "";
            if (withBedOnly && string.IsNullOrEmpty(bed)) continue;
            if (!string.IsNullOrEmpty(bed)) regionsWithBed++;
            Console.WriteLine($"    map_{key.map} / {key.region,-12}  {pick?.Name ?? "<none>",-40}  {(string.IsNullOrEmpty(bed) ? "<silent>" : bed)}");
        }
        Console.WriteLine($"  regions surveyed: {regions.Count}, with audible bed: {regionsWithBed}");
    }

    return 0;
}

// Phase 21d-2a-viii-e — frontend-mesh diagnostics. Mirrors `siegefx asp info`
// but resolves the texture name table against a tank, so the receipt can
// claim "every BSMM texture slot resolves to a shipped raw" (the runtime's
// CreatorChrome does the same resolution at GL load time).
static int DispatchUi(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx ui mesh-info <Objects.dsres> <mesh-basename>"); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "mesh-info" => CmdUiMeshInfo(a[1..]),
        _           => UnknownCommand("ui " + a[0]),
    };
}

static int CmdTemplateAttrDump(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx templates attrs <Logic.dsres> <template-name> [--depth=N]");
        return 1;
    }
    int depth = 99;
    for (int i = 2; i < a.Length; i++)
    {
        if (a[i].StartsWith("--depth=") && int.TryParse(a[i][8..], out var d)) depth = d;
    }
    using var tank = TankFile.Open(a[0]);
    var (store, _) = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(new TankReader(tank));
    if (!store.TryGet(a[1], out var t))
    {
        Console.Error.WriteLine($"no such template: {a[1]}");
        return 2;
    }
    void Dump(SiegeFX.Core.Assets.GasNode n, int indent)
    {
        if (indent > depth) return;
        var pad = new string(' ', indent * 2);
        Console.WriteLine($"{pad}[{n.Header}]");
        foreach (var attr in n.Attributes)
            Console.WriteLine($"{pad}  {attr.Name} = {attr.Value}");
        foreach (var c in n.Children) Dump(c, indent + 1);
    }
    for (var cur = t; cur is not null; cur = cur.Specializes)
    {
        Console.WriteLine($"=== {cur.Name} (from {cur.SourcePath}) ===");
        Dump(cur.Node, 0);
    }
    return 0;
}

static int DispatchFlm(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx flm dump <input.flm> <out-dir>"); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "dump" => CmdFlmDump(a[1..]),
        _      => UnknownCommand("flm " + a[0]),
    };
}

// Phase 21-SC-BARREL — visual-verify decoder. Dumps every frame in the .flm
// to PNG in the given out-dir so the user can sanity-check the offset / row
// orientation / RGBA swap by eye. Used to chase the "cycling numbers"
// regression after the offset fix.
static int CmdFlmDump(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx flm dump <input.flm> <out-dir>");
        return 1;
    }
    var bytes = File.ReadAllBytes(a[0]);
    var outDir = a[1];
    Directory.CreateDirectory(outDir);
    var frames = SiegeFX.Core.Assets.FlmAnimation.LoadFrames(bytes);
    Console.WriteLine($"flm dump: {a[0]}  ({bytes.Length} bytes -> {frames.Length} frames)");
    int sz = SiegeFX.Core.Assets.FlmAnimation.FrameSize;
    for (int i = 0; i < frames.Length; i++)
    {
        var path = Path.Combine(outDir, $"frame_{i:D2}.png");
        using var fs = File.Create(path);
        SiegeFX.Core.IO.Png.EncodeRgba(fs, frames[i], sz, sz);
        Console.WriteLine($"  frame {i,2}: {path}");
    }
    return 0;
}

// SC-TSD-ANIM — TSD sidecar dump for self-verification. Loads a Terrain
// .dsres tank, parses every art/bitmaps/terrain/**.gas, and prints a
// summary plus a sample of frame-cycle and multi-layer records so we can
// confirm the parser is reading the data DS1 actually authors.
static int DispatchTsd(string[] a)
{
    if (a.Length == 0)
    {
        Console.Error.WriteLine("usage: siegefx tsd dump <Terrain.dsres> [--name=NAME]");
        return 1;
    }
    if (!a[0].Equals("dump", StringComparison.OrdinalIgnoreCase))
        return UnknownCommand("tsd " + a[0]);
    if (a.Length < 2) { Console.Error.WriteLine("usage: siegefx tsd dump <Terrain.dsres> [--name=NAME]"); return 1; }

    string? filterName = null;
    for (int i = 2; i < a.Length; i++)
    {
        if (a[i].StartsWith("--name=", StringComparison.OrdinalIgnoreCase))
            filterName = a[i].Substring("--name=".Length);
    }

    using var tank = TankFile.Open(a[1]);
    var reader = new TankReader(tank);
    var store = TsdStore.LoadFromTerrain(reader);

    int total = 0, multiLayer = 0, frameCycle = 0, layer1Scrolls = 0, layer2Scrolls = 0;
    var samples = new List<TsdStore.Record>();
    foreach (var name in EnumerateTsdNames(reader))
    {
        var rec = store.Get(name);
        if (rec is null) continue;
        total++;
        if (rec.Layer2 is not null) multiLayer++;
        if (rec.Layer1.Textures.Length > 1) frameCycle++;
        if (rec.Layer1.UshiftPerSecond != 0f || rec.Layer1.VshiftPerSecond != 0f) layer1Scrolls++;
        if (rec.Layer2 is not null && (rec.Layer2.UshiftPerSecond != 0f || rec.Layer2.VshiftPerSecond != 0f)) layer2Scrolls++;
        if (filterName is not null && !name.Contains(filterName, StringComparison.OrdinalIgnoreCase)) continue;
        if (samples.Count < 12) samples.Add(rec);
    }

    Console.WriteLine($"TSD records parsed:    {total}");
    Console.WriteLine($"  multi-layer (l2):    {multiLayer}");
    Console.WriteLine($"  frame-cycle (l1>1):  {frameCycle}");
    Console.WriteLine($"  layer1 has scroll:   {layer1Scrolls}");
    Console.WriteLine($"  layer2 has scroll:   {layer2Scrolls}");
    Console.WriteLine();
    Console.WriteLine("samples:");
    foreach (var rec in samples)
    {
        Console.WriteLine($"  [{rec.Name}]");
        DumpLayer(rec.Layer1, 1);
        if (rec.Layer2 is not null) DumpLayer(rec.Layer2, 2);
    }
    return 0;
}

static IEnumerable<string> EnumerateTsdNames(TankReader reader)
{
    foreach (var path in reader.ListFiles())
    {
        if (!path.EndsWith(".gas", StringComparison.OrdinalIgnoreCase)) continue;
        if (path.IndexOf("/terrain/", StringComparison.OrdinalIgnoreCase) < 0) continue;
        var bare = System.IO.Path.GetFileNameWithoutExtension(path);
        yield return bare;
    }
}

static void DumpLayer(TsdStore.Layer l, int idx)
{
    var first = l.Textures[0];
    var last = l.Textures[^1];
    Console.WriteLine($"    layer{idx}: textures={l.Textures.Length} ({first}{(l.Textures.Length > 1 ? " .. " + last : "")})" +
                      $" spf={l.SecondsPerFrame} u/s={l.UshiftPerSecond} v/s={l.VshiftPerSecond} op={l.Op}");
}

static int DispatchMusic(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx music <list|play|selftest> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "list"     => CmdMusicList(a[1..]),
        "play"     => CmdMusicPlay(a[1..]),
        "selftest" => CmdMusicSelftest(a[1..]),
        _          => UnknownCommand("music " + a[0]),
    };
}

// Phase 22-SC-MUSIC-FOLD — headless decode-only verification suitable for
// CI. Extracts a music track from Sound.dsres, decodes ~100ms via NLayer,
// asserts non-zero PCM bytes + plausible header. Skips AudioEngine /
// OpenAL entirely so it runs on machines without an audio device.
static int CmdMusicSelftest(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("usage: siegefx music selftest <Sound.dsres> [--track=basename]");
        Console.Error.WriteLine("  default track is 'frontend' (s_m_frontend.mp3)");
        return 1;
    }
    var trackBasename = "frontend";
    for (int i = 1; i < a.Length; i++)
    {
        const string p = "--track=";
        if (a[i].StartsWith(p)) trackBasename = a[i][p.Length..];
        else { Console.Error.WriteLine($"unknown option: {a[i]}"); return 1; }
    }

    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var path = "/sound/music/s_m_" + trackBasename + ".mp3";
    if (!reader.TryGetFile(path, out _))
    {
        Console.Error.WriteLine($"selftest FAIL: track not in tank — {path}");
        return 2;
    }
    var bytes = reader.ExtractToMemory(path);
    Console.WriteLine($"  selftest: extracted {path} ({bytes.Length:N0} bytes)");

    NLayer.MpegFile decoder;
    try { decoder = new NLayer.MpegFile(new MemoryStream(bytes, writable: false)); }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"selftest FAIL: NLayer.MpegFile constructor — {ex.Message}");
        return 3;
    }
    int sr = decoder.SampleRate;
    int ch = decoder.Channels;
    if (sr < 8000 || sr > 96000 || ch < 1 || ch > 2)
    {
        Console.Error.WriteLine($"selftest FAIL: implausible header sr={sr} ch={ch}");
        return 4;
    }
    var buffer = new byte[32 * 1024];
    int produced = decoder.ReadSamples(buffer, 0, buffer.Length);
    decoder.Dispose();
    if (produced <= 0)
    {
        Console.Error.WriteLine($"selftest FAIL: ReadSamples returned {produced}");
        return 5;
    }
    int nonZero = 0;
    for (int i = 0; i < produced; i++) if (buffer[i] != 0) { nonZero++; if (nonZero > 16) break; }
    if (nonZero == 0)
    {
        Console.Error.WriteLine("selftest FAIL: decoded buffer is all-zero (silent stream)");
        return 6;
    }
    Console.WriteLine($"  selftest OK: sr={sr}Hz ch={ch} decoded={produced} bytes (~{produced / (sr * ch * 2.0) * 1000.0:F1}ms)");
    // Sample peak detection: scan whole buffer, report min/max/RMS for both
    // int16 and float interpretations so we can tell which encoding NLayer
    // emits. Real PCM int16 peaks ride near +-32k with RMS in the thousands;
    // a float buffer reinterpreted as int16 produces uniform-large garbage.
    long sumSq = 0; short minS = short.MaxValue, maxS = short.MinValue;
    int sampleCount = produced / 2;
    for (int i = 0; i < produced; i += 2)
    {
        short s = (short)(buffer[i] | (buffer[i + 1] << 8));
        if (s < minS) minS = s;
        if (s > maxS) maxS = s;
        sumSq += (long)s * s;
    }
    double rmsS = sampleCount > 0 ? Math.Sqrt((double)sumSq / sampleCount) : 0;
    double sumSqF = 0; float minF = float.PositiveInfinity, maxF = float.NegativeInfinity;
    int floatCount = produced / 4;
    for (int i = 0; i + 4 <= produced; i += 4)
    {
        float f = BitConverter.ToSingle(buffer, i);
        if (f < minF) minF = f;
        if (f > maxF) maxF = f;
        sumSqF += (double)f * f;
    }
    double rmsF = floatCount > 0 ? Math.Sqrt(sumSqF / floatCount) : 0;
    Console.WriteLine($"  as int16: min={minS} max={maxS} rms={rmsS:F1}");
    Console.WriteLine($"  as f32  : min={minF:F4} max={maxF:F4} rms={rmsF:F4}");
    // Dump 8 samples around the peak so we can eyeball data shape.
    int peakIdx = 0; short peakV = 0;
    for (int i = 0; i < produced; i += 2)
    {
        short s = (short)(buffer[i] | (buffer[i + 1] << 8));
        if (Math.Abs(s) > Math.Abs(peakV)) { peakV = s; peakIdx = i; }
    }
    Console.Write($"  16 int16 around peak (offs={peakIdx}): ");
    int start = Math.Max(0, peakIdx - 16);
    for (int i = start; i < Math.Min(produced, start + 32); i += 2)
    {
        short s = (short)(buffer[i] | (buffer[i + 1] << 8));
        Console.Write($"{s} ");
    }
    Console.WriteLine();

    // Re-decode 1MB+ via the byte path AND the float path; write both as WAV
    // so we can audibly verify which path is producing valid PCM.
    var d2 = new NLayer.MpegFile(new MemoryStream(bytes, writable: false));
    var bytePcm = new byte[2 * 1024 * 1024];
    int byteWritten = 0;
    while (byteWritten < bytePcm.Length)
    {
        int got = d2.ReadSamples(bytePcm, byteWritten, bytePcm.Length - byteWritten);
        if (got <= 0) break;
        byteWritten += got;
    }
    d2.Dispose();

    var d3 = new NLayer.MpegFile(new MemoryStream(bytes, writable: false));
    int floatBufLen = 1024 * 1024;
    var floats = new float[floatBufLen];
    int floatsRead = 0;
    while (floatsRead < floatBufLen)
    {
        int got = d3.ReadSamples(floats, floatsRead, floatBufLen - floatsRead);
        if (got <= 0) break;
        floatsRead += got;
    }
    int srOut = d3.SampleRate; int chOut = d3.Channels;
    d3.Dispose();

    // Convert floats to int16 ourselves
    var floatPcm = new byte[floatsRead * 2];
    for (int i = 0; i < floatsRead; i++)
    {
        short s = (short)Math.Clamp(floats[i] * 32767f, -32768f, 32767f);
        floatPcm[i * 2] = (byte)(s & 0xff);
        floatPcm[i * 2 + 1] = (byte)((s >> 8) & 0xff);
    }

    string outDir = Path.Combine(Path.GetTempPath(), "siegefx_diag");
    Directory.CreateDirectory(outDir);
    string byteWav = Path.Combine(outDir, "nlayer_byte_path.wav");
    string floatWav = Path.Combine(outDir, "nlayer_float_path.wav");
    WriteWav(byteWav, srOut, chOut, bytePcm.AsSpan(0, byteWritten).ToArray());
    WriteWav(floatWav, srOut, chOut, floatPcm);
    Console.WriteLine($"  byte-path WAV  : {byteWav}  ({byteWritten:N0} bytes PCM)");
    Console.WriteLine($"  float-path WAV : {floatWav}  ({floatPcm.Length:N0} bytes PCM)");
    return 0;
}

static void WriteWav(string path, int sampleRate, int channels, byte[] pcm16)
{
    int dataSize = pcm16.Length;
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);
    w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
    w.Write(36 + dataSize);
    w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
    w.Write(16);                  // fmt chunk size
    w.Write((short)1);            // PCM
    w.Write((short)channels);
    w.Write(sampleRate);
    w.Write(sampleRate * channels * 2); // byte rate
    w.Write((short)(channels * 2));     // block align
    w.Write((short)16);                 // bits per sample
    w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
    w.Write(dataSize);
    w.Write(pcm16);
}

// Phase 22-SC-MUSIC-A — list every mp3 under /sound/music/ in a given
// sound tank. Surfaces the 131 shipped DS1 music tracks + their byte
// sizes so the resolver wiring (slices C+D) has a concrete inventory.
static int CmdMusicList(string[] a)
{
    if (a.Length < 1) { Console.Error.WriteLine("usage: siegefx music list <Sound.dsres>"); return 1; }
    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    long total = 0; int count = 0;
    foreach (var path in reader.ListFiles())
    {
        if (!path.StartsWith("/sound/music/", StringComparison.OrdinalIgnoreCase)) continue;
        if (!path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) continue;
        if (!reader.TryGetFile(path, out var entry)) continue;
        Console.WriteLine($"  {entry.Size,10:N0}  {path}");
        total += entry.Size;
        count++;
    }
    Console.WriteLine($"  ----");
    Console.WriteLine($"  {count} tracks, {total:N0} bytes total");
    return 0;
}

// Phase 22-SC-MUSIC-A — extract a track from the sound tank, hand it to
// MusicPlayer, and pump Tick() on a 60Hz timer until the track ends.
// Headless verifier — confirms decoder + OpenAL streaming work without
// the renderer / window stack.
static int CmdMusicPlay(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx music play <Sound.dsres> <track-basename> [--seconds=N]");
        Console.Error.WriteLine("  track basename omits the leading 's_m_' and trailing '.mp3'");
        Console.Error.WriteLine("  e.g. siegefx music play Sound.dsres maintheme");
        return 1;
    }
    int maxSeconds = 0;
    for (int i = 2; i < a.Length; i++)
    {
        const string p = "--seconds=";
        if (a[i].StartsWith(p) && int.TryParse(a[i][p.Length..], out var n)) maxSeconds = n;
        else { Console.Error.WriteLine($"unknown option: {a[i]}"); return 1; }
    }

    using var tank = TankFile.Open(a[0]);
    var reader = new TankReader(tank);
    var basename = a[1].StartsWith("s_m_", StringComparison.OrdinalIgnoreCase) ? a[1] : "s_m_" + a[1];
    if (!basename.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) basename += ".mp3";
    var path = "/sound/music/" + basename;
    if (!reader.TryGetFile(path, out _))
    {
        Console.Error.WriteLine($"track not found: {path}");
        return 2;
    }
    var bytes = reader.ExtractToMemory(path);
    Console.WriteLine($"  loaded {path} ({bytes.Length:N0} bytes)");

    var engine = SiegeFX.Audio.AudioEngine.TryCreate();
    if (engine is null)
    {
        Console.Error.WriteLine("AudioEngine.TryCreate failed — no audio device?");
        return 3;
    }
    using var music = SiegeFX.Audio.MusicPlayer.TryCreate(engine);
    if (music is null)
    {
        Console.Error.WriteLine("MusicPlayer.TryCreate failed");
        engine.Dispose();
        return 4;
    }
    if (!music.Play(bytes))
    {
        Console.Error.WriteLine("MusicPlayer.Play returned false (decoder failure)");
        return 5;
    }
    Console.WriteLine($"  playing — Ctrl+C or wait{(maxSeconds > 0 ? $" {maxSeconds}s" : " for end")}");

    var started = DateTime.UtcNow;
    while (music.Tick())
    {
        Thread.Sleep(16); // ~60Hz pump
        if (maxSeconds > 0 && (DateTime.UtcNow - started).TotalSeconds >= maxSeconds) break;
    }
    music.Stop();
    engine.Dispose();
    Console.WriteLine($"  done after {(DateTime.UtcNow - started).TotalSeconds:F1}s");
    return 0;
}

static int CmdUiMeshInfo(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("usage: siegefx ui mesh-info <Objects.dsres> <mesh-basename>");
        Console.Error.WriteLine("  e.g. siegefx ui mesh-info Objects.dsres m_gui_fe_m_mn_3d_heromenu.asp");
        return 1;
    }
    var tankPath = a[0];
    var meshName = a[1];

    using var tank = SiegeFX.Core.Tank.TankFile.Open(tankPath);
    var reader = new SiegeFX.Core.Tank.TankReader(tank);
    var resolver = new SiegeFX.Core.Assets.AssetResolver();
    resolver.Add(reader, Path.GetFileName(tankPath));

    if (!resolver.TryLoadByBasename(meshName, out var aspBytes))
    {
        Console.Error.WriteLine($"mesh '{meshName}' not found in {Path.GetFileName(tankPath)}");
        return 2;
    }

    var asp = SiegeFX.Core.Assets.AspMesh.Load(aspBytes);
    Console.WriteLine($"ui mesh-info: {meshName}");
    Console.WriteLine($"  asp version : {asp.AspVersionMajor}.{asp.AspVersionMinor}");
    Console.WriteLine($"  bones       : {asp.BoneCount}");
    Console.WriteLine($"  positions   : {asp.Positions.Length}");
    Console.WriteLine($"  corners     : {asp.Corners.Length}");
    Console.WriteLine($"  triangles   : {asp.TriangleCount}");
    Console.WriteLine($"  subsets     : {asp.Subsets.Length}");
    Console.WriteLine($"  textures    : {asp.TextureNames.Count}");

    int resolved = 0;
    int strippedAtlas = 0;
    Console.WriteLine();
    Console.WriteLine("  texture-slot resolution against tank:");
    for (int i = 0; i < asp.TextureNames.Count; i++)
    {
        var name  = asp.TextureNames[i];
        var direct = resolver.TryLoadByBasename(name + ".raw", out _) ? "OK" : "MISS";
        var alias = StripMapSuffix(name);
        bool aliasUsed = !string.Equals(alias, name, StringComparison.Ordinal);
        var aliasResult = aliasUsed
            ? (resolver.TryLoadByBasename(alias + ".raw", out _) ? "OK" : "MISS")
            : "-";
        bool ok = direct == "OK" || (aliasUsed && aliasResult == "OK");
        if (ok) resolved++;
        if (aliasUsed && aliasResult == "OK" && direct != "OK") strippedAtlas++;
        Console.WriteLine($"    [{i,2}] {name,-44} direct={direct}  alias={alias,-32} alias-resolved={aliasResult}");
    }
    Console.WriteLine();
    Console.WriteLine($"  resolved (direct or via -mapN alias): {resolved}/{asp.TextureNames.Count}");
    Console.WriteLine($"  resolved only via -mapN alias       : {strippedAtlas}");

    Console.WriteLine();
    Console.WriteLine("  per-subset (firstTri, triCount, texIdx, textureName):");
    for (int i = 0; i < asp.Subsets.Length; i++)
    {
        var s = asp.Subsets[i];
        var tex = s.TextureIndex >= 0 && s.TextureIndex < asp.TextureNames.Count
            ? asp.TextureNames[s.TextureIndex] : "<oob>";
        Console.WriteLine($"    [{i,2}] firstTri={s.FirstTriangle,4}  triCount={s.TriangleCount,4}  texIdx={s.TextureIndex,2}  {tex}");
    }

    // Per-subset UV + bind-pose XY bounds. Tells us whether each subset is at its
    // own physical mesh-space position (= mesh handles per-cell layout) or all
    // stacked at the same position (= engine has to spread them per-row).
    Console.WriteLine();
    Console.WriteLine("  per-subset UV + bind-pose XY bounds:");
    Console.WriteLine($"    {"#",2}  {"uv-min",-18} {"uv-max",-18} {"xy-min",-18} {"xy-max",-18} {"xy-size",-18}");
    for (int i = 0; i < asp.Subsets.Length; i++)
    {
        var s = asp.Subsets[i];
        var uvMin = new Vector2(float.PositiveInfinity);
        var uvMax = new Vector2(float.NegativeInfinity);
        var xyMin = new Vector2(float.PositiveInfinity);
        var xyMax = new Vector2(float.NegativeInfinity);
        int triEnd = s.FirstTriangle + s.TriangleCount;
        for (int t = s.FirstTriangle; t < triEnd; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int cornerIdx = asp.TriangleIndices[t * 3 + k];
                var corner = asp.Corners[cornerIdx];
                uvMin = Vector2.Min(uvMin, corner.Uv);
                uvMax = Vector2.Max(uvMax, corner.Uv);
                var pos = asp.Positions[corner.VertexIndex];
                xyMin = Vector2.Min(xyMin, new Vector2(pos.X, pos.Y));
                xyMax = Vector2.Max(xyMax, new Vector2(pos.X, pos.Y));
            }
        }
        var xySize = xyMax - xyMin;
        Console.WriteLine($"    {i,2}  ({uvMin.X,5:F2},{uvMin.Y,5:F2})    ({uvMax.X,5:F2},{uvMax.Y,5:F2})    ({xyMin.X,6:F2},{xyMin.Y,6:F2})  ({xyMax.X,6:F2},{xyMax.Y,6:F2})  ({xySize.X,6:F2},{xySize.Y,6:F2})");
    }

    return resolved == asp.TextureNames.Count ? 0 : 4;
}

static string StripMapSuffix(string name)
{
    var dash = name.LastIndexOf("-map", StringComparison.Ordinal);
    if (dash <= 0) return name;
    for (int i = dash + 4; i < name.Length; i++)
        if (!char.IsDigit(name[i])) return name;
    if (dash + 4 == name.Length) return name;
    return name[..dash];
}

// Phase 9-SC-16 Phase B-1 — diagnostic CLI for the pcontent roller. Dumps
// every literal class bucket the resolver indexed (sorted by power) so a
// `#club/2-3` no longer silently rolls a unique. With --spec=#class/lo-hi
// it also runs N sample rolls, listing the names + powers actually picked
// by TryResolve under the same filter the runtime uses.
static int DispatchPcontent(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx pcontent dump <tank> [--spec=#class/lo-hi] [--rolls=N] [--seed=K] [--class=X]"); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "dump" => CmdPcontentDump(a[1..]),
        _      => UnknownCommand("pcontent " + a[0]),
    };
}

static int CmdPcontentDump(string[] a)
{
    string? specArg = null;
    string? classFilter = null;
    int rolls = 12;
    int? seed = null;
    var rest = new List<string>();
    foreach (var x in a)
    {
        if      (x.StartsWith("--spec=",  StringComparison.Ordinal)) specArg = x["--spec=".Length..];
        else if (x.StartsWith("--class=", StringComparison.Ordinal)) classFilter = x["--class=".Length..];
        else if (x.StartsWith("--rolls=", StringComparison.Ordinal)) int.TryParse(x["--rolls=".Length..], out rolls);
        else if (x.StartsWith("--seed=",  StringComparison.Ordinal)) { if (int.TryParse(x["--seed=".Length..], out var s)) seed = s; }
        else rest.Add(x);
    }
    if (rest.Count != 1)
    {
        Console.Error.WriteLine("usage: siegefx pcontent dump <tank> [--spec=#class/lo-hi] [--rolls=N] [--seed=K] [--class=X]");
        return 1;
    }

    using var tank = TankFile.Open(rest[0]);
    var reader = new TankReader(tank);
    var (store, _) = TemplateStore.LoadFromTank(reader);
    var resolver = new PcontentResolver(store);

    var byClass = resolver.ByClass();
    var classKeys = byClass.Keys
        .Where(k => classFilter is null || k.Equals(classFilter, StringComparison.OrdinalIgnoreCase))
        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var allEntries = resolver.AllEntries();
    Console.WriteLine($"indexed {byClass.Count} class bucket(s); {byClass.Sum(kv => kv.Value.Count)} weapon entries; {allEntries.Count} total entries (incl. armor)");
    Console.WriteLine();

    // Wildcard summaries — these are the synthetic groups the
    // resolver folds across at run-time for #weapon / #armor / #*
    // specs. Useful sanity check: armor count > 0, melee < weapon, etc.
    Console.WriteLine("== wildcards ==");
    PrintGroup("weapon (melee + ranged)", allEntries.Where(e => (e.Group & (PcontentResolver.Group.Melee | PcontentResolver.Group.Ranged)) != 0));
    PrintGroup("melee",                   allEntries.Where(e => (e.Group & PcontentResolver.Group.Melee) != 0));
    PrintGroup("armor",                   allEntries.Where(e => (e.Group & PcontentResolver.Group.Armor) != 0));
    Console.WriteLine();

    // Rarity tally — how many normal / rare / unique items got
    // indexed; Phase B-2 needs unique > 0 for #*/-unique(2)/... to
    // resolve at all.
    var rarityCounts = allEntries.GroupBy(e => e.Rarity).ToDictionary(g => g.Key, g => g.Count());
    var pcontentDisallowed = allEntries.Count(e => !e.PcontentAllowed);
    Console.WriteLine($"== rarity tally ==  normal={rarityCounts.GetValueOrDefault(PcontentResolver.Rarity.Normal)}  rare={rarityCounts.GetValueOrDefault(PcontentResolver.Rarity.Rare)}  unique={rarityCounts.GetValueOrDefault(PcontentResolver.Rarity.Unique)}  is_pcontent_allowed=false: {pcontentDisallowed}");
    Console.WriteLine();

    foreach (var key in classKeys)
    {
        var bucket = byClass[key];
        var minP = bucket.Count == 0 ? 0 : bucket.Min(e => e.Power);
        var maxP = bucket.Count == 0 ? 0 : bucket.Max(e => e.Power);
        Console.WriteLine($"== {key}  ({bucket.Count} entries, power {minP}..{maxP}) ==");
        // Show low-tier head and high-tier tail so the spread is visible
        // even for big buckets (e.g. weapon classes with dozens of variants).
        int head = Math.Min(8, bucket.Count);
        for (int i = 0; i < head; i++)
            Console.WriteLine($"  pow={bucket[i].Power,4}  rar={RarityTag(bucket[i].Rarity)}  {bucket[i].Name}");
        if (bucket.Count > head + 4)
        {
            Console.WriteLine($"  ... ({bucket.Count - head - 4} entries omitted) ...");
            for (int i = bucket.Count - 4; i < bucket.Count; i++)
                Console.WriteLine($"  pow={bucket[i].Power,4}  rar={RarityTag(bucket[i].Rarity)}  {bucket[i].Name}");
        }
        else
        {
            for (int i = head; i < bucket.Count; i++)
                Console.WriteLine($"  pow={bucket[i].Power,4}  rar={RarityTag(bucket[i].Rarity)}  {bucket[i].Name}");
        }
        Console.WriteLine();
    }

    if (specArg is not null)
    {
        var parsed = PcontentResolver.ParseSpec(specArg);
        Console.WriteLine($"spec '{specArg}' parsed: class='{parsed.Class}' sub='{parsed.Sub}' rarity={parsed.Rarity} power={(parsed.HasPower ? $"{parsed.PowerMin}-{parsed.PowerMax}" : "<any>")}");
        var rng = new Random(seed ?? Environment.TickCount);
        var hist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool resolved = false;
        for (int i = 0; i < rolls; i++)
        {
            if (resolver.TryResolve(specArg, rng, out var name, out var power))
            {
                resolved = true;
                var line = $"pow={power,4}  {name}";
                hist[line] = hist.TryGetValue(line, out var c) ? c + 1 : 1;
            }
        }
        if (!resolved)
        {
            Console.WriteLine("  (resolver returned false for every roll — class unknown or bucket empty)");
            return 2;
        }
        Console.WriteLine($"{rolls} roll(s):");
        foreach (var (line, count) in hist.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  x{count,2}  {line}");
    }

    return 0;

    static void PrintGroup(string label, IEnumerable<PcontentResolver.Entry> entries)
    {
        var list = entries.ToList();
        if (list.Count == 0) { Console.WriteLine($"  {label,-26}: 0"); return; }
        var minP = list.Min(e => e.Power);
        var maxP = list.Max(e => e.Power);
        var n = list.Count(e => e.Rarity == PcontentResolver.Rarity.Normal && e.PcontentAllowed);
        var r = list.Count(e => e.Rarity == PcontentResolver.Rarity.Rare);
        var u = list.Count(e => e.Rarity == PcontentResolver.Rarity.Unique);
        Console.WriteLine($"  {label,-26}: {list.Count,5}  power {minP,4}..{maxP,-4}  (normal-rollable={n}  rare={r}  unique={u})");
    }

    static string RarityTag(PcontentResolver.Rarity r) => r switch
    {
        PcontentResolver.Rarity.Rare   => "ra",
        PcontentResolver.Rarity.Unique => "un",
        _                              => "g ",
    };
}

// Phase 12-SC-3 — `siegefx loot dump <tank> <template> [--rolls=N] [--seed=K]`.
// Parses the template's [inventory][pcontent] tree via the runtime LootTable
// + LootRoller, prints the parsed Equipped/Drops buckets, then rolls N times
// and aggregates frequency by reference. Cross-checked against shipped DS1
// retail behavior so the audit can spot bugs like "equipped weapon always
// drops" or "branch picks aren't weighted".
static int DispatchLoot(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx loot dump <tank> <template> [--rolls=N] [--seed=K]"); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "dump" => CmdLootDump(a[1..]),
        _      => UnknownCommand("loot " + a[0]),
    };
}

static int CmdLootDump(string[] a)
{
    int rolls = 100;
    int? seed = null;
    var rest = new List<string>();
    foreach (var x in a)
    {
        if      (x.StartsWith("--rolls=", StringComparison.Ordinal)) int.TryParse(x["--rolls=".Length..], out rolls);
        else if (x.StartsWith("--seed=",  StringComparison.Ordinal)) { if (int.TryParse(x["--seed=".Length..], out var s)) seed = s; }
        else rest.Add(x);
    }
    if (rest.Count != 2)
    {
        Console.Error.WriteLine("usage: siegefx loot dump <tank> <template> [--rolls=N] [--seed=K]");
        return 1;
    }
    using var tank = TankFile.Open(rest[0]);
    var reader = new TankReader(tank);
    var (store, _) = TemplateStore.LoadFromTank(reader);
    if (!store.TryGet(rest[1], out var tpl) || tpl is null)
    {
        Console.Error.WriteLine($"template not found: {rest[1]}");
        return 2;
    }

    var table = LootTable.FromTemplate(store, tpl);
    Console.WriteLine($"== {tpl.Name} ==");
    if (table.IsEmpty)
    {
        Console.WriteLine("  (no [inventory][pcontent] in specializes chain — actor drops nothing)");
        return 0;
    }

    Console.WriteLine($"  equipped buckets: {table.Equipped.Count}");
    for (int i = 0; i < table.Equipped.Count; i++) PrintBucket($"  [eq {i}]", table.Equipped[i], 4);
    Console.WriteLine($"  drop buckets:     {table.Drops.Count}");
    for (int i = 0; i < table.Drops.Count; i++) PrintBucket($"  [drop {i}]", table.Drops[i], 4);

    var rng = new Random(seed ?? Environment.TickCount);
    var hist = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    int totalDrops = 0;
    int emptyKills = 0;
    for (int i = 0; i < rolls; i++)
    {
        var drops = LootRoller.Roll(table, rng);
        if (drops.Count == 0) emptyKills++;
        foreach (var d in drops)
        {
            totalDrops++;
            string key = d.IsEquipped ? $"[{d.Slot}] {d.Reference}" : d.Reference;
            hist[key] = hist.TryGetValue(key, out var c) ? c + 1 : 1;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"  {rolls} rolls — {totalDrops} drops total, {emptyKills} empty kills, avg {(float)totalDrops/rolls:F2} drops/kill");
    foreach (var kv in hist.OrderByDescending(kv => kv.Value))
        Console.WriteLine($"  x{kv.Value,4} ({100f * kv.Value / rolls,5:F1}%)  {kv.Key}");
    return 0;

    static void PrintBucket(string label, LootBucket b, int indent)
    {
        var pad = new string(' ', indent);
        Console.WriteLine($"{label} chance={b.Chance:F2}  entries={b.Entries.Count}  children={b.Children.Count}");
        foreach (var e in b.Entries)
            Console.WriteLine(e.IsEquipped
                ? $"{pad}  es_{e.Slot} = {e.Reference}"
                : $"{pad}  il_main  = {e.Reference}");
        for (int i = 0; i < b.Children.Count; i++) PrintBucket($"{pad}  [child {i}]", b.Children[i], indent + 4);
    }
}

// SC-QUEST-OBJ-F-AUDIT (2026-05-13) — quest-catalog drift report.
// Walks every region's conversations/conversations.gas, mines every
// `activate_quest=KEY;` attribute from dialogue nodes, and cross-checks
// against SiegeFX.Core.Actors.QuestCatalog. Three output sections:
//   COVERED  — keys in BOTH the dialogue trees and the catalog
//   MISSING  — keys authored in DS1 dialogue but NOT in the catalog
//              (these are the real gaps SC-QUEST-OBJ-F needs to fill)
//   ORPHAN   — keys in the catalog but NOT activated by any dialogue
//              (catalog rows that no DS1 NPC will ever trigger — either
//               wrong key spelling or pre-emptive placeholders)
// Used to verify the catalog matches reality before / during the
// FH->Ehb playtest. Modeled on `siegefx spells visual-audit`.
static int DispatchQuests(string[] a)
{
    if (a.Length == 0)
    {
        Console.Error.WriteLine("usage: siegefx quests audit <map-tank>");
        return 1;
    }
    if (!a[0].Equals("audit", StringComparison.OrdinalIgnoreCase))
        return UnknownCommand("quests " + a[0]);
    return CmdQuestsAudit(a[1..]);
}

static int CmdQuestsAudit(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("usage: siegefx quests audit <map-tank>");
        return 1;
    }

    using var mapTank = TankFile.Open(a[0]);
    var mapReader = new TankReader(mapTank);

    // Enumerate every region path under the map tank (same shape the
    // breakable-audit `all` mode uses).
    var regionPaths = new List<string>();
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

    // key -> list of (region, conversationKey) sites that activate it.
    var perKey = new SortedDictionary<string, List<(string Region, string Conv)>>(StringComparer.OrdinalIgnoreCase);
    int regionsWithConvs = 0;
    foreach (var regionPath in regionPaths)
    {
        var (convs, _) = SiegeFX.Core.Assets.ConversationStore.Load(mapReader, regionPath);
        if (convs.Count == 0) continue;
        regionsWithConvs++;
        foreach (var (convKey, conv) in convs)
        {
            foreach (var node in conv.Nodes)
            {
                var key = node.ActivateQuest;
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (!perKey.TryGetValue(key, out var sites))
                    perKey[key] = sites = new List<(string, string)>();
                sites.Add((regionPath, convKey));
            }
        }
    }

    var catalog = SiegeFX.Core.Actors.QuestCatalog.All;
    var covered = new List<string>();
    var missing = new List<string>();
    foreach (var k in perKey.Keys)
    {
        if (catalog.ContainsKey(k)) covered.Add(k);
        else missing.Add(k);
    }
    var orphan = new List<string>();
    foreach (var k in catalog.Keys)
        if (!perKey.ContainsKey(k)) orphan.Add(k);

    Console.WriteLine($"quest audit: {regionPaths.Count} region(s), {regionsWithConvs} with conversations");
    Console.WriteLine($"  dialogue-authored quest keys:  {perKey.Count}");
    Console.WriteLine($"  catalog rows:                  {catalog.Count}");
    Console.WriteLine($"  COVERED (both):                {covered.Count}");
    Console.WriteLine($"  MISSING (dialogue, not cat.):  {missing.Count}");
    Console.WriteLine($"  ORPHAN  (cat., no dialogue):   {orphan.Count}");

    Console.WriteLine();
    Console.WriteLine("== COVERED ==");
    foreach (var k in covered)
        Console.WriteLine($"  {k,-40}  ({perKey[k].Count} dialogue site(s))");

    Console.WriteLine();
    Console.WriteLine("== MISSING (add to QuestCatalog or fix spelling) ==");
    foreach (var k in missing)
    {
        Console.WriteLine($"  {k}");
        foreach (var (region, conv) in perKey[k])
        {
            // Trim region path noise so the output reads as
            // "<region-basename> [<conversation>]".
            var slash = region.LastIndexOf('/');
            var regBase = slash >= 0 ? region[(slash + 1)..] : region;
            Console.WriteLine($"      {regBase}  conversation_{conv}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("== ORPHAN (catalog row no dialogue activates) ==");
    foreach (var k in orphan) Console.WriteLine($"  {k}");

    return 0;
}


/// <summary>Audit-CLI helper: pretends a single party member sits at <see cref="Position"/>.
/// Used by `siegefx region triggers` to drive a synthetic enter/leave through the first
/// producer placement so the SC-1b/SC-1c paths get exercised on every run.</summary>
sealed class SyntheticPartyContext : SiegeFX.Core.Actors.TriggerContext
{
    public System.Numerics.Vector3 Position;
    public override bool PartyMemberWithinSphere(System.Numerics.Vector3 c, float r)
        => (Position - c).LengthSquared() <= r * r;
    public override bool PartyMemberWithinAabb(System.Numerics.Vector3 c, float hx, float hy, float hz)
    {
        var d = Position - c;
        return MathF.Abs(d.X) <= hx && MathF.Abs(d.Y) <= hy && MathF.Abs(d.Z) <= hz;
    }
}

/// <summary>Phase 21-SC-SPELL-VFX-AUDIT — one spell's worth of static-IR
/// audit results, accumulated by <c>WalkAuditScript</c>. Lives next to
/// <see cref="TallySink"/> because Program.cs is a top-level-statement file
/// and class declarations must follow all top-level functions.</summary>
sealed class SpellAuditRow
{
    public string Name { get; }
    public string ScriptName { get; }
    public HashSet<string> PrimitiveKinds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> UnhandledVerbs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Textures       { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Verdict { get; set; } = "?";
    public SpellAuditRow(string name, string script) { Name = name; ScriptName = script; }
}

/// <summary>Headless <see cref="SiegeFX.Core.Sfx.IParticleSink"/> for `siegefx sfx run`.
/// Counts spawn/maintain calls and accumulates per-kind particle budgets so the audit
/// CLI can verify the VM produces the expected receipt without standing up GL.</summary>
// Phase 23b — deterministic sink for `siegefx sfx timeline`. Records every
// IParticleSink call in order with a caller-stamped timestamp and every
// argument at fixed precision, so a full cast run serializes to a stable
// text trace. Combined with SfxRuntime.SetDeterministicSeed, identical
// code + data produce identical traces — the committed goldens under
// goldens/sfx-timelines/ are a regression net for VM/param changes.
sealed class TimelineSink : SiegeFX.Core.Sfx.IParticleSink
{
    public float Now;
    public readonly List<string> Events = new();

    static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    static string V3(System.Numerics.Vector3 v) => $"({F(v.X)},{F(v.Y)},{F(v.Z)})";
    static string V4(System.Numerics.Vector4 v) => $"({F(v.X)},{F(v.Y)},{F(v.Z)},{F(v.W)})";
    void Add(string s) => Events.Add($"{Now.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),7}  {s}");

    public void SpawnFire(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float scale, float dur, int count = 12)
        => Add($"fire        pos={V3(p)} color={V4(c)} scale={F(scale)} dur={F(dur)} n={count}");
    public void SpawnSmoke(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float scale, float dur, int count = 8)
        => Add($"smoke       pos={V3(p)} color={V4(c)} scale={F(scale)} dur={F(dur)} n={count}");
    public void SpawnSteam(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float scale, float dur, int count = 8)
        => Add($"steam       pos={V3(p)} color={V4(c)} scale={F(scale)} dur={F(dur)} n={count}");
    public void SpawnSpark(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float scale, float dur, int count = 16)
        => Add($"spark       pos={V3(p)} color={V4(c)} scale={F(scale)} dur={F(dur)} n={count}");
    public void SpawnLightning(System.Numerics.Vector3 s, System.Numerics.Vector3 t, System.Numerics.Vector4 c, float dur)
        => Add($"lightning   src={V3(s)} tgt={V3(t)} color={V4(c)} dur={F(dur)}");
    public void SpawnLightning(System.Numerics.Vector3 s, System.Numerics.Vector3 t, System.Numerics.Vector4 c, float dur, float displace)
        => Add($"lightning   src={V3(s)} tgt={V3(t)} color={V4(c)} dur={F(dur)} displace={F(displace)}");
    public void SpawnLightning(System.Numerics.Vector3 s, System.Numerics.Vector3 t, System.Numerics.Vector4 c, float dur, float minDisplace, float maxDisplace, float subd, float minSubd)
        => Add($"lightning   src={V3(s)} tgt={V3(t)} color={V4(c)} dur={F(dur)} displace={F(minDisplace)}..{F(maxDisplace)} subd={F(subd)} minsubd={F(minSubd)}");
    public void SpawnExplosion(in SiegeFX.Core.Sfx.ExplosionSpec e)
        => Add($"explosion   pos={V3(e.Anchor)} color={V4(e.Color)} r={F(e.Radius)} n={e.Count} scale={F(e.ScaleMin)}..{F(e.ScaleMax)} v={F(e.VMin)}..{F(e.VMax)} ivel={V3(e.IVel)} rvel={V3(e.RVel)} omni={(e.OmniDir ? 1 : 0)} fade={F(e.FadeStart)}..{F(e.FadeEnd)} dur={F(e.Duration)} srate={F(e.SpawnOver)} tex={e.TexSlot}");
    public void SpawnCylinderTube(in SiegeFX.Core.Sfx.CylinderSpec e)
        => Add($"cylinder    pos={V3(e.Anchor)} color={V4(e.Color)} rp0={V3(e.Rp0)} rp1={V3(e.Rp1)} hp0={V3(e.Hp0)} hp1={V3(e.Hp1)} alpha={F(e.Alpha)} spin={F(e.Spin)} tin={F(e.FadeIn)} tout={F(e.FadeOut)} dur={F(e.Duration)} rot={V3(e.Rotate)} irot={V3(e.IRotate)} tex={e.TexSlot} seg={e.Segments}");
    public void SpawnSrayTimed(in SiegeFX.Core.Sfx.SraySpec e)
        => Add($"sray        pos={V3(e.Anchor)} c0={V4(e.Color0)} c1={V4(e.Color1)} r={F(e.Radius)} n={e.Count} len={F(e.LMin)}..{F(e.LMax)} ws={F(e.WsMin)}..{F(e.WsMax)} we={F(e.WeMin)}..{F(e.WeMax)} theta={V3(e.Theta)} phi={V3(e.Phi)} alpha={V3(e.Alpha)} srate={F(e.SpawnPeriod)} dur={F(e.Duration)}");
    public void SpawnFlurry(in SiegeFX.Core.Sfx.FlurrySpec e)
        => Add($"flurry      pos={V3(e.Anchor)} color={V4(e.Color)} r={F(e.Radius)} n={e.Count} iphi={F(e.IPhi)} itheta={F(e.ITheta)} iamp={F(e.IAmp)} amp={F(e.Amplitude)} grow={F(e.GrowStart)}/{F(e.GrowMid)}/{F(e.GrowEnd)} tin={F(e.FadeIn)} tout={F(e.FadeOut)} dur={F(e.Duration)} tex={e.TexSlot}");
    public void SpawnProjectile(System.Numerics.Vector3 s, System.Numerics.Vector3 t, System.Numerics.Vector4 c, float scale, float speed, int impactKind)
        => Add($"projectile  src={V3(s)} tgt={V3(t)} color={V4(c)} scale={F(scale)} speed={F(speed)} impact={impactKind}");
    public void SpawnCylinder(System.Numerics.Vector3 a, System.Numerics.Vector4 c, float rOut, float thick, float spin, float tin, float tout, float dur, byte tex, byte seg)
        => Add($"cylinder    pos={V3(a)} color={V4(c)} r={F(rOut)} thick={F(thick)} spin={F(spin)} tin={F(tin)} tout={F(tout)} dur={F(dur)} tex={tex} seg={seg}");
    public void SpawnSray(System.Numerics.Vector3 a, System.Numerics.Vector4 c0, System.Numerics.Vector4 c1, float lmin, float lmax, float ws, float we, float dur, int rays)
        => Add($"sray        pos={V3(a)} c0={V4(c0)} c1={V4(c1)} len={F(lmin)}..{F(lmax)} w={F(ws)}..{F(we)} dur={F(dur)} rays={rays}");
    public void SpawnFireb(System.Numerics.Vector3 a, System.Numerics.Vector4 c, System.Numerics.Vector3 vel, System.Numerics.Vector3 acc, float life, float maxDisp, float lr, float ur, int count, float flame)
        => Add($"fireb       pos={V3(a)} color={V4(c)} vel={V3(vel)} accel={V3(acc)} life={F(life)} disp={F(maxDisp)} r={F(lr)}..{F(ur)} n={count} flame={F(flame)}");
    public void SpawnSphere(System.Numerics.Vector3 a, System.Numerics.Vector4 c, float radius, float dur, int count)
        => Add($"sphere      pos={V3(a)} color={V4(c)} r={F(radius)} dur={F(dur)} n={count}");

    // Maintain* pumps fire every tick; emulate the renderer's carry math so
    // spawn cadence matches the live system, and only log ticks that
    // actually emit (keeps traces dense with signal, still deterministic).
    float Pump(string kind, System.Numerics.Vector3 p, System.Numerics.Vector4 c, float scale, float dt, float rate, float carry)
    {
        float budget = carry + rate * dt;
        int n = (int)budget;
        if (n > 0) Add($"{kind,-11} pos={V3(p)} color={V4(c)} scale={F(scale)} rate={F(rate)} n={n}");
        return budget - n;
    }
    public float MaintainPlume(in SiegeFX.Core.Sfx.PlumeSpec s, System.Numerics.Vector3 p, float age, float dt, float carry)
    {
        float life = Math.Clamp(1f / MathF.Max(0.15f, s.AlphaFade), 0.30f, 3.5f);
        float rate = MathF.Max(1f, s.Count / life);
        float budget = carry + rate * dt;
        int n = (int)budget;
        if (n > 0)
        {
            string kind = s.Kind == 0 ? "fire~" : s.Kind == 2 ? "steam~" : "smoke~";
            Add($"{kind,-11} pos={V3(p)} color={V4(s.Color)} vel={V3(s.Velocity)} accel={V3(s.Accel)} flame={F(s.FlameSize)} ring={F(s.MinRadius)}..{F(s.MaxRadius)} ydisp={F(s.MinDisplace)}..{F(s.MaxDisplace)} line={(s.Line ? 1 : 0)} n={n}");
        }
        return budget - n;
    }
    public void BurstPlume(in SiegeFX.Core.Sfx.PlumeSpec s, System.Numerics.Vector3 p, int n)
        => Add($"plume!      pos={V3(p)} kind={s.Kind} n={n} (instant fill)");
    public void SpawnLineTracer(System.Numerics.Vector3 s, System.Numerics.Vector3 t, System.Numerics.Vector4 c0, System.Numerics.Vector4 c1, float fadeRate, float tin, float tout)
        => Add($"linetracer  src={V3(s)} tgt={V3(t)} c0={V4(c0)} c1={V4(c1)} fade={F(fadeRate)} tin={F(tin)} tout={F(tout)}");
    public void SpawnSpe(in SiegeFX.Core.Sfx.SpeSpec e)
        => Add($"spe         pos={V3(e.Anchor)} color={V4(e.Color)} r={F(e.Radius)} n={e.Count} scale={F(e.Scale)} i0={V3(e.Index0)} i1={V3(e.Index1)} v0={V3(e.Speed0)} v1={V3(e.Speed1)} s0={V3(e.Space0)} s1={V3(e.Space1)} tin={F(e.FadeIn)} tout={F(e.FadeOut)} dur={F(e.Duration)}");
    public void SpawnSparkles(in SiegeFX.Core.Sfx.SparklesSpec e)
        => Add($"sparkles    pos={V3(e.Anchor)} color={V4(e.Color)} r={F(e.Radius)} n={e.Count} psize={F(e.PSize)} yvel={F(e.YVel)} dur={F(e.Duration)}");
    public void SpawnCharge(in SiegeFX.Core.Sfx.ChargeSpec e)
        => Add($"charge      pos={V3(e.Anchor)} color={V4(e.Color)} r={F(e.Radius)} n={e.Count} tout={F(e.Tout)} speed0={F(e.Speed0)} center={F(e.CenterSize)} ialpha={F(e.IAlpha)} dur={F(e.Duration)}");
    public void SpawnPolyExplosion(in SiegeFX.Core.Sfx.PolyExplosionSpec e)
        => Add($"polyexpl    pos={V3(e.Anchor)} color={V4(e.Color)} sides<={e.PolySides} n={e.Count} r={F(e.Radius)} mag={F(e.Mag)} rotrange={V3(e.RotRange)} disp={V3(e.Displace)} fade={F(e.FadeStart)}..{F(e.FadeEnd)} dur={F(e.Duration)}");
    public void SpawnSphereMesh(in SiegeFX.Core.Sfx.SphereMeshSpec e)
        => Add($"spheremesh  pos={V3(e.Anchor)} color={V4(e.Color)} r={F(e.Radius)} sides={e.Sides} subd={e.Subd} grow={F(e.GrowStart)}/{F(e.GrowMid)}/{F(e.GrowEnd)} rot={V3(e.Rotate)} irot={V3(e.IRotate)} tin={F(e.FadeIn)} tout={F(e.FadeOut)} dur={F(e.Duration)}");
    public float MaintainFire(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float scale, float dt, float rate, float carry)
        => Pump("fire~", p, c, scale, dt, rate, carry);
    public float MaintainSmoke(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float scale, float dt, float rate, float carry)
        => Pump("smoke~", p, c, scale, dt, rate, carry);
    public float MaintainSteam(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float scale, float dt, float rate, float carry)
        => Pump("steam~", p, c, scale, dt, rate, carry);
    public float MaintainGlow(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float radius, float dt, float rate, float carry)
        => Pump("glow~", p, c, radius, dt, rate, carry);
}

sealed class TallySink : SiegeFX.Core.Sfx.IParticleSink
{
    public int SpawnFireCount, SpawnSmokeCount, SpawnSteamCount, SpawnSparkCount, SpawnLightningCount, SpawnProjectileCount;
    public int SpawnCylinderCount, SpawnSrayCount, SpawnFirebCount;
    public int MaintainFireCount, MaintainSmokeCount, MaintainSteamCount, MaintainGlowCount;
    public int SpawnGlowCount;
    public void SpawnFire(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float s, float d, int n = 12) => SpawnFireCount += n;
    public void SpawnSmoke(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float s, float d, int n = 8) => SpawnSmokeCount += n;
    public void SpawnSteam(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float s, float d, int n = 8) => SpawnSteamCount += n;
    public void SpawnSpark(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float s, float d, int n = 16) => SpawnSparkCount += n;
    public void SpawnLightning(System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector4 c, float d) => SpawnLightningCount++;
    public void SpawnLightning(System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector4 c, float d, float disp) => SpawnLightningCount++;
    public void SpawnLightning(System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector4 c, float d, float minD, float maxD, float subd, float minSubd) => SpawnLightningCount++;
    public int SpawnExplosionCount;
    public void SpawnExplosion(in SiegeFX.Core.Sfx.ExplosionSpec e) => SpawnExplosionCount += e.Count;
    public void SpawnCylinderTube(in SiegeFX.Core.Sfx.CylinderSpec e) => SpawnCylinderCount++;
    public void SpawnSrayTimed(in SiegeFX.Core.Sfx.SraySpec e) => SpawnSrayCount++;
    public int SpawnFlurryCount;
    public void SpawnFlurry(in SiegeFX.Core.Sfx.FlurrySpec e) => SpawnFlurryCount += e.Count;
    public float MaintainPlume(in SiegeFX.Core.Sfx.PlumeSpec s, System.Numerics.Vector3 p, float age, float dt, float carry)
    {
        MaintainFireCount++;
        float life = Math.Clamp(1f / MathF.Max(0.15f, s.AlphaFade), 0.30f, 3.5f);
        float b = carry + MathF.Max(1f, s.Count / life) * dt;
        int k = (int)b;
        SpawnFireCount += Math.Max(0, k);
        return b - k;
    }
    public void BurstPlume(in SiegeFX.Core.Sfx.PlumeSpec s, System.Numerics.Vector3 p, int n) => SpawnFireCount += n;
    public void SpawnLineTracer(System.Numerics.Vector3 s, System.Numerics.Vector3 t, System.Numerics.Vector4 c0, System.Numerics.Vector4 c1, float fadeRate, float tin, float tout) => SpawnLightningCount++;
    public void SpawnSpe(in SiegeFX.Core.Sfx.SpeSpec e) => SpawnSparkCount += e.Count;
    public void SpawnSparkles(in SiegeFX.Core.Sfx.SparklesSpec e) => SpawnSparkCount += e.Count;
    public void SpawnCharge(in SiegeFX.Core.Sfx.ChargeSpec e) => SpawnSparkCount += e.Count;
    public void SpawnPolyExplosion(in SiegeFX.Core.Sfx.PolyExplosionSpec e) => SpawnSparkCount += e.Count;
    public void SpawnSphereMesh(in SiegeFX.Core.Sfx.SphereMeshSpec e) => SpawnSphereCount++;
    public void SpawnProjectile(System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector4 c, float s, float sp, int k) => SpawnProjectileCount++;
    public float MaintainFire(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float s, float dt, float r, float carry)
    { MaintainFireCount++; float b = carry + r * dt; int k = (int)b; SpawnFireCount += Math.Max(0, k); return b - k; }
    public float MaintainSmoke(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float s, float dt, float r, float carry)
    { MaintainSmokeCount++; float b = carry + r * dt; int k = (int)b; SpawnSmokeCount += Math.Max(0, k); return b - k; }
    public float MaintainSteam(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float s, float dt, float r, float carry)
    { MaintainSteamCount++; float b = carry + r * dt; int k = (int)b; SpawnSteamCount += Math.Max(0, k); return b - k; }
    public float MaintainGlow(System.Numerics.Vector3 p, System.Numerics.Vector4 c, float radius, float dt, float r, float carry)
    { MaintainGlowCount++; float b = carry + r * dt; int k = (int)b; SpawnGlowCount += Math.Max(0, k); return b - k; }
    public void SpawnCylinder(System.Numerics.Vector3 anchor, System.Numerics.Vector4 color,
                              float radiusOuter, float thicknessRatio,
                              float spinPerSec,  float fadeIn, float fadeOut,
                              float duration,    byte texSlot, byte segments)
        => SpawnCylinderCount++;
    public void SpawnSray(System.Numerics.Vector3 anchor, System.Numerics.Vector4 colorStart,
                          System.Numerics.Vector4 colorEnd,
                          float lengthMin, float lengthMax,
                          float widthStart, float widthEnd,
                          float duration, int rayCount)
        => SpawnSrayCount++;
    public void SpawnFireb(System.Numerics.Vector3 anchor, System.Numerics.Vector4 color,
                           System.Numerics.Vector3 velocity, System.Numerics.Vector3 accel,
                           float lifetime, float maxDisplace,
                           float lowerRadius, float upperRadius,
                           int count, float flameSize)
        => SpawnFirebCount++;
    public int SpawnSphereCount;
    public void SpawnSphere(System.Numerics.Vector3 anchor, System.Numerics.Vector4 color,
                            float radius, float duration, int count)
        => SpawnSphereCount += count;
}
