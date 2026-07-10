using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SiegeFX.Core.Assets;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels;

/// <summary>Drives the Tank Explorer: builds the VFS tree for an open <see cref="TankDocument"/>,
/// runs search, offers asset-category quick-jumps, surfaces selection metadata as property rows,
/// and performs extraction.</summary>
public sealed class TankExplorerViewModel : ObservableObject, IDisposable
{
    private readonly TankDocument _doc;
    private readonly IReadOnlyList<string> _installTankPaths;
    private readonly List<TankNodeViewModel> _allFiles = new();
    private readonly Dictionary<string, TankNodeViewModel> _dirs = new(StringComparer.OrdinalIgnoreCase);

    // Lazily built the first time a 3D asset is previewed: indexes .raw across the open tank plus
    // the install's tanks so meshes/nodes can be shown textured. Owned here; disposed with the VM.
    private TextureResolver? _textures;
    private bool _texturesTried;

    /// <summary>Well-known asset homes in a DS1 tank's virtual filesystem. A category button
    /// appears only when at least one of its candidate paths exists in the open tank.</summary>
    private static readonly (string Label, string[] Paths)[] CategoryDefs =
    {
        ("Textures",   new[] { "/art/bitmaps" }),
        ("Meshes",     new[] { "/art/meshes" }),
        ("Animations", new[] { "/art/motion", "/art/animation" }),
        ("Sounds",     new[] { "/sound", "/sounds" }),
        ("World",      new[] { "/world" }),
        ("Shaders",    new[] { "/shaders" }),
    };

    public string TankName => _doc.Name;
    public string TankPath => _doc.Path;

    public ObservableCollection<TankNodeViewModel> Roots { get; } = new();
    public ObservableCollection<TankNodeViewModel> SearchResults { get; } = new();
    public ObservableCollection<PropertyRow> Properties { get; } = new();

    /// <summary>Asset-category quick-jumps present in this tank (may be empty).</summary>
    public IReadOnlyList<AssetCategory> Categories { get; }

    public RelayCommand ExtractSelectedCommand { get; }
    public RelayCommand ExtractTreeCommand { get; }
    public RelayCommand OpenExternallyCommand { get; }
    public RelayCommand ExportPngCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public RelayCommand CopyCrcCommand { get; }

    /// <summary>Raised with a short message for the shell status bar.</summary>
    public event Action<string>? Status;

    public TankExplorerViewModel(TankDocument doc, IReadOnlyList<string>? installTankPaths = null)
    {
        _doc = doc;
        _installTankPaths = installTankPaths ?? Array.Empty<string>();
        BuildTree();
        ExtractSelectedCommand = new RelayCommand(_ => ExtractSelected(), _ => HasSelectedFile);
        ExtractTreeCommand = new RelayCommand(_ => ExtractTree());
        OpenExternallyCommand = new RelayCommand(_ => OpenExternally(), _ => HasSelectedFile);
        ExportPngCommand = new RelayCommand(_ => ExportPng(), _ => IsTextureSelected);
        CopyPathCommand = new RelayCommand(_ => CopyText(SelectedNode?.FullPath), _ => SelectedNode is not null);
        CopyCrcCommand = new RelayCommand(_ => CopyText(SelectedNode?.Entry is { } e ? $"0x{e.Crc32:X8}" : null), _ => HasSelectedFile);
        Categories = CategoryDefs
            .Where(d => d.Paths.Any(_dirs.ContainsKey))
            .Select(d => new AssetCategory(d.Label, new RelayCommand(_ => JumpTo(d.Paths))))
            .ToList();
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
    private bool IsTextureSelected => SelectedNode is { IsDirectory: false } f &&
        f.Name.EndsWith(".raw", StringComparison.OrdinalIgnoreCase);
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
        UpdateViewer();
        ExtractSelectedCommand.RaiseCanExecuteChanged();
        OpenExternallyCommand.RaiseCanExecuteChanged();
        ExportPngCommand.RaiseCanExecuteChanged();
        CopyPathCommand.RaiseCanExecuteChanged();
        CopyCrcCommand.RaiseCanExecuteChanged();
    }

