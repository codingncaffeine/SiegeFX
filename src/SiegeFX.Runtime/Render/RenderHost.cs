using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Skrit;
using SiegeFX.Core.Tank;

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
    private double _actorTickAccumulator;

    private sealed class ActorRenderState
    {
        public Actor Actor = null!;
        public SkinnedMesh GlMesh = null!;
        public double AnimTime;
        public int LastClipIndex;

        // Phase 11d — actors that spawn over the nav mesh get a follower and wander
        // around it. Those that land off-mesh (pens inside buildings, props) have
        // Follower=null and just render at their authored spawn pose via CurrentTransform.
        public SiegeFX.Core.Actors.ActorFollower? Follower;
        public Matrix4x4 CurrentTransform;

        // Phase 12c — once the actor dies (CurrentLife hits 0), the follower is
        // nulled, the anim accumulator stops advancing, and the last skin matrices
        // effectively freeze the body mid-pose. Phase 12d will swap to chore_die.
        public bool IsDead;
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
        _window.Load   += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Resize += OnResize;
    }

    public void Run() => _window.Run();

    private void OnLoad()
    {
        _gl = GL.GetApi(_window);
        _input = _window.CreateInput();

        foreach (var kb in _input.Keyboards)
            kb.KeyDown += (_, key, _) =>
            {
                if (key == Key.Escape) _window.Close();
                // Phase 12c: F key strikes the nearest living actor in front of the
                // camera. Placeholder player-stand-in until Phase 13 brings a real PC
                // with its own template and equipped weapon. Logs each hit to the
                // console so the damage math is visible without a HUD overlay.
                else if (key == Key.F) DebugAttackNearestActor();
            };

        foreach (var mouse in _input.Mice)
        {
            mouse.MouseDown += (m, btn) =>
            {
                if (btn == MouseButton.Right)
                {
                    _mouseLookActive = true;
                    _lastMousePos = null;
                    m.Cursor.CursorMode = CursorMode.Raw;
                }
            };
            mouse.MouseUp += (m, btn) =>
            {
                if (btn == MouseButton.Right)
                {
                    _mouseLookActive = false;
                    _lastMousePos = null;
                    m.Cursor.CursorMode = CursorMode.Normal;
                }
            };
            mouse.MouseMove += (_, pos) =>
            {
                if (!_mouseLookActive) return;
                if (_lastMousePos is { } last)
                    _camera.LookDelta(pos.X - last.X, pos.Y - last.Y);
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

        using var mapTank     = TankFile.Open(mapTankPath);
        using var logicTank   = TankFile.Open(logicTankPath);
        using var objectsTank = TankFile.Open(objectsTankPath);
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

            SiegeFX.Core.Actors.ActorFollower? follower = null;
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
                // on the same region play out the same. Speed = 4 u/s is the DS1 walk
                // gait ballpark; overridable later per-template when we wire gait.
                //
                // Authored facing = Actor.WorldTransform's local +Z direction in world
                // space (DS1 convention — characters look down +Z in their local frame).
                // Extracted with a forward-vector transform, then projected to XZ by
                // the follower ctor so actors stalled at spawn don't snap to +Z.
                var authoredFacing = Vector3.TransformNormal(Vector3.UnitZ, actor.WorldTransform);
                follower = new SiegeFX.Core.Actors.ActorFollower(
                    navMesh, snapped, speed: 4f, rngSeed: (int)actor.Instance.Scid,
                    initialFacing: authoredFacing);
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
                Follower          = follower,
                CurrentTransform  = actor.WorldTransform,
            });
        }

        if (navMesh is not null)
        {
            Console.WriteLine($"  followers: {actorsOnMesh} wandering / {actorsOffMesh} pinned (off-mesh spawn)");
        }

        // Frame the camera on the centroid of the spawned actors so the user sees them on
        // first paint instead of staring at a corner. Lift + pull back by the region-anchor
        // radius we already computed in LoadRegion (camera position is set there); overwrite
        // here so "play-region" always frames the actors, not the raw terrain anchor.
        if (_actors.Count > 0)
        {
            var centroid = Vector3.Zero;
            foreach (var s in _actors) centroid += s.Actor.WorldTransform.Translation;
            centroid /= _actors.Count;
            _camera.Position = centroid + new Vector3(0, 15f, 25f);
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
        _camera.Move(forward, strafe, vert, (float)dt, sprint);

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
                // Phase 11d — drive each follower at the same fixed cadence as the
                // skrit runtime. Stepping movement inside the accumulator loop (not
                // once per render frame) keeps translation deterministic regardless
                // of framerate — an actor walks at exactly `speed` u/s wall-clock.
                foreach (var s in _actors)
                {
                    if (s.IsDead) continue;
                    if (s.Follower is null) continue;
                    s.Follower.Tick((float)stepSec);
                    // Compose CurrentTransform = rotate-to-facing * translate-to-pos.
                    // We drop the authored spawn orientation once the follower owns
                    // movement; the mesh's local forward in DS1 is +Z, so facing into
                    // the XZ heading means rotating around Y by atan2(dx, dz).
                    var facing = s.Follower.Facing;
                    float yaw = MathF.Atan2(facing.X, facing.Z);
                    s.CurrentTransform =
                        Matrix4x4.CreateRotationY(yaw) *
                        Matrix4x4.CreateTranslation(s.Follower.Position);
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

        // Synthetic attacker profile — placeholder until Phase 13 PC lands. Picks
        // a damage band that crit-one-shots a chicken but takes 4-6 hits on a
        // goblin grunt, which gives useful combat feedback in the viewer.
        var attacker = new SiegeFX.Core.Actors.ActorStats(
            MaxLife: 1000f, MaxMana: 0f,
            DamageMin: 200f, DamageMax: 300f,
            Defense: 0f, AttackRange: 0f, WalkSpeed: 0f, ExperienceValue: 0);
        var rng = new Random();
        float raw = SiegeFX.Core.Actors.CombatResolver.RollMeleeDamage(
            attacker, best.Actor.Stats, rng);
        float dealt = best.Actor.Combat.ApplyDamage(raw);
        float life = best.Actor.Combat.CurrentLife;
        float maxLife = best.Actor.Stats.MaxLife;
        Console.WriteLine(
            $"debug-attack: hit {best.Actor.Template.Name} for {dealt:F0} " +
            $"({life:F0}/{maxLife:F0}){(best.Actor.Combat.IsDead ? "  *** DEAD ***" : "")}");

        if (best.Actor.Combat.ConsumeJustDied())
        {
            best.IsDead = true;
            best.Follower = null;
            LogLootDrop(best.Actor);
        }
    }

    // Phase 12d — roll the dying actor's loot table and log the outcome. Seeds the
    // RNG from the actor's scid so every kill of the same instance produces the same
    // drop (re-attack the same goblin in two sessions, same drop) — helps when we're
    // debugging whether a template's pcontent parses correctly.
    private void LogLootDrop(SiegeFX.Core.Actors.Actor actor)
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
    }

    private void OnRender(double _)
    {
        if (_gl is null) return;
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
    }

    private void OnResize(Vector2D<int> size) => _gl?.Viewport(size);

    public void Dispose()
    {
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
        _grid?.Dispose();
        _skinShader?.Dispose();
        _meshShader?.Dispose();
        _gridShader?.Dispose();
        _input?.Dispose();
        _window.Dispose();
    }
}
