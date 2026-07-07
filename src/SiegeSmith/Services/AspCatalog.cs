using System;
using System.Collections.Generic;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeSmith.Services;

/// <summary>Resolves an aspect-model name (from a template's <c>aspect.model</c>) to a loaded
/// <see cref="AspMesh"/>, so the World Builder can preview placed props. Indexes every <c>.asp</c> in
/// the install tanks by bare name (DS1's model names match the .asp filename) and lazily loads +
/// caches meshes on demand. Owns the tanks it opens.</summary>
public sealed class AspCatalog : IDisposable
{
    private readonly List<TankFile> _tanks = new();
    private readonly Dictionary<string, (TankReader Reader, string Path)> _byName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AspMesh?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static AspCatalog Build(IEnumerable<string> tankPaths)
    {
        var c = new AspCatalog();
        c.Load(tankPaths);
        return c;
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
                if (f.EndsWith(".asp", StringComparison.OrdinalIgnoreCase))
                    _byName[BareName(f)] = (reader, f); // last wins; duplicates are the same mesh in mods
        }
    }

    /// <summary>Loads (and caches) the mesh for a model name, or null if absent/unparseable. Nulls are
    /// cached too, so an unresolved name only fails its lookup once.</summary>
    public AspMesh? Resolve(string modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return null;
        if (_cache.TryGetValue(modelName, out var cached)) return cached;
        AspMesh? mesh = null;
        if (_byName.TryGetValue(modelName, out var loc))
        {
            try { mesh = AspMesh.Load(loc.Reader.ExtractToMemory(loc.Path)); }
            catch { mesh = null; }
        }
        _cache[modelName] = mesh;
        return mesh;
    }

    private static string BareName(string path)
    {
        int slash = path.LastIndexOf('/');
        int start = slash < 0 ? 0 : slash + 1;
        int dot = path.LastIndexOf('.');
        int end = dot < start ? path.Length : dot;
        return path[start..end];
    }

    public void Dispose()
    {
        foreach (var f in _tanks) { try { f.Dispose(); } catch { } }
        _tanks.Clear();
        _cache.Clear();
    }
}
