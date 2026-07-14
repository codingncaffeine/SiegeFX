using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Sfx;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.EffectsLab;

/// <summary>One effect script in the Lab browser — a stock script harvested from
/// the install tanks (read-only until copied) or a project script living in the
/// custom-assets folder (editable; bundles into every packaged map).</summary>
public sealed class LabScriptItem : ObservableObject
{
    public string Name { get; set; } = "";
    public string Body { get; set; } = "";          // raw DSL, [[ ]] markers stripped
    public bool IsProject { get; set; }
    public string Source { get; set; } = "";        // "offensive.gas" / project file name
    public string? FilePath { get; set; }           // project scripts only

    public string Label => IsProject ? Name + "  ●" : Name;
    public string Detail => (IsProject ? "project · " : "stock · ") + Source;
}

public sealed class LabDiagRow
{
    public string Icon { get; init; } = "";
    public string Text { get; init; } = "";
    public bool IsWarning { get; init; }
    public Brush Tint => IsWarning ? Brushes.Goldenrod : Brushes.SeaGreen;
}

/// <summary>SS-FXLAB — the Effects Lab: browse every SiegeFX effect script (DS1's
/// "SiegeFX" DSL), edit with live compile diagnostics, and SEE the result instantly —
/// the engine's own <see cref="SfxRuntime"/> VM drives a CPU particle preview on every
/// keystroke, and one click renders the same script through the engine's real GL
/// particle system into a filmstrip. Saved scripts land in the custom-assets folder
/// (<c>world/global/effects/</c>), bundle into every packaged map, and are callable
/// from emitters and triggers via <c>call_sfx_script</c>.</summary>
public sealed class EffectsLabViewModel : ObservableObject
{
    private readonly IReadOnlyList<string> _tankPaths;
    private readonly List<TankDocument> _openTanks = new();
    private SfxScriptStore? _stockStore;
    private readonly List<LabScriptItem> _all = new();

    private readonly LabParticleSink _sink = new();
    private SfxRuntime? _runtime;
    private readonly System.Windows.Threading.DispatcherTimer _simTimer;
    private readonly System.Windows.Threading.DispatcherTimer _editDebounce;
    private float _doneLinger;

    // viewport camera (preview space is Z-up like every SiegeSmith viewport)
    private int _vw = 640, _vh = 520;
    private float _yaw = -2.05f, _pitch = 0.42f, _dist = 9.5f;
    private Vector3 _pan = Vector3.Zero;

    public ObservableCollection<LabScriptItem> Scripts { get; } = new();
    public ObservableCollection<LabDiagRow> Diagnostics { get; } = new();

    public EffectsLabViewModel(IReadOnlyList<string> tankPaths)
    {
        _tankPaths = tankPaths;

        PlayCommand = new RelayCommand(_ => Respawn(), _ => _selected is not null);
        StopCommand = new RelayCommand(_ => StopSim());
        NewScriptCommand = new RelayCommand(_ => NewScript(), _ => _assetsFolder is not null);
        CopyToProjectCommand = new RelayCommand(_ => CopyToProject(), _ => _selected is { IsProject: false } && _assetsFolder is not null);
        SaveScriptCommand = new RelayCommand(_ => SaveScript(), _ => _selected is { IsProject: true });
        DeleteScriptCommand = new RelayCommand(_ => DeleteScript(), _ => _selected is { IsProject: true });
        SetAssetsFolderCommand = new RelayCommand(_ => SetAssetsFolder());
        FilmstripCommand = new RelayCommand(_ => _ = RenderFilmstripAsync(), _ => _selected is not null && !_filmstripBusy);
        ResetViewCommand = new RelayCommand(_ => { _yaw = -2.05f; _pitch = 0.42f; _dist = 9.5f; _pan = Vector3.Zero; RenderViewport(); });

        _editDebounce = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        _editDebounce.Tick += (_, _) =>
        {
            _editDebounce.Stop();
            RunDiagnostics();
            if (AutoPlay && _selected is not null) Respawn();
        };

        _simTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _simTimer.Tick += (_, _) => SimTick(0.033f);
        _simTimer.Start();

        LoadPrefs();
        LoadStock();
        LoadProjectScripts();
        RebuildVisible();
        RenderViewport();
    }

