using System.Runtime.InteropServices;

namespace SiegeFX.Core.Tank;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public readonly struct FourCC : IEquatable<FourCC>
{
    public readonly byte C0;
    public readonly byte C1;
    public readonly byte C2;
    public readonly byte C3;

    public FourCC(byte c0, byte c1, byte c2, byte c3)
    {
        C0 = c0; C1 = c1; C2 = c2; C3 = c3;
    }

    public FourCC(char c0, char c1, char c2, char c3)
        : this((byte)c0, (byte)c1, (byte)c2, (byte)c3) { }

    public FourCC(string ascii)
    {
        if (ascii.Length != 4) throw new ArgumentException("FourCC must be 4 chars", nameof(ascii));
        C0 = (byte)ascii[0]; C1 = (byte)ascii[1]; C2 = (byte)ascii[2]; C3 = (byte)ascii[3];
    }

    public override string ToString() => new(new[] { (char)C0, (char)C1, (char)C2, (char)C3 });

    public bool Equals(FourCC other) => C0 == other.C0 && C1 == other.C1 && C2 == other.C2 && C3 == other.C3;
    public override bool Equals(object? obj) => obj is FourCC other && Equals(other);
    public override int GetHashCode() => (C0 << 24) | (C1 << 16) | (C2 << 8) | C3;
    public static bool operator ==(FourCC a, FourCC b) => a.Equals(b);
    public static bool operator !=(FourCC a, FourCC b) => !a.Equals(b);
}
