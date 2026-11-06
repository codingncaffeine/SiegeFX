using System.Numerics;

namespace SiegeFX.Core.Assets;

/// <summary>SC-PROP-REST-POSE — the pose DS1 draws a static, skinned prop in.
/// A GO with a chore dictionary is drawn through its animation, not at the
/// mesh bind: before anything plays, the prop sits at the first frame of the
/// clip that would move it out of its rest state (chore_default, else
/// chore_open — the pose just before it opens). When that clip keys the root
/// bone away from the bind, the whole mesh moves with it, and the shipped
/// placements are authored against the posed mesh. See
/// <see cref="AnimationRuntime.TryRootRestOffset"/>.</summary>
public static class PropRestPose
{
    static readonly string[] RestChores = { "chore_default", "chore_open", "chore_close" };

    /// <summary>PRS basenames (with .prs) to try for the rest clip, best first.
    /// Item/prop chores author no stances, so the file is
    /// <c>{chore_prefix}_{anim}.prs</c> (<c>a_i_glb_switch-06-close.prs</c>);
    /// the stance-composed <c>{chore_prefix}0_{anim}.prs</c> form follows as
    /// a fallback.</summary>
    public static IReadOnlyList<string> RestClipCandidates(TemplateStore store, Template template)
    {
        var list = new List<string>();
        var prefix = store.GetAttribute(template, "body", "chore_dictionary", "chore_prefix")?.Trim().Trim('"');
        if (string.IsNullOrEmpty(prefix)) return list;
        var dict = store.GetSection(template, "body", "chore_dictionary");
        if (dict is null) return list;
        foreach (var choreName in RestChores)
        {
            var chore = TemplateStore.FindChild(dict, choreName);
            if (chore is null) continue;
            var anim = FirstAnimFile(chore);
            if (string.IsNullOrEmpty(anim)) continue;
            list.Add($"{prefix}_{anim}.prs");
            list.Add($"{prefix}0_{anim}.prs");
            return list;
        }
        return list;
    }

    /// <summary>First <c>[anim_files]</c> entry of a chore, in either the
    /// nested block form or the <c>anim_files:00 = x;</c> shorthand the item
    /// templates use.</summary>
    static string? FirstAnimFile(GasNode chore)
    {
        var files = TemplateStore.FindChild(chore, "anim_files");
        if (files is not null && files.Attributes.Count > 0)
            return files.Attributes[0].Value?.Trim().Trim('"');
        foreach (var attr in chore.Attributes)
            if (attr.Name.StartsWith("anim_files:", StringComparison.OrdinalIgnoreCase))
                return attr.Value?.Trim().Trim('"');
        return null;
    }

    /// <summary>The whole-mesh offset (mesh space, applied before the
    /// placement) of <paramref name="template"/>'s rest clip on
    /// <paramref name="mesh"/>. False when the template authors no clip, none
    /// loads, or the clip leaves the root at its bind.</summary>
    public static bool TryRestOffset(TemplateStore store, Template template, AspMesh mesh,
        Func<string, PrsAnimation?> loadClip, out Matrix4x4 offset)
    {
        offset = Matrix4x4.Identity;
        if (mesh.BoneCount == 0) return false;
        foreach (var name in RestClipCandidates(store, template))
        {
            var clip = loadClip(name);
            if (clip is null) continue;
            return AnimationRuntime.TryRootRestOffset(mesh, clip, out offset);
        }
        return false;
    }
}
