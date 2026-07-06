using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SiegeFX.Core.Assets;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.Viewers;

/// <summary>Structural inspector for 3D assets — .asp aspect meshes and .sno siege nodes. Reports
/// geometry counts, bounds, textures, subsets/surfaces, skeleton, doors and nav groupings, so a
/// modder can understand a model without a full 3D viewport (that arrives as the GL preview).</summary>
public sealed class ModelInfoViewerViewModel : ObservableObject
{
    public string Title { get; }
    public string Kind { get; }
    public string Info { get; }
    public IReadOnlyList<InfoSection> Sections { get; }

    private ModelInfoViewerViewModel(string title, string kind, string info, IReadOnlyList<InfoSection> sections)
    {
        Title = title;
        Kind = kind;
        Info = info;
        Sections = sections;
    }

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

        var info = $"{m.TriangleCount:N0} tris · {m.Positions.Length:N0} verts · {m.TextureNames.Count} texture(s)";
        return new ModelInfoViewerViewModel(name, "ASP aspect mesh", info, sections);
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

        var info = $"{m.TotalTriangleCount:N0} tris · {m.Surfaces.Length} surface(s) · {m.Doors.Length} door(s)";
        return new ModelInfoViewerViewModel(name, "SNO siege node", info, sections);
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

    private static IReadOnlyList<string> Cap(IEnumerable<string> lines)
    {
        var list = lines.Take(MaxListLines + 1).ToList();
        if (list.Count > MaxListLines)
        {
            list[MaxListLines] = $"… (list truncated at {MaxListLines})";
        }
        return list;
    }
}

/// <summary>A titled block of monospace lines in the model inspector.</summary>
public sealed record InfoSection(string Header, IReadOnlyList<string> Lines);
