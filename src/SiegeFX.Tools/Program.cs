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
