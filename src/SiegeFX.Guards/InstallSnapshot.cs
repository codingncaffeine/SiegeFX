using System.Globalization;

namespace SiegeFX.Guards;

/// <summary>Every file in a Dungeon Siege install with its size and
/// last-write time, taken before the receipts run and compared after it:
/// SiegeFX reads the original game's files and must never change them.</summary>
public sealed class InstallSnapshot
{
    public sealed record Entry(string Path, long Size, long WriteTicksUtc);

    public IReadOnlyDictionary<string, Entry> Files { get; }

    InstallSnapshot(IReadOnlyDictionary<string, Entry> files) => Files = files;

    public static InstallSnapshot Take(string root)
    {
        var files = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var full in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
        {
            var info = new FileInfo(full);
            var rel = Path.GetRelativePath(root, full).Replace('\\', '/');
            files[rel] = new Entry(rel, info.Length, info.LastWriteTimeUtc.Ticks);
        }
        return new InstallSnapshot(files);
    }

    public void Write(string file) =>
        File.WriteAllLines(file, Files.Values.OrderBy(e => e.Path, StringComparer.Ordinal)
            .Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Size}\t{e.WriteTicksUtc}\t{e.Path}")));

    public static InstallSnapshot Read(string file)
    {
        var files = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(file))
        {
            var p = line.Split('\t', 3);
            if (p.Length != 3) throw new InvalidDataException($"bad snapshot line: {line}");
            files[p[2]] = new Entry(p[2], long.Parse(p[0], CultureInfo.InvariantCulture), long.Parse(p[1], CultureInfo.InvariantCulture));
        }
        return new InstallSnapshot(files);
    }

    /// <summary>Files added, removed or changed (size or write time) since <paramref name="before"/>.</summary>
    public IReadOnlyList<string> ChangesSince(InstallSnapshot before)
    {
        var changes = new List<string>();
        foreach (var (path, e) in Files.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (!before.Files.TryGetValue(path, out var old)) changes.Add($"added    {path}");
            else if (old.Size != e.Size || old.WriteTicksUtc != e.WriteTicksUtc) changes.Add($"changed  {path}");
        foreach (var path in before.Files.Keys.Where(p => !Files.ContainsKey(p)).Order(StringComparer.Ordinal))
            changes.Add($"removed  {path}");
        return changes;
    }
}