    public void Shutdown()
    {
        _simTimer.Stop();
        _editDebounce.Stop();
        foreach (var t in _openTanks) t.Dispose();
        _openTanks.Clear();
        SavePrefs();
    }

    // ── commands ─────────────────────────────────────────────────
    public RelayCommand PlayCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand NewScriptCommand { get; }
    public RelayCommand CopyToProjectCommand { get; }
    public RelayCommand SaveScriptCommand { get; }
    public RelayCommand DeleteScriptCommand { get; }
    public RelayCommand SetAssetsFolderCommand { get; }
    public RelayCommand FilmstripCommand { get; }
    public RelayCommand ResetViewCommand { get; }

    private void RaiseCommands()
    {
        PlayCommand.RaiseCanExecuteChanged();
        NewScriptCommand.RaiseCanExecuteChanged();
        CopyToProjectCommand.RaiseCanExecuteChanged();
        SaveScriptCommand.RaiseCanExecuteChanged();
        DeleteScriptCommand.RaiseCanExecuteChanged();
        FilmstripCommand.RaiseCanExecuteChanged();
    }

    // ── script browser ───────────────────────────────────────────
    private string _filter = "";
    public string FilterText
    {
        get => _filter;
        set { if (SetProperty(ref _filter, value)) RebuildVisible(); }
    }

    private LabScriptItem? _selected;
    private bool _rebuildingList;
    public LabScriptItem? SelectedScript
    {
        get => _selected;
        set
        {
            // Refilling the ListBox pushes a null selection through the binding —
            // ignore it or a rename/filter keystroke wipes the open editor.
            if (_rebuildingList) return;
            if (!SetProperty(ref _selected, value)) return;
            _suppressEdit = true;
            EditorText = value?.Body ?? "";
            ScriptName = value?.Name ?? "";
            _suppressEdit = false;
            OnPropertyChanged(nameof(EditorReadOnly));
            OnPropertyChanged(nameof(ShowStockBanner));
            OnPropertyChanged(nameof(SelectedDetail));
            RunDiagnostics();
            if (value is not null && AutoPlay) Respawn(); else StopSim();
            RaiseCommands();
        }
    }

    public string SelectedDetail => _selected?.Detail ?? "";
    public bool EditorReadOnly => _selected is null or { IsProject: false };
    public bool ShowStockBanner => _selected is { IsProject: false };

