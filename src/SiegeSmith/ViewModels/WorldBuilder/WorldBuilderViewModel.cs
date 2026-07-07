using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SiegeFX.Core.Assets;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.WorldBuilder;

/// <summary>Drives the World Builder: loads the install's SNO catalogue, exposes a searchable
/// node palette, lets the user place an anchor node and door-connect further nodes, and renders
/// the composed region live. The region round-trips through real <c>nodes.gas</c> — the same text
/// the engine loads and we save — so the preview is faithful to the shipped world.</summary>
public sealed class WorldBuilderViewModel : ObservableObject, IDisposable
{
    private SnoCatalog? _catalog;
    private TextureResolver? _textures;
    private readonly BuilderRegion _region = new();
    private readonly HashSet<uint> _usedGuids = new();
    private uint _nextGuid = 0x00010001;

    // ── loading / status ────────────────────────────────────────
    private string _status = "Loading SNO catalogue from the install…";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private bool _isLoading = true;
    public bool IsLoading
    {
        get => _isLoading;
        private set { if (SetProperty(ref _isLoading, value)) OnPropertyChanged(nameof(IsReady)); }
    }
    public bool IsReady => !_isLoading;

    // ── palette ─────────────────────────────────────────────────
    private readonly List<SnoMeshEntry> _allMeshes = new();
    public ObservableCollection<SnoMeshEntry> Palette { get; } = new();

    private string _search = "";
    public string SearchText { get => _search; set { if (SetProperty(ref _search, value)) RefreshPalette(); } }

    private SnoMeshEntry? _selectedMesh;
    public SnoMeshEntry? SelectedMesh { get => _selectedMesh; set { if (SetProperty(ref _selectedMesh, value)) OnMeshSelected(); } }

    // ── installed regions (open by name, no path hunting) ───────
    public ObservableCollection<RegionEntry> InstallRegions { get; } = new();
    private RegionEntry? _selectedInstallRegion;
    public RegionEntry? SelectedInstallRegion
    {
        get => _selectedInstallRegion;
        set { if (SetProperty(ref _selectedInstallRegion, value) && value is not null) LoadInstallRegion(value); }
    }

    public ObservableCollection<DoorRow> TargetDoors { get; } = new();
    private DoorRow? _selectedTargetDoor;
    public DoorRow? SelectedTargetDoor { get => _selectedTargetDoor; set { if (SetProperty(ref _selectedTargetDoor, value)) RaiseCommands(); } }

    // ── placed nodes ────────────────────────────────────────────
    public ObservableCollection<NodeRow> Nodes { get; } = new();
    private NodeRow? _selectedNode;
    public NodeRow? SelectedNode { get => _selectedNode; set { if (SetProperty(ref _selectedNode, value)) OnNodeSelected(); } }

    public ObservableCollection<DoorRow> SourceDoors { get; } = new();
    private DoorRow? _selectedSourceDoor;
    public DoorRow? SelectedSourceDoor { get => _selectedSourceDoor; set { if (SetProperty(ref _selectedSourceDoor, value)) RaiseCommands(); } }

    // Free doors on OTHER placed nodes — pick one to link the selected node's free door to (close a loop).
    public ObservableCollection<NodeDoorRow> OtherFreeDoors { get; } = new();
    private NodeDoorRow? _selectedOtherDoor;
    public NodeDoorRow? SelectedOtherDoor { get => _selectedOtherDoor; set { if (SetProperty(ref _selectedOtherDoor, value)) RaiseCommands(); } }

    // Per-node texture set — pick another texset used in the region to re-skin the selected node live.
    public ObservableCollection<string> AvailableTexsets { get; } = new();
    public string? SelectedNodeTexset
    {
        get => _selectedNode is null ? null : _region.Find(_selectedNode.Guid)?.TexsetAbbr;
        set
        {
            if (_selectedNode is null) return;
            var n = _region.Find(_selectedNode.Guid);
            var v = value ?? "";
            if (n is null || n.TexsetAbbr == v) return;
            PushUndo();
            n.TexsetAbbr = v;
            Render();
            Status = $"Texset for {_catalog?.NameOf(n.MeshGuid)} → '{v}'.";
        }
    }

    public int NodeCount => _region.Nodes.Count;
    public bool IsEmpty => _region.Nodes.Count == 0;

    // ── viewport ────────────────────────────────────────────────
    private BitmapSource? _image;
    public BitmapSource? Image { get => _image; private set => SetProperty(ref _image, value); }

    private float _yaw = 0.7f, _pitch = 0.5f, _dist;
    private Vector3 _center;
    private Vector3 _pan; // right-drag pan offset added to the framed centre
    private Vector3[] _pickVerts = System.Array.Empty<Vector3>(); // world-space tris (3/verts) for click-picking
    private uint[] _pickGuid = System.Array.Empty<uint>();         // node GUID per pick triangle
    private float _radius = 1f;
    private int _vw = 800, _vh = 600;
    private bool _wireframe;
    public string WireframeLabel => _wireframe ? "Solid" : "Wireframe";

