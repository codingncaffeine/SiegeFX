using System.Collections.ObjectModel;
using System.IO;
using SiegeSmith.Mvvm;

namespace SiegeSmith.ViewModels.ProjectFiles;

/// <summary>A real on-disk file or folder under a mod project's source tree. Supports inline
/// rename (via <see cref="IsEditing"/> + <see cref="EditName"/>) and tracks expand/select state.</summary>
public sealed class ProjectNodeViewModel : ObservableObject
{
    public bool IsDirectory { get; }

    private string _fullPath;
    public string FullPath { get => _fullPath; private set => SetProperty(ref _fullPath, value); }

    private string _name;
    public string Name { get => _name; private set => SetProperty(ref _name, value); }

    public ObservableCollection<ProjectNodeViewModel> Children { get; } = new();

    // Segoe MDL2 Assets: 0xE8B7 folder, 0xE7C3 document (numeric casts survive transit).
    public string Glyph => ((char)(IsDirectory ? 0xE8B7 : 0xE7C3)).ToString();

    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    private bool _isEditing;
    public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }

    private string _editName = "";
    public string EditName { get => _editName; set => SetProperty(ref _editName, value); }

    public ProjectNodeViewModel(string fullPath, bool isDir)
    {
        _fullPath = fullPath;
        IsDirectory = isDir;
        _name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    public void BeginEdit()
    {
        EditName = Name;
        IsEditing = true;
    }

    public void UpdatePath(string newFullPath)
    {
        FullPath = newFullPath;
        Name = Path.GetFileName(newFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
