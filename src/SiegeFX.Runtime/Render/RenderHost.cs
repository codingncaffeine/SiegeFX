using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Skrit;
using SiegeFX.Core.Tank;
using SiegeFX.Runtime.Render.Hud;

namespace SiegeFX.Runtime.Render;

/// <summary>
/// Owns the window, GL context, input binding, camera, and render loop.
/// Phase 3 just draws a reference grid so WASD + mouse-look can be verified;
/// later phases will hang real scene objects off this.
/// </summary>
public sealed class RenderHost : IDisposable
{
    private readonly IWindow _window;
    private readonly string? _meshPath;
    private readonly string? _texturePath;
    private readonly string? _regionMapTankPath;
    private readonly string? _regionTerrainTankPath;
    private readonly string? _regionPath;
    private readonly string? _worldMapTankPath;
    private readonly string? _worldTerrainTankPath;
    private readonly string? _worldRootHint;
    private readonly string? _animAspPath;
    private readonly string? _animPrsPath;
    private readonly string? _animTexturePath;
    private readonly string? _skritPath;
    private readonly IReadOnlyList<string>? _skritClipPaths;
    private readonly string? _playLogicTankPath;
    private readonly string? _playObjectsTankPath;

    // Phase 10e — play-region mode. Populated by LoadPlayActors from a spawned ActorSpawner;
    // OnUpdate ticks the runtime + drains the bus each 20 Hz step, OnRender issues a skinned
    // draw per actor with its own animTime + skrit-chosen clip. The region-layout stash
    // below keeps LoadRegion's node transforms available so actor node-anchored positions
    // compose against the same world frame as the terrain.
    private RegionLayout? _regionLayout;
    private readonly List<ActorRenderState> _actors = new();
    private readonly Dictionary<AspMesh, SkinnedMesh> _actorMeshCache = new();
    // Bind-pose bone array cached per unique mesh — reused every frame for zero-clip actors
    // so the hot render loop doesn't allocate 181× Matrix4x4[BoneCount] under GC.
    private readonly Dictionary<AspMesh, Matrix4x4[]> _actorIdentityBones = new();
    private SkritRuntime? _actorRuntime;
    private SiegeFX.Core.Actors.WorldMessageBus? _actorBus;
    // Phase 12d — kept so DebugAttackNearestActor can build a LootTable for the
    // dying actor's template chain on demand. Loot tables live in Core and don't
    // require the render-side resolver, so the store reference is enough.
    private SiegeFX.Core.Assets.TemplateStore? _templateStore;
    private SiegeFX.Core.Assets.FormulasStore? _formulas;
    // Phase 16d — player XP/level state. Created right after the PC spawns once
    // _formulas is also live; null-guarded everywhere because viewer modes that
    // don't TrySpawnPlayer leave both at null and we still want the world to load.
    private SiegeFX.Core.Actors.PlayerProgression? _progression;
    // Phase 16d — seconds the "LEVEL UP!" banner stays on screen after a
    // threshold crossing. Counts down each render frame; >0 means banner
    // is currently being drawn near the top of the HUD.
    private float _levelUpToastRemaining;
    private int _levelUpToastLevel;
    private const float LevelUpToastDuration = 3f;
    // Phase 17a — instant-hit spell catalog (parsed from spl_spell.gas via the
    // template store) and the player's single-slot spellbook. Built after the
    // template store is populated and the player has spawned. Both are null in
    // viewer modes that bypass play-region loading.
    private SiegeFX.Core.Assets.SpellCatalog? _spellCatalog;
    private SiegeFX.Core.Actors.PlayerSpellbook? _playerSpellbook;
    private const string DefaultPrimarySpellName   = "spell_zap";
    private const string DefaultSecondarySpellName = "spell_healing_wind";
    // Phase 17a — short-lived floating world-anchored text (cast feedback like
    // "ZAP -7", "no mana", "out of range"). One render-frame timer per entry;
    // entries with Remaining<=0 are pruned at the start of OnRender.
    private readonly List<FloatingText> _floatingTexts = new();
    private const float FloatingTextDuration = 1.6f;
    private sealed class FloatingText
    {
        public string Text = "";
        public Vector3 WorldPos;
        public Vector4 Color;
        public float Remaining;
        public float Total;
    }
    // Phase 17b — short-lived projectile glyph traveling source→target. Pure
    // visual feedback so casting feels like more than a popping number; the
    // actual damage was already applied at TryClickToCast time. Drawn in the
    // HUD pass as a head dot + trailing dots projected to screen each frame.
    private readonly List<SpellBolt> _spellBolts = new();
    private const float SpellBoltDuration = 0.30f;
    private const int SpellBoltTrailDots = 5;
    private sealed class SpellBolt
    {
        public Vector3 Source;
        public Vector3 Target;
        public Vector4 Color;
        public float Remaining;
        public float Total;
    }
    // Phase 12e — one entry per dead actor that produced a non-empty loot roll.
    // Drawn as a small untextured cube at Pile.Position using the mesh shader's
    // default beige tint. Phase 14a upgraded the bare Vector3 to a LootPile record
    // so the pickup path can transfer the actual rolled items into PC inventory.
    private readonly List<LootPile> _lootPiles = new();
    private DebugCubeMesh? _lootCube;
    // Phase 14a — PC inventory. Flat list for now; grid wiring lands with the HUD
    // in Phase 15. Populated by auto-pickup when the Farmboy walks within
    // PickupRadius of a pile during the 20 Hz tick.
    private readonly List<SiegeFX.Core.Actors.LootEntry> _playerInventory = new();
    private const float PickupRadius = 1.8f;
    // Phase 14b — PC equipment slots keyed by DS1 slot tag (es_weapon_hand,
    // es_feet, es_spellbook, etc.). Populated at spawn by walking the specializes
    // chain for `[inventory][equipment]`. 14c reads damage_min/max off the
    // weapon template to replace HeroBaselineStats damage.
    private readonly Dictionary<string, string> _playerEquipment = new(StringComparer.OrdinalIgnoreCase);
    // Phase 14d — resolver kept alive past LoadPlayActors so weapon swaps at pickup
    // time can load the new item's ASP + texture on demand. Null outside play-region mode.
    // The resolver holds TankReader handles that wrap the FileStreams below, so the
    // tanks must outlive the resolver — hence they're fields here rather than `using
    // var` locals inside LoadPlayActors (initial try died with ObjectDisposedException
    // the first time a goblin dropped something).
    private SiegeFX.Core.Assets.AssetResolver? _playResolver;
    private SiegeFX.Core.Tank.TankFile? _playMapTank;
    private SiegeFX.Core.Tank.TankFile? _playLogicTank;
    private SiegeFX.Core.Tank.TankFile? _playObjectsTank;
    // Phase 18a — Sound.dsres pinned for the audio engine's clip lifetime.
    // We only read .wav blobs out at LoadPlayActors time, but keeping the
    // tank open is harmless and lets later sub-phases lazy-load extra SFX
    // (hit, death, level-up) without re-opening the file.
    private SiegeFX.Core.Tank.TankFile? _playSoundTank;
    private SiegeFX.Runtime.Audio.AudioEngine? _audio;
    // Clip-id constants — tying RenderHost call sites to the AudioEngine
    // dictionary keys through symbols rather than magic strings keeps a typo
    // in one place from silently disabling a sound.
    const string SfxZapCast         = "spell_zap_cast";
    const string SfxHealingWindCast = "spell_healing_wind_cast";
    // Phase 18b — combat-feedback SFX. Singles + per-group ids. The
    // group ids are what RenderHost calls Play() with; the singles are
    // the underlying variants (registered first, then grouped).
    const string SfxMeleeSwingGroup = "melee_swing";
    const string SfxMeleeHitGroup   = "melee_hit";
    const string SfxMeleeMiss       = "melee_miss";
    const string SfxLevelUp         = "level_up";
    // Death SFX live as direct ids — looked up by template species so
    // we don't grow a death-species enum in the render layer.
    static readonly Dictionary<string, string> SpeciesDeathSfx = new(StringComparer.OrdinalIgnoreCase)
    {
        ["goblin"]      = "die_goblin",
        ["gremal"]      = "die_gremal",
        ["krug"]        = "die_krug_scout",
        ["krug_scout"]  = "die_krug_scout",
        ["krug_dog"]    = "die_krug_dog",
    };
    // Phase 14d — currently-rendered weapon mesh pinned to the PC's weapon_grip bone.
    // Null until TrySpawnPlayer resolves the first equipped weapon. Swapped on every
    // es_weapon_hand change so a pickup upgrade shows up visually.
    private StaticMesh? _weaponMesh;
    private GlTexture? _weaponTexture;
    private int _weaponGripBoneIdx = -1;
    // Phase 14d — inverse bind-pose of the weapon ASP's grip bone (bone 0 for DS1
    // weapons; every corner weights to it). Pre-multiplied into uModel so the
    // weapon's grip bone origin snaps to the player's hand bone. Without this
    // the dagger draws at the weapon ASP's mesh origin, which is nowhere near
    // the grip — blade tip usually, judging by how DS1 weapons are authored.
    private Matrix4x4 _weaponBindInv = Matrix4x4.Identity;

    private sealed record LootPile(Vector3 Position, List<SiegeFX.Core.Actors.LootEntry> Items);
    // Phase 13a — the one player-controlled actor's render state. Null until
    // TrySpawnPlayer succeeds. Also lives inside _actors (rendered + ticked with
    // the NPCs); this field is the named handle for the input layer.
    private ActorRenderState? _player;
    // Phase 13c — NavFollower driving the PC's click-to-move. Separate from any
    // ActorFollower (wander AI) because the player's targets come from user clicks,
    // not random sampling. Null until the first LMB click routes through the nav
    // mesh; once created, ticked alongside NPC followers in the logic-step loop.
    private SiegeFX.Core.Nav.NavFollower? _playerFollower;
    // Region-scope nav mesh stashed from LoadPlayActors so the LMB raycaster can
    // hit-test against it. Null when the region has no nav-floor SNOs (shouldn't
    // happen for playable regions; guarded anyway).
    private SiegeFX.Core.Nav.NavMesh? _navMesh;
    // Persistent XZ unit facing for the PC. Updated only when the player moves,
    // so an idle PC keeps his last heading instead of snapping to +Z each tick.
    private Vector3 _playerFacing = Vector3.UnitZ;
    private double _actorTickAccumulator;

    private sealed class ActorRenderState
    {
        public Actor Actor = null!;
        public SkinnedMesh GlMesh = null!;
        public double AnimTime;
        public int LastClipIndex;

        // Phase 11d — actors that spawn over the nav mesh get a brain (wander + aggro)
        // and roam/chase. Those that land off-mesh (pens inside buildings, props) have
        // Brain=null and just render at their authored spawn pose via CurrentTransform.
        // The brain wraps the wander follower; aggro / chase / swing on top of pure
        // wander landed in Phase 16c.
        public SiegeFX.Core.Actors.ActorBrain? Brain;
        public Matrix4x4 CurrentTransform;

        // Phase 12c — once the actor dies (CurrentLife hits 0), the follower is
        // nulled, the anim accumulator stops advancing, and the last skin matrices
        // effectively freeze the body mid-pose. Phase 12d will swap to chore_die.
        public bool IsDead;

        // Phase 13a — set for the single player character. Distinct from NPCs so
        // the input layer (LMB move, RMB attack — 13c/d) can find and drive this
        // one actor without scanning all 181. Also gates off the random-wander
        // follower: the player stands still until the user clicks somewhere.
        public bool IsPlayer;
    }

    // Phase 9a — skrit-driven animation. The runtime ticks a SkritInstance every logic
    // frame; OnStartChore$ sets the blender's "current anim" index, which we watch via
    // ActorHostBridge.CurrentAnimIndex to pick the PRS clip to play. Replaces the hardcoded
    // single-clip _anim pipeline when --skrit-anim is used.
    private SkritRuntime? _skritRuntime;
    private SkritInstance? _skritInstance;
    private ActorHostBridge? _skritHost;
    private PrsAnimation[]? _skritClips;
    private int _skritCurrentClip = -1;
    private double _skritTickAccumulator;
    private GL? _gl;
    private IInputContext? _input;
    private Shader? _gridShader;
    private Shader? _meshShader;
    private Shader? _skinShader;
    private GridMesh? _grid;
    // Phase 15a — 2D text overlay. Constructed in OnLoad, font wired in
    // LoadPlayActors once _playResolver can serve fonts.gas + the .raw atlas.
    // Drawn after the 3D scene each frame inside its own depth-disabled,
    // alpha-blended pass.
    private TextRenderer? _textRenderer;
    private BarRenderer? _barRenderer;
    private bool _inventoryOpen; // 'I' toggles; rendered above the HUD bars
    private readonly PauseMenu _pauseMenu = new(); // Esc toggles; click "Resume" or "Quit"
    private StaticMesh? _mesh;
    private SnoMesh? _sno;
    private SkinnedMesh? _skinnedMesh;
    private AspMesh? _skinnedAsp;
    private PrsAnimation? _anim;
    private GlTexture? _animTexture;
    private GlTexture? _texture;
    private double _animTime;
    private readonly Dictionary<string, GlTexture> _snoTextures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, SnoMesh> _regionMeshes = new();
    private readonly List<RegionInstance> _regionInstances = new();
    private readonly Camera _camera = new();
    private bool _mouseLookActive;
    private Vector2? _lastMousePos;
    // Phase 13d — RMB does double duty: drag to orbit, tap-click to attack. We
    // latch the screen position at RMB-down and measure pixel drift on MouseUp;
    // below _rmbClickDriftPx it's a click (→ attack), otherwise just end orbit.
    private Vector2 _rmbDownPos;
    private float _rmbDrift;
    private const float RmbClickDriftPx = 4f;

    // Phase 13b — camera modes. Fly is the free RMB+WASD debug cam from earlier
    // phases; Chase locks the camera behind the player with RMB-drag to orbit the
    // yaw around him. Default is Fly so viewer modes without a PC (mesh/region
    // probes) keep their old behavior; TrySpawnPlayer flips to Chase.
    private enum CameraMode { Fly, Chase }
    private CameraMode _cameraMode = CameraMode.Fly;
    // Yaw of the camera *around* the player in Chase mode. Independent from
    // Camera.Yaw because we overwrite Camera.Yaw/Pitch every frame to make Forward
    // point at the player (so DebugAttackNearestActor still picks whatever the
    // camera is aimed at).
    private float _chaseYaw;
    private float _chaseDistance = 12f;
    private float _chaseHeight   = 7f;
    // Head-height offset so the camera looks at the torso/head of the ~5-foot
    // Farmboy model instead of his feet. Rough guess; tune when the real PC
    // camera appears in Phase 14+.
    private const float ChaseLookTargetY = 1.5f;

    private readonly record struct RegionInstance(Matrix4x4 World, SnoMesh Mesh, string TexsetAbbr);

