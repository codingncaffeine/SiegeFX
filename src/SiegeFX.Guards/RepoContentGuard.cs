namespace SiegeFX.Guards;

/// <summary>One file the repository should not carry, and why.</summary>
public sealed record RepoFinding(string Path, string Reason);

/// <summary>Finds game content committed to the repository: files in the
/// original game's formats (by extension, or by signature when renamed) and
/// any file large enough to be an asset. SiegeFX ships none of the original
/// game; players point it at their own install. A file that is original
/// work in one of these formats goes on the allowlist.</summary>
public static class RepoContentGuard
{
    /// <summary>Larger than anything the repository holds today (the biggest
    /// is a 2.8 MB PNG); a bigger file is far more likely an asset.</summary>
    public const long MaxFileBytes = 5L * 1024 * 1024;

    static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".dsres"] = "a game resource archive",
        [".dsmap"] = "a game map archive",
        [".dsmod"] = "a game mod archive",
        [".dssave"] = "an original-game save",
        [".dsasave"] = "an original-game autosave",
        [".dsparty"] = "an original-game party file",
        [".raw"] = "a game texture",
        [".asp"] = "a game mesh",
        [".prs"] = "a game animation",
        [".sno"] = "a game terrain node",
        [".skrit"] = "a game script",
        [".gas"] = "game data text",
        [".bik"] = "a Bink movie",
        [".smk"] = "a Smacker movie",
        [".mp3"] = "audio",
        [".wav"] = "audio",
    };

    /// <summary>Leading bytes of the game's binary formats, checked so a
    /// renamed file is still caught.</summary>
    static readonly (byte[] Bytes, string What)[] Signatures =
    [
        ("DSigTank"u8.ToArray(), "a game archive (tank)"),
        ("DSg2Tank"u8.ToArray(), "a game archive (tank)"),
        ("ipaR"u8.ToArray(), "a game texture (RAW)"),
        ("SNOD"u8.ToArray(), "a game terrain node (SNO)"),
        ("ANIM"u8.ToArray(), "a game animation (PRS)"),
        ("BMSH"u8.ToArray(), "a game mesh (ASP)"),
    ];

    /// <summary>Checks <paramref name="paths"/> (relative to
    /// <paramref name="root"/>, '/'-separated, as <c>git ls-files</c> lists
    /// them); paths in <paramref name="allow"/> are skipped.</summary>
    public static IReadOnlyList<RepoFinding> Check(string root, IEnumerable<string> paths, IReadOnlySet<string> allow)
    {
        var findings = new List<RepoFinding>();
        Span<byte> head = stackalloc byte[8];
        foreach (var path in paths)
        {
            if (allow.Contains(path)) continue;
            var full = Path.Combine(root, path);
            if (!File.Exists(full)) continue;   // deleted in the working tree
            if (Extensions.TryGetValue(Path.GetExtension(path), out var kind))
            {
                findings.Add(new RepoFinding(path, $"{kind} ({Path.GetExtension(path)})"));
                continue;
            }
            using var f = File.OpenRead(full);
            if (f.Length > MaxFileBytes)
            {
                findings.Add(new RepoFinding(path, $"{f.Length / (1024.0 * 1024):0.0} MB, over the {MaxFileBytes / (1024 * 1024)} MB limit"));
                continue;
            }
            int n = f.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            foreach (var (bytes, what) in Signatures)
                if (n >= bytes.Length && head[..bytes.Length].SequenceEqual(bytes))
                {
                    findings.Add(new RepoFinding(path, $"{what}, by its signature"));
                    break;
                }
        }
        return findings;
    }

    /// <summary>An allowlist file: one repository path per line; blank lines
    /// and lines starting with '#' are ignored. A missing file is empty.</summary>
    public static IReadOnlySet<string> ReadAllowList(string file)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(file)) return set;
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.Length > 0 && line[0] != '#') set.Add(line.Replace('\\', '/'));
        }
        return set;
    }
}