    private void RebuildVisible()
    {
        var keep = _selected;
        _rebuildingList = true;
        Scripts.Clear();
        foreach (var s in _all
                     .Where(s => _filter.Length == 0 || s.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(s => s.IsProject).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            Scripts.Add(s);
        _rebuildingList = false;
        // Restore the selection without the full setter (no editor reload); if the
        // filter hid it, the editor keeps its content with no list highlight.
        if (keep is not null && Scripts.Contains(keep))
            OnPropertyChanged(nameof(SelectedScript));
        OnPropertyChanged(nameof(BrowserLabel));
    }

    public string BrowserLabel =>
        $"{_all.Count(s => !s.IsProject)} stock · {_all.Count(s => s.IsProject)} project";

    private void LoadStock()
    {
        foreach (var path in _tankPaths)
        {
            try
            {
                var doc = TankDocument.Open(path);
                var store = SfxScriptStore.LoadFromTank(doc.Reader);
                if (store.Count == 0) { doc.Dispose(); continue; }
                _openTanks.Add(doc);
                _stockStore = _stockStore is null ? store : Merge(_stockStore, store);
            }
            catch { /* not a tank with effects — skip */ }
        }
        if (_stockStore is not null)
            foreach (var s in _stockStore.All)
                _all.Add(new LabScriptItem
                {
                    Name = s.Name,
                    Body = StripMarkers(s.Body),
                    IsProject = false,
                    Source = System.IO.Path.GetFileName(s.SourcePath),
                });
        Status = _stockStore is null
            ? "No stock effect scripts found — is the DS1 install detected? (Logic.dsres carries /world/global/effects.)"
            : $"Loaded {_stockStore.Count} stock effect scripts.";
    }

    private static SfxScriptStore Merge(SfxScriptStore into, SfxScriptStore from)
    {
        foreach (var s in from.All) into.AddOrReplace(s);
        return into;
    }

    private static string StripMarkers(string body)
    {
        var t = body.Trim();
        if (t.StartsWith("[[", StringComparison.Ordinal)) t = t[2..];
        if (t.EndsWith("]]", StringComparison.Ordinal)) t = t[..^2];
        return t.Trim('\r', '\n');
    }

    // ── project scripts (custom-assets folder) ───────────────────
    private string? _assetsFolder;
    public string AssetsLabel => _assetsFolder is null
        ? "No assets folder — set one to create and save scripts (they bundle into every packaged map)."
        : _assetsFolder;

    private string ProjectEffectsDir => System.IO.Path.Combine(_assetsFolder!, "world", "global", "effects");

    private void SetAssetsFolder()
    {
        var folder = DialogService.PickFolder("Choose the custom-assets folder (same folder the World Builder bundles)");
        if (folder is null) return;
        _assetsFolder = folder;
        OnPropertyChanged(nameof(AssetsLabel));
        _all.RemoveAll(s => s.IsProject);
        LoadProjectScripts();
        RebuildVisible();
        RaiseCommands();
        SavePrefs();
    }

    private void LoadProjectScripts()
    {
        if (_assetsFolder is null || !Directory.Exists(ProjectEffectsDir)) return;
        foreach (var f in Directory.EnumerateFiles(ProjectEffectsDir, "*.gas"))
        {
            try
            {
                var probe = new SfxScriptStore();
                if (probe.AddFromGasText(File.ReadAllText(f), f) == 0) continue;
                foreach (var s in probe.All)
                    _all.Add(new LabScriptItem
                    {
                        Name = s.Name,
                        Body = StripMarkers(s.Body),
                        IsProject = true,
                        Source = System.IO.Path.GetFileName(f),
                        FilePath = f,
                    });
            }
            catch { /* unreadable project file — skip */ }
        }
    }

    private void NewScript()
    {
        if (_assetsFolder is null) return;
        var baseName = "my_effect";
        int n = 1;
        while (_all.Any(s => s.Name.Equals($"{baseName}_{n}", StringComparison.OrdinalIgnoreCase))) n++;
        var item = new LabScriptItem
        {
            Name = $"{baseName}_{n}",
            IsProject = true,
            Source = $"{baseName}_{n}.gas",
            Body = "// SiegeFX effect script — edit and watch the preview.\n" +
                   "// Anchors: #SOURCE (caster) and #TARGET_KB (target dummy).\n" +
                   "sfx create fire #SOURCE \"flamesize(1.75) fade(.85) count(30)\";\n" +
                   "sfx start #POP;\n" +
                   "pause 1.0;\n" +
                   "sfx create lightning #SOURCE \"dur(0.6) maxdisplace(0.2)\";\n" +
                   "sfx target #PEEK #TARGET_KB;\n" +
                   "sfx start #POP;\n",
        };
        _all.Add(item);
        RebuildVisible();
        SelectedScript = item;
        SaveScript();
        Status = $"Created {item.Name} — edit away; every keystroke recompiles and replays.";
    }

    private void CopyToProject()
    {
        if (_selected is null || _assetsFolder is null) return;
        var baseName = _selected.Name;
        var name = baseName;
        if (_all.Any(s => s.IsProject && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            int n = 2;
            while (_all.Any(s => s.IsProject && s.Name.Equals($"{baseName}_{n}", StringComparison.OrdinalIgnoreCase))) n++;
            name = $"{baseName}_{n}";
        }
        var item = new LabScriptItem
        {
            Name = name, Body = EditorText, IsProject = true, Source = name + ".gas",
        };
        _all.Add(item);
        RebuildVisible();
        SelectedScript = item;
        SaveScript();
        Status = name == baseName
            ? $"Copied {baseName} to the project — it now OVERRIDES the stock script in packaged maps."
            : $"Copied {baseName} to the project as {name}.";
    }

    private void SaveScript()
    {
        if (_selected is not { IsProject: true } s || _assetsFolder is null) return;
        try
        {
            Directory.CreateDirectory(ProjectEffectsDir);
            s.Body = EditorText;
            var path = s.FilePath ?? System.IO.Path.Combine(ProjectEffectsDir, Sanitize(s.Name) + ".gas");
            File.WriteAllText(path, WrapGas(s.Name, s.Body));
            s.FilePath = path;
            s.Source = System.IO.Path.GetFileName(path);
            Dirty = false;
            OnPropertyChanged(nameof(SelectedDetail));
            Status = $"Saved {s.Name} → {path}";
        }
        catch (Exception ex) { Status = "Save failed: " + ex.Message; }
    }

    private void DeleteScript()
    {
        if (_selected is not { IsProject: true } s) return;
        try { if (s.FilePath is not null && File.Exists(s.FilePath)) File.Delete(s.FilePath); }
        catch (Exception ex) { Status = "Delete failed: " + ex.Message; return; }
        _all.Remove(s);
        RebuildVisible();
        SelectedScript = null;
        Status = $"Deleted {s.Name}.";
    }

    private static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return sb.Length == 0 ? "effect" : sb.ToString();
    }

    /// <summary>The exact shipped shape: one [effect_script*] block, body in [[ ]].</summary>
    private static string WrapGas(string name, string body) =>
        "[effect_script*]\r\n{\r\n\tname = " + name + ";\r\n\tscript = [[\r\n" +
        body.Replace("\r\n", "\n").Replace("\n", "\r\n") +
        "\r\n\t]];\r\n}\r\n";

    // ── editor ───────────────────────────────────────────────────
    private bool _suppressEdit;
    private string _editorText = "";
    public string EditorText
    {
        get => _editorText;
        set
        {
            if (!SetProperty(ref _editorText, value) || _suppressEdit) return;
            Dirty = true;
            if (_selected is { IsProject: true } s) s.Body = value;
            _editDebounce.Stop();
            _editDebounce.Start();
        }
    }

    private string _scriptName = "";
    public string ScriptName
    {
        get => _scriptName;
        set
        {
            if (!SetProperty(ref _scriptName, value)) return;
            if (_suppressEdit || _selected is not { IsProject: true } s || value.Length == 0) return;
            s.Name = value;
            Dirty = true;
            RebuildVisible();
        }
    }

    private bool _dirty;
    public bool Dirty { get => _dirty; set => SetProperty(ref _dirty, value); }

    private bool _autoPlay = true;
    public bool AutoPlay { get => _autoPlay; set => SetProperty(ref _autoPlay, value); }

    private bool _loop = true;
    public bool Loop { get => _loop; set => SetProperty(ref _loop, value); }

    private string _scriptArgs = "";
    public string ScriptArgs { get => _scriptArgs; set => SetProperty(ref _scriptArgs, value); }

    private double _targetDist = 4.0;
    public double TargetDist
    {
        get => _targetDist;
        set { if (SetProperty(ref _targetDist, Math.Clamp(value, 1.0, 16.0))) { if (AutoPlay) Respawn(); else RenderViewport(); } }
    }

    // ── diagnostics ──────────────────────────────────────────────
    private string _coverage = "";
    public string CoverageBadge { get => _coverage; private set => SetProperty(ref _coverage, value); }
    private bool _coverageOk;
    public bool CoverageOk { get => _coverageOk; private set => SetProperty(ref _coverageOk, value); }

    private void RunDiagnostics()
    {
        Diagnostics.Clear();
        if (_selected is null) { CoverageBadge = ""; return; }

        SfxProgram prog;
        try { prog = SfxScriptCompiler.Compile(_selected.Name, EditorText); }
        catch (Exception ex)
        {
            Diagnostics.Add(new LabDiagRow { Icon = "✕", Text = "Compile failed: " + ex.Message, IsWarning = true });
            CoverageBadge = "compile error"; CoverageOk = false;
            return;
        }

        int creates = 0, raws = 0, unsupported = 0;
        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_stockStore is not null) foreach (var s in _stockStore.All) knownNames.Add(s.Name);
        foreach (var s in _all.Where(s => s.IsProject)) knownNames.Add(s.Name);

        foreach (var st in prog.Statements)
        {
            switch (st.Kind)
            {
                case StatementKind.SfxCreate when st.Tokens.Count > 0:
                    creates++;
                    var kind = st.Tokens[0].ToLowerInvariant();
                    if (!SfxRuntime.SupportedCreateKinds.Contains(kind))
                    {
                        unsupported++;
                        Diagnostics.Add(new LabDiagRow { Icon = "⚠", Text = $"create '{kind}' — no engine renderer yet (no-ops in-game)", IsWarning = true });
                    }
                    break;
                case StatementKind.Raw:
                    // Several verbs compile as Raw but ARE executed by the VM
                    // (worldmsg / randrange / frandrange / camerashake / exit) —
                    // consult the engine's own truth table, not the statement kind.
                    if (SfxRuntime.HandledRawVerbs.Contains(st.Verb)) break;
                    raws++;
                    Diagnostics.Add(new LabDiagRow { Icon = "⚠", Text = $"'{st.Verb} {string.Join(' ', st.Tokens)}' — unknown to the engine VM (logged + skipped)", IsWarning = true });
                    break;
                case StatementKind.Call when st.Tokens.Count > 0 && !knownNames.Contains(st.Tokens[0]):
                    Diagnostics.Add(new LabDiagRow { Icon = "⚠", Text = $"call '{st.Tokens[0]}' — script not found in stock or project", IsWarning = true });
                    break;
            }
        }

        Diagnostics.Insert(0, new LabDiagRow
        {
            Icon = "✓",
            Text = $"{prog.Statements.Count} statement(s), {creates} create(s) — compiles clean",
        });
        CoverageOk = raws == 0 && unsupported == 0;
        CoverageBadge = CoverageOk
            ? "engine-covered — every verb runs in-game"
            : $"{raws + unsupported} verb(s) won't render in-game";
    }

    // ── simulation ───────────────────────────────────────────────
    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    private string _liveStats = "";
    public string LiveStats { get => _liveStats; private set => SetProperty(ref _liveStats, value); }

    private void Respawn()
    {
        if (_selected is null) return;
        _sink.Clear();

        var store = _stockStore?.Clone() ?? new SfxScriptStore();
        foreach (var s in _all.Where(s => s.IsProject))
            store.AddOrReplace(new SfxScript(s.Name, s.Body, s.FilePath ?? "lab"));
        if (_selected.IsProject || _selected.Body != EditorText)
            store.AddOrReplace(new SfxScript(_selected.Name, EditorText, "lab-buffer"));

        _runtime = new SfxRuntime(store, _sink);
        _runtime.SetDeterministicSeed(1);

        var ctx = LabContext();
        var args = ScriptArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _runtime.Spawn(_selected.Name, ctx, args.Length > 0 ? args : null);
        _doneLinger = 0f;
    }

    private SfxContext LabContext() => new(
        SourcePos: new Vector3(0f, 0f, 0f),
        TargetPos: new Vector3((float)TargetDist, 0f, 0f),
        WeaponBonePos: new Vector3(0.3f, 1.2f, 0f));

    private void StopSim()
    {
        _runtime = null;
        _sink.Clear();
        RenderViewport();
    }

    private void SimTick(float dt)
    {
        if (_runtime is null && _sink.IsIdle) return;
        _runtime?.Tick(dt);
        _sink.Tick(dt);

        if (_runtime is not null && Loop
            && _runtime.LiveCoroutineCount == 0 && _runtime.LivePersistentCount == 0 && _sink.IsIdle)
        {
            _doneLinger += dt;
            if (_doneLinger > 0.45f) Respawn();
        }
        else _doneLinger = 0f;

        var unhandled = _runtime is { UnhandledVerbs.Count: > 0 }
            ? " · unhandled: " + string.Join(", ", _runtime.UnhandledVerbs.Take(4))
            : "";
        LiveStats = $"particles {_sink.LiveParticles} · shapes {_sink.LiveShapes}"
                    + (_runtime is null ? "" : $" · coroutines {_runtime.LiveCoroutineCount} · emitters {_runtime.LivePersistentCount}")
                    + unhandled;
        RenderViewport();
    }

    // ── viewport ─────────────────────────────────────────────────
    private ImageSource? _image;
    public ImageSource? Image { get => _image; private set => SetProperty(ref _image, value); }

    public void SetViewport(int w, int h)
    {
        if (w < 8 || h < 8) return;
        _vw = w; _vh = h;
        RenderViewport();
    }

    public void Orbit(double dx, double dy)
    {
        _yaw -= (float)dx * 0.008f;
        _pitch = Math.Clamp(_pitch + (float)dy * 0.008f, -1.45f, 1.45f);
        RenderViewport();
    }

    public void Pan(double dx, double dy)
    {
        // pan across the view plane: right = d(yaw), "up" mostly world +Z
        var right = new Vector3(-MathF.Sin(_yaw), MathF.Cos(_yaw), 0f);
        _pan -= right * (float)dx * _dist * 0.0016f;
        _pan.Z += (float)dy * _dist * 0.0016f;
        RenderViewport();
    }

    public void Zoom(int delta)
    {
        _dist = Math.Clamp(_dist * MathF.Pow(1.0015f, -delta), 1.2f, 80f);
        RenderViewport();
    }

    private void RenderViewport()
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var triColor = new List<int>();

        // checkered floor, render space XY plane (Z up), 1 m tiles
        const int half = 8;
        for (int gx = -half; gx < half; gx++)
            for (int gy = -half; gy < half; gy++)
            {
                int c = ((gx + gy) & 1) == 0 ? 0x2A2C31 : 0x33363C;
                AppendQuad(verts, normals, triColor,
                    new Vector3(gx, gy, 0f), new Vector3(gx + 1, gy, 0f),
                    new Vector3(gx + 1, gy + 1, 0f), new Vector3(gx, gy + 1, 0f), c);
            }

        // anchors: source (bronze) at origin, target (red) TargetDist east.
        // sim (x,y,z) → render (x,z,y): both sit on the floor.
        AppendCube(verts, normals, triColor, new Vector3(0f, 0f, 0.18f), 0.18f, 0xD8A657);
        AppendCube(verts, normals, triColor, new Vector3((float)TargetDist, 0f, 0.18f), 0.18f, 0xE03535);

        var splats = new List<SoftwareRenderer.Splat>(256);
        _sink.Collect(splats);

        var center = new Vector3((float)TargetDist * 0.5f, 0f, 0.7f) + _pan;
        var bgra = SoftwareRenderer.Render(verts.ToArray(), normals.ToArray(), _vw, _vh,
            center, MathF.Max(4f, (float)TargetDist), _yaw, _pitch, _dist,
            wireframe: false, lights: null, triColor: triColor.ToArray(),
            ortho: false, fog: null, splats: splats);
        var bmp = BitmapSource.Create(_vw, _vh, 96, 96, PixelFormats.Bgra32, null, bgra, _vw * 4);
        bmp.Freeze();
        Image = bmp;
    }

