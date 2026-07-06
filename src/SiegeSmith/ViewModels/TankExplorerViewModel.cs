using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels;

/// <summary>Drives the Tank Explorer: builds the VFS tree for an open <see cref="TankDocument"/>,
/// runs search, surfaces selection metadata as property rows, and performs extraction.</summary>
public sealed class TankExplorerViewModel : ObservableObject, IDisposable
{
    private readonly TankDocument _doc;
    private readonly List<TankNodeViewModel> _allFiles = new();

    public string TankName => _doc.Name;
    public string TankPath => _doc.Path;

    public ObservableCollection<TankNodeViewModel> Roots { get; } = new();
    public ObservableCollection<TankNodeViewModel> SearchResults { get; } = new();
    public ObservableCollection<PropertyRow> Properties { get; } = new();

    public RelayCommand ExtractSelectedCommand { get; }
    public RelayCommand ExtractTreeCommand { get; }

    /// <summary>Raised with a short message for the shell status bar.</summary>
    public event Action<string>? Status;

    public TankExplorerViewModel(TankDocument doc)
    {
        _doc = doc;
        BuildTree();
        ExtractSelectedCommand = new RelayCommand(_ => ExtractSelected(), _ => HasSelectedFile);
        ExtractTreeCommand = new RelayCommand(_ => ExtractTree());
        ShowTankProperties();
    }

