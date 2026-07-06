using System.Text;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.Viewers;

/// <summary>Plain-text viewer for gas, skrit, config, and other text assets. Decodes as UTF-8
/// (DS1 ships ASCII/UTF-8) and caps very large files so the TextBox stays responsive. A
/// structured gas tree viewer arrives as a later Phase 2 splinter; this keeps everything
/// readable in the meantime.</summary>
public sealed class TextViewerViewModel : ObservableObject
{
    private const int MaxChars = 512 * 1024;

    public string Name { get; }
    public string Text { get; }
    public string Info { get; }

    public TextViewerViewModel(string name, byte[] bytes)
    {
        Name = name;
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
