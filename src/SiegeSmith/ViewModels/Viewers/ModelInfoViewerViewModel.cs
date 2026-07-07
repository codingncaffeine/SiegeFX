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
    private readonly Vector3[] _normals; // parallel to _verts (unused by flat shading, kept for later)
    private readonly Vector3 _center;
    private readonly float _radius;

    private float _yaw = 0.7f, _pitch = 0.5f, _dist;
    private int _vw = 800, _vh = 600;
    private bool _wireframe;

    public bool HasPreview => _verts.Length >= 3;

    private BitmapSource? _image;
    public BitmapSource? Image { get => _image; private set => SetProperty(ref _image, value); }

    public bool Wireframe
    {
        get => _wireframe;
        set { if (SetProperty(ref _wireframe, value)) { OnPropertyChanged(nameof(WireframeLabel)); Render(); } }
    }
    public string WireframeLabel => _wireframe ? "Solid" : "Wireframe";

    public RelayCommand ResetViewCommand { get; }
    public RelayCommand WireframeCommand { get; }

    private ModelInfoViewerViewModel(
        string title, string kind, string info, IReadOnlyList<InfoSection> sections,
        Vector3[] verts, Vector3[] normals, Vector3 center, float radius)
    {
        Title = title;
        Kind = kind;
        Info = info;
        Sections = sections;
        _verts = verts;
        _normals = normals;
        _center = center;
        _radius = MathF.Max(radius, 0.001f);
        _dist = _radius * 2.6f;

        ResetViewCommand = new RelayCommand(_ => ResetView());
        WireframeCommand = new RelayCommand(_ => Wireframe = !Wireframe);

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

    public void ResetView()
    {
        _yaw = 0.7f;
        _pitch = 0.5f;
        _dist = _radius * 2.6f;
        Render();
    }

    private void Render()
    {
        if (_verts.Length < 3) return;
        var bgra = SoftwareRenderer.Render(_verts, _normals, _vw, _vh, _center, _radius, _yaw, _pitch, _dist, _wireframe);
        var bmp = BitmapSource.Create(_vw, _vh, 96, 96, PixelFormats.Bgra32, null, bgra, _vw * 4);
        bmp.Freeze();
        Image = bmp;
    }

    // ── builders ────────────────────────────────────────────────
    private const int MaxListLines = 200;

    public static ModelInfoViewerViewModel FromAsp(string name, AspMesh m)
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

        // Flatten triangles for the preview: each corner -> its vertex position + normal.
        var verts = new List<Vector3>(m.TriangleIndices.Length);
        var normals = new List<Vector3>(m.TriangleIndices.Length);
        foreach (var ci in m.TriangleIndices)
        {
            var corner = m.Corners[ci];
            verts.Add(m.Positions[corner.VertexIndex]);
            normals.Add(corner.Normal);
        }

        var (center, radius) = CenterRadius(min, max);
        var info = $"{m.TriangleCount:N0} tris · {m.Positions.Length:N0} verts · {m.TextureNames.Count} texture(s)";
        return new ModelInfoViewerViewModel(name, "ASP aspect mesh", info, sections,
            verts.ToArray(), normals.ToArray(), center, radius);
    }

    public static ModelInfoViewerViewModel FromSno(string name, SnoModel m)
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

        // Flatten surfaces for the preview: surface triangle indices are local to the corner
        // pool starting at Surface.StartCorner.
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        foreach (var s in m.Surfaces)
        {
            foreach (var local in s.TriangleIndices)
            {
                int gi = (int)s.StartCorner + local;
                if ((uint)gi >= (uint)m.Corners.Length) continue;
                var corner = m.Corners[gi];
                verts.Add(corner.Position);
                normals.Add(corner.Normal);
            }
        }

        var (center, radius) = CenterRadius(m.MinBounds, m.MaxBounds);
        var info = $"{m.TotalTriangleCount:N0} tris · {m.Surfaces.Length} surface(s) · {m.Doors.Length} door(s)";
        return new ModelInfoViewerViewModel(name, "SNO siege node", info, sections,
            verts.ToArray(), normals.ToArray(), center, radius);
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