    // ── preview viewer ──────────────────────────────────────────
    private object? _currentViewer;
    /// <summary>The format-aware viewer for the selected file (texture / gas / text / hex), or
    /// null for a directory or an empty selection (the workspace shows the overview card).</summary>
    public object? CurrentViewer
    {
        get => _currentViewer;
        private set
        {
            if (ReferenceEquals(_currentViewer, value)) return;
            // Dispose the outgoing viewer so audio players / temp files / native handles release.
            if (_currentViewer is IDisposable old) old.Dispose();
            _currentViewer = value;
            OnPropertyChanged(nameof(CurrentViewer));
            OnPropertyChanged(nameof(HasViewer));
            OnPropertyChanged(nameof(ShowOverview));
        }
    }
    public bool HasViewer => _currentViewer is not null;
    public bool ShowOverview => _currentViewer is null;

    private void UpdateViewer()
    {
        if (SelectedNode is { IsDirectory: false } file)
        {
            try
            {
                var bytes = _doc.Reader.ExtractToMemory(file.FullPath);
                var ext = Path.GetExtension(file.Name).ToLowerInvariant();
                var textures = ext is ".asp" or ".sno" ? EnsureTextures() : null;
                var rig = ext == ".prs" ? ResolveRigMesh : (Func<string, SiegeFX.Core.Assets.AspMesh?>?)null;
                CurrentViewer = ViewerFactory.Create(file.Name, bytes, textures, rig);
            }
            catch (Exception ex)
            {
                CurrentViewer = null;
                Status?.Invoke($"Preview failed: {ex.Message}");
            }
        }
        else
        {
            CurrentViewer = null;
        }
    }

    // ED-15 — bare-name → path index over the open tank's .asp files, so a
    // .prs preview can find its paired rig (a_* clip → m_* mesh). Built lazily
    // on the first animation preview.
    private Dictionary<string, string>? _aspIndex;

