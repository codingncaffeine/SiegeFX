using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SiegeFX.Core.Assets;
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
    private GL? _gl;
    private IInputContext? _input;
    private Shader? _gridShader;
    private Shader? _meshShader;
    private GridMesh? _grid;
    private StaticMesh? _mesh;
    private SnoMesh? _sno;
    private GlTexture? _texture;
    private readonly Dictionary<string, GlTexture> _snoTextures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, SnoMesh> _regionMeshes = new();
    private readonly List<RegionInstance> _regionInstances = new();
    private readonly Camera _camera = new();
    private bool _mouseLookActive;
    private Vector2? _lastMousePos;

    private readonly record struct RegionInstance(Matrix4x4 World, SnoMesh Mesh);

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

    // Cheap N·L lambert plus a constant ambient. Samples uAlbedo when uHasTexture != 0;
    // otherwise falls back to a neutral sand colour so untextured meshes still read as solids.
    // DS1 textures were authored for D3D (V=0 at top), so we flip V on sample for GL.
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
        string? regionMapTankPath = null, string? regionTerrainTankPath = null, string? regionPath = null)
    {
        _meshPath = meshPath;
        _texturePath = texturePath;
        _regionMapTankPath = regionMapTankPath;
        _regionTerrainTankPath = regionTerrainTankPath;
        _regionPath = regionPath;
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
            kb.KeyDown += (_, key, _) => { if (key == Key.Escape) _window.Close(); };

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
                foreach (var subset in mesh.Subsets)
                {
                    if (string.IsNullOrEmpty(subset.TextureName)) continue;
                    if (_snoTextures.ContainsKey(subset.TextureName)) continue;
                    if (!rawIndex.TryGetValue(subset.TextureName, out var texPath)) continue;
                    try
                    {
                        var raw = RawImage.Load(terrainReader.ExtractToMemory(texPath));
                        _snoTextures[subset.TextureName] = new GlTexture(_gl, raw);
                    }
                    catch { /* skip unreadable textures; subset falls back to uHasTexture=0 */ }
                }
            }

            _regionInstances.Add(new RegionInstance(world, mesh));
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

        Console.WriteLine($"region '{normalized}': placed {_regionInstances.Count} instance(s) " +
                          $"across {_regionMeshes.Count} unique SNO(s), {_snoTextures.Count} texture(s)");
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
                    if (_snoTextures.TryGetValue(subset.TextureName, out var tex))
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
        _texture?.Dispose();
        _sno?.Dispose();
        _mesh?.Dispose();
        _grid?.Dispose();
        _meshShader?.Dispose();
        _gridShader?.Dispose();
        _input?.Dispose();
        _window.Dispose();
    }
}
