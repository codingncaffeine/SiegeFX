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
    public PropTemplate? SelectedProp { get => _selectedProp; set { if (SetProperty(ref _selectedProp, value)) RaiseCommands(); } }

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
            RefreshPropPalette();
        }
    }
    public string PlacingModeLabel => _placingActors ? "Mode: Actors (NPCs)" : "Mode: Props (scenery)";
    public string PaletteHint => _placingActors
        ? "Actors — NPCs & monsters (animate in-engine), placed into actor.gas."
        : "Props — inert scenery, placed into non_interactive.gas.";
    public ObservableCollection<PlacedObjectRow> PlacedObjects { get; } = new();
    private PlacedObjectRow? _selectedPlacedObject;
    public PlacedObjectRow? SelectedPlacedObject { get => _selectedPlacedObject; set { if (SetProperty(ref _selectedPlacedObject, value)) RaiseCommands(); } }

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
    public string[] QuestKeys => QuestCatalogKeys.Keys;
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

    public WorldBuilderViewModel(IReadOnlyList<string> tankPaths)
    {
        _tankPaths = tankPaths;
        TexturedCommand = new RelayCommand(_ => Textured = !Textured);
        SetAssetsFolderCommand = new RelayCommand(_ => SetAssetsFolder());
        OpenAssetsFolderCommand = new RelayCommand(_ => OpenAssetsFolder(), _ => _assetsFolder is not null);
        ImportObjMeshCommand = new RelayCommand(_ => ImportObjMesh());
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
        DeleteTriggerCommand = new RelayCommand(_ => { if (_selectedTrigger is not null) { Triggers.Remove(_selectedTrigger); SelectedTrigger = null; Render(); } }, _ => _selectedTrigger is not null);
        AddTriggerRowCommand = new RelayCommand(_ => { if (_selectedTrigger is not null) { var r = NewTriggerRow(); _selectedTrigger.Rows.Add(r); SelectedTriggerRow = r; } }, _ => _selectedTrigger is not null);
        DeleteTriggerRowCommand = new RelayCommand(_ => { if (_selectedTrigger is not null && _selectedTriggerRow is not null) { _selectedTrigger.Rows.Remove(_selectedTriggerRow); SelectedTriggerRow = _selectedTrigger.Rows.Count > 0 ? _selectedTrigger.Rows[0] : null; } }, _ => _selectedTriggerRow is not null);
        AddConditionCommand = new RelayCommand(_ => { if (_selectedTriggerRow is not null) { var c = new TriggerCall { Verb = RegionTrigger.Conditions[0] }; _selectedTriggerRow.Conditions.Add(c); SelectedCondition = c; } }, _ => _selectedTriggerRow is not null);
        DeleteConditionCommand = new RelayCommand(_ => { if (_selectedTriggerRow is not null && _selectedCondition is not null) { _selectedTriggerRow.Conditions.Remove(_selectedCondition); SelectedCondition = _selectedTriggerRow.Conditions.Count > 0 ? _selectedTriggerRow.Conditions[0] : null; } }, _ => _selectedCondition is not null);
        AddActionCommand = new RelayCommand(_ => { if (_selectedTriggerRow is not null) { var a = new TriggerCall { Verb = RegionTrigger.Actions[0] }; _selectedTriggerRow.Actions.Add(a); SelectedAction = a; } }, _ => _selectedTriggerRow is not null);
        DeleteActionCommand = new RelayCommand(_ => { if (_selectedTriggerRow is not null && _selectedAction is not null) { _selectedTriggerRow.Actions.Remove(_selectedAction); SelectedAction = _selectedTriggerRow.Actions.Count > 0 ? _selectedTriggerRow.Actions[0] : null; } }, _ => _selectedAction is not null);
        AddCommandCommand = new RelayCommand(_ => AddCommand(), _ => IsReady && _selectedNode is not null);
        DeleteCommandCommand = new RelayCommand(_ => { if (_selectedCommand is not null) { Commands.Remove(_selectedCommand); SelectedCommand = null; Render(); } }, _ => _selectedCommand is not null);
        AddConversationCommand = new RelayCommand(_ => AddConversation(), _ => IsReady);
        DeleteConversationCommand = new RelayCommand(_ => { if (_selectedConversation is not null) { Conversations.Remove(_selectedConversation); SelectedConversation = null; } }, _ => _selectedConversation is not null);
        AddDialogueCommand = new RelayCommand(_ => AddDialogue(), _ => _selectedConversation is not null);
        DeleteDialogueCommand = new RelayCommand(_ => { if (_selectedConversation is not null && _selectedDialogue is not null) { _selectedConversation.Nodes.Remove(_selectedDialogue); SelectedDialogue = _selectedConversation.Nodes.Count > 0 ? _selectedConversation.Nodes[0] : null; } }, _ => _selectedDialogue is not null);
        BindConversationCommand = new RelayCommand(_ => BindConversation(), _ => _selectedConversation is not null && _selectedPlacedObject is not null);
        ImportSiblingCommand = new RelayCommand(_ => ImportSibling());
        RemoveSiblingCommand = new RelayCommand(_ => RemoveSibling(), _ => _selectedSibling is not null);
        CreateStitchCommand = new RelayCommand(_ => CreateStitch(), _ => _selectedPrimaryDoor is not null && _selectedSibling is not null && _selectedSiblingDoor is not null);
        DeleteStitchCommand = new RelayCommand(_ => DeleteStitch(), _ => _selectedStitch is not null);
        AddNavFlagCommand = new RelayCommand(_ => AddNavFlag(), _ => IsReady && _selectedNode is not null);
        DeleteNavFlagCommand = new RelayCommand(_ => { if (_selectedFlag is not null) { LogicalFlags.Remove(_selectedFlag); SelectedFlag = null; } }, _ => _selectedFlag is not null);
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
    private const string SnapSep = "\nOBJECTS\n"; // separates the nodes.gas half from the placed-objects half
    private string Snapshot() => (IsEmpty ? "" : NodesGasWriter.Write(_region)) + SnapSep + SerializeObjects();

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
              .Append(o.File).Append('\n');
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
        int sep = snap.IndexOf(SnapSep, StringComparison.Ordinal);
        string nodesPart = sep < 0 ? snap : snap[..sep];
        string objPart = sep < 0 ? "" : snap[(sep + SnapSep.Length)..];
        var region = nodesPart.Length > 0 ? NodesGasReader.Read(GasDocument.Parse(nodesPart)) : new BuilderRegion();
        ReplaceRegion(region);
        DeserializeObjects(objPart);
        RebuildPlacedRows();
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
            var pkg = MapPackager.PackStartableMap(nodesGas, MapName, RegionName, outDir, BuildStartInfo(),
                assetsRoot: _assetsFolder, placements: _objects,
                lights: new List<AuthoredLight>(Lights), mood: BuildMood(),
                emitters: new List<RegionEmitter>(Emitters), decals: new List<RegionDecal>(Decals),
                triggers: new List<RegionTrigger>(Triggers), commands: new List<CommandPlacement>(Commands),
                conversations: new List<Conversation>(Conversations),
                sourceGuid: PrimaryStitches.Count > 0 ? PrimarySourceGuid() : 0,
                stitches: new List<RegionStitch>(PrimaryStitches), siblings: new List<StitchRegionRef>(Siblings),
                logicalFlags: new List<LogicalFlag>(LogicalFlags));
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
                logicalFlags: new List<LogicalFlag>(LogicalFlags));
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
            int noTex = 0;
            foreach (var d in Decals) if (string.IsNullOrWhiteSpace(d.Texture)) noTex++;
            rows.Add(new ValidationRow(noTex == 0,
                noTex == 0 ? $"{Decals.Count} decal(s) → decals.gas." : $"{noTex} decal(s) missing a texture name."));
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
        if (!_moodInterior && string.IsNullOrWhiteSpace(_moodAmbient)
            && string.IsNullOrWhiteSpace(_moodStandard) && string.IsNullOrWhiteSpace(_moodBattle))
            return null;
        return new AuthoredMood
        {
            Name = $"map_{LightSanitize(MapName)}_{LightSanitize(RegionName)}_1",
            Interior = _moodInterior,
            Ambient = _moodAmbient, Standard = _moodStandard, Battle = _moodBattle,
        };
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
        Emitters.Remove(_selectedEmitter);
        SelectedEmitter = null;
        Render();
    }

    private void AddDecal()
    {
        if (_selectedNode is null) return;
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
        var c = new Conversation { Key = $"custom_{Conversations.Count + 1}" };
        c.Nodes.Add(new DialogueLine { Order = 1, ScreenText = "Hello, traveler." });
        Conversations.Add(c);
        SelectedConversation = c;
        Status = "Added a conversation. Bind it to a placed actor, then add dialogue lines.";
    }

    private void AddDialogue()
    {
        if (_selectedConversation is null) return;
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
        Status = $"Custom assets: {MapPackager.CountAssets(folder):N0} file(s) will bundle into the map.";
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
        foreach (var o in _objects)
        {
            if (!layout.TryGetTransform(o.NodeGuid, out var nodeWorld)) continue;

            var world = Matrix4x4.CreateFromQuaternion(o.Orientation)
                      * Matrix4x4.CreateTranslation(o.LocalPos)
                      * nodeWorld;

            AspMesh? mesh = _templateModel.TryGetValue(o.Template, out var model) ? _asp?.Resolve(model) : null;
            if (mesh is null || mesh.TriangleIndices.Length < 3)
            {
                // No mesh resolved — draw a marker cube so the placement is always visible + grabbable.
                AppendMarkerCube(world, MarkerSize(_radius), verts, normals, uvs, triTex, triColor, pickGuid, pickScid,
                    o.Scid, -1, ref min, ref max);
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
        }

        // Every node-anchored placeable that has no mesh of its own previews as a colour-coded, grabbable
        // marker cube — particles/logic/decals don't render in the software renderer, but you can still see,
        // select, and drag each one. Selected marker uses the accent colour so you can tell what you're editing.
        void Marker(uint nodeGuid, Vector3 localPos, uint scid, int color, object item)
        {
            if (!layout.TryGetTransform(nodeGuid, out var nw)) return;
            var w = Matrix4x4.CreateTranslation(localPos) * nw;
            bool sel = ReferenceEquals(item, _selectedEmitter) || ReferenceEquals(item, _selectedTrigger)
                    || ReferenceEquals(item, _selectedCommand) || ReferenceEquals(item, _selectedDecal)
                    || ReferenceEquals(item, _selectedLight);
            // Selection brightens the TYPE colour instead of replacing it —
            // a selected fire emitter still reads as fire, just lit up.
            AppendMarkerCube(w, MarkerSize(_radius), verts, normals, uvs, triTex, triColor, pickGuid, pickScid,
                scid, sel ? Brighten(color) : color, ref min, ref max);
            if (scid != 0) _markers[scid] = item;
        }

        foreach (var em in Emitters)
            Marker(em.NodeGuid, em.LocalPos, em.Scid, em.Smoke ? MarkerSmoke : MarkerFire, em);
        foreach (var tg in Triggers)
            Marker(tg.NodeGuid, tg.LocalPos, tg.Scid, MarkerTrigger, tg);
        foreach (var dc in Decals)
            Marker(dc.NodeGuid, dc.OriginLocal, dc.Scid, MarkerDecal, dc);
        foreach (var pl in Lights)
            if (pl.Kind == AuthoredLightKind.Point)
                Marker(pl.NodeGuid, pl.Position, pl.Scid, MarkerLight, pl);
        foreach (var cm in Commands)
            Marker(cm.NodeGuid, cm.LocalPos, cm.Scid, MarkerCommand, cm);

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
        var bgra = useTex
            ? SoftwareRenderer.RenderTextured(verts.ToArray(), normals.ToArray(), uvs.ToArray(), triTex.ToArray(), texList.ToArray(),
                _vw, _vh, _center + _pan, _radius, _yaw, _pitch, _dist, lights, triColorArr)
            : SoftwareRenderer.Render(verts.ToArray(), normals.ToArray(), _vw, _vh,
                _center + _pan, _radius, _yaw, _pitch, _dist, _wireframe, lights, triColorArr);
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
            _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy);
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
            _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy);
        if (scid == 0) return false;

        _dragEffect = null;
        foreach (var r in PlacedObjects)
            if (r.Scid == scid) { SelectedPlacedObject = r; PushUndo(); return true; }

        if (_markers.TryGetValue(scid, out var item))
        {
            SelectMarker(item);
            _dragEffect = item;
            PushUndo(); // one undo entry per grab covers the whole move gesture
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
                    _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy, out var worldHit)) return;
            SetEffectPos(_dragEffect, Vector3.Transform(worldHit, inv));
            Render();
            return;
        }
        if (_selectedPlacedObject is null) return;
        var o = _objects.Find(x => x.Scid == _selectedPlacedObject.Scid);
        if (o is null) return;
        if (!_nodeWorld.TryGetValue(o.NodeGuid, out var nw2) || !Matrix4x4.Invert(nw2, out var inv2)) return;
        if (!SoftwareRenderer.PickPoint(_pickVerts, _pickGuid, o.NodeGuid, _vw, _vh,
                _center + _pan, _radius, _yaw, _pitch, _dist, sx, sy, out var worldHit2)) return;
        o.LocalPos = Vector3.Transform(worldHit2, inv2);
        Render();
    }

    /// <summary>Spins the selected object about its vertical axis (yaw). Shift-drag while moving. Markers
    /// have no orientation, so this is a no-op for them.</summary>
    public void RotateSelectedObject(double dx)
    {
        if (_dragEffect is not null) return;
        if (_selectedPlacedObject is null) return;
        var o = _objects.Find(x => x.Scid == _selectedPlacedObject.Scid);
        if (o is null) return;
        o.Orientation = Quaternion.Normalize(
            Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), (float)dx * 0.02f) * o.Orientation);
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
        _catalog?.Dispose();
        _textures?.Dispose();
        _props?.Dispose();
        _asp?.Dispose();
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