    private SiegeFX.Core.Assets.AspMesh? ResolveRigMesh(string bareName)
    {
        try
        {
            if (_aspIndex is null)
            {
                _aspIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in _doc.Reader.ListFiles())
                {
                    if (!p.EndsWith(".asp", StringComparison.OrdinalIgnoreCase)) continue;
                    var bare = Path.GetFileNameWithoutExtension(p);
                    _aspIndex[bare] = p; // last wins — names are unique in practice
                }
            }
            if (!_aspIndex.TryGetValue(bareName, out var path))
            {
                // Prefix fallback — mesh names can be LONGER than the clip stem
                // (droog clips are a_c_eam_dg_* but the mesh is m_c_eam_dg_pos_a1).
                // Only species-specific candidates (≥4 segments) may prefix-match,
                // and the shortest matching name wins (base body over variants).
                if (bareName.Split('_').Length < 4) return null;
                string? bestBare = null;
                foreach (var (bare, p) in _aspIndex)
                    if (bare.StartsWith(bareName + "_", StringComparison.OrdinalIgnoreCase)
                        && (bestBare is null || bare.Length < bestBare.Length))
                    { bestBare = bare; path = p; }
                if (bestBare is null) return null;
            }
            if (path is null) return null;
            return SiegeFX.Core.Assets.AspMesh.Load(_doc.Reader.ExtractToMemory(path));
        }
        catch { return null; }
    }

    /// <summary>Builds (once) the texture resolver used to show 3D assets as they look in-game:
    /// the open tank's own .raw files plus every install tank. Built lazily on first model preview,
    /// so browsing non-3D files costs nothing. Null only if construction fails entirely.</summary>
    private TextureResolver? EnsureTextures()
    {
        if (_texturesTried) return _textures;
        _texturesTried = true;
        try
        {
            var r = new TextureResolver();
            r.AddReader(_doc.Reader);
            foreach (var p in _installTankPaths)
                if (!string.Equals(p, _doc.Path, StringComparison.OrdinalIgnoreCase))
                    r.AddTankPath(p);
            _textures = r;
        }
        catch { _textures = null; }
        return _textures;
    }

    // ── asset-category quick-jump ───────────────────────────────
    private void JumpTo(string[] candidates)
    {
        if (HasSearch) SearchText = ""; // ensure the tree (not search results) is showing
        foreach (var path in candidates)
        {
            if (!_dirs.TryGetValue(path, out var node)) continue;
            ExpandAncestors(node);
            node.IsExpanded = true;
            node.IsSelected = true;
            Status?.Invoke($"Jumped to {path}");
            return;
        }
        Status?.Invoke("That asset category isn't in this tank.");
    }

    private void ExpandAncestors(TankNodeViewModel node)
    {
        var path = node.FullPath;
        int slash;
        while ((slash = path.LastIndexOf('/')) > 0)
        {
            path = path[..slash];
            if (_dirs.TryGetValue(path, out var dir)) dir.IsExpanded = true;
        }
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
        Status?.Invoke($"{SearchResults.Count} match(es) for \"{q}\"{(capped ? " (capped)" : "")}");
    }

    // ── tree build ──────────────────────────────────────────────
    private void BuildTree()
    {
        TankNodeViewModel GetDir(string dirPath) // dirPath is '/'-rooted, e.g. "/world/maps"
        {
            if (_dirs.TryGetValue(dirPath, out var existing)) return existing;
            var name = dirPath[(dirPath.LastIndexOf('/') + 1)..];
            var node = new TankNodeViewModel(name, dirPath, true, null);
            _dirs[dirPath] = node;
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
        foreach (var d in _dirs.Values) SortChildren(d.Children);
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

    private void OpenExternally()
    {
        if (SelectedNode is not { IsDirectory: false } file) return;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "SiegeSmith", "open");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, file.Name);
            _doc.Extract(file.FullPath, dest);
            Process.Start(new ProcessStartInfo(dest) { UseShellExecute = true });
            Status?.Invoke($"Opened {file.Name} in its default app");
        }
        catch (Exception ex) { Status?.Invoke($"Open externally failed: {ex.Message}"); }
    }

    private void ExportPng()
    {
        if (SelectedNode is not { IsDirectory: false } file) return;
        var dest = DialogService.SaveFileAs(Path.GetFileNameWithoutExtension(file.Name) + ".png");
        if (dest is null) return;
        try
        {
            var img = RawImage.Load(_doc.Reader.ExtractToMemory(file.FullPath));
            int w = img.GetSurfaceWidth(0), h = img.GetSurfaceHeight(0), stride = w * 4;
            var slice = new byte[stride * h];
            Buffer.BlockCopy(img.Pixels, img.GetSurfaceOffset(0), slice, 0, slice.Length);
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, slice, stride);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(dest);
            enc.Save(fs);
            Status?.Invoke($"Exported {file.Name} to {dest}");
        }
        catch (Exception ex) { Status?.Invoke($"Export failed: {ex.Message}"); }
    }

    private void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); Status?.Invoke("Copied to clipboard"); }
        catch (Exception ex) { Status?.Invoke($"Copy failed: {ex.Message}"); }
    }

    /// <summary>Extracts the selected file to a temp path for a drag-out-to-Windows gesture and
    /// returns that path (or null if no file is selected / extraction fails).</summary>
    public string? PrepareDragOut()
    {
        if (SelectedNode is not { IsDirectory: false } file) return null;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "SiegeSmith", "drag");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, file.Name);
            _doc.Extract(file.FullPath, dest);
            return dest;
        }
        catch (Exception ex) { Status?.Invoke($"Drag failed: {ex.Message}"); return null; }
    }

    /// <summary>Marshals a status message back to the UI thread (extraction runs off-thread).</summary>
    private void Report(string message)
    {
        var app = Application.Current;
        if (app is not null) app.Dispatcher.Invoke(() => Status?.Invoke(message));
        else Status?.Invoke(message);
    }

    public void Dispose()
    {
        if (_currentViewer is IDisposable v) v.Dispose();
        _textures?.Dispose();
        _doc.Dispose();
    }
}

/// <summary>A name/value row shown in the Properties inspector.</summary>
public sealed record PropertyRow(string Name, string Value);

/// <summary>An asset-category quick-jump button (label + command that reveals its tree folder).</summary>
public sealed record AssetCategory(string Label, RelayCommand Jump);