    private static void AppendQuad(List<Vector3> verts, List<Vector3> normals, List<int> triColor,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d, int color)
    {
        void V(Vector3 p) { verts.Add(p); normals.Add(Vector3.UnitZ); }
        V(a); V(b); V(c);
        V(a); V(c); V(d);
        triColor.Add(color);
        triColor.Add(color);
    }

    private static readonly Vector3[] CubeCorners =
    {
        new(-1,-1,-1), new(1,-1,-1), new(1,1,-1), new(-1,1,-1),
        new(-1,-1, 1), new(1,-1, 1), new(1,1, 1), new(-1,1, 1),
    };
    private static readonly int[] CubeTris =
    {
        0,1,2, 0,2,3,  4,6,5, 4,7,6,  0,4,5, 0,5,1,
        3,2,6, 3,6,7,  1,5,6, 1,6,2,  0,3,7, 0,7,4,
    };

    private static void AppendCube(List<Vector3> verts, List<Vector3> normals, List<int> triColor,
        Vector3 center, float size, int color)
    {
        for (int i = 0; i < CubeTris.Length; i += 3)
        {
            var a = center + CubeCorners[CubeTris[i]] * size;
            var b = center + CubeCorners[CubeTris[i + 1]] * size;
            var c = center + CubeCorners[CubeTris[i + 2]] * size;
            var n = Vector3.Cross(b - a, c - a);
            if (n.LengthSquared() > 1e-10f) n = Vector3.Normalize(n);
            verts.Add(a); verts.Add(b); verts.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            triColor.Add(color);
        }
    }

