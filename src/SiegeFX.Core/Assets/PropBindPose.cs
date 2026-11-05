using System.Numerics;

namespace SiegeFX.Core.Assets;

/// <summary>How a static prop's mesh is rooted in the world: bone 0's bind
/// pose (the Z-up to Y-up stand-up) for rigid props. The game composes a
/// map prop as scale × <see cref="Root"/> × placement × node, and so do the
/// tools that mark the same props on the nav mesh.</summary>
public static class PropBindPose
{
    /// <summary>Phase 21c — bone-0's world bind pose for a static prop, used to
    /// rotate Z-up authoring space into the engine's Y-up convention. Returns
    /// identity for unrigged meshes (no BindPose entries) AND for genuinely
    /// multi-bone skinned meshes (V2.5+ props whose geometry ships in
    /// world-bind space across several bones).
    ///
    /// SC-PROP-ATTACH-BONE — the gate was originally "exactly 1 bone", but many
    /// rigid props ship EXTRA attach bones (a wall torch carries an AP_light
    /// socket for its flame) while all their geometry still binds bone 0. Those
    /// tripped the old count gate, got no root correction, and rendered in raw
    /// Z-up — the torch cantilevering off the wall. The correct test is whether
    /// any GEOMETRY binds past bone 0: attach bones carry no verts, so a rigid
    /// prop reads max-geometry-bone 0 regardless of how many sockets it has.</summary>
    public static Matrix4x4 Root(AspMesh asp)
    {
        if (asp.BindPose.Length == 0) return Matrix4x4.Identity;
        if (!IsRigidRoot(asp)) return Matrix4x4.Identity;
        var bp = asp.BindPose[0];
        return Matrix4x4.CreateFromQuaternion(bp.Rotation) *
               Matrix4x4.CreateTranslation(bp.Translation);
    }

    /// <summary>A prop is "rigid-root" — safe to apply bone-0's bind
    /// correction to — when ALL geometry binds bone 0 AND every additional
    /// bone is an attach socket (AP_light for a torch/candlestand flame,
    /// AP_* generally), not a real animation bone.
    ///
    /// SC-PROP-ATTACH-BONE — verified by a tank-wide asp sweep: of the
    /// multi-bone props that bind all geometry to bone 0, two shapes exist —
    /// light props (candlestands, torches) whose extra bones are AP_light
    /// sockets and DO need the Z-up->Y-up correction, and hinged props
    /// (grates) whose extra bone is a real pivot (Bone04) and currently
    /// render right WITHOUT it. Gating on attach-socket names fixes the
    /// former and leaves the latter (and every skinned mesh) untouched, so
    /// this can't flip a prop that already sits correctly.</summary>
    public static bool IsRigidRoot(AspMesh asp)
    {
        if (MaxGeometryBone(asp) != 0) return false;
        for (int i = 1; i < asp.BoneNames.Count; i++)
        {
            var n = asp.BoneNames[i];
            if (string.IsNullOrEmpty(n) ||
                !n.StartsWith("ap_", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>Highest bone index that any vertex is actually weighted to
    /// (its highest-weight/primary bone). Unrigged geometry is implicitly on
    /// the root, so returns 0. Attach bones (AP_light, AP_*) carry no geometry
    /// and never raise this, so a rigid prop with sockets still reads 0.</summary>
    public static int MaxGeometryBone(AspMesh asp)
    {
        if (!asp.HasSkin) return 0;
        int max = 0;
        for (int c = 0; c < asp.SkinWeights.Length; c++)
        {
            var w = asp.SkinWeights[c];
            uint b = asp.SkinBones[c];
            int primary = 0; float pw = -1f;
            if (w.X > pw) { primary = (int)( b        & 0xFF); pw = w.X; }
            if (w.Y > pw) { primary = (int)((b >>  8) & 0xFF); pw = w.Y; }
            if (w.Z > pw) { primary = (int)((b >> 16) & 0xFF); pw = w.Z; }
            if (w.W > pw) { primary = (int)((b >> 24) & 0xFF); pw = w.W; }
            if (primary > max) max = primary;
        }
        return max;
    }
}