    // ── selection ───────────────────────────────────────────────
    private TankNodeViewModel? _selectedNode;
    public TankNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set { if (SetProperty(ref _selectedNode, value)) OnSelectionChanged(); }
    }

    public bool HasSelectedFile => SelectedNode is { IsDirectory: false };
    public string SelectionTitle => SelectedNode?.Name ?? _doc.Name;
    public string SelectionSubtitle => SelectedNode?.FullPath ?? _doc.Path;

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelectedFile));
        OnPropertyChanged(nameof(SelectionTitle));
        OnPropertyChanged(nameof(SelectionSubtitle));
        if (SelectedNode is null) ShowTankProperties();
        else if (SelectedNode.IsDirectory) ShowDirectoryProperties(SelectedNode);
        else ShowFileProperties(SelectedNode);
        ExtractSelectedCommand.RaiseCanExecuteChanged();
    }

    // ── search ──────────────────────────────────────────────────
    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) { OnPropertyChanged(nameof(HasSearch)); RunSearch(); } }
    }
    public bool HasSearch => !string.IsNullOrWhiteSpace(_searchText);

    private const int MaxResults = 3000;

    private void RunSearch()
    {
        SearchResults.Clear();
        if (!HasSearch) return;
        var q = _searchText.Trim();
        bool capped = false;
        foreach (var f in _allFiles)
        {
            if (!f.FullPath.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            if (SearchResults.Count >= MaxResults) { capped = true; break; }
            SearchResults.Add(f);
        }
        Status?.Invoke($"{SearchResults.Count} match(es) for “{q}”{(capped ? " (capped)" : "")}");
    }

    // ── tree build ──────────────────────────────────────────────
    private void BuildTree()
    {
        var dirs = new Dictionary<string, TankNodeViewModel>(StringComparer.OrdinalIgnoreCase);

        TankNodeViewModel GetDir(string dirPath) // dirPath is '/'-rooted, e.g. "/world/maps"
        {
            if (dirs.TryGetValue(dirPath, out var existing)) return existing;
            var name = dirPath[(dirPath.LastIndexOf('/') + 1)..];
            var node = new TankNodeViewModel(name, dirPath, true, null);
            dirs[dirPath] = node;
            var slash = dirPath.LastIndexOf('/');
            var parent = slash <= 0 ? "" : dirPath[..slash];
            if (parent.Length == 0) Roots.Add(node);
            else GetDir(parent).Children.Add(node);
            return node;
        }

        foreach (var path in _doc.ListFiles())
        {
            if (path.Length <= 1) continue;
            _doc.Reader.TryGetFile(path, out var entry);
            var slash = path.LastIndexOf('/');
            var name = path[(slash + 1)..];
            var file = new TankNodeViewModel(name, path, false, entry);
            _allFiles.Add(file);
            var dir = slash <= 0 ? "" : path[..slash];
            if (dir.Length == 0) Roots.Add(file);
            else GetDir(dir).Children.Add(file);
        }

        SortChildren(Roots);
        foreach (var d in dirs.Values) SortChildren(d.Children);
    }

    private static void SortChildren(IList<TankNodeViewModel> nodes)
    {
        var sorted = nodes.OrderByDescending(n => n.IsDirectory)
                          .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                          .ToList();
        nodes.Clear();
        foreach (var n in sorted) nodes.Add(n);
    }

    // ── property rows ───────────────────────────────────────────
    private void Add(string name, string value) => Properties.Add(new PropertyRow(name, value));

    private void ShowTankProperties()
    {
        Properties.Clear();
        var h = _doc.Header;
        Add("Tank", _doc.Name);
        Add("Path", _doc.Path);
        Add("Size", Format.Bytes(_doc.SizeBytes));
        Add("Files", _doc.FileCount.ToString("N0"));
        Add("Directories", _doc.DirCount.ToString("N0"));
        if (_doc.InvalidFileCount > 0) Add("Invalid files", _doc.InvalidFileCount.ToString("N0"));
        Add("Product", h.IsDs1 ? "Dungeon Siege" : h.IsDs2 ? "Dungeon Siege II" : h.ProductId.ToString());
        Add("Priority", h.Priority.ToString());
        if (!string.IsNullOrWhiteSpace(h.TitleText)) Add("Title", h.TitleText);
        if (!string.IsNullOrWhiteSpace(h.AuthorText)) Add("Author", h.AuthorText);
        if (!string.IsNullOrWhiteSpace(h.CopyrightText)) Add("Copyright", h.CopyrightText);
        if (!string.IsNullOrWhiteSpace(h.BuildText)) Add("Build", h.BuildText);
        var built = h.UtcBuildTime.ToDateTime();
        if (built > DateTime.MinValue) Add("Built (UTC)", built.ToString("u"));
        Add("Version", h.ProductVersion.ToString());
        Add("Creator", h.CreatorId.ToString());
        Add("GUID", h.Guid.ToString());
    }

    private void ShowDirectoryProperties(TankNodeViewModel node)
    {
        Properties.Clear();
        int files = 0;
        long size = 0;
        CountTree(node, ref files, ref size);
        Add("Folder", node.Name);
        Add("Path", node.FullPath);
        Add("Files (recursive)", files.ToString("N0"));
        Add("Size (recursive)", Format.Bytes(size));
    }

    private static void CountTree(TankNodeViewModel node, ref int files, ref long size)
    {
        foreach (var c in node.Children)
        {
            if (c.IsDirectory) CountTree(c, ref files, ref size);
            else { files++; size += c.Entry?.Size ?? 0; }
        }
    }

    private void ShowFileProperties(TankNodeViewModel node)
    {
        Properties.Clear();
        var e = node.Entry!;
        Add("Name", node.Name);
        Add("Path", node.FullPath);
        Add("Size", Format.Bytes(e.Size));
        if (e.IsCompressed && e.Compressed is not null)
        {
            Add("Format", e.Format.ToString());
            Add("Packed", Format.Bytes(e.Compressed.CompressedSize));
            var ratio = e.Size == 0 ? 0 : 100.0 * e.Compressed.CompressedSize / e.Size;
            Add("Compression", $"{ratio:F1}% of original, {e.Compressed.NumChunks} chunk(s)");
        }
        else
        {
            Add("Format", "Raw (uncompressed)");
        }
        Add("CRC32", $"0x{e.Crc32:X8}");
        var t = e.FileTime.ToDateTime();
        if (t > DateTime.MinValue) Add("Modified (UTC)", t.ToString("u"));
        if (e.IsInvalid) Add("Flags", "INVALID — extracts empty");
    }

    // ── extraction ──────────────────────────────────────────────
    private void ExtractSelected()
    {
        if (SelectedNode is not { IsDirectory: false } file) return;
        var dest = DialogService.SaveFileAs(file.Name);
        if (dest is null) return;
        try
        {
            _doc.Extract(file.FullPath, dest);
            Status?.Invoke($"Extracted {file.Name} to {dest}");
        }
        catch (Exception ex) { Status?.Invoke($"Extract failed: {ex.Message}"); }
    }

    private void ExtractTree()
    {
        var node = SelectedNode;
        var prefix = node is { IsDirectory: true } ? node.FullPath : "";
        var label = prefix.Length == 0 ? "the whole tank" : node!.Name;
        var destRoot = DialogService.PickFolder($"Choose a folder to extract {label} into");
        if (destRoot is null) return;

        Status?.Invoke($"Extracting {label}…");
        Task.Run(() =>
        {
            try
            {
                int n = _doc.ExtractTree(prefix, destRoot);
                Report($"Extracted {n:N0} file(s) to {destRoot}");
            }
            catch (Exception ex) { Report($"Extract failed: {ex.Message}"); }
        });
    }

    /// <summary>Marshals a status message back to the UI thread (extraction runs off-thread).</summary>
    private void Report(string message)
    {
        var app = Application.Current;
        if (app is not null) app.Dispatcher.Invoke(() => Status?.Invoke(message));
        else Status?.Invoke(message);
    }

    public void Dispose() => _doc.Dispose();
}

/// <summary>A name/value row shown in the Properties inspector.</summary>
public sealed record PropertyRow(string Name, string Value);