    // ── engine-true filmstrip ────────────────────────────────────
    private bool _filmstripBusy;
    private ImageSource? _filmstrip;
    public ImageSource? FilmstripImage { get => _filmstrip; private set => SetProperty(ref _filmstrip, value); }
    private string _filmstripLabel = "Engine filmstrip — renders this script through the game's real GL particle system.";
    public string FilmstripLabel { get => _filmstripLabel; private set => SetProperty(ref _filmstripLabel, value); }

    private async System.Threading.Tasks.Task RenderFilmstripAsync()
    {
        if (_selected is null || _filmstripBusy) return;
        var runtime = RuntimeLauncher.FindRuntime();
        if (runtime is null) { Status = "SiegeFX.Runtime not found — build the engine first."; return; }
        string? logic = FindTank("logic.dsres"), objects = FindTank("objects.dsres");
        if (logic is null || objects is null) { Status = "Logic.dsres / Objects.dsres not found among the install tanks."; return; }

        _filmstripBusy = true;
        RaiseCommands();
        FilmstripLabel = "Rendering through the engine…";
        try
        {
            var work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SiegeSmith", "fxlab");
            var fxDir = System.IO.Path.Combine(work, "effects");
            var outDir = System.IO.Path.Combine(work, "strips");
            Directory.CreateDirectory(fxDir);
            Directory.CreateDirectory(outDir);
            // current buffer + every project script (call targets must resolve)
            File.WriteAllText(System.IO.Path.Combine(fxDir, "_lab_buffer.gas"), WrapGas(_selected.Name, EditorText));
            foreach (var s in _all.Where(s => s.IsProject && !ReferenceEquals(s, _selected)))
                File.WriteAllText(System.IO.Path.Combine(fxDir, Sanitize(s.Name) + ".gas"), WrapGas(s.Name, s.Body));

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            if (runtime.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "dotnet";
                psi.ArgumentList.Add(runtime);
            }
            else psi.FileName = runtime;
            foreach (var a in new[]
            {
                "--sfx-filmstrip", logic, objects, _selected.Name,
                $"--script={_selected.Name}", $"--effects-dir={fxDir}", $"--out={outDir}",
                "--frames=36", "--strip=9", "--size=200",
                $"--target-dist={((float)TargetDist).ToString(CultureInfo.InvariantCulture)}",
            }) psi.ArgumentList.Add(a);

            using var proc = System.Diagnostics.Process.Start(psi)!;
            var stderr = proc.StandardError.ReadToEndAsync();
            _ = proc.StandardOutput.ReadToEndAsync();
            await System.Threading.Tasks.Task.Run(() => proc.WaitForExit(90_000));
            if (!proc.HasExited) { try { proc.Kill(); } catch { } }

            var png = System.IO.Path.Combine(outDir, _selected.Name + ".png");
            if (proc.ExitCode == 0 && File.Exists(png))
            {
                // fresh stream + OnLoad, so a re-render replaces the old strip
                var img = new BitmapImage();
                using (var fs = File.OpenRead(png))
                {
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = fs;
                    img.EndInit();
                }
                img.Freeze();
                FilmstripImage = img;
                FilmstripLabel = $"Engine-true: 9 frames over 1.8 s (seed 1, target {TargetDist:0.#} m).";
                Status = "Filmstrip rendered through the engine's real particle system.";
            }
            else
            {
                var err = (await stderr).Trim();
                FilmstripLabel = "Filmstrip failed" + (err.Length > 0 ? ": " + err.Split('\n')[^1].Trim() : $" (exit {proc.ExitCode}).");
            }
        }
        catch (Exception ex) { FilmstripLabel = "Filmstrip failed: " + ex.Message; }
        finally
        {
            _filmstripBusy = false;
            RaiseCommands();
        }
    }

