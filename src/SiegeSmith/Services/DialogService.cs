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

    public static string? SaveFileAs(string suggestedName, string? rememberKey = null)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Extract file as",
            FileName = suggestedName,
            OverwritePrompt = true,
            InitialDirectory = (rememberKey is not null ? AppSettings.GetLastDir(rememberKey) : null) ?? "",
        };
        if (dlg.ShowDialog() != true) return null;
        if (rememberKey is not null) AppSettings.SaveLastDir(rememberKey, dlg.FileName);
        return dlg.FileName;
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

    public static string? OpenProjectFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open SiegeSmith project",
            Filter = "SiegeSmith project (*.ssproj)|*.ssproj|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    /// <summary>Generic open-file picker. <paramref name="filter"/> is a standard WPF dialog filter
    /// (e.g. "GAS files (*.gas)|*.gas|All files (*.*)|*.*"). <paramref name="rememberKey"/> makes the
    /// dialog open in the last directory used for that purpose.</summary>
    public static string? OpenFile(string title, string filter, string? rememberKey = null)
    {
        var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            InitialDirectory = (rememberKey is not null ? AppSettings.GetLastDir(rememberKey) : null) ?? "",
        };
        if (dlg.ShowDialog() != true) return null;
        if (rememberKey is not null) AppSettings.SaveLastDir(rememberKey, dlg.FileName);
        return dlg.FileName;
    }

    public static string? SaveProjectFile(string name)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save SiegeSmith project",
            FileName = string.IsNullOrWhiteSpace(name) ? "mod" : name,
            DefaultExt = ".ssproj",
            Filter = "SiegeSmith project (*.ssproj)|*.ssproj",
            OverwritePrompt = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
