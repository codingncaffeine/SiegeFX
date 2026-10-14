using System.Security.Cryptography;
using System.Text;

namespace SiegeFX.Guards;

/// <summary>A string in the code (or a committed file) that copies game text.
/// <see cref="Key"/> identifies it without repeating the text: a short hash
/// of its normalized words, so the baseline file never stores game text.</summary>
public sealed record TextHit(string Where, string Literal, TextMatch Match)
{
    public string Key => $"{Where}\t{Fingerprint(Literal)}";

    public static string Fingerprint(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(' ', GameTextIndex.Words(s)))))[..16].ToLowerInvariant();
}

/// <summary>Compares the strings found against a committed baseline: hits
/// already recorded pass (existing cases, to be cleaned up deliberately),
/// new ones fail, and recorded ones no longer found are reported so the
/// baseline can shrink.</summary>
public static class TextGuard
{
    public static IReadOnlyList<TextHit> Scan(GameTextIndex index, IEnumerable<(string Where, string Text)> strings)
    {
        var hits = new List<TextHit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (where, text) in strings)
            if (index.Match(text) is { } m)
            {
                var hit = new TextHit(where, text, m);
                if (seen.Add(hit.Key)) hits.Add(hit);
            }
        return hits;
    }

    public static (IReadOnlyList<TextHit> New, IReadOnlyList<string> Cleared) Compare(IReadOnlyList<TextHit> hits, IReadOnlySet<string> baseline)
    {
        var keys = hits.Select(h => h.Key).ToHashSet(StringComparer.Ordinal);
        return (hits.Where(h => !baseline.Contains(h.Key)).ToList(),
                baseline.Where(k => !keys.Contains(k)).Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>Baseline lines are keys; '#' lines are comments.</summary>
    public static IReadOnlySet<string> ReadBaseline(string file) =>
        File.Exists(file)
            ? File.ReadLines(file).Where(l => l.Length > 0 && l[0] != '#').ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    public static void WriteBaseline(string file, IReadOnlyList<TextHit> hits)
    {
        var lines = new List<string>
        {
            "# Strings in the code or in goldens/ that copy the original game's text, recorded when",
            "# the text guard was added so only new copies fail. Each line is where the string is",
            "# and a hash of its words (never the text itself). Shrink this file by reading the",
            "# text from the game data instead; regenerate with tools/receipts.sh --update-baseline.",
        };
        lines.AddRange(hits.Select(h => h.Key).Distinct().Order(StringComparer.Ordinal));
        File.WriteAllLines(file, lines);
    }
}
