using System.Numerics;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Applies a <see cref="PrsAnimation"/> clip to an <see cref="AspMesh"/> at a given time:
///
///   bone pose (PRS, by bone NAME)  → per-ASP-bone local transform
///                                  → walk ASP hierarchy → world pose
///   skinMatrix[i] = worldPose[i] * inverse(worldBindPose[i])
///   skinnedCorner[c] = Σ_k weight[c,k] * skinMatrix[bone[c,k]] * Positions[Corners[c].VertexIndex]
///
/// All math is CPU-side and <see cref="Matrix4x4"/>-based. This isn't fast enough for
/// real-time rendering of hundreds of skinned meshes, but it's exactly what we need to
/// validate the loader pipeline end-to-end and drive early tooling (pose dumps, bone
/// AABBs, regression fuzzing across every PRS×ASP pair).
/// </summary>
public static class AnimationRuntime
{
    // Phase 21b-2 — bone-index map cache. Resolving "ASP bone i → PRS bone j" by
    // walking BoneNames was a per-call Dictionary<string,int> allocation, which at
    // 180 actors × 60 fps was the single largest source of Gen 0 garbage in
    // OnRender. The mapping is a pure function of (mesh, anim) and both objects
    // outlive the region they're loaded with, so cache once and reuse.
    private static readonly Dictionary<(AspMesh, PrsAnimation), int[]> _boneMapCache = new();
    private static readonly object _boneMapLock = new();

    private static int[] GetBoneMap(AspMesh mesh, PrsAnimation anim)
    {
        lock (_boneMapLock)
        {
            if (_boneMapCache.TryGetValue((mesh, anim), out var map)) return map;
            var byName = new Dictionary<string, int>(anim.BoneNames.Count);
            for (var i = 0; i < anim.BoneNames.Count; i++)
                byName[anim.BoneNames[i]] = i;
            map = new int[mesh.BoneCount];
            for (var i = 0; i < mesh.BoneCount; i++)
                map[i] = byName.TryGetValue(mesh.BoneNames[i], out var idx) ? idx : -1;
            _boneMapCache[(mesh, anim)] = map;
            return map;
        }
    }

    /// <summary>Compose skin matrices for <paramref name="mesh"/> at <paramref name="timeSec"/>.
    /// Returns one matrix per ASP bone; identity entries appear for bones with no matching
    /// PRS keys (they stay in their bind pose, which yields an identity skin delta).</summary>
    public static Matrix4x4[] ComputeSkinMatrices(AspMesh mesh, PrsAnimation anim, float timeSec)
    {
        if (mesh.BoneCount == 0) return Array.Empty<Matrix4x4>();
        var skin = new Matrix4x4[mesh.BoneCount];
        ComputeSkinMatrices(mesh, anim, timeSec, skin);
        return skin;
    }

    /// <summary>Zero-alloc overload: writes <c>mesh.BoneCount</c> skin matrices into
    /// <paramref name="skinOut"/>. Hot-path callers (per-frame, per-actor) should
    /// reuse one scratch buffer across the entire actor list.</summary>
    public static void ComputeSkinMatrices(AspMesh mesh, PrsAnimation anim, float timeSec, Span<Matrix4x4> skinOut)
    {
        var bc = mesh.BoneCount;
        if (bc == 0) return;
        if (skinOut.Length < bc)
            throw new ArgumentException($"skinOut length {skinOut.Length} < BoneCount {bc}", nameof(skinOut));
        if (mesh.InverseBindMatrices.Length != bc)
            throw new InvalidDataException("AspMesh InverseBindMatrices length does not match BoneCount");

        var map = GetBoneMap(mesh, anim);
        const int MaxStack = 128;
        Span<Quaternion> rotBuf   = bc <= MaxStack ? stackalloc Quaternion[MaxStack] : new Quaternion[bc];
        Span<Vector3>    posBuf   = bc <= MaxStack ? stackalloc Vector3[MaxStack]    : new Vector3[bc];
        Span<Matrix4x4>  worldBuf = bc <= MaxStack ? stackalloc Matrix4x4[MaxStack]  : new Matrix4x4[bc];
        var localRot  = rotBuf.Slice(0, bc);
        var localPos  = posBuf.Slice(0, bc);
        var worldAnim = worldBuf.Slice(0, bc);

        for (var i = 0; i < bc; i++)
        {
            localRot[i] = mesh.BindPose[i].Rotation;
            localPos[i] = mesh.BindPose[i].Translation;
        }
        for (var i = 0; i < bc; i++)
        {
            var prs = map[i]; if (prs < 0) continue;
            var keys = anim.BoneKeys[prs]; if (keys is null) continue;
            if (keys.RotKeys.Count > 0) localRot[i] = SampleRotation(keys, anim.AnimLength, timeSec);
            if (keys.PosKeys.Count > 0) localPos[i] = SamplePosition(keys, anim.AnimLength, timeSec);
        }
        for (var i = 0; i < bc; i++)
        {
            var localAnim = Matrix4x4.CreateFromQuaternion(localRot[i]) * Matrix4x4.CreateTranslation(localPos[i]);
            var p = mesh.BoneParents[i];
            if (p < 0)
                worldAnim[i] = localAnim;
            else
            {
                if (p >= i)
                    throw new InvalidDataException($"ASP bone {i} parent {p} is not parent-first; cannot compose skin");
                worldAnim[i] = localAnim * worldAnim[p];
            }
        }
        for (var i = 0; i < bc; i++)
            skinOut[i] = mesh.InverseBindMatrices[i] * worldAnim[i];
    }

