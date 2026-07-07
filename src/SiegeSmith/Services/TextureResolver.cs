using System;
using System.Collections.Generic;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeSmith.Services;

/// <summary>Resolves DS1 texture names (as they appear in .asp <c>TextureNames</c> and .sno
/// <c>Surface.TextureName</c>) to decoded, sample-ready <see cref="SoftwareRenderer.Texture"/>s so
/// the 3D preview can show an asset the way it looks in-game rather than flat-shaded. Indexes every
/// <c>.raw</c> across the open tank plus the install's tanks by bare name (last-added wins, matching
/// DS1 patch-tank override), and caches both hits and misses. Owns the tanks it opens; dispose to
/// release them (a reader passed via <see cref="AddReader"/> is borrowed, not owned).</summary>
public sealed class TextureResolver : IDisposable
{
    private readonly List<TankFile> _owned = new();
    private readonly Dictionary<string, (TankReader Reader, string Path)> _byBare =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SoftwareRenderer.Texture?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Indexes the .raw entries of an already-open reader (e.g. the tank being browsed).
    /// The reader is not disposed by this resolver.</summary>
    public void AddReader(TankReader reader) => IndexRaw(reader);

    /// <summary>Opens a tank file and indexes its .raw entries. The file is owned and disposed with
    /// the resolver. Failures (locked/corrupt tank) are skipped silently.</summary>
    public void AddTankPath(string path)
    {
        TankFile file;
        try { file = TankFile.Open(path); }
        catch { return; }
        TankReader reader;
        try { reader = new TankReader(file); }
        catch { file.Dispose(); return; }
        _owned.Add(file);
        IndexRaw(reader);
    }

    private void IndexRaw(TankReader reader)
    {
        foreach (var f in reader.ListFiles())
            if (f.EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
                _byBare[BareName(f)] = (reader, f); // last-added wins
    }

    /// <summary>Resolves a texture name to a decoded texture, or null if it isn't present or fails
    /// to decode. Result (including null) is cached per bare name.</summary>
    public SoftwareRenderer.Texture? Resolve(string textureName)
    {
        if (string.IsNullOrEmpty(textureName)) return null;
        var bare = BareName(textureName);
        if (_cache.TryGetValue(bare, out var cached)) return cached;

        SoftwareRenderer.Texture? tex = null;
        if (_byBare.TryGetValue(bare, out var loc))
        {
            try
            {
                var img = RawImage.Load(loc.Reader.ExtractToMemory(loc.Path));
                tex = new SoftwareRenderer.Texture(img.Pixels, img.Width, img.Height);
            }
            catch { tex = null; }
        }
        _cache[bare] = tex;
        return tex;
    }

    /// <summary>Applies DS1's terrain texset substitution: a surface name's <c>_xxx_</c> placeholder
    /// is rebound to the node's texset abbreviation (e.g. <c>t_xxx_wall</c> + <c>grs01</c> →
    /// <c>t_grs01_wall</c>). Mirrors the engine's terrain texture resolution (RenderHost.ResolveTexName)
    /// so the World Builder preview matches in-game; names without the placeholder pass through
    /// unchanged, and an empty abbreviation leaves the placeholder in place (it simply won't resolve).</summary>
    public static string ApplyTexset(string raw, string texsetAbbr)
    {
        if (string.IsNullOrEmpty(texsetAbbr) || string.IsNullOrEmpty(raw)) return raw;
        int i = raw.IndexOf("_xxx_", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? raw : string.Concat(raw.AsSpan(0, i + 1), texsetAbbr, raw.AsSpan(i + 4));
    }

    private static string BareName(string path)
    {
        int slash = path.LastIndexOfAny(new[] { '/', '\\' });
        int start = slash < 0 ? 0 : slash + 1;
        int dot = path.LastIndexOf('.');
        int end = dot < start ? path.Length : dot;
        return path[start..end];
    }

    public void Dispose()
    {
        foreach (var f in _owned) { try { f.Dispose(); } catch { } }
        _owned.Clear();
        _byBare.Clear();
        _cache.Clear();
    }
}
