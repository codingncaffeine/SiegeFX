using SiegeFX.Core.Assets;
using SiegeFX.Core.IO;
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
        "gas"  => DispatchGas(args[1..]),
        "region" => DispatchRegion(args[1..]),
        "world"  => DispatchWorld(args[1..]),
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
    Console.WriteLine("  siegefx gas  info    <file.gas>");
    Console.WriteLine("  siegefx gas  dump    <file.gas>");
    Console.WriteLine("  siegefx gas  fuzz    <tank>");
    Console.WriteLine("  siegefx region info  <map-tank> <region-path>");
    Console.WriteLine("  siegefx region fuzz  <map-tank>");
    Console.WriteLine("  siegefx region layout      <map-tank> <terrain-tank> <region-path>");
    Console.WriteLine("  siegefx region layout-fuzz <map-tank> <terrain-tank>");
    Console.WriteLine("  siegefx world  layout      <map-tank> <terrain-tank> [root-region]");
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
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx asp <info> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info" => CmdAspInfo(a[1..]),
        _      => UnknownCommand("asp " + a[0]),
    };
}

static int DispatchSno(string[] a)
{
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx sno <info> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info" => CmdSnoInfo(a[1..]),
        _      => UnknownCommand("sno " + a[0]),
    };
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
    if (a.Length == 0) { Console.Error.WriteLine("usage: siegefx region <info|fuzz|layout|layout-fuzz> ..."); return 1; }
    return a[0].ToLowerInvariant() switch
    {
        "info"        => CmdRegionInfo(a[1..]),
        "fuzz"        => CmdRegionFuzz(a[1..]),
        "layout"      => CmdRegionLayout(a[1..]),
        "layout-fuzz" => CmdRegionLayoutFuzz(a[1..]),
        "layout-diag" => CmdRegionLayoutDiag(a[1..]),
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
    Console.WriteLine($"Skeleton  : {mesh.SkeletonName}");
    Console.WriteLine($"Mesh      : {mesh.MeshName}");
    Console.WriteLine($"Vertices  : {mesh.Positions.Length}");
    Console.WriteLine($"Corners   : {mesh.Corners.Length}");
    Console.WriteLine($"Triangles : {mesh.TriangleCount}");

    var chunks = AspScanner.Scan(data);
    Console.WriteLine($"Chunks    : {chunks.Count}");
    foreach (var c in chunks)
        Console.WriteLine($"  0x{c.Offset:X8}  {c.Id}  v{c.Version}");
    return 0;
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
