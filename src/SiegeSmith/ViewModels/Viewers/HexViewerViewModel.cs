using System;
using System.Text;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.Viewers;

/// <summary>Classic offset / 16-byte-hex / ASCII hex dump, used as the fallback viewer for
/// binary files (meshes, animations, fonts, …). Capped so a huge file doesn't build a giant
/// string on the UI thread.</summary>
public sealed class HexViewerViewModel : ObservableObject
{
    private const int MaxBytes = 64 * 1024;

    public string Name { get; }
    public string HexText { get; }
    public string Info { get; }

    public HexViewerViewModel(string name, byte[] bytes)
    {
        Name = name;
        int n = Math.Min(bytes.Length, MaxBytes);
        var sb = new StringBuilder(n * 4);
        for (int off = 0; off < n; off += 16)
        {
            sb.Append(off.ToString("X8")).Append("   ");
            int len = Math.Min(16, n - off);
            for (int i = 0; i < 16; i++)
            {
                sb.Append(i < len ? bytes[off + i].ToString("X2") + ' ' : "   ");
                if (i == 7) sb.Append(' ');
            }
            sb.Append("  ");
            for (int i = 0; i < len; i++)
            {
                byte b = bytes[off + i];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            sb.Append('\n');
        }
        HexText = sb.ToString();
        Info = bytes.Length > MaxBytes
            ? $"{Format.Bytes(bytes.Length)} — showing first {Format.Bytes(MaxBytes)}"
            : Format.Bytes(bytes.Length);
    }
}
