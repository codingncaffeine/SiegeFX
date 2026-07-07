using System.Text;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.Viewers;

/// <summary>Plain-text viewer for skrit, config, and other text assets — and the fallback for a
/// .gas file that fails to parse, in which case <see cref="Error"/> carries the parser's message
/// so the problem is visible rather than silently swallowed. Caps very large files.</summary>
public sealed class TextViewerViewModel : ObservableObject
{
    private const int MaxChars = 512 * 1024;

    public string Name { get; }
    public string Text { get; }
    public string Info { get; }
    public string? Error { get; }
    public bool HasError => Error is not null;

    public TextViewerViewModel(string name, byte[] bytes, string? error = null)
    {
        Name = name;
        Error = error;
        var text = new UTF8Encoding(false, false).GetString(bytes);
        if (text.Length > MaxChars)
        {
            text = text[..MaxChars];
            Info = $"{Format.Bytes(bytes.Length)} — showing first {MaxChars:N0} chars";
        }
        else
        {
            Info = Format.Bytes(bytes.Length);
        }
        Text = text;
    }
}
