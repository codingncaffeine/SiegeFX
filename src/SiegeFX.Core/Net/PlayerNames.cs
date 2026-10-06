using System.Text;

namespace SiegeFX.Core.Net;

/// <summary>The player's name as an online service is given it.</summary>
public static class PlayerNames
{
    /// <summary>The name trimmed and cut to <paramref name="maxUtf8Bytes"/> in whole
    /// characters (a cut never splits one, so the service never gets a broken
    /// UTF-8 sequence); "Player" when nothing is left.</summary>
    public static string Fit(string? name, int maxUtf8Bytes)
    {
        var fit = new StringBuilder();
        int bytes = 0;
        foreach (var r in (name ?? "").Trim().EnumerateRunes())
        {
            bytes += r.Utf8SequenceLength;
            if (bytes > maxUtf8Bytes) break;
            fit.Append(r.ToString());
        }
        var cut = fit.ToString().TrimEnd();
        return cut.Length > 0 ? cut : "Player";
    }
}
