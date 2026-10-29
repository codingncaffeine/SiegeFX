namespace SiegeFX.Core.Tank;

/// <summary>
/// DS1's tank priority layering: a tank whose header priority is above
/// <see cref="TankPriority.Factory"/> (Language, Expansion, Patch, User) shadows
/// the files of the same path in the factory tanks. Fan translations ship this
/// way: the Czech one, for instance, is six User tanks (<c>PredKouOsoby.dsres</c>,
/// <c>SingleWorld.dsres</c>, ...) dropped into <c>Resources\</c> that replace
/// <c>pcontent.gas</c>, the templates, the conversations and the fonts.
///
/// The engine opens its factory tanks by name in many places, so the overlay is
/// applied where they are indexed: every <see cref="TankReader"/> built after
/// <see cref="Configure"/> merges in the overlay files that belong to it.
/// </summary>
public static class TankOverlays
{
    static TankReader[] _active = Array.Empty<TankReader>();
    static readonly List<TankFile> _opened = new();

    /// <summary>Overlay readers in ascending priority; a later one wins a path.</summary>
    public static IReadOnlyList<TankReader> Active => _active;

    /// <summary>The overlay tanks found under <paramref name="resourcesDir"/>
    /// (recursively, like DS1's <c>Resources\Language\</c>): every <c>.dsres</c>
    /// with a priority above Factory, ordered by priority then by name.</summary>
    public static IReadOnlyList<(string Path, TankPriority Priority)> Find(string resourcesDir)
    {
        var found = new List<(string Path, TankPriority Priority)>();
        if (!Directory.Exists(resourcesDir)) return found;
        foreach (var path in Directory.EnumerateFiles(resourcesDir, "*.dsres", SearchOption.AllDirectories))
        {
            try
            {
                using var tank = TankFile.Open(path);
                if (tank.Header.Priority > TankPriority.Factory)
                    found.Add((path, tank.Header.Priority));
            }
            catch (Exception) { /* not a tank we can read: DS1 skips it too */ }
        }
        found.Sort((a, b) => a.Priority != b.Priority
            ? a.Priority.CompareTo(b.Priority)
            : StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(a.Path), Path.GetFileName(b.Path)));
        return found;
    }

    /// <summary>Opens <paramref name="paths"/> (ascending priority) as the active
    /// overlay. Readers built afterwards see them; readers built before do not.</summary>
    public static void Configure(IEnumerable<string> paths)
    {
        Clear();
        var readers = new List<TankReader>();
        foreach (var path in paths)
        {
            var tank = TankFile.Open(path);
            _opened.Add(tank);
            readers.Add(new TankReader(tank, applyOverlays: false));
        }
        _active = readers.ToArray();
    }

    public static void Clear()
    {
        _active = Array.Empty<TankReader>();
        foreach (var t in _opened) t.Dispose();
        _opened.Clear();
    }
}
