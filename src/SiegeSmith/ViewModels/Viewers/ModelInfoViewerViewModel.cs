using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SiegeFX.Core.Assets;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.Viewers;

/// <summary>Viewer for 3D assets — .asp aspect meshes and .sno siege nodes. Combines a live,
/// orbit-able CPU-rendered preview (<see cref="SoftwareRenderer"/>) with a structural inspector
/// (geometry counts, bounds, textures, subsets/surfaces, skeleton, doors, nav groupings).</summary>
public sealed class ModelInfoViewerViewModel : ObservableObject
{
    public string Title { get; }
    public string Kind { get; }
    public string Info { get; }
    public IReadOnlyList<InfoSection> Sections { get; }

    // ── preview geometry + camera ───────────────────────────────
    private readonly Vector3[] _verts;   // flattened triangles (3 verts each), Z-up model space
    private readonly Vector3[] _normals; // parallel to _verts
    private readonly Vector2[] _uvs;     // parallel to _verts (one UV per corner); empty if none
    private readonly int[] _triTex;      // one entry per triangle -> index into _textures, or -1
    private readonly SoftwareRenderer.Texture[] _textures; // resolved, sample-ready
    private readonly Vector3 _center;
    private readonly float _radius;

    private float _yaw = 0.7f, _pitch = 0.5f, _dist;
    private Vector3 _pan; // right-drag pan offset added to _center
    private int _vw = 800, _vh = 600;
    private bool _wireframe;
    private bool _textured;

    public bool HasPreview => _verts.Length >= 3;

    /// <summary>True when at least one triangle resolved a real texture, so the Textured toggle
    /// is meaningful. (When false the mesh shipped no texture, or none could be found in the
    /// install — the preview stays flat-shaded.)</summary>
    public bool CanTexture => _textures.Length > 0 && _uvs.Length == _verts.Length
                              && _triTex.Length == _verts.Length / 3;

    private BitmapSource? _image;
    public BitmapSource? Image { get => _image; private set => SetProperty(ref _image, value); }

    public bool Wireframe
    {
        get => _wireframe;
        set { if (SetProperty(ref _wireframe, value)) { OnPropertyChanged(nameof(WireframeLabel)); Render(); } }
    }
    public string WireframeLabel => _wireframe ? "Solid" : "Wireframe";

    /// <summary>Show the mesh with its resolved textures (as it looks in-game) vs. flat-shaded.
    /// On by default whenever textures resolved. Wireframe overrides to the flat line path.</summary>
    public bool Textured
    {
        get => _textured;
        set { if (SetProperty(ref _textured, value)) { OnPropertyChanged(nameof(TexturedLabel)); Render(); } }
    }
    // Name the action (what the click switches to), matching the sibling Wireframe button.
    public string TexturedLabel => _textured ? "Flat" : "Textured";

    public RelayCommand ResetViewCommand { get; }
    public RelayCommand WireframeCommand { get; }
    public RelayCommand TexturedCommand { get; }

    private ModelInfoViewerViewModel(
        string title, string kind, string info, IReadOnlyList<InfoSection> sections,
        Vector3[] verts, Vector3[] normals, Vector3 center, float radius,
        Vector2[]? uvs = null, int[]? triTex = null, SoftwareRenderer.Texture[]? textures = null)
    {
        Title = title;
        Kind = kind;
        Info = info;
        Sections = sections;
        _verts = verts;
        _normals = normals;
        _uvs = uvs ?? Array.Empty<Vector2>();
        _triTex = triTex ?? Array.Empty<int>();
        _textures = textures ?? Array.Empty<SoftwareRenderer.Texture>();
        _center = center;
        _radius = MathF.Max(radius, 0.001f);
        _dist = _radius * 2.6f;
        _textured = CanTexture; // default to the in-game look when we have it

        ResetViewCommand = new RelayCommand(_ => ResetView());
        WireframeCommand = new RelayCommand(_ => Wireframe = !Wireframe);
        TexturedCommand = new RelayCommand(_ => Textured = !Textured, _ => CanTexture);

        Render();
    }

