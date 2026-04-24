using System;
using SiegeFX.Core.Skrit;

namespace SiegeFX.Tools;

/// <summary>Debug <see cref="IHostBridge"/> that prints every host-surface interaction
/// to stdout. Used by <c>siegefx skrit run</c> so you can see what the VM is asking
/// for without wiring the real actor bridge.</summary>
internal sealed class TracingHostBridge : IHostBridge
{
    public SkritValue GetExtern(string name)
    {
        Console.WriteLine($"  [host] GetExtern {name}");
        return SkritValue.Null;
    }
    public void SetExtern(string name, SkritValue value)
        => Console.WriteLine($"  [host] SetExtern {name} = {value}");
    public SkritValue CallExtern(string name, SkritValue[] args)
    {
        Console.WriteLine($"  [host] CallExtern {name}({string.Join(", ", args)})");
        return SkritValue.Null;
    }
    public SkritValue GetMember(SkritValue receiver, string member)
    {
        Console.WriteLine($"  [host] GetMember {receiver}.{member}");
        return SkritValue.Null;
    }
    public void SetMember(SkritValue receiver, string member, SkritValue value)
        => Console.WriteLine($"  [host] SetMember {receiver}.{member} = {value}");
    public SkritValue CallMember(SkritValue receiver, string member, SkritValue[] args)
    {
        Console.WriteLine($"  [host] CallMember {receiver}.{member}({string.Join(", ", args)})");
        return SkritValue.Null;
    }
    public void SetState(string stateName)
        => Console.WriteLine($"  [host] SetState {stateName}");
}