    /// <summary>DS1 SNO surfaces often hold a <c>_xxx_</c> placeholder that per-snode
    /// <c>texset</c> from nodes.gas fills in at load time (e.g. <c>t_xxx_flr_04x04-a</c>
    /// + texset <c>grs01</c> → <c>t_grs01_flr_04x04-a</c>). Matches OpenSiege's
    /// <c>ReaderWriterSNO.cpp</c> behavior — the one missing piece that was leaving
    /// most terrain subsets untextured.</summary>
    private static string ResolveTexName(string raw, string texsetAbbr)
    {
        if (string.IsNullOrEmpty(texsetAbbr)) return raw;
        var i = raw.IndexOf("_xxx_", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? raw : string.Concat(raw.AsSpan(0, i + 1), texsetAbbr, raw.AsSpan(i + 4));
    }

    private const string GridVertexSource = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aColor;
uniform mat4 uViewProj;
out vec3 vColor;
void main()
{
    gl_Position = uViewProj * vec4(aPos, 1.0);
    vColor = aColor;
}";

    private const string GridFragmentSource = @"#version 330 core
in  vec3 vColor;
out vec4 FragColor;
void main() { FragColor = vec4(vColor, 1.0); }";

    private const string MeshVertexSource = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aUv;
uniform mat4 uViewProj;
uniform mat4 uModel;
out vec3 vNormal;
out vec2 vUv;
void main()
{
    gl_Position = uViewProj * uModel * vec4(aPos, 1.0);
    vNormal = mat3(uModel) * aNormal;
    vUv = aUv;
}";

    // Skinning vertex shader. Same per-vertex output contract as MeshVertexSource (vNormal,
    // vUv) so it pairs with MeshFragmentSource unchanged. Bone uniforms are uploaded by the
    // host once per frame from CPU-composed skin matrices (AnimationRuntime.ComputeSkinMatrices).
    // .NET row-vector matrices uploaded with transpose=false flip to column-vector in GL,
    // so `uBones[i] * vec4(aPos, 1.0)` here matches `Vector3.Transform(aPos, skin[i])` on
    // the CPU side. The linear combination of mat4s before applying to aPos is mathematically
    // identical to summing the per-bone-transformed positions weighted by the same weights —
    // one matmul instead of four, which the GPU prefers.
    private const string SkinnedVertexSource = @"#version 330 core
layout (location = 0) in vec3  aPos;
layout (location = 1) in vec3  aNormal;
layout (location = 2) in vec2  aUv;
layout (location = 3) in vec4  aWeights;
layout (location = 4) in uvec4 aBones;
uniform mat4 uViewProj;
uniform mat4 uModel;
uniform mat4 uBones[64];
out vec3 vNormal;
out vec2 vUv;
void main()
{
    mat4 skin = aWeights.x * uBones[aBones.x]
              + aWeights.y * uBones[aBones.y]
              + aWeights.z * uBones[aBones.z]
              + aWeights.w * uBones[aBones.w];
    vec4 sp = skin * vec4(aPos, 1.0);
    gl_Position = uViewProj * uModel * sp;
    // DS1 rigs are rigid (rotation + translation, no non-uniform scale), so mat3(skin) is
    // the correct normal transform. Don't rewrite this as a transpose-inverse: it would be
    // slower, identical for these inputs, and would mask corruption if a future skin ever did
    // carry scale.
    vec3 sn = mat3(skin) * aNormal;
    vNormal  = mat3(uModel) * sn;
    vUv = aUv;
}";

    // Cheap N·L lambert plus a constant ambient. Samples uAlbedo when uHasTexture != 0;
    // otherwise falls back to a neutral sand colour so untextured meshes still read as solids.
    // DS1 textures were authored for D3D (V=0 at top), so we flip V on sample for GL.
    // Vertex colors are parsed (DS1 bakes radiosity into them) but not wired here yet —
    // trying texture*vertexColor made the visible picture darker overall, suggesting the
    // color range isn't a straight [0..1] sRGB tint. Needs a data-look before hooking up.
    private const string MeshFragmentSource = @"#version 330 core
in  vec3 vNormal;
in  vec2 vUv;
out vec4 FragColor;
uniform sampler2D uAlbedo;
uniform int       uHasTexture;
void main()
{
    vec3 L = normalize(vec3(0.4, 0.9, 0.3));
    float ndl = max(dot(normalize(vNormal), L), 0.0);
    vec3 base = (uHasTexture != 0)
        ? texture(uAlbedo, vec2(vUv.x, 1.0 - vUv.y)).rgb
        : vec3(0.85, 0.78, 0.62);
    vec3 lit  = base * (0.25 + 0.75 * ndl);
    FragColor = vec4(lit, 1.0);
}";

    public RenderHost(string title = "SiegeFX", int width = 1280, int height = 720,
        string? meshPath = null, string? texturePath = null,
        string? regionMapTankPath = null, string? regionTerrainTankPath = null, string? regionPath = null,
        string? worldMapTankPath = null, string? worldTerrainTankPath = null, string? worldRootHint = null,
        string? animAspPath = null, string? animPrsPath = null, string? animTexturePath = null,
        string? skritPath = null, IReadOnlyList<string>? skritClipPaths = null,
        string? playLogicTankPath = null, string? playObjectsTankPath = null)
    {
        _meshPath = meshPath;
        _texturePath = texturePath;
        _regionMapTankPath = regionMapTankPath;
        _regionTerrainTankPath = regionTerrainTankPath;
        _regionPath = regionPath;
        _worldMapTankPath = worldMapTankPath;
        _worldTerrainTankPath = worldTerrainTankPath;
        _worldRootHint = worldRootHint;
        _animAspPath = animAspPath;
        _animPrsPath = animPrsPath;
        _animTexturePath = animTexturePath;
        _skritPath = skritPath;
        _skritClipPaths = skritClipPaths;
        _playLogicTankPath = playLogicTankPath;
        _playObjectsTankPath = playObjectsTankPath;
        var opts = WindowOptions.Default with
        {
            Title = title,
            Size = new Vector2D<int>(width, height),
            VSync = true,
        };
        _window = Window.Create(opts);
        _window.Load    += OnLoad;
        _window.Update  += OnUpdate;
        _window.Render  += OnRender;
        _window.Resize  += OnResize;
        // Phase 16b — release GL resources while the context is still alive.
        // Silk.NET's Run() returns after the GLFW window is destroyed, so the
        // outer Dispose() runs with no current context and DeleteTextures
        // throws "NoContext". Closing fires while the window is still valid.
        _window.Closing += OnClosing;
    }

    public void Run() => _window.Run();

    private void OnLoad()
    {
        _gl = GL.GetApi(_window);
        _input = _window.CreateInput();

        foreach (var kb in _input.Keyboards)
            kb.KeyDown += (_, key, _) =>
            {
                // Phase 15d: Esc opens/closes the pause menu instead of slamming the
                // window shut. Quit-from-menu still routes through _window.Close, so
                // there's a one-button-press exit (Esc, click Quit).
                if (key == Key.Escape) _pauseMenu.Toggle();
                // Phase 12c: F key strikes the nearest living actor in front of the
                // camera. Placeholder player-stand-in until Phase 13 brings a real PC
                // with its own template and equipped weapon. Logs each hit to the
                // console so the damage math is visible without a HUD overlay.
                else if (key == Key.F) DebugAttackNearestActor();
                // Phase 13b: C flips between chase cam (follows the PC) and fly cam
                // (free WASD+RMB). No-op if there's no player.
                else if (key == Key.C && _player is not null)
                {
                    _cameraMode = _cameraMode == CameraMode.Chase ? CameraMode.Fly : CameraMode.Chase;
                    Console.WriteLine($"camera: {_cameraMode}");
                }
                // Phase 15c: 'I' toggles the grid inventory panel.
                else if (key == Key.I) _inventoryOpen = !_inventoryOpen;
                // Phase 16b: 'H' takes 5 HP and 5 MP off the player (debug only —
                // until enemy aggro lands in a later phase, this is the only way
                // to drain the bars to verify regen is ticking).
                else if (key == Key.H && _player is not null && !_player.IsDead)
                {
                    _player.Actor.Combat.ApplyDamage(5f);
                    // Mana drain has no helper yet; nudge MP via heal-negative isn't
                    // supported (Heal clamps), so we'll just call the formula path
                    // when spells land. For now drain via a direct method.
                    _player.Actor.Combat.SpendMana(5f);
                }
                // Phase 17a — Q casts the slotted primary spell at whatever the
                // cursor is currently pointing at. Picks the same way RMB does:
                // unproject, find the closest combatant in XZ to the ground hit.
                // The spellbook owns the gating (range, mana, cooldown) — the
                // render layer just supplies the candidate target + distance.
                else if (key == Key.Q && _player is not null && !_player.IsDead)
                {
                    TryClickToCast(SiegeFX.Core.Actors.SpellSlot.Primary);
                }
                // Phase 17c — W casts the secondary slot. Defaults to a self
                // heal so a single key keeps the loop simple; when a real
                // hotbar lands the user picks per-slot bindings.
                else if (key == Key.W && _player is not null && !_player.IsDead)
                {
                    TryClickToCast(SiegeFX.Core.Actors.SpellSlot.Secondary);
                }
            };

        foreach (var mouse in _input.Mice)
        {
            mouse.MouseDown += (m, btn) =>
            {
                // Phase 15d — pause menu eats LMB while it's open so a click on
                // a button doesn't also retarget the follower behind the panel.
                if (_pauseMenu.IsOpen && btn == MouseButton.Left)
                {
                    _pauseMenu.OnMouseDown((int)m.Position.X, (int)m.Position.Y);
                    return;
                }
                if (btn == MouseButton.Right)
                {
                    _mouseLookActive = true;
                    _lastMousePos = null;
                    _rmbDownPos = m.Position;
                    _rmbDrift = 0f;
                    m.Cursor.CursorMode = CursorMode.Raw;
                }
                // Phase 13c — LMB click-to-move. Unproject the cursor onto the
                // player's current Y plane, confirm the point lands on a nav
                // triangle, retarget the follower. Silent no-op on off-mesh clicks
                // (clicks past the nav edge) so the user can rotate-drag + click
                // without stray warnings.
                else if (btn == MouseButton.Left)
                    TryClickToMove(m.Position);
            };
            mouse.MouseUp += (m, btn) =>
            {
                if (_pauseMenu.IsOpen && btn == MouseButton.Left)
                {
                    _pauseMenu.OnMouseUp((int)m.Position.X, (int)m.Position.Y);
                    if (_pauseMenu.QuitRequested) _window.Close();
                    return;
                }
                if (btn == MouseButton.Right)
                {
                    _mouseLookActive = false;
                    _lastMousePos = null;
                    m.Cursor.CursorMode = CursorMode.Normal;
                    // Phase 13d — tap-click discrimination. In Raw cursor mode
                    // m.Position is unreliable on mouse-up (driver snaps it back),
                    // so we use the drift accumulated while RMB was held instead.
                    // A nearly-zero drift means the user tapped without dragging;
                    // anything past the threshold was an orbit gesture.
                    if (_rmbDrift <= RmbClickDriftPx)
                        TryClickToAttack(_rmbDownPos);
                    _rmbDrift = 0f;
                }
            };
            mouse.MouseMove += (_, pos) =>
            {
                // Phase 15d — feed the pause menu so its buttons can light up on
                // hover. Cheap rect tests; no-op when the menu is closed.
                if (_pauseMenu.IsOpen)
                    _pauseMenu.OnMouseMove((int)pos.X, (int)pos.Y);
                if (!_mouseLookActive) return;
                if (_lastMousePos is { } last)
                {
                    float dx = pos.X - last.X;
                    float dy = pos.Y - last.Y;
                    // Phase 13d — accumulate L1 pixel drift so MouseUp can tell a
                    // tap from a drag. L1 (abs sum) instead of L2 so a dead-slow
                    // diagonal scrub still accumulates past the click threshold.
                    _rmbDrift += MathF.Abs(dx) + MathF.Abs(dy);
                    if (_cameraMode == CameraMode.Chase)
                    {
                        // Orbit only (no pitch change) — chase pitch is derived from the
                        // look-at target each frame so mouse-Y would fight that logic.
                        _chaseYaw += dx * _camera.MouseSens;
                    }
                    else
                    {
                        _camera.LookDelta(dx, dy);
                    }
                }
                _lastMousePos = pos;
            };
        }

        _gl.Enable(GLEnum.DepthTest);
        _gl.Enable(GLEnum.CullFace);
        _gl.CullFace(GLEnum.Back);
        _gl.FrontFace(GLEnum.Ccw);
        _gl.ClearColor(0.08f, 0.09f, 0.11f, 1f);

        _gridShader = new Shader(_gl, GridVertexSource, GridFragmentSource);
        _meshShader = new Shader(_gl, MeshVertexSource, MeshFragmentSource);
        _skinShader = new Shader(_gl, SkinnedVertexSource, MeshFragmentSource);
        _grid       = new GridMesh(_gl);
        _lootCube   = new DebugCubeMesh(_gl);
        _textRenderer = new TextRenderer(_gl);
        _barRenderer  = new BarRenderer(_gl);

        if (_meshPath is not null)
        {
            var bytes = File.ReadAllBytes(_meshPath);
            var ext = Path.GetExtension(_meshPath).ToLowerInvariant();
            Vector3 center;
            float radius;

            if (ext == ".sno")
            {
                var sno = SnoModel.Load(bytes);
                _sno = new SnoMesh(_gl, sno);
                center = _sno.Center;
                radius = MathF.Max(_sno.Radius, 1f);
                Console.WriteLine($"loaded SNO v{sno.Version} ({sno.Corners.Length} corners, {sno.TotalTriangleCount} tris across {sno.Surfaces.Length} surfaces, bounds {_sno.Min} .. {_sno.Max})");
            }
            else
            {
                var asp = AspMesh.Load(bytes);
                _mesh = new StaticMesh(_gl, asp);
                center = _mesh.Center;
                radius = MathF.Max(_mesh.Radius, 0.5f);
                Console.WriteLine($"loaded mesh '{asp.MeshName}' ({asp.Positions.Length} v, {asp.TriangleCount} tris, bounds {_mesh.Min} .. {_mesh.Max})");
            }

            // Frame whatever we loaded: put the camera radius*3 back along +Z, looking
            // at the node's center, so something is visible on first paint.
            _camera.Position = center + new Vector3(0, 0, radius * 3f);
            _camera.Yaw = 0;
            _camera.Pitch = 0;
        }

        if (_texturePath is not null)
        {
            var ext = Path.GetExtension(_texturePath).ToLowerInvariant();
            if (ext is ".dsres" or ".dsmap")
            {
                if (_sno is not null) LoadSnoTexturesFromTank(_texturePath);
                else Console.WriteLine($"tank '{_texturePath}' ignored — texture-from-tank resolution only runs for SNO meshes");
            }
            else
            {
                var texBytes = File.ReadAllBytes(_texturePath);
                var raw = RawImage.Load(texBytes);
                _texture = new GlTexture(_gl, raw);
                Console.WriteLine($"loaded texture '{_texturePath}' ({raw.Width}x{raw.Height}, {raw.SurfaceCount} surface(s))");
            }
        }

        if (_regionMapTankPath is not null && _regionTerrainTankPath is not null && _regionPath is not null)
            LoadRegion(_regionMapTankPath, _regionTerrainTankPath, _regionPath);

        if (_worldMapTankPath is not null && _worldTerrainTankPath is not null)
            LoadWorld(_worldMapTankPath, _worldTerrainTankPath, _worldRootHint);

        if (_regionMapTankPath is not null && _playLogicTankPath is not null
            && _playObjectsTankPath is not null && _regionPath is not null)
            LoadPlayActors(_regionMapTankPath, _playLogicTankPath, _playObjectsTankPath, _regionPath);

        if (_animAspPath is not null && _animPrsPath is not null)
            LoadAnim(_animAspPath, _animPrsPath, _animTexturePath);

        if (_animAspPath is not null && _skritPath is not null && _skritClipPaths is not null)
            LoadSkrit(_animAspPath, _skritPath, _skritClipPaths, _animTexturePath);
    }

    /// <summary>Phase 9a entry: load a rigged ASP + a skrit + N PRS clips, then let the
    /// skrit pick which clip plays. The skrit's <c>OnStartChore$</c> handler calls
    /// <c>owner.blender.AddAnimToBlendGroup(idx, w)</c>; we capture idx via
    /// <see cref="ActorHostBridge.CurrentAnimIndex"/> and swap <see cref="_anim"/> to
    /// <paramref name="clipPaths"/>[idx]. Only the first blend slot is honored — proper
    /// multi-slot blending is Phase 10+.</summary>
    private void LoadSkrit(string aspPath, string skritPath, IReadOnlyList<string> clipPaths, string? texturePath)
    {
        if (_gl is null) return;
        if (!File.Exists(skritPath)) { Console.Error.WriteLine($"skrit '{skritPath}' not found"); return; }
        if (clipPaths.Count == 0) { Console.Error.WriteLine("--skrit-anim requires at least one clip"); return; }

        // If LoadAnim didn't already run, stand up the skinned mesh ourselves.
        if (_skinnedAsp is null || _skinnedMesh is null)
        {
            _skinnedAsp = AspMesh.Load(File.ReadAllBytes(aspPath));
            if (!_skinnedAsp.HasSkin) { Console.Error.WriteLine($"asp '{aspPath}' has no WCRN skin data"); _skinnedAsp = null; return; }
            _skinnedMesh = new SkinnedMesh(_gl, _skinnedAsp);
            if (texturePath is not null && File.Exists(texturePath))
                _animTexture = new GlTexture(_gl, RawImage.Load(File.ReadAllBytes(texturePath)));
            _camera.Position = _skinnedMesh.Center + new Vector3(0, 0, MathF.Max(_skinnedMesh.Radius, 0.5f) * 3f);
            _camera.Yaw = 0;
            _camera.Pitch = 0;
        }

        _skritClips = new PrsAnimation[clipPaths.Count];
        for (int i = 0; i < clipPaths.Count; i++)
        {
            if (!File.Exists(clipPaths[i]))
            {
                Console.Error.WriteLine($"clip '{clipPaths[i]}' not found — anim index {i} will fall back to clip 0");
                continue;
            }
            _skritClips[i] = PrsAnimation.Load(File.ReadAllBytes(clipPaths[i]));
        }
        // Guarantee clip 0 is present so CurrentAnimIndex == -1 / unresolved defaults cleanly.
        if (_skritClips[0] is null)
        {
            Console.Error.WriteLine("clip 0 must exist");
            _skritClips = null;
            return;
        }

        // Compile the skrit. Shipped content carries real binder diagnostics (see project
        // memory); surface them but don't abort — the VM can still run the handlers we need.
        var src = File.ReadAllText(skritPath);
        var script = SkritParser.Parse(src);
        var bind = new SkritBinder(script).Bind();
        if (bind.Diagnostics.Count > 0)
        {
            Console.WriteLine($"skrit '{skritPath}': {bind.Diagnostics.Count} bind diagnostic(s):");
            foreach (var d in bind.Diagnostics) Console.WriteLine($"  !! {d}");
        }
        var program = new SkritCompiler(script, bind).Compile();

        _skritHost = new ActorHostBridge(rngSeed: 1) { NumSubAnims = clipPaths.Count };
        _skritRuntime = new SkritRuntime();
        _skritInstance = _skritRuntime.Add(new SkritInstance(program, _skritHost));
        _skritHost.Instance = _skritInstance;
        _skritInstance.Start();
        // Anim skrits conventionally enter through OnStartChore$( subanim, flags ).
        _skritInstance.Dispatch("OnStartChore$", SkritValue.FromInt(0), SkritValue.FromInt(0));

        _skritCurrentClip = Math.Max(0, _skritHost.CurrentAnimIndex);
        _anim = _skritClips[_skritCurrentClip] ?? _skritClips[0];
        _animTime = 0;

        Console.WriteLine($"skrit '{skritPath}' driving animation: state={_skritInstance.CurrentState}, " +
                          $"clip={_skritCurrentClip} ({clipPaths[_skritCurrentClip]}), " +
                          $"blender-log={_skritHost.BlenderLog.Count} call(s)");
    }

    /// <summary>Loads a rigged ASP and a PRS clip, builds a SkinnedMesh, frames the camera,
    /// and (optionally) loads an albedo .raw to bind during draw. The clip just loops at
    /// its native length — this is a viewer, not a state machine.</summary>
    private void LoadAnim(string aspPath, string prsPath, string? texturePath)
    {
        if (_gl is null) return;

        if (!File.Exists(aspPath))
        {
            Console.Error.WriteLine($"asp '{aspPath}' not found");
            return;
        }
        if (!File.Exists(prsPath))
        {
            Console.Error.WriteLine($"prs '{prsPath}' not found");
            return;
        }

        _skinnedAsp = AspMesh.Load(File.ReadAllBytes(aspPath));
        if (!_skinnedAsp.HasSkin)
        {
            Console.Error.WriteLine($"asp '{aspPath}' has no WCRN skin data; --anim requires a rigged mesh");
            _skinnedAsp = null;
            return;
        }
        _anim = PrsAnimation.Load(File.ReadAllBytes(prsPath));
        _skinnedMesh = new SkinnedMesh(_gl, _skinnedAsp);

        if (texturePath is not null)
        {
            if (!File.Exists(texturePath))
            {
                Console.Error.WriteLine($"texture '{texturePath}' not found; rendering untextured");
            }
            else
            {
                var raw = RawImage.Load(File.ReadAllBytes(texturePath));
                _animTexture = new GlTexture(_gl, raw);
            }
        }

        var matched = 0;
        var prsByName = new HashSet<string>(_anim.BoneNames);
        foreach (var n in _skinnedAsp.BoneNames) if (prsByName.Contains(n)) matched++;

        Console.WriteLine($"loaded skinned mesh '{_skinnedAsp.MeshName}' " +
                          $"({_skinnedAsp.Positions.Length} v, {_skinnedAsp.TriangleCount} tris, " +
                          $"{_skinnedAsp.BoneCount} bones, bounds {_skinnedMesh.Min} .. {_skinnedMesh.Max})");
        Console.WriteLine($"loaded clip '{prsPath}' ({_anim.NumBones} bones, length={_anim.AnimLength:F3}s, " +
                          $"{matched}/{_anim.NumBones} bones map to mesh)");

        // Frame the rigged mesh the same way we frame static meshes — bounds×3 back along +Z.
        var center = _skinnedMesh.Center;
        var radius = MathF.Max(_skinnedMesh.Radius, 0.5f);
        _camera.Position = center + new Vector3(0, 0, radius * 3f);
        _camera.Yaw = 0;
        _camera.Pitch = 0;
    }

    /// <summary>Loads every region in the map tank stitched into one world. Shares the
    /// per-mesh / per-texture caches so the 77k-instance MpWorld costs the same in GPU
    /// upload as one region — the bulk of the work is the CPU-side layout build.</summary>
    private void LoadWorld(string mapTankPath, string terrainTankPath, string? rootHint)
    {
        if (_gl is null) return;

        using var mapTank = TankFile.Open(mapTankPath);
        var mapReader = new TankReader(mapTank);
        using var terrainTank = TankFile.Open(terrainTankPath);
        var terrainReader = new TankReader(terrainTank);

        var meshIndex = SnoMeshIndex.Build(terrainReader);
        var modelCache = new Dictionary<uint, SnoModel?>();
        SnoModel? ResolveModel(uint meshGuid)
        {
            if (modelCache.TryGetValue(meshGuid, out var cached)) return cached;
            SnoModel? sno = null;
            if (meshIndex.TryResolve(meshGuid, out var path))
            {
                try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
                catch { sno = null; }
            }
            modelCache[meshGuid] = sno;
            return sno;
        }

        // Enumerate every region, build its graph + layout + (optional) stitch helper.
        var entries = new List<WorldLayout.RegionEntry>();
        var regionGraphs = new Dictionary<string, RegionGraph>();
        foreach (var path in mapReader.ListFiles())
        {
            if (!path.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase)) continue;
            var regionPath = path[..^"/terrain_nodes/nodes.gas".Length];
            try
            {
                var graph = RegionGraph.Load(mapReader.ExtractToMemory(path));
                var layout = RegionLayout.Build(graph, ResolveModel);
                RegionStitchHelper? stitches = null;
                var stitchPath = regionPath + "/editor/stitch_helper.gas";
                if (mapReader.TryGetFile(stitchPath, out _))
                {
                    try { stitches = RegionStitchHelper.Load(mapReader.ExtractToMemory(stitchPath)); }
                    catch { /* a bad stitch file just makes that region un-stitchable */ }
                }
                entries.Add(new WorldLayout.RegionEntry(regionPath, graph, layout, stitches));
                regionGraphs[regionPath] = graph;
            }
            catch
            {
                // Skipping a broken region is better than failing the whole world load.
            }
        }

        var world = WorldLayout.Build(entries, ResolveModel, rootHint);

        // .raw index built once; reused for every subset resolution below.
        var rawIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in terrainReader.ListFiles())
        {
            if (!p.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) continue;
            var bare = Path.GetFileNameWithoutExtension(p);
            if (!rawIndex.ContainsKey(bare)) rawIndex[bare] = p;
        }

