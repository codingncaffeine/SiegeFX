using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
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
    public NodeRow? SelectedNode { get => _selectedNode; set { if (SetProperty(ref _selectedNode, value)) { OnPropertyChanged(nameof(HasSelectedNode)); OnNodeSelected(); } } }
    public bool HasSelectedNode => _selectedNode is not null;

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

    // ── object placement (props / pickups / actors) ────────────
    private TemplateCatalog? _props;
    private AspCatalog? _asp;                                    // resolves template model → .asp for preview
    private readonly Dictionary<string, string> _templateModel = new(StringComparer.OrdinalIgnoreCase); // template → aspect model
    private readonly List<PlacedObject> _objects = new();       // placed game objects (emitted to objects/*.gas)
    private uint _nextScid = 0x01000001;                        // map-scoped SCID allocator (shipped scids are 0x01xxxxxx)
    private readonly List<PropTemplate> _allProps = new();
    private readonly List<PropTemplate> _allActors = new();      // spawnable NPC/monster templates
    public ObservableCollection<PropTemplate> PropPalette { get; } = new();
    private string _propSearch = "";
    public string PropSearchText { get => _propSearch; set { if (SetProperty(ref _propSearch, value)) RefreshPropPalette(); } }
    private PropTemplate? _selectedProp;
    public PropTemplate? SelectedProp { get => _selectedProp; set { if (SetProperty(ref _selectedProp, value)) { UpdatePropThumb(); RaiseCommands(); } } }

    // Toggle: the one palette lists inert props (→ non_interactive.gas) or spawnable actors (→ actor.gas).
    private bool _placingActors;
    public bool PlacingActors
    {
        get => _placingActors;
        set
        {
            if (!SetProperty(ref _placingActors, value)) return;
            SelectedProp = null;
            OnPropertyChanged(nameof(PaletteHint));
            OnPropertyChanged(nameof(PlacingModeLabel));
            RebuildPropTags(); // ED-7 — actor tags differ from prop tags
            RefreshPropPalette();
        }
    }
    public string PlacingModeLabel => _placingActors ? "Mode: Actors (NPCs)" : "Mode: Props (scenery)";
    public string PaletteHint => _placingActors
        ? "Actors — NPCs & monsters (animate in-engine), placed into actor.gas."
        : "Props — inert scenery, placed into non_interactive.gas.";
    public ObservableCollection<PlacedObjectRow> PlacedObjects { get; } = new();
    private PlacedObjectRow? _selectedPlacedObject;
    public PlacedObjectRow? SelectedPlacedObject { get => _selectedPlacedObject; set { if (SetProperty(ref _selectedPlacedObject, value)) { OnPropertyChanged(nameof(HasSelectedPlacedObject)); RaiseObjTransform(); RaiseCommands(); } } }
    public bool HasSelectedPlacedObject => _selectedPlacedObject is not null;

    // SC-UX2 — numeric transform entry for the selected placed object. The
    // Inspector's X/Y/Z boxes edit the node-local offset; Yaw replaces the
    // orientation with a pure vertical-axis spin in degrees (the same axis
    // Shift-drag rotates around). Every commit is one undo step.
    private PlacedObject? SelObj() =>
        _selectedPlacedObject is null ? null : _objects.Find(x => x.Scid == _selectedPlacedObject.Scid);
    public string ObjPosX { get => SelObj()?.LocalPos.X.ToString("0.###", CultureInfo.InvariantCulture) ?? ""; set => SetObjPos(0, value); }
    public string ObjPosY { get => SelObj()?.LocalPos.Y.ToString("0.###", CultureInfo.InvariantCulture) ?? ""; set => SetObjPos(1, value); }
    public string ObjPosZ { get => SelObj()?.LocalPos.Z.ToString("0.###", CultureInfo.InvariantCulture) ?? ""; set => SetObjPos(2, value); }
    public string ObjYawDeg
    {
        get
        {
            if (SelObj() is not { } o) return "";
            float yaw = 2f * MathF.Atan2(o.Orientation.Z, o.Orientation.W) * 180f / MathF.PI;
            return yaw.ToString("0.#", CultureInfo.InvariantCulture);
        }
        set
        {
            if (SelObj() is not { } o) return;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var deg)) return;
            PushUndo();
            o.Orientation = Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), deg * MathF.PI / 180f);
            Render();
        }
    }
    private void SetObjPos(int axis, string text)
    {
        if (SelObj() is not { } o) return;
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;
        PushUndo();
        o.LocalPos = axis switch
        {
            0 => new Vector3(v, o.LocalPos.Y, o.LocalPos.Z),
            1 => new Vector3(o.LocalPos.X, v, o.LocalPos.Z),
            _ => new Vector3(o.LocalPos.X, o.LocalPos.Y, v),
        };
        Render();
    }
    /// <summary>ED-1b — read-only world-space position of the selected object;
    /// the editable X/Y/Z above are node-relative (local).</summary>
    public string ObjWorldText
    {
        get
        {
            if (SelObj() is not { } o || !_nodeWorld.TryGetValue(o.NodeGuid, out var nw)) return "";
            var w = Vector3.Transform(o.LocalPos, nw);
            return string.Create(CultureInfo.InvariantCulture,
                $"World: {w.X:0.##}, {w.Y:0.##}, {w.Z:0.##}  ·  node 0x{o.NodeGuid:X8}");
        }
    }

    // ED-4b — per-placement instance overrides (life, scale, guaranteed drop).
    // Empty box = no override = the template's own values; the writer emits
    // instance [aspect]/[inventory] blocks only when something is authored.
    public string ObjLife
    {
        get => SelObj() is { LifeOverride: > 0f } o ? o.LifeOverride.ToString("0.#", CultureInfo.InvariantCulture) : "";
        set
        {
            if (SelObj() is not { } o) return;
            float v = 0f;
            if (!string.IsNullOrWhiteSpace(value)
                && !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return;
            if (v < 0f) v = 0f;
            if (Math.Abs(o.LifeOverride - v) < 0.001f) return;
            PushUndo();
            o.LifeOverride = v;
            Status = v > 0f ? $"Life override {v:0.#} — this placement spawns with it regardless of the template."
                            : "Life override cleared — back to the template's value.";
        }
    }

    public string ObjScale
    {
        get
        {
            var o = SelObj();
            return o is null || Math.Abs(o.ScaleMult - 1f) < 0.0001f ? "" : o.ScaleMult.ToString("0.###", CultureInfo.InvariantCulture);
        }
        set
        {
            if (SelObj() is not { } o) return;
            float v = 1f;
            if (!string.IsNullOrWhiteSpace(value)
                && (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v <= 0f)) return;
            if (Math.Abs(o.ScaleMult - v) < 0.0001f) return;
            PushUndo();
            o.ScaleMult = v;
            Render();
            Status = Math.Abs(v - 1f) < 0.0001f ? "Scale override cleared."
                : $"Scale ×{v:0.###} — the engine renders this placement at that size (preview shows the marker only).";
        }
    }

    public string ObjLoot
    {
        get => SelObj()?.LootDrop ?? "";
        set
        {
            if (SelObj() is not { } o) return;
            var v = (value ?? "").Trim();
            if (o.LootDrop == v) return;
            PushUndo();
            o.LootDrop = v;
            Status = v.Length > 0
                ? $"Guaranteed drop '{v}' — dropped on death (actors) or found inside (containers), on top of the template's own loot."
                : "Guaranteed drop cleared.";
        }
    }

    private void RaiseObjTransform()
    {
        OnPropertyChanged(nameof(ObjPosX));
        OnPropertyChanged(nameof(ObjPosY));
        OnPropertyChanged(nameof(ObjPosZ));
        OnPropertyChanged(nameof(ObjYawDeg));
        OnPropertyChanged(nameof(ObjWorldText));
        OnPropertyChanged(nameof(ObjLife));
        OnPropertyChanged(nameof(ObjScale));
        OnPropertyChanged(nameof(ObjLoot));
    }

    // ── lighting & mood (LE-6) ──────────────────────────────────
    public ObservableCollection<AuthoredLight> Lights { get; } = new();
    private uint _nextLightScid = 0x02000001;
    private AuthoredLight? _selectedLight;
    public AuthoredLight? SelectedLight
    {
        get => _selectedLight;
        set { if (SetProperty(ref _selectedLight, value)) { RaiseLightProps(); RaiseCommands(); } }
    }
    public bool HasSelectedLight => _selectedLight is not null;
    public bool SelectedLightIsDirectional => _selectedLight?.Kind == AuthoredLightKind.Directional;

    private bool _moodInterior;
    public bool MoodInterior { get => _moodInterior; set { if (SetProperty(ref _moodInterior, value)) OnPropertyChanged(nameof(MoodInteriorLabel)); } }
    public string MoodInteriorLabel => _moodInterior ? "Interior (reverb): on" : "Interior (reverb): off";
    private string _moodAmbient = "", _moodStandard = "", _moodBattle = "";
    public string MoodAmbient { get => _moodAmbient; set => SetProperty(ref _moodAmbient, value); }
    public string MoodStandard { get => _moodStandard; set => SetProperty(ref _moodStandard, value); }
    public string MoodBattle { get => _moodBattle; set => SetProperty(ref _moodBattle, value); }

    // ED-8 — atmosphere authoring (fog auditions live in the viewport; rain/
    // snow/wind play in the real engine via Test/Play). Empty = component off.
    private string _moodFogNear = "", _moodFogFar = "", _moodFogColor = "";
    private string _moodRain = "", _moodSnow = "", _moodWindVel = "", _moodWindDir = "";
    private bool _moodLightning, _moodFogPreview = true;
    public string MoodFogNear { get => _moodFogNear; set { if (SetProperty(ref _moodFogNear, value)) Render(); } }
    public string MoodFogFar { get => _moodFogFar; set { if (SetProperty(ref _moodFogFar, value)) Render(); } }
    public string MoodFogColor { get => _moodFogColor; set { if (SetProperty(ref _moodFogColor, value)) Render(); } }
    public string MoodRainDensity { get => _moodRain; set => SetProperty(ref _moodRain, value); }
    public string MoodSnowDensity { get => _moodSnow; set => SetProperty(ref _moodSnow, value); }
    public string MoodWindVelocity { get => _moodWindVel; set => SetProperty(ref _moodWindVel, value); }
    public string MoodWindDirDeg { get => _moodWindDir; set => SetProperty(ref _moodWindDir, value); }
    public bool MoodLightning { get => _moodLightning; set => SetProperty(ref _moodLightning, value); }
    public bool MoodFogPreview { get => _moodFogPreview; set { if (SetProperty(ref _moodFogPreview, value)) Render(); } }

    private static float MoodF(string s, float fallback = 0f) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static uint MoodColor(string s, uint fallback)
    {
        var t = (s ?? "").Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        else if (t.StartsWith("#")) t = t[1..];
        if (t.Length == 6) t = "FF" + t; // RGB shorthand — opaque
        return uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    /// <summary>ED-8 — the viewport fog audition parameters, or null when fog
    /// isn't authored / preview is off.</summary>
    private SoftwareRenderer.Fog? PreviewFog()
    {
        if (!_moodFogPreview) return null;
        float far = MoodF(_moodFogFar, -1f);
        if (far <= 0f) return null;
        uint fc = MoodColor(_moodFogColor, 0xFF888888);
        return new SoftwareRenderer.Fog(MoodF(_moodFogNear), far,
            (byte)((fc >> 16) & 0xFF), (byte)((fc >> 8) & 0xFF), (byte)(fc & 0xFF));
    }

    // ── effects: emitters, decals, placed sound (LE-7) ──────────
    private uint _nextEffectScid = 0x03000001;
    public ObservableCollection<RegionEmitter> Emitters { get; } = new();
    private RegionEmitter? _selectedEmitter;
    public RegionEmitter? SelectedEmitter
    {
        get => _selectedEmitter;
        set { if (!SetProperty(ref _selectedEmitter, value)) return; RaiseEmitterProps(); RaiseCommands(); Render(); }
    }
    public bool HasSelectedEmitter => _selectedEmitter is not null;
    public bool SelectedEmitterSmoke
    {
        get => _selectedEmitter?.Smoke ?? false;
        set { if (_selectedEmitter is null || _selectedEmitter.Smoke == value) return; _selectedEmitter.Smoke = value; OnPropertyChanged(); RefreshEmitterItem(); Render(); }
    }
    public string SelectedEmitterCount
    {
        get => _selectedEmitter?.Count.ToString(CultureInfo.InvariantCulture) ?? "";
        set { if (_selectedEmitter is not null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) { _selectedEmitter.Count = System.Math.Clamp(v, 1, 500); OnPropertyChanged(); RefreshEmitterItem(); } }
    }
    public string SelectedEmitterSize
    {
        get => _selectedEmitter is null ? "" : _selectedEmitter.ParticleSize.ToString("0.0##", CultureInfo.InvariantCulture);
        set { if (_selectedEmitter is not null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { _selectedEmitter.ParticleSize = System.Math.Clamp(v, 0.05f, 8f); OnPropertyChanged(); } }
    }
    public string SelectedEmitterFade
    {
        get => _selectedEmitter is null ? "" : _selectedEmitter.Fade.ToString("0.0##", CultureInfo.InvariantCulture);
        set { if (_selectedEmitter is not null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { _selectedEmitter.Fade = System.Math.Clamp(v, 0.1f, 10f); OnPropertyChanged(); RefreshEmitterItem(); } }
    }
    public string SelectedEmitterGrowth
    {
        get => _selectedEmitter is null ? "" : _selectedEmitter.Growth.ToString("0.0##", CultureInfo.InvariantCulture);
        set { if (_selectedEmitter is not null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { _selectedEmitter.Growth = v; OnPropertyChanged(); } }
    }
    public string SelectedEmitterOffsetX
    {
        get => _selectedEmitter is null ? "" : _selectedEmitter.LocalPos.X.ToString("0.0##", CultureInfo.InvariantCulture);
        set { if (_selectedEmitter is not null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { var p = _selectedEmitter.LocalPos; _selectedEmitter.LocalPos = new Vector3(v, p.Y, p.Z); OnPropertyChanged(); Render(); } }
    }
    public string SelectedEmitterOffsetY
    {
        get => _selectedEmitter is null ? "" : _selectedEmitter.LocalPos.Y.ToString("0.0##", CultureInfo.InvariantCulture);
        set { if (_selectedEmitter is not null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { var p = _selectedEmitter.LocalPos; _selectedEmitter.LocalPos = new Vector3(p.X, v, p.Z); OnPropertyChanged(); Render(); } }
    }
    public string SelectedEmitterOffsetZ
    {
        get => _selectedEmitter is null ? "" : _selectedEmitter.LocalPos.Z.ToString("0.0##", CultureInfo.InvariantCulture);
        set { if (_selectedEmitter is not null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { var p = _selectedEmitter.LocalPos; _selectedEmitter.LocalPos = new Vector3(p.X, p.Y, v); OnPropertyChanged(); Render(); } }
    }
    private void RaiseEmitterProps()
    {
        OnPropertyChanged(nameof(HasSelectedEmitter));
        OnPropertyChanged(nameof(SelectedEmitterSmoke));
        OnPropertyChanged(nameof(SelectedEmitterCount));
        OnPropertyChanged(nameof(SelectedEmitterSize));
        OnPropertyChanged(nameof(SelectedEmitterFade));
        OnPropertyChanged(nameof(SelectedEmitterGrowth));
        OnPropertyChanged(nameof(SelectedEmitterOffsetX));
        OnPropertyChanged(nameof(SelectedEmitterOffsetY));
        OnPropertyChanged(nameof(SelectedEmitterOffsetZ));
    }
    private bool _refreshingEmitterItem;
    private void RefreshEmitterItem()
    {
        if (_selectedEmitter is null || _refreshingEmitterItem) return;
        int i = Emitters.IndexOf(_selectedEmitter);
        if (i < 0) return;
        _refreshingEmitterItem = true;             // re-fire the item template so Label/Detail recompute
        var keep = _selectedEmitter;
        Emitters[i] = _selectedEmitter;
        SelectedEmitter = keep;                    // Replace can drop the ListBox selection; restore it
        _refreshingEmitterItem = false;
    }

    public ObservableCollection<RegionDecal> Decals { get; } = new();
    private RegionDecal? _selectedDecal;
    public RegionDecal? SelectedDecal
    {
        get => _selectedDecal;
        set
        {
            if (!SetProperty(ref _selectedDecal, value)) return;
            OnPropertyChanged(nameof(HasSelectedDecal));
            OnPropertyChanged(nameof(SelectedDecalTexture));
            OnPropertyChanged(nameof(SelectedDecalHoriz));
            OnPropertyChanged(nameof(SelectedDecalVert));
            RaiseCommands();
        }
    }
    public bool HasSelectedDecal => _selectedDecal is not null;
    public string SelectedDecalTexture
    {
        get => _selectedDecal?.Texture ?? "";
        set { if (_selectedDecal is not null) _selectedDecal.Texture = value ?? ""; }
    }
    public string SelectedDecalHoriz
    {
        get => _selectedDecal is null ? "" : _selectedDecal.HorizExtent.ToString("0.0##", CultureInfo.InvariantCulture);
        set { if (_selectedDecal is not null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) _selectedDecal.HorizExtent = v; }
    }
    public string SelectedDecalVert
    {
        get => _selectedDecal is null ? "" : _selectedDecal.VertExtent.ToString("0.0##", CultureInfo.InvariantCulture);
        set { if (_selectedDecal is not null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) _selectedDecal.VertExtent = v; }
    }

    // Placed positional sound ships to objects/sound.gas (retail DS1 only — silent in the SiegeFX test).
    private string _soundTemplate = "";
    public string SoundTemplate { get => _soundTemplate; set { if (SetProperty(ref _soundTemplate, value)) RaiseCommands(); } }

    // ── logic: triggers, commands, conversations, quests (LE-8) ──
    private uint _nextLogicScid = 0x04000001;
    public string[] ConditionVerbs => RegionTrigger.Conditions;
    public string[] ActionVerbs => RegionTrigger.Actions;
    // GAME-4 — dialogue's activate_quest suggestions = shipped catalog keys
    // PLUS this map's own quests, so custom stories wire up as easily as
    // DS1's. The combo stays editable for anything else.
    public string[] QuestKeys
    {
        get
        {
            if (MapQuests.Count == 0) return QuestCatalogKeys.Keys;
            var list = new List<string>(MapQuests.Count + QuestCatalogKeys.Keys.Length);
            foreach (var q in MapQuests) list.Add(q.Key);
            list.AddRange(QuestCatalogKeys.Keys);
            return list.ToArray();
        }
    }
    public string[] DialogueChoices => DialogueLine.Choices;
    public CmdKind[] CommandKinds { get; } = (CmdKind[])System.Enum.GetValues(typeof(CmdKind));

    public ObservableCollection<RegionTrigger> Triggers { get; } = new();
    private RegionTrigger? _selectedTrigger;
    public RegionTrigger? SelectedTrigger
    {
        get => _selectedTrigger;
        set
        {
            if (!SetProperty(ref _selectedTrigger, value)) return;
            SelectedTriggerRow = value is { Rows.Count: > 0 } ? value.Rows[0] : null;
            OnPropertyChanged(nameof(HasSelectedTrigger));
            RaiseCommands();
        }
    }
    public bool HasSelectedTrigger => _selectedTrigger is not null;

    private TriggerRow? _selectedTriggerRow;
    public TriggerRow? SelectedTriggerRow
    {
        get => _selectedTriggerRow;
        set
        {
            if (!SetProperty(ref _selectedTriggerRow, value)) return;
            SelectedCondition = value is { Conditions.Count: > 0 } ? value.Conditions[0] : null;
            SelectedAction = value is { Actions.Count: > 0 } ? value.Actions[0] : null;
            OnPropertyChanged(nameof(HasSelectedTriggerRow));
            RaiseCommands();
        }
    }
    public bool HasSelectedTriggerRow => _selectedTriggerRow is not null;

    private TriggerCall? _selectedCondition;
    public TriggerCall? SelectedCondition
    {
        get => _selectedCondition;
        set { if (SetProperty(ref _selectedCondition, value)) { OnPropertyChanged(nameof(HasSelectedCondition)); RaiseCallProps("Cond"); RaiseCommands(); } }
    }
    public bool HasSelectedCondition => _selectedCondition is not null;
    public string CondVerb { get => _selectedCondition?.Verb ?? ""; set { if (_selectedCondition is not null) _selectedCondition.Verb = value ?? ""; } }
    public string CondArgs { get => _selectedCondition?.Args ?? ""; set { if (_selectedCondition is not null) _selectedCondition.Args = value ?? ""; } }
    public bool CondWhenFalse { get => _selectedCondition?.WhenFalse ?? false; set { if (_selectedCondition is not null) _selectedCondition.WhenFalse = value; } }
    public string CondGroup { get => _selectedCondition?.Group?.ToString() ?? ""; set { if (_selectedCondition is not null) _selectedCondition.Group = int.TryParse(value, out var g) ? g : null; } }
    public string CondDelay { get => _selectedCondition?.Delay?.ToString(CultureInfo.InvariantCulture) ?? ""; set { if (_selectedCondition is not null) _selectedCondition.Delay = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null; } }

    private TriggerCall? _selectedAction;
    public TriggerCall? SelectedAction
    {
        get => _selectedAction;
        set { if (SetProperty(ref _selectedAction, value)) { OnPropertyChanged(nameof(HasSelectedAction)); RaiseCallProps("Act"); RaiseCommands(); } }
    }
    public bool HasSelectedAction => _selectedAction is not null;
    public string ActVerb { get => _selectedAction?.Verb ?? ""; set { if (_selectedAction is not null) _selectedAction.Verb = value ?? ""; } }
    public string ActArgs { get => _selectedAction?.Args ?? ""; set { if (_selectedAction is not null) _selectedAction.Args = value ?? ""; } }
    public string ActGroup { get => _selectedAction?.Group?.ToString() ?? ""; set { if (_selectedAction is not null) _selectedAction.Group = int.TryParse(value, out var g) ? g : null; } }
    public string ActDelay { get => _selectedAction?.Delay?.ToString(CultureInfo.InvariantCulture) ?? ""; set { if (_selectedAction is not null) _selectedAction.Delay = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null; } }

    private void RaiseCallProps(string prefix)
    {
        OnPropertyChanged(prefix + "Verb");
        OnPropertyChanged(prefix + "Args");
        OnPropertyChanged(prefix + "Group");
        OnPropertyChanged(prefix + "Delay");
        if (prefix == "Cond") OnPropertyChanged(nameof(CondWhenFalse));
    }

    public ObservableCollection<CommandPlacement> Commands { get; } = new();
    private CommandPlacement? _selectedCommand;
    public CommandPlacement? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (!SetProperty(ref _selectedCommand, value)) return;
            OnPropertyChanged(nameof(HasSelectedCommand));
            OnPropertyChanged(nameof(CmdKindSel));
            OnPropertyChanged(nameof(CmdNextScid));
            OnPropertyChanged(nameof(CmdTarget1));
            OnPropertyChanged(nameof(CmdTarget2));
            OnPropertyChanged(nameof(CmdClientScid));
            OnPropertyChanged(nameof(CmdDuration));
            OnPropertyChanged(nameof(CmdOrder));
            RaiseCommands();
        }
    }
    public bool HasSelectedCommand => _selectedCommand is not null;
    public CmdKind CmdKindSel { get => _selectedCommand?.Kind ?? CmdKind.AiPatrol; set { if (_selectedCommand is not null) _selectedCommand.Kind = value; } }
    public string CmdNextScid { get => Hex(_selectedCommand?.NextScid); set { if (_selectedCommand is not null) _selectedCommand.NextScid = ParseHex(value); } }
    public string CmdTarget1 { get => Hex(_selectedCommand?.Target1); set { if (_selectedCommand is not null) _selectedCommand.Target1 = ParseHex(value); } }
    public string CmdTarget2 { get => Hex(_selectedCommand?.Target2); set { if (_selectedCommand is not null) _selectedCommand.Target2 = ParseHex(value); } }
    public string CmdClientScid { get => Hex(_selectedCommand?.ClientScid); set { if (_selectedCommand is not null) _selectedCommand.ClientScid = ParseHex(value); } }
    public string CmdDuration { get => _selectedCommand?.Duration?.ToString(CultureInfo.InvariantCulture) ?? ""; set { if (_selectedCommand is not null) _selectedCommand.Duration = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null; } }
    public string CmdOrder { get => _selectedCommand?.Order ?? ""; set { if (_selectedCommand is not null) _selectedCommand.Order = value ?? ""; } }

    private static string Hex(uint? v) => v is uint u ? $"0x{u:X8}" : "";
    private static uint? ParseHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public ObservableCollection<Conversation> Conversations { get; } = new();
    private Conversation? _selectedConversation;
    public Conversation? SelectedConversation
    {
        get => _selectedConversation;
        set
        {
            if (!SetProperty(ref _selectedConversation, value)) return;
            SelectedDialogue = value is { Nodes.Count: > 0 } ? value.Nodes[0] : null;
            OnPropertyChanged(nameof(HasSelectedConversation));
            OnPropertyChanged(nameof(SelectedConversationKey));
            RaiseCommands();
        }
    }
    public bool HasSelectedConversation => _selectedConversation is not null;
    public string SelectedConversationKey
    {
        get => _selectedConversation?.Key ?? "";
        set { if (_selectedConversation is not null) _selectedConversation.Key = value ?? ""; }
    }

    private DialogueLine? _selectedDialogue;
    public DialogueLine? SelectedDialogue { get => _selectedDialogue; set { if (SetProperty(ref _selectedDialogue, value)) { OnPropertyChanged(nameof(HasSelectedDialogue)); RaiseCommands(); } } }
    public bool HasSelectedDialogue => _selectedDialogue is not null;

    // ── world stitching: multi-region adjacency (LE-9) ─────────
    private uint _nextRegionGuid = 0x0A000001;
    private uint _nextPairId = 0x0B000001;
    private uint _primarySourceGuid;
    private int _stitchDangling, _stitchCollisions, _stitchUnreachable;
    public ObservableCollection<RegionStitch> PrimaryStitches { get; } = new();
    public ObservableCollection<StitchDoor> PrimaryFreeDoors { get; } = new();
    public ObservableCollection<StitchRegionRef> Siblings { get; } = new();
    public ObservableCollection<StitchDoor> SiblingFreeDoors { get; } = new();

    private StitchDoor? _selectedPrimaryDoor;
    public StitchDoor? SelectedPrimaryDoor { get => _selectedPrimaryDoor; set { if (SetProperty(ref _selectedPrimaryDoor, value)) RaiseCommands(); } }

    private StitchRegionRef? _selectedSibling;
    public StitchRegionRef? SelectedSibling
    {
        get => _selectedSibling;
        set { if (SetProperty(ref _selectedSibling, value)) { OnPropertyChanged(nameof(HasSelectedSibling)); RaiseSiblingDoors(); RaiseCommands(); } }
    }
    public bool HasSelectedSibling => _selectedSibling is not null;

    private StitchDoor? _selectedSiblingDoor;
    public StitchDoor? SelectedSiblingDoor { get => _selectedSiblingDoor; set { if (SetProperty(ref _selectedSiblingDoor, value)) RaiseCommands(); } }

    private RegionStitch? _selectedStitch;
    public RegionStitch? SelectedStitch { get => _selectedStitch; set { if (SetProperty(ref _selectedStitch, value)) RaiseCommands(); } }

    private string _stitchDiagnostics = "No stitches — the region is a standalone (isolated) world.";
    public string StitchDiagnostics { get => _stitchDiagnostics; private set => SetProperty(ref _stitchDiagnostics, value); }

    // Graphical world map: region boxes + stitch edges, laid out primary-centre with siblings on a ring.
    public ObservableCollection<WorldGraphNode> WorldGraphNodes { get; } = new();
    public ObservableCollection<WorldGraphEdge> WorldGraphEdges { get; } = new();

    // ── nav flags & validation (LE-10) ─────────────────────────
    public ObservableCollection<LogicalFlag> LogicalFlags { get; } = new();
    private LogicalFlag? _selectedFlag;
    public LogicalFlag? SelectedFlag
    {
        get => _selectedFlag;
        set { if (SetProperty(ref _selectedFlag, value)) { OnPropertyChanged(nameof(HasSelectedFlag)); RaiseFlagProps(); RaiseCommands(); } }
    }
    public bool HasSelectedFlag => _selectedFlag is not null;
    public bool FlagHuman { get => _selectedFlag?.HumanPlayer ?? false; set { if (_selectedFlag is not null) _selectedFlag.HumanPlayer = value; } }
    public bool FlagComputer { get => _selectedFlag?.ComputerPlayer ?? false; set { if (_selectedFlag is not null) _selectedFlag.ComputerPlayer = value; } }
    public bool FlagWater { get => _selectedFlag?.Water ?? false; set { if (_selectedFlag is not null) _selectedFlag.Water = value; } }
    public string FlagSurfaceTag { get => _selectedFlag?.SurfaceTag ?? ""; set { if (_selectedFlag is not null) _selectedFlag.SurfaceTag = value ?? ""; } }
    private void RaiseFlagProps()
    {
        OnPropertyChanged(nameof(FlagHuman));
        OnPropertyChanged(nameof(FlagComputer));
        OnPropertyChanged(nameof(FlagWater));
        OnPropertyChanged(nameof(FlagSurfaceTag));
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
    private uint[] _pickScid = System.Array.Empty<uint>();         // placed-object SCID per pick triangle (0 = terrain)

    // Unified selectable-marker registry, rebuilt each Render: every node-anchored placeable that has no
    // mesh of its own — emitter, trigger, command, decal, point light — keyed by its SCID, so one
    // click-select + drag path covers all of them (a level editor lets you grab every piece).
    private readonly Dictionary<uint, object> _markers = new();
    private object? _dragEffect; // the marker Item under an active drag, or null when dragging a placed object
    private readonly Dictionary<uint, Matrix4x4> _nodeWorld = new(); // node GUID → world transform, for object drag
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

    // A folder laid out like a tank (art/…, world/…) overlaid into every packaged map, so custom
    // textures/meshes ship in-engine. Authored region files always win on a path collision.
    private string? _assetsFolder;
    public string? AssetsFolder
    {
        get => _assetsFolder;
        private set { if (SetProperty(ref _assetsFolder, value)) OnPropertyChanged(nameof(AssetsLabel)); }
    }
    public string AssetsLabel => _assetsFolder is null
        ? "No custom assets — optional. Add a folder laid out like a tank (art/…) to bundle custom art."
        : $"Bundling {MapPackager.CountAssets(_assetsFolder):N0} file(s) from {_assetsFolder}";

    // --- Custom terrain tile authoring (LE-12) ---
    private string _tileName = "ss_tile";
    public string TileName { get => _tileName; set => SetProperty(ref _tileName, value); }
    private string _tileTexture = "t_grs01_grass";
    public string TileTexture { get => _tileTexture; set => SetProperty(ref _tileTexture, value); }
    public string[] TileKinds { get; } = { "Flat", "Ramp" };
    private string _tileKind = "Flat";
    public string TileKind { get => _tileKind; set { if (SetProperty(ref _tileKind, value)) OnPropertyChanged(nameof(TileIsRamp)); } }
    public bool TileIsRamp => _tileKind == "Ramp";
    private double _tileSize = 8;
    public double TileSize { get => _tileSize; set => SetProperty(ref _tileSize, value); }
    private int _tileSubdiv = 4;
    public int TileSubdiv { get => _tileSubdiv; set => SetProperty(ref _tileSubdiv, value); }
    private double _tileRise = 2;
    public double TileRise { get => _tileRise; set => SetProperty(ref _tileRise, value); }
    private bool _tileWalkable = true;
    public bool TileWalkable { get => _tileWalkable; set => SetProperty(ref _tileWalkable, value); }
    private uint _nextTerrainGuid = 0xC0000001;

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
    public RelayCommand SetAssetsFolderCommand { get; }
    public RelayCommand OpenAssetsFolderCommand { get; }
    public RelayCommand ImportObjMeshCommand { get; }
    public RelayCommand GenerateTerrainTileCommand { get; }
    public RelayCommand PlaceObjectCommand { get; }
    public RelayCommand DeleteObjectCommand { get; }
    public RelayCommand TogglePlacingCommand { get; }
    public RelayCommand AddDirectionalLightCommand { get; }
    public RelayCommand AddPointLightCommand { get; }
    public RelayCommand DeleteLightCommand { get; }
    public RelayCommand ToggleMoodInteriorCommand { get; }
    public RelayCommand AddFireEmitterCommand { get; }
    public RelayCommand AddSmokeEmitterCommand { get; }
    public RelayCommand DeleteEmitterCommand { get; }
    public RelayCommand AddDecalCommand { get; }
    public RelayCommand DeleteDecalCommand { get; }
    public RelayCommand AddSoundCommand { get; }
    public RelayCommand AddTriggerCommand { get; }
    public RelayCommand DeleteTriggerCommand { get; }
    public RelayCommand AddTriggerRowCommand { get; }
    public RelayCommand DeleteTriggerRowCommand { get; }
    public RelayCommand AddConditionCommand { get; }
    public RelayCommand DeleteConditionCommand { get; }
    public RelayCommand AddActionCommand { get; }
    public RelayCommand DeleteActionCommand { get; }
    public RelayCommand AddCommandCommand { get; }
    public RelayCommand DeleteCommandCommand { get; }
    public RelayCommand AddConversationCommand { get; }
    public RelayCommand DeleteConversationCommand { get; }
    public RelayCommand AddDialogueCommand { get; }
    public RelayCommand DeleteDialogueCommand { get; }
    public RelayCommand BindConversationCommand { get; }
    public RelayCommand ImportSiblingCommand { get; }
    public RelayCommand RemoveSiblingCommand { get; }
    public RelayCommand CreateStitchCommand { get; }
    public RelayCommand DeleteStitchCommand { get; }
    public RelayCommand AddNavFlagCommand { get; }
    public RelayCommand DeleteNavFlagCommand { get; }
    /// <summary>Del key — deletes whatever piece is currently selected,
    /// routed to the matching per-type delete (markers first, then placed
    /// object, then node). One key, every piece.</summary>
    public RelayCommand DeleteSelectedAnyCommand { get; }
    public RelayCommand ImportTextureCommand { get; }
    public RelayCommand CreateTemplateCommand { get; }
    public RelayCommand AddQuestCommand { get; }
    public RelayCommand DeleteQuestCommand { get; }
    public RelayCommand ImportAudioCommand { get; }
    /// <summary>ED-1a — Ctrl+C/Ctrl+V. Copy remembers the selected piece;
    /// paste recreates it on the CURRENTLY SELECTED NODE (so copy → pick
    /// another node → paste moves content across the region), or beside the
    /// original when no other node is picked.</summary>
    public RelayCommand CopyCommand { get; }
    public RelayCommand PasteCommand { get; }
    private object? _clipboard;

    private void CopySelected()
    {
        _clipboard = (object?)SelObj() ?? _selectedEmitter ?? _selectedDecal
                   ?? (_selectedLight is { Kind: AuthoredLightKind.Point } ? _selectedLight : null)
                   ?? (object?)_selectedTrigger ?? _selectedCommand;
        Status = _clipboard is null
            ? "Select an object, emitter, decal, point light, trigger, or command to copy."
            : "Copied — select a target node and press Ctrl+V.";
        PasteCommand.RaiseCanExecuteChanged();
    }

    private void PasteClipboard()
    {
        if (_clipboard is null) return;
        uint SrcNode(uint fallback) => _selectedNode?.Guid ?? fallback;
        Vector3 Pos(uint node, Vector3 srcPos) =>
            _selectedNode is not null && _selectedNode.Guid != NodeOf(_clipboard)
                ? LocalCenter(node) : srcPos + DupOffset;
        static uint NodeOfStatic(object item) => item switch
        {
            PlacedObject p => p.NodeGuid,
            RegionEmitter e => e.NodeGuid,
            RegionDecal d => d.NodeGuid,
            AuthoredLight l => l.NodeGuid,
            RegionTrigger t => t.NodeGuid,
            CommandPlacement c => c.NodeGuid,
            _ => 0u,
        };
        uint NodeOf(object item) => NodeOfStatic(item);

        PushUndo();
        switch (_clipboard)
        {
            case PlacedObject src:
            {
                uint node = SrcNode(src.NodeGuid);
                var copy = src.Clone(); // overrides (life/scale/loot) ride along
                copy.Scid = _nextScid++;
                copy.NodeGuid = node;
                copy.LocalPos = Pos(node, src.LocalPos);
                _objects.Add(copy);
                RebuildPlacedRows();
                foreach (var r in PlacedObjects) if (r.Scid == copy.Scid) { SelectedPlacedObject = r; break; }
                break;
            }
            case RegionEmitter src:
            {
                uint node = SrcNode(src.NodeGuid);
                var copy = new RegionEmitter
                {
                    Scid = _nextEffectScid++, Template = src.Template, NodeGuid = node,
                    LocalPos = Pos(node, src.LocalPos), Smoke = src.Smoke,
                    Count = src.Count, Fade = src.Fade, ParticleSize = src.ParticleSize, Growth = src.Growth,
                };
                Emitters.Add(copy);
                SelectMarker(copy);
                break;
            }
            case RegionDecal src:
            {
                uint node = SrcNode(src.NodeGuid);
                var copy = new RegionDecal
                {
                    Scid = _nextEffectScid++, NodeGuid = node, OriginLocal = Pos(node, src.OriginLocal),
                    Normal = src.Normal, AxisH = src.AxisH, AxisV = src.AxisV,
                    HorizExtent = src.HorizExtent, VertExtent = src.VertExtent, Texture = src.Texture,
                };
                Decals.Add(copy);
                SelectMarker(copy);
                break;
            }
            case AuthoredLight src:
            {
                uint node = SrcNode(src.NodeGuid);
                var copy = new AuthoredLight
                {
                    Scid = _nextLightScid++, Kind = AuthoredLightKind.Point, NodeGuid = node,
                    Position = Pos(node, src.Position), Color = src.Color, Intensity = src.Intensity,
                    InnerRadius = src.InnerRadius, OuterRadius = src.OuterRadius,
                    DrawShadow = src.DrawShadow, AffectsActors = src.AffectsActors,
                    AffectsItems = src.AffectsItems, AffectsTerrain = src.AffectsTerrain,
                };
                Lights.Add(copy);
                SelectMarker(copy);
                break;
            }
            case RegionTrigger src:
            {
                uint node = SrcNode(src.NodeGuid);
                var copy = new RegionTrigger { Scid = _nextLogicScid++, Template = src.Template, NodeGuid = node, LocalPos = Pos(node, src.LocalPos) };
                Triggers.Add(copy);
                SelectMarker(copy);
                break;
            }
            case CommandPlacement src:
            {
                uint node = SrcNode(src.NodeGuid);
                var copy = new CommandPlacement
                {
                    Scid = _nextLogicScid++, Kind = src.Kind, NodeGuid = node, LocalPos = Pos(node, src.LocalPos),
                    NextScid = src.NextScid, Target1 = src.Target1, Target2 = src.Target2,
                    ClientScid = src.ClientScid, Duration = src.Duration, Order = src.Order,
                };
                Commands.Add(copy);
                SelectMarker(copy);
                break;
            }
        }
        Render();
        Status = "Pasted.";
    }

    // ED-1a — recently used palette entries (max 8, most-recent first).
    public ObservableCollection<string> RecentMeshes { get; } = new();
    public ObservableCollection<string> RecentProps { get; } = new();
    public bool HasRecentMeshes => RecentMeshes.Count > 0;
    public bool HasRecentProps => RecentProps.Count > 0;

    // ED-1a — persisted favorites (%APPDATA%\SiegeSmith\worldbuilder.json).
    public ObservableCollection<string> FavoriteMeshes { get; } = new();
    public ObservableCollection<string> FavoriteProps { get; } = new();
    public bool HasFavoriteMeshes => FavoriteMeshes.Count > 0;
    public bool HasFavoriteProps => FavoriteProps.Count > 0;
    public RelayCommand ToggleFavoriteMeshCommand { get; }
    public RelayCommand ToggleFavoritePropCommand { get; }
    private string? _selectedFavoriteMesh;
    public string? SelectedFavoriteMesh
    {
        get => _selectedFavoriteMesh;
        set
        {
            if (!SetProperty(ref _selectedFavoriteMesh, value) || value is null) return;
            foreach (var m in _allMeshes) if (m.Name == value) { SelectedMesh = m; break; }
        }
    }
    private string? _selectedFavoriteProp;
    public string? SelectedFavoriteProp
    {
        get => _selectedFavoriteProp;
        set
        {
            if (!SetProperty(ref _selectedFavoriteProp, value) || value is null) return;
            foreach (var p in _allProps) if (p.Name == value) { SelectedProp = p; return; }
            foreach (var a in _allActors) if (a.Name == value) { SelectedProp = a; return; }
        }
    }

    private void ToggleFavorite(ObservableCollection<string> list, string? name, string what)
    {
        if (string.IsNullOrEmpty(name)) { Status = $"Select a {what} in the palette first."; return; }
        if (!list.Remove(name)) { list.Insert(0, name); Status = $"★ {name} added to favorites."; }
        else Status = $"{name} removed from favorites.";
        OnPropertyChanged(nameof(HasFavoriteMeshes));
        OnPropertyChanged(nameof(HasFavoriteProps));
        SaveWorldBuilderPrefs();
    }

    private string PrefsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SiegeSmith", "worldbuilder.json");

    private sealed class WbPrefs
    {
        public List<string> FavoriteMeshes { get; set; } = new();
        public List<string> FavoriteProps { get; set; } = new();
    }

    private void LoadWorldBuilderPrefs()
    {
        try
        {
            if (!File.Exists(PrefsPath)) return;
            var p = System.Text.Json.JsonSerializer.Deserialize<WbPrefs>(File.ReadAllText(PrefsPath));
            if (p is null) return;
            foreach (var m in p.FavoriteMeshes) FavoriteMeshes.Add(m);
            foreach (var t in p.FavoriteProps) FavoriteProps.Add(t);
            OnPropertyChanged(nameof(HasFavoriteMeshes));
            OnPropertyChanged(nameof(HasFavoriteProps));
        }
        catch { /* favorites are a convenience — never block startup */ }
    }

    private void SaveWorldBuilderPrefs()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath)!);
            var p = new WbPrefs
            {
                FavoriteMeshes = new List<string>(FavoriteMeshes),
                FavoriteProps = new List<string>(FavoriteProps),
            };
            File.WriteAllText(PrefsPath, System.Text.Json.JsonSerializer.Serialize(p));
        }
        catch { /* best effort */ }
    }
    private string? _selectedRecentMesh;
    public string? SelectedRecentMesh
    {
        get => _selectedRecentMesh;
        set
        {
            if (!SetProperty(ref _selectedRecentMesh, value) || value is null) return;
            foreach (var m in _allMeshes) if (m.Name == value) { SelectedMesh = m; break; }
        }
    }
    private string? _selectedRecentProp;
    public string? SelectedRecentProp
    {
        get => _selectedRecentProp;
        set
        {
            if (!SetProperty(ref _selectedRecentProp, value) || value is null) return;
            foreach (var p in _allProps) if (p.Name == value) { SelectedProp = p; return; }
            foreach (var a in _allActors) if (a.Name == value) { SelectedProp = a; return; }
        }
    }
    private void PushRecent(ObservableCollection<string> list, string name)
    {
        list.Remove(name);
        list.Insert(0, name);
        while (list.Count > 8) list.RemoveAt(list.Count - 1);
        OnPropertyChanged(nameof(HasRecentMeshes));
        OnPropertyChanged(nameof(HasRecentProps));
    }

    // ═══ ED-1b — multi-select ════════════════════════════════════════
    // Ctrl+click builds a selection SET across every positionable family
    // (objects, emitters, decals, point lights, triggers, commands).
    // Dragging any member moves the whole group; Shift-drag orbits the
    // group about its centroid; Ctrl+D duplicates all; Del deletes all.

    private readonly List<object> _multiSel = new();
    public bool IsMultiSelect => _multiSel.Count > 1;
    public string MultiText => $"{_multiSel.Count} pieces selected";
    public RelayCommand AlignXCommand { get; }
    public RelayCommand AlignYCommand { get; }
    public RelayCommand DistributeXCommand { get; }
    public RelayCommand DistributeYCommand { get; }

    /// <summary>15°-step rotation snap for Shift-drag (toolbar toggle).</summary>
    private bool _snapAngle;
    public bool SnapAngle { get => _snapAngle; set => SetProperty(ref _snapAngle, value); }

    private void RaiseMulti()
    {
        OnPropertyChanged(nameof(IsMultiSelect));
        OnPropertyChanged(nameof(MultiText));
        AlignXCommand?.RaiseCanExecuteChanged();
        AlignYCommand?.RaiseCanExecuteChanged();
        DistributeXCommand?.RaiseCanExecuteChanged();
        DistributeYCommand?.RaiseCanExecuteChanged();
    }

    private object? PrimaryPiece() =>
        (object?)SelObj() ?? _selectedEmitter ?? _selectedDecal
        ?? (_selectedLight is { Kind: AuthoredLightKind.Point } ? _selectedLight : null)
        ?? (object?)_selectedTrigger ?? _selectedCommand;

    private object? PieceByScid(uint scid) =>
        (object?)_objects.Find(x => x.Scid == scid)
        ?? (_markers.TryGetValue(scid, out var m) ? m : null);

    private static uint PieceNode(object item) => item is PlacedObject p ? p.NodeGuid : EffectNode(item);

    private static Vector3 GetPieceLocal(object item) => item switch
    {
        PlacedObject p => p.LocalPos,
        RegionEmitter e => e.LocalPos,
        RegionTrigger t => t.LocalPos,
        CommandPlacement c => c.LocalPos,
        RegionDecal d => d.OriginLocal,
        AuthoredLight l => l.Position,
        _ => default,
    };

    private void SetPieceLocal(object item, Vector3 p)
    {
        if (item is PlacedObject po) po.LocalPos = p;
        else SetEffectPos(item, p);
    }

    private bool PieceAlive(object m) => m switch
    {
        PlacedObject p => _objects.Contains(p),
        RegionEmitter e => Emitters.Contains(e),
        RegionDecal d => Decals.Contains(d),
        AuthoredLight l => Lights.Contains(l),
        RegionTrigger t => Triggers.Contains(t),
        CommandPlacement c => Commands.Contains(c),
        _ => false,
    };

    /// <summary>Drops members that a per-family delete or an undo restore
    /// removed from the region, so group operations never touch ghosts.</summary>
    private void PruneMulti() => _multiSel.RemoveAll(m => !PieceAlive(m));

    /// <summary>Ctrl+click — toggles the piece under the cursor in/out of the
    /// multi-selection. The first Ctrl+click seeds the set with the current
    /// selection, so "select one, Ctrl+click the next" just works.</summary>
    public bool TryToggleMultiSelect(double sx, double sy)
    {
        if (_pickVerts.Length < 3) return false;
        uint scid = SoftwareRenderer.PickTriangle(_pickVerts, _pickScid, _vw, _vh,
            _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy, _ortho);
        if (scid == 0) return false;
        var piece = PieceByScid(scid);
        if (piece is null) return false;

        if (_multiSel.Count == 0 && PrimaryPiece() is { } primary && !ReferenceEquals(primary, piece))
            _multiSel.Add(primary);

        if (_multiSel.Remove(piece))
            Status = $"Removed from selection — {_multiSel.Count} left.";
        else
        {
            _multiSel.Add(piece);
            SelectPiece(piece);
            Status = $"{_multiSel.Count} selected — drag any member to move the group, "
                   + "Shift-drag rotates it, Ctrl+D duplicates all, Del deletes all.";
        }
        RaiseMulti();
        Render();
        return true;
    }

    /// <summary>Plain click on empty terrain drops the multi-selection —
    /// the same "click away to deselect" every editor teaches.</summary>
    public void ClearMultiSelect()
    {
        if (_multiSel.Count == 0) return;
        _multiSel.Clear();
        RaiseMulti();
        Render();
    }

    private void KeepOrClearMulti(object? piece)
    {
        if (piece is not null && _multiSel.Contains(piece)) return; // grabbing a member drags the group
        if (_multiSel.Count == 0) return;
        _multiSel.Clear();
        RaiseMulti();
    }

    /// <summary>Routes a piece model to the matching single-selection so the
    /// Inspector always shows the last piece added to the set.</summary>
    private void SelectPiece(object piece)
    {
        if (piece is PlacedObject po)
        {
            foreach (var r in PlacedObjects) if (r.Scid == po.Scid) { SelectedPlacedObject = r; break; }
            SelectedEmitter = null; SelectedDecal = null; SelectedLight = null;
            SelectedTrigger = null; SelectedCommand = null;
        }
        else SelectMarker(piece);
    }

    /// <summary>Moves every other member of the group by the same world-space
    /// delta the dragged anchor just moved. Members stay anchored to their own
    /// nodes — the group slides as one, whatever it spans.</summary>
    private void ApplyGroupDelta(object anchor, Vector3 deltaWorld)
    {
        if (_multiSel.Count < 2 || !_multiSel.Contains(anchor) || deltaWorld == Vector3.Zero) return;
        PruneMulti();
        foreach (var m in _multiSel)
        {
            if (ReferenceEquals(m, anchor)) continue;
            if (!_nodeWorld.TryGetValue(PieceNode(m), out var nw) || !Matrix4x4.Invert(nw, out var inv)) continue;
            SetPieceLocal(m, Vector3.Transform(Vector3.Transform(GetPieceLocal(m), nw) + deltaWorld, inv));
        }
    }

    // Shift-drag rotation is a GESTURE: orientations and world positions are
    // captured at grab time and recomputed from the accumulated angle every
    // move — so 15° snapping quantizes cleanly instead of fighting the deltas.
    private float _rotAccum;
    private List<(object Item, Quaternion Orient, Vector3 World)>? _rotStart;
    private Vector3 _rotCentroid;

    private void BeginRotateGesture(object anchor)
    {
        _rotAccum = 0f;
        _rotStart = null;
        PruneMulti();
        var members = _multiSel.Count > 1 && _multiSel.Contains(anchor)
            ? (IReadOnlyList<object>)_multiSel : new[] { anchor };
        var list = new List<(object, Quaternion, Vector3)>();
        var sum = Vector3.Zero;
        foreach (var m in members)
        {
            if (!_nodeWorld.TryGetValue(PieceNode(m), out var nw)) continue;
            var w = Vector3.Transform(GetPieceLocal(m), nw);
            list.Add((m, (m as PlacedObject)?.Orientation ?? Quaternion.Identity, w));
            sum += w;
        }
        if (list.Count == 0) return;
        _rotCentroid = sum / list.Count;
        _rotStart = list;
    }

    /// <summary>Align the multi-selection on a world axis (0=X, 1=Y) — every
    /// member moves to the group's average coordinate, so the row straightens
    /// without anything jumping far.</summary>
    private void AlignSelected(int axis)
    {
        PruneMulti();
        if (_multiSel.Count < 2) { Status = "Ctrl+click at least two pieces to align."; return; }
        PushUndo();
        var worlds = new List<(object Item, Vector3 W, Matrix4x4 Inv)>();
        float sum = 0;
        foreach (var m in _multiSel)
        {
            if (!_nodeWorld.TryGetValue(PieceNode(m), out var nw) || !Matrix4x4.Invert(nw, out var inv)) continue;
            var w = Vector3.Transform(GetPieceLocal(m), nw);
            worlds.Add((m, w, inv));
            sum += axis == 0 ? w.X : w.Y;
        }
        if (worlds.Count < 2) return;
        float target = sum / worlds.Count;
        foreach (var (item, w, inv) in worlds)
            SetPieceLocal(item, Vector3.Transform(
                axis == 0 ? new Vector3(target, w.Y, w.Z) : new Vector3(w.X, target, w.Z), inv));
        RaiseObjTransform();
        Render();
        Status = $"Aligned {worlds.Count} pieces on world {(axis == 0 ? "X" : "Y")}.";
    }

    /// <summary>Distribute the multi-selection evenly along a world axis —
    /// the two outermost members stay put, everything between spaces out.</summary>
    private void DistributeSelected(int axis)
    {
        PruneMulti();
        if (_multiSel.Count < 3) { Status = "Ctrl+click at least three pieces to distribute."; return; }
        PushUndo();
        var worlds = new List<(object Item, Vector3 W, Matrix4x4 Inv)>();
        foreach (var m in _multiSel)
        {
            if (!_nodeWorld.TryGetValue(PieceNode(m), out var nw) || !Matrix4x4.Invert(nw, out var inv)) continue;
            worlds.Add((m, Vector3.Transform(GetPieceLocal(m), nw), inv));
        }
        if (worlds.Count < 3) return;
        worlds.Sort((a, b) => (axis == 0 ? a.W.X : a.W.Y).CompareTo(axis == 0 ? b.W.X : b.W.Y));
        float lo = axis == 0 ? worlds[0].W.X : worlds[0].W.Y;
        float hi = axis == 0 ? worlds[^1].W.X : worlds[^1].W.Y;
        for (int i = 1; i < worlds.Count - 1; i++)
        {
            float t = lo + (hi - lo) * i / (worlds.Count - 1);
            var w = worlds[i].W;
            SetPieceLocal(worlds[i].Item, Vector3.Transform(
                axis == 0 ? new Vector3(t, w.Y, w.Z) : new Vector3(w.X, t, w.Z), worlds[i].Inv));
        }
        RaiseObjTransform();
        Render();
        Status = $"Distributed {worlds.Count} pieces evenly along world {(axis == 0 ? "X" : "Y")}.";
    }

    /// <summary>Del with a multi-selection — one undo step removes every
    /// member across every family.</summary>
    private bool DeleteMultiIfAny()
    {
        PruneMulti();
        if (_multiSel.Count < 2) return false;
        PushUndo();
        int n = _multiSel.Count;
        foreach (var m in _multiSel.ToArray())
            switch (m)
            {
                case PlacedObject p: _objects.Remove(p); break;
                case RegionEmitter e: Emitters.Remove(e); break;
                case RegionDecal d: Decals.Remove(d); break;
                case AuthoredLight l: Lights.Remove(l); break;
                case RegionTrigger t: Triggers.Remove(t); break;
                case CommandPlacement c: Commands.Remove(c); break;
            }
        _multiSel.Clear();
        SelectedPlacedObject = null;
        SelectedEmitter = null; SelectedDecal = null; SelectedLight = null;
        SelectedTrigger = null; SelectedCommand = null;
        RebuildPlacedRows();
        RaiseMulti();
        Render();
        Status = $"Deleted {n} pieces (Ctrl+Z restores all of them).";
        return true;
    }

    /// <summary>Clones one piece beside itself with a fresh SCID and registers
    /// it in its family collection (multi-duplicate building block).</summary>
    private object? ClonePiece(object src)
    {
        switch (src)
        {
            case PlacedObject p:
            {
                var c = p.Clone(); // overrides (life/scale/loot) ride along
                c.Scid = _nextScid++;
                c.LocalPos = p.LocalPos + DupOffset;
                _objects.Add(c); return c;
            }
            case RegionEmitter e:
            {
                var c = new RegionEmitter
                {
                    Scid = _nextEffectScid++, Template = e.Template, NodeGuid = e.NodeGuid,
                    LocalPos = e.LocalPos + DupOffset, Smoke = e.Smoke,
                    Count = e.Count, Fade = e.Fade, ParticleSize = e.ParticleSize, Growth = e.Growth,
                };
                Emitters.Add(c); return c;
            }
            case RegionDecal d:
            {
                var c = new RegionDecal
                {
                    Scid = _nextEffectScid++, NodeGuid = d.NodeGuid, OriginLocal = d.OriginLocal + DupOffset,
                    Normal = d.Normal, AxisH = d.AxisH, AxisV = d.AxisV,
                    HorizExtent = d.HorizExtent, VertExtent = d.VertExtent, Texture = d.Texture,
                };
                Decals.Add(c); return c;
            }
            case AuthoredLight l:
            {
                var c = new AuthoredLight
                {
                    Scid = _nextLightScid++, Kind = AuthoredLightKind.Point, NodeGuid = l.NodeGuid,
                    Position = l.Position + DupOffset, Color = l.Color, Intensity = l.Intensity,
                    InnerRadius = l.InnerRadius, OuterRadius = l.OuterRadius,
                    DrawShadow = l.DrawShadow, AffectsActors = l.AffectsActors,
                    AffectsItems = l.AffectsItems, AffectsTerrain = l.AffectsTerrain,
                };
                Lights.Add(c); return c;
            }
            case RegionTrigger t:
            {
                var c = new RegionTrigger { Scid = _nextLogicScid++, Template = t.Template, NodeGuid = t.NodeGuid, LocalPos = t.LocalPos + DupOffset };
                Triggers.Add(c); return c;
            }
            case CommandPlacement cm:
            {
                var c = new CommandPlacement
                {
                    Scid = _nextLogicScid++, Kind = cm.Kind, NodeGuid = cm.NodeGuid, LocalPos = cm.LocalPos + DupOffset,
                    NextScid = cm.NextScid, Target1 = cm.Target1, Target2 = cm.Target2,
                    ClientScid = cm.ClientScid, Duration = cm.Duration, Order = cm.Order,
                };
                Commands.Add(c); return c;
            }
        }
        return null;
    }

    // ═══ ED-2 — navigation ═══════════════════════════════════════════

    /// <summary>Orthographic projection toggle — render AND picking share the
    /// same projection, so clicks stay accurate in both modes.</summary>
    private bool _ortho;
    public string OrthoLabel => _ortho ? "Perspective" : "Orthographic";
    public RelayCommand OrthoCommand { get; }

    /// <summary>Fly-speed multiplier for WASD movement (toolbar combo).</summary>
    public double[] CamSpeeds { get; } = { 0.25, 0.5, 1.0, 2.0, 4.0 };
    private double _camSpeed = 1.0;
    public double CamSpeed { get => _camSpeed; set => SetProperty(ref _camSpeed, value); }

    /// <summary>WASD/QE fly: moves the orbit target along the camera basis.
    /// Shift = 3×, Ctrl = 0.3×. Called from the view's fly timer while the
    /// cursor is over the viewport.</summary>
    public void Fly(float forward, float strafe, float vertical, bool fast, bool slow)
    {
        var toEye = new Vector3(MathF.Cos(_pitch) * MathF.Cos(_yaw), MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch));
        var fwd = -toEye; // view direction
        var right = Vector3.Normalize(Vector3.Cross(new Vector3(0, 0, 1), toEye));
        float step = (float)_camSpeed * MathF.Max(_dist, _radius * 0.25f) * 0.035f
                   * (fast ? 3f : slow ? 0.3f : 1f);
        _pan += fwd * forward * step + right * (-strafe) * step + new Vector3(0, 0, 1) * vertical * step;
        Render();
    }

    /// <summary>Camera bookmarks: Ctrl+1..4 stores the current view, 1..4
    /// recalls it — pan, angles, and zoom all round-trip.</summary>
    private readonly (Vector3 Pan, float Yaw, float Pitch, float Dist)?[] _bookmarks = new (Vector3, float, float, float)?[4];
    public RelayCommand StoreBookmarkCommand { get; }
    public RelayCommand RecallBookmarkCommand { get; }

    private void StoreBookmark(int slot)
    {
        _bookmarks[slot] = (_pan, _yaw, _pitch, _dist);
        Status = $"Camera bookmark {slot + 1} saved (press {slot + 1} to return here).";
    }

    private void RecallBookmark(int slot)
    {
        if (_bookmarks[slot] is not { } b) { Status = $"No bookmark {slot + 1} yet — Ctrl+{slot + 1} saves the current view."; return; }
        (_pan, _yaw, _pitch, _dist) = (b.Pan, b.Yaw, b.Pitch, b.Dist);
        Render();
        Status = $"Camera bookmark {slot + 1}.";
    }

    /// <summary>ED-2 — F: frame the selection. Pans the orbit target onto the
    /// selected piece and pulls the camera in, Unity/Unreal-style.</summary>
    public RelayCommand FocusSelectedCommand { get; }

    private void FocusSelected()
    {
        Vector3? target = null;
        if (_selectedPlacedObject is not null
            && _objects.Find(x => x.Scid == _selectedPlacedObject.Scid) is { } o
            && _nodeWorld.TryGetValue(o.NodeGuid, out var ow))
            target = Vector3.Transform(o.LocalPos, ow);
        else if (_selectedEmitter is { } em && _nodeWorld.TryGetValue(em.NodeGuid, out var ew))
            target = Vector3.Transform(em.LocalPos, ew);
        else if (_selectedDecal is { } dc && _nodeWorld.TryGetValue(dc.NodeGuid, out var dw))
            target = Vector3.Transform(dc.OriginLocal, dw);
        else if (_selectedLight is { Kind: AuthoredLightKind.Point } pl && _nodeWorld.TryGetValue(pl.NodeGuid, out var lw))
            target = Vector3.Transform(pl.Position, lw);
        else if (_selectedTrigger is { } tg && _nodeWorld.TryGetValue(tg.NodeGuid, out var tw))
            target = Vector3.Transform(tg.LocalPos, tw);
        else if (_selectedCommand is { } cm && _nodeWorld.TryGetValue(cm.NodeGuid, out var cw))
            target = Vector3.Transform(cm.LocalPos, cw);
        else if (_selectedNode is not null && _nodeWorld.TryGetValue(_selectedNode.Guid, out var nw))
            target = nw.Translation;
        if (target is null) { Status = "Select something to focus (F)."; return; }
        _pan = target.Value - _center;
        _dist = MathF.Max(MathF.Min(_dist, _radius * 0.5f), 6f);
        Render();
    }

    /// <summary>ED-1a — Ctrl+D: duplicate whatever is selected (object,
    /// emitter, decal, point light, trigger, command) with a fresh SCID and
    /// a small offset so the copy is immediately visible and grabbable.</summary>
    public RelayCommand DuplicateCommand { get; }

    private static readonly Vector3 DupOffset = new(0.6f, 0.6f, 0f);

    private void DuplicateSelected()
    {
        // ED-1b — with a multi-selection, Ctrl+D clones every member in one
        // undo step; the copies become the new selection, ready to drag away.
        PruneMulti();
        if (_multiSel.Count > 1)
        {
            PushUndo();
            var copies = new List<object>();
            foreach (var m in _multiSel) if (ClonePiece(m) is { } c) copies.Add(c);
            _multiSel.Clear();
            _multiSel.AddRange(copies);
            RebuildPlacedRows();
            RaiseMulti();
            Render();
            Status = $"Duplicated {copies.Count} pieces — the copies are now the selection; drag any one to move them all.";
            return;
        }
        if (_selectedPlacedObject is not null)
        {
            var src = _objects.Find(x => x.Scid == _selectedPlacedObject.Scid);
            if (src is null) return;
            PushUndo();
            var copy = src.Clone(); // overrides (life/scale/loot) ride along
            copy.Scid = _nextScid++;
            copy.LocalPos = src.LocalPos + DupOffset;
            _objects.Add(copy);
            RebuildPlacedRows();
            foreach (var r in PlacedObjects) if (r.Scid == copy.Scid) { SelectedPlacedObject = r; break; }
            Render();
            Status = $"Duplicated {src.Template}.";
            return;
        }
        if (_selectedEmitter is { } em)
        {
            PushUndo();
            var copy = new RegionEmitter
            {
                Scid = _nextEffectScid++, Template = em.Template, NodeGuid = em.NodeGuid,
                LocalPos = em.LocalPos + DupOffset, Smoke = em.Smoke,
                Count = em.Count, Fade = em.Fade, ParticleSize = em.ParticleSize, Growth = em.Growth,
            };
            Emitters.Add(copy);
            SelectMarker(copy);
            Status = "Duplicated emitter.";
            return;
        }
        if (_selectedDecal is { } dc)
        {
            PushUndo();
            var copy = new RegionDecal
            {
                Scid = _nextEffectScid++, NodeGuid = dc.NodeGuid, OriginLocal = dc.OriginLocal + DupOffset,
                Normal = dc.Normal, AxisH = dc.AxisH, AxisV = dc.AxisV,
                HorizExtent = dc.HorizExtent, VertExtent = dc.VertExtent, Texture = dc.Texture,
            };
            Decals.Add(copy);
            SelectMarker(copy);
            Status = "Duplicated decal.";
            return;
        }
        if (_selectedLight is { Kind: AuthoredLightKind.Point } pl)
        {
            PushUndo();
            var copy = new AuthoredLight
            {
                Scid = _nextLightScid++, Kind = AuthoredLightKind.Point, NodeGuid = pl.NodeGuid,
                Position = pl.Position + DupOffset, Color = pl.Color, Intensity = pl.Intensity,
                InnerRadius = pl.InnerRadius, OuterRadius = pl.OuterRadius,
                DrawShadow = pl.DrawShadow, AffectsActors = pl.AffectsActors,
                AffectsItems = pl.AffectsItems, AffectsTerrain = pl.AffectsTerrain,
            };
            Lights.Add(copy);
            SelectMarker(copy);
            Status = "Duplicated point light.";
            return;
        }
        if (_selectedTrigger is { } tg)
        {
            PushUndo();
            var copy = new RegionTrigger { Scid = _nextLogicScid++, Template = tg.Template, NodeGuid = tg.NodeGuid, LocalPos = tg.LocalPos + DupOffset };
            Triggers.Add(copy);
            SelectMarker(copy);
            Status = "Duplicated trigger (rows start empty — copy conditions as needed).";
            return;
        }
        if (_selectedCommand is { } cm)
        {
            PushUndo();
            var copy = new CommandPlacement
            {
                Scid = _nextLogicScid++, Kind = cm.Kind, NodeGuid = cm.NodeGuid, LocalPos = cm.LocalPos + DupOffset,
                NextScid = cm.NextScid, Target1 = cm.Target1, Target2 = cm.Target2,
                ClientScid = cm.ClientScid, Duration = cm.Duration, Order = cm.Order,
            };
            Commands.Add(copy);
            SelectMarker(copy);
            Status = "Duplicated command.";
            return;
        }
        Status = "Select an object, emitter, decal, point light, trigger, or command to duplicate (Ctrl+D).";
    }

    // GAME-4 — map-local quests (journal entries of the custom game).
    public ObservableCollection<MapQuest> MapQuests { get; } = new();
    private MapQuest? _selectedQuest;
    public MapQuest? SelectedQuest
    {
        get => _selectedQuest;
        set { if (SetProperty(ref _selectedQuest, value)) { OnPropertyChanged(nameof(HasSelectedQuest)); RaiseQuestProps(); RaiseCommands(); } }
    }
    public bool HasSelectedQuest => _selectedQuest is not null;
    public string QuestKey
    {
        get => _selectedQuest?.Key ?? "";
        set { if (_selectedQuest is not null) { _selectedQuest.Key = TemplateAuthor.SanitizeName(value); RefreshQuestItem(); OnPropertyChanged(nameof(QuestKeys)); } }
    }
    public string QuestScreenName
    {
        get => _selectedQuest?.ScreenName ?? "";
        set { if (_selectedQuest is not null) { _selectedQuest.ScreenName = value ?? ""; RefreshQuestItem(); } }
    }
    public string QuestDescription
    {
        get => _selectedQuest?.Description ?? "";
        set { if (_selectedQuest is not null) _selectedQuest.Description = value ?? ""; }
    }
    public string QuestOrder
    {
        get => _selectedQuest?.Order.ToString(CultureInfo.InvariantCulture) ?? "";
        set { if (_selectedQuest is not null && int.TryParse(value, out var o)) { _selectedQuest.Order = o; RefreshQuestItem(); } }
    }
    private string _chapterName = "Chapter 1";
    public string ChapterName { get => _chapterName; set => SetProperty(ref _chapterName, value); }
    private string _chapterIntro = "";
    public string ChapterIntro { get => _chapterIntro; set => SetProperty(ref _chapterIntro, value); }

    private void RaiseQuestProps()
    {
        OnPropertyChanged(nameof(QuestKey));
        OnPropertyChanged(nameof(QuestScreenName));
        OnPropertyChanged(nameof(QuestDescription));
        OnPropertyChanged(nameof(QuestOrder));
    }

    private void RefreshQuestItem()
    {
        if (_selectedQuest is null) return;
        int i = MapQuests.IndexOf(_selectedQuest);
        if (i >= 0) MapQuests[i] = _selectedQuest; // replace-in-place → Label/Detail recompute
    }

    /// <summary>The quests gas for packaging, or null when the map has none.</summary>
    private string? ComposeQuests() =>
        MapQuests.Count == 0 ? null : QuestAuthor.Compose(_chapterName, _chapterIntro, MapQuests);

    // GAME-3 — custom template authoring (items / monsters / NPCs).
    public string[] TemplateKinds { get; } = { "Weapon", "Armor / shield", "Monster", "NPC" };
    private string _tplKind = "Weapon";
    public string TplKind
    {
        get => _tplKind;
        set
        {
            if (!SetProperty(ref _tplKind, value)) return;
            OnPropertyChanged(nameof(TplIsWeapon));
            OnPropertyChanged(nameof(TplIsArmor));
            OnPropertyChanged(nameof(TplIsMonster));
        }
    }
    public bool TplIsWeapon => _tplKind == "Weapon";
    public bool TplIsArmor => _tplKind.StartsWith("Armor", StringComparison.Ordinal);
    public bool TplIsMonster => _tplKind == "Monster";
    private string _tplName = "";
    public string TplName { get => _tplName; set => SetProperty(ref _tplName, value); }
    private string _tplBase = "";
    public string TplBase { get => _tplBase; set => SetProperty(ref _tplBase, value); }
    private string _tplScreen = "";
    public string TplScreenName { get => _tplScreen; set => SetProperty(ref _tplScreen, value); }
    private string _tplDmgMin = "4", _tplDmgMax = "9", _tplDefense = "10", _tplLife = "50";
    public string TplDamageMin { get => _tplDmgMin; set => SetProperty(ref _tplDmgMin, value); }
    public string TplDamageMax { get => _tplDmgMax; set => SetProperty(ref _tplDmgMax, value); }
    public string TplDefense { get => _tplDefense; set => SetProperty(ref _tplDefense, value); }
    public string TplLife { get => _tplLife; set => SetProperty(ref _tplLife, value); }

    private static int ParseI(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>GAME-3 — compose → syntax-check → write a custom template, and
    /// register monsters/NPCs into the placeable palette when the base's model
    /// resolves so they can be placed immediately.</summary>
    private void CreateTemplate()
    {
        if (_assetsFolder is null) { Status = "Set the assets folder first (Custom tab)."; return; }
        var name = TemplateAuthor.SanitizeName(_tplName);
        var baseName = _tplBase.Trim();
        if (string.IsNullOrEmpty(baseName)) { Status = "Pick a base template to specialize (e.g. farmgirl, sword_bastard)."; return; }
        var kind = TplIsWeapon ? TemplateAuthor.Kind.Weapon
                 : TplIsArmor ? TemplateAuthor.Kind.Armor
                 : TplIsMonster ? TemplateAuthor.Kind.Monster
                 : TemplateAuthor.Kind.Npc;
        var spec = new TemplateAuthor.Spec
        {
            Kind = kind, Name = name, Base = baseName, ScreenName = _tplScreen,
            DamageMin = ParseI(_tplDmgMin, 4), DamageMax = ParseI(_tplDmgMax, 9),
            Defense = ParseI(_tplDefense, 10), Life = ParseI(_tplLife, 50),
        };
        string path;
        try
        {
            GasDocument.Parse(TemplateAuthor.Compose(spec)); // engine-grammar acceptance BEFORE writing
            path = TemplateAuthor.Write(_assetsFolder, spec);
        }
        catch (Exception ex) { Status = "Template failed: " + ex.Message; return; }

        bool knownBase = _allProps.Exists(p => p.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                      || _allActors.Exists(a => a.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));
        if (kind is TemplateAuthor.Kind.Monster or TemplateAuthor.Kind.Npc)
        {
            var b = _allActors.Find(a => a.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));
            if (b is not null && !_allActors.Exists(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                _allActors.Add(new PropTemplate(name, b.Model));
                _templateModel[name] = b.Model;
                RefreshPropPalette();
            }
        }
        Status = $"Created template '{name}' → {path} — bundles into the map and loads in-engine."
               + (knownBase ? "" : $" Note: base '{baseName}' isn't in the placeable catalog; abstract bases are fine if the name is exact.");
    }

    /// <summary>SC-UX1 — the Scene Outliner's fixed groups. Each group wraps
    /// one of the live ObservableCollections, so the tree stays current with
    /// zero extra bookkeeping; the colour dot matches the viewport marker.</summary>
    public OutlineGroup[] OutlineGroups { get; }

    public WorldBuilderViewModel(IReadOnlyList<string> tankPaths)
    {
        _tankPaths = tankPaths;
        TexturedCommand = new RelayCommand(_ => Textured = !Textured);
        SetAssetsFolderCommand = new RelayCommand(_ => SetAssetsFolder());
        OpenAssetsFolderCommand = new RelayCommand(_ => OpenAssetsFolder(), _ => _assetsFolder is not null);
        ImportObjMeshCommand = new RelayCommand(_ => ImportObjMesh());
        ImportTextureCommand = new RelayCommand(_ => ImportTexture(), _ => _assetsFolder is not null);
        CreateTemplateCommand = new RelayCommand(_ => CreateTemplate(), _ => _assetsFolder is not null);
        ImportAudioCommand = new RelayCommand(_ => ImportAudio(), _ => _assetsFolder is not null);
        DuplicateCommand = new RelayCommand(_ => DuplicateSelected());
        FocusSelectedCommand = new RelayCommand(_ => FocusSelected());
        CopyCommand = new RelayCommand(_ => CopySelected());
        PasteCommand = new RelayCommand(_ => PasteClipboard(), _ => _clipboard is not null);
        ToggleFavoriteMeshCommand = new RelayCommand(_ => ToggleFavorite(FavoriteMeshes, _selectedMesh?.Name, "node mesh"));
        ToggleFavoritePropCommand = new RelayCommand(_ => ToggleFavorite(FavoriteProps, _selectedProp?.Name, "template"));
        OrthoCommand = new RelayCommand(_ => { _ortho = !_ortho; OnPropertyChanged(nameof(OrthoLabel)); Render(); });
        StoreBookmarkCommand = new RelayCommand(p => { if (int.TryParse(p as string, out var i) && i is >= 0 and < 4) StoreBookmark(i); });
        RecallBookmarkCommand = new RelayCommand(p => { if (int.TryParse(p as string, out var i) && i is >= 0 and < 4) RecallBookmark(i); });
        ReplaceNodeMeshCommand = new RelayCommand(_ => ReplaceNodeMesh(),
            _ => _selectedNode is not null && _selectedMesh is not null);
        AlignXCommand = new RelayCommand(_ => AlignSelected(0), _ => _multiSel.Count >= 2);
        AlignYCommand = new RelayCommand(_ => AlignSelected(1), _ => _multiSel.Count >= 2);
        DistributeXCommand = new RelayCommand(_ => DistributeSelected(0), _ => _multiSel.Count >= 3);
        DistributeYCommand = new RelayCommand(_ => DistributeSelected(1), _ => _multiSel.Count >= 3);
        LoadWorldBuilderPrefs();
        AddQuestCommand = new RelayCommand(_ =>
        {
            PushUndo();
            var q = new MapQuest { Key = $"quest_custom_{MapQuests.Count + 1:00}" };
            MapQuests.Add(q);
            SelectedQuest = q;
            OnPropertyChanged(nameof(QuestKeys));
        });
        DeleteQuestCommand = new RelayCommand(_ =>
        {
            if (_selectedQuest is null) return;
            PushUndo();
            MapQuests.Remove(_selectedQuest);
            SelectedQuest = null;
            OnPropertyChanged(nameof(QuestKeys));
        }, _ => _selectedQuest is not null);
        GenerateTerrainTileCommand = new RelayCommand(_ => GenerateTerrainTile(), _ => _assetsFolder is not null);
        PlaceObjectCommand = new RelayCommand(_ => PlaceObject(), _ => IsReady && _selectedProp is not null && _selectedNode is not null);
        DeleteObjectCommand = new RelayCommand(_ => DeleteObject(), _ => _selectedPlacedObject is not null);
        TogglePlacingCommand = new RelayCommand(_ => PlacingActors = !PlacingActors);
        AddDirectionalLightCommand = new RelayCommand(_ => AddLight(AuthoredLightKind.Directional), _ => IsReady);
        AddPointLightCommand = new RelayCommand(_ => AddLight(AuthoredLightKind.Point), _ => IsReady && _selectedNode is not null);
        DeleteLightCommand = new RelayCommand(_ => DeleteLight(), _ => _selectedLight is not null);
        ToggleMoodInteriorCommand = new RelayCommand(_ => MoodInterior = !MoodInterior);
        AddFireEmitterCommand = new RelayCommand(_ => AddEmitter(false), _ => IsReady && _selectedNode is not null);
        AddSmokeEmitterCommand = new RelayCommand(_ => AddEmitter(true), _ => IsReady && _selectedNode is not null);
        DeleteEmitterCommand = new RelayCommand(_ => DeleteEmitter(), _ => _selectedEmitter is not null);
        AddDecalCommand = new RelayCommand(_ => AddDecal(), _ => IsReady && _selectedNode is not null);
        DeleteDecalCommand = new RelayCommand(_ => DeleteDecal(), _ => _selectedDecal is not null);
        AddSoundCommand = new RelayCommand(_ => AddSound(), _ => IsReady && _selectedNode is not null && !string.IsNullOrWhiteSpace(_soundTemplate));
        AddTriggerCommand = new RelayCommand(_ => AddTrigger(), _ => IsReady && _selectedNode is not null);
        DeleteTriggerCommand = new RelayCommand(_ => { if (_selectedTrigger is not null) { PushUndo(); Triggers.Remove(_selectedTrigger); SelectedTrigger = null; Render(); } }, _ => _selectedTrigger is not null);
        AddTriggerRowCommand = new RelayCommand(_ => { if (_selectedTrigger is not null) { PushUndo(); var r = NewTriggerRow(); _selectedTrigger.Rows.Add(r); SelectedTriggerRow = r; } }, _ => _selectedTrigger is not null);
        DeleteTriggerRowCommand = new RelayCommand(_ => { if (_selectedTrigger is not null && _selectedTriggerRow is not null) { PushUndo(); _selectedTrigger.Rows.Remove(_selectedTriggerRow); SelectedTriggerRow = _selectedTrigger.Rows.Count > 0 ? _selectedTrigger.Rows[0] : null; } }, _ => _selectedTriggerRow is not null);
        AddConditionCommand = new RelayCommand(_ => { if (_selectedTriggerRow is not null) { PushUndo(); var c = new TriggerCall { Verb = RegionTrigger.Conditions[0] }; _selectedTriggerRow.Conditions.Add(c); SelectedCondition = c; } }, _ => _selectedTriggerRow is not null);
        DeleteConditionCommand = new RelayCommand(_ => { if (_selectedTriggerRow is not null && _selectedCondition is not null) { PushUndo(); _selectedTriggerRow.Conditions.Remove(_selectedCondition); SelectedCondition = _selectedTriggerRow.Conditions.Count > 0 ? _selectedTriggerRow.Conditions[0] : null; } }, _ => _selectedCondition is not null);
        AddActionCommand = new RelayCommand(_ => { if (_selectedTriggerRow is not null) { PushUndo(); var a = new TriggerCall { Verb = RegionTrigger.Actions[0] }; _selectedTriggerRow.Actions.Add(a); SelectedAction = a; } }, _ => _selectedTriggerRow is not null);
        DeleteActionCommand = new RelayCommand(_ => { if (_selectedTriggerRow is not null && _selectedAction is not null) { PushUndo(); _selectedTriggerRow.Actions.Remove(_selectedAction); SelectedAction = _selectedTriggerRow.Actions.Count > 0 ? _selectedTriggerRow.Actions[0] : null; } }, _ => _selectedAction is not null);
        AddCommandCommand = new RelayCommand(_ => AddCommand(), _ => IsReady && _selectedNode is not null);
        DeleteCommandCommand = new RelayCommand(_ => { if (_selectedCommand is not null) { PushUndo(); Commands.Remove(_selectedCommand); SelectedCommand = null; Render(); } }, _ => _selectedCommand is not null);
        AddConversationCommand = new RelayCommand(_ => AddConversation(), _ => IsReady);
        DeleteConversationCommand = new RelayCommand(_ => { if (_selectedConversation is not null) { PushUndo(); Conversations.Remove(_selectedConversation); SelectedConversation = null; } }, _ => _selectedConversation is not null);
        AddDialogueCommand = new RelayCommand(_ => AddDialogue(), _ => _selectedConversation is not null);
        DeleteDialogueCommand = new RelayCommand(_ => { if (_selectedConversation is not null && _selectedDialogue is not null) { PushUndo(); _selectedConversation.Nodes.Remove(_selectedDialogue); SelectedDialogue = _selectedConversation.Nodes.Count > 0 ? _selectedConversation.Nodes[0] : null; } }, _ => _selectedDialogue is not null);
        BindConversationCommand = new RelayCommand(_ => BindConversation(), _ => _selectedConversation is not null && _selectedPlacedObject is not null);
        ImportSiblingCommand = new RelayCommand(_ => ImportSibling());
        RemoveSiblingCommand = new RelayCommand(_ => RemoveSibling(), _ => _selectedSibling is not null);
        CreateStitchCommand = new RelayCommand(_ => CreateStitch(), _ => _selectedPrimaryDoor is not null && _selectedSibling is not null && _selectedSiblingDoor is not null);
        DeleteStitchCommand = new RelayCommand(_ => DeleteStitch(), _ => _selectedStitch is not null);
        AddNavFlagCommand = new RelayCommand(_ => AddNavFlag(), _ => IsReady && _selectedNode is not null);
        DeleteNavFlagCommand = new RelayCommand(_ => { if (_selectedFlag is not null) { PushUndo(); LogicalFlags.Remove(_selectedFlag); SelectedFlag = null; } }, _ => _selectedFlag is not null);
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
        DeleteSelectedAnyCommand = new RelayCommand(_ => DeleteSelectedAny());

        OutlineGroups = new[]
        {
            new OutlineGroup("Nodes",         Nodes,         "#8A8F98"),
            new OutlineGroup("Objects",       PlacedObjects, "#B0A890", canHide: true),
            new OutlineGroup("Emitters",      Emitters,      "#F07A28", canHide: true),
            new OutlineGroup("Decals",        Decals,        "#D8A24B", canHide: true),
            new OutlineGroup("Lights",        Lights,        "#F2D24C", canHide: true),
            new OutlineGroup("Triggers",      Triggers,      "#33C2A6", canHide: true),
            new OutlineGroup("Commands",      Commands,      "#4C8DF0", canHide: true),
            new OutlineGroup("Conversations", Conversations, "#C77DD8"),
            new OutlineGroup("Quests",        MapQuests,     "#E0709A"),
            new OutlineGroup("Nav flags",     LogicalFlags,  "#7FBF7F"),
        };
        foreach (var g in OutlineGroups) g.VisibilityChanged = Render; // eyes re-render

        // GAME-1 — the live-FX clock: ~15fps re-render while emitters exist
        // and FX are live. Deterministic particle time, so toggling live/
        // static freezes and resumes rather than restarting.
        LiveFxCommand = new RelayCommand(_ =>
        {
            _liveFx = !_liveFx;
            OnPropertyChanged(nameof(LiveFxLabel));
            Render();
        });
        _fxTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(66),
        };
        _fxTimer.Tick += (_, _) =>
        {
            if (!_liveFx || Emitters.Count == 0 || _isLoading) return;
            _fxTime += 0.066f;
            Render();
        };
        _fxTimer.Start();

        // SC-UX5 — crash-safe autosave: every 3 minutes, the WHOLE region
        // (terrain, objects, lights, mood, effects, logic, conversations,
        // stitches, nav flags) packs through the same MapPackager path the
        // Play button uses, into %APPDATA%\SiegeSmith\autosave. Failures
        // never interrupt editing.
        _autosaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(3),
        };
        _autosaveTimer.Tick += (_, _) => Autosave();
        _autosaveTimer.Start();

        LoadCatalogAsync(tankPaths);
    }

    private readonly System.Windows.Threading.DispatcherTimer _autosaveTimer;
    private string? _lastAutosaveFingerprint;

    // GAME-1 — live effects preview state.
    private readonly System.Windows.Threading.DispatcherTimer _fxTimer;
    private float _fxTime;
    private bool _liveFx = true;
    public string LiveFxLabel => _liveFx ? "FX: live" : "FX: static";
    public RelayCommand LiveFxCommand { get; }

    private void Autosave()
    {
        if (IsEmpty || _catalog is null) return;
        try
        {
            // Cheap change fingerprint: the undo snapshot (nodes + objects)
            // plus the per-family counts. Property-only edits inside a family
            // may not change it — the next fingerprinted edit catches up.
            string fp = Snapshot() + "|" + Emitters.Count + "," + Decals.Count + "," + Lights.Count + ","
                + Triggers.Count + "," + Commands.Count + "," + Conversations.Count + ","
                + LogicalFlags.Count + "," + PrimaryStitches.Count;
            if (fp == _lastAutosaveFingerprint) return;

            var nodesGas = NodesGasWriter.Write(_region);
            var outDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SiegeSmith", "autosave");
            MapPackager.PackStartableMap(nodesGas,
                string.IsNullOrWhiteSpace(MapName) ? "autosave" : MapName,
                string.IsNullOrWhiteSpace(RegionName) ? "region" : RegionName,
                outDir, BuildStartInfo(),
                assetsRoot: _assetsFolder, placements: _objects,
                lights: new List<AuthoredLight>(Lights), mood: BuildMood(),
                emitters: new List<RegionEmitter>(Emitters), decals: new List<RegionDecal>(Decals),
                triggers: new List<RegionTrigger>(Triggers), commands: new List<CommandPlacement>(Commands),
                conversations: new List<Conversation>(Conversations),
                sourceGuid: PrimaryStitches.Count > 0 ? PrimarySourceGuid() : 0,
                stitches: new List<RegionStitch>(PrimaryStitches), siblings: new List<StitchRegionRef>(Siblings),
                logicalFlags: new List<LogicalFlag>(LogicalFlags),
                questsGas: ComposeQuests());
            _lastAutosaveFingerprint = fp;
            Status = "Autosaved to " + outDir + ".";
        }
        catch
        {
            // Autosave must never interrupt or alarm — the next tick retries.
        }
    }

    /// <summary>Outliner click → the same selection the side panels and the
    /// viewport use, so all three stay in lock-step.</summary>
    public void SelectFromOutliner(object item)
    {
        switch (item)
        {
            case NodeRow n: SelectedNode = n; break;
            case PlacedObjectRow p: SelectedPlacedObject = p; SelectMarker(p); break;
            case RegionEmitter or RegionTrigger or CommandPlacement or RegionDecal or AuthoredLight:
                SelectMarker(item); break;
            case Conversation c: SelectedConversation = c; break;
            case LogicalFlag f: SelectedFlag = f; break;
            case MapQuest mq: SelectedQuest = mq; break;
        }
    }

    private void DeleteSelectedAny()
    {
        if (DeleteMultiIfAny()) return; // ED-1b — Del removes the whole set in one undo step
        if (_selectedEmitter is not null) { DeleteEmitterCommand.Execute(null); return; }
        if (_selectedDecal is not null) { DeleteDecalCommand.Execute(null); return; }
        if (_selectedLight is not null) { DeleteLightCommand.Execute(null); return; }
        if (_selectedTrigger is not null) { DeleteTriggerCommand.Execute(null); return; }
        if (_selectedCommand is not null) { DeleteCommandCommand.Execute(null); return; }
        if (_selectedConversation is not null) { DeleteConversationCommand.Execute(null); return; }
        if (_selectedQuest is not null) { DeleteQuestCommand.Execute(null); return; }
        if (_selectedFlag is not null) { DeleteNavFlagCommand.Execute(null); return; }
        if (_selectedPlacedObject is not null) { DeleteObjectCommand.Execute(null); return; }
        if (_selectedNode is not null && DeleteNodeCommand.CanExecute(null)) DeleteNodeCommand.Execute(null);
    }

    private async void LoadCatalogAsync(IReadOnlyList<string> tankPaths)
    {
        try
        {
            var (cat, tex, props, asp) = await Task.Run(() =>
            {
                var c = SnoCatalog.Build(tankPaths);
                var t = new TextureResolver();
                foreach (var p in tankPaths) t.AddTankPath(p);
                var pr = TemplateCatalog.Build(tankPaths);
                var a = AspCatalog.Build(tankPaths);
                return (c, t, pr, a);
            });
            _catalog = cat;
            _textures = tex;
            _props = props;
            _asp = asp;
            _allMeshes.AddRange(cat.Meshes);
            _allProps.AddRange(props.Props);
            _allActors.AddRange(props.Actors);
            foreach (var p in props.Props) _templateModel[p.Name] = p.Model;
            foreach (var a in props.Actors) _templateModel[a.Name] = a.Model; // actors preview via the same mesh path
            RefreshPalette();
            RebuildPropTags(); // ED-7 — tag combo derives from the loaded catalog
            RefreshPropPalette();
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
        UpdateMeshThumb(); // ED-7 — preview still of the selected mesh
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
        RefreshStitchState();
        RaiseCommands();
    }

    // ── build operations ────────────────────────────────────────
    private void PlaceAnchor()
    {
        if (_selectedMesh is null) return;
        PushUndo();
        PushRecent(RecentMeshes, _selectedMesh.Name); // ED-1a
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
        PushRecent(RecentMeshes, _selectedMesh.Name); // ED-1a

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

    // ═══ ED-5 — terrain painting ══════════════════════════════════════
    // Paint mode turns terrain layout into a brush: each click (or drag
    // step) chains the palette's selected mesh onto the free door nearest
    // the cursor — no manual door pairing per node. The nearest-door rule
    // means dragging along the ground naturally extends the path.

    private bool _paintMode;
    public bool PaintMode
    {
        get => _paintMode;
        set
        {
            if (!SetProperty(ref _paintMode, value)) return;
            if (value && _scatterMode) ScatterMode = false; // one brush at a time
            Status = value
                ? "Paint mode — pick a node mesh, then click/drag on terrain: each step chains it onto the nearest free door. Every step is one undo."
                : "Paint mode off.";
        }
    }

    /// <summary>One paint step. Returns true when handled (even on a miss with
    /// a helpful status), so the caller doesn't fall through to grab/orbit.</summary>
    public bool TryPaint(double sx, double sy)
    {
        if (!_paintMode) return false;
        return PaintStepAt(sx, sy);
    }

    /// <summary>The paint step itself — also invoked directly by ED-7 mesh
    /// drag-drop, which paints one node at the drop point without the mode.</summary>
    private bool PaintStepAt(double sx, double sy)
    {
        if (_selectedMesh is null) { Status = "Paint: pick a node mesh in the Nodes palette first."; return true; }
        if (_catalog is null || _pickVerts.Length < 3) return true;

        uint guid = SoftwareRenderer.PickTriangle(_pickVerts, _pickGuid, _vw, _vh,
            _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy, _ortho);
        if (guid == 0) return true; // clicked past the terrain — ignore quietly (drag steps do this)
        if (!SoftwareRenderer.PickPoint(_pickVerts, _pickGuid, guid, _vw, _vh,
                _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy, out var hit, _ortho)) return true;

        // Nearest FREE door anywhere in the region — painting near a region
        // edge grabs the edge door even when the click lands mid-floor.
        BuilderNode? bestNode = null;
        int bestDoor = -1, bestHot = 0;
        float bestD = float.MaxValue;
        foreach (var n in _region.Nodes)
        {
            if (!_nodeWorld.TryGetValue(n.Guid, out var nw)) continue;
            var sno = _catalog.Resolve(n.MeshGuid);
            if (sno is null) continue;
            foreach (var d in sno.Doors)
            {
                if (n.UsesDoor((int)d.Id)) continue;
                float dist = Vector3.DistanceSquared(Vector3.Transform(d.Transform.Translation, nw), hit);
                if (dist < bestD) { bestD = dist; bestNode = n; bestDoor = (int)d.Id; bestHot = d.HotSpots.Length; }
            }
        }
        if (bestNode is null) { Status = "Paint: no free doors left — every door in the region is already connected."; return true; }

        var target = _catalog.Resolve(_selectedMesh.MeshGuid);
        if (target is null || target.Doors.Length == 0)
        {
            Status = $"Paint: {_selectedMesh.Name} has no doors, so it can't chain. Pick a tile mesh.";
            return true;
        }
        // Prefer a target door with the same hotspot width as the source door —
        // same-size doors join seamlessly (how retail tile sets are authored).
        int tgtDoor = (int)target.Doors[0].Id;
        foreach (var d in target.Doors)
            if (d.HotSpots.Length == bestHot) { tgtDoor = (int)d.Id; break; }

        PushUndo();
        PushRecent(RecentMeshes, _selectedMesh.Name);
        var guidNew = NextGuid();
        var newNode = new BuilderNode
        {
            Guid = guidNew, MeshGuid = _selectedMesh.MeshGuid,
            TexsetAbbr = bestNode.TexsetAbbr, // visual continuity with the tile it grows from
        };
        bestNode.Doors.Add(new BuilderDoor(bestDoor, guidNew, tgtDoor));
        newNode.Doors.Add(new BuilderDoor(tgtDoor, bestNode.Guid, bestDoor));
        _region.Nodes.Add(newNode);
        _usedGuids.Add(guidNew);
        AfterModelChanged($"Painted {_selectedMesh.Name} (door {bestDoor} ↔ {tgtDoor}). {_region.Nodes.Count} node(s).");
        return true;
    }

    /// <summary>ED-5 — swap the selected node's mesh for the palette's selected
    /// mesh IN PLACE: guid, door edges, texset, and everything anchored to the
    /// node (objects, emitters, lights…) survive. Requires every connected
    /// door id to exist on the new mesh.</summary>
    public RelayCommand ReplaceNodeMeshCommand { get; }

    private void ReplaceNodeMesh()
    {
        if (_selectedNode is null || _selectedMesh is null || _catalog is null) return;
        var node = _region.Find(_selectedNode.Guid);
        if (node is null) return;
        if (node.MeshGuid == _selectedMesh.MeshGuid) { Status = "Replace: the node already uses that mesh."; return; }
        var sno = _catalog.Resolve(_selectedMesh.MeshGuid);
        if (sno is null) { Status = $"Replace: {_selectedMesh.Name} doesn't resolve."; return; }

        var have = new HashSet<int>();
        foreach (var d in sno.Doors) have.Add((int)d.Id);
        var missing = new List<int>();
        foreach (var bd in node.Doors) if (!have.Contains(bd.LocalId)) missing.Add(bd.LocalId);
        if (missing.Count > 0)
        {
            Status = $"Replace: connected door(s) {string.Join(", ", missing)} don't exist on {_selectedMesh.Name} — disconnect them first or pick a mesh with matching doors.";
            return;
        }

        PushUndo();
        node.MeshGuid = _selectedMesh.MeshGuid;
        AfterModelChanged($"Replaced node mesh with {_selectedMesh.Name} — doors and anchored content kept.");
        SelectNode(node.Guid);
    }

    // ═══ ED-6 — scatter brush ═════════════════════════════════════════
    // Scatters copies of the Objects palette's template around the click
    // point: uniform-disc candidates, projected onto whatever node terrain
    // is under each one, spacing-checked against existing placements and
    // each other, optional random facing. One click = one undo step.

    private bool _scatterMode;
    public bool ScatterMode
    {
        get => _scatterMode;
        set
        {
            if (!SetProperty(ref _scatterMode, value)) return;
            if (value && _paintMode) PaintMode = false; // one brush at a time
            Status = value
                ? "Scatter mode — pick a template in the Objects palette, then click/drag terrain to sprinkle copies. Tune count/radius/spacing beside the toggle."
                : "Scatter mode off.";
        }
    }

    public int[] ScatterCounts { get; } = { 3, 5, 8, 12, 20 };
    private int _scatterCount = 5;
    public int ScatterCount { get => _scatterCount; set => SetProperty(ref _scatterCount, value); }

    public double[] ScatterRadii { get; } = { 1.5, 3, 5, 8, 12 };
    private double _scatterRadius = 5;
    public double ScatterRadius { get => _scatterRadius; set => SetProperty(ref _scatterRadius, value); }

    public double[] ScatterSpacings { get; } = { 0.5, 1, 2, 3 };
    private double _scatterSpacing = 1;
    public double ScatterSpacing { get => _scatterSpacing; set => SetProperty(ref _scatterSpacing, value); }

    private bool _scatterRandomYaw = true;
    public bool ScatterRandomYaw { get => _scatterRandomYaw; set => SetProperty(ref _scatterRandomYaw, value); }

    private readonly Random _scatterRng = new();

    /// <summary>Routes a brush click/drag step to whichever brush is active.</summary>
    public bool TryBrush(double sx, double sy) =>
        _paintMode ? TryPaint(sx, sy) : _scatterMode && TryScatter(sx, sy);

    public bool HasBrush => _paintMode || _scatterMode;

    private bool TryScatter(double sx, double sy)
    {
        if (!_scatterMode) return false;
        if (_selectedProp is null) { Status = "Scatter: pick a template in the Objects palette first."; return true; }
        if (_pickVerts.Length < 3) return true;

        uint anchor = SoftwareRenderer.PickTriangle(_pickVerts, _pickGuid, _vw, _vh,
            _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy, _ortho);
        if (anchor == 0) return true; // clicked past the terrain — ignore quietly

        // World-units-per-pixel at the current view, so the radius combo means
        // world units regardless of zoom or projection.
        float unitsPerPx = _ortho
            ? _dist * 0.828f / MathF.Max(1, _vh)
            : 2f * _dist * MathF.Tan(MathF.PI / 8f) / MathF.Max(1, _vh);
        double radiusPx = _scatterRadius / MathF.Max(unitsPerPx, 1e-6f);
        float sp2 = (float)(_scatterSpacing * _scatterSpacing);

        var existing = new List<Vector3>();
        foreach (var o in _objects)
            if (_nodeWorld.TryGetValue(o.NodeGuid, out var onw))
                existing.Add(Vector3.Transform(o.LocalPos, onw));

        var accepted = new List<Vector3>();
        int placed = 0, attempts = _scatterCount * 5;
        bool pushed = false;
        for (int i = 0; i < attempts && placed < _scatterCount; i++)
        {
            double ang = _scatterRng.NextDouble() * Math.PI * 2;
            double r = Math.Sqrt(_scatterRng.NextDouble()) * radiusPx; // sqrt = uniform over the disc
            double cx = sx + Math.Cos(ang) * r, cy = sy + Math.Sin(ang) * r;
            if (cx < 0 || cy < 0 || cx >= _vw || cy >= _vh) continue;

            uint g = SoftwareRenderer.PickTriangle(_pickVerts, _pickGuid, _vw, _vh,
                _center + _pan, _radius, _yaw, _pitch, _dist, cx, cy, _ortho);
            if (g == 0) continue; // candidate fell off the terrain
            if (!SoftwareRenderer.PickPoint(_pickVerts, _pickGuid, g, _vw, _vh,
                    _center + _pan, _radius, _yaw, _pitch, _dist, cx, cy, out var hit, _ortho)) continue;

            bool tooClose = false;
            foreach (var w in accepted) if (Vector3.DistanceSquared(w, hit) < sp2) { tooClose = true; break; }
            if (!tooClose)
                foreach (var w in existing) if (Vector3.DistanceSquared(w, hit) < sp2) { tooClose = true; break; }
            if (tooClose) continue;

            if (!_nodeWorld.TryGetValue(g, out var nw) || !Matrix4x4.Invert(nw, out var inv)) continue;
            if (!pushed) { PushUndo(); PushRecent(RecentProps, _selectedProp.Name); pushed = true; }
            float yaw = _scatterRandomYaw ? (float)(_scatterRng.NextDouble() * Math.PI * 2) : 0f;
            _objects.Add(new PlacedObject
            {
                Scid = NextScid(),
                Template = _selectedProp.Name,
                NodeGuid = g,
                LocalPos = Vector3.Transform(hit, inv),
                Orientation = Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), yaw),
                File = _placingActors ? "actor.gas" : "non_interactive.gas",
            });
            accepted.Add(hit);
            placed++;
        }

        if (placed == 0)
        {
            Status = "Scatter: no clear spots found — widen the radius, lower the spacing, or aim at open ground.";
            return true;
        }
        RebuildPlacedRows();
        Render();
        Status = $"Scattered {placed}× {_selectedProp.Name} (radius {_scatterRadius:0.#}u, spacing {_scatterSpacing:0.#}u). One undo removes them all.";
        RaiseCommands();
        return true;
    }

    // ═══ ED-7 — palette thumbnails, tags, drag-drop ═══════════════════

    // Selected-item thumbnails: a small software-rendered still of the mesh
    // or template model under each palette, so you see what you're placing
    // before you place it. Rendered off the UI thread; a version counter
    // discards stale results while scrolling.
    private int _meshThumbVer, _propThumbVer;
    private System.Windows.Media.ImageSource? _meshThumb, _propThumb;
    public System.Windows.Media.ImageSource? MeshThumb
    {
        get => _meshThumb;
        private set { if (SetProperty(ref _meshThumb, value)) OnPropertyChanged(nameof(HasMeshThumb)); }
    }
    public bool HasMeshThumb => _meshThumb is not null;
    public System.Windows.Media.ImageSource? PropThumb
    {
        get => _propThumb;
        private set { if (SetProperty(ref _propThumb, value)) OnPropertyChanged(nameof(HasPropThumb)); }
    }
    public bool HasPropThumb => _propThumb is not null;

    private async void UpdateMeshThumb()
    {
        int ver = ++_meshThumbVer;
        var mesh = _selectedMesh;
        var cat = _catalog;
        if (mesh is null || cat is null) { MeshThumb = null; return; }
        BitmapSource? bmp = null;
        try
        {
            bmp = await Task.Run(() =>
            {
                var sno = cat.Resolve(mesh.MeshGuid);
                return sno is null ? null : RenderSnoThumb(sno, 236, 132);
            });
        }
        catch { /* a thumbnail is never worth an error dialog */ }
        if (ver == _meshThumbVer) MeshThumb = bmp;
    }

    private async void UpdatePropThumb()
    {
        int ver = ++_propThumbVer;
        var prop = _selectedProp;
        var asp = _asp;
        string? model = prop is not null && _templateModel.TryGetValue(prop.Name, out var m) ? m : null;
        if (model is null || asp is null) { PropThumb = null; return; }
        BitmapSource? bmp = null;
        try
        {
            bmp = await Task.Run(() =>
            {
                var mesh = asp.Resolve(model);
                return mesh is null || mesh.TriangleIndices.Length < 3 ? null : RenderAspThumb(mesh, 236, 132);
            });
        }
        catch { }
        if (ver == _propThumbVer) PropThumb = bmp;
    }

    private static BitmapSource? RenderThumbCore(Vector3[] v, Vector3[] n, int w, int h)
    {
        if (v.Length < 3) return null;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in v) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        var c = (min + max) * 0.5f;
        float r = MathF.Max((max - min).Length() * 0.5f, 0.001f);
        var bgra = SoftwareRenderer.Render(v, n, w, h, c, r, yaw: -2.35f, pitch: 0.5f, dist: r * 2.2f, wireframe: false);
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze(); // rendered off-thread; frozen bitmaps cross to the UI thread safely
        return bmp;
    }

    private static BitmapSource? RenderSnoThumb(SnoModel sno, int w, int h)
    {
        var stand = Matrix4x4.CreateRotationX(MathF.PI / 2f); // SNO art is Y-up; the thumb camera is Z-up
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        foreach (var s in sno.Surfaces)
        {
            var idx = s.TriangleIndices;
            for (int k = 0; k + 2 < idx.Length; k += 3)
            {
                int g0 = (int)s.StartCorner + idx[k];
                int g1 = (int)s.StartCorner + idx[k + 1];
                int g2 = (int)s.StartCorner + idx[k + 2];
                if ((uint)g0 >= (uint)sno.Corners.Length || (uint)g1 >= (uint)sno.Corners.Length || (uint)g2 >= (uint)sno.Corners.Length)
                    continue;
                Span<int> tri = stackalloc int[3] { g0, g1, g2 };
                foreach (var g in tri)
                {
                    verts.Add(Vector3.Transform(sno.Corners[g].Position, stand));
                    norms.Add(Vector3.TransformNormal(sno.Corners[g].Normal, stand));
                }
            }
        }
        return RenderThumbCore(verts.ToArray(), norms.ToArray(), w, h);
    }

    private static BitmapSource? RenderAspThumb(AspMesh mesh, int w, int h)
    {
        int mtc = mesh.TriangleIndices.Length / 3;
        var verts = new List<Vector3>(mtc * 3);
        var norms = new List<Vector3>(mtc * 3);
        for (int t = 0; t < mtc; t++)
            for (int e = 0; e < 3; e++)
            {
                var corner = mesh.Corners[mesh.TriangleIndices[t * 3 + e]];
                verts.Add(mesh.Positions[corner.VertexIndex]);
                norms.Add(corner.Normal);
            }
        return RenderThumbCore(verts.ToArray(), norms.ToArray(), w, h);
    }

    // Tag filter for the Objects palette — tags are the template-name prefix
    // (tree_, barrel_, krug_, …), derived from the loaded catalog so it always
    // matches the install (mods included). "(all)" = no tag filter.
    public ObservableCollection<string> PropTags { get; } = new();
    private string _propTag = "(all)";
    public string SelectedPropTag
    {
        get => _propTag;
        set { if (SetProperty(ref _propTag, value ?? "(all)")) RefreshPropPalette(); }
    }

    private void RebuildPropTags()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _placingActors ? _allActors : _allProps)
        {
            int us = p.Name.IndexOf('_');
            if (us <= 0) continue;
            var tok = p.Name[..us];
            counts[tok] = counts.TryGetValue(tok, out var c) ? c + 1 : 1;
        }
        var keep = new List<string>();
        foreach (var (tok, c) in counts) if (c >= 3) keep.Add(tok.ToLowerInvariant());
        keep.Sort(StringComparer.OrdinalIgnoreCase);
        PropTags.Clear();
        PropTags.Add("(all)");
        foreach (var t in keep) PropTags.Add(t);
        if (!PropTags.Contains(_propTag)) { _propTag = "(all)"; OnPropertyChanged(nameof(SelectedPropTag)); RefreshPropPalette(); }
    }

    /// <summary>ED-7 — drag-drop from the Objects palette: place the dragged
    /// template exactly where it lands on the terrain.</summary>
    public void DropObjectAt(double sx, double sy, PropTemplate tpl)
    {
        if (_pickVerts.Length < 3) { Status = "Place a terrain node first — objects need ground to stand on."; return; }
        uint g = SoftwareRenderer.PickTriangle(_pickVerts, _pickGuid, _vw, _vh,
            _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy, _ortho);
        if (g == 0) { Status = "Drop it on terrain — that point is off the region."; return; }
        if (!SoftwareRenderer.PickPoint(_pickVerts, _pickGuid, g, _vw, _vh,
                _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy, out var hit, _ortho)) return;
        if (!_nodeWorld.TryGetValue(g, out var nw) || !Matrix4x4.Invert(nw, out var inv)) return;

        PushUndo();
        PushRecent(RecentProps, tpl.Name);
        SelectedProp = tpl;
        var o = new PlacedObject
        {
            Scid = NextScid(), Template = tpl.Name, NodeGuid = g,
            LocalPos = Vector3.Transform(hit, inv),
            File = _placingActors ? "actor.gas" : "non_interactive.gas",
        };
        _objects.Add(o);
        RebuildPlacedRows();
        foreach (var r in PlacedObjects) if (r.Scid == o.Scid) { SelectedPlacedObject = r; break; }
        Render();
        Status = $"Placed {tpl.Name} where you dropped it.";
        RaiseCommands();
    }

    /// <summary>ED-7 — drag-drop from the Nodes palette: an empty region gets
    /// its anchor; otherwise one paint step chains the mesh onto the free door
    /// nearest the drop point.</summary>
    public void DropMeshAt(double sx, double sy, SnoMeshEntry mesh)
    {
        SelectedMesh = mesh;
        if (IsEmpty) { PlaceAnchor(); return; }
        PaintStepAt(sx, sy);
    }

    // ═══ ED-9 — analysis overlays & per-family visibility ═════════════
    // Translucent geometry the software renderer alpha-blends over the
    // scene: trigger activation volumes, light radii, nav-flag markers,
    // and node bounding boxes. None of it is pickable or counts toward
    // the camera's scene bounds.

    private bool _ovTriggers, _ovRadii, _ovNav, _ovBounds;
    public bool OverlayTriggerVolumes { get => _ovTriggers; set { if (SetProperty(ref _ovTriggers, value)) Render(); } }
    public bool OverlayLightRadii { get => _ovRadii; set { if (SetProperty(ref _ovRadii, value)) Render(); } }
    public bool OverlayNavFlags { get => _ovNav; set { if (SetProperty(ref _ovNav, value)) Render(); } }
    public bool OverlayNodeBounds { get => _ovBounds; set { if (SetProperty(ref _ovBounds, value)) Render(); } }

    private bool GroupVisible(string header)
    {
        if (OutlineGroups is null) return true;
        foreach (var g in OutlineGroups) if (g.Header == header) return g.Visible;
        return true;
    }

    /// <summary>The trigger's activation half-extents — from its first
    /// bounding-box condition when authored, else a 1u default so every
    /// trigger still shows a volume under the overlay.</summary>
    private static Vector3 TriggerHalfExtents(RegionTrigger tg)
    {
        foreach (var row in tg.Rows)
            foreach (var c in row.Conditions)
            {
                if (c.Verb is null || c.Verb.IndexOf("bounding_box", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var parts = (c.Args ?? "").Split(',');
                if (parts.Length >= 3
                    && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                    && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    return new Vector3(MathF.Abs(x), MathF.Abs(y), MathF.Abs(z));
            }
        return new Vector3(1f, 1f, 1f);
    }

    /// <summary>ED-9 — translucent overlay box: 12 alpha-blended triangles,
    /// self-lit, not pickable, excluded from scene bounds.</summary>
    private static void AppendBoxBlend(Matrix4x4 world, Vector3 half, int rgb, byte alpha,
        List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
        List<int> triTex, List<int> triColor, List<uint> pickGuid, List<uint> pickScid)
    {
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
            c[i] = Vector3.Transform(new Vector3(
                ((i & 1) == 0 ? -half.X : half.X),
                ((i & 2) == 0 ? -half.Y : half.Y),
                ((i & 4) == 0 ? -half.Z : half.Z)), world);
        int packed = (alpha << 24) | (rgb & 0xFFFFFF);
        void Tri(int i, int j, int k)
        {
            verts.Add(c[i]); verts.Add(c[j]); verts.Add(c[k]);
            normals.Add(Vector3.UnitZ); normals.Add(Vector3.UnitZ); normals.Add(Vector3.UnitZ);
            uvs.Add(default); uvs.Add(default); uvs.Add(default);
            triTex.Add(-1); triColor.Add(packed);
            pickGuid.Add(0u); pickScid.Add(0u);
        }
        void Quad(int a, int b, int cc, int d) { Tri(a, b, cc); Tri(a, cc, d); }
        Quad(0, 1, 3, 2); Quad(4, 6, 7, 5);
        Quad(0, 4, 5, 1); Quad(2, 3, 7, 6);
        Quad(0, 2, 6, 4); Quad(1, 5, 7, 3);
    }

    /// <summary>ED-9 — translucent horizontal disc (light radii).</summary>
    private static void AppendDiscBlend(Vector3 center, float radius, int rgb, byte alpha,
        List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
        List<int> triTex, List<int> triColor, List<uint> pickGuid, List<uint> pickScid)
    {
        if (radius <= 0f) return;
        const int Seg = 20;
        int packed = (alpha << 24) | (rgb & 0xFFFFFF);
        var prev = center + new Vector3(radius, 0, 0);
        for (int s = 1; s <= Seg; s++)
        {
            float a = s * MathF.PI * 2f / Seg;
            var cur = center + new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0);
            verts.Add(center); verts.Add(prev); verts.Add(cur);
            normals.Add(Vector3.UnitZ); normals.Add(Vector3.UnitZ); normals.Add(Vector3.UnitZ);
            uvs.Add(default); uvs.Add(default); uvs.Add(default);
            triTex.Add(-1); triColor.Add(packed);
            pickGuid.Add(0u); pickScid.Add(0u);
            prev = cur;
        }
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
    // ED-3 — the undo snapshot covers EVERY authored family, not just nodes
    // and objects. JSON with IncludeFields (the models and Vector3/Quaternion
    // use public fields); stitches/siblings are world-scope wiring over live
    // region graphs and stay outside the undo stack by design.
    private sealed class UndoState
    {
        public string NodesGas { get; set; } = "";
        public string ObjectsTsv { get; set; } = "";
        public List<RegionEmitter> Emitters { get; set; } = new();
        public List<RegionDecal> Decals { get; set; } = new();
        public List<AuthoredLight> Lights { get; set; } = new();
        public List<RegionTrigger> Triggers { get; set; } = new();
        public List<CommandPlacement> Commands { get; set; } = new();
        public List<Conversation> Conversations { get; set; } = new();
        public List<LogicalFlag> Flags { get; set; } = new();
        public List<MapQuest> Quests { get; set; } = new();
    }

    private static readonly System.Text.Json.JsonSerializerOptions UndoJson = new()
    {
        IncludeFields = true,
        // Populate get-only collection properties (trigger Rows, conversation
        // Nodes) instead of silently skipping them on deserialize.
        PreferredObjectCreationHandling = System.Text.Json.Serialization.JsonObjectCreationHandling.Populate,
    };

    private string Snapshot()
    {
        var s = new UndoState
        {
            NodesGas = IsEmpty ? "" : NodesGasWriter.Write(_region),
            ObjectsTsv = SerializeObjects(),
            Emitters = new List<RegionEmitter>(Emitters),
            Decals = new List<RegionDecal>(Decals),
            Lights = new List<AuthoredLight>(Lights),
            Triggers = new List<RegionTrigger>(Triggers),
            Commands = new List<CommandPlacement>(Commands),
            Conversations = new List<Conversation>(Conversations),
            Flags = new List<LogicalFlag>(LogicalFlags),
            Quests = new List<MapQuest>(MapQuests),
        };
        return System.Text.Json.JsonSerializer.Serialize(s, UndoJson);
    }

    private string SerializeObjects()
    {
        var sb = new StringBuilder();
        foreach (var o in _objects)
            sb.Append(o.Scid.ToString("X8")).Append('\t').Append(o.Template).Append('\t')
              .Append(o.NodeGuid.ToString("X8")).Append('\t')
              .Append(o.LocalPos.X.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(o.LocalPos.Y.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(o.LocalPos.Z.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(o.Orientation.X.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(o.Orientation.Y.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(o.Orientation.Z.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(o.Orientation.W.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(o.File).Append('\t')
              // ED-4b — instance overrides ride the same undo snapshot.
              .Append(o.ScaleMult.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(o.LifeOverride.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(o.LootDrop).Append('\n');
        return sb.ToString();
    }

    private void DeserializeObjects(string blob)
    {
        _objects.Clear();
        _selectedPlacedObject = null;
        OnPropertyChanged(nameof(SelectedPlacedObject));
        foreach (var line in blob.Split('\n'))
        {
            if (line.Length == 0) continue;
            var f = line.Split('\t');
            if (f.Length < 11) continue;
            _objects.Add(new PlacedObject
            {
                Scid = ParseHexU(f[0]), Template = f[1], NodeGuid = ParseHexU(f[2]),
                LocalPos = new Vector3(PF(f[3]), PF(f[4]), PF(f[5])),
                Orientation = new Quaternion(PF(f[6]), PF(f[7]), PF(f[8]), PF(f[9])),
                File = f[10],
                // ED-4b columns — absent in pre-override snapshots, so default.
                ScaleMult = f.Length > 11 && PF(f[11]) > 0f ? PF(f[11]) : 1f,
                LifeOverride = f.Length > 12 ? PF(f[12]) : 0f,
                LootDrop = f.Length > 13 ? f[13] : "",
            });
        }
    }

    private static uint ParseHexU(string s) =>
        uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
    private static float PF(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;

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
        UndoState s;
        try { s = System.Text.Json.JsonSerializer.Deserialize<UndoState>(snap, UndoJson) ?? new UndoState(); }
        catch (Exception ex) { Status = "Undo state unreadable: " + ex.Message; return; }
        var region = s.NodesGas.Length > 0 ? NodesGasReader.Read(GasDocument.Parse(s.NodesGas)) : new BuilderRegion();
        ReplaceRegion(region);
        DeserializeObjects(s.ObjectsTsv);
        RebuildPlacedRows();

        SelectedEmitter = null; SelectedDecal = null; SelectedLight = null;
        SelectedTrigger = null; SelectedCommand = null; SelectedConversation = null;
        SelectedFlag = null; SelectedQuest = null;
        _multiSel.Clear(); // ED-1b — restored collections hold NEW instances; old refs are ghosts
        RaiseMulti();
        Refill(Emitters, s.Emitters);
        Refill(Decals, s.Decals);
        Refill(Lights, s.Lights);
        Refill(Triggers, s.Triggers);
        Refill(Commands, s.Commands);
        Refill(Conversations, s.Conversations);
        Refill(LogicalFlags, s.Flags);
        Refill(MapQuests, s.Quests);
        BumpScidCounters();
        OnPropertyChanged(nameof(QuestKeys));
        AfterModelChanged(status);
    }

    private static void Refill<T>(ObservableCollection<T> target, List<T> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    /// <summary>After restoring a snapshot, push the SCID allocators past
    /// every restored id so a subsequent Add can never collide.</summary>
    private void BumpScidCounters()
    {
        foreach (var o in _objects) if (o.Scid >= _nextScid) _nextScid = o.Scid + 1;
        foreach (var l in Lights) if (l.Scid >= _nextLightScid) _nextLightScid = l.Scid + 1;
        foreach (var e in Emitters) if (e.Scid >= _nextEffectScid) _nextEffectScid = e.Scid + 1;
        foreach (var d in Decals) if (d.Scid >= _nextEffectScid) _nextEffectScid = d.Scid + 1;
        foreach (var t in Triggers) if (t.Scid >= _nextLogicScid) _nextLogicScid = t.Scid + 1;
        foreach (var c in Commands) if (c.Scid >= _nextLogicScid) _nextLogicScid = c.Scid + 1;
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
            var pkg = MapPackager.PackStartableMap(nodesGas, MapName, RegionName, outDir, BuildStartInfo(),
                assetsRoot: _assetsFolder, placements: _objects,
                lights: new List<AuthoredLight>(Lights), mood: BuildMood(),
                emitters: new List<RegionEmitter>(Emitters), decals: new List<RegionDecal>(Decals),
                triggers: new List<RegionTrigger>(Triggers), commands: new List<CommandPlacement>(Commands),
                conversations: new List<Conversation>(Conversations),
                sourceGuid: PrimaryStitches.Count > 0 ? PrimarySourceGuid() : 0,
                stitches: new List<RegionStitch>(PrimaryStitches), siblings: new List<StitchRegionRef>(Siblings),
                logicalFlags: new List<LogicalFlag>(LogicalFlags),
                questsGas: ComposeQuests());
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
            var pkg = MapPackager.PackStartableMap(nodesGas, MapName, RegionName, outDir, BuildStartInfo(), BuildSeedActor(), _assetsFolder, _objects,
                lights: new List<AuthoredLight>(Lights), mood: BuildMood(),
                emitters: new List<RegionEmitter>(Emitters), decals: new List<RegionDecal>(Decals),
                triggers: new List<RegionTrigger>(Triggers), commands: new List<CommandPlacement>(Commands),
                conversations: new List<Conversation>(Conversations),
                sourceGuid: PrimaryStitches.Count > 0 ? PrimarySourceGuid() : 0,
                stitches: new List<RegionStitch>(PrimaryStitches), siblings: new List<StitchRegionRef>(Siblings),
                logicalFlags: new List<LogicalFlag>(LogicalFlags),
                questsGas: ComposeQuests());
            RuntimeLauncher.LaunchPlayRegion(runtime, pkg.MapTankPath, terrain, logic, objects, pkg.RegionPath,
                onEarlyExit: (code, err) => System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    Status = $"Engine exited with code {code} — {FirstLine(err)}";
                    ValidationRows.Add(new ValidationRow(false, $"Runtime exited {code}: {FirstLine(err)}"));
                }));
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

        // ED-12 — contents statistics up front, so the scale of the region is
        // always visible next to its health.
        rows.Add(new ValidationRow(true,
            $"Contents: {_region.Nodes.Count} node(s) · {_objects.Count} object(s) · {Emitters.Count} emitter(s) · "
            + $"{Decals.Count} decal(s) · {Lights.Count} light(s) · {Triggers.Count} trigger(s) · {Commands.Count} command(s) · "
            + $"{Conversations.Count} conversation(s) · {MapQuests.Count} quest(s) · {LogicalFlags.Count} nav flag(s)."));

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

        if (_objects.Count > 0)
        {
            int badObj = 0, actors = 0;
            foreach (var o in _objects)
            {
                if (string.IsNullOrEmpty(o.Template) || !guids.Contains(o.NodeGuid)) badObj++;
                if (o.File.Equals("actor.gas", StringComparison.OrdinalIgnoreCase)) actors++;
            }
            int props = _objects.Count - actors;
            rows.Add(new ValidationRow(badObj == 0,
                badObj == 0
                    ? $"All {_objects.Count} placed object(s) anchored — {props} prop(s), {actors} actor(s)."
                    : $"{badObj} placed object(s) reference a missing node."));

            // ED-12 — template resolution: every placement's template must be
            // a known catalog entry or a created custom template, or the
            // engine spawns nothing there.
            int badTpl = 0;
            var badTplNames = new List<string>();
            foreach (var o in _objects)
                if (!string.IsNullOrEmpty(o.Template) && !_templateModel.ContainsKey(o.Template)
                    && !o.File.Equals("sound.gas", StringComparison.OrdinalIgnoreCase))
                { badTpl++; if (badTplNames.Count < 3) badTplNames.Add(o.Template); }
            rows.Add(new ValidationRow(badTpl == 0,
                badTpl == 0 ? "Every placed template resolves in the catalog."
                            : $"{badTpl} placement(s) use an unknown template ({string.Join(", ", badTplNames)}…)."));
        }

        // ED-12 — duplicate SCIDs anywhere = two things claiming one identity;
        // triggers/commands/quests would misfire silently in-engine.
        {
            var seen = new HashSet<uint>();
            int dup = 0;
            void Check(uint scid) { if (scid != 0 && !seen.Add(scid)) dup++; }
            foreach (var o in _objects) Check(o.Scid);
            foreach (var e in Emitters) Check(e.Scid);
            foreach (var d in Decals) Check(d.Scid);
            foreach (var l in Lights) Check(l.Scid);
            foreach (var t in Triggers) Check(t.Scid);
            foreach (var c in Commands) Check(c.Scid);
            rows.Add(new ValidationRow(dup == 0,
                dup == 0 ? "All SCIDs unique across every family."
                         : $"{dup} duplicate SCID(s) — two pieces claim one identity; delete and re-add the duplicates."));
        }

        // ED-12 — dialogue quest references must resolve (map-local quests
        // count; a typo'd key silently never journals).
        {
            int badQuestRef = 0;
            var known = new HashSet<string>(QuestKeys, StringComparer.OrdinalIgnoreCase);
            foreach (var c in Conversations)
                foreach (var n in c.Nodes)
                    if (!string.IsNullOrWhiteSpace(n.ActivateQuest) && !known.Contains(n.ActivateQuest.Split(',')[0].Trim()))
                        badQuestRef++;
            if (Conversations.Count > 0 || MapQuests.Count > 0)
                rows.Add(new ValidationRow(badQuestRef == 0,
                    badQuestRef == 0 ? "All dialogue quest references resolve (shipped + this map's quests)."
                                     : $"{badQuestRef} dialogue line(s) activate an unknown quest key."));
        }

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

        if (Lights.Count > 0)
        {
            int dir = 0;
            foreach (var l in Lights) if (l.Kind == AuthoredLightKind.Directional && l.AffectsActors) dir++;
            int authorOnly = Lights.Count - dir;
            rows.Add(new ValidationRow(true,
                $"{Lights.Count} light(s) — {Math.Min(dir, 4)} directional reach the engine"
                + (authorOnly > 0 ? $", {authorOnly} author-only (no in-engine illumination)." : ".")));
        }
        var moodCheck = BuildMood();
        if (moodCheck is not null)
            rows.Add(new ValidationRow(moodCheck.HasAudio,
                moodCheck.HasAudio
                    ? $"Mood '{moodCheck.Name}' — ambient audio will play (map is map_-prefixed)."
                    : "Mood set but has no ambient track — add one for region audio."));

        if (Emitters.Count > 0)
            rows.Add(new ValidationRow(true, $"{Emitters.Count} particle emitter(s) → emitter.gas (live in SiegeFX)."));
        if (Decals.Count > 0)
        {
            // ED-12 — beyond "has a name": the texture must actually RESOLVE
            // (install tanks or imported custom art) or the decal is invisible.
            int noTex = 0, unresolved = 0;
            foreach (var d in Decals)
            {
                if (string.IsNullOrWhiteSpace(d.Texture)) { noTex++; continue; }
                if (_textures?.Resolve(d.Texture) is not { Valid: true })
                    unresolved++;
            }
            rows.Add(new ValidationRow(noTex == 0 && unresolved == 0,
                noTex == 0 && unresolved == 0
                    ? $"{Decals.Count} decal(s) → decals.gas, textures resolve."
                    : $"Decals: {noTex} missing a texture name, {unresolved} texture(s) don't resolve (typo, or import it in the Custom tab)."));
        }
        int soundCount = 0;
        foreach (var o in _objects) if (o.File.Equals("sound.gas", StringComparison.OrdinalIgnoreCase)) soundCount++;
        if (soundCount > 0)
            rows.Add(new ValidationRow(true, $"{soundCount} placed sound(s) → objects/sound.gas (retail DS1 only; silent in the SiegeFX test)."));

        if (Triggers.Count > 0)
        {
            int emptyRows = 0, unknownVerbs = 0, badQuest = 0;
            foreach (var t in Triggers)
                foreach (var r in t.Rows)
                {
                    bool hasCond = false, hasAct = false;
                    foreach (var c in r.Conditions)
                        if (!string.IsNullOrWhiteSpace(c.Verb))
                        { hasCond = true; if (System.Array.IndexOf(RegionTrigger.Conditions, c.Verb) < 0) unknownVerbs++; }
                    foreach (var a in r.Actions)
                        if (!string.IsNullOrWhiteSpace(a.Verb))
                        {
                            hasAct = true;
                            if (System.Array.IndexOf(RegionTrigger.Actions, a.Verb) < 0) unknownVerbs++;
                            if (a.Verb == "change_quest_state" && !ArgsReferenceQuest(a.Args)) badQuest++;
                        }
                    if (!hasCond || !hasAct) emptyRows++;
                }
            rows.Add(new ValidationRow(emptyRows == 0, emptyRows == 0
                ? $"{Triggers.Count} trigger(s) → special.gas."
                : $"{emptyRows} trigger row(s) missing a condition or action (never fire)."));
            if (unknownVerbs > 0)
                rows.Add(new ValidationRow(false, $"{unknownVerbs} trigger verb(s) outside the live DSL — author-only (no-op in SiegeFX)."));
            if (badQuest > 0)
                rows.Add(new ValidationRow(false, $"{badQuest} change_quest_state action(s) name a key outside the QuestCatalog (bare state, no journal)."));
        }
        if (Commands.Count > 0)
        {
            int unresolved = 0;
            foreach (var c in Commands)
            {
                if (c.NextScid is uint n && !ResolvesScid(n)) unresolved++;
                if (c.Target1 is uint t1 && !ResolvesScid(t1)) unresolved++;
                if (c.Target2 is uint t2 && !ResolvesScid(t2)) unresolved++;
                if (c.ClientScid is uint cs && !ResolvesScid(cs)) unresolved++;
            }
            rows.Add(new ValidationRow(unresolved == 0, unresolved == 0
                ? $"{Commands.Count} command/NIS gizmo(s) → command.gas."
                : $"{unresolved} command SCID link(s) don't resolve in this region."));
        }
        if (Conversations.Count > 0)
        {
            int unbound = 0, badBind = 0, badQ = 0;
            foreach (var cv in Conversations)
            {
                if (cv.BoundActorScid == 0) unbound++;
                else if (!ResolvesScid(cv.BoundActorScid)) badBind++;
                foreach (var n in cv.Nodes)
                    if (!string.IsNullOrWhiteSpace(n.ActivateQuest)
                        && System.Array.IndexOf(QuestCatalogKeys.Keys, n.ActivateQuest.Trim()) < 0) badQ++;
            }
            rows.Add(new ValidationRow(badBind == 0, badBind == 0
                ? $"{Conversations.Count} conversation(s) → conversations.gas."
                : $"{badBind} conversation(s) bound to a missing actor SCID."));
            if (unbound > 0)
                rows.Add(new ValidationRow(true, $"{unbound} conversation(s) not bound to an NPC (unreachable in-game)."));
            if (badQ > 0)
                rows.Add(new ValidationRow(false, $"{badQ} dialogue activate_quest key(s) outside the QuestCatalog."));
        }
        if (Siblings.Count > 0 || PrimaryStitches.Count > 0)
        {
            rows.Add(new ValidationRow(_stitchDangling == 0, _stitchDangling == 0
                ? $"{PrimaryStitches.Count} reciprocal world stitch(es) → stitch_helper.gas."
                : $"{_stitchDangling} stitch(es) have no reciprocal (dangling — neighbour never streams)."));
            if (_stitchCollisions > 0)
                rows.Add(new ValidationRow(false, $"{_stitchCollisions} snode guid(s) collide across regions (WorldLayout throws — needs world-unique guids)."));
            if (_stitchUnreachable > 0)
                rows.Add(new ValidationRow(true, $"{_stitchUnreachable} region(s) unreachable from the primary via stitches."));
        }

        // Within-region snode-guid uniqueness — WorldLayout throws on a collision even single-region.
        var guidSeen = new HashSet<uint>();
        int dupGuids = 0;
        foreach (var n in _region.Nodes) if (!guidSeen.Add(n.Guid)) dupGuids++;
        if (dupGuids > 0)
            rows.Add(new ValidationRow(false, $"{dupGuids} duplicate snode guid(s) in this region (WorldLayout throws)."));

        if (LogicalFlags.Count > 0)
        {
            int blocked = 0, water = 0;
            foreach (var f in LogicalFlags)
            {
                if (!f.HumanPlayer && !f.ComputerPlayer) blocked++;
                if (f.Water) water++;
            }
            rows.Add(new ValidationRow(true, $"{LogicalFlags.Count} nav flag(s) → logical_flags.gas."));
            if (blocked > 0) rows.Add(new ValidationRow(true, $"{blocked} grouping(s) blocked to all players (walls?)."));
            if (water > 0) rows.Add(new ValidationRow(true, $"{water} water grouping(s) — impassable to stock actors."));
        }

        // Custom-asset integrity (LE-11/LE-12) — catch a placed custom mesh whose .asp went missing, and a
        // generated .sno tile with no index entry (both pass the generic checks but fail silently at launch).
        if (_assetsFolder is not null)
        {
            int missingAsp = 0;
            foreach (var o in _objects)
            {
                if (o.Template is null || !o.Template.StartsWith("ss_custom_", StringComparison.OrdinalIgnoreCase)) continue;
                var model = _templateModel.TryGetValue(o.Template, out var mm) ? mm : null;
                if (model is null) { missingAsp++; continue; }
                if (!System.IO.File.Exists(System.IO.Path.Combine(_assetsFolder, "art", "meshes", model + ".asp"))) missingAsp++;
            }
            if (missingAsp > 0)
                rows.Add(new ValidationRow(false, $"{missingAsp} placed custom mesh(es) have no matching .asp in art/meshes (won't render)."));

            var snoDir = System.IO.Path.Combine(_assetsFolder, "art", "terrain", "ss_custom");
            if (System.IO.Directory.Exists(snoDir))
            {
                var idxFile = System.IO.Path.Combine(_assetsFolder, "world", "global", "siege_nodes", "ss_custom", "misc_ss_custom.gas");
                string idxText = System.IO.File.Exists(idxFile) ? System.IO.File.ReadAllText(idxFile) : "";
                int orphanTiles = 0;
                foreach (var f in System.IO.Directory.GetFiles(snoDir, "*.sno"))
                {
                    var bare = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (!idxText.Contains($"filename={bare};", StringComparison.OrdinalIgnoreCase)) orphanTiles++;
                }
                rows.Add(orphanTiles == 0
                    ? new ValidationRow(true, "Custom terrain tiles all carry a [mesh_file] index entry.")
                    : new ValidationRow(false, $"{orphanTiles} custom .sno tile(s) have no [mesh_file] index entry (mesh_guid won't resolve)."));
            }
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

    // ── object placement ────────────────────────────────────────
    private const int MaxPropResults = 400;

    private void RefreshPropPalette()
    {
        PropPalette.Clear();
        var q = _propSearch?.Trim() ?? "";
        int shown = 0;
        foreach (var p in _placingActors ? _allActors : _allProps)
        {
            if (q.Length > 0 && p.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
            // ED-7 — tag filter (template-name prefix)
            if (_propTag != "(all)" && !p.Name.StartsWith(_propTag + "_", StringComparison.OrdinalIgnoreCase)) continue;
            PropPalette.Add(p);
            if (++shown >= MaxPropResults) break;
        }
    }

    /// <summary>Places the selected template on the selected node, at the node's local centre. Props
    /// ship into objects/non_interactive.gas; actors into objects/actor.gas (they animate + can fight).
    /// Both appear in the preview and when the map is tested/played.</summary>
    private void PlaceObject()
    {
        if (_selectedProp is null || _selectedNode is null) return;
        var node = _region.Find(_selectedNode.Guid);
        if (node is null) return;
        bool actor = _placingActors;
        PushUndo();
        PushRecent(RecentProps, _selectedProp.Name); // ED-1a
        _objects.Add(new PlacedObject
        {
            Scid = NextScid(),
            Template = _selectedProp.Name,
            NodeGuid = node.Guid,
            LocalPos = LocalCenter(node.Guid),
            File = actor ? "actor.gas" : "non_interactive.gas",
        });
        RebuildPlacedRows();
        var model = _templateModel.TryGetValue(_selectedProp.Name, out var mm) ? mm : "(none)";
        var mesh = _asp?.Resolve(model);
        var kind = actor ? "actor" : "prop";
        Status = mesh is null
            ? $"Placed {kind} {_selectedProp.Name} — model '{model}' has no .asp; shown as a marker cube."
            : $"Placed {kind} {_selectedProp.Name} — model '{model}' → {mesh.TriangleCount} tris.";
        RaiseCommands();
    }

    private void DeleteObject()
    {
        if (_selectedPlacedObject is null) return;
        PushUndo();
        uint scid = _selectedPlacedObject.Scid;
        _objects.RemoveAll(o => o.Scid == scid);
        _selectedPlacedObject = null;
        OnPropertyChanged(nameof(SelectedPlacedObject));
        RebuildPlacedRows();
        Status = $"Deleted object 0x{scid:X8}.";
        RaiseCommands();
    }

    private void RebuildPlacedRows()
    {
        PlacedObjects.Clear();
        foreach (var o in _objects)
            PlacedObjects.Add(new PlacedObjectRow(o.Scid, o.Template, o.NodeGuid,
                o.File.Equals("actor.gas", StringComparison.OrdinalIgnoreCase)));
        OnPropertyChanged(nameof(HasObjects));
    }

    public bool HasObjects => _objects.Count > 0;

    // ── lighting & mood (LE-6) ──────────────────────────────────
    private void AddLight(AuthoredLightKind kind)
    {
        PushUndo();
        var l = new AuthoredLight { Kind = kind, Scid = _nextLightScid++ };
        if (kind == AuthoredLightKind.Point && _selectedNode is not null)
        {
            l.NodeGuid = _selectedNode.Guid;
            l.Position = LocalCenter(_selectedNode.Guid);
        }
        Lights.Add(l);
        SelectedLight = l;
        Render();
        Status = kind == AuthoredLightKind.Point
            ? "Added a point light — ships to lights.gas, but SiegeFX renders no point-light illumination."
            : "Added a directional light — edit colour / intensity / direction; the preview updates live.";
    }

    private void DeleteLight()
    {
        if (_selectedLight is null) return;
        PushUndo();
        Lights.Remove(_selectedLight);
        SelectedLight = null;
        Render();
    }

    /// <summary>Authored directional lights (affects_actors) as renderer lights, capped at the engine's 4.</summary>
    private SoftwareRenderer.DirLight[]? BuildPreviewLights()
    {
        List<SoftwareRenderer.DirLight>? dl = null;
        foreach (var l in Lights)
        {
            if (l.Kind != AuthoredLightKind.Directional || !l.AffectsActors) continue;
            if (l.Direction.LengthSquared() < 1e-6f) continue;
            var dir = Vector3.Normalize(l.Direction);
            float r = ((l.Color >> 16) & 0xFF) / 255f, g = ((l.Color >> 8) & 0xFF) / 255f, b = (l.Color & 0xFF) / 255f;
            (dl ??= new()).Add(new SoftwareRenderer.DirLight(dir, r, g, b, l.Intensity));
            if (dl.Count >= 4) break;
        }
        return dl?.ToArray();
    }

    private AuthoredMood? BuildMood()
    {
        var m = new AuthoredMood
        {
            Name = $"map_{LightSanitize(MapName)}_{LightSanitize(RegionName)}_1",
            Interior = _moodInterior,
            Ambient = _moodAmbient, Standard = _moodStandard, Battle = _moodBattle,
            // ED-8 — atmosphere; empty boxes parse to "component off".
            FogNear = MoodF(_moodFogNear, -1f),
            FogFar = MoodF(_moodFogFar, -1f),
            FogColor = MoodColor(_moodFogColor, 0xFF888888),
            RainDensity = MoodF(_moodRain),
            Lightning = _moodLightning,
            SnowDensity = MoodF(_moodSnow),
            WindVelocity = MoodF(_moodWindVel),
            WindDirectionRad = MoodF(_moodWindDir) * MathF.PI / 180f,
        };
        return m.HasContent || m.Interior ? m : null;
    }

    private static string LightSanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "custom";
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw.Trim().ToLowerInvariant())
            sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' ? c : '_');
        var s = sb.ToString().Trim('_');
        return s.Length == 0 ? "custom" : s;
    }

    private void RaiseLightProps()
    {
        OnPropertyChanged(nameof(HasSelectedLight));
        OnPropertyChanged(nameof(SelectedLightIsDirectional));
        OnPropertyChanged(nameof(SelectedLightColorHex));
        OnPropertyChanged(nameof(SelectedLightIntensity));
        OnPropertyChanged(nameof(SelectedLightDirX));
        OnPropertyChanged(nameof(SelectedLightDirY));
        OnPropertyChanged(nameof(SelectedLightDirZ));
    }

    public string SelectedLightColorHex
    {
        get => _selectedLight is null ? "" : _selectedLight.Color.ToString("X8");
        set
        {
            if (_selectedLight is null) return;
            var t = (value ?? "").Trim();
            if (t.StartsWith("0x") || t.StartsWith("0X")) t = t.Substring(2);
            t = t.TrimStart('#');
            if (uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var c)) { _selectedLight.Color = c; Render(); }
        }
    }
    public string SelectedLightIntensity
    {
        get => _selectedLight is null ? "" : _selectedLight.Intensity.ToString("0.0##", CultureInfo.InvariantCulture);
        set { if (_selectedLight is not null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { _selectedLight.Intensity = v; Render(); } }
    }
    public string SelectedLightDirX
    {
        get => _selectedLight is null ? "" : _selectedLight.Direction.X.ToString("0.0##", CultureInfo.InvariantCulture);
        set { if (_selectedLight is not null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { var d = _selectedLight.Direction; d.X = v; _selectedLight.Direction = d; Render(); } }
    }
    public string SelectedLightDirY
    {
        get => _selectedLight is null ? "" : _selectedLight.Direction.Y.ToString("0.0##", CultureInfo.InvariantCulture);
        set { if (_selectedLight is not null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { var d = _selectedLight.Direction; d.Y = v; _selectedLight.Direction = d; Render(); } }
    }
    public string SelectedLightDirZ
    {
        get => _selectedLight is null ? "" : _selectedLight.Direction.Z.ToString("0.0##", CultureInfo.InvariantCulture);
        set { if (_selectedLight is not null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { var d = _selectedLight.Direction; d.Z = v; _selectedLight.Direction = d; Render(); } }
    }

    // ── effects: emitters, decals, placed sound (LE-7) ──────────
    private void AddEmitter(bool smoke)
    {
        if (_selectedNode is null) return;
        var e = new RegionEmitter
        {
            Scid = _nextEffectScid++, NodeGuid = _selectedNode.Guid,
            LocalPos = LocalCenter(_selectedNode.Guid), Smoke = smoke,
        };
        Emitters.Add(e);
        SelectedEmitter = e;
        Render();
        Status = $"Added a {(smoke ? "smoke" : "fire")} emitter → objects/emitter.gas (plays in SiegeFX).";
    }

    private void DeleteEmitter()
    {
        if (_selectedEmitter is null) return;
        PushUndo();
        Emitters.Remove(_selectedEmitter);
        SelectedEmitter = null;
        Render();
    }

    private void AddDecal()
    {
        if (_selectedNode is null) return;
        PushUndo();
        var d = new RegionDecal
        {
            Scid = _nextEffectScid++, NodeGuid = _selectedNode.Guid, OriginLocal = LocalCenter(_selectedNode.Guid),
        };
        Decals.Add(d);
        SelectedDecal = d;
        Status = "Added a decal — set its texture (a .raw basename under Art/Bitmaps/Decals).";
    }

    private void DeleteDecal()
    {
        if (_selectedDecal is null) return;
        PushUndo();
        Decals.Remove(_selectedDecal);
        SelectedDecal = null;
    }

    private void AddSound()
    {
        if (_selectedNode is null || string.IsNullOrWhiteSpace(_soundTemplate)) return;
        PushUndo();
        _objects.Add(new PlacedObject
        {
            Scid = NextScid(), Template = _soundTemplate.Trim(), NodeGuid = _selectedNode.Guid,
            LocalPos = LocalCenter(_selectedNode.Guid), File = "sound.gas",
        });
        RebuildPlacedRows();
        Render();
        Status = $"Added sound emitter '{_soundTemplate.Trim()}' → objects/sound.gas (retail DS1 only — silent in the SiegeFX test).";
        RaiseCommands();
    }

    // ── logic: triggers, commands, conversations, quests (LE-8) ──
    private static TriggerRow NewTriggerRow() => new()
    {
        Conditions = { new TriggerCall { Verb = "receive_world_message", Args = "\"we_entered_world\"" } },
        Actions = { new TriggerCall { Verb = "send_world_message", Args = "" } },
    };

    private void AddTrigger()
    {
        if (_selectedNode is null) return;
        PushUndo();
        var t = new RegionTrigger
        {
            Scid = _nextLogicScid++, NodeGuid = _selectedNode.Guid, LocalPos = LocalCenter(_selectedNode.Guid),
        };
        t.Rows.Add(NewTriggerRow());
        Triggers.Add(t);
        SelectedTrigger = t;
        Render();
        Status = "Added a trigger — edit its condition → action row(s). Boot rows should use receive_world_message(\"we_entered_world\").";
    }

    private void AddCommand()
    {
        if (_selectedNode is null) return;
        PushUndo();
        var c = new CommandPlacement
        {
            Scid = _nextLogicScid++, NodeGuid = _selectedNode.Guid, LocalPos = LocalCenter(_selectedNode.Guid),
        };
        Commands.Add(c);
        SelectedCommand = c;
        Render();
        Status = "Added a command gizmo → objects/command.gas. Chain NIS steps with next_scid; target actors by SCID.";
    }

    private void AddConversation()
    {
        PushUndo();
        var c = new Conversation { Key = $"custom_{Conversations.Count + 1}" };
        c.Nodes.Add(new DialogueLine { Order = 1, ScreenText = "Hello, traveler." });
        Conversations.Add(c);
        SelectedConversation = c;
        Status = "Added a conversation. Bind it to a placed actor, then add dialogue lines.";
    }

    private void AddDialogue()
    {
        if (_selectedConversation is null) return;
        PushUndo();
        int order = _selectedConversation.Nodes.Count + 1;
        var n = new DialogueLine { Order = order, ScreenText = "" };
        _selectedConversation.Nodes.Add(n);
        SelectedDialogue = n;
    }

    private void BindConversation()
    {
        if (_selectedConversation is null || _selectedPlacedObject is null) return;
        if (!_selectedPlacedObject.IsActor)
        {
            Status = "Select a placed ACTOR (not a prop) to bind a conversation to.";
            return;
        }
        _selectedConversation.BoundActorScid = _selectedPlacedObject.Scid;
        OnPropertyChanged(nameof(Conversations));
        Status = $"Bound {_selectedConversation.FullKey} to actor 0x{_selectedPlacedObject.Scid:X8}.";
    }

    /// <summary>Every SCID referenced by a trigger/command/conversation, mapped to whether it resolves
    /// to a placed object/trigger/command in this region — the copy/paste/delete integrity check.</summary>
    private bool ResolvesScid(uint scid)
    {
        if (scid == 0) return true;
        foreach (var o in _objects) if (o.Scid == scid) return true;
        foreach (var t in Triggers) if (t.Scid == scid) return true;
        foreach (var c in Commands) if (c.Scid == scid) return true;
        return false;
    }

    private static bool ArgsReferenceQuest(string args)
    {
        foreach (var k in QuestCatalogKeys.Keys)
            if (args.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // ── world stitching: multi-region adjacency (LE-9) ─────────
    private string StitchPrimaryLeaf() => LightSanitize(RegionName) is { Length: > 0 } s ? s : "region_r1";

    private uint PrimarySourceGuid()
    {
        if (_primarySourceGuid == 0) _primarySourceGuid = _nextRegionGuid++;
        return _primarySourceGuid;
    }

    /// <summary>Rebuilds the primary region's free-door list (stitch endpoints) and refreshes
    /// diagnostics. Called whenever the region's door graph changes.</summary>
    public void RefreshStitchState()
    {
        PrimaryFreeDoors.Clear();
        if (_catalog is not null)
            foreach (var n in _region.Nodes)
            {
                var s = _catalog.Resolve(n.MeshGuid);
                if (s is null) continue;
                foreach (var d in s.Doors)
                    if (!n.UsesDoor((int)d.Id))
                        PrimaryFreeDoors.Add(new StitchDoor(n.Guid, (int)d.Id, _catalog.NameOf(n.MeshGuid) ?? $"0x{n.MeshGuid:X8}"));
            }
        RecomputeStitchDiagnostics();
        RaiseCommands();
    }

    private void RaiseSiblingDoors()
    {
        SiblingFreeDoors.Clear();
        if (_selectedSibling is not null)
            foreach (var d in _selectedSibling.FreeDoors) SiblingFreeDoors.Add(d);
        _selectedSiblingDoor = null;
        OnPropertyChanged(nameof(SelectedSiblingDoor));
    }

    private void ImportSibling()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Region nodes (nodes.gas)|nodes.gas;*.gas|All files|*.*",
            Title = "Import a sibling region's nodes.gas",
        };
        if (dlg.ShowDialog() != true) return;
        string gas;
        try { gas = System.IO.File.ReadAllText(dlg.FileName); }
        catch (System.Exception ex) { Status = $"Couldn't read nodes.gas: {ex.Message}"; return; }

        string leaf = LightSanitize(DeriveLeaf(dlg.FileName));
        if (leaf.Length == 0) leaf = $"region_r{Siblings.Count + 2}";
        BuilderRegion parsed;
        try { parsed = NodesGasReader.Read(GasDocument.Parse(gas)); }
        catch (System.Exception ex) { Status = $"Couldn't parse nodes.gas: {ex.Message}"; return; }
        if (parsed.Nodes.Count == 0) { Status = "That nodes.gas had no snodes to import."; return; }

        var reff = new StitchRegionRef { LeafName = leaf, SourceGuid = _nextRegionGuid++, NodesGas = gas, IsPrimary = false };
        foreach (var sn in parsed.Nodes)
        {
            reff.SnodeGuids.Add(sn.Guid);
            var sno = _catalog?.Resolve(sn.MeshGuid);
            if (sno is not null)
                foreach (var d in sno.Doors)
                    if (!sn.UsesDoor((int)d.Id))
                        reff.FreeDoors.Add(new StitchDoor(sn.Guid, (int)d.Id, _catalog?.NameOf(sn.MeshGuid) ?? $"0x{sn.MeshGuid:X8}"));
        }
        Siblings.Add(reff);
        SelectedSibling = reff;
        RecomputeStitchDiagnostics();
        Status = $"Imported region '{leaf}' — {reff.SnodeGuids.Count} snode(s), {reff.FreeDoors.Count} free door(s).";
        RaiseCommands();
    }

    private static string DeriveLeaf(string nodesGasPath)
    {
        try
        {
            // …/regions/<leaf>/terrain_nodes/nodes.gas → <leaf>
            var tn = System.IO.Path.GetDirectoryName(nodesGasPath);
            var region = System.IO.Path.GetDirectoryName(tn);
            var leaf = System.IO.Path.GetFileName(region);
            return string.IsNullOrWhiteSpace(leaf) ? "region_r2" : leaf!;
        }
        catch { return "region_r2"; }
    }

    private void RemoveSibling()
    {
        if (_selectedSibling is null) return;
        for (int i = PrimaryStitches.Count - 1; i >= 0; i--)
            if (PrimaryStitches[i].DestRegion.Equals(_selectedSibling.LeafName, StringComparison.OrdinalIgnoreCase))
                PrimaryStitches.RemoveAt(i);
        Siblings.Remove(_selectedSibling);
        SelectedSibling = null;
        RecomputeStitchDiagnostics();
        RaiseCommands();
    }

    private void CreateStitch()
    {
        if (_selectedPrimaryDoor is null || _selectedSibling is null || _selectedSiblingDoor is null) return;
        uint pair = _nextPairId++;
        string primLeaf = StitchPrimaryLeaf();
        // Reciprocal pair sharing one pairId — both sides written atomically at pack time.
        PrimaryStitches.Add(new RegionStitch { PairId = pair, LocalSnode = _selectedPrimaryDoor.Snode, LocalDoor = _selectedPrimaryDoor.Door, DestRegion = _selectedSibling.LeafName });
        _selectedSibling.Stitches.Add(new RegionStitch { PairId = pair, LocalSnode = _selectedSiblingDoor.Snode, LocalDoor = _selectedSiblingDoor.Door, DestRegion = primLeaf });
        RecomputeStitchDiagnostics();
        Status = $"Stitched {primLeaf}·door {_selectedPrimaryDoor.Door} ⇄ {_selectedSibling.LeafName}·door {_selectedSiblingDoor.Door} (pair 0x{pair:X8}).";
        RaiseCommands();
    }

    private void DeleteStitch()
    {
        if (_selectedStitch is null) return;
        uint pair = _selectedStitch.PairId;
        for (int i = PrimaryStitches.Count - 1; i >= 0; i--) if (PrimaryStitches[i].PairId == pair) PrimaryStitches.RemoveAt(i);
        foreach (var sib in Siblings)
            for (int i = sib.Stitches.Count - 1; i >= 0; i--) if (sib.Stitches[i].PairId == pair) sib.Stitches.RemoveAt(i);
        SelectedStitch = null;
        RecomputeStitchDiagnostics();
        RaiseCommands();
    }

    private void RecomputeStitchDiagnostics()
    {
        // Snode-guid world-uniqueness (WorldLayout throws on collision).
        var seen = new Dictionary<uint, int>();
        foreach (var n in _region.Nodes) seen[n.Guid] = seen.TryGetValue(n.Guid, out var c0) ? c0 + 1 : 1;
        foreach (var sib in Siblings) foreach (var g in sib.SnodeGuids) seen[g] = seen.TryGetValue(g, out var c1) ? c1 + 1 : 1;
        _stitchCollisions = 0; foreach (var kv in seen) if (kv.Value > 1) _stitchCollisions++;

        string primLeaf = StitchPrimaryLeaf();

        // Dangling: a stitch whose reciprocal (same pairId, opposite direction) is absent.
        _stitchDangling = 0;
        foreach (var ps in PrimaryStitches)
        {
            bool recip = false;
            foreach (var sib in Siblings)
                if (sib.LeafName.Equals(ps.DestRegion, StringComparison.OrdinalIgnoreCase))
                    foreach (var ss in sib.Stitches)
                        if (ss.PairId == ps.PairId && ss.DestRegion.Equals(primLeaf, StringComparison.OrdinalIgnoreCase)) recip = true;
            if (!recip) _stitchDangling++;
        }

        // Reachability BFS from the primary over stitch edges.
        var byLeaf = new Dictionary<string, StitchRegionRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var sib in Siblings) byLeaf[sib.LeafName] = sib;
        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primLeaf };
        var queue = new Queue<string>();
        queue.Enqueue(primLeaf);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            IEnumerable<RegionStitch> edges = cur.Equals(primLeaf, StringComparison.OrdinalIgnoreCase)
                ? PrimaryStitches
                : byLeaf.TryGetValue(cur, out var r) ? r.Stitches : System.Array.Empty<RegionStitch>();
            foreach (var e in edges) if (reached.Add(e.DestRegion)) queue.Enqueue(e.DestRegion);
        }
        _stitchUnreachable = 0;
        int isolated = 0;
        foreach (var sib in Siblings)
        {
            if (!reached.Contains(sib.LeafName)) _stitchUnreachable++;
            if (sib.Stitches.Count == 0) isolated++;
        }

        StitchDiagnostics = PrimaryStitches.Count == 0 && Siblings.Count == 0
            ? "No stitches — the region is a standalone (isolated) world."
            : $"{1 + Siblings.Count} region(s) · {PrimaryStitches.Count} stitch(es) · dangling {_stitchDangling} · unreachable {_stitchUnreachable} · isolated {isolated} · guid-collisions {_stitchCollisions}";

        RebuildWorldGraph(reached: reached, byLeaf: byLeaf);
    }

    /// <summary>Lays out the world graph: the primary region pinned at the canvas centre and each
    /// sibling on a ring around it, with one line per stitch pair (dangling pairs flagged). A schematic
    /// node-link view of the stitch topology — reachability and reciprocity at a glance.</summary>
    private void RebuildWorldGraph(HashSet<string> reached, Dictionary<string, StitchRegionRef> byLeaf)
    {
        WorldGraphNodes.Clear();
        WorldGraphEdges.Clear();
        string primLeaf = StitchPrimaryLeaf();

        const double cx = 150, cy = 100, ring = 74, halfW = 52, halfH = 18;
        var pos = new Dictionary<string, (double x, double y)>(StringComparer.OrdinalIgnoreCase)
        {
            [primLeaf] = (cx - halfW, cy - halfH),
        };
        int n = Siblings.Count;
        for (int i = 0; i < n; i++)
        {
            double ang = 2 * Math.PI * i / Math.Max(1, n) - Math.PI / 2;
            pos[Siblings[i].LeafName] = (cx + ring * Math.Cos(ang) - halfW, cy + ring * Math.Sin(ang) - halfH);
        }

        // Edges first so nodes paint over them. One visible line per primary-side stitch.
        foreach (var ps in PrimaryStitches)
        {
            if (!pos.TryGetValue(ps.DestRegion, out var dp)) continue;
            var sp = pos[primLeaf];
            bool recip = false;
            if (byLeaf.TryGetValue(ps.DestRegion, out var dr))
                foreach (var ss in dr.Stitches) if (ss.PairId == ps.PairId) { recip = true; break; }
            WorldGraphEdges.Add(new WorldGraphEdge(sp.x + halfW, sp.y + halfH, dp.x + halfW, dp.y + halfH, !recip));
        }

        WorldGraphNodes.Add(new WorldGraphNode(primLeaf, $"{_region.Nodes.Count} snode(s)", pos[primLeaf].x, pos[primLeaf].y, true, true));
        foreach (var sib in Siblings)
        {
            var p = pos[sib.LeafName];
            WorldGraphNodes.Add(new WorldGraphNode(sib.LeafName, $"{sib.SnodeGuids.Count} snode(s)", p.x, p.y, false, reached.Contains(sib.LeafName)));
        }
    }

    // ── nav flags (LE-10) ──────────────────────────────────────
    private void AddNavFlag()
    {
        if (_selectedNode is null) return;
        PushUndo();
        foreach (var f in LogicalFlags)
            if (f.SnodeGuid == _selectedNode.Guid && f.Lnode == 0) { SelectedFlag = f; Status = "This node already has a nav flag (lnode 0)."; return; }
        var flag = new LogicalFlag { SnodeGuid = _selectedNode.Guid, Lnode = 0 };
        LogicalFlags.Add(flag);
        SelectedFlag = flag;
        Status = "Added a nav flag for the selected node — toggle human/computer passability (unset both = a wall).";
        RaiseCommands();
    }

    private static string FirstLine(string s)
    {
        foreach (var line in s.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length > 0) return t.Length > 160 ? t[..160] : t;
        }
        return "(no error output)";
    }

    private uint NextScid()
    {
        var used = new HashSet<uint>();
        foreach (var o in _objects) used.Add(o.Scid);
        uint s;
        do { s = _nextScid++; } while (s == 0 || used.Contains(s));
        return s;
    }

    /// <summary>The node's local-space XZ centre (from its SNO corner bounds), Y=0 — a sensible default
    /// drop point on the node floor, the same basis start positions and actors use.</summary>
    private Vector3 LocalCenter(uint guid)
    {
        var node = _region.Find(guid);
        var sno = node is not null ? _catalog?.Resolve(node.MeshGuid) : null;
        if (sno is null || sno.Corners.Length == 0) return default;
        float minx = float.MaxValue, maxx = float.MinValue, minz = float.MaxValue, maxz = float.MinValue;
        foreach (var c in sno.Corners)
        {
            var p = c.Position;
            if (p.X < minx) minx = p.X; if (p.X > maxx) maxx = p.X;
            if (p.Z < minz) minz = p.Z; if (p.Z > maxz) maxz = p.Z;
        }
        return new Vector3((minx + maxx) * 0.5f, 0f, (minz + maxz) * 0.5f);
    }

    private void SetAssetsFolder()
    {
        var folder = DialogService.PickFolder("Pick a custom-assets folder (laid out like a tank: art/…, world/…)");
        if (folder is null) return;
        AssetsFolder = folder;
        OpenAssetsFolderCommand.RaiseCanExecuteChanged();
        ImportTextureCommand.RaiseCanExecuteChanged();
        CreateTemplateCommand.RaiseCanExecuteChanged();
        ImportAudioCommand.RaiseCanExecuteChanged();
        Status = $"Custom assets: {MapPackager.CountAssets(folder):N0} file(s) will bundle into the map.";
    }

    /// <summary>GAME-5 — import audio into the map bundle: WAV validated by the
    /// engine's own PCM parser → assets/sound (usable by SED-referencing sound
    /// emitters); MP3 → assets/music (usable as mood ambient/standard/battle
    /// track by basename).</summary>
    private void ImportAudio()
    {
        if (_assetsFolder is null) { Status = "Set the assets folder first (Custom tab)."; return; }
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Audio (*.wav;*.mp3)|*.wav;*.mp3|WAV (*.wav)|*.wav|MP3 (*.mp3)|*.mp3|All files|*.*",
            Title = "Import audio (WAV for sound effects, MP3 for music)",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            var sb = new StringBuilder();
            foreach (var ch in Path.GetFileNameWithoutExtension(dlg.FileName).ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_');
            var name = sb.Length > 0 ? sb.ToString() : "audio";
            if (ext == ".wav")
            {
                var bytes = File.ReadAllBytes(dlg.FileName);
                var clip = SiegeFX.Audio.WavLoader.Parse(bytes); // engine acceptance
                var dir = Path.Combine(_assetsFolder, "sound");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, name + ".wav"), bytes);
                Status = $"Imported {name}.wav — {clip.Channels}ch {clip.BitsPerSample}-bit {clip.SampleRate}Hz, engine-verified. " +
                         "Reference it by basename from sound emitters / effect cues.";
            }
            else if (ext == ".mp3")
            {
                var dir = Path.Combine(_assetsFolder, "music");
                Directory.CreateDirectory(dir);
                File.Copy(dlg.FileName, Path.Combine(dir, name + ".mp3"), overwrite: true);
                Status = $"Imported {name}.mp3 — use '{name}' as a mood track (Region tab: ambient / standard / battle).";
            }
            else Status = "Pick a .wav (sound effect) or .mp3 (music track).";
        }
        catch (Exception ex) { Status = "Audio import failed: " + ex.Message; }
    }

    /// <summary>GAME-2 — import any WIC-decodable image (PNG/JPG/BMP/GIF/TIFF)
    /// as a DS1 <c>.raw</c> with a full mip chain, verified by the engine's own
    /// reader before it lands in the assets folder. The basename becomes the
    /// texture name usable by decals, custom terrain texsets, and templates.</summary>
    private void ImportTexture()
    {
        if (_assetsFolder is null) { Status = "Set the assets folder first (Custom tab)."; return; }
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
            Title = "Import an image as a DS1 .raw texture",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                new Uri(dlg.FileName),
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var raw = RawWriter.Write(decoder.Frames[0]);
            var img = SiegeFX.Core.Assets.RawImage.Load(raw); // the engine reader is the acceptance test
            var sb = new StringBuilder();
            foreach (var ch in Path.GetFileNameWithoutExtension(dlg.FileName).ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_');
            var name = sb.Length > 0 ? sb.ToString() : "texture";
            var dir = Path.Combine(_assetsFolder, "art", "bitmaps");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".raw"), raw);
            Status = $"Imported {name}.raw — {img.Width}×{img.Height}, {img.SurfaceCount} mips, engine-verified. " +
                     $"Use '{name}' wherever a texture name is asked (decals, custom tiles, templates).";
        }
        catch (Exception ex) { Status = "Texture import failed: " + ex.Message; }
    }

    private void OpenAssetsFolder()
    {
        if (_assetsFolder is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_assetsFolder) { UseShellExecute = true }); }
        catch (Exception ex) { Status = "Couldn't open folder: " + ex.Message; }
    }

    /// <summary>Imports a Wavefront OBJ, converts it to a static DS1 <c>.asp</c>, round-trips it through
    /// the engine's own reader (the format has no length fields, so a stride bug only surfaces there),
    /// and saves it into the custom-assets tree at <c>art/meshes/&lt;name&gt;.asp</c> so it bundles into the
    /// map and resolves by basename.</summary>
    private void ImportObjMesh()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Mesh (*.obj;*.gltf;*.glb)|*.obj;*.gltf;*.glb|Wavefront OBJ (*.obj)|*.obj|glTF 2.0 (*.gltf;*.glb)|*.gltf;*.glb|All files|*.*",
            Title = "Import a mesh (OBJ or glTF) as a custom .asp",
        };
        if (dlg.ShowDialog() != true) return;

        ObjImporter.Result res;
        try
        {
            var ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
            res = ext is ".gltf" or ".glb"
                ? GltfImporter.Parse(System.IO.File.ReadAllBytes(dlg.FileName), System.IO.Path.GetDirectoryName(dlg.FileName))
                : ObjImporter.Parse(System.IO.File.ReadAllText(dlg.FileName));
        }
        catch (Exception ex) { Status = $"Couldn't parse mesh: {ex.Message}"; return; }
        ObjImporter.FillMissingNormals(res);
        if (res.Faces.Count == 0) { Status = "That mesh produced no triangles."; return; }

        byte[] asp;
        try
        {
            asp = res.IsSkinned
                ? AspWriter.WriteSkinned(res.Positions, res.Corners, res.Faces, res.TextureNames, res.Bones!, res.Skins!)
                : AspWriter.WriteStatic(res.Positions, res.Corners, res.Faces, res.TextureNames);
        }
        catch (Exception ex) { Status = $"ASP write failed: {ex.Message}"; return; }

        // Reader-as-oracle round-trip: if this throws or mis-counts, the .asp would corrupt in-engine.
        try
        {
            var check = SiegeFX.Core.Assets.AspMesh.Load(asp);
            if (check.TriangleCount != res.Faces.Count)
            { Status = $"ASP round-trip mismatch ({check.TriangleCount} vs {res.Faces.Count} tris) — not saved."; return; }
        }
        catch (Exception ex) { Status = $"ASP round-trip failed ({ex.Message}) — not saved."; return; }

        string kind = res.IsSkinned ? $"skinned ({res.Bones!.Count} bones)" : "static";

        string baseName = LightSanitize(System.IO.Path.GetFileNameWithoutExtension(dlg.FileName));
        if (baseName.Length == 0) baseName = "custom_mesh";
        string outPath;
        if (_assetsFolder is not null)
        {
            var meshDir = System.IO.Path.Combine(_assetsFolder, "art", "meshes");
            System.IO.Directory.CreateDirectory(meshDir);
            outPath = System.IO.Path.Combine(meshDir, baseName + ".asp");
        }
        else
        {
            var picked = DialogService.SaveFileAs(baseName + ".asp");
            if (picked is null) return;
            outPath = picked;
        }
        try { System.IO.File.WriteAllBytes(outPath, asp); }
        catch (Exception ex) { Status = $"Couldn't save .asp: {ex.Message}"; return; }

        OnPropertyChanged(nameof(AssetsLabel));

        // With an assets folder, also emit a placeable template + register it in the prop palette so the
        // mesh can be dropped on a node (bundles as world/contentdb/templates/ss_custom.gas; the engine's
        // map-template fallback resolves it at play time).
        if (_assetsFolder is not null)
        {
            string tplName = "ss_custom_" + baseName;
            AppendCustomTemplate(tplName, baseName);
            RegisterCustomProp(tplName, baseName);
            OnPropertyChanged(nameof(AssetsLabel));
            Status = $"Imported {baseName}.asp ({kind}) — {res.Faces.Count} tris. Placeable as '{tplName}' in the object palette (previews as a marker; renders in-engine).";
        }
        else
        {
            Status = $"Imported {baseName}.asp ({kind}) — {res.Positions.Count} verts, {res.Faces.Count} tris, texset '{res.TextureNames[0]}'. Round-trip OK → {outPath}. Set an assets folder to make it placeable.";
        }
    }

    /// <summary>Generates a parametric custom terrain tile (flat or ramp), runs it through the nav generator
    /// when walkable, verifies it round-trips through the engine's SnoModel reader, then saves the .sno plus a
    /// [mesh_file] index entry so the engine's SnoMeshIndex can resolve its mesh_guid.</summary>
    private void GenerateTerrainTile()
    {
        if (_assetsFolder is null)
        { Status = "Set an assets folder first — the .sno and its index bundle into the mod."; return; }

        string name = LightSanitize(_tileName);
        if (name.Length == 0) name = "ss_tile";
        string tex = LightSanitize(_tileTexture);
        if (tex.Length == 0) tex = "custom_terrain";

        float size = (float)Math.Clamp(_tileSize, 1, 128);
        int sub = Math.Clamp(_tileSubdiv, 1, 64);

        List<SnoWriter.Vertex> verts;
        List<SnoWriter.Tri> tris;
        if (TileIsRamp) (verts, tris) = SnoWriter.BuildRampTile(size, size, sub, (float)_tileRise);
        else (verts, tris) = SnoWriter.BuildFlatTile(size, size, sub);

        var groupings = _tileWalkable ? SnoNavGen.Build(verts, tris) : null;
        byte[] sno;
        try { sno = SnoWriter.Write(verts, tris, tex, groupings: groupings); }
        catch (Exception ex) { Status = $"SNO write failed: {ex.Message}"; return; }

        // Reader-as-oracle round-trip: a stride slip would silently corrupt this length-less format.
        SiegeFX.Core.Assets.SnoModel check;
        try { check = SiegeFX.Core.Assets.SnoModel.Load(sno); }
        catch (Exception ex) { Status = $"SNO round-trip failed ({ex.Message}) — not saved."; return; }
        if (check.TotalTriangleCount != tris.Count)
        { Status = $"SNO round-trip mismatch ({check.TotalTriangleCount} vs {tris.Count} tris) — not saved."; return; }

        uint guid = _nextTerrainGuid++;
        var snoDir = System.IO.Path.Combine(_assetsFolder, "art", "terrain", "ss_custom");
        System.IO.Directory.CreateDirectory(snoDir);
        var snoPath = System.IO.Path.Combine(snoDir, name + ".sno");
        try { System.IO.File.WriteAllBytes(snoPath, sno); }
        catch (Exception ex) { Status = $"Couldn't save .sno: {ex.Message}"; return; }
        AppendMeshFileIndex(name, guid);

        OnPropertyChanged(nameof(AssetsLabel));
        int floors = check.LogicalGroupings.Count(g => g.Kind == SiegeFX.Core.Assets.SnoModel.FloorKind.Floor);
        Status = $"Generated {name}.sno ({TileKind.ToLowerInvariant()}, {tris.Count} tris, " +
                 $"{(_tileWalkable ? $"{floors} floor grouping(s)" : "cosmetic")}) guid=0x{guid:X8}. " +
                 $"Round-trip OK → {snoPath}";
    }

    /// <summary>Rewrites the custom siege-node index (<c>world/global/siege_nodes/ss_custom/misc_ss_custom.gas</c>)
    /// with every generated tile's <c>[mesh_file*]</c> entry, matching DS1's exact shape so SnoMeshIndex maps
    /// mesh_guid → bare .sno name.</summary>
    private void AppendMeshFileIndex(string filename, uint guid)
    {
        if (_assetsFolder is null) return;
        var dir = System.IO.Path.Combine(_assetsFolder, "world", "global", "siege_nodes", "ss_custom");
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "misc_ss_custom.gas");

        var entries = new List<(string Name, string Guid)>();
        if (System.IO.File.Exists(file))
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                System.IO.File.ReadAllText(file), @"filename\s*=\s*([^;]+);\s*guid\s*=\s*(0x[0-9A-Fa-f]+)"))
                entries.Add((m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));

        if (!entries.Any(e => e.Name.Equals(filename, StringComparison.OrdinalIgnoreCase)))
            entries.Add((filename, $"0x{guid:X8}"));

        var sb = new System.Text.StringBuilder();
        sb.Append("[t:siege_nodes,n:*]\r\n{\r\n");
        foreach (var e in entries)
            sb.Append($"\t[mesh_file*] {{ filename={e.Name}; guid={e.Guid}; }}\r\n");
        sb.Append("}\r\n");
        System.IO.File.WriteAllText(file, sb.ToString());
    }

    /// <summary>Appends a self-contained placeable template (no <c>specializes</c>, so it resolves without
    /// a parent) into the custom-assets contentdb. Skips if the template is already present.</summary>
    private void AppendCustomTemplate(string tplName, string model)
    {
        if (_assetsFolder is null) return;
        var dir = System.IO.Path.Combine(_assetsFolder, "world", "contentdb", "templates");
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "ss_custom.gas");
        string existing = System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file) : "";
        if (existing.Contains($"n:{tplName}]", StringComparison.OrdinalIgnoreCase)) return;
        var block = $"[t:template,n:{tplName}]\r\n{{\r\n\tdoc = \"SiegeSmith custom mesh\";\r\n\t[aspect]\r\n\t{{\r\n\t\tmodel = {model};\r\n\t}}\r\n}}\r\n";
        System.IO.File.AppendAllText(file, block);
    }

    /// <summary>Adds an imported custom mesh to the prop palette so it can be placed like any stock prop.</summary>
    private void RegisterCustomProp(string tplName, string model)
    {
        if (!_templateModel.ContainsKey(tplName))
        {
            _allProps.Add(new PropTemplate(tplName, model));
            _templateModel[tplName] = model;
        }
        RefreshPropPalette();
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
        var triColor = new List<int>();  // packed 0xRRGGBB per triangle (-1 = default), colour-codes markers
        var pickGuid = new List<uint>(); // node GUID per triangle, for click-picking
        var pickScid = new List<uint>(); // placed-object SCID per triangle (0 = terrain), for object grabbing
        _nodeWorld.Clear();
        _markers.Clear();
        var texList = new List<SoftwareRenderer.Texture>();
        var texSlot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // dedup textures across nodes/surfaces
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var node in _region.Nodes)
        {
            if (!layout.TryGetTransform(node.Guid, out var world)) continue;
            _nodeWorld[node.Guid] = world;
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
                    triColor.Add(-1);
                    pickGuid.Add(node.Guid);
                    pickScid.Add(0u);
                }
            }
        }

        // Placed objects — render each prop's .asp mesh at its composed world transform. Same Z-up
        // convention as terrain, so no bind-pose flip: world = R(orient) · T(localPos) · nodeWorld.
        // ED-9 — hidden families (Outliner eye) neither render nor pick.
        if (GroupVisible("Objects"))
        foreach (var o in _objects)
        {
            if (!layout.TryGetTransform(o.NodeGuid, out var nodeWorld)) continue;

            var world = Matrix4x4.CreateFromQuaternion(o.Orientation)
                      * Matrix4x4.CreateTranslation(o.LocalPos)
                      * nodeWorld;

            bool objSel = _selectedPlacedObject?.Scid == o.Scid || _multiSel.Contains(o);
            AspMesh? mesh = _templateModel.TryGetValue(o.Template, out var model) ? _asp?.Resolve(model) : null;
            if (mesh is null || mesh.TriangleIndices.Length < 3)
            {
                // No mesh resolved — draw a marker cube so the placement is always visible + grabbable.
                AppendMarkerCube(world, MarkerSize(_radius), verts, normals, uvs, triTex, triColor, pickGuid, pickScid,
                    o.Scid, objSel ? Brighten(MarkerObject) : -1, ref min, ref max);
                continue;
            }

            int mtc = mesh.TriangleIndices.Length / 3;
            var subsetTex = new int[mtc];
            System.Array.Fill(subsetTex, -1);
            foreach (var sub in mesh.Subsets)
            {
                int slot = (sub.TextureIndex >= 0 && sub.TextureIndex < mesh.TextureNames.Count)
                    ? ResolveSlot(mesh.TextureNames[sub.TextureIndex], "", texSlot, texList) : -1;
                int end = Math.Min(mtc, sub.FirstTriangle + sub.TriangleCount);
                for (int t = Math.Max(0, sub.FirstTriangle); t < end; t++) subsetTex[t] = slot;
            }
            for (int t = 0; t < mtc; t++)
            {
                for (int e = 0; e < 3; e++)
                {
                    var corner = mesh.Corners[mesh.TriangleIndices[t * 3 + e]];
                    var p = Vector3.Transform(mesh.Positions[corner.VertexIndex], world);
                    verts.Add(p);
                    normals.Add(Vector3.TransformNormal(corner.Normal, world));
                    uvs.Add(corner.Uv);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                triTex.Add(subsetTex[t]);
                triColor.Add(-1);
                // pickGuid stays 0 for placed meshes: the drag projection
                // (PickPoint by node guid) must only see the node's TERRAIN,
                // or a dragged object hits its own faces and crawls toward
                // the camera. Grabbing/selection rides pickScid instead.
                pickGuid.Add(0u);
                pickScid.Add(o.Scid);
            }

            // ED-1b — a bright base puck marks selected mesh objects (the CPU
            // renderer can't outline a textured mesh) and doubles as a grab
            // handle; multi-selection members all get one.
            if (objSel)
                AppendMarkerCube(world, MarkerSize(_radius) * 0.55f, verts, normals, uvs, triTex, triColor, pickGuid, pickScid,
                    o.Scid, Brighten(MarkerObject), ref min, ref max);
        }

        // Every node-anchored placeable that has no mesh of its own previews as a colour-coded, grabbable
        // marker cube — particles/logic/decals don't render in the software renderer, but you can still see,
        // select, and drag each one. Selected marker uses the accent colour so you can tell what you're editing.
        void Marker(uint nodeGuid, Vector3 localPos, uint scid, int color, object item, float sizeScale = 1f)
        {
            if (!layout.TryGetTransform(nodeGuid, out var nw)) return;
            var w = Matrix4x4.CreateTranslation(localPos) * nw;
            bool sel = ReferenceEquals(item, _selectedEmitter) || ReferenceEquals(item, _selectedTrigger)
                    || ReferenceEquals(item, _selectedCommand) || ReferenceEquals(item, _selectedDecal)
                    || ReferenceEquals(item, _selectedLight)
                    || _multiSel.Contains(item); // ED-1b — every set member lights up
            // Selection brightens the TYPE colour instead of replacing it —
            // a selected fire emitter still reads as fire, just lit up.
            AppendMarkerCube(w, MarkerSize(_radius) * sizeScale, verts, normals, uvs, triTex, triColor, pickGuid, pickScid,
                scid, sel ? Brighten(color) : color, ref min, ref max);
            if (scid != 0) _markers[scid] = item;
        }

        // With live FX the flames ARE the preview — the emitter's cube
        // shrinks to a small base handle for grabbing. ED-9 — each family's
        // Outliner eye gates its markers (hidden = invisible + unpickable).
        if (GroupVisible("Emitters"))
            foreach (var em in Emitters)
                Marker(em.NodeGuid, em.LocalPos, em.Scid, em.Smoke ? MarkerSmoke : MarkerFire, em,
                    sizeScale: _liveFx ? 0.45f : 1f);
        if (GroupVisible("Triggers"))
            foreach (var tg in Triggers)
                Marker(tg.NodeGuid, tg.LocalPos, tg.Scid, MarkerTrigger, tg);
        if (GroupVisible("Lights"))
            foreach (var pl in Lights)
                if (pl.Kind == AuthoredLightKind.Point)
                    Marker(pl.NodeGuid, pl.Position, pl.Scid, MarkerLight, pl);
        if (GroupVisible("Commands"))
            foreach (var cm in Commands)
                Marker(cm.NodeGuid, cm.LocalPos, cm.Scid, MarkerCommand, cm);

        // GAME-1 — decals preview as REAL textured quads on the surface
        // (world-absolute basis per the decal contract; origin node-local).
        // The quad carries the decal's scid, so clicking the art selects it.
        // A small marker cube stays only while the texture doesn't resolve.
        if (GroupVisible("Decals"))
        foreach (var dc in Decals)
        {
            int slot = string.IsNullOrWhiteSpace(dc.Texture) ? -1 : ResolveSlot(dc.Texture, "", texSlot, texList);
            if (slot < 0 || !layout.TryGetTransform(dc.NodeGuid, out var dnw))
            {
                Marker(dc.NodeGuid, dc.OriginLocal, dc.Scid, MarkerDecal, dc);
                continue;
            }
            if (dc.Scid != 0) _markers[dc.Scid] = dc;
            var o = Vector3.Transform(dc.OriginLocal, dnw) + dc.Normal * 0.04f;
            var h = dc.AxisH * (dc.HorizExtent * 0.5f);
            var v = dc.AxisV * (dc.VertExtent * 0.5f);
            AppendQuad(o - h - v, o + h - v, o + h + v, o - h + v, slot, -1, dc.Scid,
                verts, normals, uvs, triTex, triColor, pickGuid, pickScid);
        }

        // GAME-1 — LIVE effects: fire/smoke emitters preview as animated,
        // age-shaded billboard particles (deterministic per time, so no sim
        // state). Excluded from the scene bounds so the camera never
        // "breathes" with the flames; not pickable (the marker cube is the
        // grab handle). The FX toolbar toggle pauses this for huge regions.
        if (_liveFx && Emitters.Count > 0 && GroupVisible("Emitters"))
        {
            var fxDir = new Vector3(MathF.Cos(_pitch) * MathF.Cos(_yaw), MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch));
            var fxRight = Vector3.Normalize(Vector3.Cross(new Vector3(0, 0, 1), fxDir));
            var fxUp = Vector3.Normalize(Vector3.Cross(fxDir, fxRight));
            foreach (var em in Emitters)
            {
                if (!layout.TryGetTransform(em.NodeGuid, out var enw)) continue;
                AppendEmitterParticles(em, Vector3.Transform(em.LocalPos, enw), fxRight, fxUp, _fxTime,
                    verts, normals, uvs, triTex, triColor, pickGuid, pickScid);
            }
        }

        // ═══ ED-9 — analysis overlays (translucent, unpickable, no bounds) ═══
        if (_ovTriggers && GroupVisible("Triggers"))
            foreach (var tg in Triggers)
                if (layout.TryGetTransform(tg.NodeGuid, out var tvw))
                    AppendBoxBlend(Matrix4x4.CreateTranslation(tg.LocalPos) * tvw, TriggerHalfExtents(tg),
                        MarkerTrigger, 0x30, verts, normals, uvs, triTex, triColor, pickGuid, pickScid);
        if (_ovRadii && GroupVisible("Lights"))
            foreach (var pl in Lights)
                if (pl.Kind == AuthoredLightKind.Point && layout.TryGetTransform(pl.NodeGuid, out var lvw))
                {
                    var lc = Vector3.Transform(pl.Position, lvw);
                    int rgb = (int)(pl.Color & 0xFFFFFF);
                    AppendDiscBlend(lc, pl.OuterRadius, rgb, 0x22, verts, normals, uvs, triTex, triColor, pickGuid, pickScid);
                    AppendDiscBlend(lc, pl.InnerRadius, rgb, 0x44, verts, normals, uvs, triTex, triColor, pickGuid, pickScid);
                }
        if (_ovNav)
            foreach (var f in LogicalFlags)
                if (layout.TryGetTransform(f.SnodeGuid, out var fvw))
                    AppendBoxBlend(fvw, new Vector3(0.5f, 0.5f, 0.5f),
                        0x7FBF7F, 0x60, verts, normals, uvs, triTex, triColor, pickGuid, pickScid);
        if (_ovBounds && _catalog is not null)
            foreach (var node in _region.Nodes)
            {
                if (!layout.TryGetTransform(node.Guid, out var bvw)) continue;
                var bs = _catalog.Resolve(node.MeshGuid);
                if (bs is null) continue;
                var ctr = (bs.MinBounds + bs.MaxBounds) * 0.5f;
                var half = (bs.MaxBounds - bs.MinBounds) * 0.5f;
                AppendBoxBlend(Matrix4x4.CreateTranslation(ctr) * bvw, half,
                    0x8A8F98, 0x16, verts, normals, uvs, triTex, triColor, pickGuid, pickScid);
            }

        if (verts.Count < 3)
        {
            Image = null;
            _pickVerts = System.Array.Empty<Vector3>();
            _pickGuid = System.Array.Empty<uint>();
            _pickScid = System.Array.Empty<uint>();
            return;
        }

        _center = (min + max) * 0.5f;
        _radius = MathF.Max((max - min).Length() * 0.5f, 0.001f);
        if (_dist <= 0f) _dist = _radius * 2.6f;
        _pickVerts = verts.ToArray();
        _pickGuid = pickGuid.ToArray();
        _pickScid = pickScid.ToArray();

        bool useTex = _textured && !_wireframe && texList.Count > 0
                      && uvs.Count == verts.Count && triTex.Count == verts.Count / 3;
        var lights = BuildPreviewLights(); // authored directional lights → the software renderer
        var triColorArr = triColor.ToArray();
        var fog = PreviewFog(); // ED-8 — audition the authored mood fog live
        var bgra = useTex
            ? SoftwareRenderer.RenderTextured(verts.ToArray(), normals.ToArray(), uvs.ToArray(), triTex.ToArray(), texList.ToArray(),
                _vw, _vh, _center + _pan, _radius, _yaw, _pitch, _dist, lights, triColorArr, _ortho, fog)
            : SoftwareRenderer.Render(verts.ToArray(), normals.ToArray(), _vw, _vh,
                _center + _pan, _radius, _yaw, _pitch, _dist, _wireframe, lights, triColorArr, _ortho, fog);
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

    // Absolute-ish marker size: scales gently with the region but clamps hard —
    // the old unclamped radius*0.03 made markers enormous in big regions.
    private static float MarkerSize(float radius) => Math.Clamp(radius * 0.015f, 0.25f, 0.6f);

    /// <summary>GAME-1 — appends one textured/coloured quad (two tris) with a
    /// pick scid, used by decal previews and effect billboards.</summary>
    private static void AppendQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int texSlot, int color, uint scid,
        List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
        List<int> triTex, List<int> triColor, List<uint> pickGuid, List<uint> pickScid)
    {
        void V(Vector3 p, float u, float w)
        {
            verts.Add(p);
            normals.Add(Vector3.Zero);
            uvs.Add(new Vector2(u, w));
        }
        V(p0, 0, 0); V(p1, 1, 0); V(p2, 1, 1);
        V(p0, 0, 0); V(p2, 1, 1); V(p3, 0, 1);
        for (int i = 0; i < 2; i++)
        {
            triTex.Add(texSlot);
            triColor.Add(color);
            pickGuid.Add(0u);
            pickScid.Add(scid);
        }
    }

    /// <summary>GAME-1 — the live fire/smoke preview. Each particle's whole
    /// life is a pure function of (emitter scid, index, time): phase loops
    /// over the fade time, the particle spirals up from the base with the
    /// authored size/growth, and the colour rides the phase (fire: hot core
    /// → orange → ember; smoke: light → dark grey, expanding). Deterministic,
    /// so pausing the FX timer freezes rather than resets.</summary>
    private static void AppendEmitterParticles(RegionEmitter em, Vector3 basePos, Vector3 right, Vector3 up, float t,
        List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
        List<int> triTex, List<int> triColor, List<uint> pickGuid, List<uint> pickScid)
    {
        int count = Math.Clamp(em.Count, 10, 48); // preview budget — the engine shows the full count
        float life = MathF.Max(0.4f, em.Fade);
        float size0 = MathF.Max(0.06f, em.ParticleSize * 0.5f);
        float grow = MathF.Max(0f, em.Growth);
        for (int i = 0; i < count; i++)
        {
            uint seed = em.Scid * 977u + (uint)i * 7919u;
            float h0 = Hash01(seed);
            float h1 = Hash01(seed * 3u + 11u);
            float h2 = Hash01(seed * 7u + 5u);
            float phase = (t / life + h0) % 1f;
            float ang = h1 * (MathF.PI * 2f) + phase * (0.8f + h2 * 1.4f);
            float rad = 0.10f + size0 * 0.4f * h2 + phase * (em.Smoke ? 0.55f : 0.22f);
            float rise = phase * life * (em.Smoke ? 0.9f : 1.25f);
            var p = basePos + new Vector3(MathF.Cos(ang) * rad, MathF.Sin(ang) * rad, 0.05f + rise);
            float flick = em.Smoke ? 1f : 0.8f + 0.4f * Hash01(seed ^ (uint)(t * 24f));
            float s = size0 * (0.45f + phase * (0.8f + grow * 0.35f)) * flick;
            int rgb = em.Smoke
                ? LerpRgb(0x9A9AA2, 0x3A3A40, phase)
                : phase < 0.35f
                    ? LerpRgb(0xFFE08A, 0xF07A28, phase / 0.35f)
                    : LerpRgb(0xF07A28, 0x6E1F0A, (phase - 0.35f) / 0.65f);
            // Soft wisps: alpha rides the phase (dense young, fading out),
            // packed in triColor's high byte for the blend raster path.
            float alpha = em.Smoke ? 0.50f - 0.30f * phase : 0.75f - 0.55f * phase;
            int color = (Math.Clamp((int)(alpha * 255f), 0x18, 0xF0) << 24) | rgb;
            // Diamond billboard on per-particle rotated axes — never reads
            // as a box, and the slow spin sells the churn.
            float rot = h1 * (MathF.PI * 2f) + t * (em.Smoke ? 0.6f : 1.7f) * (h2 > 0.5f ? 1f : -1f);
            float cs = MathF.Cos(rot), sn = MathF.Sin(rot);
            var rr = (right * cs + up * sn) * s;
            var uu = (up * cs - right * sn) * s;
            AppendQuad(p - rr, p - uu, p + rr, p + uu, -1, color, 0u,
                verts, normals, uvs, triTex, triColor, pickGuid, pickScid);
        }
    }

    private static float Hash01(uint x)
    {
        x ^= x >> 16; x *= 2654435761u; x ^= x >> 13; x *= 2246822519u; x ^= x >> 16;
        return (x & 0xFFFFFF) / 16777216f;
    }

    private static int LerpRgb(int a, int b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        int ar = (a >> 16) & 0xFF, ag = (a >> 8) & 0xFF, ab = a & 0xFF;
        int br = (b >> 16) & 0xFF, bg = (b >> 8) & 0xFF, bb = b & 0xFF;
        int r = ar + (int)((br - ar) * t), g = ag + (int)((bg - ag) * t), c = ab + (int)((bb - ab) * t);
        return (r << 16) | (g << 8) | c;
    }

    /// <summary>Selection highlight: lerp the packed 0xRRGGBB ~45% toward white,
    /// so a selected marker stays recognisably its type colour, just lit.</summary>
    private static int Brighten(int rgb)
    {
        if (rgb < 0) rgb = 0xB0A890; // default beige
        int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        r += (255 - r) * 45 / 100; g += (255 - g) * 45 / 100; b += (255 - b) * 45 / 100;
        return (r << 16) | (g << 8) | b;
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

    /// <summary>Appends a small flat-shaded cube at <paramref name="world"/>'s origin — a marker for a
    /// placeable that has no mesh of its own (emitter/trigger/command/decal/light) or whose mesh didn't
    /// resolve. <paramref name="color"/> (packed 0xRRGGBB, -1 = default beige) colour-codes it by type;
    /// <paramref name="scid"/> makes it grabbable/selectable like any placed object. Marker tris carry
    /// pickGuid 0 (NOT the anchor node) so the drag projection — which targets the node's terrain —
    /// can never hit the marker's own faces (the old self-hit made dragged markers jitter/teleport).</summary>
    private static void AppendMarkerCube(Matrix4x4 world, float size,
        List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
        List<int> triTex, List<int> triColor, List<uint> pickGuid, List<uint> pickScid,
        uint scid, int color,
        ref Vector3 min, ref Vector3 max)
    {
        for (int i = 0; i < CubeTris.Length; i += 3)
        {
            for (int e = 0; e < 3; e++)
            {
                var p = Vector3.Transform(CubeCorners[CubeTris[i + e]] * size, world);
                verts.Add(p);
                normals.Add(Vector3.Zero);
                uvs.Add(Vector2.Zero);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            triTex.Add(-1);
            triColor.Add(color);
            pickGuid.Add(0u);
            pickScid.Add(scid);
        }
    }

    // Editor marker palette — every placeable reads at a glance instead of an anonymous beige box.
    // Selection brightens the type colour (see Brighten) rather than swapping to an accent, so a
    // selected fire emitter still reads as fire.
    internal const int MarkerObject   = 0xC9A063;
    internal const int MarkerFire     = 0xF07A28;
    internal const int MarkerSmoke    = 0xBFBFC8;
    internal const int MarkerTrigger  = 0x33C2A6;
    internal const int MarkerCommand  = 0x4C8DF0;
    internal const int MarkerDecal    = 0xD8A24B;
    internal const int MarkerLight    = 0xF2D24C;

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

    /// <summary>Middle-drag spin: turns the scene about its vertical axis — a turntable, like a planet
    /// on its axis. Yaw only, so it never tilts (unlike left-drag orbit, which also pitches).</summary>
    public void Spin(double dx)
    {
        _yaw += (float)dx * 0.01f;
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
            _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy, _ortho);
        if (guid == 0) return false;
        if (_selectedNode?.Guid != guid) SelectNode(guid);
        return true;
    }

    /// <summary>Grabs whatever is under the cursor — a placed object OR any node-anchored marker (emitter,
    /// trigger, command, decal, point light) — selecting it so a drag moves it. Returns true on a hit, so
    /// the caller drags instead of orbiting. A good level editor lets you grab every piece in the scene.</summary>
    public bool TryGrabObject(double sx, double sy)
    {
        if (_pickVerts.Length < 3) return false;
        uint scid = SoftwareRenderer.PickTriangle(_pickVerts, _pickScid, _vw, _vh,
            _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy, _ortho);
        if (scid == 0) return false;

        _dragEffect = null;
        foreach (var r in PlacedObjects)
            if (r.Scid == scid)
            {
                SelectedPlacedObject = r;
                var model = _objects.Find(x => x.Scid == scid);
                KeepOrClearMulti(model);
                PushUndo();
                if (model is not null) BeginRotateGesture(model);
                return true;
            }

        if (_markers.TryGetValue(scid, out var item))
        {
            SelectMarker(item);
            _dragEffect = item;
            KeepOrClearMulti(item);
            PushUndo(); // one undo entry per grab covers the whole move gesture
            BeginRotateGesture(item);
            return true;
        }
        return false;
    }

    /// <summary>Selects a marker in its own editor panel/list, so clicking it in the viewport and picking it
    /// from the side list are the same selection. Setting the matching kind and nulling the rest means
    /// exactly one piece is highlighted/edited at a time.</summary>
    private void SelectMarker(object item)
    {
        SelectedEmitter = item as RegionEmitter;
        SelectedTrigger = item as RegionTrigger;
        SelectedCommand = item as CommandPlacement;
        SelectedDecal   = item as RegionDecal;
        SelectedLight   = item as AuthoredLight;
        Render(); // reflect the selection highlight even on a click without a drag
    }

    private static uint EffectNode(object item) => item switch
    {
        RegionEmitter em => em.NodeGuid,
        RegionTrigger tg => tg.NodeGuid,
        CommandPlacement cm => cm.NodeGuid,
        RegionDecal dc => dc.NodeGuid,
        AuthoredLight pl => pl.NodeGuid,
        _ => 0u,
    };

    private void SetEffectPos(object item, Vector3 p)
    {
        switch (item)
        {
            case RegionEmitter em: em.LocalPos = p; RaiseEmitterProps(); break;
            case RegionTrigger tg: tg.LocalPos = p; break;
            case CommandPlacement cm: cm.LocalPos = p; break;
            case RegionDecal dc: dc.OriginLocal = p; break;
            case AuthoredLight pl: pl.Position = p; break;
        }
    }

    /// <summary>Slides the selected piece (placed object or marker) along its node's surface to follow the
    /// cursor.</summary>
    public void MoveSelectedObject(double sx, double sy)
    {
        if (_dragEffect is not null)
        {
            uint node = EffectNode(_dragEffect);
            if (!_nodeWorld.TryGetValue(node, out var nw) || !Matrix4x4.Invert(nw, out var inv)) return;
            if (!SoftwareRenderer.PickPoint(_pickVerts, _pickGuid, node, _vw, _vh,
                    _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy, out var worldHit, _ortho)) return;
            var before = Vector3.Transform(GetPieceLocal(_dragEffect), nw);
            SetEffectPos(_dragEffect, Vector3.Transform(worldHit, inv));
            ApplyGroupDelta(_dragEffect, worldHit - before); // ED-1b — the set moves as one
            Render();
            return;
        }
        if (_selectedPlacedObject is null) return;
        var o = _objects.Find(x => x.Scid == _selectedPlacedObject.Scid);
        if (o is null) return;
        if (!_nodeWorld.TryGetValue(o.NodeGuid, out var nw2) || !Matrix4x4.Invert(nw2, out var inv2)) return;
        if (!SoftwareRenderer.PickPoint(_pickVerts, _pickGuid, o.NodeGuid, _vw, _vh,
                _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy, out var worldHit2, _ortho)) return;
        var before2 = Vector3.Transform(o.LocalPos, nw2);
        o.LocalPos = Vector3.Transform(worldHit2, inv2);
        ApplyGroupDelta(o, worldHit2 - before2); // ED-1b — the set moves as one
        RaiseObjTransform();
        Render();
    }

    /// <summary>Shift-drag: spins the grabbed object about its vertical axis. ED-1b — as a GESTURE
    /// recomputed from grab-time state: a lone object yaws in place (15°-snapped when the toolbar
    /// toggle is on), a multi-selection orbits its centroid while each object also spins. A lone
    /// marker has no orientation, so single-marker rotation stays a no-op.</summary>
    public void RotateSelectedObject(double dx)
    {
        if (_rotStart is not { Count: > 0 } start) return;
        if (start.Count == 1 && start[0].Item is not PlacedObject) return;
        _rotAccum += (float)dx * 0.02f;
        float ang = _rotAccum;
        if (_snapAngle)
        {
            const float step = 15f * MathF.PI / 180f;
            ang = MathF.Round(ang / step) * step;
        }
        var rot = Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), ang);
        bool group = start.Count > 1;
        foreach (var (item, orient0, world0) in start)
        {
            if (item is PlacedObject o) o.Orientation = Quaternion.Normalize(rot * orient0);
            if (group)
            {
                if (!_nodeWorld.TryGetValue(PieceNode(item), out var nw) || !Matrix4x4.Invert(nw, out var inv)) continue;
                SetPieceLocal(item, Vector3.Transform(_rotCentroid + Vector3.Transform(world0 - _rotCentroid, rot), inv));
            }
        }
        RaiseObjTransform();
        Render();
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
        PlaceObjectCommand.RaiseCanExecuteChanged();
        DeleteObjectCommand.RaiseCanExecuteChanged();
        AddDirectionalLightCommand.RaiseCanExecuteChanged();
        AddPointLightCommand.RaiseCanExecuteChanged();
        DeleteLightCommand.RaiseCanExecuteChanged();
        AddFireEmitterCommand.RaiseCanExecuteChanged();
        AddSmokeEmitterCommand.RaiseCanExecuteChanged();
        DeleteEmitterCommand.RaiseCanExecuteChanged();
        AddDecalCommand.RaiseCanExecuteChanged();
        DeleteDecalCommand.RaiseCanExecuteChanged();
        AddSoundCommand.RaiseCanExecuteChanged();
        AddTriggerCommand.RaiseCanExecuteChanged();
        DeleteTriggerCommand.RaiseCanExecuteChanged();
        AddTriggerRowCommand.RaiseCanExecuteChanged();
        DeleteTriggerRowCommand.RaiseCanExecuteChanged();
        AddConditionCommand.RaiseCanExecuteChanged();
        DeleteConditionCommand.RaiseCanExecuteChanged();
        AddActionCommand.RaiseCanExecuteChanged();
        DeleteActionCommand.RaiseCanExecuteChanged();
        AddCommandCommand.RaiseCanExecuteChanged();
        DeleteCommandCommand.RaiseCanExecuteChanged();
        AddConversationCommand.RaiseCanExecuteChanged();
        DeleteConversationCommand.RaiseCanExecuteChanged();
        AddDialogueCommand.RaiseCanExecuteChanged();
        DeleteDialogueCommand.RaiseCanExecuteChanged();
        BindConversationCommand.RaiseCanExecuteChanged();
        ImportSiblingCommand.RaiseCanExecuteChanged();
        RemoveSiblingCommand.RaiseCanExecuteChanged();
        CreateStitchCommand.RaiseCanExecuteChanged();
        DeleteStitchCommand.RaiseCanExecuteChanged();
        AddNavFlagCommand.RaiseCanExecuteChanged();
        DeleteNavFlagCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        _fxTimer.Stop();
        _autosaveTimer.Stop();
        _catalog?.Dispose();
        _textures?.Dispose();
        _props?.Dispose();
        _asp?.Dispose();
    }
}

