using System;

namespace SiegeFX.Core.Skrit;

/// <summary>Runtime value type for the Skrit VM. A compact tagged union — Skrit's dynamic
/// types (int, float, bool, string, null, host object) fit in a single struct. <see cref="Tag"/>
/// discriminates which of the payload fields is live. Host-owned objects live in
/// <see cref="Object"/>; the VM treats them opaquely and routes member access through
/// <see cref="IHostBridge"/>.</summary>
public enum SkritValueTag : byte
{
    Null, Bool, Int, Float, String, Object,
}

public readonly struct SkritValue : IEquatable<SkritValue>
{
    public readonly SkritValueTag Tag;
    readonly long _int;       // Int, Bool (0/1)
    readonly double _float;   // Float
    readonly object? _ref;    // String, Object

    SkritValue(SkritValueTag tag, long i, double f, object? r)
    { Tag = tag; _int = i; _float = f; _ref = r; }

    public static readonly SkritValue Null = new(SkritValueTag.Null, 0, 0, null);
    public static readonly SkritValue True = new(SkritValueTag.Bool, 1, 0, null);
    public static readonly SkritValue False = new(SkritValueTag.Bool, 0, 0, null);

    public static SkritValue FromBool(bool b) => b ? True : False;
    public static SkritValue FromInt(long i) => new(SkritValueTag.Int, i, 0, null);
    public static SkritValue FromFloat(double f) => new(SkritValueTag.Float, 0, f, null);
    public static SkritValue FromString(string s) => new(SkritValueTag.String, 0, 0, s);
    public static SkritValue FromObject(object? o) => o is null ? Null : new(SkritValueTag.Object, 0, 0, o);

    public bool AsBool => Tag switch
    {
        SkritValueTag.Bool => _int != 0,
        SkritValueTag.Int => _int != 0,
        SkritValueTag.Float => _float != 0,
        SkritValueTag.Null => false,
        SkritValueTag.String => !string.IsNullOrEmpty((string?)_ref),
        SkritValueTag.Object => _ref is not null,
        _ => false,
    };

    public long AsInt => Tag switch
    {
        SkritValueTag.Int => _int,
        SkritValueTag.Bool => _int,
        SkritValueTag.Float => (long)_float,
        _ => 0,
    };

    public double AsFloat => Tag switch
    {
        SkritValueTag.Float => _float,
        SkritValueTag.Int => _int,
        SkritValueTag.Bool => _int,
        _ => 0.0,
    };

    public string AsString => Tag switch
    {
        SkritValueTag.String => (string)_ref!,
        SkritValueTag.Null => "",
        _ => ToString() ?? "",
    };

    public object? AsObject => _ref;

    public override string ToString() => Tag switch
    {
        SkritValueTag.Null => "null",
        SkritValueTag.Bool => _int != 0 ? "true" : "false",
        SkritValueTag.Int => _int.ToString(System.Globalization.CultureInfo.InvariantCulture),
        SkritValueTag.Float => _float.ToString(System.Globalization.CultureInfo.InvariantCulture),
        SkritValueTag.String => (string)_ref!,
        SkritValueTag.Object => _ref?.ToString() ?? "null",
        _ => "?",
    };

    public bool Equals(SkritValue other)
    {
        if (Tag != other.Tag)
        {
            // Cross-numeric comparison: int == float if values match.
            if ((Tag == SkritValueTag.Int && other.Tag == SkritValueTag.Float)
                || (Tag == SkritValueTag.Float && other.Tag == SkritValueTag.Int))
                return AsFloat == other.AsFloat;
            return false;
        }
        return Tag switch
        {
            SkritValueTag.Null => true,
            SkritValueTag.Bool or SkritValueTag.Int => _int == other._int,
            SkritValueTag.Float => _float == other._float,
            SkritValueTag.String => string.Equals((string?)_ref, (string?)other._ref, StringComparison.Ordinal),
            SkritValueTag.Object => ReferenceEquals(_ref, other._ref),
            _ => false,
        };
    }

    public override bool Equals(object? obj) => obj is SkritValue v && Equals(v);
    public override int GetHashCode() => HashCode.Combine((byte)Tag, _int, _float, _ref);
}
