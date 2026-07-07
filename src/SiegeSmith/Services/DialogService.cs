using Microsoft.Win32;

namespace SiegeSmith.Services;

/// <summary>Thin wrappers over the WPF common dialogs so view-models can request files
/// and folders without referencing window types directly.</summary>
public static class DialogService
{
    public static string? OpenTankFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open a Dungeon Siege tank",
            Filter = "Dungeon Siege tanks (*.dsres;*.dsmap;*.dsmod)|*.dsres;*.dsmap;*.dsmod|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public static string? SaveFileAs(string suggestedName)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Extract file as",
            FileName = suggestedName,
            OverwritePrompt = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public static string? PickFolder(string title)
    {
        var dlg = new OpenFolderDialog { Title = title };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public static string? SaveTankFile(string suggestedTitle)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save tank as",
            FileName = string.IsNullOrWhiteSpace(suggestedTitle) ? "mod" : suggestedTitle,
            DefaultExt = ".dsres",
            Filter = "Resource tank (*.dsres)|*.dsres|Map tank (*.dsmap)|*.dsmap|Mod tank (*.dsmod)|*.dsmod",
            OverwritePrompt = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
