using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Nav;

/// <summary>
/// Per-actor traversal policy: which <see cref="SnoModel.FloorKind"/> values are passable
/// and how much extra they cost relative to a normal floor tile. Plumbed through
/// <see cref="NavPathfinder.TryFindPath"/> so the same nav mesh serves both a chicken
/// (land-only) and a future amphibious enemy without rebuilding.
///
/// A multiplier of <see cref="float.PositiveInfinity"/> means impassable — the pathfinder
/// rejects start/goal triangles of that kind and never expands into them. <c>1.0</c>
/// matches the historical Floor-only cost, <c>>1.0</c> makes the kind a last-resort
/// detour. Sub-1 multipliers are not supported (would break the centroid-distance A*
/// heuristic's admissibility).
/// </summary>
public sealed class NavTraversal
{
    /// <summary>Cost multiplier on water tiles. Default <see cref="float.PositiveInfinity"/>
    /// keeps water fully blocking, which matches DS1's stock NPC behavior — almost no
    /// shipped actor wades or swims. Phase 21+ (LoA underwater) will hand this a finite
    /// value for the appropriate templates.</summary>
    public float WaterCostMultiplier { get; init; } = float.PositiveInfinity;

    /// <summary>Phase 24-NAV-LOGICAL-FLAGS — which actor-class gate
    /// this policy passes. DS1's logical_flags.gas tags terrain
    /// per-(snode,lnode) with <c>lf_human_player</c> /
    /// <c>lf_computer_player</c> so authors can keep NPCs out of
    /// player-only zones and vice versa. Defaults to
    /// <see cref="LogicalFlagsStore.ActorClass.Neutral"/> (no gate
    /// check) so existing call sites that didn't pass an ActorClass
    /// keep working — opt-in by setting this on the policy passed to
    /// the pathfinder.</summary>
    public LogicalFlagsStore.ActorClass Actor { get; init; } = LogicalFlagsStore.ActorClass.Neutral;

    /// <summary>True when an actor with this policy can stand on / pass through the kind.</summary>
    public bool CanEnter(SnoModel.FloorKind kind) => float.IsFinite(GetMultiplier(kind));

    /// <summary>Cost multiplier for entering a triangle of this kind. <see cref="float.PositiveInfinity"/>
    /// means impassable.</summary>
    public float GetMultiplier(SnoModel.FloorKind kind) => kind switch
    {
        SnoModel.FloorKind.Floor => 1f,
        SnoModel.FloorKind.Water => WaterCostMultiplier,
        // Ignored never reaches the mesh (filtered at build), but if a future change
        // ever lets it through we want to refuse it loudly rather than walk it.
        _ => float.PositiveInfinity,
    };

    /// <summary>Default policy: walk on floor, refuse water. Stateless singleton.</summary>
    public static NavTraversal LandOnly { get; } = new();

    /// <summary>Convenience policy for amphibious actors: water costs 4× floor. Picked
    /// so a 5-tile shortcut through a pond beats a 25-tile detour around it but a
    /// 30-tile detour beats a 10-tile swim — close to "swim only when there's no
    /// other way".</summary>
    public static NavTraversal Amphibious { get; } = new() { WaterCostMultiplier = 4f };

    /// <summary>Player policy: land-only + lf_human_player gate. The
    /// pathfinder rejects triangles tagged computer-only by the
    /// region's logical_flags.gas.</summary>
    public static NavTraversal Player { get; } = new() { Actor = LogicalFlagsStore.ActorClass.HumanPlayer };

    /// <summary>NPC / brain policy: land-only + lf_computer_player gate.
    /// Keeps NPCs out of human-only zones (towns interiors etc.).</summary>
    public static NavTraversal Computer { get; } = new() { Actor = LogicalFlagsStore.ActorClass.ComputerPlayer };
}
