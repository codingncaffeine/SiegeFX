using System.Numerics;
using System.Text;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Nav;

namespace SiegeFX.Tests;

// The door stitcher's overflow fallback must not hop a fence. Layout (XZ, all floor at y=0):
//
//   z 8 +-------+-------+
//       |   B   |       |   F = fence piece: floor strip S1 (z 0..1.8) and S0 (z 2.2..4)
//   z 4 +-------+   C   |       either side of Ignored boards standing at z=2 (0..1.5 high),
//       |  S0   |       |       like t_grs01_fence_generic_wood-04x04-a.
//       |=boards|       |   B = plain tile welded to S0, door-linked to F.
//       |  S1   |       |   C = the way round (a gate): welds to S1, to S0 and to B.
//   z 0 +-------+-------+
//      x 0     4       8
public class NavFenceSeamTests
{
    const uint Fence = 0x10, Tile = 0x20, Round = 0x30;

    static SnoModel.NavFace F(Vector3 a, Vector3 b, Vector3 c) => new(a, b, c, Vector3.UnitY);

    static IEnumerable<SnoModel.NavFace> Quad(float x0, float x1, float z0, float z1)
    {
        yield return F(new(x0, 0, z0), new(x1, 0, z0), new(x1, 0, z1));
        yield return F(new(x0, 0, z0), new(x1, 0, z1), new(x0, 0, z1));
    }

    static SnoModel.LogicalGrouping Group(byte id, SnoModel.FloorKind kind, IEnumerable<SnoModel.NavFace> faces) =>
        new() { Id = id, Kind = kind, Faces = faces.ToArray() };

    static SnoModel.Door Door(uint id, Vector3 at) =>
        new(id, new SnoModel.Xform4x3(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, at), []);

    static NavMesh Build(bool boards, bool wayRound)
    {
        var groups = new List<SnoModel.LogicalGrouping>
        {
            Group(0, SnoModel.FloorKind.Floor, Quad(0, 4, 2.2f, 4)),  // S0
            Group(1, SnoModel.FloorKind.Floor, Quad(0, 4, 0, 1.8f)),  // S1
        };
        if (boards)
            groups.Add(Group(2, SnoModel.FloorKind.Ignored, new[]
            {
                F(new(0, 0, 2), new(4, 0, 2), new(4, 1.5f, 2)),
                F(new(0, 0, 2), new(4, 1.5f, 2), new(0, 1.5f, 2)),
            }));
        var snos = new Dictionary<uint, SnoModel>
        {
            [Fence] = new() { LogicalGroupings = groups.ToArray(), Doors = [Door(1, new(2, 0, 4))] },
            [Tile] = new()
            {
                LogicalGroupings = [Group(0, SnoModel.FloorKind.Floor, Quad(0, 4, 4, 8))],
                Doors = [Door(1, new(2, 0, 4))],
            },
            // C's x=4 edge is cut where S1, S0 and B end so it welds to all three.
            [Round] = new()
            {
                LogicalGroupings = [Group(0, SnoModel.FloorKind.Floor,
                    Quad(4, 8, 0, 1.8f).Concat(Quad(4, 8, 1.8f, 2.2f)).Concat(Quad(4, 8, 2.2f, 4)).Concat(Quad(4, 8, 4, 8)))],
            },
        };

        var gas = new StringBuilder("[t:terrain_nodes,n:siege_node_list]\n{\n  x targetnode = 0x00000001;\n");
        void Node(uint guid, uint mesh, string doors) =>
            gas.Append($"  [t:snode,n:0x{guid:x8}]\n  {{\n    x guid = 0x{guid:X8};\n    x mesh_guid = 0x{mesh:X8};\n    texsetabbr = grs01;\n{doors}  }}\n");
        Node(1, Fence, "    [door*] { i fardoor = 1; x farguid = 0x00000002; i id = 1; }\n");
        Node(2, Tile, "    [door*] { i fardoor = 1; x farguid = 0x00000001; i id = 1; }\n");
        if (wayRound) Node(3, Round, "");
        gas.Append("}\n");
        var graph = RegionGraph.Load(Encoding.UTF8.GetBytes(gas.ToString()));

        var transforms = new Dictionary<uint, Matrix4x4> { [1] = Matrix4x4.Identity, [2] = Matrix4x4.Identity };
        if (wayRound) transforms[3] = Matrix4x4.Identity;
        return NavMesh.BuildForRegion(graph, RegionLayout.FromTransforms(1, transforms),
            g => snos.TryGetValue(g, out var s) ? s : null);
    }

    static readonly Vector3 InPen = new(2.7f, 0, 1.2f);     // an S1 triangle centroid
    static readonly Vector3 OutsideTile = new(1.3f, 0, 5.3f); // a B triangle centroid

    [Fact]
    public void OverflowHopDoesNotJumpTheFenceWhenThereIsAWayRound()
    {
        var mesh = Build(boards: true, wayRound: true);
        // The F->B and B->F door entries each offer the hop; both are refused.
        Assert.Equal(0, mesh.DoorSeamOverflowCount);
        Assert.Equal(2, mesh.DoorSeamBarrierRejectCount);
        Assert.Null(mesh.ExtraLinks);
        // Still reachable — the long way, through C.
        Assert.True(mesh.TryFindTriangle(InPen, out var a));
        Assert.True(mesh.TryFindTriangle(OutsideTile, out var b));
        var path = new List<int>();
        Assert.True(NavPathfinder.TryFindPath(mesh, a, b, path));
        Assert.Contains(path, t => mesh.SourceSnodeGuid[t] == 3);
    }

    [Fact]
    public void WithoutBoardsTheHopIsStillWired()
    {
        var mesh = Build(boards: false, wayRound: true);
        Assert.Equal(0, mesh.DoorSeamBarrierRejectCount);
        Assert.True(mesh.DoorSeamOverflowCount > 0);
    }

    [Fact]
    public void AHopThatIsTheOnlyConnectionIsKept()
    {
        // No way round: dropping the hop would strand S1, so the pre-barrier link stays
        // (once — the mirror door entry's copy is then redundant and refused).
        var mesh = Build(boards: true, wayRound: false);
        Assert.Equal(1, mesh.DoorSeamOverflowCount);
        Assert.Equal(1, mesh.DoorSeamBarrierRejectCount);
        Assert.True(mesh.TryFindTriangle(InPen, out var a));
        Assert.True(mesh.TryFindTriangle(OutsideTile, out var b));
        Assert.Equal(mesh.ComponentOf(a), mesh.ComponentOf(b));
    }

    [Fact]
    public void BarrierTestNeedsTheEdgeToStandAboveTheHop()
    {
        Vector3[] board = [new(0, 0, 2), new(4, 1.5f, 2)];
        Assert.True(NavMesh.CrossesBarrier(new(2, 0, 1), new(2, 0, 3), board));
        Assert.False(NavMesh.CrossesBarrier(new(2, 0, 1), new(2, 0, 1.9f), board));   // stops short
        Vector3[] kerb = [new(0, 0.1f, 2), new(4, 0.1f, 2)];
        Assert.False(NavMesh.CrossesBarrier(new(2, 0, 1), new(2, 0, 3), kerb));        // too low
        Vector3[] lintel = [new(0, 3, 2), new(4, 3, 2)];
        Assert.False(NavMesh.CrossesBarrier(new(2, 0, 1), new(2, 0, 3), lintel));      // overhead
    }
}