    // ── camera interaction (driven by the view's mouse handlers) ─
    public void SetViewport(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (width == _vw && height == _vh) return;
        _vw = width;
        _vh = height;
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
        _dist = Math.Clamp(_dist * (wheelDelta > 0 ? 0.9f : 1.1f), _radius * 0.3f, _radius * 24f);
        Render();
    }

    /// <summary>Middle-drag spin: turns the model about its vertical axis — a turntable, like a planet
    /// on its axis. Yaw only, so it never tilts (unlike left-drag orbit, which also pitches).</summary>
    public void Spin(double dx)
    {
        _yaw += (float)dx * 0.01f;
        Render();
    }

    /// <summary>Right-drag pan: slides the framed centre in the camera's screen plane.</summary>
    public void Pan(double dx, double dy)
    {
        var dir = new Vector3(MathF.Cos(_pitch) * MathF.Cos(_yaw), MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch));
        var right = Vector3.Normalize(Vector3.Cross(new Vector3(0, 0, 1), dir));
        var up = Vector3.Normalize(Vector3.Cross(dir, right));
        float s = _dist * 0.0016f;
        _pan += right * (float)(-dx) * s + up * (float)dy * s;
        Render();
    }

    public void ResetView()
    {
        _yaw = 0.7f;
        _pitch = 0.5f;
        _dist = _radius * 2.6f;
        _pan = default;
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

    private static bool NearAngle(float a, float b)
    {
        float d = a - b;
        while (d > MathF.PI) d -= 2f * MathF.PI;
        while (d < -MathF.PI) d += 2f * MathF.PI;
        return MathF.Abs(d) < 0.16f;
    }

    private void Render()
    {
        if (_verts.Length < 3) return;
        var bgra = _textured && CanTexture && !_wireframe
            ? SoftwareRenderer.RenderTextured(_verts, _normals, _uvs, _triTex, _textures, _vw, _vh, _center + _pan, _radius, _yaw, _pitch, _dist)
            : SoftwareRenderer.Render(_verts, _normals, _vw, _vh, _center + _pan, _radius, _yaw, _pitch, _dist, _wireframe);
        var bmp = BitmapSource.Create(_vw, _vh, 96, 96, PixelFormats.Bgra32, null, bgra, _vw * 4);
        bmp.Freeze();
        Image = bmp;
    }

    // ── builders ────────────────────────────────────────────────
    private const int MaxListLines = 200;

    public static ModelInfoViewerViewModel FromAsp(string name, AspMesh m, TextureResolver? textures = null)
    {
        var (min, max) = Bounds(m.Positions);
        var size = max - min;

        var sections = new List<InfoSection>
        {
            new("Geometry", new[]
            {
                $"version         {m.AspVersionMajor}.{m.AspVersionMinor}",
                $"vertices        {m.Positions.Length:N0}",
                $"corners         {m.Corners.Length:N0}   (vertex/normal/uv tuples)",
                $"triangles       {m.TriangleCount:N0}",
                $"subsets         {m.Subsets.Length:N0}",
                $"skinned         {(m.HasSkin ? "yes (rigged)" : "no (static)")}",
            }),
            new("Bounds (mesh space)", new[]
            {
                $"min   ({min.X,8:F2}, {min.Y,8:F2}, {min.Z,8:F2})",
                $"max   ({max.X,8:F2}, {max.Y,8:F2}, {max.Z,8:F2})",
                $"size  ({size.X,8:F2}, {size.Y,8:F2}, {size.Z,8:F2})",
            }),
        };

        if (m.TextureNames.Count > 0)
            sections.Add(new("Textures", Cap(m.TextureNames.Select((t, i) => $"[{i}] {t}"))));

        sections.Add(new("Subsets (triangle span -> texture)", Cap(m.Subsets.Select(s =>
        {
            var tex = s.TextureIndex >= 0 && s.TextureIndex < m.TextureNames.Count ? m.TextureNames[s.TextureIndex] : $"#{s.TextureIndex}";
            return $"tris [{s.FirstTriangle}..{s.FirstTriangle + s.TriangleCount})  ->  {tex}";
        }))));

        if (m.BoneCount > 0)
            sections.Add(new($"Skeleton ({m.BoneCount} bones)", Cap(m.BoneNames.Select((b, i) =>
            {
                var parent = i < m.BoneParents.Length ? m.BoneParents[i] : -1;
                return parent < 0 ? $"[{i}] {b}  (root)" : $"[{i}] {b}  <- [{parent}]";
            }))));

        // Flatten triangles for the preview: each corner -> vertex position + normal + UV.
        int triCount = m.TriangleIndices.Length / 3;
        var verts = new List<Vector3>(m.TriangleIndices.Length);
        var normals = new List<Vector3>(m.TriangleIndices.Length);
        var uvs = new List<Vector2>(m.TriangleIndices.Length);

        // Resolve each texture name once; texSlot[i] indexes into texList, or -1 if unresolved.
        var texList = new List<SoftwareRenderer.Texture>();
        var texSlot = new int[m.TextureNames.Count];
        for (int i = 0; i < m.TextureNames.Count; i++)
        {
            texSlot[i] = -1;
            var t = textures?.Resolve(m.TextureNames[i]);
            if (t is { } tv && tv.Valid) { texSlot[i] = texList.Count; texList.Add(tv); }
        }
        // Map each triangle to its subset's resolved texture.
        var triTex = new int[triCount];
        Array.Fill(triTex, -1);
        foreach (var s in m.Subsets)
        {
            int slot = s.TextureIndex >= 0 && s.TextureIndex < texSlot.Length ? texSlot[s.TextureIndex] : -1;
            int end = Math.Min(triCount, s.FirstTriangle + s.TriangleCount);
            for (int t = Math.Max(0, s.FirstTriangle); t < end; t++) triTex[t] = slot;
        }

        for (int t = 0; t < triCount; t++)
            for (int e = 0; e < 3; e++)
            {
                var corner = m.Corners[m.TriangleIndices[t * 3 + e]];
                verts.Add(m.Positions[corner.VertexIndex]);
                normals.Add(corner.Normal);
                uvs.Add(corner.Uv);
            }

        var (center, radius) = CenterRadius(min, max);
        var info = $"{m.TriangleCount:N0} tris · {m.Positions.Length:N0} verts · {m.TextureNames.Count} texture(s)" +
                   (texList.Count > 0 ? $" · {texList.Count} loaded" : "");
        return new ModelInfoViewerViewModel(name, "ASP aspect mesh", info, sections,
            verts.ToArray(), normals.ToArray(), center, radius,
            uvs.ToArray(), triTex, texList.ToArray());
    }

    public static ModelInfoViewerViewModel FromSno(string name, SnoModel m, TextureResolver? textures = null)
    {
        var size = m.MaxBounds - m.MinBounds;
        var sections = new List<InfoSection>
        {
            new("Node", new[]
            {
                $"version         {m.Version}.{m.VersionMinor}",
                $"corners         {m.Corners.Length:N0}",
                $"surfaces        {m.Surfaces.Length:N0}",
                $"triangles       {m.TotalTriangleCount:N0}",
                $"doors           {m.Doors.Length:N0}",
                $"spots           {m.Spots.Length:N0}",
                $"nav groupings   {m.LogicalGroupings.Length:N0}",
                $"data crc32      0x{m.DataCrc32:X8}",
            }),
            new("Bounds (node space)", new[]
            {
                $"min   ({m.MinBounds.X,8:F2}, {m.MinBounds.Y,8:F2}, {m.MinBounds.Z,8:F2})",
                $"max   ({m.MaxBounds.X,8:F2}, {m.MaxBounds.Y,8:F2}, {m.MaxBounds.Z,8:F2})",
                $"size  ({size.X,8:F2}, {size.Y,8:F2}, {size.Z,8:F2})",
            }),
        };

        if (m.Surfaces.Length > 0)
            sections.Add(new("Surfaces (texture -> triangles)", Cap(m.Surfaces.Select(s =>
                $"{s.TextureName}  ->  {s.TriangleCount:N0} tris  ({s.CornerCount} corners)"))));

        if (m.Doors.Length > 0)
            sections.Add(new("Doors (stitch connectors)", Cap(m.Doors.Select(d =>
                $"door {d.Id}  ·  {d.HotSpots.Length} hotspot(s)"))));

        if (m.Spots.Length > 0)
            sections.Add(new("Spots (named anchors)", Cap(m.Spots.Select(s => s.Name))));

        if (m.LogicalGroupings.Length > 0)
            sections.Add(new("Nav groupings", Cap(m.LogicalGroupings.Select((g, i) =>
                $"[{i}] {g.Kind}  ·  {g.Faces.Length} face(s)"))));

        // Flatten surfaces for the preview: triangle indices are local to the corner pool starting
        // at Surface.StartCorner. Emit whole triangles (groups of 3) so a bad index drops just that
        // triangle instead of desyncing the rest, and tag each with its surface's resolved texture.
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triTex = new List<int>();
        var texList = new List<SoftwareRenderer.Texture>();
        foreach (var s in m.Surfaces)
        {
            int slot = -1;
            var t = textures?.Resolve(s.TextureName);
            if (t is { } tv && tv.Valid) { slot = texList.Count; texList.Add(tv); }

            var idx = s.TriangleIndices;
            for (int k = 0; k + 2 < idx.Length; k += 3)
            {
                int g0 = (int)s.StartCorner + idx[k];
                int g1 = (int)s.StartCorner + idx[k + 1];
                int g2 = (int)s.StartCorner + idx[k + 2];
                if ((uint)g0 >= (uint)m.Corners.Length || (uint)g1 >= (uint)m.Corners.Length || (uint)g2 >= (uint)m.Corners.Length)
                    continue;
                var c0 = m.Corners[g0]; verts.Add(c0.Position); normals.Add(c0.Normal); uvs.Add(c0.Uv);
                var c1 = m.Corners[g1]; verts.Add(c1.Position); normals.Add(c1.Normal); uvs.Add(c1.Uv);
                var c2 = m.Corners[g2]; verts.Add(c2.Position); normals.Add(c2.Normal); uvs.Add(c2.Uv);
                triTex.Add(slot);
            }
        }

        var (center, radius) = CenterRadius(m.MinBounds, m.MaxBounds);
        var loaded = texList.Count > 0 ? $" · {texList.Count} texture(s) loaded" : "";
        var info = $"{m.TotalTriangleCount:N0} tris · {m.Surfaces.Length} surface(s) · {m.Doors.Length} door(s)" + loaded;
        return new ModelInfoViewerViewModel(name, "SNO siege node", info, sections,
            verts.ToArray(), normals.ToArray(), center, radius,
            uvs.ToArray(), triTex.ToArray(), texList.ToArray());
    }

    private static (Vector3 Min, Vector3 Max) Bounds(Vector3[] points)
    {
        if (points.Length == 0) return (Vector3.Zero, Vector3.Zero);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return (min, max);
    }

    private static (Vector3 Center, float Radius) CenterRadius(Vector3 min, Vector3 max)
    {
        var center = (min + max) * 0.5f;
        var radius = (max - min).Length() * 0.5f;
        return (center, radius);
    }

    private static IReadOnlyList<string> Cap(IEnumerable<string> lines)
    {
        var list = lines.Take(MaxListLines + 1).ToList();
        if (list.Count > MaxListLines)
            list[MaxListLines] = $"… (list truncated at {MaxListLines})";
        return list;
    }
}

/// <summary>A titled block of monospace lines in the model inspector.</summary>
public sealed record InfoSection(string Header, IReadOnlyList<string> Lines);