    private string? FindTank(string fileName) =>
        _tankPaths.FirstOrDefault(p => System.IO.Path.GetFileName(p).Equals(fileName, StringComparison.OrdinalIgnoreCase));

    // ── prefs ────────────────────────────────────────────────────
    private string PrefsPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SiegeSmith", "effectslab.json");

    private sealed class LabPrefs
    {
        public string? AssetsFolder { get; set; }
        public double TargetDist { get; set; } = 4.0;
    }

    private void LoadPrefs()
    {
        try
        {
            if (!File.Exists(PrefsPath)) return;
            var p = System.Text.Json.JsonSerializer.Deserialize<LabPrefs>(File.ReadAllText(PrefsPath));
            if (p is null) return;
            if (p.AssetsFolder is not null && Directory.Exists(p.AssetsFolder)) _assetsFolder = p.AssetsFolder;
            _targetDist = Math.Clamp(p.TargetDist, 1.0, 16.0);
            OnPropertyChanged(nameof(AssetsLabel));
        }
        catch { /* prefs are a convenience */ }
    }

    private void SavePrefs()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefsPath)!);
            File.WriteAllText(PrefsPath, System.Text.Json.JsonSerializer.Serialize(
                new LabPrefs { AssetsFolder = _assetsFolder, TargetDist = _targetDist }));
        }
        catch { /* best effort */ }
    }
}