        // Materialize each placed snode as a RegionInstance. We need per-instance texsetAbbr
        // (authored per-snode in nodes.gas), so walk region graphs rather than the flat
        // world.Transforms dict directly.
        foreach (var entry in entries)
        {
            if (!world.RegionOffsets.ContainsKey(entry.Path)) continue;
            foreach (var node in entry.Graph.Nodes)
            {
                if (!world.Transforms.TryGetValue(node.Guid, out var worldXf)) continue;
                var model = ResolveModel(node.MeshGuid);
                if (model is null) continue;

                if (!_regionMeshes.TryGetValue(node.MeshGuid, out var mesh))
                {
                    mesh = new SnoMesh(_gl, model);
                    _regionMeshes[node.MeshGuid] = mesh;
                }

                foreach (var subset in mesh.Subsets)
                {
                    if (string.IsNullOrEmpty(subset.TextureName)) continue;
                    var resolved = ResolveTexName(subset.TextureName, node.TexsetAbbr);
                    if (_snoTextures.ContainsKey(resolved)) continue;
                    if (!rawIndex.TryGetValue(resolved, out var texPath)) continue;
                    try
                    {
                        var raw = RawImage.Load(terrainReader.ExtractToMemory(texPath));
                        _snoTextures[resolved] = new GlTexture(_gl, raw);
                    }
                    catch { /* skip */ }
                }

                _regionInstances.Add(new RegionInstance(worldXf, mesh, node.TexsetAbbr));
            }
        }

        // Frame the camera over the root region's origin, lifted so the player sees a
        // decent patch of ground on first paint. World extents span ±1km, so the user
        // will fly a lot — WASD+sprint is the expected traversal.
        if (world.RegionOffsets.TryGetValue(world.RootRegion, out var rootOffset))
        {
            _camera.Position = new Vector3(rootOffset.M41, rootOffset.M42 + 20f, rootOffset.M43 + 40f);
        }
        else
        {
            _camera.Position = new Vector3(0, 20f, 40f);
        }
        _camera.Yaw = 0;
        _camera.Pitch = -0.3f;

