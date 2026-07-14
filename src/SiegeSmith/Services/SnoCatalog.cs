using System;
using System.Collections.Generic;
using System.Globalization;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeSmith.Services;

/// <summary>An SNO mesh available in the World Builder's node palette.</summary>
public sealed record SnoMeshEntry(uint MeshGuid, string Name);

/// <summary>A shipped region discovered in the install (its <c>terrain_nodes/nodes.gas</c>), offered
/// in the World Builder so a user can open a region by name instead of hunting for the file — the
/// tool already knows where the game lives.</summary>
public sealed record RegionEntry(string Region, string Map)
{
    public string Display => Map.Length > 0 ? $"{Map} / {Region}" : Region;
}

/// <summary>Catalogue of terrain SNO meshes discovered across a Dungeon Siege install's tanks.
/// Builds the palette (mesh_guid → friendly name, only for meshes whose .sno is actually present)
/// and lazily resolves + caches a parsed <see cref="SnoModel"/> per mesh_guid for preview and
/// door-graph layout. Owns the tanks it opens; dispose to release them.
///
/// DS1 splits the mapping: <c>/world/global/siege_nodes/**/*.gas</c> files carry
/// <c>[mesh_file*]</c> blocks (bare name → guid) and the meshes live at
/// <c>/art/terrain/&lt;set&gt;/&lt;name&gt;.sno</c>. We merge both across every tank.</summary>
public sealed class SnoCatalog : IDisposable
{
    private readonly List<TankFile> _tanks = new();
    private readonly Dictionary<uint, string> _guidToBare = new();
    private readonly Dictionary<string, (TankReader Reader, string Path)> _bareToSno =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, SnoModel?> _snoCache = new();
    private readonly List<(RegionEntry Entry, TankReader Reader, string Path)> _regions = new();

    public IReadOnlyList<SnoMeshEntry> Meshes { get; private set; } = Array.Empty<SnoMeshEntry>();
    public IReadOnlyList<RegionEntry> Regions { get; private set; } = Array.Empty<RegionEntry>();

    /// <summary>Opens every tank in <paramref name="tankPaths"/> and builds the catalogue.
    /// Safe to call on a background thread — no WPF types touched.</summary>
    public static SnoCatalog Build(IEnumerable<string> tankPaths)
    {
        var cat = new SnoCatalog();
        cat.Load(tankPaths);
        return cat;
    }

    private void Load(IEnumerable<string> tankPaths)
    {
        foreach (var path in tankPaths)
        {
            TankFile file;
            try { file = TankFile.Open(path); }
            catch { continue; }
            TankReader reader;
            try { reader = new TankReader(file); }
            catch { file.Dispose(); continue; }
            _tanks.Add(file);

            foreach (var f in reader.ListFiles())
            {
                if (f.EndsWith(".sno", StringComparison.OrdinalIgnoreCase))
                    _bareToSno[BareName(f)] = (reader, f);
                else if (f.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase))
                    IndexRegion(reader, f);
                else if (f.EndsWith(".gas", StringComparison.OrdinalIgnoreCase) &&
                         f.Contains("/siege_nodes/", StringComparison.OrdinalIgnoreCase))
                    IndexMeshFileGas(reader, f);
            }
        }

        var list = new List<SnoMeshEntry>();
        foreach (var (guid, bare) in _guidToBare)
            if (_bareToSno.ContainsKey(bare))
                list.Add(new SnoMeshEntry(guid, bare));
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Meshes = list;