/// <summary>A row in the placed-node list.</summary>
/// <summary>SC-UX1 — one Scene Outliner group: a fixed header + colour dot
/// over one of the builder's live collections.</summary>
public sealed class OutlineGroup : System.ComponentModel.INotifyPropertyChanged
{
    public OutlineGroup(string header, System.Collections.IEnumerable items, string dot, bool canHide = false)
    {
        Header = header; Items = items; CanHide = canHide;
        var b = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(dot));
        b.Freeze();
        DotBrush = b;
    }
    public string Header { get; }
    public System.Collections.IEnumerable Items { get; }
    public System.Windows.Media.Brush DotBrush { get; }

    // ED-9 — per-family visibility eye. Hidden families neither render nor
    // pick; only spatial families offer the toggle (CanHide).
    public bool CanHide { get; }
    private bool _visible = true;
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Visible)));
            VisibilityChanged?.Invoke();
        }
    }
    public System.Action? VisibilityChanged;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

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

/// <summary>A row in the placed-objects list.</summary>
public sealed record PlacedObjectRow(uint Scid, string Template, uint NodeGuid, bool IsActor = false)
{
    public string Label => Template;
    public string Detail => $"{(IsActor ? "actor" : "prop")} · 0x{Scid:X8} · node 0x{NodeGuid:X8}";
}

/// <summary>A region box in the world-graph canvas. <see cref="X"/>/<see cref="Y"/> is its top-left on
/// the canvas; colour is driven by <see cref="Primary"/> / <see cref="Reachable"/>.</summary>
public sealed record WorldGraphNode(string Name, string Detail, double X, double Y, bool Primary, bool Reachable)
{
    public double CenterX => X + 55;
    public double CenterY => Y + 20;
}

/// <summary>A stitch edge in the world-graph canvas (region centre → region centre). Dangling edges
/// (no reciprocal) render in the warning colour.</summary>
public sealed record WorldGraphEdge(double X1, double Y1, double X2, double Y2, bool Dangling);