    private bool _textured = true; // default to the in-game look; effective only when textures resolve
    public bool Textured
    {
        get => _textured;
        set { if (SetProperty(ref _textured, value)) { OnPropertyChanged(nameof(TexturedLabel)); Render(); } }
    }
    public string TexturedLabel => _textured ? "Flat" : "Textured"; // name the action, matching Wireframe

    // ── startable map export ────────────────────────────────────
    private readonly IReadOnlyList<string> _tankPaths;
    private string _mapName = "custom";
    public string MapName { get => _mapName; set => SetProperty(ref _mapName, value); }
    private string _regionName = "custom_r1";
    public string RegionName { get => _regionName; set => SetProperty(ref _regionName, value); }

    // A stock actor template harvested from the last shipped region loaded — guaranteed to resolve,
    // so a seeded objects/actor.gas opens the engine's PC-spawn gate for the walkable "Play" test.
    private string? _seedTemplate;
    public ObservableCollection<ValidationRow> ValidationRows { get; } = new();

    // ── undo / redo (nodes.gas snapshots) ───────────────────────
    // Each edit snapshots the region as nodes.gas text before mutating; nodes, doors, texsets and the
    // anchor all round-trip through that text, so restoring a snapshot restores the whole region.
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    // ── commands ────────────────────────────────────────────────
    public RelayCommand PlaceAnchorCommand { get; }
    public RelayCommand ConnectCommand { get; }
    public RelayCommand LinkExistingCommand { get; }
    public RelayCommand DeleteNodeCommand { get; }
    public RelayCommand SaveNodesCommand { get; }
    public RelayCommand ImportNodesCommand { get; }
    public RelayCommand ResetViewCommand { get; }
    public RelayCommand WireframeCommand { get; }
    public RelayCommand TexturedCommand { get; }
    public RelayCommand TestInEngineCommand { get; }
    public RelayCommand PlayInEngineCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }

    public WorldBuilderViewModel(IReadOnlyList<string> tankPaths)
    {
        _tankPaths = tankPaths;
        TexturedCommand = new RelayCommand(_ => Textured = !Textured);
        TestInEngineCommand = new RelayCommand(_ => TestInEngine(), _ => IsReady && !IsEmpty);
        PlayInEngineCommand = new RelayCommand(_ => PlayInEngine(), _ => IsReady && !IsEmpty);
        ValidateCommand = new RelayCommand(_ => Validate(), _ => IsReady && !IsEmpty);
        UndoCommand = new RelayCommand(_ => Undo(), _ => _undo.Count > 0);
        RedoCommand = new RelayCommand(_ => Redo(), _ => _redo.Count > 0);
        PlaceAnchorCommand = new RelayCommand(_ => PlaceAnchor(),
            _ => IsReady && IsEmpty && _selectedMesh is not null);
        ConnectCommand = new RelayCommand(_ => Connect(),
            _ => IsReady && !IsEmpty && _selectedNode is not null && _selectedSourceDoor is { IsFree: true }
                 && _selectedMesh is not null && _selectedTargetDoor is not null);
        LinkExistingCommand = new RelayCommand(_ => LinkExisting(),
            _ => IsReady && _selectedNode is not null && _selectedSourceDoor is { IsFree: true } && _selectedOtherDoor is not null);
        DeleteNodeCommand = new RelayCommand(_ => DeleteSelectedNode(), _ => _selectedNode is not null);
        SaveNodesCommand = new RelayCommand(_ => SaveNodes(), _ => !IsEmpty);
        ImportNodesCommand = new RelayCommand(_ => ImportNodes(), _ => IsReady);
        ResetViewCommand = new RelayCommand(_ => ResetView());
        WireframeCommand = new RelayCommand(_ => { _wireframe = !_wireframe; OnPropertyChanged(nameof(WireframeLabel)); Render(); });

        LoadCatalogAsync(tankPaths);
    }

    private async void LoadCatalogAsync(IReadOnlyList<string> tankPaths)
    {
        try
        {
            var (cat, tex) = await Task.Run(() =>
            {
                var c = SnoCatalog.Build(tankPaths);
                var t = new TextureResolver();
                foreach (var p in tankPaths) t.AddTankPath(p);
                return (c, t);
            });
            _catalog = cat;
            _textures = tex;
            _allMeshes.AddRange(cat.Meshes);
            RefreshPalette();
            foreach (var r in cat.Regions) InstallRegions.Add(r);
            IsLoading = false;
            Status = _allMeshes.Count > 0
                ? $"{_allMeshes.Count:N0} node meshes · {InstallRegions.Count:N0} shipped regions. Place a node to start, or load a region to edit."
                : "No SNO meshes found in the install tanks — check the Dungeon Siege install path.";
            RaiseCommands();
        }
        catch (Exception ex)
        {
            IsLoading = false;
            Status = "Failed to load SNO catalogue: " + ex.Message;
        }
    }

    // ── palette / selection ─────────────────────────────────────
    private const int MaxPaletteResults = 400;

    private void RefreshPalette()
    {
        Palette.Clear();
        var q = _search?.Trim() ?? "";
        int shown = 0;
        foreach (var m in _allMeshes)
        {
            if (q.Length > 0 && m.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
            Palette.Add(m);
            if (++shown >= MaxPaletteResults) break;
        }
    }

    private void OnMeshSelected()
    {
        TargetDoors.Clear();
        _selectedTargetDoor = null;
        OnPropertyChanged(nameof(SelectedTargetDoor));

        if (_catalog is not null && _selectedMesh is not null)
        {
            var sno = _catalog.Resolve(_selectedMesh.MeshGuid);
            if (sno is not null)
                foreach (var d in sno.Doors)
                    TargetDoors.Add(new DoorRow((int)d.Id, true, null));
            if (TargetDoors.Count > 0) SelectedTargetDoor = TargetDoors[0];
        }
        RaiseCommands();
    }

    private void OnNodeSelected()
    {
        SourceDoors.Clear();
        _selectedSourceDoor = null;
        OnPropertyChanged(nameof(SelectedSourceDoor));

        if (_catalog is not null && _selectedNode is not null)
        {
            var node = _region.Find(_selectedNode.Guid);
            var sno = node is not null ? _catalog.Resolve(node.MeshGuid) : null;
            if (node is not null && sno is not null)
            {
                foreach (var d in sno.Doors)
                {
                    int id = (int)d.Id;
                    bool used = node.UsesDoor(id);
                    string? target = null;
                    if (used)
                        foreach (var bd in node.Doors)
                            if (bd.LocalId == id) { target = $"→ 0x{bd.FarGuid:X8} door {bd.FarDoorId}"; break; }
                    SourceDoors.Add(new DoorRow(id, !used, target));
                }
                foreach (var dr in SourceDoors)
                    if (dr.IsFree) { SelectedSourceDoor = dr; break; }
            }
        }

        // Free doors on every OTHER placed node — link targets for closing a loop.
        OtherFreeDoors.Clear();
        _selectedOtherDoor = null;
        OnPropertyChanged(nameof(SelectedOtherDoor));
        if (_catalog is not null && _selectedNode is not null)
        {
            foreach (var n in _region.Nodes)
            {
                if (n.Guid == _selectedNode.Guid) continue;
                var s = _catalog.Resolve(n.MeshGuid);
                if (s is null) continue;
                foreach (var d in s.Doors)
                    if (!n.UsesDoor((int)d.Id))
                        OtherFreeDoors.Add(new NodeDoorRow(n.Guid, (int)d.Id, _catalog.NameOf(n.MeshGuid) ?? $"0x{n.MeshGuid:X8}"));
            }
        }
        OnPropertyChanged(nameof(SelectedNodeTexset));
        RaiseCommands();
    }

    // ── build operations ────────────────────────────────────────
    private void PlaceAnchor()
    {
        if (_selectedMesh is null) return;
        PushUndo();
        var guid = NextGuid();
        var node = new BuilderNode { Guid = guid, MeshGuid = _selectedMesh.MeshGuid };
        _region.Nodes.Add(node);
        _region.TargetGuid = guid;
        _usedGuids.Add(guid);
        AfterModelChanged($"Placed anchor node {_catalog?.NameOf(node.MeshGuid)}.");
        SelectNode(guid);
    }

    private void Connect()
    {
        if (_selectedNode is null || _selectedSourceDoor is null || _selectedMesh is null || _selectedTargetDoor is null) return;
        var src = _region.Find(_selectedNode.Guid);
        if (src is null) return;

        int srcDoor = _selectedSourceDoor.Id;
        int tgtDoor = _selectedTargetDoor.Id;
        if (src.UsesDoor(srcDoor)) { Status = $"Door {srcDoor} on the selected node is already connected."; return; }

        PushUndo();
        var guid = NextGuid();
        var newNode = new BuilderNode { Guid = guid, MeshGuid = _selectedMesh.MeshGuid };
        src.Doors.Add(new BuilderDoor(srcDoor, guid, tgtDoor));          // reciprocal edges
        newNode.Doors.Add(new BuilderDoor(tgtDoor, src.Guid, srcDoor));
        _region.Nodes.Add(newNode);
        _usedGuids.Add(guid);
        AfterModelChanged($"Connected {_catalog?.NameOf(newNode.MeshGuid)} (door {srcDoor} ↔ {tgtDoor}).");
        SelectNode(guid);
    }

    /// <summary>Connects two ALREADY-PLACED nodes through a pair of free doors, closing a loop.
    /// Only the door adjacency is recorded — both nodes keep the positions they were composed at —
    /// so a ring of nodes can meet without adding geometry. Both doors must currently be free.</summary>
    private void LinkExisting()
    {
        if (_selectedNode is null || _selectedSourceDoor is null || _selectedOtherDoor is null) return;
        var a = _region.Find(_selectedNode.Guid);
        var b = _region.Find(_selectedOtherDoor.NodeGuid);
        if (a is null || b is null || a.Guid == b.Guid) return;

        int aDoor = _selectedSourceDoor.Id, bDoor = _selectedOtherDoor.DoorId;
        if (a.UsesDoor(aDoor)) { Status = $"Door {aDoor} on the selected node is already connected."; return; }
        if (b.UsesDoor(bDoor)) { Status = $"Door {bDoor} on {_selectedOtherDoor.Mesh} is already connected."; return; }

        PushUndo();
        a.Doors.Add(new BuilderDoor(aDoor, b.Guid, bDoor));   // reciprocal edge closes the loop
        b.Doors.Add(new BuilderDoor(bDoor, a.Guid, aDoor));
        AfterModelChanged($"Linked {_catalog?.NameOf(a.MeshGuid)} door {aDoor} ↔ {_selectedOtherDoor.Mesh} door {bDoor} (loop closed).");
        SelectNode(a.Guid);
    }

    private void DeleteSelectedNode()
    {
        if (_selectedNode is null) return;
        PushUndo();
        uint g = _selectedNode.Guid;
        _region.Nodes.RemoveAll(n => n.Guid == g);
        foreach (var n in _region.Nodes)
            n.Doors.RemoveAll(d => d.FarGuid == g);
        if (_region.TargetGuid == g)
            _region.TargetGuid = _region.Nodes.Count > 0 ? _region.Nodes[0].Guid : 0;

        _selectedNode = null;
        OnPropertyChanged(nameof(SelectedNode));
        SourceDoors.Clear();
        AfterModelChanged($"Deleted node 0x{g:X8}.");
    }

    private void AfterModelChanged(string status)
    {
        RebuildNodeRows();
        RebuildTexsets();
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(IsEmpty));
        Status = status;
        Render();
        RaiseCommands();
    }

    /// <summary>Distinct texset abbreviations in use across the region — the picker's options.</summary>
    private void RebuildTexsets()
    {
        var seen = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in _region.Nodes)
            if (!string.IsNullOrEmpty(n.TexsetAbbr)) seen.Add(n.TexsetAbbr);
        AvailableTexsets.Clear();
        foreach (var t in seen) AvailableTexsets.Add(t);
    }

    private void RebuildNodeRows()
    {
        Nodes.Clear();
        foreach (var n in _region.Nodes)
        {
            var mesh = _catalog?.NameOf(n.MeshGuid) ?? $"0x{n.MeshGuid:X8}";
            Nodes.Add(new NodeRow(n.Guid, mesh, n.Doors.Count, n.Guid == _region.TargetGuid));
        }
    }

    private void SelectNode(uint guid)
    {
        foreach (var r in Nodes)
            if (r.Guid == guid) { SelectedNode = r; break; }
    }

    private uint NextGuid()
    {
        uint g;
        do { g = _nextGuid++; } while (g == 0 || _usedGuids.Contains(g));
        return g;
    }

    /// <summary>Loads a region the tool discovered in the install, by name — no path hunting.</summary>
    private void LoadInstallRegion(RegionEntry entry)
    {
        var bytes = _catalog?.ReadRegionNodes(entry);
        if (bytes is null) { Status = $"Couldn't read region {entry.Display}."; return; }
        try
        {
            ApplyImportedRegion(NodesGasReader.Read(GasDocument.Load(bytes)), entry.Display);
            _seedTemplate = HarvestSeedTemplate(entry); // a real actor template so Play can spawn a PC
        }
        catch (Exception ex) { Status = "Load failed: " + ex.Message; }
    }

    /// <summary>Reads the shipped region's objects/actor.gas and returns the first actor template name,
    /// which is guaranteed to resolve in Logic/Objects — a safe seed for the walkable Play test.</summary>
    private string? HarvestSeedTemplate(RegionEntry entry)
    {
        var bytes = _catalog?.ReadRegionSibling(entry, "objects/actor.gas");
        if (bytes is null) return null;
        try
        {
            foreach (var root in GasDocument.Load(bytes).Roots)
            {
                int t = root.Header.IndexOf("t:", StringComparison.OrdinalIgnoreCase);
                if (t < 0) continue;
                int start = t + 2;
                int comma = root.Header.IndexOf(',', start);
                string name = (comma < 0 ? root.Header[start..] : root.Header[start..comma]).Trim();
                if (name.Length > 0) return name;
            }
        }
        catch { /* fall through */ }
        return null;
    }

    /// <summary>Manual fallback: open any nodes.gas the user points us at.</summary>
    private void ImportNodes()
    {
        if (!IsReady) return;
        var path = DialogService.OpenFile("Open a region's nodes.gas", "GAS files (*.gas)|*.gas|All files (*.*)|*.*");
        if (path is null) return;
        try { ApplyImportedRegion(NodesGasReader.Read(GasDocument.Load(File.ReadAllBytes(path))), Path.GetFileName(path)); }
        catch (Exception ex) { Status = "Import failed: " + ex.Message; }
    }

    private void ApplyImportedRegion(BuilderRegion region, string label)
    {
        if (region.Nodes.Count == 0) { Status = "No snodes found in that region."; return; }
        PushUndo();
        ReplaceRegion(region);
        _dist = 0f; // reframe the camera onto the loaded region
        AfterModelChanged($"Loaded {_region.Nodes.Count} node(s) from {label}.");
    }

    /// <summary>Swaps the whole region contents in place (nodes, doors, anchor) and resets guid state.
    /// Pushes no undo entry and sets no status — callers decide those.</summary>
    private void ReplaceRegion(BuilderRegion region)
    {
        _region.Nodes.Clear();
        _region.Nodes.AddRange(region.Nodes);
        _region.TargetGuid = region.TargetGuid;

        _usedGuids.Clear();
        uint maxGuid = 0;
        foreach (var n in _region.Nodes)
        {
            _usedGuids.Add(n.Guid);
            if (n.Guid > maxGuid) maxGuid = n.Guid;
        }
        _nextGuid = maxGuid == 0 ? 0x00010001 : maxGuid + 1;

        _selectedNode = null;
        OnPropertyChanged(nameof(SelectedNode));
        SourceDoors.Clear();
    }

    // ── undo / redo ─────────────────────────────────────────────
    private string Snapshot() => IsEmpty ? "" : NodesGasWriter.Write(_region);

    private void PushUndo()
    {
        _undo.Push(Snapshot());
        _redo.Clear();
        RaiseUndoRedo();
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Snapshot());
        LoadSnapshot(_undo.Pop(), "Undid last change.");
        RaiseUndoRedo();
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Snapshot());
        LoadSnapshot(_redo.Pop(), "Redid change.");
        RaiseUndoRedo();
    }

    private void LoadSnapshot(string snap, string status)
    {
        var region = snap.Length > 0 ? NodesGasReader.Read(GasDocument.Parse(snap)) : new BuilderRegion();
        ReplaceRegion(region);
        AfterModelChanged(status);
    }

    private void RaiseUndoRedo()
    {
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Packs the current region into a startable .dsmap and launches the SiegeFX engine to
    /// render its terrain — the fastest way to see the level in-engine. The terrain view needs only the
    /// region's nodes.gas, so this works as soon as one node is placed.</summary>
    private void TestInEngine()
    {
        if (IsEmpty) { Status = "Place at least one node before testing in the engine."; return; }

        string nodesGas;
        try
        {
            nodesGas = NodesGasWriter.Write(_region);
            RegionGraph.FromDocument(GasDocument.Parse(nodesGas)); // engine reads nodes.gas with no error guard — validate first
        }
        catch (Exception ex) { Status = "Region won't load (nodes.gas invalid): " + ex.Message; return; }

        var terrain = FindTank("terrain");
        if (terrain is null) { Status = "Couldn't find Terrain.dsres among the install tanks."; return; }

        var runtime = RuntimeLauncher.FindRuntime();
        if (runtime is null) { Status = "Couldn't find SiegeFX.Runtime — build the engine (Release) first."; return; }

        try
        {
            var outDir = Path.Combine(Path.GetTempPath(), "SiegeSmith", "maps");
            var pkg = MapPackager.PackStartableMap(nodesGas, MapName, RegionName, outDir, BuildStartInfo());
            RuntimeLauncher.LaunchRegion(runtime, pkg.MapTankPath, terrain, pkg.RegionPath);
            Status = $"Packed {Path.GetFileName(pkg.MapTankPath)} and launched SiegeFX ({pkg.RegionPath}).";
        }
        catch (Exception ex) { Status = "Test-in-engine failed: " + ex.Message; }
    }

    /// <summary>Packs the region as a full playable scene (start position + one seed actor) and
    /// launches --play-region so the user can walk it as a PC. Gated on a green validation pass.</summary>
    private void PlayInEngine()
    {
        if (IsEmpty) { Status = "Place at least one node first."; return; }
        var checks = RunChecks(play: true);
        ValidationRows.Clear();
        foreach (var r in checks) ValidationRows.Add(r);
        foreach (var r in checks)
            if (!r.Ok) { Status = "Can't play yet — " + r.Text; return; }

        var terrain = FindTank("terrain"); var logic = FindTank("logic"); var objects = FindTank("objects");
        var runtime = RuntimeLauncher.FindRuntime();
        if (terrain is null || logic is null || objects is null || runtime is null)
        { Status = "Missing a required tank or the engine — see the checklist."; return; }

        try
        {
            var nodesGas = NodesGasWriter.Write(_region);
            var outDir = Path.Combine(Path.GetTempPath(), "SiegeSmith", "maps");
            var pkg = MapPackager.PackStartableMap(nodesGas, MapName, RegionName, outDir, BuildStartInfo(), BuildSeedActor());
            RuntimeLauncher.LaunchPlayRegion(runtime, pkg.MapTankPath, terrain, logic, objects, pkg.RegionPath);
            Status = $"Launched playable {Path.GetFileName(pkg.MapTankPath)} — walk it as a PC ({pkg.RegionPath}).";
        }
        catch (Exception ex) { Status = "Play-in-engine failed: " + ex.Message; }
    }

    /// <summary>Runs the pre-launch checklist and shows it in the validation panel.</summary>
    private void Validate()
    {
        var checks = RunChecks(play: true);
        ValidationRows.Clear();
        foreach (var r in checks) ValidationRows.Add(r);
        int bad = 0;
        foreach (var r in checks) if (!r.Ok) bad++;
        Status = bad == 0
            ? $"Region valid — {NodeCount} node(s), ready to launch."
            : $"Region has {bad} problem(s) — see the checklist below.";
    }

    /// <summary>The pre-launch checks: everything the engine needs to load and (optionally) play the
    /// region, each as a pass/fail row. Turns the engine's silent load failures into a red/green list.</summary>
    private List<ValidationRow> RunChecks(bool play)
    {
        var rows = new List<ValidationRow>();
        if (IsEmpty) { rows.Add(new ValidationRow(false, "Region is empty — place at least one node.")); return rows; }

        RegionGraph? graph = null;
        try
        {
            graph = RegionGraph.FromDocument(GasDocument.Parse(NodesGasWriter.Write(_region)));
            rows.Add(new ValidationRow(true, "nodes.gas parses cleanly."));
        }
        catch (Exception ex) { rows.Add(new ValidationRow(false, "nodes.gas invalid: " + ex.Message)); return rows; }

        var anchor = _region.Find(_region.TargetGuid);
        rows.Add(new ValidationRow(anchor is not null,
            anchor is not null ? $"Anchor node 0x{_region.TargetGuid:X8} present." : "No anchor node set."));

        int missMesh = 0;
        foreach (var n in _region.Nodes) if (_catalog?.Resolve(n.MeshGuid) is null) missMesh++;
        rows.Add(new ValidationRow(missMesh == 0,
            missMesh == 0 ? $"All {_region.Nodes.Count} node meshes resolve." : $"{missMesh} node(s) reference a missing mesh."));

        var guids = new HashSet<uint>();
        foreach (var n in _region.Nodes) guids.Add(n.Guid);
        int badDoor = 0;
        foreach (var n in _region.Nodes) foreach (var d in n.Doors) if (!guids.Contains(d.FarGuid)) badDoor++;
        rows.Add(new ValidationRow(badDoor == 0,
            badDoor == 0 ? "All door links reference existing nodes." : $"{badDoor} door link(s) point to a missing node."));

        if (graph is not null && _catalog is not null)
        {
            try
            {
                var layout = RegionLayout.Build(graph, g => _catalog.Resolve(g));
                int noXform = 0;
                foreach (var n in _region.Nodes) if (!layout.TryGetTransform(n.Guid, out _)) noXform++;
                rows.Add(new ValidationRow(noXform == 0,
                    noXform == 0 ? "Layout solves for every node." : $"{noXform} node(s) not placed by the layout solver (disconnected?)."));
            }
            catch (Exception ex) { rows.Add(new ValidationRow(false, "Layout solve failed: " + ex.Message)); }
        }

        var terrain = FindTank("terrain");
        rows.Add(new ValidationRow(terrain is not null,
            terrain is not null ? "Terrain.dsres found." : "Terrain.dsres not found in the install tanks."));

        var runtime = RuntimeLauncher.FindRuntime();
        rows.Add(new ValidationRow(runtime is not null,
            runtime is not null ? "SiegeFX.Runtime found." : "SiegeFX.Runtime not built — build the engine (Release)."));

        if (play)
        {
            rows.Add(new ValidationRow(FindTank("logic") is not null,
                FindTank("logic") is not null ? "Logic.dsres found." : "Logic.dsres not found."));
            rows.Add(new ValidationRow(FindTank("objects") is not null,
                FindTank("objects") is not null ? "Objects.dsres found." : "Objects.dsres not found."));
            rows.Add(new ValidationRow(_seedTemplate is not null,
                _seedTemplate is not null
                    ? $"Seed actor '{_seedTemplate}' ready (a PC will spawn)."
                    : "No seed actor — load a shipped region first so the engine spawns a PC."));
        }
        return rows;
    }

    /// <summary>The PC drop point: the anchor node's local X,Z centre (from its SNO corners), anchored
    /// to the anchor guid. Y is nav-snapped by the engine at load.</summary>
    private MapPackager.StartInfo? BuildStartInfo()
    {
        uint g = _region.TargetGuid != 0 ? _region.TargetGuid : (_region.Nodes.Count > 0 ? _region.Nodes[0].Guid : 0);
        if (g == 0) return null;
        var node = _region.Find(g);
        var sno = node is not null ? _catalog?.Resolve(node.MeshGuid) : null;
        float x = 0f, z = 0f;
        if (sno is not null && sno.Corners.Length > 0)
        {
            float minx = float.MaxValue, maxx = float.MinValue, minz = float.MaxValue, maxz = float.MinValue;
            foreach (var c in sno.Corners)
            {
                var p = c.Position;
                if (p.X < minx) minx = p.X; if (p.X > maxx) maxx = p.X;
                if (p.Z < minz) minz = p.Z; if (p.Z > maxz) maxz = p.Z;
            }
            x = (minx + maxx) * 0.5f; z = (minz + maxz) * 0.5f;
        }
        return new MapPackager.StartInfo(g, x, z, "default", $"{MapName} start");
    }

    private MapPackager.SeedActor? BuildSeedActor()
    {
        if (string.IsNullOrWhiteSpace(_seedTemplate)) return null;
        if (BuildStartInfo() is not { } s) return null;
        return new MapPackager.SeedActor(_seedTemplate!, 0x00020001, s.NodeGuid, s.X, s.Z);
    }

    private string? FindTank(string substr)
    {
        foreach (var p in _tankPaths)
            if (Path.GetFileName(p).Contains(substr, StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }

    private void SaveNodes()
    {
        if (IsEmpty) return;
        var dest = DialogService.SaveFileAs("nodes.gas");
        if (dest is null) return;
        try
        {
            File.WriteAllText(dest, NodesGasWriter.Write(_region));
            Status = $"Saved region to {dest} ({_region.Nodes.Count} node(s)).";
        }
        catch (Exception ex) { Status = "Save failed: " + ex.Message; }
    }

    // ── preview render ──────────────────────────────────────────
    private void Render()
    {
        if (_catalog is null || _region.Nodes.Count == 0) { Image = null; return; }

        RegionGraph graph;
        try { graph = RegionGraph.FromDocument(GasDocument.Parse(NodesGasWriter.Write(_region))); }
        catch (Exception ex) { Status = "Preview parse error: " + ex.Message; Image = null; return; }

        var layout = RegionLayout.Build(graph, g => _catalog.Resolve(g));

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triTex = new List<int>();
        var pickGuid = new List<uint>(); // node GUID per triangle, for click-picking
        var texList = new List<SoftwareRenderer.Texture>();
        var texSlot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // dedup textures across nodes/surfaces
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var node in _region.Nodes)
        {
            if (!layout.TryGetTransform(node.Guid, out var world)) continue;
            var sno = _catalog.Resolve(node.MeshGuid);
            if (sno is null) continue;
            foreach (var s in sno.Surfaces)
            {
                int slot = ResolveSlot(s.TextureName, node.TexsetAbbr, texSlot, texList);
                var idx = s.TriangleIndices;
                for (int k = 0; k + 2 < idx.Length; k += 3)
                {
                    int g0 = (int)s.StartCorner + idx[k];
                    int g1 = (int)s.StartCorner + idx[k + 1];
                    int g2 = (int)s.StartCorner + idx[k + 2];
                    if ((uint)g0 >= (uint)sno.Corners.Length || (uint)g1 >= (uint)sno.Corners.Length || (uint)g2 >= (uint)sno.Corners.Length)
                        continue;
                    var p0 = AddCorner(sno.Corners[g0], world, verts, normals, uvs);
                    var p1 = AddCorner(sno.Corners[g1], world, verts, normals, uvs);
                    var p2 = AddCorner(sno.Corners[g2], world, verts, normals, uvs);
                    min = Vector3.Min(min, Vector3.Min(p0, Vector3.Min(p1, p2)));
                    max = Vector3.Max(max, Vector3.Max(p0, Vector3.Max(p1, p2)));
                    triTex.Add(slot);
                    pickGuid.Add(node.Guid);
                }
            }
        }

        if (verts.Count < 3) { Image = null; _pickVerts = System.Array.Empty<Vector3>(); _pickGuid = System.Array.Empty<uint>(); return; }

        _center = (min + max) * 0.5f;
        _radius = MathF.Max((max - min).Length() * 0.5f, 0.001f);
        if (_dist <= 0f) _dist = _radius * 2.6f;
        _pickVerts = verts.ToArray();
        _pickGuid = pickGuid.ToArray();

        bool useTex = _textured && !_wireframe && texList.Count > 0
                      && uvs.Count == verts.Count && triTex.Count == verts.Count / 3;
        var bgra = useTex
            ? SoftwareRenderer.RenderTextured(verts.ToArray(), normals.ToArray(), uvs.ToArray(), triTex.ToArray(), texList.ToArray(),
                _vw, _vh, _center + _pan, _radius, _yaw, _pitch, _dist)
            : SoftwareRenderer.Render(verts.ToArray(), normals.ToArray(), _vw, _vh,
                _center + _pan, _radius, _yaw, _pitch, _dist, _wireframe);
        var bmp = BitmapSource.Create(_vw, _vh, 96, 96, PixelFormats.Bgra32, null, bgra, _vw * 4);
        bmp.Freeze();
        Image = bmp;
    }

    /// <summary>Resolves a surface's texture to a slot in <paramref name="texList"/>, first rebinding
    /// the node's texset (so <c>_xxx_</c> placeholder surfaces — the "white squares" — resolve), then
    /// deduping by the resolved name and caching misses as -1 (flat-shaded). Deduping on the resolved
    /// name (not the raw surface name) keeps two nodes that share a mesh but use different texsets from
    /// colliding on one slot. Returns -1 when no resolver or no texture.</summary>
    private int ResolveSlot(string textureName, string texsetAbbr, Dictionary<string, int> texSlot, List<SoftwareRenderer.Texture> texList)
    {
        if (_textures is null || string.IsNullOrEmpty(textureName)) return -1;
        string resolved = TextureResolver.ApplyTexset(textureName, texsetAbbr);
        if (texSlot.TryGetValue(resolved, out var slot)) return slot;
        slot = -1;
        if (_textures.Resolve(resolved) is { } tv && tv.Valid)
        {
            slot = texList.Count;
            texList.Add(tv);
        }
        texSlot[resolved] = slot;
        return slot;
    }

    private static Vector3 AddCorner(in SnoModel.Corner c, in Matrix4x4 world,
        List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs)
    {
        var p = Vector3.Transform(c.Position, world);
        verts.Add(p);
        normals.Add(Vector3.TransformNormal(c.Normal, world));
        uvs.Add(c.Uv);
        return p;
    }

    // ── camera (driven by the view's mouse handlers) ────────────
    public void SetViewport(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width == _vw && height == _vh)) return;
        _vw = width; _vh = height;
        Render();
    }

    public void Orbit(double dx, double dy)
    {
        _yaw += (float)dx * 0.01f;
        _pitch = Math.Clamp(_pitch + (float)dy * 0.01f, -1.50f, 1.50f);
        Render();
    }

    public void Zoom(int wheelDelta)
    {
        _dist = Math.Clamp(_dist * (wheelDelta > 0 ? 0.9f : 1.1f), _radius * 0.2f, _radius * 40f);
        Render();
    }

    /// <summary>Right-drag pan: slides the framed centre in the camera's screen plane.</summary>
    public void Pan(double dx, double dy)
    {
        var dir = new Vector3(MathF.Cos(_pitch) * MathF.Cos(_yaw), MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch));
        var right = Vector3.Normalize(Vector3.Cross(new Vector3(0, 0, 1), dir));
        var up = Vector3.Normalize(Vector3.Cross(dir, right));
        float s = MathF.Max(_dist, _radius) * 0.0016f;
        _pan += right * (float)(-dx) * s + up * (float)dy * s;
        Render();
    }

    public void ResetView()
    {
        _yaw = 0.7f; _pitch = 0.5f; _dist = _radius * 2.6f; _pan = default;
        Render();
    }

    /// <summary>Click-to-snap on the corner gizmo: an axis tip snaps the camera to look down that
    /// axis (clicking the same axis again flips to the opposite side); the centre hub resets to the
    /// iso view. Returns true when the click hit the gizmo, so the viewport should not orbit.</summary>
    public bool TrySnapView(double sx, double sy)
    {
        int hit = SoftwareRenderer.HitGizmo(sx, sy, _yaw, _pitch, _vw, _vh);
        if (hit < 0) return false;
        const float H = MathF.PI / 2f;
        switch (hit)
        {
            case 0: _yaw = 0.7f; _pitch = 0.5f; _pan = default; break;                                    // hub → iso
            case 1: (_yaw, _pitch) = NearAngle(_yaw, 0f) && NearAngle(_pitch, 0f) ? (MathF.PI, 0f) : (0f, 0f); break; // ±X
            case 2: (_yaw, _pitch) = NearAngle(_yaw, H) && NearAngle(_pitch, 0f) ? (-H, 0f) : (H, 0f); break;         // ±Y
            case 3: _pitch = _pitch > 0.9f ? -1.5f : 1.5f; break;                                          // Z top/bottom (keep yaw)
        }
        Render();
        return true;
    }

    /// <summary>Selects the node whose terrain is under the given viewport point, if any. Returns
    /// true when a node was hit (the caller may still start an orbit from there).</summary>
    public bool TryPick(double sx, double sy)
    {
        if (_pickVerts.Length < 3) return false;
        uint guid = SoftwareRenderer.PickTriangle(_pickVerts, _pickGuid, _vw, _vh,
            _center + _pan, _radius, _yaw, _pitch, _dist, 0f, sx, sy);
        if (guid == 0) return false;
        if (_selectedNode?.Guid != guid) SelectNode(guid);
        return true;
    }

    private static bool NearAngle(float a, float b)
    {
        float d = a - b;
        while (d > MathF.PI) d -= 2f * MathF.PI;
        while (d < -MathF.PI) d += 2f * MathF.PI;
        return MathF.Abs(d) < 0.16f;
    }

    private void RaiseCommands()
    {
        PlaceAnchorCommand.RaiseCanExecuteChanged();
        ConnectCommand.RaiseCanExecuteChanged();
        LinkExistingCommand.RaiseCanExecuteChanged();
        DeleteNodeCommand.RaiseCanExecuteChanged();
        SaveNodesCommand.RaiseCanExecuteChanged();
        ImportNodesCommand.RaiseCanExecuteChanged();
        TestInEngineCommand.RaiseCanExecuteChanged();
        PlayInEngineCommand.RaiseCanExecuteChanged();
        ValidateCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        _catalog?.Dispose();
        _textures?.Dispose();
    }
}

/// <summary>A row in the placed-node list.</summary>
public sealed record NodeRow(uint Guid, string Mesh, int DoorCount, bool IsAnchor)
{
    public string Label => (IsAnchor ? "★ " : "") + Mesh;
    public string Detail => $"0x{Guid:X8} · {DoorCount} door(s)";
}

/// <summary>A door slot on a node or palette mesh: free (connectable) or already mated.</summary>
public sealed record DoorRow(int Id, bool IsFree, string? Target)
{
    public string Label => IsFree ? $"door {Id}  ·  free" : $"door {Id}  {Target}";
}

/// <summary>A free door on another placed node — a candidate to link the selected node's door to.</summary>
public sealed record NodeDoorRow(uint NodeGuid, int DoorId, string Mesh)
{
    public string Label => $"{Mesh}  ·  door {DoorId}";
}

/// <summary>One pre-launch check result: a green tick or red cross with an explanation.</summary>
public sealed record ValidationRow(bool Ok, string Text)
{
    public string Glyph => Ok ? "✓" : "✕";
}