        Console.WriteLine($"world '{mapTankPath}': root={world.RootRegion}");
        Console.WriteLine($"  placed {world.PlacedRegionCount}/{entries.Count} region(s), " +
                          $"{_regionInstances.Count:N0} instance(s), " +
                          $"{_regionMeshes.Count:N0} unique SNO(s), {_snoTextures.Count:N0} texture(s)");
        Console.WriteLine($"  unresolved={world.UnresolvedStitchCount} dangling={world.DanglingStitchCount} unreachable={world.UnreachableRegionCount}");
    }

    /// <summary>Loads a full region: opens both tanks, parses the region graph, walks the
    /// door graph to get world-space transforms, resolves and uploads each unique SNO + its
    /// textures once, then enqueues one <see cref="RegionInstance"/> per placed snode.</summary>
    private void LoadRegion(string mapTankPath, string terrainTankPath, string regionPath)
    {
        if (_gl is null) return;

        var normalized = regionPath.Replace('\\', '/');
        if (!normalized.StartsWith('/')) normalized = "/" + normalized;
        if (normalized.EndsWith('/')) normalized = normalized[..^1];

        using var mapTank = TankFile.Open(mapTankPath);
        var mapReader = new TankReader(mapTank);
        using var terrainTank = TankFile.Open(terrainTankPath);
        var terrainReader = new TankReader(terrainTank);

        var graph = RegionGraph.Load(mapReader.ExtractToMemory(normalized + "/terrain_nodes/nodes.gas"));
        var meshIndex = SnoMeshIndex.Build(terrainReader);

        // Two caches: SnoModel (Core-side) per mesh_guid and SnoMesh (GL-side) per mesh_guid.
        // The model cache lets RegionLayout.Build read door transforms; the mesh cache lets
        // us upload each unique SNO exactly once regardless of how many instances use it.
        var modelCache = new Dictionary<uint, SnoModel?>();
        SnoModel? ResolveModel(uint meshGuid)
        {
            if (modelCache.TryGetValue(meshGuid, out var cached)) return cached;
            SnoModel? sno = null;
            if (meshIndex.TryResolve(meshGuid, out var path))
            {
                try { sno = SnoModel.Load(terrainReader.ExtractToMemory(path)); }
                catch { sno = null; }
            }
            modelCache[meshGuid] = sno;
            return sno;
        }

        var layout = RegionLayout.Build(graph, ResolveModel);
        _regionLayout = layout;

        // Build a bare-filename → full-path .raw index on demand (same pattern as
        // LoadSnoTexturesFromTank), used every time a subset's texture is first seen.
        var rawIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in terrainReader.ListFiles())
        {
            if (!p.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) continue;
            var bare = Path.GetFileNameWithoutExtension(p);
            if (!rawIndex.ContainsKey(bare)) rawIndex[bare] = p;
        }

        foreach (var node in graph.Nodes)
        {
            if (!layout.TryGetTransform(node.Guid, out var world)) continue;
            var model = ResolveModel(node.MeshGuid);
            if (model is null) continue;

            if (!_regionMeshes.TryGetValue(node.MeshGuid, out var mesh))
            {
                mesh = new SnoMesh(_gl, model);
                _regionMeshes[node.MeshGuid] = mesh;
            }

            // Load textures per (subset, node.texset). Different instances of the same
            // SNO can use different texsets, so keying by resolved name lets one mesh
            // appear under multiple palettes without reloading raws we already have.
            foreach (var subset in mesh.Subsets)
            {
                if (string.IsNullOrEmpty(subset.TextureName)) continue;
                var resolved = ResolveTexName(subset.TextureName, node.TexsetAbbr);
                if (_snoTextures.ContainsKey(resolved)) continue;
                if (!rawIndex.TryGetValue(resolved, out var texPath)) continue;
                try
                {
                    var raw = RawImage.Load(terrainReader.ExtractToMemory(texPath));
                    _snoTextures[resolved] = new GlTexture(_gl, raw);
                }
                catch { /* skip unreadable textures; subset falls back to uHasTexture=0 */ }
            }

            _regionInstances.Add(new RegionInstance(world, mesh, node.TexsetAbbr));
        }

        // Frame the camera on the anchor: pull back along +Z by ~3× the anchor SNO's
        // bounding radius and lift by ~1× so the tile is comfortably visible on first
        // paint. A full region-bounds pass is overkill here — the user flies from there.
        var anchorRadius = 8f;
        if (graph.TryGetNode(layout.AnchorGuid, out var anchorNode) &&
            _regionMeshes.TryGetValue(anchorNode.MeshGuid, out var anchorMesh))
        {
            anchorRadius = MathF.Max(anchorMesh.Radius, 1f);
        }
        _camera.Position = new Vector3(0, anchorRadius, anchorRadius * 3f);
        _camera.Yaw = 0;
        _camera.Pitch = -0.3f;

        // Diagnostic: count resolved vs missing subset texture refs across every placed
        // instance. A miss means either the subset's resolved name isn't in rawIndex, or
        // the .raw failed to decode. Shows up as untextured (sand) fallback.
        var resolvedSubsets = 0;
        var missingSubsets = 0;
        var missingSample = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inst in _regionInstances)
        {
            foreach (var subset in inst.Mesh.Subsets)
            {
                if (string.IsNullOrEmpty(subset.TextureName)) continue;
                var resolved = ResolveTexName(subset.TextureName, inst.TexsetAbbr);
                if (_snoTextures.ContainsKey(resolved)) resolvedSubsets++;
                else
                {
                    missingSubsets++;
                    if (missingSample.Count < 12) missingSample.Add(resolved);
                }
            }
        }
        Console.WriteLine($"region '{normalized}': placed {_regionInstances.Count} instance(s) " +
                          $"across {_regionMeshes.Count} unique SNO(s), {_snoTextures.Count} texture(s)");
        Console.WriteLine($"  subsets: {resolvedSubsets} textured, {missingSubsets} missing");
        if (missingSample.Count > 0)
            Console.WriteLine($"  missing samples: {string.Join(", ", missingSample)}");
    }

    /// <summary>Phase 10e — spawn every shipped actor in a region and attach each one to the
    /// skinned-mesh draw loop. Terrain already loaded by <see cref="LoadRegion"/> populated
    /// <see cref="_regionLayout"/> so actor node-anchored positions compose against the same
    /// world frame. One <see cref="SkritRuntime"/> + one <see cref="SiegeFX.Core.Actors.WorldMessageBus"/>
    /// are shared across all actors; OnUpdate drains them at the 20 Hz logical tick so actor
    /// skrits can self-swap clips the same way the single-actor <see cref="_skritRuntime"/>
    /// path does. Untextured for now — the skinned fragment shader falls back to the
    /// neutral-sand color when uHasTexture=0, which is fine for "do the 181 fleshy shapes
    /// actually stand where the game places them" verification.</summary>
    private void LoadPlayActors(string mapTankPath, string logicTankPath, string objectsTankPath, string regionPath)
    {
        if (_gl is null) return;
        if (_regionLayout is null)
        {
            Console.Error.WriteLine("--play-region: region did not load; cannot place actors");
            return;
        }

        // Pinned to RenderHost fields (not `using var`) so _playResolver can keep
        // reading from them during gameplay — loot-swap weapon loads fire hours after
        // LoadPlayActors returns.
        _playMapTank     = TankFile.Open(mapTankPath);
        _playLogicTank   = TankFile.Open(logicTankPath);
        _playObjectsTank = TankFile.Open(objectsTankPath);
        var mapTank     = _playMapTank;
        var logicTank   = _playLogicTank;
        var objectsTank = _playObjectsTank;
        var mapReader     = new TankReader(mapTank);
        var logicReader   = new TankReader(logicTank);
        var objectsReader = new TankReader(objectsTank);

        var (store, storeDiags)    = SiegeFX.Core.Assets.TemplateStore.LoadFromTank(logicReader);
        var (instances, instDiags) = SiegeFX.Core.Assets.RegionObjects.LoadActors(mapReader, regionPath);

        // Objects.dsres carries more recent asset overrides than Logic.dsres in shipped DS1
        // content (patch-tank order), so add Logic last — the AssetResolver does last-added-wins
        // basename indexing and we want Logic's skrit/prs/asp resolution to shadow stale objects
        // copies. Terrain stays out of the resolver (it's all SNOs and subset rawsnap textures).
        var resolver = new SiegeFX.Core.Assets.AssetResolver();
        resolver.Add(objectsReader, "Objects.dsres");
        resolver.Add(logicReader,   "Logic.dsres");

        var spawner = new ActorSpawner(store, resolver, _regionLayout);
        var actors  = spawner.Spawn(instances);
        _actorRuntime = spawner.Runtime;
        _actorBus     = spawner.MessageBus;
        _templateStore = store;
        _playResolver = resolver;

        // Phase 16a — load formulas.gas once. Combat regen, level-up math, and
        // future spell costs all key off this. Failure is non-fatal: shipped data
        // always has the file, but a heavily-modded install might not, and the
        // engine should still render the world without leveling.
        try { _formulas = SiegeFX.Core.Assets.FormulasStore.LoadFromTank(logicReader); }
        catch (Exception ex) { Console.WriteLine($"  formulas.gas load failed: {ex.Message} (regen disabled)"); }

        // Phase 17a — build the spell catalog from the same template store. Cheap
        // (~150 instant-hit templates filter out of ~7300 total) and one-shot.
        // Failure here just leaves _spellCatalog null; cast attempts then no-op
        // through the null guard in TryClickToCast.
        try
        {
            _spellCatalog = SiegeFX.Core.Assets.SpellCatalog.Build(store);
            Console.WriteLine($"  spell catalog: {_spellCatalog.Count} instant-hit spells loaded");
        }
        catch (Exception ex) { Console.WriteLine($"  spell catalog build failed: {ex.Message}"); }

        // Phase 18a — bootstrap the audio engine and pre-register the two
        // shipped cast SFX. Sound.dsres lives next to Logic.dsres in the
        // Resources folder; if it's missing (very-stripped install) we
        // silently skip — TryCreate / RegisterClip never throw upward.
        try
        {
            string soundTankPath = Path.Combine(Path.GetDirectoryName(logicTankPath) ?? "",
                                                "Sound.dsres");
            if (File.Exists(soundTankPath))
            {
                _playSoundTank = TankFile.Open(soundTankPath);
                var soundReader = new TankReader(_playSoundTank);
                _audio = SiegeFX.Runtime.Audio.AudioEngine.TryCreate();
                if (_audio is not null)
                {
                    TryRegisterSfx(soundReader, SfxZapCast,
                        "/sound/effects/s_e_spell_zap_cast.wav");
                    TryRegisterSfx(soundReader, SfxHealingWindCast,
                        "/sound/effects/s_e_spell_healing_wind_cast.wav");

                    // Phase 18b — combat-feedback library. Swings + hits ship
                    // as 4-5 wav variants each so a sustained fight doesn't
                    // sound like one .wav looping; we group them and let
                    // AudioEngine pick at random per Play() call.
                    TryRegisterSfx(soundReader, "swing_01", "/sound/effects/s_e_swing_01.wav");
                    TryRegisterSfx(soundReader, "swing_02", "/sound/effects/s_e_swing_02.wav");
                    TryRegisterSfx(soundReader, "swing_03", "/sound/effects/s_e_swing_03.wav");
                    TryRegisterSfx(soundReader, "swing_04", "/sound/effects/s_e_swing_04.wav");
                    _audio.RegisterGroup(SfxMeleeSwingGroup,
                        "swing_01", "swing_02", "swing_03", "swing_04");

                    TryRegisterSfx(soundReader, "hit_flesh_1", "/sound/effects/s_e_hit_steelsword_flesh1.wav");
                    TryRegisterSfx(soundReader, "hit_flesh_2", "/sound/effects/s_e_hit_steelsword_flesh2.wav");
                    TryRegisterSfx(soundReader, "hit_flesh_3", "/sound/effects/s_e_hit_steelsword_flesh3.wav");
                    TryRegisterSfx(soundReader, "hit_flesh_4", "/sound/effects/s_e_hit_steelsword_flesh4.wav");
                    TryRegisterSfx(soundReader, "hit_flesh_5", "/sound/effects/s_e_hit_steelsword_flesh5.wav");
                    _audio.RegisterGroup(SfxMeleeHitGroup,
                        "hit_flesh_1", "hit_flesh_2", "hit_flesh_3", "hit_flesh_4", "hit_flesh_5");

                    TryRegisterSfx(soundReader, SfxMeleeMiss, "/sound/effects/s_e_miss_melee.wav");
                    TryRegisterSfx(soundReader, SfxLevelUp,   "/sound/effects/s_e_level_up_melee.wav");

                    TryRegisterSfx(soundReader, "die_goblin",     "/sound/effects/s_e_die_goblin.wav");
                    TryRegisterSfx(soundReader, "die_gremal",     "/sound/effects/s_e_die_gremal.wav");
                    TryRegisterSfx(soundReader, "die_krug_scout", "/sound/effects/s_e_die_krug_scout.wav");
                    TryRegisterSfx(soundReader, "die_krug_dog",   "/sound/effects/s_e_die_krug_dog.wav");
                }
            }
            else
            {
                Console.WriteLine($"  audio: Sound.dsres not at {soundTankPath} — SFX disabled");
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"  audio init failed: {ex.Message}"); }

        // Phase 15a — load DS1's small UI font and hand it to the text overlay.
        // copperplate-light is the body font DS1 uses for HP/MP readouts and
        // tooltip text; it covers ASCII 0x20..0x7F at 12px which is plenty for
        // a debug "SiegeFX [coords]" tag. Silent no-op if the font's missing
        // (modded installs that strip the GUI tank still get a working scene).
        if (_textRenderer is not null)
        {
            var font = SiegeFX.Core.Assets.BitmapFont.TryLoadByName(
                resolver, "b_gui_fnt_12p_copperplate-light");
            if (font is not null)
            {
                _textRenderer.SetFont(font);
                Console.WriteLine($"  hud font: {font.Name} ({font.Atlas.Width}x{font.Atlas.Height} atlas, " +
                                  $"glyphs {font.StartRange:X2}..{font.EndRange:X2})");
            }
            else
            {
                Console.Error.WriteLine("  !! hud font missing — overlay text disabled");
            }
        }

        // Phase 11d — build a region-scope nav mesh once and hand a follower to every
        // actor that spawns over a walkable triangle. We reuse the terrain tank already
        // opened at LoadRegion time but re-resolve SNOs into our own cache to keep the
        // nav mesh's lifetime decoupled from the render-mesh cache.
        SiegeFX.Core.Nav.NavMesh? navMesh = null;
        if (_regionTerrainTankPath is not null)
        {
            try
            {
                using var terrainTank = TankFile.Open(_regionTerrainTankPath);
                var terrainReader = new TankReader(terrainTank);
                var navGraph = RegionGraph.Load(mapReader.ExtractToMemory(regionPath + "/terrain_nodes/nodes.gas"));
                var meshIdx = SnoMeshIndex.Build(terrainReader);
                var navCache = new Dictionary<uint, SnoModel?>();
                SnoModel? ResolveNav(uint meshGuid)
                {
                    if (navCache.TryGetValue(meshGuid, out var hit)) return hit;
                    SnoModel? m = null;
                    if (meshIdx.TryResolve(meshGuid, out var p))
                    {
                        try { m = SnoModel.Load(terrainReader.ExtractToMemory(p)); }
                        catch { m = null; }
                    }
                    navCache[meshGuid] = m;
                    return m;
                }
                // Re-use the already-built RegionLayout: it's the same graph, same
                // resolver output, just with the render-side SnoModel cache. Building
                // a second layout would redo door-chain composition for no reason.
                navMesh = SiegeFX.Core.Nav.NavMesh.BuildForRegion(navGraph, _regionLayout, ResolveNav);
                Console.WriteLine($"  nav mesh: {navMesh.TriangleCount} tri(s), " +
                                  $"{navMesh.Vertices.Length} welded vert(s), " +
                                  $"{navMesh.SourceSnodeCount} snode(s), " +
                                  $"{navMesh.NonManifoldEdgeCount} non-manifold edge(s)");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  !! nav mesh build failed: {ex.Message} — actors will stand still");
                navMesh = null;
            }
        }

        int actorsOnMesh = 0;
        int actorsOffMesh = 0;
        foreach (var actor in actors)
        {
            if (!_actorMeshCache.TryGetValue(actor.Mesh, out var gl))
            {
                gl = new SkinnedMesh(_gl, actor.Mesh);
                _actorMeshCache[actor.Mesh] = gl;
            }

            SiegeFX.Core.Actors.ActorBrain? brain = null;
            if (navMesh is not null &&
                navMesh.TryFindTriangle(actor.WorldTransform.Translation, out var startTri))
            {
                // Snap the starting Y to the mesh surface so the first tick doesn't
                // lift the actor up off a ramp or drop it through a floor.
                var snapped = actor.WorldTransform.Translation with
                {
                    Y = navMesh.SampleYOnTriangle(startTri, actor.WorldTransform.Translation),
                };
                // Scid makes the per-actor RNG deterministic across runs so two launches
                // on the same region play out the same.
                //
                // Phase 13e — speed from template.body.avg_move_velocity (ActorStats.WalkSpeed).
                // Krug scouts amble at 3.1u/s, chickens scurry at ~1.9, butterflies float fast.
                // A template that didn't resolve a gait falls back to ActorStats's own 4u/s
                // default. Clamp non-positive values (dead/prop templates) up to 0.5 so the
                // follower still moves visibly when the author left a zero in by mistake.
                //
                // Authored facing = Actor.WorldTransform's local +Z direction in world
                // space (DS1 convention — characters look down +Z in their local frame).
                // Extracted with a forward-vector transform, then projected to XZ by
                // the follower ctor so actors stalled at spawn don't snap to +Z.
                var authoredFacing = Vector3.TransformNormal(Vector3.UnitZ, actor.WorldTransform);
                var gait = actor.Stats.WalkSpeed > 0.5f ? actor.Stats.WalkSpeed : 4f;
                var follower = new SiegeFX.Core.Actors.ActorFollower(
                    navMesh, snapped, speed: gait, rngSeed: (int)actor.Instance.Scid,
                    initialFacing: authoredFacing);
                // Phase 16c — wrap the wander follower in a brain so combatants
                // can chase + swing at the player. Non-combatants (chickens) keep
                // the same brain class but never receive a target during Tick, so
                // their state machine stays parked in Wander.
                brain = new SiegeFX.Core.Actors.ActorBrain(
                    follower, actor.Stats, rngSeed: (int)actor.Instance.Scid ^ unchecked((int)0xA17ACC1Eu));
                actorsOnMesh++;
            }
            else
            {
                actorsOffMesh++;
            }

            _actors.Add(new ActorRenderState
            {
                Actor             = actor,
                GlMesh            = gl,
                AnimTime          = 0,
                LastClipIndex     = actor.CurrentClipIndex,
                Brain             = brain,
                CurrentTransform  = actor.WorldTransform,
            });
        }

        if (navMesh is not null)
        {
            Console.WriteLine($"  followers: {actorsOnMesh} wandering / {actorsOffMesh} pinned (off-mesh spawn)");
        }

        // Phase 13c — remember the nav mesh for the LMB raycast handler. We always
        // set it (even to null) so mid-session mode changes can't leave a stale
        // reference from a previous region.
        _navMesh = navMesh;

        // Phase 13a — spawn one Farmboy PC next to the NPC centroid so he lands on the
        // nav mesh among the goblins. AI-idle until 13c/13d wire LMB/RMB input — the
        // render loop already tolerates a null follower by falling back to CurrentTransform.
        TrySpawnPlayer(spawner, navMesh);

        // Frame the camera on the player (13a) if we spawned one, else fall back to the
        // NPC centroid. Lift + pull back by the region-anchor radius we already computed
        // in LoadRegion (camera position is set there); overwrite here so "play-region"
        // always frames the actors, not the raw terrain anchor.
        var framingTarget = _player?.CurrentTransform.Translation;
        if (framingTarget is null && _actors.Count > 0)
        {
            var centroid = Vector3.Zero;
            foreach (var s in _actors) centroid += s.Actor.WorldTransform.Translation;
            centroid /= _actors.Count;
            framingTarget = centroid;
        }
        if (framingTarget is Vector3 f)
        {
            _camera.Position = f + new Vector3(0, 15f, 25f);
            _camera.Yaw = 0;
            _camera.Pitch = -0.35f;
        }

        // Diagnostic: actor world-position bounds + how many resolved a node transform vs
        // fell back to node-local identity. A large chunk falling back means NodeGuids in
        // actor.gas aren't matching the region's snode guids — actors would then pile at
        // the scene origin while terrain sprawls outward.
        int nodeResolved = 0, nodeFallback = 0;
        Vector3 pmin = new(float.PositiveInfinity), pmax = new(float.NegativeInfinity);
        foreach (var a in actors)
        {
            if (_regionLayout.TryGetTransform(a.Instance.Placement.NodeGuid, out _)) nodeResolved++;
            else nodeFallback++;
            var p = a.WorldTransform.Translation;
            pmin = Vector3.Min(pmin, p);
            pmax = Vector3.Max(pmax, p);
        }
        Console.WriteLine($"play-region '{regionPath}': {actors.Count}/{instances.Count} actor(s) live, " +
                          $"{_actorMeshCache.Count} unique mesh(es), " +
                          $"{spawner.Diagnostics.Count} diagnostic(s)");
        Console.WriteLine($"  actor node xforms: resolved {nodeResolved} / fallback-to-local {nodeFallback}");
        Console.WriteLine($"  actor world bounds: {pmin} .. {pmax}  (extent {pmax - pmin})");
        if (storeDiags.Count > 0) Console.WriteLine($"  templates: {storeDiags.Count} diagnostic(s)");
        if (instDiags.Count  > 0) Console.WriteLine($"  instances: {instDiags.Count} diagnostic(s)");
        foreach (var d in spawner.Diagnostics.Take(5)) Console.WriteLine($"  !! {d}");
        if (spawner.Diagnostics.Count > 5) Console.WriteLine($"  ... ({spawner.Diagnostics.Count - 5} more)");
    }

    /// <summary>
    /// Resolves each unique <see cref="SnoModel.Surface.TextureName"/> against a tank
    /// by matching bare filename (case-insensitive) against every <c>*.raw</c> entry.
    /// DS1 SNOs reference textures by basename (e.g. "t_grs01"), with the canonical
    /// location varying between terrain/logic tanks — basename matching side-steps that.
    /// </summary>
    private void LoadSnoTexturesFromTank(string tankPath)
    {
        if (_gl is null || _sno is null) return;

        using var tank = TankFile.Open(tankPath);
        var reader = new TankReader(tank);

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in reader.ListFiles())
        {
            if (!path.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) continue;
            var bare = Path.GetFileNameWithoutExtension(path);
            // First match wins; most DS1 basenames are unique within a tank. A future pass
            // can promote collision handling if a terrain/logic merge actually shows one.
            if (!index.ContainsKey(bare)) index[bare] = path;
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _sno.Subsets) unique.Add(s.TextureName);

        var hits = 0;
        foreach (var name in unique)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (!index.TryGetValue(name, out var fullPath))
            {
                Console.WriteLine($"  [miss] '{name}' not found in tank");
                continue;
            }
            var bytes = reader.ExtractToMemory(fullPath);
            var raw = RawImage.Load(bytes);
            _snoTextures[name] = new GlTexture(_gl, raw);
            hits++;
        }

        Console.WriteLine($"resolved {hits}/{unique.Count} SNO textures from tank '{tankPath}'");
    }

    private void OnUpdate(double dt)
    {
        if (_input is null) return;
        var forward = 0f;
        var strafe  = 0f;
        var vert    = 0f;
        var sprint  = false;

        foreach (var kb in _input.Keyboards)
        {
            if (kb.IsKeyPressed(Key.W)) forward += 1f;
            if (kb.IsKeyPressed(Key.S)) forward -= 1f;
            if (kb.IsKeyPressed(Key.D)) strafe  += 1f;
            if (kb.IsKeyPressed(Key.A)) strafe  -= 1f;
            if (kb.IsKeyPressed(Key.E) || kb.IsKeyPressed(Key.Space))      vert += 1f;
            if (kb.IsKeyPressed(Key.Q) || kb.IsKeyPressed(Key.ControlLeft)) vert -= 1f;
            if (kb.IsKeyPressed(Key.ShiftLeft)) sprint = true;
        }
        // Fly mode keeps WASD. Chase mode ignores it (PC movement is 13c) so the
        // camera can't drift off the player — cleaner than silently re-snapping
        // every frame.
        if (_cameraMode == CameraMode.Fly)
            _camera.Move(forward, strafe, vert, (float)dt, sprint);

        // Phase 13b — in chase mode, snap the camera behind the player and aim at
        // him. _chaseYaw is the orbit angle around the player's +Y axis; yaw=0
        // puts the camera on +Z so the player is seen looking down -Z (screen
        // "forward"). Yaw/pitch of Camera are overwritten here so DebugAttackNearestActor
        // still works in chase mode — its "in front of camera" test reads Camera.Forward.
        if (_cameraMode == CameraMode.Chase && _player is not null)
        {
            var target = _player.CurrentTransform.Translation + new Vector3(0, ChaseLookTargetY, 0);
            var offset = new Vector3(MathF.Sin(_chaseYaw), 0f, MathF.Cos(_chaseYaw)) * _chaseDistance;
            _camera.Position = target + offset + new Vector3(0, _chaseHeight, 0);
            var dir = Vector3.Normalize(target - _camera.Position);
            _camera.Yaw   = MathF.Atan2(dir.X, -dir.Z);
            _camera.Pitch = MathF.Asin(Math.Clamp(dir.Y, -0.999f, 0.999f));
        }

        if (_anim is not null && _anim.AnimLength > 0f)
            _animTime += dt;

        // Phase 9a: advance the skrit runtime at a fixed 20 Hz logic tick (DS1's authoritative
        // rate for `at ( N frames )` scheduling). Render dt is variable-rate — we accumulate
        // and drain in fixed steps, matching the deterministic-loop pattern we settled on in
        // 8d. After each tick, poll the host bridge's CurrentAnimIndex; a change means the
        // skrit picked a new sub-anim and we swap clips (reset _animTime so the new clip
        // starts from its first keyframe).
        if (_skritRuntime is not null && _skritHost is not null && _skritClips is not null)
        {
            const double stepSec = 1.0 / SkritInstance.FramesPerSecond;
            // Cap backlog at 5 ticks (250 ms) so a hitched frame / window drag doesn't
            // burst-fire chores the user didn't witness real time for.
            _skritTickAccumulator = Math.Min(_skritTickAccumulator + dt, stepSec * 5);
            while (_skritTickAccumulator >= stepSec)
            {
                _skritTickAccumulator -= stepSec;
                _skritRuntime.Tick(stepSec);
            }
            int idx = _skritHost.CurrentAnimIndex;
            if (idx >= 0 && idx < _skritClips.Length && idx != _skritCurrentClip)
            {
                var nextClip = _skritClips[idx] ?? _skritClips[0];
                if (nextClip is not null)
                {
                    _anim = nextClip;
                    _skritCurrentClip = idx;
                    _animTime = 0;
                }
            }
        }

        // Phase 10e: same 20 Hz accumulator pattern, but one shared runtime ticks every actor
        // in a region at once and the bus drains after each tick so cross-actor messages
        // (broadcasts, targeted self-sends) see the updated state. Per-actor AnimTime is
        // advanced by real dt (not step*stepsDone) to keep the visible anim smooth between
        // logic ticks — the skrit state only updates at 20 Hz, but the clip plays at render rate.
        if (_actorRuntime is not null && _actorBus is not null && _actors.Count > 0)
        {
            const double stepSec = 1.0 / SkritInstance.FramesPerSecond;
            _actorTickAccumulator = Math.Min(_actorTickAccumulator + dt, stepSec * 5);
            while (_actorTickAccumulator >= stepSec)
            {
                _actorTickAccumulator -= stepSec;
                _actorRuntime.Tick(stepSec);
                _actorBus.Deliver();
                // Phase 11d/16c — drive each brain at the same fixed cadence as the
                // skrit runtime. Stepping movement inside the accumulator loop (not
                // once per render frame) keeps translation deterministic regardless
                // of framerate — an actor walks at exactly `speed` u/s wall-clock.
                // The brain wraps wander + aggro: combatants get the player as a
                // target so they chase and swing; non-combatants get null and stay
                // in Wander forever.
                Vector3? npcTargetPos = null;
                SiegeFX.Core.Actors.ActorCombatState? npcTargetCombat = null;
                SiegeFX.Core.Actors.ActorStats? npcTargetStats = null;
                if (_player is not null && !_player.IsDead)
                {
                    npcTargetPos    = _player.CurrentTransform.Translation;
                    npcTargetCombat = _player.Actor.Combat;
                    npcTargetStats  = _player.Actor.Stats;
                }
                foreach (var s in _actors)
                {
                    if (s.IsDead) continue;
                    if (s.Brain is null) continue;
                    bool hostile = s.Actor.Stats.IsCombatant;
                    s.Brain.Tick(
                        (float)stepSec,
                        hostile ? npcTargetPos    : null,
                        hostile ? npcTargetCombat : null,
                        hostile ? npcTargetStats  : null);
                    // Compose CurrentTransform = rotate-to-facing * translate-to-pos.
                    // We drop the authored spawn orientation once the brain owns
                    // movement; the mesh's local forward in DS1 is +Z, so facing into
                    // the XZ heading means rotating around Y by atan2(dx, dz).
                    var facing = s.Brain.Facing;
                    float yaw = MathF.Atan2(facing.X, facing.Z);
                    s.CurrentTransform =
                        Matrix4x4.CreateRotationY(yaw) *
                        Matrix4x4.CreateTranslation(s.Brain.Position);
                }
                // Phase 13c — tick the player follower on the same cadence. The PC
                // has no ActorFollower (no wander), so its movement lives here. When
                // the raw NavFollower actually translates, derive XZ facing from the
                // position delta and compose a new world transform; if it didn't
                // move (idle, reached goal, blocked), the last facing is kept so the
                // PC doesn't snap to +Z on arrival.
                if (_player is not null && _playerFollower is not null && !_player.IsDead)
                {
                    var before = _playerFollower.Position;
                    _playerFollower.Tick((float)stepSec);
                    var after = _playerFollower.Position;
                    float dx = after.X - before.X;
                    float dz = after.Z - before.Z;
                    float len2 = dx * dx + dz * dz;
                    if (len2 > 1e-6f)
                    {
                        float len = MathF.Sqrt(len2);
                        _playerFacing = new Vector3(dx / len, 0f, dz / len);
                    }
                    float pyaw = MathF.Atan2(_playerFacing.X, _playerFacing.Z);
                    _player.CurrentTransform =
                        Matrix4x4.CreateRotationY(pyaw) *
                        Matrix4x4.CreateTranslation(after);
                    TryAutoPickup(after);

                    // Phase 16b — passive HP/MP regen. Rates come from formulas.gas
                    // (lr_unit/lr_period and mr_unit/mr_period; STR/INT-scaled). At
                    // 10/10/10 a fresh hero gains 0.25 HP/sec and 0.333 MP/sec, so a
                    // full HP refill takes ~3min and a full MP refill ~90s — slow
                    // enough that you can't tank by waiting between hits in a fight,
                    // fast enough to recover between encounters without a healer.
                    if (_formulas is not null)
                    {
                        var stats  = _player.Actor.Stats;
                        var combat = _player.Actor.Combat;
                        combat.Heal       (_formulas.LifeRecoveryRate(stats.Strength)    * (float)stepSec);
                        combat.RestoreMana(_formulas.ManaRecoveryRate(stats.Intelligence) * (float)stepSec);
                    }
                    // Phase 17a — countdown the spell cooldown on the same 20Hz
                    // tick the rest of player state runs on. Trip-key checks in
                    // TryClickToCast read CooldownRemaining off the spellbook.
                    _playerSpellbook?.Tick((float)stepSec);
                }
            }
            foreach (var s in _actors)
            {
                if (s.IsDead) continue;
                int idx = s.Actor.CurrentClipIndex;
                if (idx != s.LastClipIndex)
                {
                    s.LastClipIndex = idx;
                    s.AnimTime = 0;
                }
                if (s.Actor.Clips.Length > 0)
                {
                    var clip = s.Actor.Clips[Math.Min(idx, s.Actor.Clips.Length - 1)];
                    if (clip.AnimLength > 0f) s.AnimTime += dt;
                }
            }
        }
    }

    // Phase 12c — placeholder player attack. Picks the nearest living combatant in
    // front of the camera (XZ distance ≤ 30u, dot(fwd, actor-ray) > 0) and applies
    // one melee hit with a fixed 250-damage profile. This stands in for the PC's
    // weapon stats until Phase 13 wires a real player character.
    // Phase 13a — spawn a single Farmboy PC at the NPC centroid (snapped to the
    // nav mesh when present) using the existing ActorSpawner. Template 'farmboy'
    // is the canonical DS1 male-human hero archetype; aspect.model and the usual
    // chore_default skrit resolve through the same specializes chain every NPC
    // uses, so no special-casing in spawn. The actor just doesn't get a wander
    // follower — 13c will add click-to-move.
    private void TrySpawnPlayer(ActorSpawner spawner, SiegeFX.Core.Nav.NavMesh? navMesh)
    {
        if (_actors.Count == 0)
        {
            Console.WriteLine("  player: no NPCs spawned, skipping player spawn (nothing to anchor against)");
            return;
        }

        var centroid = Vector3.Zero;
        foreach (var s in _actors) centroid += s.Actor.WorldTransform.Translation;
        centroid /= _actors.Count;

        var spawnPos = centroid;
        if (navMesh is not null && navMesh.TryFindTriangle(centroid, out var tri))
            spawnPos = spawnPos with { Y = navMesh.SampleYOnTriangle(tri, centroid) };

        const string playerTemplate = "farmboy";
        // Scid 0xffffff00 is well clear of region actor.gas scids (region scids are
        // 0x01xxxxxx); any stable out-of-band value works, this one is easy to spot
        // in the combat log.
        var inst = SiegeFX.Core.Assets.ActorInstance.CreateSynthetic(
            playerTemplate, scid: 0xffffff00u, worldPosition: spawnPos,
            orientation: System.Numerics.Quaternion.Identity);

        var spawned = spawner.Spawn(new[] { inst });
        if (spawned.Count == 0)
        {
            Console.WriteLine($"  player: '{playerTemplate}' did not spawn (spawner diagnostics follow)");
            foreach (var d in spawner.Diagnostics.TakeLast(4))
                Console.WriteLine($"    !! {d}");
            return;
        }

        var player = spawned[0];
        if (!_actorMeshCache.TryGetValue(player.Mesh, out var gl))
        {
            gl = new SkinnedMesh(_gl!, player.Mesh);
            _actorMeshCache[player.Mesh] = gl;
        }

        var state = new ActorRenderState
        {
            Actor             = player,
            GlMesh            = gl,
            AnimTime          = 0,
            LastClipIndex     = player.CurrentClipIndex,
            Brain             = null,
            CurrentTransform  = player.WorldTransform,
            IsPlayer          = true,
        };
        _actors.Add(state);
        _player = state;
        // Phase 16d — XP/level state. Requires the formulas store, which loads
        // alongside the play-region path; viewer modes that skip TrySpawnPlayer
        // also skip this. The PC starts at level 1 with zero XP regardless of
        // template authoring (DS1 PCs always start fresh).
        if (_formulas is not null)
            _progression = new SiegeFX.Core.Actors.PlayerProgression(player, _formulas);
        // Phase 17a — slot the canonical spell_zap into the player's primary
        // book slot. Future inventory/learn UI will populate this from the
        // spellbook items in the PC's [inventory][equipment]; for now zap is
        // the always-on starter so 'Q' has something to fire.
        if (_spellCatalog is not null && _spellCatalog.TryGet(DefaultPrimarySpellName, out var primary))
        {
            _playerSpellbook = new SiegeFX.Core.Actors.PlayerSpellbook(
                player, new Random(unchecked((int)0x5C617AC1u)));
            _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Primary, primary);
            Console.WriteLine($"  spellbook: primary <- {primary.Name} (\"{primary.ScreenName}\") " +
                              $"range={primary.CastRange:F1} cd={primary.CastReloadDelay:F2}s");
            // Phase 17c — slot a self-heal into Secondary so 'W' has something
            // to fire. spell_healing_wind has a simple `(#magic+1)*5.15` mana
            // formula and a tractable alter_life enchantment value our
            // SpellExpr resolver handles without hitting the ternary syntax
            // the more complex heal templates use.
            if (_spellCatalog.TryGet(DefaultSecondarySpellName, out var secondary))
            {
                _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Secondary, secondary);
                Console.WriteLine($"  spellbook: secondary <- {secondary.Name} (\"{secondary.ScreenName}\") " +
                                  $"kind={secondary.Kind} cd={secondary.CastReloadDelay:F2}s");
            }
        }
        // Phase 13b — once a PC exists, default to chase cam. Toggle with C if the
        // user wants to fly around for debugging.
        _cameraMode = CameraMode.Chase;

        // Phase 13c — build the click-to-move NavFollower now so LMB handler can
        // simply SetTarget without null-checking a fresh PC. Speed falls back to
        // 4.5u/s (typical DS1 walk) when the template's stats chain didn't resolve
        // a max_life/walk_speed — see project_siegefx_max_life_formula.md.
        if (navMesh is not null)
        {
            var speed = player.Stats.WalkSpeed > 0f ? player.Stats.WalkSpeed : 4.5f;
            _playerFollower = new SiegeFX.Core.Nav.NavFollower(navMesh, spawnPos, speed);
        }
        _playerFacing = Vector3.UnitZ;

        // Phase 14b — seed PC equipment from the template's [inventory][equipment]
        // block. base_farmboy authors es_weapon_hand=dg_g_d_1h_fun (fun dagger),
        // es_feet=bo_bo_le_light, es_spellbook=book_glb_magic_01. Any attribute
        // whose name starts with es_ is treated as an equip slot -> item ref.
        _playerEquipment.Clear();
        if (_templateStore is not null)
        {
            var eq = _templateStore.GetSection(player.Template, "inventory", "equipment");
            if (eq is not null)
            {
                foreach (var a in eq.Attributes)
                {
                    if (!a.Name.StartsWith("es_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrWhiteSpace(a.Value)) continue;
                    _playerEquipment[a.Name] = a.Value.Trim();
                }
            }
        }

        Console.WriteLine(
            $"  player: '{playerTemplate}' spawned at " +
            $"({spawnPos.X:F1}, {spawnPos.Y:F1}, {spawnPos.Z:F1})  " +
            $"life={player.Stats.MaxLife:F0} " +
            $"walk={player.Stats.WalkSpeed:F1}u/s");
        if (_playerEquipment.Count > 0)
        {
            var slots = new List<string>(_playerEquipment.Count);
            foreach (var kv in _playerEquipment) slots.Add($"[{kv.Key}] {kv.Value}");
            Console.WriteLine($"  equipment: {string.Join(", ", slots)}");
        }

        // Phase 14d — cache the weapon_grip bone index from the PC's skeleton and
        // preload the initial weapon mesh/texture. base_farmboy's body.bone_translator
        // authors weapon_bone=weapon_grip, which is a bone name on the biped skeleton.
        _weaponGripBoneIdx = -1;
        for (int bi = 0; bi < player.Mesh.BoneNames.Count; bi++)
        {
            if (string.Equals(player.Mesh.BoneNames[bi], "weapon_grip", StringComparison.OrdinalIgnoreCase))
            { _weaponGripBoneIdx = bi; break; }
        }
        if (_weaponGripBoneIdx < 0)
            Console.WriteLine("  weapon: no weapon_grip bone on PC skeleton — weapon render disabled");
        else
            TryLoadPlayerWeapon();
    }

    // Phase 14d — load (or replace) the mesh + texture for whatever is currently in
    // es_weapon_hand. Silent no-op when no resolver / no slot / template missing;
    // we only render if the full chain resolves. Called on initial spawn and every
    // time an auto-equip swaps the weapon slot.
    private void TryLoadPlayerWeapon()
    {
        if (_gl is null) return;
        if (_playResolver is null || _templateStore is null) return;
        if (!_playerEquipment.TryGetValue("es_weapon_hand", out var weaponRef)) return;
        if (!_templateStore.TryGet(weaponRef, out var tpl)) return;
        var modelName = _templateStore.GetAttribute(tpl!, "aspect", "model");
        if (string.IsNullOrEmpty(modelName)) return;
        if (!_playResolver.TryLoadModel(modelName, out var aspBytes))
        {
            Console.WriteLine($"  weapon: model '{modelName}.asp' not in any tank");
            return;
        }
        SiegeFX.Core.Assets.AspMesh asp;
        try { asp = SiegeFX.Core.Assets.AspMesh.Load(aspBytes); }
        catch (Exception ex)
        {
            Console.WriteLine($"  weapon: '{modelName}.asp' load failed: {ex.Message}");
            return;
        }

        _weaponMesh?.Dispose();
        _weaponMesh = new StaticMesh(_gl, asp);
        _weaponBindInv = asp.InverseBindMatrices.Length > 0
            ? asp.InverseBindMatrices[0]
            : Matrix4x4.Identity;

        _weaponTexture?.Dispose();
        _weaponTexture = null;
        if (asp.TextureNames.Count > 0 &&
            _playResolver.TryLoadByBasename(asp.TextureNames[0] + ".raw", out var texBytes))
        {
            try { _weaponTexture = new GlTexture(_gl, RawImage.Load(texBytes)); }
            catch (Exception ex) { Console.WriteLine($"  weapon: texture load failed: {ex.Message}"); }
        }
        Console.WriteLine(
            $"  weapon: equipped mesh '{modelName}' ({asp.Corners.Length} corners, " +
            $"{(_weaponTexture is null ? "untextured" : asp.TextureNames[0])})");
    }

    // Phase 13c — LMB click-to-move. Unprojects the screen-space cursor to a
    // world ray, intersects it with the horizontal plane at the player's current
    // Y (cheap and accurate enough at DS1's shallow terrain slopes), confirms the
    // hit lands on the nav mesh, and retargets the player's follower. No-op when
    // there is no player, no nav mesh, the ray misses the Y plane, or the hit
    // point lies outside any walkable triangle.
    private void TryClickToMove(Vector2 cursorPx)
    {
        if (_player is null || _playerFollower is null || _navMesh is null) return;
        if (_player.IsDead) return;
        if (_window is null) return;
        var size = _window.FramebufferSize;
        if (size.X <= 0 || size.Y <= 0) return;

        // Screen → NDC. GL's NDC Y is up, window Y is down, hence the 1 - ...
        float ndcX = (cursorPx.X / size.X) * 2f - 1f;
        float ndcY = 1f - (cursorPx.Y / size.Y) * 2f;

        // Unproject near + far NDC points through the inverse view-proj. Row-vector
        // convention: the forward matrix is row-major stored, View * Proj; inverse
        // multiplies a row vector on the left. Vector4.Transform with Matrix4x4 uses
        // the same convention, so we can feed Vector4(ndcX, ndcY, z_ndc, 1) in and
        // divide by W to get the world-space point.
        float aspect = (float)size.X / size.Y;
        if (!Matrix4x4.Invert(_camera.GetViewProjection(aspect), out var invVp)) return;

        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVp);
        var farH  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invVp);
        if (MathF.Abs(nearH.W) < 1e-6f || MathF.Abs(farH.W) < 1e-6f) return;
        var near = new Vector3(nearH.X / nearH.W, nearH.Y / nearH.W, nearH.Z / nearH.W);
        var far_ = new Vector3(farH.X  / farH.W,  farH.Y  / farH.W,  farH.Z  / farH.W);
        var dir  = far_ - near;
        if (dir.LengthSquared() < 1e-8f) return;

        // Intersect with the horizontal plane Y = player.Y. Ray: p = near + t*dir.
        // Solve: near.Y + t*dir.Y = planeY. Reject near-parallel rays so a top-down
        // view (dir.Y ~= 0) doesn't land us at effectively infinity.
        float planeY = _playerFollower.Position.Y;
        if (MathF.Abs(dir.Y) < 1e-4f) return;
        float t = (planeY - near.Y) / dir.Y;
        if (t < 0f) return;
        var hit = near + dir * t;

        if (!_navMesh.TryFindTriangle(hit, out var tri)) return;
        hit = hit with { Y = _navMesh.SampleYOnTriangle(tri, hit) };
        _playerFollower.SetTarget(hit);
        Console.WriteLine($"click-move: target=({hit.X:F1}, {hit.Y:F1}, {hit.Z:F1})  tri={tri}");
    }

    // Phase 13d — RMB click-to-target attack. Same unproject math as
    // TryClickToMove, but instead of retargeting the follower we look for the
    // closest living combatant near the click point (XZ radius <see cref="ClickAttackRadius"/>).
    // Damage uses the player's stats when the template chain resolves non-zero
    // values; farmboy currently has 0 damage in stats (max_life formula bug,
    // project_siegefx_max_life_formula.md), so we fall back to the same synthetic
    // profile as DebugAttackNearestActor to keep the kill loop playable until the
    // stat fix lands in Phase 13e.
    private const float ClickAttackRadius = 3f;
    // Phase 13e — fallback attacker profile used when the PC template didn't
    // resolve real damage stats (hero templates author damage=0 and derive it
    // from the equipped weapon — Phase 14). 1-3 matches a bare-fisted level-1
    // hero swinging at farmhouse krug_scouts: ~4-5 hits per kill instead of
    // the instant kills the old 200-300 placeholder gave.
    private static readonly SiegeFX.Core.Actors.ActorStats HeroBaselineStats = new(
        MaxLife: 49f, MaxMana: 30f,
        DamageMin: 1f, DamageMax: 3f,
        Defense: 0f, AttackRange: 0.5f, WalkSpeed: 4.5f, ExperienceValue: 0,
        Strength: 10f, Dexterity: 10f, Intelligence: 10f);
    private void TryClickToAttack(Vector2 cursorPx)
    {
        if (_player is null || _window is null || _actors.Count == 0) return;
        if (_player.IsDead) return;
        var size = _window.FramebufferSize;
        if (size.X <= 0 || size.Y <= 0) return;

        float ndcX = (cursorPx.X / size.X) * 2f - 1f;
        float ndcY = 1f - (cursorPx.Y / size.Y) * 2f;
        float aspect = (float)size.X / size.Y;
        if (!Matrix4x4.Invert(_camera.GetViewProjection(aspect), out var invVp)) return;

        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVp);
        var farH  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invVp);
        if (MathF.Abs(nearH.W) < 1e-6f || MathF.Abs(farH.W) < 1e-6f) return;
        var near = new Vector3(nearH.X / nearH.W, nearH.Y / nearH.W, nearH.Z / nearH.W);
        var far_ = new Vector3(farH.X  / farH.W,  farH.Y  / farH.W,  farH.Z  / farH.W);
        var dir  = far_ - near;
        if (dir.LengthSquared() < 1e-8f || MathF.Abs(dir.Y) < 1e-4f) return;

        float planeY = _player.CurrentTransform.Translation.Y;
        float t = (planeY - near.Y) / dir.Y;
        if (t < 0f) return;
        var groundHit = near + dir * t;

        // Closest-combatant-to-click in XZ. DS1's selection is more of a screen-
        // picking test, but on DS1's shallow terrain a planar radius at the click
        // point is indistinguishable from "the actor you clicked on" for 99% of
        // cases. Revisit if we start getting mis-picks behind tall props.
        ActorRenderState? best = null;
        float bestDist = ClickAttackRadius;
        foreach (var s in _actors)
        {
            if (s.IsDead) continue;
            if (s.IsPlayer) continue;
            if (!s.Actor.Stats.IsCombatant) continue;
            var pos = s.CurrentTransform.Translation;
            float dx = pos.X - groundHit.X;
            float dz = pos.Z - groundHit.Z;
            float d  = MathF.Sqrt(dx * dx + dz * dz);
            if (d < bestDist) { bestDist = d; best = s; }
        }
        if (best is null)
        {
            Console.WriteLine(
                $"click-attack: no combatant within {ClickAttackRadius:F1}u of " +
                $"({groundHit.X:F1}, {groundHit.Z:F1})");
            return;
        }

        // Phase 14c: damage derives from the equipped weapon when es_weapon_hand is
        // populated; otherwise the 1-3 HeroBaselineStats fallback stands in for
        // bare-fisted swings. DS1 hero templates author damage=0 because the real
        // number is the weapon's own attack.damage_min/max.
        var attacker = GetPlayerAttackStats();
        var rng = new Random();
        float raw = SiegeFX.Core.Actors.CombatResolver.RollMeleeDamage(
            attacker, best.Actor.Stats, rng);
        float dealt = best.Actor.Combat.ApplyDamage(raw);
        float life = best.Actor.Combat.CurrentLife;
        float maxLife = best.Actor.Stats.MaxLife;
        Console.WriteLine(
            $"click-attack: hit {best.Actor.Template.Name} for {dealt:F0} " +
            $"({life:F0}/{maxLife:F0}){(best.Actor.Combat.IsDead ? "  *** DEAD ***" : "")}");

        // Phase 18b — every swing makes the swing sound; only swings that
        // land also play the flesh-hit variant. Whiff condition (dealt <=
        // 0) hits the miss SFX instead, which mirrors DS1's "hit/whiff
        // pair" behavior on melee.
        // Phase 18c — swing stays player-relative (your weapon), but the
        // hit/miss happens at the target's chest and pans accordingly.
        _audio?.Play(SfxMeleeSwingGroup);
        var hitPos = best.CurrentTransform.Translation + new Vector3(0f, 1.0f, 0f);
        if (dealt > 0f) _audio?.PlayAt(SfxMeleeHitGroup, hitPos);
        else            _audio?.PlayAt(SfxMeleeMiss,     hitPos);

        // Phase 16d — XP per damage point + kill bonus from aspect.experience_value.
        // Melee skill is hardcoded for now (the only swing flavor we have); when
        // ranged/spells land they pick their own SkillKind at the call site.
        AwardCombatXp((long)dealt, best.Actor.Combat.IsDead ? best.Actor.Stats.ExperienceValue : 0,
                       SiegeFX.Core.Assets.SkillKind.Melee);

        if (best.Actor.Combat.ConsumeJustDied())
        {
            best.IsDead = true;
            best.Brain = null;
            // Phase 18b — pick the species-matching death SFX off the
            // template name. Generic "die_*" for the species; nothing if
            // the species isn't in our shipped table (chickens go quietly).
            PlayDeathSfx(best.Actor.Template.Name, best.CurrentTransform.Translation);
            LogLootDrop(best.Actor, best.CurrentTransform.Translation);
        }
    }

    /// <summary>Phase 18b — resolve a template name to the matching death
    /// .wav. Heuristic substring match — DS1 names goblins as
    /// 3W_goblin_grunt etc., krug as krug_scout/krug_dog/etc., so a
    /// substring scan covers the variants without a per-template table.
    /// Phase 18c added <paramref name="worldPos"/> so the death scream
    /// pans + falls off from the corpse's location instead of always
    /// blasting in the center channel.</summary>
    private void PlayDeathSfx(string templateName, Vector3 worldPos)
    {
        if (_audio is null || string.IsNullOrEmpty(templateName)) return;
        string lower = templateName.ToLowerInvariant();
        string? bestKey = null;
        foreach (var key in SpeciesDeathSfx.Keys)
            if (lower.Contains(key) && (bestKey is null || key.Length > bestKey.Length))
                bestKey = key;
        if (bestKey is not null)
            _audio.PlayAt(SpeciesDeathSfx[bestKey], worldPos + new Vector3(0f, 1.0f, 0f));
    }

    /// <summary>Pull a .wav blob out of <paramref name="reader"/> and hand
    /// it to the audio engine. One-time call at LoadPlayActors. Logs and
    /// keeps going on failure so a missing/corrupt asset doesn't tank the
    /// scene load.</summary>
    private void TryRegisterSfx(SiegeFX.Core.Tank.TankReader reader,
                                string clipId, string tankPath)
    {
        if (_audio is null) return;
        try
        {
            if (!reader.TryGetFile(tankPath, out _))
            {
                Console.Error.WriteLine($"  audio: '{tankPath}' missing in Sound.dsres");
                return;
            }
            var bytes = reader.ExtractToMemory(tankPath);
            if (_audio.RegisterClip(clipId, bytes))
                Console.WriteLine($"  audio: '{clipId}' ← {tankPath} ({bytes.Length} B)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  audio: '{clipId}' load threw — {ex.Message}");
        }
    }

    /// <summary>Phase 17a — cast the slotted spell at whatever the cursor's
    /// currently picking. Mirrors <see cref="TryClickToAttack"/>'s unproject-
    /// then-pick-nearest-combatant logic so casting and clicking share one
    /// targeting model. The spellbook owns range/mana/cooldown gating; this
    /// method just routes the result into the floating-text + XP pipelines.</summary>
    private void TryClickToCast(SiegeFX.Core.Actors.SpellSlot slot)
    {
        if (_player is null || _window is null || _input is null) return;
        if (_playerSpellbook is null) return;
        var spell = slot == SiegeFX.Core.Actors.SpellSlot.Primary
            ? _playerSpellbook.Primary : _playerSpellbook.Secondary;
        if (spell is null) return;
        if (_input.Mice.Count == 0) return;

        var playerPos = _player.CurrentTransform.Translation;
        // Phase 16d ships one combined skill pool, so the player's progression
        // level is the only "magic level" we have to feed into the spell formula.
        // 17b+ replaces this with the dedicated CombatMagic / NatureMagic level.
        float magicLevel = _progression?.Level ?? 1;

        ActorRenderState? best = null;
        // Phase 17c — self-heal spells skip target picking entirely. The
        // spellbook ignores the target arg for SpellKind.SelfHeal, but we
        // also skip the unproject math so the cast still fires when the
        // cursor is hovering empty terrain.
        if (spell.Kind == SiegeFX.Core.Assets.SpellKind.OffensiveInstantHit)
        {
            if (_actors.Count == 0) return;
            var cursor = _input.Mice[0].Position;
            var size = _window.FramebufferSize;
            if (size.X <= 0 || size.Y <= 0) return;

            float ndcX = (cursor.X / size.X) * 2f - 1f;
            float ndcY = 1f - (cursor.Y / size.Y) * 2f;
            float aspect = (float)size.X / size.Y;
            if (!Matrix4x4.Invert(_camera.GetViewProjection(aspect), out var invVp)) return;

            var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVp);
            var farH  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invVp);
            if (MathF.Abs(nearH.W) < 1e-6f || MathF.Abs(farH.W) < 1e-6f) return;
            var near = new Vector3(nearH.X / nearH.W, nearH.Y / nearH.W, nearH.Z / nearH.W);
            var far_ = new Vector3(farH.X  / farH.W,  farH.Y  / farH.W,  farH.Z  / farH.W);
            var dir  = far_ - near;
            if (dir.LengthSquared() < 1e-8f || MathF.Abs(dir.Y) < 1e-4f) return;

            float planeY = _player.CurrentTransform.Translation.Y;
            float t = (planeY - near.Y) / dir.Y;
            if (t < 0f) return;
            var groundHit = near + dir * t;

            float bestDist = ClickAttackRadius;
            foreach (var s in _actors)
            {
                if (s.IsDead) continue;
                if (s.IsPlayer) continue;
                if (!s.Actor.Stats.IsCombatant) continue;
                var pos = s.CurrentTransform.Translation;
                float dx = pos.X - groundHit.X;
                float dz = pos.Z - groundHit.Z;
                float d  = MathF.Sqrt(dx * dx + dz * dz);
                if (d < bestDist) { bestDist = d; best = s; }
            }
        }

        SiegeFX.Core.Actors.CastResult result;
        if (spell.Kind == SiegeFX.Core.Assets.SpellKind.SelfHeal)
        {
            result = _playerSpellbook.TryCast(slot, _player.Actor, 0f, magicLevel);
        }
        else if (best is null)
        {
            result = new SiegeFX.Core.Actors.CastResult(
                SiegeFX.Core.Actors.CastOutcome.NoTarget, spell, 0, 0, 0, false);
        }
        else
        {
            float dx = best.CurrentTransform.Translation.X - playerPos.X;
            float dz = best.CurrentTransform.Translation.Z - playerPos.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            result = _playerSpellbook.TryCast(slot, best.Actor, dist, magicLevel);
        }

        // Cast feedback. Console line is the canonical record (matches the
        // melee log shape); the floating text gives the player the at-a-glance
        // "did it land" cue — anchored in world space at the target so big
        // sprawl battles don't blur the hit count.
        var anchor = best?.CurrentTransform.Translation ?? (playerPos + new Vector3(0f, 1.5f, 0f));
        switch (result.Outcome)
        {
            case SiegeFX.Core.Actors.CastOutcome.Cast:
                if (spell!.Kind == SiegeFX.Core.Assets.SpellKind.SelfHeal)
                {
                    // Phase 17c — self-heal cast: no target, no bolt, green
                    // restore popup over the player. Heal is applied inside
                    // the spellbook so the HP bar is already updated by the
                    // time this branch runs.
                    Console.WriteLine(
                        $"cast {spell.ScreenName}: heal +{result.HealAmount:F0} " +
                        $"(mana -{result.ManaSpent:F1})");
                    AddFloatingText($"+{(int)MathF.Round(result.HealAmount)} HP",
                                    playerPos + new Vector3(0f, 2.2f, 0f),
                                    new Vector4(0.40f, 0.95f, 0.40f, 1f));
                    // Phase 18a — DS1's healing-wind cast SFX. 2D playback for
                    // now; 18b will switch to per-source positional audio.
                    _audio?.Play(SfxHealingWindCast);
                    // Heals award NatureMagic XP proportional to HP restored,
                    // matching DS1's "cast_experience grows with effect" rule.
                    // Without this the heal slot is progression-dead.
                    AwardCombatXp((long)result.HealAmount, 0,
                                  SiegeFX.Core.Assets.SkillKind.NatureMagic);
                }
                else
                {
                    Console.WriteLine(
                        $"cast {spell.ScreenName}: hit {best!.Actor.Template.Name} for {result.Damage:F0} " +
                        $"(mana -{result.ManaSpent:F1}){(result.TargetKilled ? "  *** DEAD ***" : "")}");
                    // Phase 17b — snap PC facing toward the target so the cast
                    // at least visually originates *toward* the victim.
                    // _playerFacing normally only updates from movement deltas;
                    // without this, a player who casts while standing still
                    // would shoot bolts out of his back.
                    var tp = best.CurrentTransform.Translation;
                    float fx = tp.X - playerPos.X;
                    float fz = tp.Z - playerPos.Z;
                    float fl2 = fx * fx + fz * fz;
                    if (fl2 > 1e-6f)
                    {
                        float fl = MathF.Sqrt(fl2);
                        _playerFacing = new Vector3(fx / fl, 0f, fz / fl);
                        float pyaw = MathF.Atan2(_playerFacing.X, _playerFacing.Z);
                        _player.CurrentTransform =
                            Matrix4x4.CreateRotationY(pyaw) *
                            Matrix4x4.CreateTranslation(playerPos);
                    }
                    var src = playerPos + new Vector3(0f, 1.2f, 0f);
                    var dst = tp        + new Vector3(0f, 1.0f, 0f);
                    _spellBolts.Add(new SpellBolt
                    {
                        Source = src, Target = dst,
                        Color = new Vector4(0.55f, 0.85f, 1.00f, 1f),
                        Remaining = SpellBoltDuration, Total = SpellBoltDuration,
                    });
                    // Phase 18a — DS1's zap cast SFX. Same Play() shape as the
                    // heal branch; only the clip id differs because we still
                    // hardcode the spell→sound mapping (full sfx_script
                    // resolution is the Phase 18b job).
                    _audio?.Play(SfxZapCast);
                    AddFloatingText($"{spell.ScreenName.ToUpperInvariant()} -{(int)MathF.Round(result.Damage)}",
                                    anchor + new Vector3(0f, 1.8f, 0f),
                                    new Vector4(0.55f, 0.80f, 1.00f, 1f));
                    AwardCombatXp((long)result.Damage,
                                  result.TargetKilled ? best.Actor.Stats.ExperienceValue : 0,
                                  SiegeFX.Core.Assets.SkillKind.CombatMagic);
                    if (result.TargetKilled && best.Actor.Combat.ConsumeJustDied())
                    {
                        best.IsDead = true;
                        best.Brain = null;
                        // Phase 18b — death scream from the matching species.
                        PlayDeathSfx(best.Actor.Template.Name, best.CurrentTransform.Translation);
                        LogLootDrop(best.Actor, best.CurrentTransform.Translation);
                    }
                }
                break;
            case SiegeFX.Core.Actors.CastOutcome.NoTarget:
                AddFloatingText("no target", playerPos + new Vector3(0f, 2.1f, 0f), new Vector4(0.85f, 0.85f, 0.85f, 1f));
                break;
            case SiegeFX.Core.Actors.CastOutcome.OutOfRange:
                AddFloatingText("out of range", anchor + new Vector3(0f, 1.8f, 0f), new Vector4(0.95f, 0.65f, 0.20f, 1f));
                break;
            case SiegeFX.Core.Actors.CastOutcome.NoMana:
                AddFloatingText("no mana", playerPos + new Vector3(0f, 2.1f, 0f), new Vector4(0.45f, 0.65f, 1.00f, 1f));
                break;
            case SiegeFX.Core.Actors.CastOutcome.OnCooldown:
                // Cooldown is short (zap = 0.15s) — silent miss feels right rather
                // than spamming feedback for the player just leaning on the key.
                break;
            case SiegeFX.Core.Actors.CastOutcome.TargetDead:
                AddFloatingText("already dead", anchor + new Vector3(0f, 1.8f, 0f), new Vector4(0.7f, 0.7f, 0.7f, 1f));
                break;
            case SiegeFX.Core.Actors.CastOutcome.AlreadyFull:
                AddFloatingText("at full health", playerPos + new Vector3(0f, 2.1f, 0f),
                                new Vector4(0.40f, 0.95f, 0.40f, 1f));
                break;
        }
    }

    /// <summary>Push a world-anchored short-lived label onto the floating-text
    /// list. Drawn in the HUD pass each frame, projected from world to screen
    /// so it tracks the actor as the camera moves and fades over its lifetime.</summary>
    private void AddFloatingText(string text, Vector3 worldPos, Vector4 color)
    {
        _floatingTexts.Add(new FloatingText
        {
            Text = text,
            WorldPos = worldPos,
            Color = color,
            Remaining = FloatingTextDuration,
            Total = FloatingTextDuration,
        });
    }

    /// <summary>Phase 16d — fold a damage roll + kill bonus into the player's
    /// progression. Pulled out so the click-attack and F-key debug paths share
    /// identical XP awarding (and the eventual ranged/spell attacks just call
    /// in with their own <see cref="SiegeFX.Core.Assets.SkillKind"/>).</summary>
    private void AwardCombatXp(long damageXp, int killBonus, SiegeFX.Core.Assets.SkillKind skill)
    {
        if (_progression is null) return;
        long total = damageXp + killBonus;
        if (total <= 0) return;
        int oldLevel = _progression.Level;
        _progression.AwardXp(total, skill);
        if (_progression.Level > oldLevel)
        {
            // Level-up announce: console line for diagnostics + on-screen banner
            // so the user actually notices. The toast fades after 3 seconds.
            var s = _player!.Actor.Stats;
            Console.WriteLine(
                $"  *** LEVEL UP! L{oldLevel}->L{_progression.Level} " +
                $"str={s.Strength:F2} dex={s.Dexterity:F2} int={s.Intelligence:F2} " +
                $"life={s.MaxLife:F0} mana={s.MaxMana:F0} ***");
            _levelUpToastRemaining = LevelUpToastDuration;
            _levelUpToastLevel = _progression.Level;
            // Phase 18b — punctuate the toast with the matching DS1 jingle.
            // Single shared melee-flavored cue for now; per-skill cues
            // (magic_nature/magic_dark/ranged) can land in 18c next to the
            // skill-flavored XP bar.
            _audio?.Play(SfxLevelUp);
        }
    }

    private void DebugAttackNearestActor()
    {
        if (_actors.Count == 0) return;
        var camPos = _camera.Position;
        var camFwd = _camera.Forward;

        ActorRenderState? best = null;
        float bestDist = float.PositiveInfinity;
        foreach (var s in _actors)
        {
            if (s.IsDead) continue;
            if (!s.Actor.Stats.IsCombatant) continue;
            var pos = s.CurrentTransform.Translation;
            float dx = pos.X - camPos.X;
            float dz = pos.Z - camPos.Z;
            float distXZ = MathF.Sqrt(dx * dx + dz * dz);
            if (distXZ > 30f) continue;
            // Cone cull: only hit what you're roughly looking at. The follower's
            // forward is exported by Camera via Forward (unit vec). A dot > 0 means
            // the actor is in front of the camera's hemisphere — cheap and good
            // enough for a debug stand-in, not a real targeting reticle.
            float fwdDot = camFwd.X * dx + camFwd.Z * dz;
            if (fwdDot <= 0f) continue;
            if (distXZ < bestDist) { bestDist = distXZ; best = s; }
        }
        if (best is null)
        {
            Console.WriteLine("debug-attack: no combatant in range (30u cone in front)");
            return;
        }

        // Same equipped-weapon resolution as TryClickToAttack. F-key debug attack
        // stays as a camera-forward fallback so feedback is consistent between
        // the two input surfaces.
        var attacker = GetPlayerAttackStats();
        var rng = new Random();
        float raw = SiegeFX.Core.Actors.CombatResolver.RollMeleeDamage(
            attacker, best.Actor.Stats, rng);
        float dealt = best.Actor.Combat.ApplyDamage(raw);
        float life = best.Actor.Combat.CurrentLife;
        float maxLife = best.Actor.Stats.MaxLife;
        Console.WriteLine(
            $"debug-attack: hit {best.Actor.Template.Name} for {dealt:F0} " +
            $"({life:F0}/{maxLife:F0}){(best.Actor.Combat.IsDead ? "  *** DEAD ***" : "")}");

        AwardCombatXp((long)dealt, best.Actor.Combat.IsDead ? best.Actor.Stats.ExperienceValue : 0,
                       SiegeFX.Core.Assets.SkillKind.Melee);

        if (best.Actor.Combat.ConsumeJustDied())
        {
            best.IsDead = true;
            best.Brain = null;
            // Phase 18b — death SFX in the F-key debug path too, so the
            // dev shortcut still feels alive when audio's plumbed through.
            PlayDeathSfx(best.Actor.Template.Name, best.CurrentTransform.Translation);
            LogLootDrop(best.Actor, best.CurrentTransform.Translation);
        }
    }

    // Phase 12d/12e — roll the dying actor's loot table, log the outcome, and place
    // a visible pile at the actor's last known position. RNG is seeded from scid so
    // a given instance's drop is stable across re-kills; the visible pile is added
    // whenever the roll produced at least one entry (which for combatants with an
    // equipped weapon is every kill — the weapon bucket has no chance gate).
    private void LogLootDrop(SiegeFX.Core.Actors.Actor actor, Vector3 deathPos)
    {
        if (_templateStore is null) return;
        var table = SiegeFX.Core.Actors.LootTable.FromTemplate(_templateStore, actor.Template);
        if (table.IsEmpty)
        {
            Console.WriteLine($"  loot: {actor.Template.Name} has no inventory.pcontent");
            return;
        }
        var rng = new Random((int)actor.Instance.Scid);
        var drops = SiegeFX.Core.Actors.LootRoller.Roll(table, rng);
        if (drops.Count == 0)
        {
            Console.WriteLine($"  loot: {actor.Template.Name} dropped nothing this kill");
            return;
        }
        var parts = new List<string>(drops.Count);
        foreach (var d in drops)
            parts.Add(d.IsEquipped ? $"[{d.Slot}] {d.Reference}" : d.Reference);
        Console.WriteLine($"  loot: {actor.Template.Name} dropped {string.Join(", ", parts)}");
        _lootPiles.Add(new LootPile(deathPos, new List<SiegeFX.Core.Actors.LootEntry>(drops)));
    }

    // Phase 14c — a dropped "weapon_hand" entry is an upgrade iff its template has
    // a non-zero damage_max AND that max beats what the PC currently wields. DS1
    // drops non-weapon equipment (boots, capes) into other es_ slots; we auto-equip
    // those unconditionally on pickup and only gate the weapon swap by damage.
    private bool IsWeaponUpgrade(SiegeFX.Core.Actors.LootEntry entry)
    {
        if (!string.Equals(entry.Slot, "weapon_hand", StringComparison.OrdinalIgnoreCase))
            return true; // non-weapon slot -> always accept

        if (_templateStore is null) return false;
        if (!_templateStore.TryGet(entry.Reference, out var tpl)) return false;
        var dmaxStr = _templateStore.GetAttribute(tpl!, "attack", "damage_max");
        if (!float.TryParse(dmaxStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var newMax)) return false;
        if (newMax <= 0f) return false;

        var currentMax = GetPlayerAttackStats().DamageMax;
        return newMax > currentMax;
    }

    // Phase 14c — resolve the PC's effective attacker stats. If es_weapon_hand is
    // equipped and the referenced template has an [attack] block with damage_min/max,
    // use those; otherwise fall back to HeroBaselineStats. The rest of the attacker
    // stats (defense, attack range, walk speed, etc.) stay on the hero baseline —
    // we only override the two damage numbers the weapon dictates.
    private SiegeFX.Core.Actors.ActorStats GetPlayerAttackStats()
    {
        if (_templateStore is null) return HeroBaselineStats;
        if (!_playerEquipment.TryGetValue("es_weapon_hand", out var weaponRef))
            return HeroBaselineStats;
        if (!_templateStore.TryGet(weaponRef, out var weaponTpl)) return HeroBaselineStats;

        var dminStr = _templateStore.GetAttribute(weaponTpl!, "attack", "damage_min");
        var dmaxStr = _templateStore.GetAttribute(weaponTpl!, "attack", "damage_max");
        if (!float.TryParse(dminStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var dmin)) return HeroBaselineStats;
        if (!float.TryParse(dmaxStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var dmax)) return HeroBaselineStats;
        if (dmax <= 0f) return HeroBaselineStats;

        return HeroBaselineStats with { DamageMin = dmin, DamageMax = dmax };
    }

    // Phase 14a — PC auto-pickup. Called each logic tick after the player follower
    // advances. Any pile within PickupRadius (XZ) of the PC is emptied into the
    // inventory list; the pile is removed so its cube despawns. Iterating backwards
    // so a mid-loop RemoveAt doesn't skip the next pile.
    private void TryAutoPickup(Vector3 playerPos)
    {
        if (_lootPiles.Count == 0) return;
        for (int i = _lootPiles.Count - 1; i >= 0; i--)
        {
            var pile = _lootPiles[i];
            float dx = pile.Position.X - playerPos.X;
            float dz = pile.Position.Z - playerPos.Z;
            if (dx * dx + dz * dz > PickupRadius * PickupRadius) continue;

            var parts = new List<string>(pile.Items.Count);
            foreach (var it in pile.Items)
            {
                _playerInventory.Add(it);
                parts.Add(it.IsEquipped ? $"[{it.Slot}] {it.Reference}" : it.Reference);
            }
            Console.WriteLine(
                $"  pickup: acquired {string.Join(", ", parts)}  (inventory: {_playerInventory.Count})");

            // Phase 14c — auto-equip dropped weapons. If the loot entry came from
            // an equipped slot on the dead actor (Slot=weapon_hand/shield_hand/etc)
            // and the new item has a non-zero damage_max, swap it into the PC's
            // matching es_ slot. Keeps the kill -> loot -> stronger-hit loop
            // visible without a real inventory UI.
            bool weaponSwapped = false;
            foreach (var it in pile.Items)
            {
                if (!it.IsEquipped) continue;
                if (!IsWeaponUpgrade(it)) continue;
                var slotKey = "es_" + it.Slot;
                _playerEquipment[slotKey] = it.Reference;
                Console.WriteLine($"  equipped: [{slotKey}] <- {it.Reference}");
                if (string.Equals(slotKey, "es_weapon_hand", StringComparison.OrdinalIgnoreCase))
                    weaponSwapped = true;
            }
            if (weaponSwapped) TryLoadPlayerWeapon();

            _lootPiles.RemoveAt(i);
        }
    }

    private void OnRender(double dt)
    {
        if (_gl is null) return;
        if (_levelUpToastRemaining > 0f) _levelUpToastRemaining -= (float)dt;
        // Phase 18c — listener follows the PC every frame. We use camera
        // forward (not _playerFacing) so the audio image rotates with
        // the camera instead of the body — matches what the user is
        // looking at and avoids weird pans when the PC stands still
        // facing one way while the user orbits the cam. Falls back to
        // camera position when no PC has spawned (debug fly-cam mode).
        if (_audio is not null)
        {
            var listenerPos = _player?.CurrentTransform.Translation ?? _camera.Position;
            _audio.UpdateListener(listenerPos, _camera.Forward, Vector3.UnitY);
        }
        // Phase 17a — tick + prune floating cast feedback. Reverse-iterate so
        // RemoveAt is O(1) per kill and the list compacts in-place.
        for (int i = _floatingTexts.Count - 1; i >= 0; i--)
        {
            _floatingTexts[i].Remaining -= (float)dt;
            if (_floatingTexts[i].Remaining <= 0f) _floatingTexts.RemoveAt(i);
        }
        // Phase 17b — same lifecycle for spell-bolt projectiles.
        for (int i = _spellBolts.Count - 1; i >= 0; i--)
        {
            _spellBolts[i].Remaining -= (float)dt;
            if (_spellBolts[i].Remaining <= 0f) _spellBolts.RemoveAt(i);
        }
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var size = _window.FramebufferSize;
        var aspect = size.Y == 0 ? 1f : (float)size.X / size.Y;
        var vp = _camera.GetViewProjection(aspect);

        if (_gridShader is not null && _grid is not null)
        {
            _gridShader.Use();
            _gridShader.SetMatrix4("uViewProj", vp);
            _grid.Draw();
        }

        if (_meshShader is not null && _mesh is not null)
        {
            _meshShader.Use();
            _meshShader.SetMatrix4("uViewProj", vp);
            _meshShader.SetMatrix4("uModel", Matrix4x4.Identity);
            _meshShader.SetInt("uAlbedo", 0);
            _meshShader.SetInt("uHasTexture", _texture is null ? 0 : 1);
            _texture?.Bind(TextureUnit.Texture0);
            _mesh.Draw();
        }

        if (_meshShader is not null && _sno is not null)
        {
            _meshShader.Use();
            _meshShader.SetMatrix4("uViewProj", vp);
            _meshShader.SetMatrix4("uModel", Matrix4x4.Identity);
            _meshShader.SetInt("uAlbedo", 0);
            for (var i = 0; i < _sno.Subsets.Count; i++)
            {
                var subset = _sno.Subsets[i];
                if (_snoTextures.TryGetValue(subset.TextureName, out var tex))
                {
                    tex.Bind(TextureUnit.Texture0);
                    _meshShader.SetInt("uHasTexture", 1);
                }
                else
                {
                    _meshShader.SetInt("uHasTexture", 0);
                }
                _sno.DrawSubset(i);
            }
        }

        if (_skinShader is not null && _skinnedMesh is not null && _skinnedAsp is not null && _anim is not null)
        {
            // Loop the clip by wrapping the running time. AnimationRuntime clamps internally,
            // so this is just for visual continuity — without the wrap a long-running session
            // would freeze on the last keyframe forever.
            var t = (float)(_anim.AnimLength > 0f ? _animTime % _anim.AnimLength : 0.0);
            var skin = AnimationRuntime.ComputeSkinMatrices(_skinnedAsp, _anim, t);

            _skinShader.Use();
            _skinShader.SetMatrix4("uViewProj", vp);
            _skinShader.SetMatrix4("uModel", Matrix4x4.Identity);
            // "uBones[0]" not "uBones": GL 3.3 spec says either is valid for array-of-basic-type
            // uniforms, but several older Intel / mesa drivers only return a real location for
            // the element-0 form. With the bare name, SetMatrix4Array silently no-ops (loc<0)
            // and you get a T-posed mesh with no GL error.
            _skinShader.SetMatrix4Array("uBones[0]", skin);
            _skinShader.SetInt("uAlbedo", 0);
            _skinShader.SetInt("uHasTexture", _animTexture is null ? 0 : 1);
            _animTexture?.Bind(TextureUnit.Texture0);
            _skinnedMesh.Draw();
        }

        if (_skinShader is not null && _actors.Count > 0)
        {
            // One draw call per actor. The mesh cache keeps unique ASPs down (DS1 ships ~12
            // distinct archetypes per region) but each instance has its own bones, so we
            // re-upload uBones per actor. This is cheap at 181 actors — can batch per-mesh
            // later if a 4-region stream pushes the actor count past ~2k.
            _skinShader.Use();
            _skinShader.SetMatrix4("uViewProj", vp);
            _skinShader.SetInt("uAlbedo", 0);
            _skinShader.SetInt("uHasTexture", 0);
            foreach (var s in _actors)
            {
                var clips = s.Actor.Clips;
                Matrix4x4[] skin;
                if (clips.Length == 0)
                {
                    // No parsable PRS for this actor (shipped 0x0202 clips we don't support
                    // yet). Identity per bone = bind-pose pass-through, which renders the
                    // mesh as authored. Good enough to confirm placement.
                    if (!_actorIdentityBones.TryGetValue(s.Actor.Mesh, out skin!))
                    {
                        skin = new Matrix4x4[s.Actor.Mesh.BoneCount];
                        for (int i = 0; i < skin.Length; i++) skin[i] = Matrix4x4.Identity;
                        _actorIdentityBones[s.Actor.Mesh] = skin;
                    }
                }
                else
                {
                    var idx = Math.Min(s.Actor.CurrentClipIndex, clips.Length - 1);
                    var clip = clips[idx];
                    var t = (float)(clip.AnimLength > 0f ? s.AnimTime % clip.AnimLength : 0.0);
                    skin = AnimationRuntime.ComputeSkinMatrices(s.Actor.Mesh, clip, t);
                }
                _skinShader.SetMatrix4("uModel", s.CurrentTransform);
                _skinShader.SetMatrix4Array("uBones[0]", skin);
                s.GlMesh.Draw();
            }
        }

        // Phase 14d — render the PC's equipped weapon attached to the weapon_grip
        // bone. We recompute the animated bone worlds for the PC (cheap — the bone
        // walk already happened inside ComputeSkinMatrices, we just didn't surface
        // the intermediate array). weaponWorld = weaponBindInv * worldAnim[weapon_grip]
        // * player.CurrentTransform. Drawn with the static-mesh pipeline because
        // DS1 weapon ASPs weight every corner to bone 0 (effectively rigid); the
        // bind-inverse pre-multiply is what snaps the ASP's grip bone onto the hand.
        if (_meshShader is not null && _weaponMesh is not null && _player is not null
            && !_player.IsDead && _weaponGripBoneIdx >= 0)
        {
            var pcMesh = _player.Actor.Mesh;
            var clips = _player.Actor.Clips;
            Matrix4x4[] boneWorlds;
            if (clips.Length == 0)
            {
                boneWorlds = AnimationRuntime.ComputeAnimatedBoneWorlds(pcMesh, null, 0f);
            }
            else
            {
                var cidx = Math.Min(_player.Actor.CurrentClipIndex, clips.Length - 1);
                var clip = clips[cidx];
                var t = (float)(clip.AnimLength > 0f ? _player.AnimTime % clip.AnimLength : 0.0);
                boneWorlds = AnimationRuntime.ComputeAnimatedBoneWorlds(pcMesh, clip, t);
            }

            if (_weaponGripBoneIdx < boneWorlds.Length)
            {
                var gripLocal = boneWorlds[_weaponGripBoneIdx];
                // weaponBindInv cancels the weapon ASP's own grip-bone bind offset
                // so the grip sits at the hand bone's world origin, then gripLocal
                // places it in the player mesh frame, then CurrentTransform moves
                // the whole rig to world space.
                var weaponModel = _weaponBindInv * gripLocal * _player.CurrentTransform;

                _meshShader.Use();
                _meshShader.SetMatrix4("uViewProj", vp);
                _meshShader.SetMatrix4("uModel", weaponModel);
                _meshShader.SetInt("uAlbedo", 0);
                if (_weaponTexture is not null)
                {
                    _weaponTexture.Bind(TextureUnit.Texture0);
                    _meshShader.SetInt("uHasTexture", 1);
                }
                else
                {
                    _meshShader.SetInt("uHasTexture", 0);
                }
                _weaponMesh.Draw();
            }
        }

        // Phase 12e — placeholder loot piles. Untextured cube scaled to ~0.5u lifted
        // so its base sits on the actor's ground plane; the mesh shader's default
        // untextured beige fills in as a stand-in for actual DS1 gold/loot models.
        if (_meshShader is not null && _lootCube is not null && _lootPiles.Count > 0)
        {
            _meshShader.Use();
            _meshShader.SetMatrix4("uViewProj", vp);
            _meshShader.SetInt("uAlbedo", 0);
            _meshShader.SetInt("uHasTexture", 0);
            const float pileSize = 0.5f;
            foreach (var pile in _lootPiles)
            {
                var pos = pile.Position;
                var model = Matrix4x4.CreateScale(pileSize)
                          * Matrix4x4.CreateTranslation(pos.X, pos.Y + pileSize * 0.5f, pos.Z);
                _meshShader.SetMatrix4("uModel", model);
                _lootCube.Draw();
            }
        }

        if (_meshShader is not null && _regionInstances.Count > 0)
        {
            _meshShader.Use();
            _meshShader.SetMatrix4("uViewProj", vp);
            _meshShader.SetInt("uAlbedo", 0);
            foreach (var inst in _regionInstances)
            {
                _meshShader.SetMatrix4("uModel", inst.World);
                for (var i = 0; i < inst.Mesh.Subsets.Count; i++)
                {
                    var subset = inst.Mesh.Subsets[i];
                    var resolved = ResolveTexName(subset.TextureName, inst.TexsetAbbr);
                    if (_snoTextures.TryGetValue(resolved, out var tex))
                    {
                        tex.Bind(TextureUnit.Texture0);
                        _meshShader.SetInt("uHasTexture", 1);
                    }
                    else
                    {
                        _meshShader.SetInt("uHasTexture", 0);
                    }
                    inst.Mesh.DrawSubset(i);
                }
            }
        }

        // Phase 15a — 2D text overlay. Drawn last so it sits over the 3D scene;
        // BeginPass turns off depth + enables alpha blend, EndPass restores both.
        // Tag string in the top-left, PC coords below it when a player exists —
        // useful for sanity-checking nav-mesh moves without leaving fly-cam.
        if (_textRenderer is not null && _textRenderer.HasFont)
        {
            _textRenderer.BeginPass();
            var col   = new Vector4(1f, 1f, 1f, 1f);
            var dim   = new Vector4(0.7f, 0.7f, 0.7f, 1f);
            _textRenderer.DrawString(size.X, size.Y, "SiegeFX", 12, 12, col);
            if (_player is not null)
            {
                var p = _player.CurrentTransform.Translation;
                var line = $"x={p.X,7:F1}  y={p.Y,6:F1}  z={p.Z,7:F1}";
                _textRenderer.DrawString(size.X, size.Y, line, 12, 30, col);

                // HP/MP bars + numeric readout. ActorRenderState wraps the live
                // Combat/Stats blocks so we can pull current/max directly. Every
                // PC in DS1 is multiclass, so the mana bar is always shown — a
                // fresh hero starts with 30 MP from the (str/dex/int) formula.
                if (_barRenderer is not null)
                {
                    var combat = _player.Actor.Combat;
                    var stats  = _player.Actor.Stats;
                    DrawHudBar(size.X, size.Y, 12, 50, 200, 12,
                        combat.CurrentLife, stats.MaxLife,
                        new Vector4(0.78f, 0.10f, 0.10f, 1f), "HP");
                    DrawHudBar(size.X, size.Y, 12, 68, 200, 12,
                        combat.CurrentMana, stats.MaxMana,
                        new Vector4(0.18f, 0.40f, 0.90f, 1f), "MP");
                }
                // Phase 16d — level + XP/next under the bars. Compact one-liner:
                // "Lv N · XP into-level/level-span" so the player can watch the
                // pool fill on each kill without juggling absolute numbers.
                if (_progression is not null)
                {
                    long span = _progression.XpForNextLevel - _progression.XpForCurrentLevel;
                    long into = _progression.XpIntoCurrentLevel;
                    string xpLine = $"Lv {_progression.Level}  XP {into}/{span}";
                    _textRenderer.DrawString(size.X, size.Y, xpLine, 12, 86, col);
                }
                // Phase 16d — level-up toast. Big banner near top-center while
                // _levelUpToastRemaining > 0; a yellow-on-black backdrop grabs
                // the eye even with a busy 3D scene behind. Console line stays
                // for diagnostics; this is the player-facing signal.
                if (_levelUpToastRemaining > 0f && _barRenderer is not null)
                {
                    string banner = $"** LEVEL UP!  Lv {_levelUpToastLevel} **";
                    int textW = banner.Length * 7;  // 7px per glyph approx
                    int padX = 16, padY = 6;
                    int boxW = textW + padX * 2;
                    int boxH = 24;
                    int boxX = (size.X - boxW) / 2;
                    int boxY = 60;
                    _barRenderer.DrawRect (size.X, size.Y, boxX, boxY, boxW, boxH, new Vector4(0.10f, 0.08f, 0.02f, 0.85f));
                    _barRenderer.DrawBorder(size.X, size.Y, boxX, boxY, boxW, boxH, new Vector4(1.00f, 0.85f, 0.20f, 1f));
                    _textRenderer.DrawString(size.X, size.Y, banner, boxX + padX, boxY + padY, new Vector4(1f, 0.92f, 0.40f, 1f));
                }
            }

            // Phase 15c: grid inventory panel (toggled by 'I'). Drawn last so it
            // sits above the bars/coords; the panel draws its own backdrop dim
            // so the world behind reads as "paused/modal" while open.
            if (_inventoryOpen && _barRenderer is not null)
            {
                InventoryPanel.Draw(_barRenderer, _textRenderer, size.X, size.Y, _playerInventory);
            }
            // Phase 15d: pause menu (Esc). Drawn after the inventory so its
            // backdrop dims the inventory grid too — pause is the topmost UI.
            if (_pauseMenu.IsOpen && _barRenderer is not null)
            {
                _pauseMenu.Draw(_barRenderer, _textRenderer, size.X, size.Y);
            }
            // Phase 17b — projectile bolts. Head dot at lerp(src,dst,progress)
            // plus a few trailing dots stepped back along the line, alpha
            // fading per step. All in screen-space via the HUD ortho pass —
            // cheap and no need to wire a 3D billboard pipeline.
            if (_spellBolts.Count > 0 && _barRenderer is not null)
            {
                foreach (var b in _spellBolts)
                {
                    float prog = 1f - (b.Remaining / MathF.Max(0.0001f, b.Total));
                    if (prog < 0f) prog = 0f; else if (prog > 1f) prog = 1f;
                    for (int i = 0; i < SpellBoltTrailDots; i++)
                    {
                        float ti = prog - i * 0.07f;
                        if (ti < 0f) break;
                        var w = Vector3.Lerp(b.Source, b.Target, ti);
                        var clip = Vector4.Transform(new Vector4(w, 1f), vp);
                        if (clip.W <= 0.001f) continue;
                        float ndcXX = clip.X / clip.W;
                        float ndcYY = clip.Y / clip.W;
                        int sx = (int)((ndcXX * 0.5f + 0.5f) * size.X);
                        int sy = (int)((1f - (ndcYY * 0.5f + 0.5f)) * size.Y);
                        // Head dot is brightest + biggest; trail tapers in
                        // both alpha and pixel size so it reads as a streak.
                        float falloff = 1f - i / (float)SpellBoltTrailDots;
                        int dot = i == 0 ? 6 : Math.Max(2, 6 - i);
                        var c = b.Color; c.W *= falloff;
                        _barRenderer.DrawRect(size.X, size.Y, sx - dot / 2, sy - dot / 2, dot, dot, c);
                    }
                }
            }
            // Phase 17a — floating cast-feedback labels. World-anchored, so we
            // project each WorldPos through the same VP used for the 3D pass.
            // Behind the camera (clip-space w<=0) entries skip silently.
            if (_floatingTexts.Count > 0)
            {
                foreach (var ft in _floatingTexts)
                {
                    var wp = new Vector4(ft.WorldPos, 1f);
                    var clip = Vector4.Transform(wp, vp);
                    if (clip.W <= 0.001f) continue;
                    float ndcXX = clip.X / clip.W;
                    float ndcYY = clip.Y / clip.W;
                    int sx = (int)((ndcXX * 0.5f + 0.5f) * size.X);
                    int sy = (int)((1f - (ndcYY * 0.5f + 0.5f)) * size.Y);
                    // Lifecycle: rise ~12px over its life and fade alpha in
                    // the last third — float-up popup behavior.
                    float t01 = 1f - (ft.Remaining / MathF.Max(0.0001f, ft.Total));
                    sy -= (int)(t01 * 12f);
                    float a = ft.Remaining > ft.Total * 0.33f
                        ? 1f
                        : ft.Remaining / (ft.Total * 0.33f);
                    var c = ft.Color; c.W *= a;
                    int textW = ft.Text.Length * 7;
                    _textRenderer.DrawString(size.X, size.Y, ft.Text, sx - textW / 2, sy, c);
                }
            }
            _textRenderer.EndPass();
        }
    }

    /// <summary>Draws a single HP/MP-style bar at (<paramref name="x"/>,<paramref name="y"/>):
    /// dark background, fill scaled to current/max, 1-pixel border, and a label like
    /// "HP 38/50" laid alongside via <see cref="TextRenderer"/>.</summary>
    private void DrawHudBar(int viewportW, int viewportH, int x, int y, int w, int h,
                            float current, float max, Vector4 fillColor, string label)
    {
        if (_barRenderer is null || _textRenderer is null) return;
        if (max <= 0f) return;
        float pct = Math.Clamp(current / max, 0f, 1f);
        int fillW = (int)MathF.Round(pct * (w - 2));
        var bg     = new Vector4(0.10f, 0.10f, 0.10f, 0.82f);
        var border = new Vector4(0.85f, 0.85f, 0.85f, 0.85f);
        var text   = new Vector4(1f, 1f, 1f, 1f);

        _barRenderer.DrawRect(viewportW, viewportH, x, y, w, h, bg);
        if (fillW > 0)
            _barRenderer.DrawRect(viewportW, viewportH, x + 1, y + 1, fillW, h - 2, fillColor);
        _barRenderer.DrawBorder(viewportW, viewportH, x, y, w, h, border);

        var caption = $"{label} {(int)MathF.Round(current)}/{(int)MathF.Round(max)}";
        _textRenderer.DrawString(viewportW, viewportH, caption, x + w + 8, y - 1, text);
    }

    private void OnResize(Vector2D<int> size) => _gl?.Viewport(size);

    private bool _glDisposed;

    private void OnClosing()
    {
        // GL context is still current here. Release every GL handle we own
        // before Silk.NET tears the window down — the outer Dispose() runs
        // post-context and would crash on DeleteTexture/DeleteBuffer.
        if (_glDisposed) return;
        _glDisposed = true;

        foreach (var tex in _snoTextures.Values) tex.Dispose();
        _snoTextures.Clear();
        foreach (var mesh in _regionMeshes.Values) mesh.Dispose();
        _regionMeshes.Clear();
        _regionInstances.Clear();
        foreach (var mesh in _actorMeshCache.Values) mesh.Dispose();
        _actorMeshCache.Clear();
        _actorIdentityBones.Clear();
        _actors.Clear();
        _animTexture?.Dispose();
        _skinnedMesh?.Dispose();
        _texture?.Dispose();
        _sno?.Dispose();
        _mesh?.Dispose();
        _lootCube?.Dispose();
        _grid?.Dispose();
        _textRenderer?.Dispose();
        _barRenderer?.Dispose();
        _skinShader?.Dispose();
        _meshShader?.Dispose();
        _gridShader?.Dispose();
    }

    // Phase 19b — capture every piece of mid-session state worth restoring
    // and bake it into a SaveFile. Everything Apply re-derives from the GAS
    // (mesh, animation, skrit, region layout) is left out; only the things
    // that drift during play (life/mana/dead/positions/inventory/spellbook
    // cooldowns/camera/XP) live in the save. RegionPath stamps the snapshot
    // so the load path can refuse a save written for a different region.
    internal SiegeFX.Core.Save.SaveFile CaptureSave()
    {
        var save = new SiegeFX.Core.Save.SaveFile
        {
            SchemaVersion = SiegeFX.Core.Save.SaveFile.CurrentSchemaVersion,
            SavedAt       = DateTime.UtcNow,
            RegionPath    = _regionPath ?? "",
        };

        foreach (var s in _actors)
        {
            // CurrentTransform tracks the live position (brain / follower
            // updates it each tick); WorldTransform is the spawn pose and
            // would mis-restore an actor that wandered.
            var pos = s.CurrentTransform.Translation;
            save.Actors.Add(new SiegeFX.Core.Save.ActorSnapshot
            {
                Scid         = s.Actor.Instance.Scid,
                TemplateName = s.Actor.Template.Name,
                Position     = SiegeFX.Core.Save.Vec3.From(pos),
                CurrentLife  = s.Actor.Combat.CurrentLife,
                CurrentMana  = s.Actor.Combat.CurrentMana,
                IsDead       = s.IsDead || s.Actor.Combat.IsDead,
            });
        }

        foreach (var pile in _lootPiles)
        {
            var pileSnap = new SiegeFX.Core.Save.LootPileSnapshot
            {
                Position = SiegeFX.Core.Save.Vec3.From(pile.Position),
            };
            foreach (var it in pile.Items)
                pileSnap.Entries.Add(new SiegeFX.Core.Save.LootEntrySnapshot
                {
                    Slot      = it.Slot,
                    Reference = it.Reference,
                });
            save.LootPiles.Add(pileSnap);
        }

        if (_player is not null)
        {
            var p = new SiegeFX.Core.Save.PlayerSnapshot
            {
                Scid          = _player.Actor.Instance.Scid,
                TotalXp       = _progression?.TotalXp ?? 0,
                Level         = _progression?.Level ?? 1,
                Strength      = _player.Actor.Stats.Strength,
                Dexterity     = _player.Actor.Stats.Dexterity,
                Intelligence  = _player.Actor.Stats.Intelligence,
                Facing        = SiegeFX.Core.Save.Vec3.From(_playerFacing),
                CameraMode    = (int)_cameraMode,
                ChaseYaw      = _chaseYaw,
                ChaseDistance = _chaseDistance,
                ChaseHeight   = _chaseHeight,
                CameraPos     = SiegeFX.Core.Save.Vec3.From(_camera.Position),
                CameraYaw     = _camera.Yaw,
                CameraPitch   = _camera.Pitch,
            };
            // Inventory + equipment fold into the same flat list the loot
            // path uses. On load, equipped items get re-wired by walking
            // entries with es_-style slots; non-equipped items stay as
            // inventory rows. Equipment lives in _playerEquipment as
            // (es_slot -> ref); convert each to an entry whose Slot drops
            // the es_ prefix so the round-trip matches LootEntry shape.
            foreach (var item in _playerInventory)
                p.Inventory.Add(new SiegeFX.Core.Save.LootEntrySnapshot
                {
                    Slot      = item.Slot,
                    Reference = item.Reference,
                });
            foreach (var kv in _playerEquipment)
            {
                var slot = kv.Key.StartsWith("es_", StringComparison.OrdinalIgnoreCase)
                    ? kv.Key.Substring(3) : kv.Key;
                p.Inventory.Add(new SiegeFX.Core.Save.LootEntrySnapshot
                {
                    Slot      = "equipped:" + slot,
                    Reference = kv.Value,
                });
            }

            if (_playerSpellbook is not null)
            {
                p.Spellbook = new SiegeFX.Core.Save.SpellbookSnapshot
                {
                    PrimarySpell      = _playerSpellbook.Primary?.Name,
                    SecondarySpell    = _playerSpellbook.Secondary?.Name,
                    PrimaryCooldown   = _playerSpellbook.PrimaryCooldownRemaining,
                    SecondaryCooldown = _playerSpellbook.SecondaryCooldownRemaining,
                };
            }
            save.Player = p;
        }
        return save;
    }

    // Phase 19b — patch a captured SaveFile back onto the live scene. Assumes
    // the same region is loaded (LoadPlayActors already ran), and the live
    // _actors list overlaps with save.Actors by Scid. Actors present in the
    // save but missing from the scene are skipped with a log; ones present
    // in the scene but missing from the save are left untouched at their
    // current state (treats them as "spawned after this save" — same as a
    // patch that adds NPCs).
    internal void ApplySave(SiegeFX.Core.Save.SaveFile save)
    {
        if (!string.Equals(save.RegionPath, _regionPath ?? "", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"  load: region mismatch — save was '{save.RegionPath}', live region is '{_regionPath}'");
            return;
        }

        // Index live actors by scid so the patch loop is O(N+M) not O(N*M).
        var byScid = new Dictionary<uint, ActorRenderState>(_actors.Count);
        foreach (var s in _actors) byScid[s.Actor.Instance.Scid] = s;

        int patched = 0, missing = 0;
        foreach (var snap in save.Actors)
        {
            if (!byScid.TryGetValue(snap.Scid, out var s)) { missing++; continue; }
            s.Actor.Combat.RestoreFromSave(snap.CurrentLife, snap.CurrentMana, snap.IsDead);
            s.IsDead = snap.IsDead;
            // Position restore: actors with a brain (on-mesh) teleport via
            // the follower; off-mesh pinned actors get their CurrentTransform
            // updated directly since they have no follower to drive movement.
            var pos = snap.Position.ToVector3();
            if (s.Brain is not null) s.Brain.Teleport(pos);
            s.CurrentTransform = Matrix4x4.CreateTranslation(pos);
            patched++;
        }

        _lootPiles.Clear();
        foreach (var pileSnap in save.LootPiles)
        {
            var items = new List<SiegeFX.Core.Actors.LootEntry>(pileSnap.Entries.Count);
            foreach (var e in pileSnap.Entries)
                items.Add(new SiegeFX.Core.Actors.LootEntry(e.Slot, e.Reference));
            _lootPiles.Add(new LootPile(pileSnap.Position.ToVector3(), items));
        }

        if (save.Player is not null && _player is not null)
        {
            var ps = save.Player;
            // Republish auto-grown attributes through ResyncStats so MaxLife/
            // MaxMana track what the player actually had at save time, not the
            // template's L1 values. Then RestoreFromSave on the combat state
            // reapplies current life/mana under the new caps.
            if (_formulas is not null)
            {
                var current = _player.Actor.Stats;
                var newMaxLife = _formulas.MaxLife(ps.Strength, ps.Dexterity, ps.Intelligence);
                var newMaxMana = _formulas.MaxMana(ps.Strength, ps.Dexterity, ps.Intelligence);
                _player.Actor.ResyncStats(current with
                {
                    Strength = ps.Strength,
                    Dexterity = ps.Dexterity,
                    Intelligence = ps.Intelligence,
                    MaxLife = newMaxLife,
                    MaxMana = newMaxMana,
                });
                // The actor snapshot already reapplied CurrentLife/Mana above;
                // running RestoreFromSave again would only hit the new caps.
            }
            if (_progression is not null)
            {
                _progression.RestoreFromSave(ps.TotalXp, ps.Level);
            }
            _playerFacing = ps.Facing.ToVector3();
            _cameraMode   = (CameraMode)ps.CameraMode;
            _chaseYaw     = ps.ChaseYaw;
            _chaseDistance = ps.ChaseDistance;
            _chaseHeight  = ps.ChaseHeight;
            _camera.Position = ps.CameraPos.ToVector3();
            _camera.Yaw   = ps.CameraYaw;
            _camera.Pitch = ps.CameraPitch;

            _playerInventory.Clear();
            _playerEquipment.Clear();
            foreach (var e in ps.Inventory)
            {
                if (e.Slot.StartsWith("equipped:", StringComparison.OrdinalIgnoreCase))
                {
                    var slotKey = "es_" + e.Slot.Substring("equipped:".Length);
                    _playerEquipment[slotKey] = e.Reference;
                }
                else
                {
                    _playerInventory.Add(new SiegeFX.Core.Actors.LootEntry(e.Slot, e.Reference));
                }
            }
            // Re-render the equipped weapon mesh so the visible model matches
            // the restored es_weapon_hand entry. Safe even when no weapon was
            // saved — TryLoadPlayerWeapon early-outs on a missing slot.
            TryLoadPlayerWeapon();

            if (ps.Spellbook is not null && _playerSpellbook is not null && _spellCatalog is not null)
            {
                if (ps.Spellbook.PrimarySpell is { } pn && _spellCatalog.TryGet(pn, out var ps1))
                    _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Primary, ps1);
                if (ps.Spellbook.SecondarySpell is { } sn && _spellCatalog.TryGet(sn, out var ps2))
                    _playerSpellbook.Slot(SiegeFX.Core.Actors.SpellSlot.Secondary, ps2);
                _playerSpellbook.RestoreCooldowns(
                    ps.Spellbook.PrimaryCooldown, ps.Spellbook.SecondaryCooldown);
            }
        }

        Console.WriteLine($"  load: patched {patched}/{save.Actors.Count} actor(s), " +
                          $"{(missing > 0 ? $"{missing} missing from scene, " : "")}" +
                          $"{save.LootPiles.Count} loot pile(s) restored");
    }

    public void Dispose()
    {
        // Belt-and-braces — if Closing didn't fire (process kill, exception
        // during Run), GL resources leak with the process. Don't try to
        // re-release them post-context here; the OS will reclaim everything.
        _input?.Dispose();
        // Audio first — DeleteSources/Buffers before tearing down the
        // Sound.dsres handle isn't required (we already extracted bytes),
        // but the OpenAL context wants to outlive any pending playback.
        _audio?.Dispose();
        _playSoundTank?.Dispose();
        _playMapTank?.Dispose();
        _playLogicTank?.Dispose();
        _playObjectsTank?.Dispose();
        _window.Dispose();
    }
}
