using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>Resolves DS1 asset refs to tank paths. Templates reference models/anims/skrits
/// by short name (<c>m_c_eam_kg_pos_1</c>, <c>infinite_loop</c>) with no path. The engine
/// original used a VFS that merged all loaded tanks; we approximate that by indexing every
/// file in every supplied tank by basename and letting callers probe.
///
/// Basename collisions across tanks are rare in shipped data; when they happen, the
/// last-added tank wins (matches DS1's patch-tank override semantics where
/// <c>DevLogic.dsres</c> shadows <c>Logic.dsres</c>).</summary>
public sealed class AssetResolver
{
    readonly List<(TankReader Reader, string Name)> _tanks = new();
    readonly Dictionary<string, (TankReader Reader, string Path)> _byBasename =
        new(StringComparer.OrdinalIgnoreCase);

    public void Add(TankReader reader, string name)
    {
        _tanks.Add((reader, name));
        foreach (var path in reader.ListFiles())
        {
            var slash = path.LastIndexOf('/');
            var basename = slash < 0 ? path : path[(slash + 1)..];
            _byBasename[basename] = (reader, path);  // last-added wins
        }
    }

    /// <summary>Enumerates every indexed basename that starts with <paramref name="prefix"/>
    /// (case-insensitive). Used by the character creator to cycle only over hero
    /// texture variants that actually ship — the skin family mixes two- and
    /// three-digit numbering, so a hardcoded 1..N range would hit gaps.</summary>
    public IEnumerable<string> BasenamesWithPrefix(string prefix)
    {
        foreach (var key in _byBasename.Keys)
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                yield return key;
    }

    /// <summary>Looks up a file by exact basename (e.g. <c>m_c_eam_kg_pos_1.asp</c>).
    /// Returns false and leaves <paramref name="bytes"/> null if not found.</summary>
    public bool TryLoadByBasename(string basename, out byte[] bytes)
    {
        if (_byBasename.TryGetValue(basename, out var hit))
        {
            bytes = hit.Reader.ExtractToMemory(hit.Path);
            return true;
        }
        bytes = null!;
        return false;
    }

    /// <summary>Template aspect.model values are bare mesh names. Resolves by appending
    /// <c>.asp</c>.</summary>
    public bool TryLoadModel(string modelName, out byte[] bytes) =>
        TryLoadByBasename(modelName + ".asp", out bytes);

    /// <summary>Composes a DS1 chore-anim filename from its three pieces and resolves it:
    /// <paramref name="chorePrefix"/> (<c>a_c_eam_kg_fs</c>) + stance number (<c>1</c>) +
    /// <paramref name="suffix"/> (<c>dfs</c>) + <c>.prs</c> →
    /// <c>a_c_eam_kg_fs1_dfs.prs</c>. Returns false if the resulting file isn't in any
    /// indexed tank.</summary>
    public bool TryLoadChoreAnim(string chorePrefix, int stance, string suffix, out byte[] bytes)
    {
        var name = $"{chorePrefix}{stance}_{suffix}.prs";
        return TryLoadByBasename(name, out bytes);
    }

    /// <summary>Resolves a skrit ref. Template chore entries say <c>skrit = infinite_loop</c>
    /// (no path, no extension) and the shipped layout places them at
    /// <c>/art/animations/skrits/NAME.skrit</c>. We probe that canonical path first, then
    /// fall back to a basename index lookup so out-of-tree skrits still resolve.</summary>
    public bool TryLoadSkrit(string skritName, out byte[] bytes)
    {
        var basename = skritName + ".skrit";
        return TryLoadByBasename(basename, out bytes);
    }
}
