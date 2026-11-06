using System.Numerics;
using System.Text;
using SiegeFX.Core.Assets;

namespace SiegeFX.Tests;

/// <summary>SC-PROP-REST-POSE — the lever_glb_06 teleporter button: its
/// press clips hold Bone01 one unit down mesh -Z from the bind, and DS1 draws
/// (and the maps place) the posed mesh.</summary>
public class PropRestPoseTests
{
    // m_i_glb_switch-06's skeleton: Bone01 (root, -90°X, y -0.183) → Bone02.
    static readonly Quaternion RootRot = new(-0.707107f, 0f, 0f, 0.707107f);
    static readonly Vector3 RootBind = new(0f, -0.183f, 0f);
    static readonly Vector3 ChildBind = new(0f, -0.0016f, 0.1856f);

    static AspMesh SwitchMesh()
    {
        var bind = new[]
        {
            new AspMesh.Transform(RootRot, RootBind),
            new AspMesh.Transform(Quaternion.Identity, ChildBind),
        };
        var root = Matrix4x4.CreateFromQuaternion(RootRot) * Matrix4x4.CreateTranslation(RootBind);
        var child = Matrix4x4.CreateTranslation(ChildBind) * root;
        Matrix4x4.Invert(root, out var invRoot);
        Matrix4x4.Invert(child, out var invChild);
        return new AspMesh
        {
            BoneNames = new[] { "Bone01", "Bone02" },
            BoneParents = new[] { -1, 0 },
            BindPose = bind,
            InverseBindMatrices = new[] { invRoot, invChild },
        };
    }

    /// <summary>A v3 PRS: per bone, rotation + position keys at t = 0..len
    /// (one entry per key).</summary>
    static byte[] Prs(float length, params (string Bone, Quaternion Rot, Vector3[] Pos)[] bones)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        var names = new MemoryStream();
        foreach (var b in bones)
        {
            var s = Encoding.ASCII.GetBytes(b.Bone);
            names.Write(s);
            names.WriteByte(0);
            while (names.Length % 4 != 0) names.WriteByte(0);
        }
        w.Write(0x4D494E41u); w.Write(3u);
        w.Write((uint)names.Length); w.Write((uint)bones.Length); w.Write(length);
        for (int i = 0; i < 3; i++) w.Write(0f);          // root travel
        for (int i = 0; i < 9; i++) w.Write(0f);          // two quats + float
        w.Write(names.ToArray());
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i];
            w.Write(0x54534C4Bu); w.Write(3u); w.Write((uint)i); w.Write(0u);
            w.Write(1u); w.Write((uint)b.Pos.Length);
            w.Write(0f); w.Write(b.Rot.X); w.Write(b.Rot.Y); w.Write(b.Rot.Z); w.Write(b.Rot.W);
            for (int k = 0; k < b.Pos.Length; k++)
            {
                w.Write(b.Pos.Length == 1 ? 0f : length * k / (b.Pos.Length - 1));
                w.Write(b.Pos[k].X); w.Write(b.Pos[k].Y); w.Write(b.Pos[k].Z);
            }
        }
        w.Write(0x444E4541u);
        return ms.ToArray();
    }

    static TemplateStore LeverStore()
    {
        var store = new TemplateStore();
        store.RegisterFromGasText("""
            [t:template,n:lever_glb_06]
            {
                [aspect] { model = m_i_glb_switch-06; }
                [body]
                {
                    [chore_dictionary]
                    {
                        chore_prefix = a_i_glb;
                        [chore_open] { skrit = transition; anim_files:00 = switch-06-close; }
                        [chore_close] { skrit = transition; anim_files:00 = switch-06-open; }
                    }
                }
            }
            """);
        return store;
    }

    [Fact]
    public void TheButtonsHeldRootDropsItOntoThePlatform()
    {
        // a_i_glb_switch-06-close: Bone01 keyed at (0,-0.183,-1) the whole
        // clip, Bone02 pressing in and out.
        var clip = PrsAnimation.Load(Prs(1.5f,
            ("Bone01", RootRot, new[] { new Vector3(0f, -0.183f, -1f) }),
            ("Bone02", Quaternion.Identity, new[] { ChildBind, ChildBind + new Vector3(0, 0, -0.05f), ChildBind })));
        var store = LeverStore();
        Assert.True(store.TryGet("lever_glb_06", out var tpl));
        var requested = new List<string>();
        Assert.True(PropRestPose.TryRestOffset(store, tpl, SwitchMesh(),
            n => { requested.Add(n); return n == "a_i_glb_switch-06-close.prs" ? clip : null; }, out var off));
        Assert.Equal("a_i_glb_switch-06-close.prs", requested[0]);

        // One unit down mesh -Z, no rotation.
        Assert.Equal(0f, off.M41, 3);
        Assert.Equal(0f, off.M42, 3);
        Assert.Equal(-1f, off.M43, 3);
        Assert.Equal(1f, off.M11, 3);
        Assert.Equal(1f, off.M22, 3);

        // The town_center placement: -90°X at y 1.135 over the platform node.
        // Posed, the button's centre lands ~0.13 above the node origin — on
        // the 0.1-high platform — instead of hanging at 1.13.
        var place = Matrix4x4.CreateFromQuaternion(new Quaternion(-0.707107f, 0f, 0f, 0.707107f))
                  * Matrix4x4.CreateTranslation(0.007f, 1.13457f, 0.0077f);
        var centre = Vector3.Transform(Vector3.Zero, off * place);
        Assert.InRange(centre.Y, 0.1f, 0.2f);
    }

    [Fact]
    public void ARootThatMovesDuringTheClipIsNotARestOffset()
    {
        // statue_cav_rise_01: the root rises through the clip — motion, not
        // an authoring offset.
        var clip = PrsAnimation.Load(Prs(2f,
            ("Bone01", RootRot, new[] { new Vector3(0f, -0.183f, -3f), new Vector3(0f, -0.183f, -1.5f), RootBind })));
        var store = LeverStore();
        store.TryGet("lever_glb_06", out var tpl);
        Assert.False(PropRestPose.TryRestOffset(store, tpl, SwitchMesh(), _ => clip, out var off));
        Assert.Equal(Matrix4x4.Identity, off);
    }

    [Fact]
    public void ARootHeldAtTheBindIsNoOffset()
    {
        var clip = PrsAnimation.Load(Prs(1f, ("Bone01", RootRot, new[] { RootBind })));
        var store = LeverStore();
        store.TryGet("lever_glb_06", out var tpl);
        Assert.False(PropRestPose.TryRestOffset(store, tpl, SwitchMesh(), _ => clip, out _));
    }

    [Fact]
    public void NoChoreAnimsMeansNoCandidates()
    {
        var store = new TemplateStore();
        store.RegisterFromGasText("[t:template,n:crate] { [aspect] { model = m_crate; } }");
        store.TryGet("crate", out var tpl);
        Assert.Empty(PropRestPose.RestClipCandidates(store, tpl));
    }
}
