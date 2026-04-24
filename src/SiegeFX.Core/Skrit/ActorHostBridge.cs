using System;
using System.Collections.Generic;

namespace SiegeFX.Core.Skrit;

/// <summary>Concrete <see cref="IHostBridge"/> that wires up enough of DS1's host surface
/// to run real shipped skrits in isolation (no actor world yet). Stubs Math, Report,
/// GpConsole, StringTool, PostWorldMessage, and a small synthetic WorldState. Unknown
/// externs return null silently so a partially-mapped skrit still runs end-to-end.
///
/// Phase 9 will plug a real actor reference into this class so <c>owner.blender.*</c>
/// calls can drive actual animation. For 8d the bridge records blender activity for
/// inspection (<see cref="BlenderLog"/>) and the viewer reads it to pick which clip
/// to play.</summary>
public sealed class ActorHostBridge : IHostBridge
{
    public SkritInstance? Instance { get; set; }

    /// <summary>Scid of the actor this bridge belongs to. Used as the sender/goid for any
    /// <c>PostWorldMessage(...)</c> calls whose script says <c>owner.goid</c>. 0 means
    /// the bridge is running outside an actor (e.g. the standalone skrit-tick harness).</summary>
    public uint OwnerScid { get; set; }

    /// <summary>Optional world-message bus. When set, <c>PostWorldMessage</c> calls from
    /// the skrit are routed through it instead of silently discarded. Remains null in
    /// the single-skrit viewer mode so that path keeps its old stub behavior.</summary>
    public SiegeFX.Core.Actors.WorldMessageBus? MessageBus { get; set; }

    readonly Dictionary<string, SkritValue> _externs = new(StringComparer.OrdinalIgnoreCase);
    readonly Random _rng;

    public List<string> BlenderLog { get; } = new();

    /// <summary>Most recent anim index the script asked to blend in via
    /// <c>owner.blender.AddAnimToBlendGroup(idx, weight)</c>. -1 before any request.</summary>
    public int CurrentAnimIndex { get; private set; } = -1;

    /// <summary>Number of sub-anims the blender reports — set by the host to match the
    /// real clip list so <c>Math.RandomInt(0, num-1)</c> picks a valid index.</summary>
    public int NumSubAnims { get; set; } = 1;

    public ActorHostBridge(int? rngSeed = null)
    {
        _rng = rngSeed is int s ? new Random(s) : new Random();

        // Seed a tiny WorldState enum so shipped auto-skrits ("auto/jipper.skrit" etc.)
        // don't stall on LoadExtern miss. Values are arbitrary placeholders; a real
        // world-state driver in Phase 9 overwrites them.
        foreach (var k in new[]
        {
            "WS_INTRO", "WS_MAIN_MENU", "WS_SP_INGAME", "WS_MP_INGAME",
            "WS_MP_LAN_GAME", "WS_MP_STAGING_AREA_SERVER", "WS_MP_PROVIDER_SELECT",
            "WS_GAME_ENDED",
        })
            _externs[k] = SkritValue.FromString(k);
    }

    public void SetExternValue(string name, SkritValue value) => _externs[name] = value;
    public bool TryGetExternValue(string name, out SkritValue value) => _externs.TryGetValue(name, out value);

    // ---- IHostBridge ----

    public SkritValue GetExtern(string name)
    {
        if (_externs.TryGetValue(name, out var v)) return v;
        // ANIMEVENT_* bitmask constants — synthesise single-bit masks on demand so
        // AnimEventBitTest(events, ANIMEVENT_x) returns coherent truthy/falsy values.
        if (name.StartsWith("ANIMEVENT_", StringComparison.OrdinalIgnoreCase))
        {
            int bit = AnimEventBit(name);
            _externs[name] = SkritValue.FromInt(1L << bit);
            return _externs[name];
        }
        // Math / Report / GpConsole / StringTool / WorldState — materialise a tag string so
        // the subsequent CallMember / GetMember can dispatch without relying on null-receiver
        // fallthroughs.
        foreach (var root in KnownRoots)
            if (string.Equals(name, root, StringComparison.OrdinalIgnoreCase))
            {
                var tag = SkritValue.FromString("<" + root.ToLowerInvariant() + ">");
                _externs[name] = tag;
                return tag;
            }
        return SkritValue.Null;
    }

    public void SetExtern(string name, SkritValue value) => _externs[name] = value;

    public SkritValue CallExtern(string name, SkritValue[] args)
    {
        switch (name)
        {
            case "AnimEventBitTest":
                if (args.Length < 2) return SkritValue.False;
                return SkritValue.FromBool((args[0].AsInt & args[1].AsInt) != 0);
            case "PostWorldMessage":
                // Arg layout per DS1: (messageName, fromGoid, toGoid, arg1, arg2). The
                // skrit typically passes `owner.goid` for from/to when self-messaging;
                // our owner.goid returns the string "actor" and the string survives to
                // here as args[1]/args[2]. Recover the real scid from OwnerScid when the
                // value isn't an int (== the canonical self-send case).
                if (MessageBus is not null && args.Length >= 1)
                {
                    var msgName = args[0].AsString ?? string.Empty;
                    uint from = ArgAsScid(args, 1, OwnerScid);
                    uint to   = ArgAsScid(args, 2, OwnerScid);
                    long a1   = args.Length > 3 ? args[3].AsInt : 0;
                    long a2   = args.Length > 4 ? args[4].AsInt : 0;
                    MessageBus.Post(msgName, from, to, a1, a2);
                }
                return SkritValue.Null;
            case "NULL":
                return SkritValue.Null;
        }
        return SkritValue.Null;
    }