    /// <summary>Compose the per-bone animated world matrices (mesh-local) for the given
    /// clip at <paramref name="timeSec"/>. Returns one matrix per ASP bone: the bone's
    /// animated parent-space local transform walked up the hierarchy. Used by weapon/
    /// shield attach logic that needs the bone's pose directly (skin matrices, which
    /// pre-multiply an inverse bind, don't carry that.) Same complexity as
    /// <see cref="ComputeSkinMatrices(AspMesh,PrsAnimation,float)"/>; we walk the
    /// hierarchy once here too.</summary>
    public static Matrix4x4[] ComputeAnimatedBoneWorlds(AspMesh mesh, PrsAnimation? anim, float timeSec)
    {
        if (mesh.BoneCount == 0) return Array.Empty<Matrix4x4>();
        var world = new Matrix4x4[mesh.BoneCount];
        ComputeAnimatedBoneWorlds(mesh, anim, timeSec, world);
        return world;
    }

    /// <summary>Zero-alloc overload of <see cref="ComputeAnimatedBoneWorlds(AspMesh,PrsAnimation?,float)"/>.</summary>
    public static void ComputeAnimatedBoneWorlds(AspMesh mesh, PrsAnimation? anim, float timeSec, Span<Matrix4x4> worldOut)
    {
        var bc = mesh.BoneCount;
        if (bc == 0) return;
        if (worldOut.Length < bc)
            throw new ArgumentException($"worldOut length {worldOut.Length} < BoneCount {bc}", nameof(worldOut));

        const int MaxStack = 128;
        Span<Quaternion> rotBuf = bc <= MaxStack ? stackalloc Quaternion[MaxStack] : new Quaternion[bc];
        Span<Vector3>    posBuf = bc <= MaxStack ? stackalloc Vector3[MaxStack]    : new Vector3[bc];
        var localRot = rotBuf.Slice(0, bc);
        var localPos = posBuf.Slice(0, bc);
        for (var i = 0; i < bc; i++)
        {
            localRot[i] = mesh.BindPose[i].Rotation;
            localPos[i] = mesh.BindPose[i].Translation;
        }
        if (anim is not null)
        {
            var map = GetBoneMap(mesh, anim);
            for (var i = 0; i < bc; i++)
            {
                var prs = map[i]; if (prs < 0) continue;
                var keys = anim.BoneKeys[prs]; if (keys is null) continue;
                if (keys.RotKeys.Count > 0) localRot[i] = SampleRotation(keys, anim.AnimLength, timeSec);
                if (keys.PosKeys.Count > 0) localPos[i] = SamplePosition(keys, anim.AnimLength, timeSec);
            }
        }
        for (var i = 0; i < bc; i++)
        {
            var local = Matrix4x4.CreateFromQuaternion(localRot[i]) * Matrix4x4.CreateTranslation(localPos[i]);
            var p = mesh.BoneParents[i];
            worldOut[i] = p < 0 ? local : local * worldOut[p];
        }
    }

