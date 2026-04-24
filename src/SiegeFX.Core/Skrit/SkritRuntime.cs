using System.Collections.Generic;

namespace SiegeFX.Core.Skrit;

/// <summary>Top-level owner of all live <see cref="SkritInstance"/>s. Phase 9 spawns
/// instances per actor as regions load; the render loop drives the whole bag with
/// <see cref="Tick"/> once per logic frame. Thin on purpose — lifecycle policy belongs
/// to the actor system, not here.</summary>
public sealed class SkritRuntime
{
    readonly List<SkritInstance> _instances = new();
    public IReadOnlyList<SkritInstance> Instances => _instances;

    public SkritInstance Add(SkritInstance instance)
    {
        _instances.Add(instance);
        return instance;
    }

    public void Remove(SkritInstance instance) => _instances.Remove(instance);

    /// <summary>Tick every live instance. Call from the logic loop, not the render loop
    /// — we want a fixed tick rate for determinism (20 Hz matches DS1's expectations for
    /// <c>frames</c>-unit scheduling).</summary>
    public void Tick(double dt)
    {
        for (int i = 0; i < _instances.Count; i++)
            _instances[i].Tick(dt);
    }

    public void DispatchAll(string eventName, params SkritValue[] args)
    {
        foreach (var inst in _instances) inst.Dispatch(eventName, args);
    }
}