        _regions.Sort((a, b) => string.Compare(a.Entry.Display, b.Entry.Display, StringComparison.OrdinalIgnoreCase));
        var regs = new List<RegionEntry>(_regions.Count);
        foreach (var r in _regions) regs.Add(r.Entry);
        Regions = regs;
    }

    private void IndexRegion(TankReader reader, string path)
    {
        // path: …/world/maps/<map>/regions/<region>/terrain_nodes/nodes.gas
        var parts = path.Split('/');
        string map = "", region = "";
        for (int i = 0; i + 1 < parts.Length; i++)
        {
            if (parts[i].Equals("maps", StringComparison.OrdinalIgnoreCase)) map = parts[i + 1];
            else if (parts[i].Equals("regions", StringComparison.OrdinalIgnoreCase)) region = parts[i + 1];
        }
        if (region.Length == 0) region = path;
        _regions.Add((new RegionEntry(region, map), reader, path));
    }

    /// <summary>Extracts the raw nodes.gas bytes for a discovered region, or null on failure.</summary>
    public byte[]? ReadRegionNodes(RegionEntry entry)
    {
        foreach (var r in _regions)
            if (r.Entry.Equals(entry))
            {
                try { return r.Reader.ExtractToMemory(r.Path); }
                catch { return null; }
            }
        return null;
    }

    /// <summary>Reads another file in the same region folder as <paramref name="entry"/>, e.g.
    /// <c>"objects/actor.gas"</c> — the region root is the parent of <c>terrain_nodes/</c>.
    /// Returns null when the region or sibling is absent.</summary>
    public byte[]? ReadRegionSibling(RegionEntry entry, string siblingRelative)
    {
        foreach (var r in _regions)
            if (r.Entry.Equals(entry))
            {
                int idx = r.Path.LastIndexOf("/terrain_nodes/", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return null;
                string sibling = r.Path[..idx] + "/" + siblingRelative.TrimStart('/');
                try { return r.Reader.ExtractToMemory(sibling); }
                catch { return null; }
            }
        return null;
    }

    private void IndexMeshFileGas(TankReader reader, string path)
    {
        byte[] bytes;
        try { bytes = reader.ExtractToMemory(path); }
        catch { return; }
        GasDocument doc;
        try { doc = GasDocument.Load(bytes); }
        catch { return; }
        foreach (var root in doc.Roots) IndexNode(root);
    }

    private void IndexNode(GasNode node)
    {
        if (node.Header.StartsWith("mesh_file", StringComparison.OrdinalIgnoreCase))
        {
            string? filename = null;
            uint guid = 0;
            foreach (var a in node.Attributes)
            {
                if (a.Name.Equals("filename", StringComparison.OrdinalIgnoreCase)) filename = a.Value;
                else if (a.Name.Equals("guid", StringComparison.OrdinalIgnoreCase)) guid = ParseHex(a.Value);
            }
            if (guid != 0 && !string.IsNullOrEmpty(filename))
                _guidToBare.TryAdd(guid, filename!);
        }
        foreach (var c in node.Children) IndexNode(c);
    }

    /// <summary>Loads (and caches) the SnoModel for a mesh_guid, or null if unavailable. The
    /// cache stores nulls too, so a missing mesh is only looked up once.</summary>
    public SnoModel? Resolve(uint meshGuid)
    {
        if (_snoCache.TryGetValue(meshGuid, out var cached)) return cached;
        SnoModel? model = null;
        if (_guidToBare.TryGetValue(meshGuid, out var bare) &&
            _bareToSno.TryGetValue(bare, out var loc))
        {
            try { model = SnoModel.Load(loc.Reader.ExtractToMemory(loc.Path)); }
            catch { model = null; }
        }
        _snoCache[meshGuid] = model;
        return model;
    }

    public string NameOf(uint meshGuid) =>
        _guidToBare.TryGetValue(meshGuid, out var bare) ? bare : $"0x{meshGuid:X8}";

    /// <summary>SS-BLENDER — registers a loose custom terrain tile (a generated or imported
    /// <c>.sno</c> living in the assets folder, outside any tank) so it is immediately
    /// placeable from the mesh palette. Re-registering the same guid replaces the cached
    /// model, so a re-import updates the tile in place.</summary>
    public void RegisterCustom(uint meshGuid, string bareName, SnoModel model)
    {
        _guidToBare[meshGuid] = bareName;
        _snoCache[meshGuid] = model;
        bool present = false;
        foreach (var m in Meshes)
            if (m.MeshGuid == meshGuid) { present = true; break; }
        if (!present)
        {
            var list = new List<SnoMeshEntry>(Meshes) { new(meshGuid, bareName) };
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Meshes = list;
        }
    }

    private static string BareName(string path)
    {
        int slash = path.LastIndexOf('/');
        int start = slash < 0 ? 0 : slash + 1;
        int dot = path.LastIndexOf('.');
        int end = dot < start ? path.Length : dot;
        return path[start..end];
    }

    private static uint ParseHex(string text)
    {
        var v = text.Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) v = v[2..];
        return uint.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u) ? u : 0;
    }

    public void Dispose()
    {
        foreach (var f in _tanks) { try { f.Dispose(); } catch { } }
        _tanks.Clear();
        _snoCache.Clear();
    }
}
