using SiegeFX.Core.Skrit;

namespace SiegeFX.Core.Actors;

/// <summary>Single-region broadcast channel for DS1's <c>PostWorldMessage</c> calls.
/// Skrits post messages like <c>WE_ANIM_SFX</c>, <c>WE_ANIM_LOOPED</c>, <c>WE_DAMAGED</c>
/// — the engine routes them to the target actor's trigger matrix or skrit event handler.
///
/// This phase implements the routing spine; the trigger-matrix language
/// (<c>[template_triggers] { condition* = receive_world_message(...); action* = ...; }</c>)
/// stays deferred. What runs today: per-actor skrits register their scid, post messages
/// through the bridge extern, and the bus dispatches <c>target.Skrit.Dispatch(name, args)</c>
/// on drain. Broadcast (goid 0) fans out to all registered actors.
///
/// Messages queue rather than deliver synchronously so a handler can post in turn without
/// re-entering the dispatch stack. Drain between ticks.</summary>
public sealed class WorldMessageBus
{
    /// <summary>Carries the five classic DS1 PostWorldMessage args: name, sender, target,
    /// and two longs whose meaning is message-specific (usually a req-block id and flags).
    /// Reshaped into a <see cref="SkritValue"/> array at dispatch time.</summary>
    public readonly record struct Message(
        string Name,
        uint FromScid,
        uint ToScid,
        long Arg1,
        long Arg2);

    readonly Dictionary<uint, SkritInstance> _byScid = new();
    readonly Queue<Message> _queue = new();
    // Scratch buffer reused across deliveries — broadcast to 181 actors with one post
    // would otherwise allocate 181× SkritValue[4]. Bus is single-threaded per region by
    // contract (Deliver runs on the 20 Hz tick), so reuse is safe.
    readonly SkritValue[] _argScratch = new SkritValue[4];

    public IReadOnlyDictionary<uint, SkritInstance> Registered => _byScid;

    /// <summary>Total messages posted since this bus was created — includes those dropped
    /// because the target scid wasn't registered. Useful for smoke tests that just want
    /// to know the skrit bytecode is hitting the extern at all.</summary>
    public int PostedCount { get; private set; }

    /// <summary>Messages that drained through without a delivery target. Usually benign
    /// (skrits broadcast to self during boot and the scid isn't registered yet, or a
    /// sibling region-message that no local actor cares about).</summary>
    public int UndeliveredCount { get; private set; }

    public void Register(uint scid, SkritInstance instance) => _byScid[scid] = instance;

    public void Unregister(uint scid) => _byScid.Remove(scid);

    public void Post(string name, uint fromScid, uint toScid, long arg1, long arg2)
    {
        _queue.Enqueue(new Message(name, fromScid, toScid, arg1, arg2));
        PostedCount++;
    }

    /// <summary>Drain the queue, dispatching each message. <c>toScid == 0</c> broadcasts.
    /// Skrits see the message as an event named by <see cref="Message.Name"/> (e.g.
    /// <c>WE_ANIM_LOOPED</c>) with args <c>(fromScid, toScid, arg1, arg2)</c>.</summary>
    public int Deliver()
    {
        int delivered = 0;
        while (_queue.Count > 0)
        {
            var m = _queue.Dequeue();
            // SkritVm.Run copies args into its locals frame before bytecode executes, so
            // the buffer is free to reuse across dispatches — including a broadcast fan-out
            // where every registered actor sees the same payload.
            _argScratch[0] = SkritValue.FromInt(m.FromScid);
            _argScratch[1] = SkritValue.FromInt(m.ToScid);
            _argScratch[2] = SkritValue.FromInt(m.Arg1);
            _argScratch[3] = SkritValue.FromInt(m.Arg2);
            if (m.ToScid == 0)
            {
                foreach (var inst in _byScid.Values)
                    if (inst.Dispatch(m.Name, _argScratch)) delivered++;
            }
            else if (_byScid.TryGetValue(m.ToScid, out var target))
            {
                if (target.Dispatch(m.Name, _argScratch)) delivered++;
                else UndeliveredCount++;
            }
            else UndeliveredCount++;
        }
        return delivered;
    }
}
