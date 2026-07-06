namespace SiegeSmith.Services;

/// <summary>Small display-formatting helpers shared across view-models.</summary>
public static class Format
{
    /// <summary>Human-readable byte size (e.g. "1.4 MB", "912 B").</summary>
    public static string Bytes(long n)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = n;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{n:N0} {units[i]}" : $"{v:N1} {units[i]}";
    }
}