    /// <summary>Pose every corner in <paramref name="mesh"/> using the given skin matrices.
    /// Output is parallel to <see cref="AspMesh.Corners"/>; static meshes (no WCRN) fall
    /// back to the unposed vertex positions.</summary>
    public static Vector3[] SkinCorners(AspMesh mesh, Matrix4x4[] skinMatrices)
    {
        var outPositions = new Vector3[mesh.Corners.Length];
        if (!mesh.HasSkin || skinMatrices.Length == 0)
        {
            for (var c = 0; c < mesh.Corners.Length; c++)
                outPositions[c] = mesh.Positions[mesh.Corners[c].VertexIndex];
            return outPositions;
        }
        for (var c = 0; c < mesh.Corners.Length; c++)
        {
            var src = mesh.Positions[mesh.Corners[c].VertexIndex];
            var w   = mesh.SkinWeights[c];
            var b   = mesh.SkinBones[c];
            // Each active (bone, weight) pair contributes a transformed copy of the rest
            // position. For a good clip the weights sum to 1, so we don't renormalise —
            // if they don't, that's an asset bug we'd rather see than paper over.
            var acc = Vector3.Zero;
            if (w.X > 0) acc += w.X * Vector3.Transform(src, skinMatrices[ b        & 0xFF]);
            if (w.Y > 0) acc += w.Y * Vector3.Transform(src, skinMatrices[(b >> 8)  & 0xFF]);
            if (w.Z > 0) acc += w.Z * Vector3.Transform(src, skinMatrices[(b >> 16) & 0xFF]);
            if (w.W > 0) acc += w.W * Vector3.Transform(src, skinMatrices[(b >> 24) & 0xFF]);
            outPositions[c] = acc;
        }
        return outPositions;
    }

    /// <summary>Sample one bone's rotation/translation from a clip at the given time.
    /// Returns null components for tracks the bone doesn't have keys for; the caller is
    /// expected to substitute the bind pose. Useful for diagnostic/fuzz tooling that
    /// needs the per-bone sample without driving a full mesh skin.</summary>
    public static (Quaternion? Rot, Vector3? Trans) EvaluateBone(PrsAnimation anim, int boneIdx, float timeSec)
    {
        if ((uint)boneIdx >= (uint)anim.BoneKeys.Count) return (null, null);
        var keys = anim.BoneKeys[boneIdx];
        if (keys is null) return (null, null);
        Quaternion? rot = keys.RotKeys.Count > 0 ? SampleRotation(keys, anim.AnimLength, timeSec) : null;
        Vector3?    pos = keys.PosKeys.Count > 0 ? SamplePosition(keys, anim.AnimLength, timeSec) : null;
        return (rot, pos);
    }

    // Sampling: PRS keys store Time in 0..1 normalised to the clip length. If the caller's
    // requested time is past the clip we clamp — DS1 clips are one-shot and the runtime
    // loops them externally by wrapping timeSec before calling in. If there's only one key
    // the interpolation collapses to returning that key's value.

    private static Quaternion SampleRotation(PrsAnimation.KeyList keys, float animLength, float timeSec)
    {
        var rot = keys.RotKeys;
        if (rot.Count == 1) return rot[0].Rotation;
        var norm = animLength > 0f ? Math.Clamp(timeSec / animLength, 0f, 1f) : 0f;
        var (lo, hi, t) = FindBracket(rot, norm, k => k.Time);
        // Quaternion.Slerp normalises internally, so we don't worry about whether the
        // authoring tool wrote unit quats.
        return Quaternion.Slerp(rot[lo].Rotation, rot[hi].Rotation, t);
    }

    private static Vector3 SamplePosition(PrsAnimation.KeyList keys, float animLength, float timeSec)
    {
        var pos = keys.PosKeys;
        if (pos.Count == 1) return pos[0].Position;
        var norm = animLength > 0f ? Math.Clamp(timeSec / animLength, 0f, 1f) : 0f;
        var (lo, hi, t) = FindBracket(pos, norm, k => k.Time);
        return Vector3.Lerp(pos[lo].Position, pos[hi].Position, t);
    }

    private static (int lo, int hi, float t) FindBracket<T>(IReadOnlyList<T> keys, float norm, Func<T, float> timeOf)
    {
        // Linear scan — DS1 clips are short (typically <40 keys per bone), so a binary
        // search isn't worth the complexity. Return (lo, hi, blend) suitable for a
        // lerp/slerp that treats norm ≤ keys[0].Time as "clamp to first" and
        // norm ≥ keys[last].Time as "clamp to last".
        if (norm <= timeOf(keys[0])) return (0, 0, 0f);
        for (var i = 1; i < keys.Count; i++)
        {
            var t0 = timeOf(keys[i - 1]);
            var t1 = timeOf(keys[i]);
            if (norm <= t1)
            {
                var dt = t1 - t0;
                var u = dt > 0f ? (norm - t0) / dt : 0f;
                return (i - 1, i, u);
            }
        }
        var last = keys.Count - 1;
        return (last, last, 0f);
    }
}