    public SkritValue GetMember(SkritValue receiver, string member)
    {
        // `owner.blender.<something>` — the intermediate hop returns a tag value so the
        // next CallMember can tell it's hitting the blender surface.
        if (string.Equals(member, "blender", StringComparison.OrdinalIgnoreCase)
            || string.Equals(member, "Blender", StringComparison.OrdinalIgnoreCase))
            return SkritValue.FromString("<blender>");
        if (string.Equals(member, "Name", StringComparison.OrdinalIgnoreCase)
            || string.Equals(member, "goid", StringComparison.OrdinalIgnoreCase))
            return SkritValue.FromString("actor");
        return SkritValue.Null;
    }

    /// <summary>Map top-level receiver names to internal dispatch tags. Skrits write
    /// <c>Math.RandomInt(...)</c> as <c>LoadExtern "Math"; CallMember "RandomInt"</c> —
    /// the extern returns the tag string so CallMember can route without relying on a
    /// null-receiver fallthrough (which would silently answer RandomInt on *any* undefined
    /// identifier and mask binder gaps).</summary>
    static readonly string[] KnownRoots = { "Math", "Report", "GpConsole", "StringTool", "WorldState" };

    public void SetMember(SkritValue receiver, string member, SkritValue value) { }

    public SkritValue CallMember(SkritValue receiver, string member, SkritValue[] args)
    {
        // `<math>.X(...)` — random helpers. Gated on the explicit tag so an undefined
        // identifier doesn't silently answer RandomInt and mask binder gaps.
        if (IsTag(receiver, "<math>"))
        {
            if (string.Equals(member, "RandomInt", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
            {
                long lo = args[0].AsInt, hi = args[1].AsInt;
                if (hi < lo) return SkritValue.FromInt(lo);
                return SkritValue.FromInt(lo + _rng.NextInt64(hi - lo + 1));
            }
            if (string.Equals(member, "RandomFloat", StringComparison.OrdinalIgnoreCase))
            {
                double lo = args.Length > 0 ? args[0].AsFloat : 0.0;
                double hi = args.Length > 1 ? args[1].AsFloat : 1.0;
                return SkritValue.FromFloat(lo + _rng.NextDouble() * (hi - lo));
            }
            return SkritValue.Null;
        }

        // `<blender>.X(...)` — capture blend operations.
        if (IsTag(receiver, "<blender>"))
            return BlenderCall(member, args);

        // `owner.UpdateBlender(dt)` and friends — owner is null-extern so receiver is Null.
        if (string.Equals(member, "UpdateBlender", StringComparison.OrdinalIgnoreCase))
            return SkritValue.FromInt(0); // zero event bits — shipped anim handlers loop cleanly

        if (string.Equals(member, "GetNumSubAnims", StringComparison.OrdinalIgnoreCase))
            return SkritValue.FromInt(NumSubAnims);

        return SkritValue.Null;
    }

    static bool IsTag(SkritValue v, string tag)
        => v.Tag == SkritValueTag.String && string.Equals(v.AsString, tag, StringComparison.Ordinal);

    // `owner.goid` reads as the string "actor" via GetMember; that value flows into
    // PostWorldMessage's from/to slots. Treat any non-int arg as "use the owner's scid"
    // so the self-send case routes correctly. Real integer goids (from targeting code
    // that already resolved the receiver) pass through unchanged.
    static uint ArgAsScid(SkritValue[] args, int i, uint fallback)
    {
        if (i >= args.Length) return fallback;
        var v = args[i];
        if (v.Tag == SkritValueTag.Int) return unchecked((uint)v.AsInt);
        return fallback;
    }

    public void SetState(string stateName) => Instance?.RequestSetState(stateName);

    // ---- internals ----

    SkritValue BlenderCall(string member, SkritValue[] args)
    {
        BlenderLog.Add($"{member}({string.Join(", ", args)})");
        switch (member)
        {
            case "GetNumSubAnims": return SkritValue.FromInt(NumSubAnims);
            case "OpenBlendGroup": return SkritValue.FromInt(0);
            case "AddAnimToBlendGroup":
                if (args.Length >= 1) CurrentAnimIndex = (int)args[0].AsInt;
                return SkritValue.Null;
            case "CloseBlendGroup":
            case "SetBlendGroupWeight":
            case "ResetTimeWarp":
                return SkritValue.Null;
        }
        return SkritValue.Null;
    }

    static int AnimEventBit(string name) => name.ToUpperInvariant() switch
    {
        "ANIMEVENT_FINISH"          => 0,
        "ANIMEVENT_SFX_1"           => 1,
        "ANIMEVENT_SFX_2"           => 2,
        "ANIMEVENT_SFX_3"           => 3,
        "ANIMEVENT_SFX_4"           => 4,
        "ANIMEVENT_HIDE_MESH"       => 5,
        "ANIMEVENT_SHOW_MESH"       => 6,
        "ANIMEVENT_LEFT_FOOT_DOWN"  => 7,
        "ANIMEVENT_RIGHT_FOOT_DOWN" => 8,
        "ANIMEVENT_WEAPON_FIRE"     => 9,
        _                           => 31,
    };
}
