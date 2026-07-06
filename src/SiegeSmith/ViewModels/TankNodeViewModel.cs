using System.Collections.Generic;
using SiegeFX.Core.Tank;
using SiegeSmith.Mvvm;

namespace SiegeSmith.ViewModels;

/// <summary>A node in a tank's virtual filesystem tree — a directory (with <see cref="Children"/>)
/// or a file (with an <see cref="Entry"/>). Directory nodes are synthesised from the flat file
/// list, so empty directories don't appear (the tank index only enumerates files).</summary>
public sealed class TankNodeViewModel : ObservableObject
{
    // Segoe MDL2 Assets code points: Folder (E8B7) and Page (E7C3). Expressed as
    // numeric casts so the glyphs survive as plain ASCII in source.
    private static readonly string FolderGlyph = ((char)0xE8B7).ToString();
    private static readonly string FileGlyph = ((char)0xE7C3).ToString();

    public string Name { get; }
    /// <summary>'/'-rooted path within the tank (e.g. <c>/world/maps/map_world/main.gas</c>).</summary>
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public TankFileEntry? Entry { get; }
    public List<TankNodeViewModel> Children { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public TankNodeViewModel(string name, string fullPath, bool isDirectory, TankFileEntry? entry)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Entry = entry;
    }

    /// <summary>Segoe MDL2 Assets glyph for the row — folder vs page.</summary>
    public string Glyph => IsDirectory ? FolderGlyph : FileGlyph;
}
